using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PromptFloat.Core;

static class ApiVerification
{
    private sealed class Mock(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>respond(request,token);
    }
    public static async Task Run(Action<bool,string> check)
    {
        var agent=new AgentProfile {Model="test-model"};PolishService.Validate(agent);
        string Envelope(object content,string reason="stop")=>JsonSerializer.Serialize(new {choices=new[]{new {finish_reason=reason,message=new {content=JsonSerializer.Serialize(content)}}}});
        var payload=new {version1="原意✨\n代码",version2="精简",version3="正式"};
        var response=Envelope(payload);
        var parsed=PolishService.Parse(agent,response);
        check(parsed.Count==3&&parsed[0].Text=="原意✨\n代码","polish parses three Unicode multiline variants");
        void Reject(string r,string name){try{PolishService.Parse(agent,r);}catch(InvalidDataException){check(true,name);return;}check(false,name);}
        Reject(Envelope(new {version1="a",version2="b"}),"missing version rejected");
        Reject(Envelope(new {version1="a",version2="",version3="c"}),"empty variant rejected");
        Reject(Envelope(payload,"length"),"truncated response rejected");
        Reject("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"refused\",\"content\":null}}]}","refusal rejected");
        Reject("{bad","malformed envelope rejected");
        var plain=JsonSerializer.Serialize(new {choices=new[]{new {finish_reason="stop",message=new {content="```json\n"+JsonSerializer.Serialize(payload)+"\n```"}}}});
        check(PolishService.Parse(agent,plain).Count==3,"compatible fenced JSON accepted");
        var calls=0;
        using(var service=new PolishService(new Mock(async(req,token)=>{
            calls++;check(req.RequestUri!.AbsolutePath=="/v1/chat/completions","compatible API endpoint");
            check(req.Headers.Authorization?.Parameter=="test-secret","API bearer key only in header");
            var request=await req.Content!.ReadAsStringAsync(token);using var json=JsonDocument.Parse(request);
            check(JsonDocument.Parse(json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement.GetProperty("source").GetString()=="选中文字","request sends explicit selected source");
            check(!json.RootElement.TryGetProperty("response_format",out _),"plain compatible mode omits schema parameter");
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")};
        })))
        {var result=await service.GenerateAsync(agent,"test-secret","选中文字");check(result.Count==3&&calls==1,"one request yields three versions");}
        agent.StructuredOutput=true;
        using(var service=new PolishService(new Mock(async(req,token)=>{
            using var json=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
            check(json.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean(),"schema mode adds strict output");
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response)};
        })))await service.GenerateAsync(agent,"test","原文");
        foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.TooManyRequests,HttpStatusCode.InternalServerError})
        {
            var count=0;using var service=new PolishService(new Mock((_,_)=>{count++;return Task.FromResult(new HttpResponseMessage(code));}));
            try {await service.GenerateAsync(agent,"test","原文");check(false,"HTTP error rejected");}catch(InvalidOperationException){check(count==1,"HTTP "+(int)code+" rejected without automatic retry");}
        }
        using(var service=new PolishService(new Mock(async(_,token)=>{await Task.Delay(5000,token);return new HttpResponseMessage(HttpStatusCode.OK);})))
        {using var cancel=new CancellationTokenSource(30);try{await service.GenerateAsync(agent,"test","原文",cancel.Token);check(false,"cancel");}catch(OperationCanceledException){check(true,"polish cancellation");}}
        agent.TimeoutSeconds=1;
        using(var service=new PolishService(new Mock(async(_,token)=>{await Task.Delay(5000,token);return new HttpResponseMessage(HttpStatusCode.OK);})))
        {try{await service.GenerateAsync(agent,"test","原文");check(false,"timeout");}catch(TimeoutException){check(true,"polish timeout");}}
        var root=Path.Combine(Path.GetTempPath(),"PromptFloat-agent-test-"+Guid.NewGuid().ToString("N"));var store=new Store(root);
        check(store.Settings.Agents.Count==0&&store.Settings.AutoSelectionActions,"legacy settings initialize new fields");
        store.Settings.Agents.Add(agent);store.Settings.DefaultAgentId=agent.Id;store.SaveSettings(store.Settings);
        check(new Store(root).Settings.Agents.Single().Id==agent.Id,"agent settings persist without keys");
        agent.Styles=[new() {Name="英译",Instruction="自定义一：译成英文。"},new() {Name="要点",Instruction="自定义二：输出三条要点。"},new() {Name="改写",Instruction="自定义三：改成对话体。"}];
        store.SaveSettings(store.Settings);
        var edited=new Store(root).Settings.Agents.Single();
        check(edited.Styles.Select(x=>x.Instruction).SequenceEqual(agent.Styles.Select(x=>x.Instruction)),"all three edited style instructions survive reopening settings");
        var delayed=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var service=new PolishService(new Mock(async(req,token)=>{
            using var json=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(token));
            var instruction=json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            check(edited.Styles.Select((x,i)=>instruction.Contains("version"+(i+1)+"："+x.Name+"；"+x.Instruction)).All(x=>x),"actual request maps each edited instruction to its corresponding version");
            check(instruction.Contains("以该版本的风格要求为准")&&!instruction.Contains("你只负责润色"),"custom style precedence is explicit without hardcoded polish-only override");
            return await delayed.Task.WaitAsync(token);
        })))
        {
            var operation=service.GenerateAsync(edited,"test","选中文字");
            edited.Styles[0].Name="请求期间改名";edited.Styles[0].Instruction="请求期间编辑";
            delayed.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response)});
            var result=await operation;
            check(result[0].Name=="英译","in-flight request keeps a consistent style snapshot");
        }
        var backup=store.Backup();using(var zip=System.IO.Compression.ZipFile.OpenRead(backup))check(zip.Entries.All(e=>!e.FullName.Contains("credentials")),"backup excludes credential file");
        var category=new Category {Name="快速保存"};var prompt=new Prompt {CategoryId=category.Id,Body="选中文字",Note="短备注"};
        store.SavePrompt(prompt,category);
        check(store.Read().Categories.Any(c=>c.Id==category.Id)&&store.Read().Prompts.Any(p=>p.CategoryId==category.Id),"new category and selection save atomically");
        var before=store.Export(statistics:true);
        try{store.SavePrompt(new Prompt {CategoryId="missing",Body="原文"},new Category {Name="不留下的分类"});}catch(InvalidDataException){}
        check(store.Export(statistics:true)==before,"failed selection save rolls back new category");
    }
}
