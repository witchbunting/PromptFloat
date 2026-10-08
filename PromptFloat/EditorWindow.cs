using System.Windows.Data;
namespace PromptFloat;

internal sealed class EditorWindow : Window
{
    private readonly Store store;private readonly Prompt prompt;
    private readonly TextBox title,body,note,tags;private readonly ComboBox category;
    private readonly DataGrid variables=new() {AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Margin=new Thickness(0,8,0,0)};
    private List<Variable> fields;
    private Category? pendingCategory;
    public EditorWindow(Store store,Prompt? existing=null,string initialBody="",Window? owner=null)
    {
        this.store=store;prompt=existing?.Clone()??new Prompt {Body=initialBody};fields=prompt.Variables;
        Title="PromptFloat · "+(existing==null?"新建模板":"编辑模板");Width=760;Height=730;MinWidth=620;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;if(owner?.IsVisible==true){Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}
        if(existing==null&&!string.IsNullOrWhiteSpace(initialBody))prompt.Title=initialBody.Split(['\r','\n']).Select(line=>line.Trim()).FirstOrDefault(line=>line.Length>0)??"新模板";
        if(prompt.Title.Length>100)prompt.Title=prompt.Title[..100];
        title=Ui.Field(prompt.Title);body=Ui.Field(prompt.Body,true);note=Ui.Field(prompt.Note,true,64);tags=Ui.Field(string.Join(", ",prompt.Tags));
        category=new ComboBox {ItemsSource=store.Read().Categories.OrderBy(c=>c.Order).ToList(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=prompt.CategoryId};
        var dock=new DockPanel {Margin=new Thickness(20)};
        var heading=Ui.Stack(Ui.Text(existing==null?"收好下一次会用的提示词":"编辑提示词",22,true),Ui.Text("备注和标签用于查找，不会随正文填入。",12,false,"Muted"),Ui.Labeled("标题",title),Ui.Labeled("所属分类",Ui.Stack(category,Ui.Button("创建分类",()=>{var name=Ui.Ask(this,"新建分类","分类名称");if(string.IsNullOrWhiteSpace(name))return;name=name.Trim();if(name.Length>100)throw new ArgumentException("分类名最多 100 字。");var categories=store.Read().Categories.OrderBy(c=>c.Order).ToList();var existing=categories.FirstOrDefault(c=>c.Name==name);pendingCategory=existing==null?new Category {Name=name}:null;if(pendingCategory!=null)categories.Add(pendingCategory);category.ItemsSource=categories;category.SelectedValue=existing?.Id??pendingCategory!.Id;}))));DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        var footer=Ui.Stack(Ui.Labeled("备注",note),Ui.Labeled("标签（逗号分隔）",tags),Ui.Row(Ui.Button("取消",()=>Close()),Ui.Button("保存模板",Save,true)));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var tabs=new TabControl();tabs.Items.Add(new TabItem {Header="正文",Content=body});
        variables.Columns.Add(new DataGridTextColumn {Header="字段名",Binding=new Binding("Name"),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        variables.Columns.Add(new DataGridTextColumn {Header="默认值（可多行）",Binding=new Binding("Default"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
        variables.Columns.Add(new DataGridCheckBoxColumn {Header="必填",Binding=new Binding("Required"),Width=70});
        var variableDock=new DockPanel();var tip=Ui.Text("正文中使用 {{字段名}}；同名字段只填一次。原样输出用 \\{{字段名}}。双击默认值可编辑。",12,false,"Muted");DockPanel.SetDock(tip,Dock.Top);variableDock.Children.Add(tip);
        var editDefault=Ui.Button("编辑选中字段的多行默认值",()=>{if(variables.SelectedItem is Variable v){var text=Ui.Ask(this,"变量默认值",v.Name,v.Default,true);if(text!=null){v.Default=text;variables.Items.Refresh();}}});DockPanel.SetDock(editDefault,Dock.Bottom);variableDock.Children.Add(editDefault);variableDock.Children.Add(variables);tabs.Items.Add(new TabItem {Header="变量配置",Content=variableDock});
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs&&tabs.SelectedIndex==1)RefreshVariables();};dock.Children.Add(tabs);Content=dock;
    }
    private void RefreshVariables(){variables.CommitEdit();variables.CommitEdit(DataGridEditingUnit.Row,true);fields=TemplateEngine.Parse(body.Text,fields);variables.ItemsSource=fields;}
    private void Save()
    {
        if(string.IsNullOrWhiteSpace(title.Text)||string.IsNullOrWhiteSpace(body.Text))throw new ArgumentException("请填写标题和正文。");
        if(title.Text.Trim().Length>100)throw new ArgumentException("标题最多 100 个字符。");
        if(body.Text.Length>100000)throw new ArgumentException("正文最多 100,000 个字符，请缩短后保存。选区内容不会被自动截断。");
        if(store.Read().Prompts.Any(p=>p.Id!=prompt.Id&&p.Deleted==null&&TemplateEngine.BodyKey(p.Body)==TemplateEngine.BodyKey(body.Text))&&!Ui.Confirm("发现相同正文，仍保存为独立模板？",this))return;
        RefreshVariables();prompt.Title=title.Text.Trim();prompt.Body=body.Text;prompt.Note=note.Text;prompt.Tags=TemplateEngine.Tags(tags.Text);prompt.Variables=fields;prompt.CategoryId=category.SelectedValue as string??Store.Uncategorized;store.SavePrompt(prompt,pendingCategory);DialogResult=true;
    }
}
