using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
namespace PromptFloat;

internal static class QuickVerification
{
    public static async Task Run(Host host,IntPtr window,Func<int,string,int,int,string,Task> command,Func<string,Task<string>> text,Action<bool,string,string> check,string output)
    {
        var category=new Category {Name="菜单测试"};var fixedPrompt=new Prompt {CategoryId=category.Id,Title="完整标题",Note="简短备注",Body="菜单插入✨\r\n"};
        host.Store.SavePrompt(fixedPrompt,category);
        await command(101,"前旧后",1,1,"plain");
        var snapshot=await host.Input.SnapshotAsync();check(snapshot?.Text=="旧","selection snapshot captures original text","");
        host.Surfaces.ShowMenu(snapshot);await Task.Delay(150);
        check(Native.GetForegroundWindow()==window,"category menu preserves external foreground","");
        Native.TestKey(0x28);await Task.Delay(80);Native.TestKey(0x1B);await Task.Delay(100);
        check(host.Surfaces.ActiveMenu?.IsOpen!=true&&await host.Input.SelectedTextAsync()=="旧","menu keyboard navigation and Escape preserve selection","");
        host.Surfaces.ShowMenu(snapshot);await Task.Delay(80);
        var menu=host.Surfaces.ActiveMenu!;
        var group=menu.Items.OfType<MenuItem>().Single(m=>m.Header as string=="菜单测试");group.IsSubmenuOpen=true;await Task.Delay(180);
        var item=group.Items.OfType<MenuItem>().Single();
        check(item.Header is TextBlock t&&t.Text=="简短备注","submenu uses short remark","");
        check(QuickSurfaces.Label(new Prompt {Title="回退标题",Note=""})=="回退标题","blank remark falls back to title","");
        Render(menu,"menu.png",output);
        host.Store.RecordUse(fixedPrompt.Id,"frozen-menu",true);
        check(ReferenceEquals(item,group.Items[0]),"open menu remains frozen after statistics change","");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,item));item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,item));
        await Task.Delay(700);check(await text("text")=="前菜单插入✨\r\n后","menu click replaces selection preserving surroundings","");
        check(host.Store.Read().Prompts.Single(p=>p.Id==fixedPrompt.Id).InsertCount==1,"duplicate menu click dispatches once","");
        await command(102,"原文",0,2,"plain");
        host.Selection.Apply();await Task.Delay(700);
        check(host.Surfaces.IsOpen&&host.Selection.Current?.Text=="原文","automatic selection action popup","");
        check(Native.GetForegroundWindow()==window,"automatic actions do not activate app","");
        if(host.Surfaces.ActiveVisual!=null)Render(host.Surfaces.ActiveVisual,"selection-actions.png",output);
        var original=host.Selection.Current!;
        host.Surfaces.ShowResults(original,[new("轻度润色","自然版本"),new("清晰精简","精简版本"),new("正式专业","专业版本")]);
        await Task.Delay(100);Render(host.Surfaces.ActiveVisual!,"polish-bubbles.png",output);
        check(Native.GetForegroundWindow()==window,"polish bubbles preserve source focus","");
        await command(103,"后来输入的文字",0,2,"plain");await Task.Delay(350);
        check(!await host.Input.ValidateSelectionAsync(original),"source edit invalidates pending polish selection","");
        await host.Surfaces.AdoptAsync(original,"不得写入");
        check(await text("text")=="后来输入的文字","stale polish result cannot overwrite edited source","");
        host.Surfaces.Close();
        await command(104,"前旧后",1,1,"plain");await Task.Delay(400);
        var valid=await host.Input.SnapshotAsync();
        var uses=host.Store.Read().Prompts.Sum(p=>p.Uses);
        await host.Surfaces.AdoptAsync(valid!,"润色✨");await host.Surfaces.AdoptAsync(valid!,"重复写入");
        check(await text("text")=="前润色✨后","bubble adoption replaces original selection only","");
        check(host.Store.Read().Prompts.Sum(p=>p.Uses)==uses,"AI adoption does not count template usage","");
        await command(105,"无选区",2,0,"plain");await Task.Delay(450);
        check(host.Selection.Current==null&&host.Surfaces.InsertBubbleVisible,"empty selection switches to persistent insert bubble","");
        check(Native.GetForegroundWindow()==window,"caret insert bubble preserves editor focus","");
        Render(host.Surfaces.ActiveVisual!,"insert-bubble.png",output);
        var stableVisual=host.Surfaces.ActiveVisual;var probes=host.Selection.ProbeCount;await Task.Delay(600);
        check(host.Surfaces.InsertBubbleVisible&&ReferenceEquals(stableVisual,host.Surfaces.ActiveVisual)&&probes==host.Selection.ProbeCount,"insert bubble stays visible without polling or recreation","");
        Native.TestKey(0x58);await Task.Delay(400);
        check(host.Surfaces.InsertBubbleVisible&&Native.GetForegroundWindow()==window,"typing keeps insert bubble visible and preserves input focus","");
        await command(110,"前后",1,0,"plain");await Task.Delay(450);
        var insertButton=Buttons(host.Surfaces.ActiveVisual!).Single(b=>b.Content as string=="插入");
        var point=insertButton.PointToScreen(new Point(insertButton.ActualWidth/2,insertButton.ActualHeight/2));
        var popupWindow=((HwndSource)PresentationSource.FromVisual(insertButton)).Handle;
        Native.GetCursorPos(out var previousCursor);
        try
        {
            check(Native.TestClick(popupWindow,(int)point.X,(int)point.Y,false),"actual mouse click on insert bubble dispatches","");
            await Task.Delay(250);
        }
        finally {Native.TestRestoreCursor(previousCursor);}
        check(Native.GetForegroundWindow()==window&&host.Input.Target?.Window==window,"insert bubble mouse click retains editor target and caret","");
        var caretGroup=host.Surfaces.ActiveMenu!.Items.OfType<MenuItem>().Single(m=>m.Header as string=="菜单测试");caretGroup.IsSubmenuOpen=true;await Task.Delay(120);
        caretGroup.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(await text("text")=="前菜单插入✨\r\n后","persistent bubble inserts at caret preserving existing text","");
        await Task.Delay(350);check(host.Surfaces.InsertBubbleVisible,"insert bubble returns after template insertion","");
        await command(111,"前后",0,0,"button");await Task.Delay(450);
        check(!host.Surfaces.InsertBubbleVisible&&host.Surfaces.ActiveVisual==null,"focus on non-input control hides caret bubble","");
        await command(112,"",0,0,"password");await Task.Delay(450);
        check(!host.Surfaces.InsertBubbleVisible,"password field does not display caret insert bubble","");
        await command(113,"只读原文",0,0,"readonly");await Task.Delay(450);
        check(!host.Surfaces.InsertBubbleVisible,"readonly caret does not display insert bubble","");
        await command(106,"只读原文",0,2,"readonly");await Task.Delay(350);var readonlySnapshot=await host.Input.SnapshotAsync();
        check(readonlySnapshot?.Text=="只读"&&readonlySnapshot.Target.CopyOnly,"readonly selection readable but copy-only","");
        await host.Surfaces.AdoptAsync(readonlySnapshot!,"不可替换");check(await text("text")=="只读原文","readonly polish cannot replace source","");
        host.Surfaces.Close();
        await command(107,"控件一原文",0,3,"plain");await Task.Delay(300);var firstControl=await host.Input.SnapshotAsync();
        await command(108,"控件二",0,0,"rich");check(!await host.Input.ValidateSelectionAsync(firstControl!),"same-window different-control invalidates replacement","");
        await PolishInteractionVerification.Run(host,window,command,text,check,output);
        host.Store.Settings.AutoSelectionActions=false;host.Selection.Apply();host.Surfaces.Close();
        var vault=new CredentialVault(host.Store.Root);vault.Save(new Dictionary<string,string>{{"test","synthetic-secret"}});
        check(vault.Get("test")=="synthetic-secret"&&!File.ReadAllText(Path.Combine(host.Store.Root,"credentials.json")).Contains("synthetic-secret"),"current-user DPAPI round trip and ciphertext","");
        var backup=host.Store.Backup();using(var zip=System.IO.Compression.ZipFile.OpenRead(backup))check(!zip.Entries.Any(e=>e.FullName.Contains("credentials")),"encrypted keys excluded from full backup","");
        var filled=host.Surfaces.FillAsync(new Prompt {Body="材料：{{材料}}"},false);await Task.Delay(100);
        Render(host.Surfaces.ActiveVisual!,"variable-popover.png",output);host.Surfaces.Close();
        check(await filled==null,"variable popover cancellation returns no result","");
        var configured=new Settings {Agents=[new AgentProfile {Model="demo-model"}]};configured.DefaultAgentId=configured.Agents[0].Id;
        var owner=Ui.Window("智能体验证",400,400);owner.Show();var agents=new AgentWindow(configured,vault,new Dictionary<string,string>(),owner);agents.Show();await Task.Delay(100);Render((FrameworkElement)agents.Content,"agents.png",output);agents.Close();owner.Close();
        await AgentConfigurationVerification.Run(host,check,output);
        var editor=new EditorWindow(host.Store,initialBody:"选区正文");editor.Show();await Task.Delay(70);
        check(editor.Content!=null,"selection save editor with category creation renders","");editor.Close();
        await command(109,"卡顿隔离测试",0,0,"plain");
        await ResponsivenessVerification.Run(host,check,output);
    }
    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if(root is Button button)yield return button;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Buttons(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    internal static void Render(FrameworkElement visual,string name,string output)
    {
        visual.UpdateLayout();var width=(int)Math.Ceiling(visual.ActualWidth+visual.Margin.Left+visual.Margin.Right);var height=(int)Math.Ceiling(visual.ActualHeight+visual.Margin.Top+visual.Margin.Bottom);
        if(width<=0||height<=0)return;
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle((Brush)Application.Current.Resources["Bg"],null,new Rect(0,0,width,height));bitmap.Render(background);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);
    }
}
