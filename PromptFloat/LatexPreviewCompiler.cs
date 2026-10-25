using System.Text;
namespace PromptFloat;

internal sealed record CompiledTranslation(byte[] Pdf,string Source,IReadOnlyList<string> Warnings);
internal static class LatexPreviewCompiler
{
    internal static string? Find(string configured="")
    {
        if(!string.IsNullOrWhiteSpace(configured))
        {
            var path=Path.GetFullPath(configured.Trim().Trim('"'));
            return Path.GetFileName(path).Equals("xelatex.exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(path)?path:null;
        }
        var candidates=new List<string>();
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
            try{candidates.Add(Path.Combine(directory.Trim().Trim('"'),"xelatex.exe"));}catch(ArgumentException){}
        candidates.Add(Path.Combine(AppContext.BaseDirectory,"tex","bin","windows","xelatex.exe"));
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs")})
            candidates.Add(Path.Combine(root,"MiKTeX","miktex","bin","x64","xelatex.exe"));
        foreach(var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady))
        {
            var root=Path.Combine(drive.RootDirectory.FullName,"texlive");
            try
            {
                if(Directory.Exists(root))
                    foreach(var folder in Directory.GetDirectories(root).OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase).Take(4))
                        candidates.Add(Path.Combine(folder,"bin","windows","xelatex.exe"));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
        }
        return candidates.FirstOrDefault(File.Exists);
    }
    internal static async Task<CompiledTranslation> CompileAsync(string text,string language,string configured,CancellationToken cancellation=default)
    {
        var document=TranslationLatex.Build(text,language);
        var compiler=await Task.Run(()=>Find(configured),cancellation).ConfigureAwait(false);
        if(compiler==null)throw new FileNotFoundException("未找到 XeLaTeX。请在设置 → 翻译预览中选择 xelatex.exe；译文仍可复制。");
        var work=Path.Combine(Path.GetTempPath(),"PromptFloat-translation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(60));
        Process? process=null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(work,"preview.tex"),document.Source,new UTF8Encoding(false),timeout.Token).ConfigureAwait(false);
            var start=new ProcessStartInfo(compiler) {WorkingDirectory=work,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"-no-shell-escape","-interaction=nonstopmode","-halt-on-error","-file-line-error","preview.tex"})start.ArgumentList.Add(argument);
            if(compiler.Contains("miktex",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Insert(0,"--disable-installer");
            start.Environment["shell_escape"]="f";start.Environment["openout_any"]="p";start.Environment["openin_any"]="p";start.Environment["TEXMFOUTPUT"]=work;
            process=Process.Start(start)??throw new InvalidOperationException("无法启动 XeLaTeX。");
            var running=process;
            using var stop=timeout.Token.Register(()=>{
                try{if(!running.HasExited)running.Kill(true);}
                catch(Exception e) when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}
            });
            var output=DrainAsync(process.StandardOutput);var error=DrainAsync(process.StandardError);
            try{await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);}
            catch(OperationCanceledException)
            {
                try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
                await process.WaitForExitAsync().ConfigureAwait(false);await Task.WhenAll(output,error).ConfigureAwait(false);throw;
            }
            var log=await output.ConfigureAwait(false);await error.ConfigureAwait(false);timeout.Token.ThrowIfCancellationRequested();
            var file=Path.Combine(work,"preview.pdf");
            if(process.ExitCode!=0||!File.Exists(file))
                throw new InvalidDataException("LaTeX 编译失败。请检查译文中的公式，并确认已安装 ctex、amsmath、amssymb、bm 和 mathtools 宏包。");
            if(new FileInfo(file).Length>20_000_000)throw new InvalidDataException("预览 PDF 过大，请缩短译文后重试。");
            var warnings=document.Warnings.ToList();
            if(log.Contains("Missing character:",StringComparison.Ordinal))warnings.Add("部分字符缺少字体，PDF 中可能无法显示；复制译文可保留原字符。");
            return new CompiledTranslation(await File.ReadAllBytesAsync(file,timeout.Token).ConfigureAwait(false),document.Source,warnings);
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested){throw new TimeoutException("LaTeX 编译超过 60 秒，已停止；可修改译文后重新编译。");}
        finally
        {
            process?.Dispose();
            // Only remove the unique temporary directory created by this operation.
            var full=Path.GetFullPath(work);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("PromptFloat-translation-",StringComparison.Ordinal))
                try{Directory.Delete(full,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
        }
    }
    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var result=new StringBuilder();var buffer=new char[4096];int count;
        while((count=await reader.ReadAsync(buffer).ConfigureAwait(false))>0)if(result.Length<65536)result.Append(buffer,0,Math.Min(count,65536-result.Length));
        return result.ToString();
    }
}
