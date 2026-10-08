using System.Text.Json;
namespace PromptFloat.Core;

public sealed class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新分类";
    public string Description { get; set; } = "";
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public string? SourceId { get; set; }
}
public sealed class Variable
{
    public string Name { get; set; } = "";
    public string Default { get; set; } = "";
    public bool Required { get; set; } = true;
}
public sealed class Prompt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CategoryId { get; set; } = Store.Uncategorized;
    public string Title { get; set; } = "新模板";
    public string Body { get; set; } = "";
    public string Note { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<Variable> Variables { get; set; } = [];
    public bool Favorite { get; set; }
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
    public long InsertCount { get; set; }
    public long CopyCount { get; set; }
    public long Uses => InsertCount + CopyCount;
    public DateTimeOffset? LastUsed { get; set; }
    public string Source { get; set; } = "user";
    public string? SourceId { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Deleted { get; set; }
    public Prompt Clone() => JsonSerializer.Deserialize<Prompt>(JsonSerializer.Serialize(this))!;
}
public sealed class Library
{
    public List<Category> Categories { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
}
public sealed class Exchange
{
    public int FormatVersion { get; set; } = 1;
    public bool IncludesStatistics { get; set; }
    public List<Category> Categories { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
}
public enum ConflictPolicy { Skip, Overwrite, NewCopy }
public sealed record ImportPreview(Exchange Data, int Categories, int Templates, int IdConflicts, int DuplicateBodies);
public sealed class Settings
{
    public bool AutoSelectionActions { get; set; } = true;
    public List<AgentProfile> Agents { get; set; } = [];
    public string? DefaultAgentId { get; set; }
    public bool Initialized { get; set; }
    public bool TutorialShown { get; set; }
    public double BallX { get; set; } = -1;
    public double BallY { get; set; } = -1;
    public bool BallPositionSaved { get; set; }
    public double BallSize { get; set; } = 44;
    public double Opacity { get; set; } = .95;
    public bool Snap { get; set; } = true;
    public bool HalfHide { get; set; }
    public bool AutoStart { get; set; }
    public bool AutoBackup { get; set; } = true;
    public string Theme { get; set; } = "浅色";
    public string PanelHotkey { get; set; } = "Ctrl+Alt+P";
    public string SaveHotkey { get; set; } = "Ctrl+Alt+S";
    public string HideHotkey { get; set; } = "Ctrl+Alt+H";
    public double PanelWidth { get; set; } = 560;
    public double PanelHeight { get; set; } = 420;
    public List<string> CopyOnlyApps { get; set; } = [];
}

public sealed class PolishStyle
{
    public string Name { get; set; } = "";
    public string Instruction { get; set; } = "";
}
public sealed class AgentProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "润色助手";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "保留原意、事实和原文语言，只润色表达，不补充未提供的信息。";
    public bool StructuredOutput { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public List<PolishStyle> Styles { get; set; } = [new() {Name="轻度润色",Instruction="修正语病，使表达自然，尽量保留原结构。"},new() {Name="清晰精简",Instruction="简洁清晰，去除冗余，但保留所有实质信息。"},new() {Name="正式专业",Instruction="表达正式、专业、条理清晰。"}];
}
