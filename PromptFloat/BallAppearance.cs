using System.Windows.Media.Imaging;
namespace PromptFloat;

internal static class BallAppearance
{
    internal const int ImagePixels=256;
    private static string? cachedData;
    private static BitmapSource? cachedImage;
    internal static string Import(string file)
    {
        var info=new FileInfo(file);
        if(info.Length>10_000_000)throw new InvalidDataException("请选择 10 MB 以内的图片。");
        using var stream=File.OpenRead(file);
        var bitmap=Decode(stream);
        // Normalized PNG keeps transparency, drops the original path and metadata,
        // and fits inside settings.json's existing backup size limit.
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=new MemoryStream();encoder.Save(output);
        if(output.Length>300_000)throw new InvalidDataException("图片处理后仍过大，请选择更简单的图片。");
        return Convert.ToBase64String(output.ToArray());
    }
    private static BitmapSource Decode(Stream stream)
    {
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand);
        if(decoder.Frames.Count==0)throw new InvalidDataException("图片中没有可读取的画面。");
        var first=decoder.Frames[0];var width=first.PixelWidth;var height=first.PixelHeight;
        if(width<1||height<1||width>16384||height>16384||(long)width*height>32_000_000)throw new InvalidDataException("图片尺寸过大，请先缩小图片。");
        stream.Position=0;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;bitmap.StreamSource=stream;
        if(width>=height)bitmap.DecodePixelWidth=Math.Min(width,ImagePixels);
        else bitmap.DecodePixelHeight=Math.Min(height,ImagePixels);
        bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    internal static BitmapSource? LoadImage(string data)
    {
        if(data==cachedData)return cachedImage;
        cachedData=data;cachedImage=null;
        if(string.IsNullOrEmpty(data)||data.Length>400_000)return null;
        try
        {
            using var stream=new MemoryStream(Convert.FromBase64String(data));
            cachedImage=Decode(stream);
        }
        catch(Exception e) when(e is FormatException or IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) {}
        return cachedImage;
    }
    internal static void Apply(Border surface,Settings settings)
    {
        surface.Clip=null;surface.CornerRadius=new CornerRadius(99);surface.BorderThickness=new Thickness(1);
        var colors=settings.BallStyle switch {
            "mint"=>("#188A76","#A5E1CF"),"orange"=>("#CF7032","#FFD4AF"),
            "graphite"=>("#39445A","#ADB8CC"),_=>("#4267E9","#B8C7FF")};
        surface.Background=Ui.Brush(colors.Item1);surface.BorderBrush=Ui.Brush(colors.Item2);
        if(settings.BallStyle=="custom"&&LoadImage(settings.BallImage) is BitmapSource bitmap)
        {
            // An almost transparent fill makes even transparent parts draggable.
            surface.Background=Ui.Brush("#08000000");surface.BorderThickness=new Thickness(0);
            surface.Child=new Image {Source=bitmap,Stretch=settings.BallImageShape=="circle"?Stretch.UniformToFill:Stretch.Uniform,IsHitTestVisible=false};
            surface.CornerRadius=new CornerRadius(0);
            if(settings.BallImageShape=="circle")surface.Clip=new EllipseGeometry(new Point(settings.BallSize/2,settings.BallSize/2),settings.BallSize/2,settings.BallSize/2);
        }
        else surface.Child=new TextBlock {Text="P",Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontSize=settings.BallSize*.5,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
    }
}
