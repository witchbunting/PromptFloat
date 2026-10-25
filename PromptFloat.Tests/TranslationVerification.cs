using PromptFloat.Core;
using System.Net;
using System.Text.Json;
namespace PromptFloat.Tests;

internal static class TranslationVerification
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Count++;return respond(request,token);}
    }
    private static string Envelope(string content,string finish="stop")=>JsonSerializer.Serialize(new {choices=new[]{new {finish_reason=finish,message=new {content}}}});
    private static void Reject(Action action,Action<bool,string> check,string name)
    {
        try{action();}catch(InvalidDataException){check(true,name);return;}check(false,name);
    }
    internal static async Task Run(Action<bool,string> check)
    {
        var agent=new AgentProfile {Model="mock-model",SystemPrompt="保留物理术语"};
        var translated="量子态为 \\(\\rho\\)。\n\\[F=\\frac{1}{2}\\sum_i p_i^2\\]";
        check(TranslationService.Parse(Envelope(JsonSerializer.Serialize(new {translation=translated})))==translated,"translation parses Chinese multiline LaTeX");
        check(TranslationService.Parse(Envelope("```json\n"+JsonSerializer.Serialize(new {translation="译文"})+"\n```"))=="译文","translation accepts compatible JSON fences");
        Reject(()=>TranslationService.Parse(Envelope("{\"translation\":\"\"}")),check,"empty translation rejected");
        Reject(()=>TranslationService.Parse(Envelope("{\"translation\":\"ok\",\"extra\":1}")),check,"unexpected translation fields rejected");
        Reject(()=>TranslationService.Parse(Envelope("{\"translation\":\"ok\"}","length")),check,"truncated translation rejected");
        Reject(()=>TranslationService.Parse("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"refused\",\"content\":null}}]}"),check,"translation refusal rejected");
        Reject(()=>TranslationService.Parse("not JSON"),check,"malformed translation rejected");
        Reject(()=>TranslationService.Parse(Envelope(JsonSerializer.Serialize(new {translation=new string('a',100001)}))),check,"oversized translation rejected");
        var seenSource=false;var seenKey=false;var schema=false;var properEndpoint=false;var preferences=false;
        using var handler=new Handler(async(request,token)=>{
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=json.RootElement;
            using var data=JsonDocument.Parse(root.GetProperty("messages")[1].GetProperty("content").GetString()!);
            seenSource=data.RootElement.GetProperty("source").GetString()=="Only this selection"&&data.RootElement.GetProperty("targetLanguage").GetString()=="简体中文";
            seenKey=request.Headers.Authorization?.Parameter=="synthetic-translation-key"&&!root.GetRawText().Contains("synthetic-translation-key");
            schema=root.TryGetProperty("response_format",out var format)&&format.GetProperty("json_schema").GetProperty("schema").GetProperty("required")[0].GetString()=="translation";
            properEndpoint=request.RequestUri!.AbsolutePath=="/v1/chat/completions";
            var instruction=root.GetProperty("messages")[0].GetProperty("content").GetString()!;
            preferences=instruction.Contains("保留物理术语")&&!instruction.Contains(agent.Styles[0].Instruction);
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(Envelope(JsonSerializer.Serialize(new {translation=translated})))};
        });
        using var service=new TranslationService(handler);
        check(await service.TranslateAsync(agent,"synthetic-translation-key","Only this selection","简体中文")==translated&&!schema,"compatible translation without schema");
        check(seenSource&&seenKey&&properEndpoint&&preferences,"translation sends selected source and preferences with header-only key");
        agent.StructuredOutput=true;await service.TranslateAsync(agent,"synthetic-translation-key","Only this selection","简体中文");check(schema,"translation structured output requests one strict field");
        foreach(var code in new[]{401,429,500,302})
        {
            using var failure=new Handler((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)));
            using var failedService=new TranslationService(failure);var rejected=false;
            try{await failedService.TranslateAsync(agent,"key","source","English");}catch(InvalidOperationException){rejected=true;}
            check(rejected&&failure.Count==1,"translation HTTP "+code+" fails without retry");
        }
        using var delay=new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);throw new Exception();});
        using var delayed=new TranslationService(delay);
        using var cancellation=new CancellationTokenSource();var pending=delayed.TranslateAsync(agent,"key","source","English",cancellation.Token);cancellation.Cancel();
        var cancelled=false;try{await pending;}catch(OperationCanceledException){cancelled=true;}check(cancelled,"translation supports cancellation");
        agent.TimeoutSeconds=1;var timedOut=false;try{await delayed.TranslateAsync(agent,"key","source","English");}catch(TimeoutException){timedOut=true;}check(timedOut,"translation timeout is bounded");
        var latex=TranslationLatex.Build(translated+"\n费用 50% & a_b #1 {ok}","简体中文");
        check(latex.Source.Contains(@"\(\rho\)")&&latex.Source.Contains(@"\frac{1}{2}")&&latex.Source.Contains(@"50\% \& a\_b \#1 \{ok\}")&&latex.Warnings.Count==0,"LaTeX keeps formulas and escapes ordinary text");
        var supported=TranslationLatex.Build(@"$x_i^2$ $$\sum_i p_i=1$$ \begin{align}a&=b\\c&=d\end{align}","中文");
        check(supported.Source.Contains(@"\begin{aligned}")&&supported.Warnings.Count==0,"LaTeX supports dollar formulas and aligned equations");
        var attack=0;
        foreach(var payload in new[]{@"\[\input{secret}\]",@"\[\write18{cmd}\]",@"\[\special{pdf:fstream}\]",@"\[\csname input\endcsname{file}\]",@"\[\^^5cinput{file}\]",@"\[\end{document}\]",@"\[\begin{aligned}a=b\]"})
        {
            var document=TranslationLatex.Build(payload,"中文");
            check(document.Warnings.Count>0&&!document.Source.Contains(payload),"unsafe formula case "+(++attack)+" is displayed as text");
        }
        var plain=TranslationLatex.Build(@"\input{file} \documentclass{evil} \def\foo{bad}","中文");
        check(plain.Source.Contains(@"\textbackslash{}input\{file\}")&&!plain.Source.Contains(@"\input{file}"),"non-math commands remain literal text");
        check(TranslationLatex.Build(@"式子 \(x+1","中文").Warnings.Count>0,"unclosed math remains readable text");
        check(TranslationLatex.Build(@"\(x=10%\)","中文").Source.Contains(@"x=10\%"),"math percent cannot comment out document");
        var legacy=JsonSerializer.Deserialize<Settings>("{}",Store.Json)!;
        check(legacy.TranslationTargetLanguage=="简体中文"&&legacy.TranslationAgentId==null&&legacy.LatexCompilerPath=="","old settings default translation safely");
    }
}
