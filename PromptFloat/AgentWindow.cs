using System.Text.Json;
namespace PromptFloat;

internal sealed class AgentWindow : Window
{
    public AgentWindow(Settings settings,CredentialVault vault,Dictionary<string,string> keyChanges,Window owner,Action<List<AgentProfile>,string?,IReadOnlyDictionary<string,string>>? saveImmediately=null)
    {
        Title="PromptFloat · 智能体";Width=860;Height=790;MinWidth=740;MinHeight=620;Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var agents=JsonSerializer.Deserialize<List<AgentProfile>>(JsonSerializer.Serialize(settings.Agents,Store.Json),Store.Json)!;
        var pending=new Dictionary<string,string>(keyChanges);
        AgentProfile? current=null;bool loading=false,keyEdited=false;
        var defaultId=settings.DefaultAgentId;
        var list=new ListBox {Name="AgentList",Width=170,DisplayMemberPath="Name",ItemsSource=agents};
        var name=Ui.Field();var url=Ui.Field();var model=Ui.Field();var system=Ui.Field("",true,85);var key=new PasswordBox {Height=32};
        system.Name="AgentSystemPrompt";
        var schema=new CheckBox {Content="接口支持 JSON Schema 结构化输出"};var defaultAgent=new CheckBox {Name="DefaultAgent",Content="设为默认智能体"};
        var timeout=Ui.Field("60");var fields=new List<(TextBox Name,TextBox Instruction)>();
        var status=Ui.Text("",12,false,"Muted");
        var details=Ui.Stack(
            Ui.Text("润色只在点击时发送选中文字至此 API。保存后立即生效。",13,false,"Muted"),
            Ui.Text("每个版本优先执行对应风格要求；未特别指定时保留原意、事实和语言。",12,false,"Muted"),
            Ui.Labeled("名称",name),Ui.Labeled("API 地址（例如 https://api.openai.com/v1）",url),
            Ui.Labeled("模型名称",model),Ui.Labeled("API Key（当前 Windows 用户加密；备份不含密钥）",key),
            Ui.Labeled("系统提示词",system),schema,defaultAgent,Ui.Labeled("超时秒数（1–300）",timeout));
        for(var i=0;i<3;i++){var n=Ui.Field();n.Name="StyleName"+(i+1);var instruction=Ui.Field("",true,55);instruction.Name="StyleInstruction"+(i+1);fields.Add((n,instruction));details.Children.Add(Ui.Labeled("风格 "+(i+1),Ui.Stack(n,instruction)));}
        void Pull()
        {
            if(current==null)return;
            current.Name=name.Text.Trim();current.BaseUrl=url.Text.Trim();current.Model=model.Text.Trim();current.SystemPrompt=system.Text;current.StructuredOutput=schema.IsChecked==true;
            if(!int.TryParse(timeout.Text,out var seconds))throw new ArgumentException("超时必须是整数。");current.TimeoutSeconds=seconds;
            current.Styles=fields.Select(x=>new PolishStyle {Name=x.Name.Text.Trim(),Instruction=x.Instruction.Text}).ToList();
            if(keyEdited)pending[current.Id]=key.Password;
        }
        void Load(AgentProfile? a)
        {
            loading=true;current=a;details.IsEnabled=a!=null;
            if(a!=null){name.Text=a.Name;url.Text=a.BaseUrl;model.Text=a.Model;system.Text=a.SystemPrompt;schema.IsChecked=a.StructuredOutput;defaultAgent.IsChecked=defaultId==a.Id;timeout.Text=a.TimeoutSeconds.ToString();key.Password=pending.TryGetValue(a.Id,out var value)?value:vault.Get(a.Id);for(var i=0;i<3;i++){fields[i].Name.Text=a.Styles[i].Name;fields[i].Instruction.Text=a.Styles[i].Instruction;}}
            keyEdited=false;loading=false;
        }
        key.PasswordChanged+=(_,_)=>{if(!loading)keyEdited=true;};
        defaultAgent.Checked+=(_,_)=>{if(!loading&&current!=null)defaultId=current.Id;};
        defaultAgent.Unchecked+=(_,_)=>{if(!loading&&current!=null&&defaultId==current.Id)defaultId=null;};
        list.SelectionChanged+=(_,_)=>{if(loading)return;try{Pull();Load(list.SelectedItem as AgentProfile);if(current!=null){loading=true;defaultAgent.IsChecked=defaultId==current.Id;loading=false;}}catch(Exception e){status.Text=e.Message;}};
        details.Children.Add(Ui.Button("测试连接（发送一段示例文字）",async()=>{
            try{Pull();if(current==null)return;status.Text="正在测试…";using var service=new PolishService();var secret=pending.TryGetValue(current.Id,out var value)?value:vault.Get(current.Id);var result=await service.GenerateAsync(current,secret,"请把这段示例文字写得清楚自然。");status.Text="连接成功：返回 "+result.Count+" 个有效版本。";}
            catch(Exception e){status.Text=e.Message;}
        }));details.Children.Add(status);
        var dock=new DockPanel {Margin=new Thickness(20)};
        var buttons=Ui.Row(Ui.Button("新增智能体",()=>{Pull();var a=new AgentProfile();agents.Add(a);list.Items.Refresh();list.SelectedItem=a;}),Ui.Button("删除当前",()=>{if(current==null)return;var id=current.Id;agents.Remove(current);pending[id]="";if(defaultId==id)defaultId=null;current=null;list.Items.Refresh();list.SelectedIndex=agents.Count>0?0:-1;Load(list.SelectedItem as AgentProfile);}),
            Ui.Button("取消",()=>Close()),Ui.Button("保存并立即生效",()=>{
                Pull();foreach(var a in agents)PolishService.Validate(a);
                var chosenDefault=defaultId??agents.FirstOrDefault()?.Id;
                saveImmediately?.Invoke(agents,chosenDefault,pending);
                settings.Agents=agents;settings.DefaultAgentId=chosenDefault;
                keyChanges.Clear();if(saveImmediately==null)foreach(var pair in pending)keyChanges[pair.Key]=pair.Value;DialogResult=true;
            },true));
        DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);DockPanel.SetDock(list,Dock.Left);dock.Children.Add(list);dock.Children.Add(new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(16,0,0,0)});Content=dock;
        if(agents.Count>0)list.SelectedIndex=0;else Load(null);
    }
}
