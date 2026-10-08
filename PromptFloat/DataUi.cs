using Microsoft.Win32;
namespace PromptFloat;

internal static class DataUi
{
    public static bool Import(Window owner,Store store)
    {
        var dialog=new OpenFileDialog {Filter="模板文件 (*.json)|*.json",Title="导入提示词"};if(dialog.ShowDialog(owner)!=true)return false;
        var info=new FileInfo(dialog.FileName);if(info.Length>40_000_000)throw new InvalidDataException("文件过大（上限 40 MB）。");
        var preview=store.PreviewImport(File.ReadAllText(dialog.FileName));
        var w=Ui.Window("导入预览",580,410,owner);var policy=new ComboBox {ItemsSource=new[]{"跳过同 ID 项（默认）","覆盖同 ID 项","同 ID 项另存为新副本"},SelectedIndex=0};var stats=new CheckBox {Content="保留文件中的使用统计（默认从零开始）"};var imported=false;
        var content=Ui.Stack(Ui.Text("确认导入内容",22,true),Ui.Text($"分类 {preview.Categories} 个 · 模板 {preview.Templates} 个\n同 ID 模板 {preview.IdConflicts} 个 · 与现有正文重复 {preview.DuplicateBodies} 个",14),Ui.Labeled("ID 冲突处理（应用于分类与模板）",policy),stats,Ui.Text("追加到当前模板库，不清空现有数据。正文重复的独立模板可以保留；导入前自动备份。",12,false,"Muted"),Ui.Row(Ui.Button("取消",()=>w.Close()),Ui.Button("确认导入",()=>{store.Import(preview,(ConflictPolicy)policy.SelectedIndex,stats.IsChecked==true);imported=true;w.DialogResult=true;},true)));content.Margin=new Thickness(22);w.Content=content;w.ShowDialog();return imported;
    }
    public static void Export(Window owner,Store store,IEnumerable<string>? selected=null)
    {
        var w=Ui.Window("导出模板",480,240,owner);var stats=new CheckBox {Content="包含使用统计"};var content=Ui.Stack(Ui.Text("导出模板 JSON",20,true),stats,Ui.Row(Ui.Button("取消",()=>w.Close()),Ui.Button("选择保存位置",()=>{
            var dialog=new SaveFileDialog {Filter="模板文件 (*.json)|*.json",FileName=$"提示词-{DateTime.Now:yyyyMMdd}.json"};if(dialog.ShowDialog(w)==true){File.WriteAllText(dialog.FileName,store.Export(selected,stats.IsChecked==true));w.DialogResult=true;}
        },true)));content.Margin=new Thickness(20);w.Content=content;w.ShowDialog();
    }
}
internal sealed record CategoryChoice(string Id,string Name) {public override string ToString()=>Name;}
