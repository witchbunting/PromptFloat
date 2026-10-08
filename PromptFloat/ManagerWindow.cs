using System.Windows.Data;
namespace PromptFloat;

internal sealed class ManagerWindow : Window
{
    private readonly Host host;private readonly Store store;private Library library=new();
    private readonly ListBox categories=new();private readonly DataGrid grid=new() {AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Extended,SelectionUnit=DataGridSelectionUnit.FullRow,CanUserAddRows=false,RowHeight=38};
    private readonly TextBox search=Ui.Field(),detail=Ui.Field("",true);
    private readonly TextBlock status=Ui.Text("",12,false,"Muted");private readonly ComboBox batch;
    private readonly TextBlock detailTitle=Ui.Text("选择模板查看正文",17,true);private Point dragStart;
    public ManagerWindow(Host host,bool onboarding=false)
    {
        this.host=host;store=host.Store;Title="PromptFloat · 分类与模板管理";Width=1160;Height=760;MinWidth=920;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel {Margin=new Thickness(20)};
        var top=Ui.Stack(Ui.Text(onboarding?"第二步 · 整理你的模板库":"分类与模板管理",24,true),Ui.Text("多选模板批量整理。这里的“使用”默认复制，不沿用之前的输入目标。",12,false,"Muted"));DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var toolbar=Ui.Row(Ui.Button("＋ 新建模板",()=>Edit(),true),Ui.Button("＋ 新建分类",AddCategory),Ui.Button("导入",()=>{if(DataUi.Import(this,store))Refresh();}),Ui.Button("导出全部",()=>DataUi.Export(this,store)),Ui.Button("导出所选 / 当前分类",ExportSelected),Ui.Button("补充内置模板",()=>{new CatalogWindow(host,false,this).ShowDialog();Refresh();}));DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        batch=new ComboBox {ItemsSource=new[]{"移动分类","添加标签","移除标签","替换标签","设置备注","清空备注","收藏","取消收藏","启用","停用","删除到回收站","恢复","永久删除","重置统计"},SelectedIndex=0,Width=130,Margin=new Thickness(3)};
        var foot=Ui.Stack(status,Ui.Row(batch,Ui.Button("应用到所选模板",Batch),Ui.Button("复制所选",async()=>{if(grid.SelectedItem is Prompt p)await host.UseAsync(p,true,this);}),Ui.Button(onboarding?"完成整理，继续":"关闭",()=>Close())));DockPanel.SetDock(foot,Dock.Bottom);root.Children.Add(foot);
        var columns=new Grid {Margin=new Thickness(0,10,0,10)};columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(165)});columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(270)});
        var left=new DockPanel();var categoryTip=Ui.Text("分类可拖动排序\n右键重命名、停用、合并",11,false,"Muted");DockPanel.SetDock(categoryTip,Dock.Bottom);left.Children.Add(categoryTip);left.Children.Add(categories);columns.Children.Add(left);
        var center=new DockPanel {Margin=new Thickness(12,0,12,0)};var searchLabel=Ui.Labeled("搜索当前列表（标题、标签、备注、正文）",search);DockPanel.SetDock(searchLabel,Dock.Top);center.Children.Add(searchLabel);center.Children.Add(grid);Grid.SetColumn(center,1);columns.Children.Add(center);
        detail.IsReadOnly=true;var right=new DockPanel();var rightTop=Ui.Stack(detailTitle,Ui.Row(Ui.Button("编辑",()=>{if(grid.SelectedItem is Prompt p)Edit(p);}),Ui.Button("复制为新模板",Duplicate)));DockPanel.SetDock(rightTop,Dock.Top);right.Children.Add(rightTop);right.Children.Add(detail);Grid.SetColumn(right,2);columns.Children.Add(right);root.Children.Add(columns);Content=root;
        grid.Columns.Add(new DataGridTextColumn {Header="标题",Binding=new Binding("Title"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn {Header="使用",Binding=new Binding("Uses"),Width=56});
        grid.Columns.Add(new DataGridCheckBoxColumn {Header="启用",Binding=new Binding("Enabled"),Width=52});
        grid.Columns.Add(new DataGridCheckBoxColumn {Header="收藏",Binding=new Binding("Favorite"),Width=52});
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is Prompt p){detailTitle.Text=p.Title;detail.Text=$"备注：{p.Note}\n标签：{string.Join(" · ",p.Tags)}\n填入：{p.InsertCount}　复制：{p.CopyCount}\n\n{p.Body}";}status.Text=$"当前 {grid.Items.Count} 个 · 已选 {grid.SelectedItems.Count} 个";};
        grid.MouseDoubleClick+=(_,e)=>{if(e.OriginalSource is DependencyObject d && FindAncestor<DataGridRow>(d)!=null&&grid.SelectedItem is Prompt p)Edit(p);};
        search.TextChanged+=(_,_)=>ApplyFilter();categories.SelectionChanged+=(_,_)=>ApplyFilter();
        categories.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(categories);
        categories.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(categories)-dragStart).Length>8&&(categories.SelectedItem is ListBoxItem selectedItem?selectedItem.Content as CategoryChoice:categories.SelectedItem as CategoryChoice) is CategoryChoice c&&c.Id!=Store.Uncategorized&&!c.Id.StartsWith("@"))DragDrop.DoDragDrop(categories,c.Id,DragDropEffects.Move);};
        categories.AllowDrop=true;categories.Drop+=(_,e)=>{
            if(e.Data.GetData(typeof(string)) is not string source)return;var item=FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);if(item?.Content is not CategoryChoice dest||dest.Id.StartsWith("@")||dest.Id==Store.Uncategorized)return;
            Ui.Run(()=>{store.Change(l=>{var ordered=l.Categories.Where(c=>c.Id!=Store.Uncategorized).OrderBy(c=>c.Order).ToList();var moved=ordered.FirstOrDefault(c=>c.Id==source);if(moved==null)return;ordered.Remove(moved);ordered.Insert(Math.Max(0,ordered.FindIndex(c=>c.Id==dest.Id)),moved);for(var i=0;i<ordered.Count;i++)ordered[i].Order=i;});Refresh(source);});
        };
        Loaded+=(_,_)=>Refresh();Closing+=(_,_)=>host.LibraryChanged();
    }
    private static T? FindAncestor<T>(DependencyObject? d) where T:DependencyObject {while(d!=null){if(d is T value)return value;d=d is System.Windows.Documents.Run?LogicalTreeHelper.GetParent(d):VisualTreeHelper.GetParent(d);}return null;}
    public void Refresh(string? select=null)
    {
        var current=select??(categories.SelectedItem is ListBoxItem selectedItem?selectedItem.Content as CategoryChoice:categories.SelectedItem as CategoryChoice)?.Id??"@all";library=store.Read();categories.Items.Clear();
        categories.Items.Add(new CategoryChoice("@all","全部模板"));categories.Items.Add(new CategoryChoice("@trash","回收站"));
        foreach(var c in library.Categories.OrderBy(c=>c.Order))
        {
            var choice=new CategoryChoice(c.Id,c.Name+(c.Enabled?"":" · 已停用"));var item=new ListBoxItem {Content=choice};var menu=new ContextMenu();
            void Add(string label,Action action){var m=new MenuItem {Header=label};m.Click+=(_,_)=>Ui.Run(action);menu.Items.Add(m);}
            if(c.Id!=Store.Uncategorized)
            {
                Add("重命名",()=>{var text=Ui.Ask(this,"重命名分类","分类名称",c.Name);if(!string.IsNullOrWhiteSpace(text)){store.Change(l=>l.Categories.First(x=>x.Id==c.Id).Name=text.Trim());Refresh(c.Id);}});
                Add("编辑说明",()=>{var text=Ui.Ask(this,"分类说明","说明",c.Description,true);if(text!=null){store.Change(l=>l.Categories.First(x=>x.Id==c.Id).Description=text);Refresh(c.Id);}});
                Add(c.Enabled?"停用分类":"启用分类",()=>{store.Change(l=>l.Categories.First(x=>x.Id==c.Id).Enabled=!c.Enabled);Refresh(c.Id);});
                Add("合并到其他分类",()=>{var dest=Ui.PickCategory(this,store,"合并分类",c.Id);if(dest!=null&&Ui.Confirm("合并后原分类将移除，模板和统计保留。",this)){store.DeleteCategory(c.Id,false,dest);Refresh(dest);}});
                Add("删除分类，模板移到未分类",()=>{if(Ui.Confirm("删除分类，所有模板移入未分类？",this)){store.DeleteCategory(c.Id);Refresh();}});
                Add("删除分类及模板",()=>{if(Ui.Confirm("删除分类，并将模板放入回收站？",this)){store.DeleteCategory(c.Id,true);Refresh();}});
            }
            Add("导出本分类",()=>DataUi.Export(this,store,library.Prompts.Where(p=>p.CategoryId==c.Id&&p.Deleted==null).Select(p=>p.Id)));
            item.ContextMenu=menu;categories.Items.Add(item);
        }
        var selected=categories.Items.Cast<object>().FirstOrDefault(x=>(x is ListBoxItem i?i.Content as CategoryChoice:x as CategoryChoice)?.Id==current);categories.SelectedItem=selected??categories.Items[0];ApplyFilter();
    }
    private string SelectedCategory=>(categories.SelectedItem is ListBoxItem i?i.Content as CategoryChoice:categories.SelectedItem as CategoryChoice)?.Id??"@all";
    private void ApplyFilter()
    {
        var selected=SelectedCategory;var items=library.Prompts.Where(p=>selected=="@trash"?p.Deleted!=null:p.Deleted==null);
        if(!selected.StartsWith("@"))items=items.Where(p=>p.CategoryId==selected);
        grid.ItemsSource=TemplateEngine.Search(items,search.Text).ToList();status.Text=$"当前 {grid.Items.Count} 个 · 按 Ctrl / Shift 多选";
    }
    private void Edit(Prompt? p=null){if(p?.Deleted!=null){MessageBox.Show(this,"请先恢复模板再编辑。","PromptFloat");return;}if(new EditorWindow(store,p,owner:this).ShowDialog()==true)Refresh();}
    private void AddCategory(){var name=Ui.Ask(this,"新建分类","分类名称");if(!string.IsNullOrWhiteSpace(name)){var c=new Category {Name=name.Trim(),Order=library.Categories.Count-1};store.Change(l=>l.Categories.Add(c));Refresh(c.Id);}}
    private void Duplicate(){if(grid.SelectedItem is not Prompt original)return;var p=original.Clone();p.Id=Guid.NewGuid().ToString("N");p.Title=(p.Title.Length>95?p.Title[..95]:p.Title)+" 副本";p.Source="user";p.SourceId=null;p.Created=DateTimeOffset.UtcNow;p.Deleted=null;p.InsertCount=0;p.CopyCount=0;p.LastUsed=null;p.Favorite=false;if(new EditorWindow(store,p,owner:this).ShowDialog()==true)Refresh();}
    private void ExportSelected(){var ids=grid.SelectedItems.Cast<Prompt>().Select(p=>p.Id).ToList();if(ids.Count==0&&!SelectedCategory.StartsWith("@"))ids=library.Prompts.Where(p=>p.CategoryId==SelectedCategory&&p.Deleted==null).Select(p=>p.Id).ToList();if(ids.Count==0)throw new ArgumentException("请选择模板或分类。");DataUi.Export(this,store,ids);}
    private void Batch()
    {
        var ids=grid.SelectedItems.Cast<Prompt>().Select(p=>p.Id).ToList();if(ids.Count==0)throw new ArgumentException("请先选择模板。");var action=batch.SelectedItem as string;
        switch(action)
        {
            case "移动分类":var target=Ui.PickCategory(this,store,"移动所选模板");if(target==null)return;store.Batch(ids,p=>p.CategoryId=target);break;
            case "添加标签":case "移除标签":case "替换标签":var input=Ui.Ask(this,action,"标签，用逗号分隔");if(input==null)return;var tags=TemplateEngine.Tags(input);store.Batch(ids,p=>p.Tags=action=="替换标签"?tags.ToList():action=="添加标签"?p.Tags.Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList():p.Tags.Where(t=>!tags.Contains(t,StringComparer.OrdinalIgnoreCase)).ToList());break;
            case "设置备注":case "清空备注":if(!Ui.Confirm($"将覆盖 {ids.Count} 个模板的现有备注。",this))return;var note=action=="清空备注"?"":Ui.Ask(this,"批量设置备注","新备注",multiline:true);if(note==null)return;store.Batch(ids,p=>p.Note=note);break;
            case "收藏":case "取消收藏":store.Batch(ids,p=>p.Favorite=action=="收藏");break;
            case "启用":case "停用":store.Batch(ids,p=>p.Enabled=action=="启用");break;
            case "删除到回收站":if(!Ui.Confirm($"将 {ids.Count} 个模板放入回收站，保留 30 天？",this))return;store.Batch(ids,p=>p.Deleted=DateTimeOffset.UtcNow);break;
            case "恢复":store.Batch(ids,p=>p.Deleted=null);break;
            case "永久删除":if(!Ui.Confirm($"永久删除所选 {ids.Count} 个模板中已在回收站的项？无法恢复。",this))return;store.Change(l=>l.Prompts.RemoveAll(p=>ids.Contains(p.Id)&&p.Deleted!=null));break;
            case "重置统计":if(!Ui.Confirm($"重置 {ids.Count} 个模板的使用次数和最近使用时间？",this))return;store.Batch(ids,p=>{p.InsertCount=0;p.CopyCount=0;p.LastUsed=null;});break;
        }
        Refresh();
    }
}
