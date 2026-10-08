using System.Windows.Automation;
using System.Windows.Automation.Text;
namespace PromptFloat;

internal enum InputStatus { Executed, NoTarget, InvalidTarget, FocusFailed, PermissionDenied, SendFailed, CopyOnly }
internal sealed record InputResult(InputStatus Status,string Message);
internal sealed class InputTarget
{
    public IntPtr Window;public int Pid;public DateTime Started;public string App="";public AutomationElement? Element;
    public int[] RuntimeId=[];public TextPatternRange[] Selection=[];public bool CopyOnly;public bool Editable;
}
internal sealed class InputBridge : IDisposable
{
    private readonly Native.WinEventDelegate callback;
    private readonly IntPtr hook;
    private readonly AutomationWorker automation=new();
    private bool restoring;
    public InputTarget? Target {get;private set;}
    internal bool Restoring=>restoring;
    internal void UseTarget(InputTarget target){Target=target;}
    internal string LastFailure {get;private set;}="";
    public IntPtr LastExternal {get;private set;}
    public event Action? TargetChanged;
    internal event Action? ForegroundChanged;
    public InputBridge()
    {
        LastExternal=Native.GetForegroundWindow();
        callback=(_,_,window,_,_,_,_)=>{
            if(!Native.IsExternal(window))return;
            Application.Current.Dispatcher.BeginInvoke(()=>{
                if(Native.GetForegroundWindow()!=window)return;
                LastExternal=window;ForegroundChanged?.Invoke();if(!restoring&&Target!=null&&Target.Window!=window) {Target=null;TargetChanged?.Invoke();}
            });
        };
        hook=Native.SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
    }
    internal Task<T> AutomationAsync<T>(Func<T> action)=>automation.RunAsync(action);
    private Task<T> UIA<T>(Func<T> action)=>AutomationAsync(action);
    public async Task CaptureAsync(Settings settings)
    {
        Target=null;var window=Native.GetForegroundWindow();
        if(!Native.IsExternal(window))window=LastExternal;
        if(!Native.IsExternal(window)||!Native.IsWindow(window)){TargetChanged?.Invoke();return;}
        try
        {
            var pid=Native.Pid(window);using var process=Process.GetProcessById(pid);
            var app=process.ProcessName;var started=process.StartTime;
            var copyApps=settings.CopyOnlyApps.ToArray();
            var data=await UIA(()=>{
                var element=AutomationElement.FocusedElement;
                if(element==null||element.Current.ProcessId!=pid)return null;
                var c=element.Current;var editable=!c.IsPassword&&c.IsEnabled&&c.IsKeyboardFocusable &&
                    (c.ControlType==ControlType.Edit||c.ControlType==ControlType.Document||element.TryGetCurrentPattern(ValuePattern.Pattern,out _));
                if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var value)&&((ValuePattern)value).Current.IsReadOnly)editable=false;
                var copyOnly=!editable||copyApps.Contains(app,StringComparer.OrdinalIgnoreCase)||
                    new[]{"WindowsTerminal","cmd","powershell","pwsh","conhost","OpenConsole","mintty","wsl"}.Contains(app,StringComparer.OrdinalIgnoreCase)||Native.WindowClass(window)=="ConsoleWindowClass";
                var node=element;
                for(var i=0;i<7 && node!=null;i++)
                {
                    var name=node.Current.Name;var cls=node.Current.ClassName;
                    var type=node.Current.ControlType;
                    var terminalPane=(type==ControlType.Pane||type==ControlType.Group)&&
                        (name.Equals("Terminal",StringComparison.OrdinalIgnoreCase)||name=="终端"||name.StartsWith("Terminal:",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Terminal ",StringComparison.OrdinalIgnoreCase)||name.StartsWith("终端:"));
                    if(cls.Contains("xterm",StringComparison.OrdinalIgnoreCase)||terminalPane)copyOnly=true;
                    node=TreeWalker.ControlViewWalker.GetParent(node);
                }
                var selection=!c.IsPassword&&element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)?((TextPattern)pattern).GetSelection().Select(r=>r.Clone()).ToArray():[];
                if(selection.Length>1)copyOnly=true;
                return new InputTarget {Window=window,Pid=pid,Started=started,App=app,Element=element,RuntimeId=element.GetRuntimeId(),Selection=selection,CopyOnly=copyOnly,Editable=editable};
            });
            // A provider may respond after the user has already switched windows.
            var foreground=Native.GetForegroundWindow();if(Native.IsExternal(foreground)&&foreground!=window)return;
            Target=data;
        }
        catch(Exception e) {LastFailure="capture: "+e.GetType().Name;Target=null;}
        TargetChanged?.Invoke();
    }
    public void Clear() {Target=null;TargetChanged?.Invoke();}
    public async Task<string?> SelectedTextAsync()
    {
        var target=Target;if(target?.Element==null)return null;
        try {return await UIA(()=>{
            if(target.Element.Current.IsPassword||!target.Element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern))return null;
            var text=string.Join("\n",((TextPattern)pattern).GetSelection().Select(r=>r.GetText(int.MaxValue)));
            return string.IsNullOrWhiteSpace(text)?null:text;
        });}catch{return null;}
    }
    public static async Task CopyAsync(string text)
    {
        for(var i=0;i<4;i++)
        {
            try {Native.WriteClipboard(new WindowInteropHelper(Host.Current.Ball).EnsureHandle(),text);return;}
            catch(System.ComponentModel.Win32Exception) when(i<3) {await Task.Delay(60);}
        }
    }
    public async Task<InputResult> InsertAsync(string text,Action? releasePanel=null,SelectionSnapshot? selection=null)
    {
        var target=Target;
        if(target==null)return new(InputStatus.NoTarget,"请选择输入框，或复制模板后手动粘贴。");
        if(selection!=null && !await ValidateSelectionAsync(selection))return new(InputStatus.InvalidTarget,"原选区已改变，结果只能复制。");
        if(target.CopyOnly)return new(InputStatus.CopyOnly,"此位置使用复制模式，请手动粘贴。");
        restoring=true;
        try
        {
            releasePanel?.Invoke();
            using var process=Process.GetProcessById(target.Pid);
            if(!Native.IsWindow(target.Window)||Native.Pid(target.Window)!=target.Pid||process.StartTime!=target.Started) return new(InputStatus.InvalidTarget,"原输入窗口已失效，请重新选择。");
            var level=Native.Integrity(target.Pid);var own=Native.Integrity(Environment.ProcessId);
            if(level<0||own<0||level>own)return new(InputStatus.PermissionDenied,"目标应用权限较高或无法确认权限，请手动粘贴。");
            if(Target!=target)return new(InputStatus.InvalidTarget,"输入目标已改变。");
            if(!Native.SetForegroundWindow(target.Window))return new(InputStatus.FocusFailed,"无法恢复原窗口焦点，请手动粘贴。");
            await Task.Delay(70);
            await UIA(()=>{
                if(target.Element==null||!target.Element.GetRuntimeId().SequenceEqual(target.RuntimeId)||target.Element.Current.IsPassword)throw new InvalidOperationException();
                if(!AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(target.RuntimeId))
                {
                    target.Element.SetFocus();foreach(var range in target.Selection)if(!string.IsNullOrEmpty(range.GetText(1)))range.Select();
                }
                return true;
            });
            var current=await UIA(()=>AutomationElement.FocusedElement.GetRuntimeId());
            for(var attempt=0;attempt<7&&!current.SequenceEqual(target.RuntimeId)&&Native.Pid(Native.GetForegroundWindow())==target.Pid;attempt++){await Task.Delay(35);current=await UIA(()=>AutomationElement.FocusedElement.GetRuntimeId());}
            if(!current.SequenceEqual(target.RuntimeId)||Native.Pid(Native.GetForegroundWindow())!=target.Pid||Target!=target){LastFailure=$"runtime={string.Join(',',target.RuntimeId)}; focused={string.Join(',',current)}; window={target.Window}/{Native.GetForegroundWindow()}; sameTarget={Target==target}";return new(InputStatus.FocusFailed,"原输入控件未恢复焦点，请手动粘贴。");}
            // Wait for user-held modifiers; never synthesize key-up for keys the user is holding.
            for(var i=0;i<20&&(Held(0x10)||Held(0x11)||Held(0x12)||Held(0x5b)||Held(0x5c));i++)await Task.Delay(25);
            if(Held(0x10)||Held(0x11)||Held(0x12)||Held(0x5b)||Held(0x5c))return new(InputStatus.SendFailed,"请松开快捷键后重试。");
            await CopyAsync(text);
            // Give clipboard notifications and the target's Paste command a turn
            // before the single key sequence; no automatic repeat is performed.
            await Task.Delay(80);
            if(selection!=null&&!await ValidateSelectionAsync(selection))return new(InputStatus.InvalidTarget,"原文或选区已改变，结果已在剪贴板中。");
            var finalFocus=await UIA(()=>AutomationElement.FocusedElement.GetRuntimeId());
            if(Target!=target||Native.Pid(Native.GetForegroundWindow())!=target.Pid||!finalFocus.SequenceEqual(target.RuntimeId))return new(InputStatus.FocusFailed,"目标已改变，模板已在剪贴板中。");
            return Native.Paste()?new(InputStatus.Executed,"已执行填入，请确认目标内容。"):new(InputStatus.SendFailed,"未完成输入发送，请手动粘贴。");
        }
        catch {return new(InputStatus.InvalidTarget,"输入控件不可用或未响应，请复制后手动粘贴。");}
        finally {restoring=false;var foreground=Native.GetForegroundWindow();if(Native.IsExternal(foreground)&&Native.Pid(foreground)!=target.Pid)Clear();}
    }
    internal async Task<SelectionSnapshot?> SnapshotAsync()
    {
        var target=Target;
        if(target?.Element==null||target.Selection.Length!=1)return null;
        try {var text=await UIA(()=>target.Element.Current.IsPassword?"":target.Selection[0].GetText(int.MaxValue));return string.IsNullOrWhiteSpace(text)?null:new SelectionSnapshot(target,text);}catch{return null;}
    }
    internal async Task<bool> SameSelectionAsync(SelectionSnapshot a,SelectionSnapshot b)
    {
        if(!a.Valid||a.Target.Window!=b.Target.Window||!a.Target.RuntimeId.SequenceEqual(b.Target.RuntimeId)||a.Text!=b.Text)return false;
        try {return await UIA(()=>a.Target.Selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start,b.Target.Selection[0],TextPatternRangeEndpoint.Start)==0&&a.Target.Selection[0].CompareEndpoints(TextPatternRangeEndpoint.End,b.Target.Selection[0],TextPatternRangeEndpoint.End)==0);}catch{return false;}
    }
    internal async Task<bool> ValidateSelectionAsync(SelectionSnapshot snapshot)
    {
        if(!snapshot.Valid||!Native.IsWindow(snapshot.Target.Window)||Native.Pid(snapshot.Target.Window)!=snapshot.Target.Pid)return false;
        var foreground=Native.GetForegroundWindow();
        if(Native.IsExternal(foreground)&&foreground!=snapshot.Target.Window)return false;
        try {return await UIA(()=>{
            var element=snapshot.Target.Element!;
            var focused=AutomationElement.FocusedElement;
            if(focused!=null&&focused.Current.ProcessId!=Environment.ProcessId&&!focused.GetRuntimeId().SequenceEqual(snapshot.Target.RuntimeId))return false;
            if(element.Current.IsPassword||!element.GetRuntimeId().SequenceEqual(snapshot.Target.RuntimeId)||!element.TryGetCurrentPattern(TextPattern.Pattern,out var pattern))return false;
            var ranges=((TextPattern)pattern).GetSelection();
            return ranges.Length==1&&ranges[0].GetText(int.MaxValue)==snapshot.Text&&snapshot.Target.Selection[0].GetText(int.MaxValue)==snapshot.Text&&
                ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,snapshot.Target.Selection[0],TextPatternRangeEndpoint.Start)==0&&
                ranges[0].CompareEndpoints(TextPatternRangeEndpoint.End,snapshot.Target.Selection[0],TextPatternRangeEndpoint.End)==0;
        });}catch{return false;}
    }
    private static bool Held(int key)=>(Native.GetAsyncKeyState(key)&0x8000)!=0;
    public void Dispose() {if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);automation.Dispose();}
}
