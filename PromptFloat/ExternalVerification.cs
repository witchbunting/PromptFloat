using Microsoft.Win32;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Text.Json;
namespace PromptFloat;

internal static class ExternalVerification
{
    private sealed record CompatibilityResult(string app,string state,string detail,string version);
    private static async Task<T> Automation<T>(Func<T> action)=>await Task.Run(action).WaitAsync(TimeSpan.FromSeconds(3));
    private static string? AppPath(string file)=>Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+file,"",null) as string??Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+file,"",null) as string;
    public static async Task Run(Host host,string output)
    {
        var records=new List<CompatibilityResult>();
        void Record(string app,string state,string detail,string version=""){records.Add(new(app,state,detail,version));File.WriteAllText(Path.Combine(output,"compatibility.json"),JsonSerializer.Serialize(records,Store.Json));}
        async Task Exercise(IntPtr hwnd,AutomationElement edit,string name,string? file)
        {
            Focus(hwnd);await Automation(()=>{edit.SetFocus();return true;});await Task.Delay(160);
            if(name=="VS Code")
            {
                var bounds=await Automation(()=>edit.Current.BoundingRectangle);
                if(!bounds.IsEmpty&&bounds.Width>0&&bounds.Height>0){Native.TestClick(hwnd,(int)(bounds.Left+Math.Min(bounds.Width/2,60)),(int)(bounds.Top+Math.Min(bounds.Height/2,12)));await Task.Delay(80);}
                if(Native.Pid(Native.GetForegroundWindow())!=Native.Pid(hwnd))throw new Exception("测试编辑区未获得焦点");
                Native.TestKey(0x24);Native.TestKey(0x27);Native.TestKey(0x27,true);await Task.Delay(120);
            }
            else await Automation(()=>
            {
                if(!edit.TryGetCurrentPattern(TextPattern.Pattern,out var p))throw new Exception("未暴露选区接口");
                var range=((TextPattern)p).DocumentRange.Clone();range.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,1);range.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,1);range.Select();return true;
            });
            await host.Input.CaptureAsync(host.Store.Settings);
            if(host.Input.Target==null||host.Input.Target.Pid!=Native.Pid(hwnd))throw new Exception("未捕获专用测试输入框");
            var selected=await host.Input.SelectedTextAsync();
            if(name!="VS Code"&&selected!="旧")throw new Exception("选区未被正确读取："+(selected??"null"));
            if(name=="VS Code")
            {
                var target=host.Input.Target;
                var info=await Automation(()=>new {target.Element!.Current.Name,target.Element.Current.ClassName,target.CopyOnly,selectedText=selected});
                File.WriteAllText(Path.Combine(output,"vscode-diagnostics.json"),JsonSerializer.Serialize(info,Store.Json));
            }
            host.Panel.Show();host.Panel.Activate();await Task.Delay(80);
            var result=await host.Input.InsertAsync("中文✨\r\n第二行",()=>host.Panel.Hide());
            if(result.Status!=InputStatus.Executed)throw new Exception(result.Message+host.Input.LastFailure);
            await Task.Delay(250);var value=Normalize(await Automation(()=>ReadText(edit)));
            if(name=="VS Code"&&file!=null){Focus(hwnd);Native.CtrlKey(0x53);await Task.Delay(350);value=Normalize(File.ReadAllText(file));}
            if(value!="A中文✨\n第二行B")
            {
                // Some providers lag behind DOM input events. Read again after
                // rendering settles; this never repeats the paste operation.
                await Task.Delay(700);
                value=Normalize(await Automation(()=>ReadText(edit)));
            }
            if(value!="A中文✨\n第二行B")
            {
                if(name=="Edge")
                {
                    var events=await Automation(()=>AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text)).Cast<AutomationElement>().Select(e=>e.Current.Name).ToArray());
                    File.WriteAllText(Path.Combine(output,"edge-input-events.json"),JsonSerializer.Serialize(new {events,value,foreground=Native.GetForegroundWindow().ToInt64(),target=hwnd.ToInt64(),clipboard=Clipboard.GetText()},Store.Json));
                }
                throw new Exception("替换结果不匹配："+value);
            }
        }
        foreach(var name in new[]{"Edge","Chrome","记事本","VS Code"})
        {
            var executable=name switch {"Edge"=>AppPath("msedge.exe"),"Chrome"=>AppPath("chrome.exe"),"VS Code"=>AppPath("Code.exe"),_=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","notepad.exe")};
            if(executable==null||!File.Exists(executable)){Record(name,"unavailable","本机未发现程序，待手动兼容验证");continue;}
            if(name=="记事本"&&Process.GetProcessesByName("notepad").Length>0){Record(name,"pending-manual","已有记事本窗口，保留当前编辑状态，跳过自动测试");continue;}
            var root=Path.Combine(host.Store.Root,"external-"+name);Directory.CreateDirectory(root);var unique=(name=="VS Code"?"终端说明-":"")+"promptfloat-check-"+Guid.NewGuid().ToString("N");var file=Path.Combine(root,unique+".txt");File.WriteAllText(file,"A旧B");
            var start=new ProcessStartInfo(executable){UseShellExecute=false};
            if(name is "Edge" or "Chrome")
            {
                var html=Path.Combine(root,"fixture.html");File.WriteAllText(html,"<!doctype html><meta charset='utf-8'><title>"+unique+"</title><h1>PromptFloat 本地输入测试</h1><textarea aria-label='PromptFloat check' autofocus style='width:600px;height:220px'>A旧B</textarea><pre id='trace'>events:</pre><script>const e=document.querySelector('textarea');e.focus();e.setSelectionRange(1,2);for(const type of ['keydown','keyup','paste','input','focus','blur'])e.addEventListener(type,x=>{document.querySelector('#trace').textContent+=type+':'+(x.key||'')+':'+(x.ctrlKey||false)+';';if(type==='input')document.querySelector('#trace').textContent+='input-value:'+JSON.stringify(e.value)+';';});</script>");
                foreach(var arg in new[]{"--user-data-dir="+Path.Combine(root,"profile"),"--no-first-run","--no-default-browser-check","--disable-sync","--force-renderer-accessibility","--new-window",new Uri(html).AbsoluteUri})start.ArgumentList.Add(arg);
            }
            else if(name=="VS Code")
            {
                var profile=Path.Combine(root,"profile");Directory.CreateDirectory(Path.Combine(profile,"User"));File.WriteAllText(Path.Combine(profile,"User","settings.json"),"{\"workbench.startupEditor\":\"none\",\"workbench.welcomePage.experimentalOnboarding\":false,\"chat.disableAIFeatures\":true,\"security.workspace.trust.enabled\":false,\"editor.accessibilitySupport\":\"on\",\"update.mode\":\"none\",\"telemetry.telemetryLevel\":\"off\"}");
                foreach(var arg in new[]{"--user-data-dir",profile,"--extensions-dir",Path.Combine(root,"extensions"),"--disable-extensions","--force-renderer-accessibility","--skip-welcome","--skip-release-notes","--new-window",file})start.ArgumentList.Add(arg);
            }
            else start.ArgumentList.Add(file);
            using var process=Process.Start(start)!;IntPtr hwnd=IntPtr.Zero;
            try
            {
                for(var i=0;i<100&&hwnd==IntPtr.Zero;i++){await Task.Delay(100);hwnd=Native.TestWindowByTitle(unique);}
                if(hwnd==IntPtr.Zero)throw new Exception("专用测试窗口未打开");Focus(hwnd);var edit=await Editor(hwnd);
                await Exercise(hwnd,edit,name,file);
                Record(name,"passed","专用测试输入：焦点恢复、中文/Emoji/多行选区替换；选区读取："+(name=="VS Code"?"依控件支持，可手动复制后保存":"支持"),FileVersionInfo.GetVersionInfo(executable).FileVersion??"");
            }
            catch(Exception e){Record(name,"failed",e.Message,FileVersionInfo.GetVersionInfo(executable).FileVersion??"");}
            finally
            {
                host.Panel.Hide();
                if(hwnd!=IntPtr.Zero){try{using var own=Process.GetProcessById(Native.Pid(hwnd));if(!own.HasExited)own.Kill(true);}catch{}}
                try{if(!process.HasExited)process.Kill(true);}catch{}
            }
        }
        Record("Microsoft Word","pending-manual","本机 Word COM 由 WPS 提供；此前 WPS 专用空白文档验证通过，不能替代 Microsoft Word 验收");
        Record("微信 / QQ","pending-manual","保留现有聊天草稿；待专用测试会话验收");
        Record("实际 AI 网页 / 富文本","pending-manual","网页 textarea 已覆盖；各站点富文本输入需人工验收");
        Record("Windows 10 / 多屏混合 DPI / 中文输入法","pending-manual","需要相应系统、显示器及输入法组合验收");
        if(records.Any(r=>r.state=="failed"))throw new Exception("实际软件兼容检查未全部通过，详情见 artifacts/compatibility.json。");
    }
    private static async Task<AutomationElement> Editor(IntPtr hwnd)
    {
        var window=await Automation(()=>AutomationElement.FromHandle(hwnd));
        await Automation(()=>{var close=window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ClassNameProperty,"onboarding-a-close-btn"));if(close!=null&&close.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))((InvokePattern)invoke).Invoke();return true;});
        for(var i=0;i<30;i++)
        {
            await Task.Delay(100);
            var found=await Automation(()=>
            {
                var candidates=window.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)));
                return candidates.Cast<AutomationElement>().OrderBy(e=>e.Current.ControlType==ControlType.Edit?0:1).FirstOrDefault(e=>{try{return e.Current.IsEnabled&&e.Current.IsKeyboardFocusable&&ReadText(e).TrimEnd('\r','\n')=="A旧B";}catch{return false;}});
            });
            if(found!=null)return found;
        }
        throw new Exception("未找到可读取的普通编辑区");
    }
    private static string Normalize(string value)=>value.Replace("\r\n","\n").Replace("\r","\n").TrimEnd('\r','\n');
    private static string ReadText(AutomationElement element)
    {
        if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var value))return ((ValuePattern)value).Current.Value;
        if(element.TryGetCurrentPattern(TextPattern.Pattern,out var text))return ((TextPattern)text).DocumentRange.GetText(1000);
        return "";
    }
    private static void Focus(IntPtr hwnd)
    {
        var thread=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);var own=Native.GetCurrentThreadId();var attached=thread!=own&&Native.AttachThreadInput(own,thread,true);try{Native.SetForegroundWindow(hwnd);}finally{if(attached)Native.AttachThreadInput(own,thread,false);}
    }
}
