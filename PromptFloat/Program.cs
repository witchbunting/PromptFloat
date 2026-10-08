using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
namespace PromptFloat;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var startupWatch=Stopwatch.StartNew();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PromptFloat");var position=Array.IndexOf(args,"--data-dir");if(position>=0&&position+1<args.Length)root=Path.GetFullPath(args[position+1]);
        if(args.Contains("--fixture"))return Verification.Fixture(args);
        var verify=args.Contains("--verify");
        if(verify)root=Path.Combine(Path.GetTempPath(),"PromptFloat-verify-"+Guid.NewGuid().ToString("N"));
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
        using var mutex=new Mutex(true,@"Local\PromptFloat-"+identity,out var first);using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\PromptFloat-show-"+identity);
        if(!first){signal.Set();return 0;}
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Styles();Ui.Theme("浅色");Host? host=null;RegisteredWaitHandle? waiter=null;
        app.DispatcherUnhandledException+=(_,e)=>{
            try{Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"errors.log"),DateTimeOffset.Now.ToString("O")+" "+e.Exception.GetType().Name+"\n");}catch{}
            MessageBox.Show(e.Exception.Message,"PromptFloat",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;
        };
        app.Startup+=async(_,_)=>{
            try
            {
                host=new Host(new Store(root),!verify);
                if(verify){await Verification.Run(host,args);app.Shutdown(0);return;}
                waiter=ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>app.Dispatcher.BeginInvoke(()=>{host.Ball.Show();host.Ball.Reveal();}),null,-1,false);host.Start();
                if(args.Contains("--measure"))
                {
                    await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);var coldStartMs=startupWatch.Elapsed.TotalMilliseconds;
                    File.WriteAllText(Path.Combine(root,"measurement-ready.json"),System.Text.Json.JsonSerializer.Serialize(new {processId=Environment.ProcessId,managedStartMs=coldStartMs},Store.Json));
                    await Task.Delay(1500);using var own=Process.GetCurrentProcess();var cpuStart=own.TotalProcessorTime;var clock=Stopwatch.StartNew();await Task.Delay(60000);own.Refresh();
                    var cpuPercent=(own.TotalProcessorTime-cpuStart).TotalMilliseconds/clock.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
                    Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/idle-metrics.json",System.Text.Json.JsonSerializer.Serialize(new {coldStartMs,cpuPercent,selectionProbes=host.Selection.ProbeCount,autoSelectionActions=host.Store.Settings.AutoSelectionActions,workingSetMB=own.WorkingSet64/1048576d,sampleSeconds=clock.Elapsed.TotalSeconds,processors=Environment.ProcessorCount,templates=host.Store.Read().Prompts.Count,os=Environment.OSVersion.ToString()},Store.Json));app.Shutdown(0);
                }
            }
            catch(Exception e)
            {
                if(verify){Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/verification-error.txt",e.ToString());}
                else MessageBox.Show("无法启动："+e.Message+"\n现有数据保留在："+root,"PromptFloat",MessageBoxButton.OK,MessageBoxImage.Error);
                app.Shutdown(1);
            }
        };
        app.Exit+=(_,_)=>{waiter?.Unregister(null);host?.Dispose();};return app.Run();
    }
}
