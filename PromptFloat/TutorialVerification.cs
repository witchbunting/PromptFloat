namespace PromptFloat;
internal static class TutorialVerification
{
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T element)yield return element;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static Button Button(Window w,string label)=>Find<Button>(w).Single(b=>b.Content as string==label);
    private static TutorialWindow Guide()=>Application.Current.Windows.OfType<TutorialWindow>().Single(w=>w.IsVisible);
    private static void Click(Window w,string label)=>Button(w,label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public static async Task Run(Host host,Action<bool,string,string> check,string output)
    {
        var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(host.Store.Settings,Store.Json),Store.Json)!;
        try
        {
            host.Store.Settings.Initialized=false;host.Store.Settings.TutorialShown=false;host.Store.SaveSettings(host.Store.Settings);
            var clock=Stopwatch.StartNew();host.Start();
            check(clock.ElapsedMilliseconds<200&&host.Ball.IsVisible,"teaching startup returns promptly with usable ball",clock.ElapsedMilliseconds+" ms");
            await Task.Delay(150);
            var guide=Guide();
            check(host.Ball.IsVisible&&host.Store.Settings.TutorialShown&&new Store(host.Store.Root).Settings.TutorialShown,
                "first teaching is nonmodal and state is persisted before display","");
            for(var page=0;page<4;page++)
            {
                QuickVerification.Render((FrameworkElement)guide.Content,"tutorial-"+(page+1)+".png",output);
                check(guide.IsVisible&&Find<TextBlock>(guide).Any(t=>t.Text==(page+1)+" / 4"),"teaching page "+(page+1)+" renders and navigates","");
                if(page<3)Click(guide,"下一步");
            }
            var choices=Find<CheckBox>(guide).ToList();
            check(choices.Count==10&&choices.Count(c=>c.IsChecked==true)==4,"first teaching offers ten categories and four defaults","");
            choices[0].IsChecked=false;Click(guide,"上一步");Click(guide,"下一步");
            check(Find<CheckBox>(guide).Count(c=>c.IsChecked==true)==3,"teaching retains category choices across page navigation","");
            Click(guide,"开始使用");
            check(host.Store.Settings.Initialized&&!FirstRunTeaching.ShouldShow(new Store(host.Store.Root).Settings),"finishing teaching persists one-time completion","");
            host.Start();await Task.Delay(100);
            check(!Application.Current.Windows.OfType<TutorialWindow>().Any(w=>w.IsVisible),"subsequent startup does not show teaching again","");
            var export=host.Store.Export(statistics:true);host.OpenTutorial();await Task.Delay(80);
            guide=Guide();Click(guide,"关闭教学");
            check(host.Store.Export(statistics:true)==export,"manual teaching replay leaves template library unchanged","");
            host.Store.Settings.Initialized=false;host.Store.Settings.TutorialShown=false;host.Store.SaveSettings(host.Store.Settings);
            host.Start();await Task.Delay(80);Guide().Close();
            check(new Store(host.Store.Root).Settings.TutorialShown&&!FirstRunTeaching.ShouldShow(host.Store.Settings),"closing first teaching also prevents repeat","");
            host.Store.Settings.Initialized=false;host.Store.Settings.TutorialShown=false;host.Store.SaveSettings(host.Store.Settings);
            var blocked=host.Store.SettingsPath+".tmp";Directory.CreateDirectory(blocked);
            try
            {
                host.Start();await Task.Delay(80);
                check(host.Ball.IsVisible&&!Application.Current.Windows.OfType<TutorialWindow>().Any(w=>w.IsVisible),
                    "teaching state failure does not block normal app startup","");
            }
            finally{Directory.Delete(blocked);}
            File.WriteAllText(Path.Combine(output,"tutorial-tests.json"),System.Text.Json.JsonSerializer.Serialize(new {
                nonmodal=true,oneTimePersisted=true,fourPages=true,categoryChoicesRetained=true,
                manualReplayDoesNotChangeLibrary=true,closePreventsRepeat=true,stateFailureDoesNotBlockStartup=true
            },Store.Json));
        }
        finally
        {
            foreach(var window in Application.Current.Windows.OfType<TutorialWindow>().ToArray())window.Close();
            host.Store.SaveSettings(saved);host.ApplySettings();await Task.Delay(200);
        }
    }
}
