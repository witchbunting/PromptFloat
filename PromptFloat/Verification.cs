using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace PromptFloat;

internal static class Verification
{
    private sealed class FixtureCommand {public int Sequence {get;set;}public string Text {get;set;}="";public int Start {get;set;}public int Length {get;set;}public string Field {get;set;}="plain";}
    public static int Fixture(string[] args)
    {
        var dir=args[Array.IndexOf(args,"--fixture")+1];Directory.CreateDirectory(dir);var app=new Application();Ui.Styles();Ui.Theme("浅色");var w=Ui.Window("输入兼容性测试",560,540);var field=Ui.Field("",true,140);var password=new PasswordBox {Margin=new Thickness(5),Height=32};var rich=new RichTextBox {Height=110};var nonInput=Ui.Button("非输入控件",()=>{});w.Content=Ui.Stack(Ui.Labeled("普通输入框",field),Ui.Labeled("富文本输入框",rich),Ui.Labeled("密码（应拒绝自动填入）",password),nonInput);
        var last=-1;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(60)};
        void State(){var snapshot=new {sequence=last,text=field.Text,rich=new System.Windows.Documents.TextRange(rich.Document.ContentStart,rich.Document.ContentEnd).Text,start=field.SelectionStart,length=field.SelectionLength};var temp=Path.Combine(dir,"state.tmp");File.WriteAllText(temp,JsonSerializer.Serialize(snapshot));File.Move(temp,Path.Combine(dir,"state.json"),true);}
        field.TextChanged+=(_,_)=>State();rich.TextChanged+=(_,_)=>State();
        w.PreviewKeyDown+=(_,e)=>{var trace=new {sequence=last,key=e.Key.ToString(),systemKey=e.SystemKey.ToString(),modifiers=Keyboard.Modifiers.ToString(),focus=Keyboard.FocusedElement?.GetType().Name,richCaretValid=rich.CaretPosition.IsAtInsertionPosition};File.AppendAllText(Path.Combine(dir,"key-events.jsonl"),JsonSerializer.Serialize(trace)+"\n");};
        timer.Tick+=(_,_)=>{
            try
            {
                var file=Path.Combine(dir,"command.json");if(!File.Exists(file))return;var c=JsonSerializer.Deserialize<FixtureCommand>(ReadShared(file));if(c==null||c.Sequence==last)return;last=c.Sequence;w.Activate();Native.SetForegroundWindow(new WindowInteropHelper(w).Handle);
                field.IsReadOnly=c.Field=="readonly";if(c.Field=="button")nonInput.Focus();else if(c.Field=="password")password.Focus();else if(c.Field=="rich"){rich.Document.Blocks.Clear();rich.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(c.Text)));rich.Focus();rich.CaretPosition=rich.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);}else{field.Text=c.Text;field.Focus();field.Select(c.Start,c.Length);}State();
            }catch(IOException){}
        };
        w.Loaded+=(_,_)=>{File.WriteAllText(Path.Combine(dir,"hwnd.tmp"),new WindowInteropHelper(w).Handle.ToInt64().ToString());File.Move(Path.Combine(dir,"hwnd.tmp"),Path.Combine(dir,"hwnd.txt"),true);timer.Start();};app.Run(w);timer.Stop();return 0;
    }
    public static async Task Run(Host host,string[] args)
    {
        var output=Path.GetFullPath("artifacts");Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"verification-error.txt")))File.Delete(Path.Combine(output,"verification-error.txt"));var checks=new List<object>();
        void Check(bool passed,string name,string detail=""){checks.Add(new {name,passed,detail});File.WriteAllText(Path.Combine(output,"desktop-tests.json"),JsonSerializer.Serialize(checks,Store.Json));if(!passed)throw new Exception(name+": "+detail);}
        host.Store.AddBuiltIns(Catalog.Create().Categories.Select(c=>c.SourceId!));host.Store.Settings.Initialized=true;host.Ball.Show();host.Panel.Reload();host.Panel.Show();await Task.Delay(180);Render(host.Panel,Path.Combine(output,"panel.png"));host.Panel.Hide();
        var manager=new ManagerWindow(host);manager.Show();await Task.Delay(120);Render(manager,Path.Combine(output,"manager.png"));manager.Close();
        var editor=new EditorWindow(host.Store,host.Store.Read().Prompts.First());editor.Show();await Task.Delay(60);Render(editor,Path.Combine(output,"editor.png"));editor.Close();
        var settings=new SettingsWindow(host);settings.Show();await Task.Delay(60);Render(settings,Path.Combine(output,"settings.png"));settings.Close();
        var catalog=new CatalogWindow(host,true);catalog.Show();await Task.Delay(60);Render(catalog,Path.Combine(output,"onboarding.png"));catalog.Close();
        var variable=new VariableWindow(host.Store.Read().Prompts.First(),null);variable.Show();await Task.Delay(60);Render(variable,Path.Combine(output,"variables.png"));variable.Close();Check(true,"six desktop views render");
        await TutorialVerification.Run(host,Check,output);
        var fixtureDir=Path.Combine(host.Store.Root,"fixture");Directory.CreateDirectory(fixtureDir);
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"PromptFloat.exe")) {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--fixture");start.ArgumentList.Add(fixtureDir);
        using var child=Process.Start(start)??throw new Exception("fixture failed");
        try
        {
            for(var i=0;i<100&&!File.Exists(Path.Combine(fixtureDir,"hwnd.txt"));i++)await Task.Delay(50);
            var handle=new IntPtr(long.Parse(File.ReadAllText(Path.Combine(fixtureDir,"hwnd.txt"))));
            async Task Command(int sequence,string text,int selectionStart,int length,string kind="plain")
            {
                var commandTemp=Path.Combine(fixtureDir,"command.tmp");
                File.WriteAllText(commandTemp,JsonSerializer.Serialize(new FixtureCommand {Sequence=sequence,Text=text,Start=selectionStart,Length=length,Field=kind}));
                File.Move(commandTemp,Path.Combine(fixtureDir,"command.json"),true);
                for(var i=0;i<60;i++){await Task.Delay(40);try{using var s=JsonDocument.Parse(ReadShared(Path.Combine(fixtureDir,"state.json")));if(s.RootElement.GetProperty("sequence").GetInt32()==sequence)break;}catch(IOException){}}
                // Test-only activation of this fixture. Production code never attaches thread input.
                var foregroundThread=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);var ownThread=Native.GetCurrentThreadId();
                var attached=foregroundThread!=ownThread&&Native.AttachThreadInput(ownThread,foregroundThread,true);
                try {Native.SetForegroundWindow(handle);}finally{if(attached)Native.AttachThreadInput(ownThread,foregroundThread,false);}
                await Task.Delay(100);
                var control=await host.Input.AutomationAsync(()=>{
                    var controls=AutomationElement.FromHandle(handle).FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,kind=="password"?ControlType.Edit:kind=="rich"?ControlType.Document:kind=="button"?ControlType.Button:ControlType.Edit));
                    var focused=controls.Cast<AutomationElement>().FirstOrDefault(e=>kind=="button"?e.Current.Name=="非输入控件":kind=="password"?e.Current.IsPassword:!e.Current.IsPassword);
                    focused?.SetFocus();return focused;
                });
                await Task.Delay(80);await host.Input.CaptureAsync(host.Store.Settings);
                for(var attempt=0;attempt<4&&host.Input.Target?.Pid!=child.Id;attempt++)
                {
                    await Task.Delay(150);foregroundThread=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);ownThread=Native.GetCurrentThreadId();attached=foregroundThread!=ownThread&&Native.AttachThreadInput(ownThread,foregroundThread,true);
                    try{Native.SetForegroundWindow(handle);}finally{if(attached)Native.AttachThreadInput(ownThread,foregroundThread,false);}
                    await host.Input.AutomationAsync(()=>{control?.SetFocus();return true;});await Task.Delay(100);await host.Input.CaptureAsync(host.Store.Settings);
                }
                Check(host.Input.Target?.Pid==child.Id,"fixture target identity "+sequence,host.Input.Target?.App??"no target; "+host.Input.LastFailure);
            }
            async Task<string> Text(string property="text")
            {
                await Task.Delay(700);using var s=JsonDocument.Parse(ReadShared(Path.Combine(fixtureDir,"state.json")));var reported=s.RootElement.GetProperty(property).GetString()!;
                var observed=await Task.Run(()=>{var elements=AutomationElement.FromHandle(handle).FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,property=="rich"?ControlType.Document:ControlType.Edit));var edit=elements.Cast<AutomationElement>().First(e=>!e.Current.IsPassword);return property=="rich"?((TextPattern)edit.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(1000):((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;});
                File.AppendAllText(Path.Combine(fixtureDir,"readback.jsonl"),JsonSerializer.Serialize(new {property,reported,observed})+"\n");return observed;
            }
            await Command(1,"前缀旧后缀",2,1);Check(host.Input.Target!=null,"UIA captures external editable control");
            var selection=await host.Input.SelectedTextAsync();
            var inputDiagnostics=await host.Input.AutomationAsync(()=>new {selection,app=host.Input.Target!.App,copyOnly=host.Input.Target.CopyOnly,control=host.Input.Target.Element!.Current.ControlType.ProgrammaticName,patterns=host.Input.Target.Element.GetSupportedPatterns().Select(p=>p.ProgrammaticName).ToArray(),captured=host.Input.Target.Selection.Select(r=>r.GetText(100)).ToArray()});
            File.WriteAllText(Path.Combine(output,"input-diagnostics.json"),JsonSerializer.Serialize(inputDiagnostics,Store.Json));
            Check(selection=="旧","selection extraction without clipboard fallback",selection??"null");
            host.Panel.Show();var result=await host.Input.InsertAsync("中文✨\r\n```code```\r\n");Check(result.Status==InputStatus.Executed,"input dispatched after panel focus",result.Message);Check(await Text()=="前缀中文✨\r\n```code```\r\n后缀","selection replacement preserves prefix suffix unicode and multiline");host.Panel.Hide();
            await Command(2,"AB",1,0);result=await host.Input.InsertAsync("插入");Check(result.Status==InputStatus.Executed&&await Text()=="A插入B","caret insertion preserves existing text",result.Message);
            await Command(3,"",0,0,"password");result=await host.Input.InsertAsync("blocked");Check(result.Status is InputStatus.CopyOnly or InputStatus.NoTarget,"password control blocks automatic paste");
            await Command(4,"复制模式",0,0);host.Store.Settings.CopyOnlyApps.Add("PromptFloat");await host.Input.CaptureAsync(host.Store.Settings);result=await host.Input.InsertAsync("blocked");Check(result.Status==InputStatus.CopyOnly&&await Text()=="复制模式","per-app copy-only blocks automatic paste");host.Store.Settings.CopyOnlyApps.Clear();
            await Command(5,"",0,0,"rich");result=await host.Input.InsertAsync("富文本✨");Check(result.Status==InputStatus.Executed&&(await Text("rich")).Contains("富文本✨"),"rich text control paste",result.Message);
            var secondDir=Path.Combine(host.Store.Root,"fixture-switch");Directory.CreateDirectory(secondDir);
            var secondStart=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"PromptFloat.exe")){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};secondStart.ArgumentList.Add("--fixture");secondStart.ArgumentList.Add(secondDir);
            using(var second=Process.Start(secondStart)!)
            {
                try
                {
                    for(var i=0;i<100&&!File.Exists(Path.Combine(secondDir,"hwnd.txt"));i++)await Task.Delay(50);
                    var secondHandle=new IntPtr(long.Parse(File.ReadAllText(Path.Combine(secondDir,"hwnd.txt"))));
                    var from=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);var ownThread=Native.GetCurrentThreadId();var attached=from!=ownThread&&Native.AttachThreadInput(ownThread,from,true);
                    host.Surfaces.ShowMenu();try{Native.SetForegroundWindow(secondHandle);}finally{if(attached)Native.AttachThreadInput(ownThread,from,false);}
                    await Task.Delay(200);Check(host.Surfaces.ActiveMenu?.IsOpen!=true,"switching application closes menu and releases navigation hook");Check(host.Input.Target==null,"external window switch invalidates captured target");
                    result=await host.Input.InsertAsync("must not paste");Check(result.Status==InputStatus.NoTarget,"stale target cannot dispatch input");
                }
                finally{if(!second.HasExited){second.Kill();await second.WaitForExitAsync();}}
            }
            await QuickVerification.Run(host,handle,(n,t,a,b,k)=>Command(n,t,a,b,k),p=>Text(p),Check,output);
            await Command(6,"目标",0,0);child.Kill();await child.WaitForExitAsync();result=await host.Input.InsertAsync("blocked");Check(result.Status==InputStatus.InvalidTarget,"closed target blocks automatic paste");
            await InputBridge.CopyAsync("剪贴板测试✨");Check(Clipboard.GetText()=="剪贴板测试✨","clipboard Unicode persisted");Check(Clipboard.GetDataObject()?.GetDataPresent("CanUploadToCloudClipboard")==true,"clipboard cloud exclusion marker");
        }
        finally {if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
        host.Store.Settings.AutoBackup=false;host.Store.Change(l=>{for(var i=0;i<4970;i++)l.Prompts.Add(new Prompt {Title="性能模板 "+i,Body="内容 "+i});});
        var menuWatch=Stopwatch.StartNew();host.Surfaces.ShowMenu();host.Surfaces.ActiveMenu!.UpdateLayout();var menuMs=menuWatch.Elapsed.TotalMilliseconds;Check(menuMs<200,"5000-template category menu under 200ms",menuMs.ToString("F1")+" ms");QuickVerification.Render(host.Surfaces.ActiveMenu!,"menu-5000.png",output);host.Surfaces.Close();
        var quickSearchWatch=Stopwatch.StartNew();host.Surfaces.ShowSearch(null);host.Surfaces.ActiveVisual!.UpdateLayout();var quickSearchMs=quickSearchWatch.Elapsed.TotalMilliseconds;
        Check(quickSearchMs<200,"5000-template quick search loads all results",quickSearchMs.ToString("F1")+" ms");QuickVerification.Render(host.Surfaces.ActiveVisual!,"quick-search.png",output);host.Surfaces.Close();
        var watch=Stopwatch.StartNew();host.Panel.Reload();host.Panel.Show();host.Panel.UpdateLayout();var elapsed=watch.Elapsed.TotalMilliseconds;await Task.Delay(200);Check(elapsed<200,"5000-template panel under 200ms on this host",elapsed.ToString("F1")+" ms");host.Panel.Hide();
        // Measure the actual debounced search and result layout, using only our own controls.
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var searchBox=(TextBox)typeof(PanelWindow).GetField("search",flags)!.GetValue(host.Panel)!;
        var promptList=(ListBox)typeof(PanelWindow).GetField("prompts",flags)!.GetValue(host.Panel)!;
        var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(ListBox));
        var completion=new TaskCompletionSource<double>();var searchWatch=Stopwatch.StartNew();double searchUiMs;
        EventHandler changed=(_,_)=>host.Panel.Dispatcher.BeginInvoke(()=>{host.Panel.UpdateLayout();completion.TrySetResult(searchWatch.Elapsed.TotalMilliseconds);},DispatcherPriority.ApplicationIdle);
        host.Panel.Show();host.Panel.UpdateLayout();descriptor.AddValueChanged(promptList,changed);searchWatch.Restart();
        try{searchBox.Text="内容 42";searchUiMs=await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));}
        finally{descriptor.RemoveValueChanged(promptList,changed);}
        Check(searchUiMs<150&&promptList.Items.Count>0&&promptList.Items.Cast<Prompt>().All(p=>p.Body.Contains("内容")&&p.Body.Contains("42")),"5000-template search including debounce and layout under 150ms on this host",searchUiMs.ToString("F1")+" ms");searchBox.Text="";host.Panel.Hide();
        GC.Collect();GC.WaitForPendingFinalizers();using var own=Process.GetCurrentProcess();var metrics=new {menuMs,quickSearchMs,panelMs=elapsed,searchUiMs,renderTestWorkingSetMB=own.WorkingSet64/1048576d,os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),dataDirectory=host.Store.Root};File.WriteAllText(Path.Combine(output,"desktop-metrics.json"),JsonSerializer.Serialize(metrics,Store.Json));
        Ui.Theme("深色");host.Panel.Reload();host.Panel.Show();Render(host.Panel,Path.Combine(output,"panel-dark.png"));host.Panel.Hide();Ui.Theme("浅色");
        if(args.Contains("--compat"))await ExternalVerification.Run(host,output);
    }
    private static string ReadShared(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    private static void Render(Window window,string path)
    {
        window.UpdateLayout();var visual=(FrameworkElement)window.Content;var image=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth+visual.Margin.Left+visual.Margin.Right),(int)Math.Ceiling(visual.ActualHeight+visual.Margin.Top+visual.Margin.Bottom),96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle((Brush)Application.Current.Resources["Bg"],null,new Rect(0,0,image.PixelWidth,image.PixelHeight));image.Render(background);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);
    }
}
