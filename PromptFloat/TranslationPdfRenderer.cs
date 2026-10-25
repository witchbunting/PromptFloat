using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
namespace PromptFloat;

internal sealed record TranslationPage(BitmapSource Image,int PageCount,int PageIndex);
internal static class TranslationPdfRenderer
{
    // PDFium is not thread-safe. Each render owns and releases every native
    // handle before another request enters; only the frozen image leaves here.
    private static readonly SemaphoreSlim gate=new(1,1);
    internal static async Task<TranslationPage> RenderAsync(byte[] pdf,int pageIndex,CancellationToken cancellation=default)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try{return await Task.Run(()=>Render(pdf,pageIndex,cancellation),cancellation).ConfigureAwait(false);}
        finally{gate.Release();}
    }
    private static TranslationPage Render(byte[] pdf,int pageIndex,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var pinned=GCHandle.Alloc(pdf,GCHandleType.Pinned);
        IntPtr document=IntPtr.Zero,page=IntPtr.Zero,bitmap=IntPtr.Zero;var initialized=false;
        try
        {
            FPDF_InitLibrary();initialized=true;
            document=FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(),(nuint)pdf.Length,IntPtr.Zero);
            if(document==IntPtr.Zero)throw new InvalidDataException("无法读取编译生成的 PDF。");
            var count=FPDF_GetPageCount(document);if(count<1)throw new InvalidDataException("PDF 没有可预览页面。");
            var index=Math.Clamp(pageIndex,0,count-1);page=FPDF_LoadPage(document,index);
            if(page==IntPtr.Zero)throw new InvalidDataException("无法读取 PDF 页面。");
            var width=FPDF_GetPageWidth(page);var height=FPDF_GetPageHeight(page);
            if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new InvalidDataException("PDF 页面尺寸无效。");
            const int pixelsWide=1400;
            var scaledHeight=Math.Ceiling(pixelsWide*height/width);
            if(scaledHeight<1||scaledHeight>25_000_000d/(pixelsWide*4))throw new InvalidDataException("PDF 页面尺寸过大。");
            var pixelsHigh=(int)scaledHeight;
            bitmap=FPDFBitmap_Create(pixelsWide,pixelsHigh,1);
            if(bitmap==IntPtr.Zero)throw new InvalidDataException("无法创建 PDF 预览图像。");
            FPDFBitmap_FillRect(bitmap,0,0,pixelsWide,pixelsHigh,0xffffffff);
            cancellation.ThrowIfCancellationRequested();
            FPDF_RenderPageBitmap(bitmap,page,0,0,pixelsWide,pixelsHigh,0,2);
            cancellation.ThrowIfCancellationRequested();
            var stride=FPDFBitmap_GetStride(bitmap);var buffer=FPDFBitmap_GetBuffer(bitmap);
            var bytes=new byte[checked(stride*pixelsHigh)];Marshal.Copy(buffer,bytes,0,bytes.Length);
            var image=BitmapSource.Create(pixelsWide,pixelsHigh,96,96,PixelFormats.Bgra32,null,bytes,stride);image.Freeze();
            return new TranslationPage(image,count,index);
        }
        finally
        {
            if(bitmap!=IntPtr.Zero)FPDFBitmap_Destroy(bitmap);
            if(page!=IntPtr.Zero)FPDF_ClosePage(page);
            if(document!=IntPtr.Zero)FPDF_CloseDocument(document);
            if(initialized)FPDF_DestroyLibrary();
            pinned.Free();
        }
    }
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDF_InitLibrary();
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDF_DestroyLibrary();
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data,nuint size,IntPtr password);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr FPDF_LoadPage(IntPtr document,int index);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr FPDFBitmap_Create(int width,int height,int alpha);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDFBitmap_FillRect(IntPtr bitmap,int left,int top,int width,int height,uint color);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDF_RenderPageBitmap(IntPtr bitmap,IntPtr page,int left,int top,int width,int height,int rotate,int flags);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
}
