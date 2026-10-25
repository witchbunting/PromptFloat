using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
namespace PromptFloat;

internal static class TranslationVerification
{
    private sealed class MockTranslation(string text,bool wait=false) : HttpMessageHandler
    {
        internal int Requests;internal bool Cancelled;internal string? SentSource;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            using var message=JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            SentSource=message.RootElement.GetProperty("source").GetString();
            try{await Task.Delay(wait?Timeout.Infinite:450,token);}
            catch(OperationCanceledException){Cancelled=true;throw;}
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new {choices=new[]{new {finish_reason="stop",message=new {content=JsonSerializer.Serialize(new {translation=text})}}}}))};
        }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T item)yield return item;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal static async Task Run(Host host,IntPtr fixture,Func<int,string,int,int,string,Task> command,Func<string,Task<string>> text,Action<bool,string,string> check,string output,bool requireCompiler=false)
    {
        var saved=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(host.Store.Settings,Store.Json),Store.Json)!;
        var field=typeof(Host).GetField("translation",BindingFlags.Instance|BindingFlags.NonPublic)!;var savedService=field.GetValue(host);
        var agent=new AgentProfile {Name="翻译模拟接口",Model="mock-model",BaseUrl="https://promptfloat.invalid/v1"};
        var translated="对于参数 \\(\\theta\\)，量子态为 \\(\\rho_\\theta\\)。量子 Fisher 信息满足\n"+
            "\\[F_Q(\\rho_\\theta)=\\operatorname{Tr}(\\rho_\\theta L_\\theta^2),\\quad \\partial_\\theta \\rho_\\theta=\\frac{L_\\theta\\rho_\\theta+\\rho_\\theta L_\\theta}{2}.\\]\n"+
            "原文给出的概率满足 50% 的示例条件，保留符号 a_b & c。";
        using var handler=new MockTranslation(translated);using var service=new TranslationService(handler);
        TranslationWindow? window=null;
        try
        {
            host.Store.Settings.Agents=[agent];host.Store.Settings.DefaultAgentId=agent.Id;host.Store.Settings.TranslationAgentId=null;host.Store.Settings.TranslationTargetLanguage="简体中文";host.Store.Settings.LatexCompilerPath="";
            host.Credentials.Save(new Dictionary<string,string>{{agent.Id,"synthetic-translation-key"}});field.SetValue(host,service);
            const string selection="The quantum state is rho(theta); preserve the equation.";
            await command(301,"Before "+selection+" After",7,selection.Length,"plain");await Task.Delay(320);
            var snapshot=await host.Input.SnapshotAsync();check(snapshot?.Text==selection,"translation has isolated original selection","");
            host.Surfaces.SelectionAvailable(snapshot!);await Task.Delay(80);
            var action=Find<Button>(host.Surfaces.ActiveVisual!).Single(b=>b.Content as string=="翻译");
            check(Native.GetForegroundWindow()==fixture,"translation action bubble preserves editor foreground","");
            var point=action.PointToScreen(new Point(action.ActualWidth/2,action.ActualHeight/2));
            var actionWindow=((HwndSource)PresentationSource.FromVisual(action)).Handle;Native.GetCursorPos(out var cursor);
            try{check(Native.TestClick(actionWindow,(int)point.X,(int)point.Y,false),"real selection translation action dispatches","");await Task.Delay(120);}
            finally{Native.TestRestoreCursor(cursor);}
            window=host.ActiveTranslation!;check(window!=null&&handler.Requests==1,"selection action opens one translation window and one request","");
            await window!.TranslateAsync();check(handler.Requests==1,"duplicate translation click does not send second request","");
            await command(302,"Original edited while translating",0,0,"plain");
            var compiler=await Task.Run(()=>LatexPreviewCompiler.Find());
            for(var i=0;i<600&&window.Busy;i++)await Task.Delay(100);
            check(!window.Busy&&window.ResultText==translated&&handler.SentSource==selection,"translation uses frozen selection despite source edit","");
            check(await text("text")=="Original edited while translating","translation never replaces source text","");
            if(compiler!=null)
            {
                check(window.PreviewReady,"API translation compiles and renders a real PDF","");
                check(window.PdfBytes!=null&&System.Text.Encoding.ASCII.GetString(window.PdfBytes,0,5)=="%PDF-","preview is compiled PDF rather than text markup","");
                check(Native.GetForegroundWindow()==fixture,"translation completion does not reactivate result window","");
                await File.WriteAllBytesAsync(Path.Combine(output,"translation-preview.pdf"),window.PdfBytes!);
                await File.WriteAllTextAsync(Path.Combine(output,"translation-preview.tex"),window.LatexSource);
                QuickVerification.Render((FrameworkElement)window.Content,"translation-window.png",output);
                QuickVerification.Render(window.PreviewVisual,"translation-page.png",output);
                var zoom=Find<ComboBox>((Visual)window.Content).Single(c=>c.Name=="TranslationPreviewZoom");
                zoom.SelectedIndex=3;await Task.Delay(50);
                check(((Image)window.PreviewVisual).Width>1000,"PDF preview supports readable zoom","");
                zoom.SelectedIndex=0;
                var before=host.Store.Read().Prompts.Sum(p=>p.Uses);
                Find<Button>((Visual)window.Content).Single(b=>b.Content as string=="复制译文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(300);check(Clipboard.GetText()==translated&&host.Store.Read().Prompts.Sum(p=>p.Uses)==before,"copy translation preserves formulas without template usage","");
                var editor=Find<TextBox>((Visual)window.Content).Single(t=>t.Name=="TranslationText");editor.Text+="\n编辑后的译文。";
                check(!window.PreviewReady&&!Find<Button>((Visual)window.Content).Single(b=>b.Name=="ExportTranslationPdf").IsEnabled,"edited translation invalidates old PDF export","");
                await window.CompileAsync();check(window.PreviewReady,"edited translation recompiles without API request","");
                check(handler.Requests==1,"local recompile never repeats API request","");
                var multipage=await LatexPreviewCompiler.CompileAsync(string.Join("\n\n",Enumerable.Repeat("用于分页验证的译文段落。公式 \\(E=mc^2\\)。",60)),"简体中文",compiler);
                var second=await TranslationPdfRenderer.RenderAsync(multipage.Pdf,1);
                check(second.PageCount>1&&second.PageIndex==1,"PDF renderer supports multiple pages","");
                var beforeTemp=Directory.GetDirectories(Path.GetTempPath(),"PromptFloat-translation-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
                using var stopCompile=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                var stopped=false;
                try{await LatexPreviewCompiler.CompileAsync(string.Join("\n",Enumerable.Repeat("取消测试公式 \\(x^2+y^2=1\\)。",1000)),"简体中文",compiler,stopCompile.Token);}
                catch(OperationCanceledException){stopped=true;}
                check(stopped&&!Directory.GetDirectories(Path.GetTempPath(),"PromptFloat-translation-*").Except(beforeTemp,StringComparer.OrdinalIgnoreCase).Any(),"cancelled XeLaTeX compilation cleans its temporary directory","");

            }
            else
            {
                check(!requireCompiler,"LaTeX compiler required for this validation run","");
                check(!window.PreviewReady&&window.ResultText==translated,"missing compiler retains copyable translation","");
            }
            host.Store.Settings.LatexCompilerPath=Path.Combine(host.Store.Root,"not-installed","xelatex.exe");
            await window.CompileAsync();check(!window.PreviewReady&&window.ResultText.Contains("量子态"),"missing configured compiler keeps translated text","");
            window.Close();window=null;
            using var slowHandler=new MockTranslation(translated,true);using var slowService=new TranslationService(slowHandler);field.SetValue(host,slowService);
            var cancelled=new TranslationWindow(host,selection,false);cancelled.Show();await Task.Delay(40);
            var operation=cancelled.TranslateAsync();await Task.Delay(60);cancelled.Close();await operation;
            check(slowHandler.Cancelled&&slowHandler.Requests==1,"closing translation window cancels pending API request","");
            host.Store.Settings.Agents=[];host.Store.Settings.DefaultAgentId=null;
            var missing=new TranslationWindow(host,selection,false);missing.Show();await missing.TranslateAsync();
            check(!missing.Busy&&slowHandler.Requests==1,"missing translation agent is handled without request","");missing.Close();
            File.WriteAllText(Path.Combine(output,"translation-tests.json"),JsonSerializer.Serialize(new {
                mockApi=true,realPaidApi=false,realXeLatex=compiler!=null,pdfiumCpuPreview=compiler!=null,
                originalPreserved=true,cancelOnClose=true,duplicateRequestProtected=true,localRecompile=true
            },Store.Json));
        }
        finally
        {
            window?.Close();field.SetValue(host,savedService);host.Store.SaveSettings(saved);host.Ball.ApplySettings();host.Surfaces.Close();
        }
    }
}
