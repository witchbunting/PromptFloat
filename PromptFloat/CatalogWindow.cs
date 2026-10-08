namespace PromptFloat;

internal sealed class CatalogWindow : Window
{
    public CatalogWindow(Host host,bool onboarding,Window? owner=null)
    {
        Title="PromptFloat · "+(onboarding?"欢迎使用":"补充内置模板");Width=780;Height=720;MinWidth=620;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterScreen;if(owner?.IsVisible==true)Owner=owner;
        var catalog=Catalog.Create();var choices=new Dictionary<string,CheckBox>();var dock=new DockPanel {Margin=new Thickness(22)};
        var heading=Ui.Stack(Ui.Text(onboarding?"第一步 · 选择你需要的模板":"补充常用提示词",25,true),Ui.Text("按类别选择，可稍后补充；所有模板都可以修改。",13,false,"Muted"));DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        var tools=Ui.Row(Ui.Button("全选",()=>{foreach(var c in choices.Values)c.IsChecked=true;}),Ui.Button("取消全选",()=>{foreach(var c in choices.Values)c.IsChecked=false;}),Ui.Button("导入已有 JSON",()=>{if(DataUi.Import(this,host.Store)){host.LibraryChanged();foreach(var c in choices.Values)c.IsChecked=false;}}));DockPanel.SetDock(tools,Dock.Top);dock.Children.Add(tools);
        var content=new StackPanel();foreach(var c in catalog.Categories)
        {
            var check=new CheckBox {Content=Ui.Stack(Ui.Text(c.Name+" · 3 个模板",15,true),Ui.Text(c.Description,12,false,"Muted")),IsChecked=onboarding&&c.Order<4};choices[c.SourceId!]=check;
            var templates=new StackPanel();foreach(var p in catalog.Prompts.Where(p=>p.CategoryId==c.Id)){var b=Ui.Button(p.Title+"  ·  预览",()=>Ui.Preview(this,p));b.HorizontalAlignment=HorizontalAlignment.Left;templates.Children.Add(b);}
            content.Children.Add(Ui.Card(Ui.Stack(check,new Expander {Header="查看模板",Content=templates,Margin=new Thickness(0,5,0,0)})));
        }
        var footer=Ui.Row(Ui.Button(onboarding?"跳过内置模板":"取消",()=>{if(onboarding)Continue(false);else Close();}),Ui.Button(onboarding?"保留所选，继续":"添加所选",()=>Continue(true),true));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);dock.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=dock;
        void Continue(bool add)
        {
            if(add)host.Store.AddBuiltIns(choices.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key));host.LibraryChanged();
            if(onboarding){Hide();new ManagerWindow(host,true).ShowDialog();new SettingsWindow(host,true).ShowDialog();DialogResult=host.Store.Settings.Initialized;}
            else DialogResult=true;
        }
    }
}
