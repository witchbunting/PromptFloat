using Microsoft.Win32;
namespace PromptFloat;

internal sealed class TranslationSettingsEditor : StackPanel
{
    private sealed record Choice(string Id,string Name);
    private readonly Settings settings;
    private readonly ComboBox agent,language;
    private readonly TextBox compiler;
    internal TranslationSettingsEditor(Settings settings)
    {
        this.settings=settings;
        var profiles=new[]{new Choice("","跟随默认智能体")}.Concat(settings.Agents.Select(a=>new Choice(a.Id,a.Name))).ToArray();
        agent=new ComboBox {Name="TranslationAgentPicker",ItemsSource=profiles,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=profiles.Any(p=>p.Id==settings.TranslationAgentId)?settings.TranslationAgentId:""};
        language=new ComboBox {Name="TranslationLanguagePicker",IsEditable=true,ItemsSource=new[]{"简体中文","繁體中文","English","日本語","한국어","Deutsch","Français"},Text=settings.TranslationTargetLanguage};
        compiler=Ui.Field(settings.LatexCompilerPath);compiler.Name="LatexCompilerPath";
        var status=Ui.Text("留空时会在翻译预览时自动寻找本机 XeLaTeX。",12,false,"Muted");
        var select=Ui.Button("选择 xelatex.exe…",()=>{
            var dialog=new OpenFileDialog {Title="选择本机 XeLaTeX",Filter="XeLaTeX (xelatex.exe)|xelatex.exe",CheckFileExists=true};
            if(dialog.ShowDialog(Window.GetWindow(this))==true)compiler.Text=dialog.FileName;
        });
        var detect=Ui.Button("检测编译器",()=>{});detect.Click+=async(_,_)=>{
            detect.IsEnabled=false;status.Text="正在检测…";
            try{var value=compiler.Text.Trim();var found=await Task.Run(()=>LatexPreviewCompiler.Find(value));status.Text=found==null?"未找到 XeLaTeX，请安装 TeX Live 或 MiKTeX 后选择编译器。":"已找到："+found;}
            catch(Exception e){status.Text=e.Message;}
            finally{detect.IsEnabled=true;}
        };
        Loaded+=(_,_)=>{
            var current=agent.SelectedValue as string??"";
            var updated=new[]{new Choice("","跟随默认智能体")}.Concat(settings.Agents.Select(a=>new Choice(a.Id,a.Name))).ToArray();
            agent.ItemsSource=updated;agent.SelectedValue=updated.Any(p=>p.Id==current)?current:"";
        };
        Children.Add(Ui.Text("翻译与 LaTeX 预览",19,true));
        Children.Add(Ui.Text("选中文字后点击“翻译”，默认译成中文并编译预览。结果窗口可切换语言、编辑译文、重新编译和导出 PDF。"));
        Children.Add(Ui.Labeled("翻译智能体",agent));
        Children.Add(Ui.Text("沿用智能体的 API 地址、模型和密钥；使用翻译任务指令，三个润色风格不参与翻译。",12,false,"Muted"));
        Children.Add(Ui.Labeled("默认目标语言（也可输入其他语言）",language));
        Children.Add(Ui.Labeled("本机 XeLaTeX（留空自动检测）",compiler));
        Children.Add(Ui.Row(select,detect,Ui.Button("自动检测模式",()=>compiler.Text="")));Children.Add(status);
        Children.Add(Ui.Text("LaTeX 编译器单独安装，便携包不附带完整 TeX 发行版。需要 XeLaTeX 和 ctex / amsmath 等常用宏包；PDF 预览组件随软件打包，无需另外安装 PDF 阅读器。",12,false,"Muted"));
        Children.Add(Ui.Text("翻译仅在点击翻译时发送选中文字；编译和 PDF 预览在本机进行。保存后，默认语言和智能体用于下一次打开翻译窗口。",12,false,"Muted"));
    }
    internal void Apply()
    {
        settings.TranslationAgentId=agent.SelectedValue is string id&&id!=""?id:null;
        settings.TranslationTargetLanguage=language.Text.Trim();settings.LatexCompilerPath=compiler.Text.Trim().Trim('"');
    }
}
