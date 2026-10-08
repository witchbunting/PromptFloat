namespace PromptFloat;
internal static class AppVersion
{
    public static string Text=>typeof(AppVersion).Assembly.GetName().Version?.ToString(3)??"1.2.0";
}
