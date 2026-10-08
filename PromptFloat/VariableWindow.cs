namespace PromptFloat;

internal sealed class VariableWindow : Window
{
    public string? Result {get;private set;}public bool CopyRequested {get;private set;}
    public VariableWindow(Prompt prompt,Window? owner,bool copyDefault=false)
    {
        Title="PromptFloat · 填写模板";Width=820;Height=620;MinWidth=650;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterScreen;if(owner?.IsVisible==true){Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}
        var fields=TemplateEngine.Parse(prompt.Body,prompt.Variables);var values=new Dictionary<string,TextBox>();
        var dock=new DockPanel {Margin=new Thickness(20)};var heading=Ui.Stack(Ui.Text(prompt.Title,22,true),Ui.Text("填写内容仅用于本次操作，不保存到模板。",12,false,"Muted"));DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        var preview=Ui.Field("",true);preview.IsReadOnly=true;var error=Ui.Text("",12,false,"Muted");
        void Update(){var inputs=values.ToDictionary(x=>x.Key,x=>x.Value.Text);try{preview.Text=TemplateEngine.Render(prompt.Body,inputs,fields);error.Text="";}catch(ArgumentException e){error.Text=e.Message;preview.Text=TemplateEngine.Render(prompt.Body,inputs,fields.Select(v=>new Variable {Name=v.Name,Required=false}));}}
        void Finish(bool copy){Result=TemplateEngine.Render(prompt.Body,values.ToDictionary(x=>x.Key,x=>x.Value.Text),fields);CopyRequested=copy;DialogResult=true;}
        var footer=Ui.Stack(error,Ui.Row(Ui.Button("取消",()=>Close()),Ui.Button("复制结果",()=>Finish(true)),Ui.Button(copyDefault?"确认并复制":"填入原输入框",()=>Finish(copyDefault),true)));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1.2,GridUnitType.Star)});
        var form=new StackPanel {Margin=new Thickness(0,10,14,0)};foreach(var v in fields){var field=Ui.Field(v.Default,true,84);values[v.Name]=field;form.Children.Add(Ui.Labeled(v.Name+(v.Required?" *":"（可选）"),field));field.TextChanged+=(_,_)=>Update();}var scroll=new ScrollViewer {Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};grid.Children.Add(scroll);var right=Ui.Stack(Ui.Text("最终正文预览",12,false,"Muted"));var pdock=new DockPanel {Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(right,Dock.Top);pdock.Children.Add(right);pdock.Children.Add(preview);Grid.SetColumn(pdock,1);grid.Children.Add(pdock);dock.Children.Add(grid);Content=dock;Update();
    }
}
