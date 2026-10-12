using Microsoft.Win32;
namespace PromptFloat;

internal sealed class BallAppearanceEditor : StackPanel
{
    private sealed record Choice(string Id,string Name);
    private readonly Settings draft;
    private readonly ComboBox style,shape;
    private readonly Border preview=new() {Name="BallAppearancePreview",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock status=Ui.Text("",12,false,"Muted");
    internal BallAppearanceEditor(Settings draft,Slider size,Slider opacity)
    {
        this.draft=draft;
        style=new ComboBox {Name="BallStylePicker",DisplayMemberPath="Name",SelectedValuePath="Id",ItemsSource=new[]{
            new Choice("blue","经典蓝"),new Choice("mint","薄荷绿"),new Choice("orange","暖橙"),new Choice("graphite","暗夜灰"),new Choice("custom","自定义图片")},SelectedValue=draft.BallStyle};
        shape=new ComboBox {Name="BallImageShapePicker",DisplayMemberPath="Name",SelectedValuePath="Id",ItemsSource=new[]{new Choice("circle","圆形裁切"),new Choice("original","保留原图比例（支持透明背景）")},SelectedValue=draft.BallImageShape};
        var frame=new Border {Height=112,CornerRadius=new CornerRadius(10),Background=Ui.Brush("#EDF1FA"),Child=preview,Margin=new Thickness(0,10,0,8)};
        Children.Add(Ui.Labeled("悬浮球样式",style));Children.Add(frame);
        var upload=Ui.Button("选择图片…",()=>{
            var dialog=new OpenFileDialog {Title="选择悬浮球图片",Filter="图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",CheckFileExists=true};
            if(dialog.ShowDialog(Window.GetWindow(this))==true)ImportImage(dialog.FileName);
        });upload.Name="BallUploadImage";
        var reset=Ui.Button("恢复默认图案",Reset);reset.Name="BallResetStyle";
        Children.Add(Ui.Row(upload,reset));Children.Add(status);
        Children.Add(Ui.Text("支持 PNG、JPG、BMP、GIF（首帧），建议使用透明背景 PNG。图片会缩小后保存在本机，不依赖原文件。",12,false,"Muted"));
        Children.Add(Ui.Labeled("图片显示方式",shape));Children.Add(Ui.Labeled("悬浮球大小（28–88）",size));Children.Add(Ui.Labeled("透明度",opacity));
        Children.Add(Ui.Text("预览随选择更新，点击“保存设置”后应用。",12,false,"Muted"));
        style.SelectionChanged+=(_,_)=>{if(style.SelectedValue is string value)draft.BallStyle=value;Refresh();};
        shape.SelectionChanged+=(_,_)=>{if(shape.SelectedValue is string value)draft.BallImageShape=value;Refresh();};
        size.ValueChanged+=(_,_)=>{draft.BallSize=size.Value;Refresh();};
        opacity.ValueChanged+=(_,_)=>{draft.Opacity=opacity.Value;Refresh();};
        Refresh();
    }
    internal Settings Draft=>draft;
    internal void ImportImage(string file)
    {
        // Decode before changing the draft: a failed import leaves the previous choice intact.
        var data=BallAppearance.Import(file);draft.BallImage=data;draft.BallStyle="custom";style.SelectedValue="custom";Refresh();
    }
    private void Reset(){draft.BallStyle="blue";draft.BallImage="";draft.BallImageShape="circle";style.SelectedValue="blue";shape.SelectedValue="circle";Refresh();}
    private void Refresh()
    {
        preview.Width=preview.Height=draft.BallSize;preview.Opacity=draft.Opacity;
        BallAppearance.Apply(preview,draft);
        if(draft.BallStyle=="custom"&&preview.Child is Image)preview.Background=Brushes.Transparent;
        shape.IsEnabled=draft.BallStyle=="custom";
        status.Text=draft.BallStyle!="custom"?"内置图案":string.IsNullOrEmpty(draft.BallImage)?"点击“选择图片…”添加自己的图案。":
            BallAppearance.LoadImage(draft.BallImage)==null?"图片无法读取，已预览默认图案；请重新选择图片。":"自定义图片已就绪";
    }
}
