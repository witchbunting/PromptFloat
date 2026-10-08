using System.Windows.Threading;
namespace PromptFloat;

internal sealed class TutorialWindow : Window
{
    private readonly Host host;
    private readonly bool initializeLibrary;
    private readonly StackPanel content=new();
    private readonly List<Button> steps=[];
    private readonly TextBlock progress;
    private readonly Button previous,next;
    private readonly Dictionary<string,CheckBox> categories=[];
    private int page;
    private bool completed;
    private static readonly string[] Titles=["认识悬浮球","插入提示词","选区与润色","准备开始"];

    public TutorialWindow(Host host,bool initializeLibrary=false)
    {
        this.host=host;this.initializeLibrary=initializeLibrary;
        Title="PromptFloat · 使用教学";Width=900;Height=730;MinWidth=760;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var shell=new Grid {Margin=new Thickness(22)};
        shell.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(190)});
        shell.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        var rail=Ui.Stack(Ui.Text("PromptFloat",24,true),Ui.Text("版本 "+AppVersion.Text,12,false,"Muted"),Ui.Text("几分钟，熟悉你的\n提示词助手。",14));
        rail.Margin=new Thickness(0,0,20,0);
        for(var i=0;i<Titles.Length;i++)
        {
            var index=i;var button=Ui.Button((i+1)+"  "+Titles[i],()=>ShowPage(index));
            button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,8,0,0);
            steps.Add(button);rail.Children.Add(button);
        }
        rail.Children.Add(Ui.Text("\n本地模板 · 按需联网\n插入文字，不自动发送",12,false,"Muted"));
        shell.Children.Add(rail);
        var dock=new DockPanel();Grid.SetColumn(dock,1);shell.Children.Add(dock);
        var header=Ui.Stack(Ui.Text(initializeLibrary?"欢迎使用，先认识几个常用操作":"随时回看基本操作",22,true),
            Ui.Text("悬浮球已启动。你可以移动或关闭这个教学窗口，继续使用软件。",12,false,"Muted"));
        header.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        progress=Ui.Text("",12,false,"Muted");previous=Ui.Button("上一步",()=>ShowPage(page-1));
        next=Ui.Button("下一步",Advance,true);
        var footer=new DockPanel {Margin=new Thickness(0,14,0,0)};
        var navigation=Ui.Row(previous,next);DockPanel.SetDock(navigation,Dock.Right);footer.Children.Add(navigation);
        footer.Children.Add(Ui.Stack(progress,Ui.Button(initializeLibrary?"跳过，不导入模板":"关闭教学",()=>{Complete(false);Close();})));
        DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Content=shell;ShowPage(0);
        Closing+=(_,_)=>{if(initializeLibrary&&!completed&&!host.Exiting)Ui.Run(()=>Complete(false));};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Complete(false);Close();e.Handled=true;}};
    }
    private void Card(string title,string body)=>content.Children.Add(Ui.Card(Ui.Stack(Ui.Text(title,16,true),Ui.Text(body,13))));
    private void ShowPage(int index)
    {
        page=Math.Clamp(index,0,Titles.Length-1);content.Children.Clear();
        for(var i=0;i<steps.Count;i++){steps[i].SetResourceReference(Control.BackgroundProperty,i==page?"AccentSoft":"Surface");steps[i].SetResourceReference(Control.ForegroundProperty,"Ink");}
        progress.Text=(page+1)+" / "+Titles.Length;previous.IsEnabled=page>0;
        next.Content=page==Titles.Length-1?(initializeLibrary?"开始使用":"完成"):"下一步";
        content.Children.Add(Ui.Text(Titles[page],25,true));
        if(page==0)
        {
            var ball=new Border {Width=60,Height=60,CornerRadius=new CornerRadius(30),Background=Ui.Brush("#4267E9"),
                Child=new TextBlock {Text="P",Foreground=Brushes.White,FontSize=28,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},Margin=new Thickness(0,14,16,14)};
            var hero=new DockPanel();DockPanel.SetDock(ball,Dock.Left);hero.Children.Add(ball);
            hero.Children.Add(Ui.Stack(Ui.Text("一个悬浮球，三个常用动作",18,true),Ui.Text("找到模板 → 填入输入框；选中文字 → 保存或润色。",13)));
            content.Children.Add(Ui.Card(hero));
            Card("点击与拖动","左键单击打开分类菜单；悬停分类展开子菜单。拖动移动悬浮球，拖动结束不会打开菜单。");
            Card("右键与托盘","右键打开新建、管理、设置和退出等入口。关闭管理窗口后继续常驻；隐藏悬浮球后可通过托盘或快捷键恢复。");
            Card("紧凑操作，窗口管理","日常选择模板用菜单，搜索与变量填写用小浮层；模板编辑、批量管理和设置使用窗口。");
        }
        else if(page==1)
        {
            Card("1  先把光标放到目标输入框","没有选中文字时，球旁常驻“插入”气泡。点击气泡或悬浮球，展开分类菜单。");
            var example=Ui.Stack(Ui.Text("办公沟通  ›",15,true),Ui.Text("    写一封简洁礼貌的工作邮件\n    根据要点整理周报",13),Ui.Text("示意：子菜单显示备注；没有备注时显示标题。",12,false,"Muted"));
            content.Children.Add(Ui.Card(example));
            Card("2  悬停分类，单击简短备注","模板按使用频次排序。选择后在光标处插入；若已有选区，则替换选区，不清空周围文字。");
            Card("3  填变量或搜索","带 {{字段名}} 的模板先填写变量，再填入。收藏、最近使用和搜索入口帮助你快速找到模板。");
            Card("需要手动粘贴时","无法确认输入目标、权限不足或控件不兼容时，软件提供复制入口。密码框和命令终端不自动填入。使用模板不会自动发送消息。");
        }
        else if(page==2)
        {
            Card("选中文字后，三个操作出现在球旁","插入：用模板替换选区。\n润色：生成三个版本。\n保存提示词：带入选中文字，选择或新建分类，填写备注和标签。");
            Card("先配置智能体","设置 → 智能体：填写 API 地址、模型、API Key 和三个风格提示词。点击“保存并立即生效”，无需再保存外层设置。只有主动润色或测试连接时才调用 API。");
            content.Children.Add(Ui.Row(Ui.Button("打开智能体设置",()=>new SettingsWindow(host,owner:this,initialTab:"智能体").ShowDialog())));
            Card("三个版本，选择适合的一种","悬停气泡查看全文。原文与选区仍有效时点击“替换”；原文、选区或目标改变后仅能复制。替换前再次校验，保留周围文字。");
            Card("等待时也能移动悬浮球","进度与结果气泡跟随球移动。可以取消请求；失败后由你主动重试，不自动重复请求。AI 润色不计入模板使用频次。");
        }
        else
        {
            if(initializeLibrary)
            {
                content.Children.Add(Ui.Text("选择初始模板库",17,true));
                content.Children.Add(Ui.Text("10 类、每类 3 个模板。默认选择前四类；可全部取消，日后在设置中补充。",12,false,"Muted"));
                var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
                var catalog=Catalog.Create();var indexInGrid=0;
                foreach(var c in catalog.Categories.OrderBy(c=>c.Order))
                {
                    var row=indexInGrid/2;if(indexInGrid%2==0)grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
                    if(!categories.TryGetValue(c.SourceId!,out var checkbox))
                    {
                        checkbox=new CheckBox {Content=c.Name+" · 3",IsChecked=c.Order<4,ToolTip=c.Description};
                        categories.Add(c.SourceId!,checkbox);
                    }
                    if(checkbox.Parent is Panel oldParent)oldParent.Children.Remove(checkbox);
                    checkbox.Margin=new Thickness(4,7,4,7);Grid.SetRow(checkbox,row);Grid.SetColumn(checkbox,indexInGrid%2);grid.Children.Add(checkbox);indexInGrid++;
                }
                content.Children.Add(Ui.Card(grid));
                content.Children.Add(Ui.Row(Ui.Button("全选",()=>{foreach(var c in categories.Values)c.IsChecked=true;}),Ui.Button("取消全选",()=>{foreach(var c in categories.Values)c.IsChecked=false;})));
            }
            else Card("按自己的习惯整理","模板库支持备注、标签、收藏、批量移动、停用和回收站。内置分类可以随时补充，不覆盖已修改的模板。");
            Card("记住三个快捷键",host.Store.Settings.PanelHotkey+"  打开分类菜单\n"+host.Store.Settings.SaveHotkey+"  保存选中文字\n"+host.Store.Settings.HideHotkey+"  显示 / 隐藏悬浮球");
            Card("你的数据保存在本机","模板库与设置位于当前用户数据目录。API Key 按 Windows 用户加密且不进入模板导出或备份。软件只在你主动润色时发送选中文字，不上传整个模板库。");
            content.Children.Add(Ui.Text("首次教学只自动出现一次。日后可在设置 → 关于 → 使用教学中回看。",12,false,"Muted"));
        }
    }
    private void Advance()
    {
        if(page<Titles.Length-1){ShowPage(page+1);return;}
        Complete(true);Close();
    }
    private void Complete(bool import)
    {
        if(completed)return;
        if(initializeLibrary)
        {
            FirstRunTeaching.Complete(host.Store,import?categories.Where(c=>c.Value.IsChecked==true).Select(c=>c.Key):[]);
            host.LibraryChanged();
        }
        completed=true;
    }
}
