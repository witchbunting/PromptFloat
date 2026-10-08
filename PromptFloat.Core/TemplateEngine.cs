using System.Text.RegularExpressions;
namespace PromptFloat.Core;

public static partial class TemplateEngine
{
    [GeneratedRegex(@"(?<escape>\\)?\{\{(?<name>[^{}\r\n]+)\}\}")]
    private static partial Regex Fields();
    public static List<Variable> Parse(string body, IEnumerable<Variable>? settings = null)
    {
        var existing = (settings ?? []).GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First());
        var result = new List<Variable>();
        foreach (Match match in Fields().Matches(body))
        {
            if (match.Groups["escape"].Success) continue;
            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0 || result.Any(v => v.Name == name)) continue;
            result.Add(existing.TryGetValue(name, out var v) ? new Variable { Name=name, Default=v.Default, Required=v.Required } : new Variable { Name=name });
        }
        return result;
    }
    public static string Render(string body, IReadOnlyDictionary<string,string> values, IEnumerable<Variable> variables)
    {
        foreach (var v in variables)
            if (v.Required && (!values.TryGetValue(v.Name, out var value) || string.IsNullOrWhiteSpace(value)))
                throw new ArgumentException($"请填写：{v.Name}");
        return Fields().Replace(body, m => m.Groups["escape"].Success ? m.Value[1..] :
            values.TryGetValue(m.Groups["name"].Value.Trim(), out var value) ? value : m.Value);
    }
    public static IEnumerable<Prompt> Sorted(IEnumerable<Prompt> prompts) => prompts.OrderByDescending(p => p.Uses)
        .ThenByDescending(p => p.LastUsed).ThenBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal);
    public static IEnumerable<Prompt> Search(IEnumerable<Prompt> prompts, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Sorted(prompts);
        var parts = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int Rank(Prompt p, string q) => p.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ? 0 :
            p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) ? 1 :
            p.Note.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2 : p.Body.Contains(q, StringComparison.OrdinalIgnoreCase) ? 3 : 100;
        return Sorted(prompts).Select(p => (p, ranks: parts.Select(q => Rank(p,q)).ToArray()))
            .Where(x => x.ranks.All(r => r < 100)).OrderBy(x => x.ranks.Sum()).Select(x => x.p);
    }
    public static List<string> Tags(string text) => text.Split([',','，',';','；'], StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public static string BodyKey(string body) => body.Replace("\r\n","\n").Trim();
}
