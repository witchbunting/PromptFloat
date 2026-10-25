using Microsoft.Win32;
namespace PromptFloat;

internal sealed class TranslationWindow : Window
{
    private readonly Host host;
    private readonly string original;
    private readonly ComboBox language,agent;
    private readonly ComboBox zoom=new() {Name="TranslationPreviewZoom",ItemsSource=new[]{"适应宽度","100%","125%","150%","200%"},SelectedIndex=0,Width=110,Margin=new Thickness(4)};
    private readonly TextBox translated=Ui.Field("",true),source=Ui.Field("",true);
    private readonly Image pageImage=new() {Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Top};
    private readonly ScrollViewer preview=new() {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock status=Ui.Text("准备翻译",12,false,"Muted"),pageLabel=Ui.Text("尚未编译",12,false,"Muted");
    private readonly Button translate,compile,cancel,previous,next,export;
    private CancellationTokenSource? operation;
    private byte[]? pdf;
    private bool busy,closed,updating,previewCurrent;
    private int pageIndex,pageCount;
    private string resultLanguage="";
    internal bool PreviewReady=>pdf!=null&&previewCurrent&&pageImage.Source!=null;
    internal string ResultText=>translated.Text;
    internal byte[]? PdfBytes=>pdf;
    internal string LatexSource=>source.Text;
    internal bool Busy=>busy;
    internal FrameworkElement PreviewVisual=>pageImage;
    internal TranslationWindow(Host host,string selectedText,bool autoStart=true)
    {
        this.host=host;original=selectedText;
        Title="PromptFloat · 翻译与 LaTeX 预览";Width=Math.Min(1120,SystemParameters.WorkArea.Width-40);Height=Math.Min(800,SystemParameters.WorkArea.Height-40);MinWidth=760;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        language=new ComboBox {IsEditable=true,ItemsSource=new[]{"简体中文","繁體中文","English","日本語","한국어","Deutsch","Français"},Text=host.Store.Settings.TranslationTargetLanguage,MinWidth=140,Margin=new Thickness(3)};
        var profiles=host.Store.Settings.Agents;
        var selected=profiles.FirstOrDefault(a=>a.Id==host.Store.Settings.TranslationAgentId)??profiles.FirstOrDefault(a=>a.Id==host.Store.Settings.DefaultAgentId)??profiles.FirstOrDefault();
        agent=new ComboBox {ItemsSource=profiles,DisplayMemberPath="Name",SelectedItem=selected,MinWidth=160,MaxWidth=240,Margin=new Thickness(3)};
        translate=Ui.Button("翻译",async()=>await TranslateAsync(),true);translate.Name="TranslateSelection";
        compile=Ui.Button("编译预览",async()=>await CompileAsync());compile.Name="CompileTranslation";
        cancel=Ui.Button("取消",()=>operation?.Cancel());
        previous=Ui.Button("上一页",async()=>await ShowPageAsync(pageIndex-1));next=Ui.Button("下一页",async()=>await ShowPageAsync(pageIndex+1));
        export=Ui.Button("导出 PDF",SavePdf);export.Name="ExportTranslationPdf";
        var dock=new DockPanel {Margin=new Thickness(18)};
        TextBlock Label(string text){var label=Ui.Text(text,12);label.VerticalAlignment=VerticalAlignment.Center;return label;}
        var heading=Ui.Stack(Ui.Text("翻译与 LaTeX 预览",23,true),Ui.Row(Label("译成"),language,Label("智能体"),agent,translate,compile,cancel),
            Ui.Row(Ui.Button("复制译文",async()=>{try{await InputBridge.CopyAsync(translated.Text);}catch{status.Text="复制失败，请稍后重试。";}}),export,Ui.Button("翻译与编译设置",()=>host.OpenSettings("翻译预览"))),status);
        DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        var grid=new Grid {Margin=new Thickness(0,10,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(3,GridUnitType.Star)});
        var left=new DockPanel {Margin=new Thickness(0,0,10,0)};
        var originalText=Ui.Field(original,true,110);originalText.IsReadOnly=true;
        var originalBox=new Expander {Header="查看原文",Content=originalText,Margin=new Thickness(0,0,0,8)};
        DockPanel.SetDock(originalBox,Dock.Top);left.Children.Add(originalBox);
        translated.FontSize=14;translated.Name="TranslationText";source.Name="TranslationLatexSource";source.IsReadOnly=true;source.FontFamily=new FontFamily("Consolas");source.FontSize=12;
        var tabs=new TabControl();tabs.Items.Add(new TabItem {Header="译文（可编辑）",Content=translated});tabs.Items.Add(new TabItem {Header="LaTeX 源码",Content=source});left.Children.Add(tabs);grid.Children.Add(left);
        var right=new DockPanel();Grid.SetColumn(right,1);
        var navigation=Ui.Row(previous,pageLabel,next,zoom);DockPanel.SetDock(navigation,Dock.Top);right.Children.Add(navigation);
        preview.Background=Ui.Brush("#DCE2ED");preview.Content=pageImage;right.Children.Add(preview);grid.Children.Add(right);dock.Children.Add(grid);Content=dock;
        preview.SizeChanged+=(_,_)=>FitPage();zoom.SelectionChanged+=(_,_)=>FitPage();
        translated.TextChanged+=(_,_)=>{
            if(updating)return;
            previewCurrent=false;pdf=null;pageImage.Source=null;source.Text="";pageLabel.Text="待重新编译";
            status.Text="译文已修改，点击“编译预览”更新 PDF。";Refresh();
        };
        Closed+=(_,_)=>{closed=true;operation?.Cancel();operation?.Dispose();pdf=null;pageImage.Source=null;};
        Activated+=(_,_)=>ReloadAgents();
        Loaded+=async(_,_)=>{if(autoStart)await TranslateAsync();};
        Refresh();
    }
    private void ReloadAgents()
    {
        if(busy||closed)return;
        var id=(agent.SelectedItem as AgentProfile)?.Id??host.Store.Settings.TranslationAgentId;
        var profiles=host.Store.Settings.Agents;agent.ItemsSource=profiles;
        agent.SelectedItem=profiles.FirstOrDefault(a=>a.Id==id)??profiles.FirstOrDefault(a=>a.Id==host.Store.Settings.DefaultAgentId)??profiles.FirstOrDefault();
    }
    private void FitPage(){pageImage.Width=zoom.SelectedIndex<=0?Math.Max(200,preview.ActualWidth-30):210d/25.4*96*new[]{1d,1.25,1.5,2}[zoom.SelectedIndex-1];}
    private void Refresh()
    {
        translate.IsEnabled=!busy;compile.IsEnabled=!busy&&!string.IsNullOrWhiteSpace(translated.Text);cancel.IsEnabled=busy;
        language.IsEnabled=agent.IsEnabled=!busy;
        previous.IsEnabled=!busy&&PreviewReady&&pageIndex>0;next.IsEnabled=!busy&&PreviewReady&&pageIndex+1<pageCount;export.IsEnabled=!busy&&pdf!=null&&previewCurrent;
        translated.IsReadOnly=busy;
    }
    private CancellationToken Begin()
    {
        operation?.Cancel();operation?.Dispose();operation=new CancellationTokenSource();busy=true;Refresh();return operation.Token;
    }
    private void Finish(){busy=false;if(!closed)Refresh();}
    internal async Task TranslateAsync()
    {
        if(busy||closed)return;
        if(agent.SelectedItem is not AgentProfile chosen){status.Text="请先在设置 → 智能体中配置翻译使用的接口、模型和密钥。";return;}
        var profile=host.Store.Settings.Agents.FirstOrDefault(a=>a.Id==chosen.Id);
        if(profile==null){status.Text="该智能体已删除，请重新打开翻译窗口。";return;}
        var target=language.Text.Trim();var token=Begin();previewCurrent=false;pdf=null;pageImage.Source=null;source.Text="";pageLabel.Text="正在翻译";
        try
        {
            status.Text="正在翻译为 "+target+"…";
            var result=await host.Translation.TranslateAsync(profile,host.Credentials.Get(profile.Id),original,target,token);
            token.ThrowIfCancellationRequested();if(closed)return;
            resultLanguage=target;updating=true;try{translated.Text=result;}finally{updating=false;}
            await CompileCoreAsync(token);
        }
        catch(OperationCanceledException){if(!closed){status.Text="已取消，原文保持不变。";pageLabel.Text="已取消";}}
        catch(Exception e){if(!closed){status.Text=e.Message;pageLabel.Text=pdf!=null&&previewCurrent?"PDF 已生成":"尚未生成预览";}}
        finally {Finish();}
    }
    internal async Task CompileAsync()
    {
        if(busy||closed)return;
        var token=Begin();
        try
        {
            resultLanguage=language.Text.Trim();
            await CompileCoreAsync(token);
        }
        catch(OperationCanceledException){if(!closed){status.Text="已取消编译；译文仍可复制。";pageLabel.Text="已取消";}}
        catch(Exception e){if(!closed){status.Text=e.Message;pageLabel.Text=pdf!=null&&previewCurrent?"PDF 已生成":"尚未生成预览";}}
        finally {Finish();}
    }
    private async Task CompileCoreAsync(CancellationToken token)
    {
        previewCurrent=false;pdf=null;pageImage.Source=null;pageLabel.Text="正在编译";status.Text="译文已就绪，正在使用 XeLaTeX 编译…";
        source.Text=TranslationLatex.Build(translated.Text,resultLanguage).Source;
        var result=await LatexPreviewCompiler.CompileAsync(translated.Text,resultLanguage,host.Store.Settings.LatexCompilerPath,token);
        token.ThrowIfCancellationRequested();if(closed)return;
        pdf=result.Pdf;previewCurrent=true;
        var rendered=await TranslationPdfRenderer.RenderAsync(result.Pdf,0,token);
        token.ThrowIfCancellationRequested();if(closed)return;
        pageCount=rendered.PageCount;pageIndex=0;pageImage.Source=rendered.Image;FitPage();
        pageLabel.Text=$"第 1 / {pageCount} 页";status.Text=result.Warnings.Count==0?"编译完成。可复制译文或导出 PDF。":string.Join(" ",result.Warnings);
    }
    private async Task ShowPageAsync(int index)
    {
        if(busy||closed||pdf==null||!previewCurrent)return;
        var bytes=pdf;var token=Begin();
        try
        {
            var page=await TranslationPdfRenderer.RenderAsync(bytes,index,token);
            token.ThrowIfCancellationRequested();if(closed)return;
            pageImage.Source=page.Image;pageIndex=page.PageIndex;pageLabel.Text=$"第 {pageIndex+1} / {pageCount} 页";FitPage();
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(!closed)status.Text=e.Message;}
        finally {Finish();}
    }
    private void SavePdf()
    {
        if(pdf==null||!previewCurrent)return;
        var dialog=new SaveFileDialog {Filter="PDF 文档 (*.pdf)|*.pdf",FileName="译文.pdf",DefaultExt=".pdf"};
        if(dialog.ShowDialog(this)==true)try{File.WriteAllBytes(dialog.FileName,pdf);status.Text="PDF 已导出。";}catch(Exception e){status.Text="导出失败："+e.Message;}
    }
}
