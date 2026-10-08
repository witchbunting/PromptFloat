using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace PromptFloat;

internal sealed class Host : IDisposable
{
    public static Host Current {get;private set;}=null!;
    public Store Store {get;}public InputBridge Input {get;}public BallWindow Ball {get;}private PanelWindow? panel;public PanelWindow Panel=>panel??=new PanelWindow(this);
    public bool Exiting {get;private set;}
    public bool IsPanelVisible=>Surfaces.IsOpen||panel?.IsVisible==true;
    public QuickSurfaces Surfaces {get;} public SelectionMonitor Selection {get;} public CredentialVault Credentials {get;} private PolishService? polish;public PolishService Polish=>polish??=new();
    private readonly HwndSource source;private NativeTray? tray;private ManagerWindow? manager;private TutorialWindow? tutorial;private bool toggling,usingTemplate;
    private string lastUseKey="";private long lastUseTick;
    private Task? menuCapture;private SelectionSnapshot? menuSnapshot;
    public Host(Store store,bool trayEnabled=true)
    {
        Current=this;Store=store;Input=new InputBridge();Ball=new BallWindow(this);Credentials=new CredentialVault(Store.Root);Surfaces=new QuickSurfaces(this);Selection=new SelectionMonitor(this);
        source=new HwndSource(new HwndSourceParameters("PromptFloat.Hotkeys") {Width=0,Height=0,WindowStyle=0,ExtendedWindowStyle=0x80});source.AddHook(Messages);
        Input.TargetChanged+=()=>{panel?.UpdateTarget();};
        if(trayEnabled)CreateTray();Ui.Theme(Store.Settings.Theme);
    }
    public void Start()
    {
        Store.PurgeTrash();Ball.Show();ApplySettings();
        try
        {
            if(FirstRunTeaching.TryReserve(Store))
                Ball.Dispatcher.BeginInvoke(new Action(()=>OpenTutorial(true)));
        }
        catch {Notice("无法记录首次教学状态；软件仍可使用。可从设置 → 关于打开使用教学。");}
    }
    public void OpenTutorial(bool initializeLibrary=false)
    {
        if(tutorial?.IsVisible==true){tutorial.Activate();return;}
        try
        {
            tutorial=new TutorialWindow(this,initializeLibrary);
            tutorial.Closed+=(_,_)=>tutorial=null;
            tutorial.Show();
        }
        catch {Notice("教学窗口暂时无法打开；悬浮球仍可使用。");}
    }
    private void CreateTray()
    {
        tray=new NativeTray(source.Handle,()=>{Ball.Show();Ball.Reveal();},()=>{
            var menu=new ContextMenu {Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint};
            void Add(string label,Action action){var item=new MenuItem {Header=label};item.Click+=(_,_)=>Ui.Run(action);menu.Items.Add(item);}
            Add("显示 / 隐藏悬浮球",ToggleBall);Add("分类与模板管理",OpenManager);Add("设置",()=>OpenSettings());Add("退出",Exit);menu.IsOpen=true;
        });
    }
    public void SaveAgentConfiguration(List<AgentProfile> agents,string? defaultId,IReadOnlyDictionary<string,string> keys)
    {
        // Commit only agent fields: other settings in the open window remain a draft.
        var previous=Store.Settings;
        var updated=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(previous,Store.Json),Store.Json)!;
        updated.Agents=System.Text.Json.JsonSerializer.Deserialize<List<AgentProfile>>(System.Text.Json.JsonSerializer.Serialize(agents,Store.Json),Store.Json)!;
        updated.DefaultAgentId=defaultId;
        Store.SaveSettings(updated);
        try {Credentials.Save(keys);}
        catch {Store.SaveSettings(previous);throw;}
    }
    public ContextMenu Menu(Window owner)
    {
        var menu=new ContextMenu();void Add(string title,Action action){var i=new MenuItem {Header=title};i.Click+=(_,_)=>Ui.Run(action);menu.Items.Add(i);}
        Add("保存选中文字为模板",async()=>await SaveSelectionAsync(owner,false));Add("从剪贴板新建模板",()=>NewFromClipboard(owner));Add("新建模板",()=>NewPrompt(owner));Add("分类与模板管理",OpenManager);Add("设置",()=>OpenSettings());Add("隐藏悬浮球",()=>{panel?.Hide();Surfaces.Close();Ball.Hide();});Add("退出",Exit);return menu;
    }
    public Task TogglePanelAsync()
    {
        if(Surfaces.ActiveMenu?.IsOpen==true){Surfaces.Close();return Task.CompletedTask;}
        if(toggling){Surfaces.ShowMenu();return menuCapture??Task.CompletedTask;}
        menuSnapshot=null;Input.Clear();
        // Show the menu first. Provider latency must not delay this mouse action.
        Surfaces.ShowMenu();
        menuCapture=CaptureMenuAsync();return menuCapture;
    }
    private async Task CaptureMenuAsync()
    {
        toggling=true;
        try
        {
            await Input.CaptureAsync(Store.Settings);
            menuSnapshot=await Input.SnapshotAsync();
            if(Surfaces.ActiveMenu?.IsOpen==true)Surfaces.UpdateMenuSelection(menuSnapshot);
        }
        finally {toggling=false;}
    }
    public async Task UseAsync(Prompt p,bool copy,Window? owner,SelectionSnapshot? snapshot=null)
    {
        var useKey=p.Id+":"+copy;var tick=Environment.TickCount64;
        if(usingTemplate||(useKey==lastUseKey&&tick-lastUseTick<Native.GetDoubleClickTime()))return;
        lastUseKey=useKey;lastUseTick=tick;usingTemplate=true;
        try
        {
            if(!copy&&owner==null&&snapshot==null&&menuCapture!=null)
            {
                await menuCapture;snapshot=menuSnapshot;
            }
            var library=Store.Read();var current=library.Prompts.FirstOrDefault(x=>x.Id==p.Id);if(current==null||current.Deleted!=null||!current.Enabled||!library.Categories.Any(c=>c.Id==current.CategoryId&&c.Enabled)){Notice("模板或分类已停用或删除，请重新打开列表。");return;}
            p=current;
            var body=p.Body;var variables=TemplateEngine.Parse(body,p.Variables);
            if(variables.Count>0){if(owner==null){var filled=await Surfaces.FillAsync(p,copy);if(filled==null)return;body=filled.Value.Body;copy=filled.Value.Copy;}else{var form=new VariableWindow(p,owner,copy);if(form.ShowDialog()!=true)return;body=form.Result!;copy=form.CopyRequested;}}
            else body=TemplateEngine.Render(body,new Dictionary<string,string>(),variables);
            var operation=Guid.NewGuid().ToString("N");
            if(copy){await InputBridge.CopyAsync(body);Store.RecordUse(p.Id,operation,true);Notice("已复制模板。");return;}
            if(snapshot!=null)Input.UseTarget(snapshot.Target);
            var result=await Input.InsertAsync(body,()=>{Surfaces.Close();panel?.Hide();},snapshot);panel?.Hide();
            if(result.Status==InputStatus.Executed){Store.RecordUse(p.Id,operation,false);Notice(result.Message);}
            else if(owner==null)Surfaces.OfferCopy(result.Message,body,()=>Store.RecordUse(p.Id,operation,true));
            else if(Ui.Confirm(result.Message+"\n\n将本次模板结果复制到剪贴板？")){await InputBridge.CopyAsync(body);Store.RecordUse(p.Id,operation,true);Notice("已复制，请手动粘贴。");}
        }
        catch(Exception e){if(owner==null)Surfaces.ShowError(e.Message);else Notice(e.Message);}
        finally {usingTemplate=false;Selection.Refresh();}
    }
    public void NewPrompt(Window? owner=null){panel?.Hide();Surfaces.Close();Input.Clear();if(new EditorWindow(Store,owner:owner?.IsVisible==true?owner:null).ShowDialog()==true)LibraryChanged();}
    public void EditPrompt(Prompt prompt,Window? owner=null){panel?.Hide();Surfaces.Close();Input.Clear();if(new EditorWindow(Store,prompt,owner:owner?.IsVisible==true?owner:null).ShowDialog()==true)LibraryChanged();}
    public void NewFromClipboard(Window? owner=null)
    {
        if(!Clipboard.ContainsText()){Notice("剪贴板没有文字。");return;}var text=Clipboard.GetText();if(string.IsNullOrWhiteSpace(text)){Notice("剪贴板没有文字。");return;}panel?.Hide();Surfaces.Close();Input.Clear();if(new EditorWindow(Store,initialBody:text,owner:owner?.IsVisible==true?owner:null).ShowDialog()==true)LibraryChanged();
    }
    public async Task SaveSelectionAsync(Window? owner=null,bool capture=true)
    {
        try
        {
            if(capture)await Input.CaptureAsync(Store.Settings);var text=await Input.SelectedTextAsync();panel?.Hide();Surfaces.Close();
            if(text==null){var w=Ui.Window("未读取到选中文字",490,270,owner?.IsVisible==true?owner:null);var content=Ui.Stack(Ui.Text("请复制文字后重试",21,true),Ui.Text("目标软件未提供可读取的选区，或当前没有选中文字。不会自动使用旧剪贴板。",13,false,"Muted"),Ui.Row(Ui.Button("取消",()=>w.Close()),Ui.Button("读取剪贴板",()=>{w.Close();NewFromClipboard();},true)));content.Margin=new Thickness(20);w.Content=content;w.ShowDialog();return;}
            Input.Clear();if(new EditorWindow(Store,initialBody:text,owner:owner?.IsVisible==true?owner:null).ShowDialog()==true)LibraryChanged();
        }
        catch(Exception e){Notice(e.Message);}
    }
    public void OpenManager(){panel?.Hide();Surfaces.Close();Input.Clear();if(manager?.IsVisible==true){manager.Activate();return;}manager=new ManagerWindow(this);manager.Closed+=(_,_)=>manager=null;manager.Show();}
    public void SaveSnapshot(SelectionSnapshot snapshot){Surfaces.Close();Input.Clear();if(new EditorWindow(Store,initialBody:snapshot.Text).ShowDialog()==true)LibraryChanged();}
    public void OpenSettings(){Surfaces.Close();var lastTargetApp=Input.Target?.App;panel?.Hide();Input.Clear();new SettingsWindow(this,lastTargetApp:lastTargetApp).ShowDialog();}
    public void LibraryChanged(){if(manager?.IsVisible==true)manager.Refresh(); /* Panel keeps its snapshot until reopened or category changes. */}
    public void ToggleBall(){panel?.Hide();Surfaces.Close();if(Ball.IsVisible)Ball.Hide();else {Ball.Show();Ball.Reveal();Ball.Clamp(false);}}
    public void ApplySettings()
    {
        Ui.Theme(Store.Settings.Theme);Selection.Apply();Ball.ApplySettings();Ball.Reveal();if(Ball.IsVisible)Ball.Clamp(false);
        if(panel!=null){var w=Store.Settings.PanelWidth;var h=Store.Settings.PanelHeight;panel.Width=w;panel.Height=h;}
        var failures=new List<string>();for(var id=1;id<=3;id++)Native.UnregisterHotKey(source.Handle,id);
        var values=new[]{Store.Settings.PanelHotkey,Store.Settings.SaveHotkey,Store.Settings.HideHotkey};
        for(var i=0;i<values.Length;i++){try{var h=Native.ParseHotkey(values[i]);if(!Native.RegisterHotKey(source.Handle,i+1,h.modifiers,h.key))failures.Add(values[i]);}catch{failures.Add(values[i]);}}
        if(failures.Count>0)Notice("快捷键未注册："+string.Join("、",failures)+"。请在设置中修改，鼠标入口仍可用。");
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(enabled)key.SetValue("PromptFloat","\""+Environment.ProcessPath+"\"");else key.DeleteValue("PromptFloat",false);
    }
    private IntPtr Messages(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message==NativeTray.Callback){handled=true;tray?.Message(l);return IntPtr.Zero;}
        if(tray!=null&&(uint)message==tray.ExplorerRestartMessage){tray.Add();}
        if(message==Native.WM_HOTKEY){handled=true;switch(w.ToInt32()){case 1:if(!Ball.IsVisible)Ball.Show();_ = TogglePanelAsync();break;case 2:_ = SaveSelectionAsync();break;case 3:ToggleBall();break;}}return IntPtr.Zero;
    }
    public void Notice(string text){if(tray!=null)tray.Notice(text);else Debug.WriteLine(text);}
    public void Exit(){Exiting=true;Ui.Run(()=>Store.SaveSettings(Store.Settings));Application.Current.Shutdown();}
    public void Dispose(){Exiting=true;for(var i=1;i<=3;i++)Native.UnregisterHotKey(source.Handle,i);Selection.Dispose();Surfaces.Dispose();polish?.Dispose();source.Dispose();Input.Dispose();Ball.DisposeEvents();tray?.Dispose();panel?.Close();Ball.Close();}
}
