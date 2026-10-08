using System.Text.Json;
using System.Windows.Threading;
namespace PromptFloat;

internal static class ResponsivenessVerification
{
    public static async Task Run(Host host,Action<bool,string,string> check,string output)
    {
        using var worker=new AutomationWorker();
        var first=await worker.RunAsync(()=> (Environment.CurrentManagedThreadId,Thread.CurrentThread.GetApartmentState()));
        var second=await worker.RunAsync(()=>Environment.CurrentManagedThreadId);
        check(first.Item1==second&&first.Item2==ApartmentState.MTA&&first.Item1!=Environment.CurrentManagedThreadId,
            "all provider work uses one non-UI MTA thread","");

        using var release=new ManualResetEventSlim();using var started=new ManualResetEventSlim();
        var watch=Stopwatch.StartNew();
        var blocked=worker.RunAsync(()=>{started.Set();release.Wait();return true;});
        for(var n=0;n<30&&!started.IsSet;n++)await Task.Delay(10);
        var ticks=0;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>ticks++;timer.Start();
        double timeoutMs;
        try
        {
            await Task.Delay(100);
            check(ticks>=2,"UI dispatcher remains responsive while provider is blocked",ticks+" ticks");
            var rejected=false;try{await worker.RunAsync(()=>true);}catch(TimeoutException){rejected=true;}
            check(rejected,"blocked provider cannot queue more work or threads","");
            var timedOut=false;try{await blocked;}catch(TimeoutException){timedOut=true;}
            timeoutMs=watch.Elapsed.TotalMilliseconds;
            check(timedOut&&timeoutMs<1500,"blocked provider has bounded 900ms caller timeout",timeoutMs.ToString("F1")+" ms");
        }
        finally {release.Set();timer.Stop();}
        await Task.Delay(80);
        check(await worker.RunAsync(()=>true),"provider worker recovers when blocked request returns","");

        using var hostRelease=new ManualResetEventSlim();using var hostStarted=new ManualResetEventSlim();
        var hostBlocked=host.Input.AutomationAsync(()=>{hostStarted.Set();hostRelease.Wait();return true;});
        for(var n=0;n<30&&!hostStarted.IsSet;n++)await Task.Delay(10);
        var ball=Native.Bounds(host.Ball);Native.GetCursorPos(out var previousCursor);var clickWatch=Stopwatch.StartNew();
        try
        {
            check(Native.TestClick(new WindowInteropHelper(host.Ball).Handle,ball.Left+ball.Width/2,ball.Top+ball.Height/2,false),
                "actual ball mouse click dispatches during blocked provider","");
            for(var n=0;n<15&&host.Surfaces.ActiveMenu?.IsOpen!=true;n++)await Task.Delay(20);
            var menuMs=clickWatch.Elapsed.TotalMilliseconds;
            check(host.Surfaces.ActiveMenu?.IsOpen==true&&menuMs<350,
                "ball click opens category menu without waiting for blocked provider",menuMs.ToString("F1")+" ms");
            Native.TestKey(0x1B);await Task.Delay(80);
            check(host.Surfaces.ActiveMenu?.IsOpen!=true,"Escape closes menu while provider stays blocked","");
            check(host.Input.Target==null,"blocked capture never reuses an old insertion target","");
            File.WriteAllText(Path.Combine(output,"responsiveness.json"),JsonSerializer.Serialize(new {
                providerTimeoutMs=timeoutMs,ballClickMenuMs=menuMs,uiHeartbeatTicks=ticks,
                workerThread=first.Item1,apartment=first.Item2.ToString(),realMouseClick=true
            },Store.Json));
        }
        finally{Native.TestRestoreCursor(previousCursor);hostRelease.Set();host.Surfaces.Close();try{await hostBlocked;}catch(TimeoutException){}}
        await Task.Delay(100);

        // Disposal must never join a stuck provider and stall program exit.
        var disposable=new AutomationWorker();using var shutdownRelease=new ManualResetEventSlim();
        var shutdownTask=disposable.RunAsync(()=>{shutdownRelease.Wait();return true;});
        var disposalWatch=Stopwatch.StartNew();disposable.Dispose();
        check(disposalWatch.ElapsedMilliseconds<100,"exit does not wait for a blocked UIA provider","");
        shutdownRelease.Set();try{await shutdownTask;}catch(TimeoutException){}
    }
}
