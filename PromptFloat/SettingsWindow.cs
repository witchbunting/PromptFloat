using Microsoft.Win32;
using System.Text.Json;
namespace PromptFloat;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(Host host,bool onboarding=false,Window? owner=null,string? lastTargetApp=null,string? initialTab=null)
    {
        Title="PromptFloat · "+(onboarding?"第三步 · 基础设置":"设置");Width=750;Height=690;MinWidth=620;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterScreen;if(owner?.IsVisible==true)Owner=owner;
        var settings=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(host.Store.Settings,Store.Json),Store.Json)!;
        var dock=new DockPanel {Margin=new Thickness(20)};var heading=Ui.Stack(Ui.Text(onboarding?"第三步 · 准备开始":"设置",24,true),Ui.Text("模板保存在本机；润色时将选中文字发送至你配置的 API。",12,false,"Muted"));DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        CheckBox Check(string text,bool value)=>new() {Content=text,IsChecked=value};
        var keyChanges=new Dictionary<string,string>();
        var selectionActions=Check("输入框中常驻插入，选中文字后显示润色 / 保存",settings.AutoSelectionActions);
        var auto=Check("开机启动（当前用户）",settings.AutoStart);var backup=Check("每日首次修改前自动备份，保留最近 7 份",settings.AutoBackup);
        var snap=Check("靠近屏幕边缘时吸附",settings.Snap);var half=Check("停靠边缘，闲置 6 秒后半隐藏",settings.HalfHide);
        var size=new Slider {Minimum=28,Maximum=88,Value=settings.BallSize,TickFrequency=4,IsSnapToTickEnabled=true,Margin=new Thickness(4,12,4,12)};
        var opacity=new Slider {Minimum=.2,Maximum=1,Value=settings.Opacity,TickFrequency=.05,Margin=new Thickness(4,12,4,12)};
        var theme=new ComboBox {ItemsSource=new[]{"浅色","深色"},SelectedItem=settings.Theme};
        var panel=Ui.Field(settings.PanelHotkey);var save=Ui.Field(settings.SaveHotkey);var hide=Ui.Field(settings.HideHotkey);var apps=Ui.Field(string.Join(", ",settings.CopyOnlyApps),true,80);
        var tabs=new TabControl {Margin=new Thickness(0,12,0,12)};
        void Tab(string title,UIElement content)=>tabs.Items.Add(new TabItem {Header=title,Content=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12)}});
        Tab("常规",Ui.Stack(auto,backup,selectionActions,Ui.Text("关闭管理或设置窗口后继续常驻。右键悬浮球或托盘选择“退出”结束程序。",13,false,"Muted"),Ui.Labeled("剪贴板行为",Ui.Text("填入后保留模板，原剪贴板会被替换。仅填入，不自动发送。"))));
        Tab("外观",Ui.Stack(Ui.Labeled("悬浮球大小（28–88）",size),Ui.Labeled("透明度",opacity),Ui.Labeled("主题",theme),snap,half,Ui.Button("将悬浮球移回可见区域",()=>{host.Ball.Show();host.Ball.Reveal();host.Ball.Clamp(false);})));
        Tab("快捷键",Ui.Stack(Ui.Labeled("展开面板",panel),Ui.Labeled("保存选中文字",save),Ui.Labeled("显示 / 隐藏悬浮球",hide),Ui.Text("示例：Ctrl+Alt+P。冲突时鼠标入口仍可用，保存后会提示注册结果。",12,false,"Muted")));
        var agentStatus=Ui.Text("智能体页面独立保存，保存后立即生效，无需再次保存外层设置。",12,false,"Muted");
        Tab("智能体",Ui.Stack(Ui.Text("支持多个 OpenAI 兼容智能体及三种可配置润色风格。"),Ui.Button("配置智能体与测试连接",()=>new AgentWindow(settings,host.Credentials,keyChanges,this,(agents,defaultId,keys)=>{
            host.SaveAgentConfiguration(agents,defaultId,keys);
            agentStatus.Text="智能体配置已保存并生效，下一次润色使用新配置。";
        }).ShowDialog()),agentStatus,Ui.Text("密钥按当前 Windows 用户加密；模板导出和备份不包含密钥。",12,false,"Muted")));
        Tab("模板库",Ui.Stack(Ui.Button("分类与模板管理",host.OpenManager),Ui.Button("补充内置分类",()=>new CatalogWindow(host,false,this).ShowDialog()),Ui.Text("10 类、30 个可编辑模板。补充时按来源 ID 去重，不覆盖已修改版本。",12,false,"Muted")));
        var current=lastTargetApp??host.Input.Target?.App;var demo=Ui.Field("在这里输入测试文字，选中一部分后点击“测试插入”。",true,110);var testText=Ui.Field("提示词示例 ✨");int selectionStart=0,selectionLength=0;
        demo.SelectionChanged+=(_,_)=>{selectionStart=demo.SelectionStart;selectionLength=demo.SelectionLength;};
        Tab("输入兼容",Ui.Stack(Ui.Text(current==null?"当前无外部输入目标":$"最近目标：{current}"),Ui.Labeled("仅复制应用（进程名，逗号分隔，不含 .exe）",apps),Ui.Button("添加最近目标为仅复制",()=>{if(current!=null)apps.Text=string.Join(", ",TemplateEngine.Tags(apps.Text).Append(current).Distinct(StringComparer.OrdinalIgnoreCase));}),Ui.Text("密码框、命令终端和权限较高的应用不会自动填入。此处演示光标插入；跨软件兼容需在实际目标中测试。",12,false,"Muted"),Ui.Labeled("测试输入框",demo),Ui.Labeled("测试文本",testText),Ui.Button("测试插入 / 替换选区",async()=>{try{var start=selectionStart;var length=selectionLength;await InputBridge.CopyAsync(testText.Text);demo.Focus();demo.Select(start,length);demo.Paste();}catch(Exception e){MessageBox.Show(this,e.Message,"PromptFloat");}})));
        Tab("数据",Ui.Stack(Ui.Text("数据目录",14,true),Ui.Text(host.Store.Root,12,false,"Muted"),Ui.Row(Ui.Button("打开目录",()=>Process.Start(new ProcessStartInfo(host.Store.Root){UseShellExecute=true})),Ui.Button("立即备份",()=>{host.Store.Backup();MessageBox.Show(this,"备份已保存到 backups 文件夹。","PromptFloat");})),Ui.Row(Ui.Button("导入 JSON",()=>{if(DataUi.Import(this,host.Store))host.LibraryChanged();}),Ui.Button("导出全部",()=>DataUi.Export(this,host.Store))),Ui.Button("从完整备份恢复",()=>{
            var dialog=new OpenFileDialog {Filter="完整备份 (*.zip)|*.zip",InitialDirectory=host.Store.BackupsPath};if(dialog.ShowDialog(this)!=true||!Ui.Confirm("恢复将替换当前模板、统计和设置。恢复前会另做备份。",this))return;
            host.Store.Restore(dialog.FileName);host.ApplySettings();host.LibraryChanged();MessageBox.Show(this,"恢复完成。请重新打开设置查看恢复后的选项。","PromptFloat");Close();
        }),Ui.Text("JSON 默认导出有效模板，不包含使用统计；完整备份包含数据库和设置。备份与日志不会上传。",12,false,"Muted")));
        Tab("关于",Ui.Stack(Ui.Text("PromptFloat "+AppVersion.Text,22,true),Ui.Button("使用教学",()=>host.OpenTutorial()),Ui.Text("本地提示词助手 · Windows x64 便携版"),Ui.Text("使用：点击输入框 → 打开悬浮球 → 选择模板。\n保存：选中文字 → Ctrl+Alt+S；不支持时手动复制并从剪贴板新建。\n更新：退出程序后替换程序目录；数据位于当前用户目录，不随程序更新删除。",13),Ui.Button("打开使用说明",()=>{var file=Path.Combine(AppContext.BaseDirectory,"使用说明.md");if(File.Exists(file))Process.Start(new ProcessStartInfo(file){UseShellExecute=true});else MessageBox.Show(this,"请查看项目 README.md 或便携包中的使用说明。","PromptFloat");})));
        if(initialTab!=null)tabs.SelectedItem=tabs.Items.OfType<TabItem>().FirstOrDefault(t=>(string)t.Header==initialTab)??tabs.Items[0];
        var footer=Ui.Row(Ui.Button("取消",()=>Close()),Ui.Button(onboarding?"完成并开始使用":"保存设置",()=>{
            var hotkeys=new[]{panel.Text.Trim(),save.Text.Trim(),hide.Text.Trim()}.Select(Native.ParseHotkey).ToArray();if(hotkeys.Distinct().Count()!=3)throw new ArgumentException("三个快捷键不能相同。");
            settings.AutoStart=auto.IsChecked==true;settings.AutoBackup=backup.IsChecked==true;settings.Snap=snap.IsChecked==true;settings.HalfHide=half.IsChecked==true;settings.BallSize=size.Value;settings.Opacity=opacity.Value;settings.Theme=(string)theme.SelectedItem;
            settings.PanelHotkey=panel.Text.Trim();settings.SaveHotkey=save.Text.Trim();settings.HideHotkey=hide.Text.Trim();settings.CopyOnlyApps=TemplateEngine.Tags(apps.Text).Select(x=>x.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?x[..^4]:x).ToList();if(onboarding)settings.Initialized=true;
            settings.AutoSelectionActions=selectionActions.IsChecked==true;Store.ValidateSettings(settings);var previous=host.Store.Settings;host.Store.SaveSettings(settings);try{host.Credentials.Save(keyChanges);}catch{host.Store.SaveSettings(previous);throw;}Host.SetAutoStart(settings.AutoStart);host.ApplySettings();DialogResult=true;
        },true));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);dock.Children.Add(tabs);Content=dock;
    }
}
