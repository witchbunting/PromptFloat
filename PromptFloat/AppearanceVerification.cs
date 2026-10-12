using System.Text.Json;
using System.Windows.Media.Imaging;
namespace PromptFloat;

internal static class AppearanceVerification
{
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T element)yield return element;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal static async Task Run(Host host,Action<bool,string,string> check,string output,Func<Task> focusFixture)
    {
        var saved=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(host.Store.Settings,Store.Json),Store.Json)!;
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen())
        {
            dc.DrawEllipse(Ui.Brush("#4267E9"),null,new Point(360,180),155,145);
            dc.DrawEllipse(Brushes.White,null,new Point(315,160),18,18);
            dc.DrawEllipse(Brushes.White,null,new Point(405,160),18,18);
            dc.DrawRoundedRectangle(Ui.Brush("#B8C7FF"),null,new Rect(320,220,80,16),8,8);
        }
        var bitmap=new RenderTargetBitmap(720,360,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        void Write(string file,BitmapEncoder encoder,BitmapSource source){encoder.Frames.Add(BitmapFrame.Create(source));using var stream=File.Create(file);encoder.Save(stream);}
        var file=Path.Combine(host.Store.Root,"appearance-source.png");Write(file,new PngBitmapEncoder(),bitmap);
        var data=BallAppearance.Import(file);var image=BallAppearance.LoadImage(data)!;
        check(image.PixelWidth==256&&image.PixelHeight==128,"custom image is bounded and preserves aspect ratio","");
        var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixels=new byte[4];converted.CopyPixels(new Int32Rect(0,0,1,1),pixels,4,0);
        check(pixels[3]==0,"custom PNG transparency survives normalization","");
        File.Delete(file);check(!File.Exists(file)&&BallAppearance.LoadImage(data)!=null,"custom image does not lock or depend on original file","");
        foreach(var (extension,encoder) in new (string,BitmapEncoder)[]{("jpg",new JpegBitmapEncoder()),("bmp",new BmpBitmapEncoder()),("gif",new GifBitmapEncoder())})
        {
            var other=Path.Combine(host.Store.Root,"appearance-source."+extension);Write(other,encoder,bitmap);
            check(BallAppearance.LoadImage(BallAppearance.Import(other))!=null,"image import accepts "+extension,"");File.Delete(other);
        }
        var tall=BitmapSource.Create(8,1200,96,96,PixelFormats.Bgra32,null,new byte[8*1200*4],8*4);
        var tallFile=Path.Combine(host.Store.Root,"appearance-tall.png");Write(tallFile,new PngBitmapEncoder(),tall);
        var tallData=BallAppearance.Import(tallFile);var tallImage=BallAppearance.LoadImage(tallData)!;
        check(tallImage.PixelHeight==256&&tallImage.PixelWidth<=2,"portrait image remains bounded on restart","");
        File.Delete(tallFile);
        try
        {
            var window=new SettingsWindow(host,initialTab:"外观");window.Show();await Task.Delay(100);
            var editor=Find<BallAppearanceEditor>((Visual)window.Content).Single();
            Write(file,new PngBitmapEncoder(),bitmap);editor.ImportImage(file);File.Delete(file);
            var shape=Find<ComboBox>(editor).Single(c=>c.Name=="BallImageShapePicker");shape.SelectedValue="original";
            check(host.Store.Settings.BallStyle==saved.BallStyle&&host.Store.Settings.BallImage==saved.BallImage,"appearance preview leaves live settings unchanged","");
            QuickVerification.Render((FrameworkElement)window.Content,"appearance-settings.png",output);
            var rejected=false;var previous=editor.Draft.BallImage;var bad=Path.Combine(host.Store.Root,"appearance-bad.png");File.WriteAllText(bad,"invalid image");
            try{editor.ImportImage(bad);}catch(Exception e) when(e is IOException or FormatException or NotSupportedException or ArgumentException){rejected=true;}
            File.Delete(bad);check(rejected&&editor.Draft.BallImage==previous,"failed import retains previous preview image","");
            window.Close();check(host.Store.Settings.BallImage==saved.BallImage,"cancel settings does not save appearance","");
            var applyWindow=new SettingsWindow(host,initialTab:"外观");applyWindow.Show();await Task.Delay(80);
            var applyEditor=Find<BallAppearanceEditor>((Visual)applyWindow.Content).Single();
            Write(file,new PngBitmapEncoder(),bitmap);applyEditor.ImportImage(file);File.Delete(file);
            Find<ComboBox>(applyEditor).Single(c=>c.Name=="BallImageShapePicker").SelectedValue="original";
            host.Store.SaveSettings(applyEditor.Draft);host.Ball.ApplySettings();applyWindow.Close();
            var restarted=new Store(host.Store.Root);
            check(restarted.Settings.BallImage==data&&restarted.Settings.BallStyle=="custom"&&restarted.Settings.BallImageShape=="original","selected image and display mode persist after settings save","");
            check(((Border)host.Ball.Content).Child is Image&&((Border)host.Ball.Content).Clip==null,"custom original-proportion image is applied to actual ball","");
            host.Store.Settings.BallImageShape="circle";host.Ball.ApplySettings();
            check(((Border)host.Ball.Content).Clip is EllipseGeometry,"circle mode clips actual ball image","");
            host.Store.Settings.BallImageShape="original";host.Store.Settings.BallImage=tallData;host.Store.Settings.Opacity=.2;host.Ball.ApplySettings();
            await focusFixture();
            var bounds=Native.Bounds(host.Ball);var handle=new WindowInteropHelper(host.Ball).Handle;Native.GetCursorPos(out var cursor);
            try
            {
                check(Native.TestClick(handle,bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2,false),"transparent custom ball remains clickable at minimum opacity","");
                await Task.Delay(150);check(host.Surfaces.ActiveMenu?.IsOpen==true,"custom ball click opens classification menu","");
                host.Surfaces.Close();
                check(await Native.TestDragAsync(handle,bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2,-35,-20),"custom ball remains draggable","");
                check(host.Surfaces.ActiveMenu?.IsOpen!=true,"custom ball drag does not trigger menu","");
            }
            finally {Native.TestRestoreCursor(cursor);}
            host.Store.Settings.BallStyle="mint";host.Ball.ApplySettings();
            check(((Border)host.Ball.Content).Child is TextBlock,"built-in style replaces custom image","");
            var resetWindow=new SettingsWindow(host,initialTab:"外观");resetWindow.Show();await Task.Delay(60);
            var resetEditor=Find<BallAppearanceEditor>((Visual)resetWindow.Content).Single();
            Find<Button>(resetEditor).Single(b=>b.Name=="BallResetStyle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(resetEditor.Draft.BallStyle=="blue"&&resetEditor.Draft.BallImage=="","reset clears custom image and restores default pattern","");
            resetWindow.Close();
            // A corrupt stored PNG falls back to the usable default visual.
            host.Store.Settings.BallStyle="custom";host.Store.Settings.BallImage=Convert.ToBase64String(new byte[]{137,80,78,71,13,10,26,10});host.Ball.ApplySettings();
            check(((Border)host.Ball.Content).Child is TextBlock,"unreadable stored image falls back without blocking ball","");
        }
        finally {host.Surfaces.Close();host.Store.SaveSettings(saved);host.Ball.ApplySettings();Native.Move(host.Ball,(int)saved.BallX,(int)saved.BallY);}
    }
}
