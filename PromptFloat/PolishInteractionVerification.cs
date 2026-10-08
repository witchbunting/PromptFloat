using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
namespace PromptFloat;

internal static class PolishInteractionVerification
{
    private sealed class DelayedResponse : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            try{return await response.Task.WaitAsync(token);}catch(OperationCanceledException){Cancelled=true;throw;}
        }
        public void Complete(string first)
        {
            var content=JsonSerializer.Serialize(new {version1=first,version2="精简版本",version3="正式版本"});
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content=new StringContent(JsonSerializer.Serialize(new {choices=new[]{new {finish_reason="stop",message=new {content}}}}))
            });
        }
    }
    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if(root is Button b)yield return b;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Buttons(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static Native.Rect PopupBounds(FrameworkElement visual)
    {
        var handle=((HwndSource)PresentationSource.FromVisual(visual)).Handle;
        Native.GetWindowRect(handle,out var rect);return rect;
    }
    private sealed class RetryResponse : HttpMessageHandler
    {
        public int Requests {get;private set;}
        public bool LatestInstructions {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var system=json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            LatestInstructions=Enumerable.Range(1,3).All(i=>system.Contains("version"+i+"：新风格"+i+"；新要求"+i));
            if(Requests==1)return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            var content=JsonSerializer.Serialize(new {version1="一",version2="二",version3="三"});
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new {
                choices=new[]{new {finish_reason="stop",message=new {content}}}
            }))};
        }
    }
    private static async Task<bool> Click(Button button)
    {
        var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
        var window=((HwndSource)PresentationSource.FromVisual(button)).Handle;Native.GetCursorPos(out var previous);
        try{var sent=Native.TestClick(window,(int)point.X,(int)point.Y,false);await Task.Delay(60);return sent;}
        finally{Native.TestRestoreCursor(previous);}
    }
    public static async Task Run(Host host,IntPtr window,Func<int,string,int,int,string,Task> command,Func<string,Task<string>> text,Action<bool,string,string> check,string output)
    {
        var field=typeof(Host).GetField("polish",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var savedService=field.GetValue(host);var savedAgents=host.Store.Settings.Agents;var savedDefault=host.Store.Settings.DefaultAgentId;
        var savedPosition=Native.Bounds(host.Ball);
        var agent=new AgentProfile {Name="隔离润色测试",Model="mock-model",BaseUrl="https://promptfloat.invalid/v1"};
        using var handler=new DelayedResponse();using var service=new PolishService(handler);
        using var cancelHandler=new DelayedResponse();using var cancelService=new PolishService(cancelHandler);
        try
        {
            field.SetValue(host,service);host.Store.Settings.Agents=[agent];host.Store.Settings.DefaultAgentId=agent.Id;
            host.Credentials.Save(new Dictionary<string,string>{{agent.Id,"synthetic-test-key"}});
            host.Surfaces.Close();
            var area=Native.WorkArea(new WindowInteropHelper(host.Ball).Handle);
            Native.Move(host.Ball,area.Left+80,area.Top+60);
            await command(201,"前旧后",1,1,"plain");await Task.Delay(400);
            var source=host.Selection.Current!;
            check(source?.Text=="旧"&&await host.Input.ValidateSelectionAsync(source),"polish interaction has original selected text","");
            var operation=host.Surfaces.PolishAsync(source!,agent);await Task.Delay(100);
            var progress=host.Surfaces.ActiveVisual!;
            check(handler.Requests==1&&!operation.IsCompleted&&progress!=null,"mock polish request stays pending while progress bubble is shown","");
            var beforeBall=Native.Bounds(host.Ball);var beforePopup=PopupBounds(progress!);
            check(await Native.TestDragAsync(new WindowInteropHelper(host.Ball).Handle,beforeBall.Left+beforeBall.Width/2,beforeBall.Top+beforeBall.Height/2,120,70),"actual mouse drags ball during polish request","");
            var afterBall=Native.Bounds(host.Ball);var afterPopup=PopupBounds(host.Surfaces.ActiveVisual!);
            check(host.Surfaces.IsOpen&&ReferenceEquals(progress,host.Surfaces.ActiveVisual)&&!operation.IsCompleted&&handler.Requests==1,"drag preserves progress popup and the original API request","");
            check(Math.Abs((afterPopup.Left-beforePopup.Left)-(afterBall.Left-beforeBall.Left))<=2&&Math.Abs((afterPopup.Top-beforePopup.Top)-(afterBall.Top-beforeBall.Top))<=2&&afterBall.Left!=beforeBall.Left,"progress popup follows ball movement","");
            var sourceValid=await host.Input.ValidateSelectionAsync(source!);
            check(Native.GetForegroundWindow()==window&&sourceValid,"dragging progress preserves source focus and selected text",
                $"foreground={Native.GetForegroundWindow()==window}; foregroundPid={Native.Pid(Native.GetForegroundWindow())}; ownPid={Environment.ProcessId}; sourcePid={Native.Pid(window)}; validated={sourceValid}; validFlag={source!.Valid}; target={host.Input.Target?.Window==window}; monitorValid={host.Selection.Current?.Valid}; selectedTextEqual={await host.Input.SelectedTextAsync()==source.Text}");
            QuickVerification.Render(host.Surfaces.ActiveVisual!,"polish-progress-drag.png",output);

            handler.Complete("润色✨\r\n第二行");await operation.WaitAsync(TimeSpan.FromSeconds(4));await Task.Delay(100);
            var results=host.Surfaces.ActiveVisual!;
            check(Buttons(results).Count(b=>b.Content as string=="替换")==3,"unchanged original gives three Replace buttons","");
            beforeBall=Native.Bounds(host.Ball);beforePopup=PopupBounds(results);
            check(await Native.TestDragAsync(new WindowInteropHelper(host.Ball).Handle,beforeBall.Left+beforeBall.Width/2,beforeBall.Top+beforeBall.Height/2,75,45),"actual mouse drags ball with three polish candidates","");
            afterBall=Native.Bounds(host.Ball);afterPopup=PopupBounds(host.Surfaces.ActiveVisual!);
            check(host.Surfaces.IsOpen&&ReferenceEquals(results,host.Surfaces.ActiveVisual)&&handler.Requests==1,"drag keeps candidate bubbles and does not resend API request","");
            check(Math.Abs((afterPopup.Left-beforePopup.Left)-(afterBall.Left-beforeBall.Left))<=2&&Math.Abs((afterPopup.Top-beforePopup.Top)-(afterBall.Top-beforeBall.Top))<=2&&afterBall.Top!=beforeBall.Top,"candidate popups follow ball movement","");
            check(Buttons(results).Count(b=>b.Content as string=="替换")==3&&await host.Input.ValidateSelectionAsync(source!),"drag leaves unchanged selection replaceable","");
            QuickVerification.Render(results,"polish-results-drag.png",output);
            var uses=host.Store.Read().Prompts.Sum(p=>p.Uses);
            var replace=Buttons(results).First(b=>b.Content as string=="替换");
            check(await Click(replace),"actual mouse click on Replace button dispatches","");
            replace.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,replace));
            check(await text("text")=="前润色✨\r\n第二行后","Replace button changes selected text once and preserves surrounding text","");
            check(host.Store.Read().Prompts.Sum(p=>p.Uses)==uses,"AI Replace button does not change template counters","");

            host.Surfaces.Close();
            await command(202,"旧文本",0,1,"plain");await Task.Delay(350);var stale=host.Selection.Current!;
            host.Surfaces.ShowResults(stale,[new("轻度润色","仅复制版本"),new("清晰精简","二"),new("正式专业","三")]);await Task.Delay(80);
            await command(203,"新文本",0,1,"plain");await Task.Delay(400);
            check(Buttons(host.Surfaces.ActiveVisual!).Count(b=>b.Content as string=="复制")==3,"editing original switches all candidate actions to Copy","");
            var copy=Buttons(host.Surfaces.ActiveVisual!).First(b=>b.Content as string=="复制");
            check(await Click(copy),"actual mouse click on stale result Copy dispatches","");
            check(Clipboard.GetText()=="仅复制版本"&&await text("text")=="新文本","stale candidate copies without overwriting edited text","");
            host.Surfaces.Close();
            await command(204,"前旧后",1,1,"plain");await Task.Delay(350);var changedRange=host.Selection.Current!;
            host.Surfaces.ShowResults(changedRange,[new("一","候选一"),new("二","候选二"),new("三","候选三")]);
            await command(205,"前旧后",0,1,"plain");await Task.Delay(400);
            check(Buttons(host.Surfaces.ActiveVisual!).Count(b=>b.Content as string=="复制")==3&&!await host.Input.ValidateSelectionAsync(changedRange),"changing selection alone switches candidate actions to Copy","");

            host.Surfaces.Close();field.SetValue(host,cancelService);
            await command(206,"可取消原文",0,3,"plain");await Task.Delay(350);var cancelSource=host.Selection.Current!;
            var cancelled=host.Surfaces.PolishAsync(cancelSource,agent);await Task.Delay(100);
            beforeBall=Native.Bounds(host.Ball);
            check(await Native.TestDragAsync(new WindowInteropHelper(host.Ball).Handle,beforeBall.Left+beforeBall.Width/2,beforeBall.Top+beforeBall.Height/2,40,30),"ball can move while cancellation test request is pending","");
            Buttons(host.Surfaces.ActiveVisual!).Single(b=>b.Content as string=="取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await cancelled.WaitAsync(TimeSpan.FromSeconds(3));
            check(cancelHandler.Cancelled&&cancelHandler.Requests==1&&!host.Surfaces.IsOpen,"cancel after dragging stops the same request and closes progress","");
            check(await text("text")=="可取消原文","cancel after dragging leaves source text unchanged","");
            using var retryHandler=new RetryResponse();using var retryService=new PolishService(retryHandler);
            field.SetValue(host,retryService);
            await command(207,"重试原文",0,2,"plain");await Task.Delay(350);var retrySource=host.Selection.Current!;
            await host.Surfaces.PolishAsync(retrySource,agent);
            check(retryHandler.Requests==1&&Buttons(host.Surfaces.ActiveVisual!).Any(b=>b.Content as string=="重试"),"failed polish shows explicit retry without automatic repeat","");
            var changed=JsonSerializer.Deserialize<AgentProfile>(JsonSerializer.Serialize(agent,Store.Json),Store.Json)!;
            changed.Styles=Enumerable.Range(1,3).Select(i=>new PolishStyle {Name="新风格"+i,Instruction="新要求"+i}).ToList();
            host.SaveAgentConfiguration([changed],changed.Id,new Dictionary<string,string>());
            Buttons(host.Surfaces.ActiveVisual!).Single(b=>b.Content as string=="重试").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(450);
            check(retryHandler.Requests==2&&retryHandler.LatestInstructions,"retry reads all three latest saved style instructions","");
            check(Buttons(host.Surfaces.ActiveVisual!).Count(b=>b.Content as string=="替换")==3,"retry renders current-config results while original selection remains valid","");
            File.WriteAllText(Path.Combine(output,"polish-interaction.json"),JsonSerializer.Serialize(new {
                mockApiRequests=handler.Requests,realMouseDrags=true,progressAndResultsKeepSamePopup=true,
                unchangedOriginalReplace=true,editedOriginalCopyOnly=true,changedSelectionCopyOnly=true,
                duplicateReplaceProtected=true,cancelAfterDrag=true,retryUsesLatestSavedStyles=true
            },Store.Json));
        }
        finally
        {
            host.Surfaces.Cancel();field.SetValue(host,savedService);
            host.Store.Settings.Agents=savedAgents;host.Store.Settings.DefaultAgentId=savedDefault;
            host.Credentials.Save(new Dictionary<string,string>());Native.Move(host.Ball,savedPosition.Left,savedPosition.Top);
        }
    }
}
