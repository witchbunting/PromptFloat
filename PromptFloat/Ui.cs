namespace PromptFloat;

internal static class Ui
{
    public static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    public static void Theme(string theme)
    {
        var dark=theme=="深色";var r=Application.Current.Resources;
        r["Bg"]=Brush(dark?"#171D28":"#F4F6FA");r["Surface"]=Brush(dark?"#222B3A":"#FFFFFF");
        r["Ink"]=Brush(dark?"#E8EEF8":"#17263D");r["Muted"]=Brush(dark?"#A8B6CC":"#66768C");
        r["Line"]=Brush(dark?"#38465B":"#DFE5EF");r["Accent"]=Brush("#4267E9");r["AccentSoft"]=Brush(dark?"#263C66":"#EDF2FF");
    }
    public static void Styles()
    {
        var r=Application.Current.Resources;
        var window=new Style(typeof(Window));window.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Microsoft YaHei UI")));window.Setters.Add(new Setter(Control.FontSizeProperty,13d));window.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));window.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Bg")));r[typeof(Window)]=window;foreach(var type in new[]{typeof(PanelWindow),typeof(ManagerWindow),typeof(EditorWindow),typeof(VariableWindow),typeof(SettingsWindow),typeof(AgentWindow),typeof(TutorialWindow),typeof(CatalogWindow),typeof(BallWindow)})r[type]=window;
        var text=new Style(typeof(TextBlock));r[typeof(TextBlock)]=text;
        var button=new Style(typeof(Button));button.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));button.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,6,10,6)));button.Setters.Add(new Setter(Control.MarginProperty,new Thickness(3)));button.Setters.Add(new Setter(Control.MinHeightProperty,30d));button.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));button.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Surface")));button.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));button.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Line")));button.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);template.VisualTree=border;button.Setters.Add(new Setter(Control.TemplateProperty,template));var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Accent")));button.Triggers.Add(hover);var disabled=new Trigger {Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45));button.Triggers.Add(disabled);r[typeof(Button)]=button;
        var field=new Style(typeof(TextBox));field.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,6,8,6)));field.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Surface")));field.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));field.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Line")));field.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));field.Setters.Add(new Setter(Control.MinHeightProperty,32d));r[typeof(TextBox)]=field;
        var list=new Style(typeof(ListBox));list.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Surface")));list.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));list.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));r[typeof(ListBox)]=list;
        var item=new Style(typeof(ListBoxItem));item.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,7,8,7)));item.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));item.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));r[typeof(ListBoxItem)]=item;
        var combo=new Style(typeof(ComboBox));combo.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#17263D")));combo.Setters.Add(new Setter(Control.MinHeightProperty,32d));combo.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5)));r[typeof(ComboBox)]=combo;
        var check=new Style(typeof(CheckBox));check.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));check.Setters.Add(new Setter(Control.MarginProperty,new Thickness(3,6,3,6)));r[typeof(CheckBox)]=check;
        var grid=new Style(typeof(DataGrid));grid.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Surface")));grid.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));grid.Setters.Add(new Setter(DataGrid.RowBackgroundProperty,new DynamicResourceExtension("Surface")));grid.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty,new DynamicResourceExtension("Bg")));grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty,DataGridGridLinesVisibility.None));grid.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Line")));r[typeof(DataGrid)]=grid;
    }
    public static Window Window(string title,double width=700,double height=540,Window? owner=null)
    {
        var w=new Window {Title="PromptFloat · "+title,Width=width,Height=height,MinWidth=Math.Min(width,480),MinHeight=Math.Min(height,360),WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner};if(owner?.IsVisible==true)w.Owner=owner;return w;
    }
    public static TextBlock Text(string text,int size=13,bool bold=false,string resource="Ink") {var t=new TextBlock {Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};t.SetResourceReference(TextBlock.ForegroundProperty,resource);return t;}
    public static Button Button(string label,Action action,bool primary=false)
    {
        var b=new Button {Content=label};if(primary){b.SetResourceReference(Control.BackgroundProperty,"Accent");b.Foreground=Brush("#FFFFFF");b.BorderThickness=new Thickness(0);}b.Click+=(_,_)=>Run(action);return b;
    }
    public static void Run(Action action) {try{action();}catch(Exception e){MessageBox.Show(e.Message,"PromptFloat",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    public static Border Card(UIElement content,Thickness? padding=null) {var b=new Border {Child=content,CornerRadius=new CornerRadius(10),Padding=padding??new Thickness(14),Margin=new Thickness(4),BorderThickness=new Thickness(1)};b.SetResourceReference(Border.BackgroundProperty,"Surface");b.SetResourceReference(Border.BorderBrushProperty,"Line");return b;}
    public static StackPanel Stack(params UIElement[] children) {var s=new StackPanel();foreach(var c in children)s.Children.Add(c);return s;}
    public static WrapPanel Row(params UIElement[] children) {var s=new WrapPanel {VerticalAlignment=VerticalAlignment.Center};foreach(var c in children)s.Children.Add(c);return s;}
    public static TextBox Field(string text="",bool multiline=false,double height=double.NaN)=>new() {Text=text,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,Height=height};
    public static UIElement Labeled(string label,UIElement field) {var stack=Stack(Text(label,12,false,"Muted"),field);stack.Margin=new Thickness(0,6,0,6);return stack;}
    public static bool Confirm(string text,Window? owner=null) => (owner!=null?MessageBox.Show(owner,text,"PromptFloat",MessageBoxButton.OKCancel,MessageBoxImage.Question):MessageBox.Show(text,"PromptFloat",MessageBoxButton.OKCancel,MessageBoxImage.Question))==MessageBoxResult.OK;
    public static string? Ask(Window owner,string title,string label,string initial="",bool multiline=false)
    {
        var w=Window(title,520,multiline?390:260,owner);var field=Field(initial,multiline,multiline?150:double.NaN);string? result=null;
        var body=Stack(Text(title,20,true),Labeled(label,field),Row(Button("取消",()=>w.Close()),Button("确定",()=>{result=field.Text;w.DialogResult=true;},true)));body.Margin=new Thickness(20);w.Content=body;w.Loaded+=(_,_)=>{field.Focus();field.SelectAll();};w.ShowDialog();return result;
    }
    public static string? PickCategory(Window owner,Store store,string title,string? exclude=null)
    {
        var w=Window(title,480,270,owner);var combo=new ComboBox {ItemsSource=store.Read().Categories.Where(c=>c.Id!=exclude).OrderBy(c=>c.Order).ToList(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedIndex=0};string? result=null;
        var body=Stack(Text(title,20,true),Labeled("目标分类",combo),Row(Button("取消",()=>w.Close()),Button("确定",()=>{result=combo.SelectedValue as string;if(result!=null)w.DialogResult=true;},true)));body.Margin=new Thickness(20);w.Content=body;w.ShowDialog();return result;
    }
    public static void Preview(Window owner,Prompt prompt)
    {
        var w=Window(prompt.Title,700,560,owner);var body=Field(prompt.Body,true);body.IsReadOnly=true;var dock=new DockPanel {Margin=new Thickness(20)};var title=Stack(Text(prompt.Title,22,true),Text(prompt.Note,13,false,"Muted"));DockPanel.SetDock(title,Dock.Top);dock.Children.Add(title);var close=Button("关闭",()=>w.Close());DockPanel.SetDock(close,Dock.Bottom);dock.Children.Add(close);dock.Children.Add(body);w.Content=dock;w.ShowDialog();
    }
}
