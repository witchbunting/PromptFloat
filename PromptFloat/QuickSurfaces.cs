using System.Windows.Controls.Primitives;
using System.Windows.Threading;
namespace PromptFloat;

internal sealed class QuickSurfaces : IDisposable
{
    private readonly Host host;
    private ContextMenu? menu;
    private MenuKeys? menuKeys;
    private MenuItem? keyboardGroup;
    private int keyboardIndex=-1;
    private Popup? popup;
    private Border? root;
    private string mode="";
    private IntPtr menuTarget;
    private SelectionSnapshot? selection;
    private CancellationTokenSource? request;
    private long generation;
    private bool adopting;
    private SelectionSnapshot? resultSnapshot;
    private TextBlock? resultHeading;
    private readonly List<Button> resultActions=[];
    private sealed class LabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>value is Prompt p?Label(p):"";
        public object ConvertBack(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
    }
    private readonly HashSet<string> adopted=[];
    public bool IsOpen=>menu?.IsOpen==true||popup?.IsOpen==true;
    public bool Editing=>menu?.IsOpen==true||mode is "search" or "variables";
    public QuickSurfaces(Host host)
    {
        this.host=host;host.Input.ForegroundChanged+=ExternalForeground;
        host.Ball.LocationChanged+=BallMoved;host.Ball.SizeChanged+=BallResized;
    }
    private void BallMoved(object? sender,EventArgs e)=>Reposition();
    private void BallResized(object sender,SizeChangedEventArgs e)=>Reposition();
    public void BeginBallDrag()
    {
        // Menus and editable forms dismiss on drag; active polish bubbles stay
        // attached to the same popup and request, without closing or reopening.
        if(menu?.IsOpen==true||mode is "search" or "variables")Close();
    }
    private void Reposition()
    {
        if(popup?.IsOpen!=true)return;
        if(root?.Child is ScrollViewer scroll)scroll.MaxHeight=WorkHeight()-20;
        // WPF Popup does not reposition when its target window moves. Changing
        // an offset recomputes placement/edge fitting without recreating content.
        var offset=popup.HorizontalOffset;
        popup.SetCurrentValue(Popup.HorizontalOffsetProperty,offset+0.01);
        popup.SetCurrentValue(Popup.HorizontalOffsetProperty,offset);
    }
    private void ExternalForeground()
    {
        if(menu?.IsOpen==true&&Native.GetForegroundWindow()!=menuTarget&&Native.IsExternal(Native.GetForegroundWindow()))Close();
    }
    internal bool InsertBubbleVisible=>mode=="insert"&&popup?.IsOpen==true;
    internal ContextMenu? ActiveMenu=>menu;
    internal FrameworkElement? ActiveVisual=>root;
    internal static string Label(Prompt p)=>string.IsNullOrWhiteSpace(p.Note)?p.Title:p.Note.Trim().Replace("\r"," ").Replace("\n"," ");
    public void Close()
    {
        menuKeys?.Dispose();menuKeys=null;keyboardGroup=null;keyboardIndex=-1;
        if(menu!=null)menu.IsOpen=false;
        if(popup!=null){popup.IsOpen=false;popup.Child=null;popup=null;}
        mode="";root=null;menu=null;resultSnapshot=null;resultHeading=null;resultActions.Clear();
    }
    public void ToggleMenu()
    {
        if(menu?.IsOpen==true){Close();return;}
        ShowMenu(host.Selection.Current);
    }
    public void ShowMenu(SelectionSnapshot? snapshot=null)
    {
        Close();selection=snapshot;
        if(snapshot?.Valid==true)host.Input.UseTarget(snapshot.Target);
        menuTarget=host.Input.Target?.Window??host.Input.LastExternal;
        var library=host.Store.Read();
        var enabled=library.Categories.Where(c=>c.Enabled).Select(c=>c.Id).ToHashSet();
        var prompts=library.Prompts.Where(p=>p.Deleted==null&&p.Enabled&&enabled.Contains(p.CategoryId)).ToList();
        menu=new ContextMenu {PlacementTarget=host.Ball,Placement=PlacementMode.Right,MaxHeight=WorkHeight(),MinWidth=180,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13};
        menu.SetResourceReference(Control.BackgroundProperty,"Surface");menu.SetResourceReference(Control.ForegroundProperty,"Ink");
        void Group(string name,IEnumerable<Prompt> items)
        {
            var frozen=items.ToList();var group=new MenuItem {Header=name,IsEnabled=frozen.Count>0};
            if(frozen.Count>0)
            {
                group.Items.Add(new MenuItem {Header="载入…"});
                bool loaded=false;group.SubmenuOpened+=(_,_)=>{if(loaded)return;loaded=true;group.Items.Clear();foreach(var p in frozen)group.Items.Add(TemplateItem(p,snapshot));};
            }
            group.MouseEnter+=(_,_)=>{if(keyboardGroup!=group){keyboardGroup=group;keyboardIndex=-1;}};
            menu.Items.Add(group);
        }
        Group("★ 收藏",TemplateEngine.Sorted(prompts.Where(p=>p.Favorite)));
        Group("◷ 最近使用",prompts.Where(p=>p.LastUsed!=null).OrderByDescending(p=>p.LastUsed).ThenBy(p=>p.Id).Take(20));
        menu.Items.Add(new Separator());
        foreach(var category in library.Categories.Where(c=>c.Enabled).OrderBy(c=>c.Order).ThenBy(c=>c.Id))
            Group(category.Name,TemplateEngine.Sorted(prompts.Where(p=>p.CategoryId==category.Id)));
        menu.Items.Add(new Separator());
        var search=new MenuItem {Header="搜索提示词…"};search.Click+=(_,_)=>ShowSearch(selection);menu.Items.Add(search);
        menu.IsOpen=true;menuKeys=new MenuKeys(Navigate);menu.Closed+=(_,_)=>{menuKeys?.Dispose();menuKeys=null;host.Selection.Refresh();};
    }
    internal void UpdateMenuSelection(SelectionSnapshot? snapshot){selection=snapshot;}
    private void Navigate(int key)
    {
        if(menu?.IsOpen!=true)return;
        if(key==0x1B){Close();return;}
        ItemsControl container=keyboardGroup?.IsSubmenuOpen==true?keyboardGroup:menu;
        var entries=container!.Items.OfType<MenuItem>().Where(i=>i.IsEnabled).ToList();
        if(entries.Count==0)return;
        if(key is 0x26 or 0x28)
        {
            keyboardIndex=(keyboardIndex+(key==0x28?1:-1)+entries.Count)%entries.Count;
            entries[keyboardIndex].Focus();entries[keyboardIndex].BringIntoView();return;
        }
        if(key==0x25&&keyboardGroup!=null){keyboardGroup.IsSubmenuOpen=false;keyboardGroup=null;keyboardIndex=-1;return;}
        var selected=entries[Math.Clamp(keyboardIndex,0,entries.Count-1)];
        if(key==0x27||key==0x0D&&selected.HasItems){keyboardGroup=selected;selected.IsSubmenuOpen=true;keyboardIndex=-1;}
        else if(key==0x0D)selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,selected));
    }
    private MenuItem TemplateItem(Prompt p,SelectionSnapshot? snapshot)
    {
        var header=new TextBlock {Text=Label(p),TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=300};
        var item=new MenuItem {Header=header,ToolTip=Label(p)+"\n"+p.Title,Tag=p.Id};
        item.Click+=async(_,e)=>{e.Handled=true;var source=selection;Close();await host.UseAsync(p,false,null,source);};
        return item;
    }
    private double WorkHeight()
    {
        var area=Native.WorkArea(new WindowInteropHelper(host.Ball).EnsureHandle());var dpi=VisualTreeHelper.GetDpi(host.Ball);
        return Math.Max(120,(area.Bottom-area.Top)/dpi.DpiScaleY-30);
    }
    private void Show(UIElement content,string kind,bool interactive=false)
    {
        Close();mode=kind;
        var scroll=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=WorkHeight()-20,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        root=Ui.Card(scroll,new Thickness(12));root.Width=kind is "actions" or "insert"?double.NaN:370;root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,new FontFamily("Microsoft YaHei UI"));root.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Ink");
        popup=new Popup {Child=root,PlacementTarget=host.Ball,Placement=PlacementMode.Right,HorizontalOffset=6,AllowsTransparency=true,StaysOpen=kind is "actions" or "insert" or "progress" or "results",Focusable=interactive};
        PresentationSource.AddSourceChangedHandler(root,(_,e)=>{
            if(e.NewSource is HwndSource source)source.AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==Native.WM_MOUSEACTIVATE&&!interactive){handled=true;return new IntPtr(Native.MA_NOACTIVATE);}return IntPtr.Zero;});
        });
        root.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Cancel();e.Handled=true;}};
        var opened=popup;opened.Closed+=(_,_)=>{if(popup==opened){mode="";root=null;}};
        popup.IsOpen=true;
        if(interactive){host.Ball.Activate();Native.SetForegroundWindow(new WindowInteropHelper(host.Ball).Handle);}
    }
    public void SelectionAvailable(SelectionSnapshot snapshot)
    {
        if(mode is "progress" or "results"){selection?.Invalidate();RefreshResultActions();return;}
        if(Editing||adopting||!host.Ball.IsVisible)return;
        selection=snapshot;host.Ball.Reveal();
        var row=Ui.Row(
            Ui.Button("插入",()=>ShowMenu(snapshot)),
            Ui.Button("润色",()=>_ = PolishAsync(snapshot)),
            Ui.Button("保存提示词",()=>host.SaveSnapshot(snapshot)),
            Ui.Button("×",()=>Close()));
        Show(row,"actions");
    }
    public void SelectionCleared()
    {
        selection?.Invalidate();RefreshResultActions();
        InputAvailable(host.Input.Target);
    }
    public void InputAvailable(InputTarget? target)
    {
        selection?.Invalidate();RefreshResultActions();selection=null;
        // Selection/results/variables take priority over the caret-only entry.
        if(Editing||adopting||mode is "progress" or "results")return;
        if(!host.Store.Settings.AutoSelectionActions||!host.Ball.IsVisible||
            target?.Editable!=true||!Native.IsWindow(target.Window)||
            Native.GetForegroundWindow()!=target.Window)
        {
            if(mode is "actions" or "insert")Close();
            return;
        }
        if(mode=="insert"||mode!=""&&mode!="actions")return;
        host.Ball.Reveal();
        // Recapture on click, so typing or moving the caret cannot reuse old ranges.
        var insert=Ui.Button("插入",()=>_ = host.TogglePanelAsync());
        insert.Focusable=false;
        Show(insert,"insert");
    }
    public void ShowSearch(SelectionSnapshot? snapshot)
    {
        selection=snapshot;
        var library=host.Store.Read();var enabled=library.Categories.Where(c=>c.Enabled).Select(c=>c.Id).ToHashSet();
        var source=library.Prompts.Where(p=>p.Enabled&&p.Deleted==null&&enabled.Contains(p.CategoryId)).ToList();
        var search=Ui.Field();var list=new ListBox {MaxHeight=320};
        VirtualizingStackPanel.SetIsVirtualizing(list,true);
        var template=new DataTemplate(typeof(Prompt));var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(".") {Converter=new LabelConverter()});text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);template.VisualTree=text;list.ItemTemplate=template;
        var count=Ui.Text("",12,false,"Muted");
        void Refresh(){list.ItemsSource=TemplateEngine.Search(source,search.Text).ToList();count.Text=list.Items.Count+" 个结果";if(list.Items.Count>0)list.SelectedIndex=0;}
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=(_,_)=>{timer.Stop();Refresh();};
        search.TextChanged+=(_,_)=>{timer.Stop();timer.Start();};
        async Task Use(){if(list.SelectedItem is Prompt p){timer.Stop();Close();await host.UseAsync(p,false,null,snapshot);}}
        list.PreviewMouseLeftButtonUp+=async(_,e)=>{var node=e.OriginalSource as DependencyObject;while(node!=null){if(node is ScrollBar)return;node=VisualTreeHelper.GetParent(node);}await Use();};
        search.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Use();}else if(e.Key is Key.Down or Key.Up){list.SelectedIndex=Math.Clamp(list.SelectedIndex+(e.Key==Key.Down?1:-1),0,Math.Max(0,list.Items.Count-1));e.Handled=true;}};
        Show(Ui.Stack(Ui.Text("搜索提示词",15,true),search,count,list,Ui.Button("关闭",Close)),"search",true);Refresh();search.Focus();
        popup!.Closed+=(_,_)=>timer.Stop();
    }
    public Task<(string Body,bool Copy)?> FillAsync(Prompt prompt,bool copy)
    {
        var done=new TaskCompletionSource<(string,bool)?>();
        var fields=TemplateEngine.Parse(prompt.Body,prompt.Variables);
        var inputs=new Dictionary<string,TextBox>();var layout=Ui.Stack(Ui.Text(Label(prompt),16,true));
        var preview=Ui.Field("",true,130);preview.IsReadOnly=true;var error=Ui.Text("",12,false,"Muted");
        string Render()=>TemplateEngine.Render(prompt.Body,inputs.ToDictionary(x=>x.Key,x=>x.Value.Text),fields);
        void Update(){try{preview.Text=Render();error.Text="";}catch(ArgumentException){preview.Text="填写必填项后显示预览。";}}
        foreach(var f in fields){var input=Ui.Field(f.Default,true,70);inputs.Add(f.Name,input);layout.Children.Add(Ui.Labeled(f.Name+(f.Required?" *":""),input));input.TextChanged+=(_,_)=>Update();}
        void Submit(bool copied){try{var body=Render();done.TrySetResult((body,copied));Close();}catch(Exception e){error.Text=e.Message;}}
        layout.Children.Add(Ui.Labeled("最终正文",preview));layout.Children.Add(error);
        layout.Children.Add(Ui.Row(Ui.Button("取消",()=>{done.TrySetResult(null);Close();}),Ui.Button("复制结果",()=>Submit(true)),Ui.Button(copy?"复制":"填入",()=>Submit(copy),true)));
        Show(layout,"variables",true);popup!.Closed+=(_,_)=>done.TrySetResult(null);Update();inputs.Values.FirstOrDefault()?.Focus();return done.Task;
    }
    public void OfferCopy(string message,string body,Action? copied=null)
    {
        Show(Ui.Stack(Ui.Text(message,13),Ui.Row(Ui.Button("复制",async()=>{try{await InputBridge.CopyAsync(body);copied?.Invoke();Close();}catch{ShowError("复制失败，请稍后重试。");}}),Ui.Button("关闭",Close))),"fallback");
    }
    public void ShowError(string message)=>Show(Ui.Stack(Ui.Text(message,13),Ui.Button("关闭",Close)),"error");
    public async Task PolishAsync(SelectionSnapshot snapshot,AgentProfile? requested=null)
    {
        if(mode=="progress")return;
        if(string.IsNullOrWhiteSpace(snapshot.Text)){ShowError("请选择需要润色的文字。");return;}
        var configured=requested==null
            ?host.Store.Settings.Agents.FirstOrDefault(a=>a.Id==host.Store.Settings.DefaultAgentId)??host.Store.Settings.Agents.FirstOrDefault()
            :host.Store.Settings.Agents.FirstOrDefault(a=>a.Id==requested.Id);
        var agent=configured==null?null:System.Text.Json.JsonSerializer.Deserialize<AgentProfile>(System.Text.Json.JsonSerializer.Serialize(configured,Store.Json),Store.Json);
        if(requested!=null&&agent==null){ShowError("该智能体已删除，请重新选择智能体。");return;}
        if(agent==null){Show(Ui.Stack(Ui.Text("请先配置润色智能体。"),Ui.Button("打开设置",()=>{Close();host.OpenSettings();}),Ui.Button("关闭",Close)),"error");return;}
        CancelRequest();selection=snapshot;var version=++generation;request=new CancellationTokenSource();var token=request.Token;
        var choose=new ComboBox {ItemsSource=host.Store.Settings.Agents,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=agent.Id,MinWidth=150};
        choose.SelectionChanged+=(_,_)=>{if(choose.SelectedItem is AgentProfile other&&other.Id!=agent.Id)_ = PolishAsyncRestart(snapshot,other);};
        Show(Ui.Stack(Ui.Text("正在生成三个润色版本…",14,true),choose,Ui.Button("取消",Cancel)),"progress");
        try
        {
            var variants=await host.Polish.GenerateAsync(agent,host.Credentials.Get(agent.Id),snapshot.Text,token);
            if(version!=generation||token.IsCancellationRequested)return;
            if(!await host.Input.ValidateSelectionAsync(snapshot))snapshot.Invalidate();
            if(version!=generation||token.IsCancellationRequested)return;
            ShowResults(snapshot,variants);
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(version==generation&&!token.IsCancellationRequested)Show(Ui.Stack(Ui.Text(e.Message),Ui.Row(Ui.Button("重试",()=>_ = PolishAsync(snapshot,agent)),Ui.Button("关闭",Close))),"error");}
    }
    private async Task PolishAsyncRestart(SelectionSnapshot snapshot,AgentProfile agent){CancelRequest();mode="";await PolishAsync(snapshot,agent);}
    internal void ShowResults(SelectionSnapshot snapshot,IReadOnlyList<PolishVariant> variants)
    {
        selection=snapshot;
        var heading=Ui.Text("",15,true);
        var actions=new List<Button>();
        var stack=Ui.Stack(heading);
        foreach(var variant in variants)
        {
            var summary=Ui.Text(variant.Text.Length>160?variant.Text[..160]+"…":variant.Text,13);
            var full=Ui.Text(variant.Text,13);
            var detail=new ScrollViewer {Content=full,MaxHeight=260,Visibility=Visibility.Collapsed,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var action=Ui.Button("替换",async()=>await UseVariantAsync(snapshot,variant.Text));
            action.Focusable=false;actions.Add(action);
            var bubble=Ui.Card(Ui.Stack(Ui.Text(variant.Name,14,true),summary,detail,action),new Thickness(10));
            bubble.Cursor=Cursors.Hand;
            bubble.MouseEnter+=(_,_)=>{summary.Visibility=Visibility.Collapsed;detail.Visibility=Visibility.Visible;};
            bubble.MouseLeave+=(_,_)=>{summary.Visibility=Visibility.Visible;detail.Visibility=Visibility.Collapsed;};
            bubble.MouseLeftButtonUp+=async(_,e)=>{
                if(e.OriginalSource is DependencyObject node){for(var current=node;current!=null;current=VisualTreeHelper.GetParent(current))if(current is Button||current is System.Windows.Controls.Primitives.ScrollBar)return;}
                e.Handled=true;await UseVariantAsync(snapshot,variant.Text);
            };
            stack.Children.Add(bubble);
        }
        stack.Children.Add(Ui.Button("关闭",Cancel));Show(stack,"results");
        resultSnapshot=snapshot;resultHeading=heading;resultActions.AddRange(actions);RefreshResultActions();
    }
    private void RefreshResultActions()
    {
        if(mode!="results"||resultSnapshot==null)return;
        var canReplace=resultSnapshot.Valid&&!resultSnapshot.Target.CopyOnly;
        foreach(var action in resultActions)
        {
            action.Content=canReplace?"替换":"复制";
            action.ToolTip=canReplace?"替换原选区；点击时再次校验原文和选区。":"原选区已变化或当前位置仅支持复制。";
        }
        if(resultHeading!=null)resultHeading.Text=canReplace?"选择一个版本替换原选区":
            resultSnapshot.Target.CopyOnly?"当前位置仅支持复制":"原文或选区已改变，结果只能复制";
    }
    private async Task UseVariantAsync(SelectionSnapshot snapshot,string body)
    {
        if(snapshot.Valid&&!snapshot.Target.CopyOnly)await AdoptAsync(snapshot,body);
        else try{await InputBridge.CopyAsync(body);}catch{ShowError("复制失败。");}
    }
    internal async Task AdoptAsync(SelectionSnapshot snapshot,string body)
    {
        if(adopting||adopted.Contains(snapshot.Id))return;adopting=true;
        try
        {
            if(!await host.Input.ValidateSelectionAsync(snapshot)){snapshot.Invalidate();OfferCopy("原文、选区或输入目标已改变，结果只能复制。",body);return;}
            host.Input.UseTarget(snapshot.Target);
            var result=await host.Input.InsertAsync(body,Close,snapshot);
            if(result.Status==InputStatus.Executed){adopted.Add(snapshot.Id);snapshot.Invalidate();Close();host.Notice(result.Message);}
            else OfferCopy(result.Message,body);
        }
        finally {adopting=false;}
    }
    private void CancelRequest(){++generation;request?.Cancel();request?.Dispose();request=null;}
    public void Cancel(){CancelRequest();Close();}
    public void Dispose(){host.Input.ForegroundChanged-=ExternalForeground;host.Ball.LocationChanged-=BallMoved;host.Ball.SizeChanged-=BallResized;Cancel();adopted.Clear();}
}
