using System.Windows.Threading;
namespace PromptFloat;

internal static class AgentConfigurationVerification
{
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T element)yield return element;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static Button Button(Window window,string label)=>Find<Button>(window).Single(b=>b.Content as string==label);
    private static TextBox Instruction(Window window,int index)=>Find<TextBox>(window).Single(t=>t.Name=="StyleInstruction"+index);
    public static async Task Run(Host host,Action<bool,string,string> check,string output)
    {
        var previous=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(host.Store.Settings,Store.Json),Store.Json)!;
        var agent=new AgentProfile {Name="风格保存验证",Model="mock-model"};
        host.SaveAgentConfiguration([agent],agent.Id,new Dictionary<string,string>());
        var parent=new SettingsWindow(host);parent.Show();await Task.Delay(80);
        try
        {
            var backupBefore=host.Store.Settings.AutoBackup;
            Find<CheckBox>(parent).Single(c=>c.Content as string=="每日首次修改前自动备份，保留最近 7 份").IsChecked=!backupBefore;
            Find<TabControl>(parent).Single().SelectedIndex=3;await Task.Delay(60);
            Exception? failure=null;
            _ = parent.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
                var editor=Application.Current.Windows.OfType<AgentWindow>().Single(w=>w.Owner==parent);
                try
                {
                    for(var i=1;i<=3;i++)Instruction(editor,i).Text="自定义风格 "+i+" ✨\n第二行";
                    QuickVerification.Render((FrameworkElement)editor.Content,"agent-style-save.png",output);
                    Button(editor,"保存并立即生效").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
                catch(Exception e){failure=e;editor.Close();}
            }));
            Button(parent,"配置智能体与测试连接").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(failure==null,"agent editor saves three edited style fields",failure?.Message??"");
            check(host.Store.Settings.Agents.Single().Styles.Select((s,i)=>s.Instruction=="自定义风格 "+(i+1)+" ✨\n第二行").All(x=>x),
                "agent Save applies all three instructions before outer settings save","");
            check(host.Store.Settings.AutoBackup==backupBefore,"agent-only save does not commit unrelated outer settings drafts","");
            Button(parent,"取消").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var reopened=new Store(host.Store.Root).Settings;
            check(reopened.Agents.Single().Styles.Select((s,i)=>s.Instruction=="自定义风格 "+(i+1)+" ✨\n第二行").All(x=>x),
                "outer settings Cancel preserves explicitly saved agent changes on disk","");
            parent=new SettingsWindow(host);
            _ = parent.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
                try
                {
                    Find<TabControl>(parent).Single().SelectedIndex=3;parent.UpdateLayout();
                    _ = parent.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
                        var editor=Application.Current.Windows.OfType<AgentWindow>().Single(w=>w.Owner==parent);
                        Instruction(editor,1).Text="未保存的改动";
                        Button(editor,"取消").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }));
                    Button(parent,"配置智能体与测试连接").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Button(parent,"保存设置").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
                catch(Exception e){failure=e;parent.Close();}
            }));
            parent.ShowDialog();
            check(failure==null,"outer settings saves after cancelling agent edits",failure?.Message??"");
            check(new Store(host.Store.Root).Settings.Agents.Single().Styles[0].Instruction=="自定义风格 1 ✨\n第二行",
                "cancelled agent edits and subsequent outer save preserve last committed instructions","");
            try{host.SaveAgentConfiguration([new AgentProfile {Model="replacement"}],null,new Dictionary<string,string>{{"invalid","bad\nkey"}});}
            catch(ArgumentException){}
            check(host.Store.Settings.Agents.Single().Id==agent.Id&&new Store(host.Store.Root).Settings.Agents.Single().Id==agent.Id,
                "credential save failure rolls back agent configuration","");
            File.WriteAllText(Path.Combine(output,"agent-style-save.json"),System.Text.Json.JsonSerializer.Serialize(new {
                editsAllThreeStyles=true,oneSaveAppliesImmediately=true,outerCancelPreservesCommittedAgents=true,
                cancelledEditsDiscarded=true,credentialFailureRollsBack=true,mockApiOnly=true
            },Store.Json));
        }
        finally
        {
            foreach(var editor in Application.Current.Windows.OfType<AgentWindow>().Where(w=>w.Owner==parent).ToArray())editor.Close();
            parent.Close();host.Store.SaveSettings(previous);host.ApplySettings();await Task.Delay(200);
        }
    }
}
