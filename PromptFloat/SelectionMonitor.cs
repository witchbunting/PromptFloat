using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
namespace PromptFloat;

internal sealed class SelectionSnapshot
{
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public InputTarget Target {get;}
    public string Text {get;}
    public bool Valid {get;private set;}=true;
    public SelectionSnapshot(InputTarget target,string text){Target=target;Text=text;}
    public void Invalidate()=>Valid=false;
}
internal sealed class SelectionMonitor : IDisposable
{
    private readonly Host host;
    // Event unsubscription can block independently of reads. Keep add/remove
    // on the same MTA thread without occupying the input worker.
    private readonly AutomationWorker subscriptions=new();
    private readonly DispatcherTimer debounce=new() {Interval=TimeSpan.FromMilliseconds(250)};
    private readonly HookProc mouseProc,keyProc;
    private IntPtr mouseHook,keyHook,focusHook;
    private readonly Native.WinEventDelegate focusCallback;
    internal int ProbeCount {get;private set;}
    private AutomationEventHandler? selectionHandler,textHandler;
    // Provider objects below are accessed only by subscription MTA worker.
    private AutomationElement? watched;
    private int[] watchedRuntimeId=[];
    private int watchVersion,queued;
    private bool disposed,probing,active;
    public SelectionSnapshot? Current {get;private set;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int kind,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    public SelectionMonitor(Host host)
    {
        this.host=host;
        focusCallback=(_,_,window,_,_,_,_)=>{if(Native.IsExternal(window))Queue();};
        mouseProc=(code,message,data)=>{if(code>=0&&message.ToInt32()==0x202)Queue();return CallNextHookEx(mouseHook,code,message,data);};
        keyProc=(code,message,data)=>{
            if(code>=0&&(message.ToInt32()==0x101||message.ToInt32()==0x105))
            {
                var key=Marshal.ReadInt32(data);
                if(Native.IsExternal(Native.GetForegroundWindow()))
                {
                    if(key is >=0x21 and <=0x28 or 0x09 or 0x0D or 0x10 or 0x11 or 0x41)Queue();
                    else if(key is >=0x30 and <=0xFE && key is not (0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5))host.Ball.Dispatcher.BeginInvoke(()=>{if(!host.Input.Restoring){Current?.Invalidate();host.Surfaces.SelectionCleared();}});
                }
            }
            return CallNextHookEx(keyHook,code,message,data);
        };
        debounce.Tick+=async(_,_)=>{debounce.Stop();await ProbeAsync();};
        host.Input.TargetChanged+=ForegroundChanged;host.Input.ForegroundChanged+=Queue;
    }
    public void Apply()
    {
        StopHooks();active=host.Store.Settings.AutoSelectionActions;
        if(!host.Store.Settings.AutoSelectionActions){Current?.Invalidate();Current=null;host.Surfaces.SelectionCleared();return;}
        mouseHook=SetWindowsHookEx(14,mouseProc,GetModuleHandle(null),0);
        keyHook=SetWindowsHookEx(13,keyProc,GetModuleHandle(null),0);
        focusHook=Native.SetWinEventHook(0x8005,0x8005,IntPtr.Zero,focusCallback,0,0,0);
        // Mouse/key/native focus signals are enough to initiate capture. UIA
        // selection events are registered only on a control the user interacts with.
        Queue(); // One initial capture; subsequent reads are driven by user/focus events.
    }
    private void ForegroundChanged()
    {
        if(probing)return;
        if(host.Input.Target==null){Current?.Invalidate();Current=null;host.Surfaces.SelectionCleared();}
        Queue();
    }
    internal void Refresh()=>Queue();
    private void Queue()
    {
        if(disposed||!active||!host.Store.Settings.AutoSelectionActions||!Native.IsExternal(Native.GetForegroundWindow())||host.Input.Restoring)return;
        if(Interlocked.Exchange(ref queued,1)!=0)return;
        host.Ball.Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref queued,0);if(!disposed&&active){debounce.Stop();debounce.Start();}});
    }
    internal async Task ProbeAsync()
    {
        if(disposed||probing||!Native.IsExternal(Native.GetForegroundWindow())||host.Input.Restoring||host.Surfaces.Editing)return;
        probing=true;ProbeCount++;
        try
        {
            await host.Input.CaptureAsync(host.Store.Settings);
            var target=host.Input.Target;
            var next=await host.Input.SnapshotAsync();
            if(Current!=null&&next!=null&&await host.Input.SameSelectionAsync(Current,next))return;
            Current?.Invalidate();Current=next;
            if(next==null)host.Surfaces.InputAvailable(target);else host.Surfaces.SelectionAvailable(next);
            if(target?.Element!=null)await WatchAsync(target);
        }
        finally {probing=false;}
    }
    private async Task WatchAsync(InputTarget target)
    {
        // Use identity already read on the worker; never query a provider here.
        if(watchedRuntimeId.SequenceEqual(target.RuntimeId))return;
        var version=Interlocked.Increment(ref watchVersion);
        try
        {
            await subscriptions.RunAsync(()=>{
                RemoveWatched();
                if(disposed||!active||version!=Volatile.Read(ref watchVersion))return false;
                watched=target.Element;
                selectionHandler=(_,_)=>{if(version==Volatile.Read(ref watchVersion))Queue();};
                textHandler=(_,_)=>{
                    if(version!=Volatile.Read(ref watchVersion))return;
                    host.Ball.Dispatcher.BeginInvoke(()=>{
                        if(version!=Volatile.Read(ref watchVersion))return;
                        if(!host.Input.Restoring){Current?.Invalidate();host.Surfaces.SelectionCleared();}
                        Queue();
                    });
                };
                Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent,watched,TreeScope.Element,selectionHandler);
                Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent,watched,TreeScope.Element,textHandler);
                return true;
            });
            if(version==Volatile.Read(ref watchVersion))watchedRuntimeId=target.RuntimeId.ToArray();
        }
        catch{watchedRuntimeId=[];}
    }
    private void RemoveWatched()
    {
        var element=watched;var selection=selectionHandler;var text=textHandler;
        watched=null;selectionHandler=null;textHandler=null;
        if(element==null)return;
        try{if(selection!=null)Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent,element,selection);}catch{}
        try{if(text!=null)Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent,element,text);}catch{}
    }
    private void Unwatch()
    {
        Interlocked.Increment(ref watchVersion);watchedRuntimeId=[];
        _=UnwatchAsync();
    }
    private async Task UnwatchAsync()
    {
        try{await subscriptions.RunAsync(()=>{RemoveWatched();return true;});}catch{}
    }
    private void StopHooks()
    {
        debounce.Stop();
        if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
        if(keyHook!=IntPtr.Zero){UnhookWindowsHookEx(keyHook);keyHook=IntPtr.Zero;}
        if(focusHook!=IntPtr.Zero){Native.UnhookWinEvent(focusHook);focusHook=IntPtr.Zero;}
        Unwatch();
    }
    public void Dispose(){disposed=true;StopHooks();subscriptions.Dispose();host.Input.TargetChanged-=ForegroundChanged;host.Input.ForegroundChanged-=Queue;Current?.Invalidate();}
}
