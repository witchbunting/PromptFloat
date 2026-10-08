namespace PromptFloat;

// UIA is synchronous COM. Keep every provider call and event subscription on
// one long-lived MTA thread, with one slot. A timeout cannot cancel COM, so the
// occupied slot stays occupied until the provider returns; the UI never joins it.
internal sealed class AutomationWorker : IDisposable
{
    private readonly object gate=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly Thread thread;
    private Action? pending;
    private bool busy,disposed;
    public AutomationWorker()
    {
        thread=new Thread(Loop) {IsBackground=true,Name="PromptFloat.UIAutomation"};
        thread.SetApartmentState(ApartmentState.MTA);thread.Start();
    }
    public Task<T> RunAsync<T>(Func<T> action)
    {
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(AutomationWorker));
            if(busy)throw new TimeoutException("目标软件暂未响应，请使用复制。");
            busy=true;
            pending=()=>{
                T result=default!;Exception? error=null;
                try{result=action();}catch(Exception e){error=e;}
                lock(gate)busy=false;
                if(error==null)completion.TrySetResult(result);else completion.TrySetException(error);
            };
            wake.Set();
        }
        // Observe late failures too, even if the caller already timed out.
        _=completion.Task.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        return completion.Task.WaitAsync(TimeSpan.FromMilliseconds(900));
    }
    private void Loop()
    {
        while(true)
        {
            wake.WaitOne();Action? work;
            lock(gate){work=pending;pending=null;if(disposed&&work==null)break;}
            work?.Invoke();
            lock(gate){if(disposed)break;}
        }
        wake.Dispose();
    }
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;wake.Set();}}
}
