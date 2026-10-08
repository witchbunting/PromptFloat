using System.Text.Json;
namespace PromptFloat.Core;

public static class FirstRunTeaching
{
    public static bool ShouldShow(Settings settings)=>!settings.Initialized&&!settings.TutorialShown;
    public static bool TryReserve(Store store)
    {
        if(!ShouldShow(store.Settings))return false;
        var settings=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(store.Settings,Store.Json),Store.Json)!;
        // Persist before opening, so closing or interruption cannot repeat the automatic guide.
        settings.TutorialShown=true;
        store.SaveSettings(settings);
        return true;
    }
    public static void Complete(Store store,IEnumerable<string> categorySourceIds)
    {
        store.AddBuiltIns(categorySourceIds);
        var settings=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(store.Settings,Store.Json),Store.Json)!;
        settings.Initialized=true;settings.TutorialShown=true;store.SaveSettings(settings);
    }
}
