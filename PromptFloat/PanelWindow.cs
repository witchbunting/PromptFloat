using System.Windows.Data;
using System.Windows.Threading;
namespace PromptFloat;

internal sealed class PanelWindow : Window
{
    private readonly Host host;private Library snapshot=new();
    private readonly ListBox categories=new(),prompts=new();private readonly TextBox search=Ui.Field();
    private readonly CheckBox local=new() {Content="当前分类"};private readonly ComboBox tag=new() {Width=108,Margin=new Thickness(3)};
    private readonly TextBlock target=Ui.Text("",11,false,"Muted"),count=Ui.Text("",11,false,"Muted");
    private readonly DispatcherTimer debounce=new() {Interval=TimeSpan.FromMilliseconds(80)};private string category="@all";private bool reloading;
    public PanelWindow(Host host)
    {
        this.host=host;Title="PromptFloat · 选择提示词";Width=host.Store.Settings.PanelWidth;Height=host.Store.Settings.PanelHeight;MinWidth=480;MinHeight=360;MaxWidth=1600;MaxHeight=1200;ShowInTaskbar=false;ShowActivated=false;Topmost=true;WindowStyle=WindowStyle.ToolWindow;
        var root=new DockPanel {Margin=new Thickness(12)};
        var head=Ui.Row(Ui.Text("PromptFloat",19,true),Ui.Text("  让好提示词随手可用",11,false,"Muted"));DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
        var tools=new Grid {Margin=new Thickness(0,8,0,8)};tools.ColumnDefinitions.Add(new ColumnDefinition());tools.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});search.ToolTip="搜索标题、标签、备注和正文";
        var searchBox=new Grid();searchBox.Children.Add(search);var hint=Ui.Text("搜索提示词、标签或正文…",12,false,"Muted");hint.Margin=new Thickness(9,0,0,0);hint.VerticalAlignment=VerticalAlignment.Center;hint.IsHitTestVisible=false;searchBox.Children.Add(hint);search.TextChanged+=(_,_)=>hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;tools.Children.Add(searchBox);
        var buttons=Ui.Row(tag,Ui.Button("＋",()=>host.NewPrompt(this)));Grid.SetColumn(buttons,1);tools.Children.Add(buttons);DockPanel.SetDock(tools,Dock.Top);root.Children.Add(tools);
        var footer=new DockPanel();var manage=Ui.Button("管理",host.OpenManager);DockPanel.SetDock(manage,Dock.Right);footer.Children.Add(manage);footer.Children.Add(Ui.Stack(target,count));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(126)});columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel {Margin=new Thickness(0,0,8,0)};DockPanel.SetDock(local,Dock.Bottom);left.Children.Add(local);left.Children.Add(categories);columns.Children.Add(left);
        prompts.SetValue(VirtualizingPanel.IsVirtualizingProperty,true);prompts.SetValue(VirtualizingPanel.VirtualizationModeProperty,VirtualizationMode.Recycling);prompts.SetValue(ScrollViewer.CanContentScrollProperty,true);
        var template=new DataTemplate(typeof(Prompt));var factory=new FrameworkElementFactory(typeof(PromptCard));factory.SetBinding(FrameworkElement.DataContextProperty,new Binding());template.VisualTree=factory;prompts.ItemTemplate=template;Grid.SetColumn(prompts,1);columns.Children.Add(prompts);root.Children.Add(columns);Content=root;
        categories.SelectionChanged+=(_,_)=>{if(reloading)return;category=(categories.SelectedItem as CategoryChoice)?.Id??"@all";snapshot=host.Store.Read();UpdateItems();};
        search.TextChanged+=(_,_)=>{debounce.Stop();debounce.Start();};debounce.Tick+=(_,_)=>{debounce.Stop();UpdateItems();};tag.SelectionChanged+=(_,_)=>UpdateItems();local.Click+=(_,_)=>UpdateItems();
        PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.Escape){Hide();e.Handled=true;}
            else if(e.Key==Key.Enter&&prompts.SelectedItem is Prompt p){_ = host.UseAsync(p,false,this);e.Handled=true;}
            else if(e.Key is Key.Down or Key.Up && (search.IsKeyboardFocusWithin||prompts.IsKeyboardFocusWithin)){prompts.SelectedIndex=Math.Clamp(prompts.SelectedIndex+(e.Key==Key.Down?1:-1),0,Math.Max(0,prompts.Items.Count-1));if(prompts.SelectedItem!=null)prompts.ScrollIntoView(prompts.SelectedItem);e.Handled=true;}
        };
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(Native.IsExternal(Native.GetForegroundWindow()))Hide();});
        Closing+=(_,e)=>{if(!host.Exiting){e.Cancel=true;Hide();}};
        SizeChanged+=(_,_)=>{if(IsLoaded&&!host.Exiting){host.Store.Settings.PanelWidth=Math.Clamp(Width,480,1600);host.Store.Settings.PanelHeight=Math.Clamp(Height,360,1200);}};
    }
    public void Reload()
    {
        reloading=true;
        snapshot=host.Store.Read();categories.Items.Clear();foreach(var c in new[]{new CategoryChoice("@all","全部模板"),new CategoryChoice("@favorite","★ 收藏"),new CategoryChoice("@recent","最近使用")})categories.Items.Add(c);
        foreach(var c in snapshot.Categories.Where(c=>c.Enabled).OrderBy(c=>c.Order))categories.Items.Add(new CategoryChoice(c.Id,c.Name));
        categories.SelectedItem=categories.Items.Cast<CategoryChoice>().FirstOrDefault(c=>c.Id==category)??categories.Items[0];
        var currentTag=tag.SelectedItem as string;var tags=new[]{"全部标签"}.Concat(snapshot.Prompts.Where(p=>p.Deleted==null&&p.Enabled).SelectMany(p=>p.Tags).Distinct().Order()).ToList();tag.ItemsSource=tags;tag.SelectedItem=tags.Contains(currentTag??"")?currentTag:tags[0];category=(categories.SelectedItem as CategoryChoice)?.Id??"@all";reloading=false;UpdateItems();UpdateTarget();
    }
    private void UpdateItems()
    {
        if(reloading)return;
        var enabled=snapshot.Categories.Where(c=>c.Enabled).Select(c=>c.Id).ToHashSet();IEnumerable<Prompt> items=snapshot.Prompts.Where(p=>p.Deleted==null&&p.Enabled&&enabled.Contains(p.CategoryId));
        if(category=="@favorite")items=items.Where(p=>p.Favorite);
        else if(category=="@recent")items=items.Where(p=>p.LastUsed!=null).OrderByDescending(p=>p.LastUsed).Take(20);
        else if(!category.StartsWith("@")&&(string.IsNullOrWhiteSpace(search.Text)||local.IsChecked==true))items=items.Where(p=>p.CategoryId==category);
        if(tag.SelectedItem is string t&&t!="全部标签")items=items.Where(p=>p.Tags.Contains(t));
        var result=category=="@recent"&&string.IsNullOrWhiteSpace(search.Text)?items.ToList():TemplateEngine.Search(items,search.Text).ToList();prompts.ItemsSource=result;prompts.SelectedIndex=result.Count>0?0:-1;count.Text=result.Count==0?"暂无模板 · 点 ＋ 新建或到管理中导入":$"{result.Count} 个模板 · Enter 使用 · Esc 收起";
    }
    public void UpdateTarget()=>target.Text=host.Input.Target is { } t?$"目标：{t.App}"+(t.CopyOnly?" · 仅复制":" · 光标插入 / 替换选区"):"请选择输入框 · 或复制后手动粘贴";
}
internal sealed class PromptCard : Border
{
    public PromptCard(){Margin=new Thickness(0,2,0,5);Padding=new Thickness(8);CornerRadius=new CornerRadius(8);SetResourceReference(BackgroundProperty,"Bg");DataContextChanged+=(_,_)=>Build();}
    private void Build()
    {
        if(DataContext is not Prompt p)return;var host=Host.Current;
        var title=new Button {Content=p.Title,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0),Padding=new Thickness(0,2,0,2),Background=Brushes.Transparent,BorderThickness=new Thickness(0),FontWeight=FontWeights.SemiBold,ToolTip="使用模板"};title.Click+=async(_,_)=>await host.UseAsync(p,false,host.Panel);
        var note=Ui.Text(p.Note,11,false,"Muted");note.TextTrimming=TextTrimming.CharacterEllipsis;note.TextWrapping=TextWrapping.NoWrap;
        var tags=Ui.Text(string.Join(" · ",p.Tags),10,false,"Muted");tags.TextTrimming=TextTrimming.CharacterEllipsis;tags.TextWrapping=TextWrapping.NoWrap;
        var star=Ui.Button(p.Favorite?"★":"☆",()=>{Ui.Run(()=>{host.Store.Batch([p.Id],x=>x.Favorite=!p.Favorite);p.Favorite=!p.Favorite;Build();host.LibraryChanged();});});star.ToolTip="收藏";
        var more=new Button {Content="···"};var menu=new ContextMenu();
        void Add(string label,Action action){var item=new MenuItem {Header=label};item.Click+=(_,_)=>Ui.Run(action);menu.Items.Add(item);}
        Add("编辑",()=>host.EditPrompt(p,host.Panel));Add("复制为新模板",()=>{var clone=p.Clone();clone.Id=Guid.NewGuid().ToString("N");clone.Source="user";clone.SourceId=null;clone.InsertCount=clone.CopyCount=0;clone.LastUsed=null;clone.Created=DateTimeOffset.UtcNow;host.EditPrompt(clone,host.Panel);});
        Add("移动分类",()=>{var c=Ui.PickCategory(host.Panel,host.Store,"移动模板");if(c!=null){host.Store.Batch([p.Id],x=>x.CategoryId=c);host.LibraryChanged();}});
        Add("停用",()=>{host.Store.Batch([p.Id],x=>x.Enabled=false);IsEnabled=false;host.LibraryChanged();});
        Add("删除",()=>{if(Ui.Confirm("将模板放入回收站？",host.Panel)){host.Store.Batch([p.Id],x=>x.Deleted=DateTimeOffset.UtcNow);IsEnabled=false;host.LibraryChanged();}});
        more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};
        var actions=Ui.Row(Ui.Button("填入",async()=>await host.UseAsync(p,false,host.Panel),true),Ui.Button("预览",()=>Ui.Preview(host.Panel,p)),Ui.Button("复制",async()=>await host.UseAsync(p,true,host.Panel)),star,more);
        Child=Ui.Stack(title,note,tags,Ui.Row(Ui.Text($"使用 {p.Uses} 次",10,false,"Muted"),actions));
    }
}
