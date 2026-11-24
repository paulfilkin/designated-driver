using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DesignatedDriver.Core;

/// <summary>
/// Reads a whole issue form for New issue (issue 88): its name, description, title prefix, labels and fields.
/// The form is read with the small YAML reader below, which covers the block YAML GitHub's issue form syntax uses
/// (mappings, sequences, quoted and block scalars, flow lists), so no YAML package is needed.
/// </summary>
public static partial class IssueForms
{
    /// <summary>
    /// The form as a New issue choice, or null when the text isn't a form: no body list, or no field New issue can
    /// show. A form without a name takes its file name. A field without an id gets one from its position.
    /// </summary>
    public static IssueChoice? Read(string yaml, string fileName = "")
    {
        if (MiniYaml.Parse(yaml) is not Dictionary<string, object?> root || root.GetValueOrDefault("body") is not List<object?> body) return null;

        var name = MiniYaml.Text(root, "name");
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(fileName);
        var labels = root.GetValueOrDefault("labels") switch
        {
            List<object?> list => list.OfType<string>().Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            string s => s.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList(),
            _ => [],
        };

        var fields = new List<IssueField>();
        for (int k = 0; k < body.Count; k++)
        {
            if (body[k] is not Dictionary<string, object?> item) continue;
            var attributes = item.GetValueOrDefault("attributes") as Dictionary<string, object?> ?? [];
            var validations = item.GetValueOrDefault("validations") as Dictionary<string, object?> ?? [];
            var id = MiniYaml.Text(item, "id");
            if (id.Length == 0) id = $"field{k + 1}";
            var label = MiniYaml.Text(attributes, "label");
            var description = MiniYaml.Text(attributes, "description");
            bool required = MiniYaml.IsTrue(validations, "required");

            IssueField? field = MiniYaml.Text(item, "type").ToLowerInvariant() switch
            {
                "markdown" => new IssueField(id, "", false) { Type = IssueFieldType.Markdown, Value = MiniYaml.Text(attributes, "value").Trim() },
                "textarea" => new IssueField(id, label, required, MultiLine: true)
                {
                    Description = description, Value = MiniYaml.Text(attributes, "value"), Render = MiniYaml.Text(attributes, "render").Trim(),
                },
                "input" => new IssueField(id, label, required, MultiLine: false) { Description = description, Value = MiniYaml.Text(attributes, "value") },
                "dropdown" => new IssueField(id, label, required, MultiLine: false)
                {
                    Type = IssueFieldType.Dropdown, Description = description,
                    Options = [.. Options(attributes).Select(o => new IssueOption(o))],
                    Multiple = MiniYaml.IsTrue(attributes, "multiple"),
                    Default = int.TryParse(MiniYaml.Text(attributes, "default"), out var d) ? d : null,
                },
                "checkboxes" => new IssueField(id, label, false, MultiLine: false)
                {
                    Type = IssueFieldType.Checkboxes, Description = description,
                    Options = [.. (attributes.GetValueOrDefault("options") as List<object?> ?? []).Select(CheckboxOption).OfType<IssueOption>()],
                },
                _ => null,
            };
            if (field is not null) fields.Add(field);
        }
        if (!fields.Any(f => f.Type != IssueFieldType.Markdown)) return null;
        return new IssueChoice(name, labels, fields, MiniYaml.Text(root, "title"), MiniYaml.Text(root, "description").Trim());
    }

    /// <summary>A dropdown's options: a list of strings.</summary>
    private static IEnumerable<string> Options(Dictionary<string, object?> attributes)
        => (attributes.GetValueOrDefault("options") as List<object?> ?? []).OfType<string>().Select(s => s.Trim()).Where(s => s.Length > 0);

    /// <summary>A checkbox option: a mapping with a label and an optional required, or, leniently, a plain string.</summary>
    private static IssueOption? CheckboxOption(object? option) => option switch
    {
        Dictionary<string, object?> map when MiniYaml.Text(map, "label").Trim().Length > 0 => new IssueOption(MiniYaml.Text(map, "label").Trim(), MiniYaml.IsTrue(map, "required")),
        string s when s.Trim().Length > 0 => new IssueOption(s.Trim()),
        _ => null,
    };
}

/// <summary>
/// The block YAML an issue form is written in, read into dictionaries, lists and strings. Anchors, tags, flow
/// mappings and multi-document files are not read; GitHub's issue form syntax doesn't use them.
/// </summary>
internal static partial class MiniYaml
{
    public static object? Parse(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Replace('\t', ' ').Split('\n');
        int i = 0;
        SkipBlank(lines, ref i);
        return i < lines.Length ? ParseNode(lines, ref i, Indent(lines[i])) : null;
    }

    /// <summary>The key's value as text: a string as it is, anything else as empty.</summary>
    public static string Text(Dictionary<string, object?> map, string key) => map.GetValueOrDefault(key) as string ?? "";

    public static bool IsTrue(Dictionary<string, object?> map, string key) => Text(map, key).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static object? ParseNode(string[] lines, ref int i, int indent)
        => IsSeqItem(lines[i].Trim()) ? ParseSeq(lines, ref i, indent) : ParseMap(lines, ref i, indent);

    private static List<object?> ParseSeq(string[] lines, ref int i, int indent)
    {
        var list = new List<object?>();
        while (true)
        {
            SkipBlank(lines, ref i);
            if (i >= lines.Length) break;
            var text = lines[i].Trim();
            if (Indent(lines[i]) != indent || !IsSeqItem(text)) break;
            var rest = text == "-" ? "" : text[2..].TrimStart();
            if (rest.Length == 0)
            {
                i++;
                SkipBlank(lines, ref i);
                list.Add(i < lines.Length && Indent(lines[i]) > indent ? ParseNode(lines, ref i, Indent(lines[i])) : null);
            }
            else if (KeyLine().IsMatch(rest))
            {
                // "- key: value" starts a mapping whose keys line up with "key".
                int column = indent + text.Length - rest.Length;
                lines[i] = new string(' ', column) + rest;
                list.Add(ParseMap(lines, ref i, column));
            }
            else
            {
                list.Add(Scalar(rest));
                i++;
            }
        }
        return list;
    }

    private static Dictionary<string, object?> ParseMap(string[] lines, ref int i, int indent)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            SkipBlank(lines, ref i);
            if (i >= lines.Length || Indent(lines[i]) != indent) break;
            var m = KeyLine().Match(lines[i].Trim());
            if (!m.Success) break;
            var key = Unquote(m.Groups[1].Value);
            var rest = m.Groups[2].Value.Trim();
            i++;
            if (rest.StartsWith('|') || rest.StartsWith('>')) { map[key] = BlockScalar(lines, ref i, indent, rest); continue; }
            if (rest.Length > 0 && !rest.StartsWith('#')) { map[key] = Scalar(rest); continue; }
            SkipBlank(lines, ref i);
            if (i < lines.Length && (Indent(lines[i]) > indent || (Indent(lines[i]) == indent && IsSeqItem(lines[i].Trim()))))
                map[key] = ParseNode(lines, ref i, Indent(lines[i]));
            else
                map[key] = null;
        }
        return map;
    }

    /// <summary>A | (literal) or > (folded) block, with - to strip the final line break; the text is every line indented past the key.</summary>
    private static string BlockScalar(string[] lines, ref int i, int keyIndent, string header)
    {
        int start = i, contentIndent = -1;
        while (i < lines.Length)
        {
            if (lines[i].Trim().Length == 0) { i++; continue; }
            int ind = Indent(lines[i]);
            if (ind <= keyIndent) break;
            if (contentIndent < 0) contentIndent = ind;
            if (ind < contentIndent) break;
            i++;
        }
        var body = lines[start..i].Select(l => contentIndent >= 0 && l.Length >= contentIndent ? l[contentIndent..] : l.Trim()).ToList();
        while (body.Count > 0 && body[^1].Trim().Length == 0) body.RemoveAt(body.Count - 1);

        string text;
        if (header.StartsWith('>'))
        {
            var sb = new StringBuilder();
            bool previousText = false;
            foreach (var line in body)
            {
                if (line.Trim().Length == 0) { sb.Append('\n'); previousText = false; continue; }
                if (previousText) sb.Append(' ');
                sb.Append(line);
                previousText = true;
            }
            text = sb.ToString();
        }
        else text = string.Join("\n", body);
        return header.Contains('-') || text.Length == 0 ? text : text + "\n";
    }

    /// <summary>A scalar after its key or dash: quoted, a [flow, list], or plain up to a " #" comment.</summary>
    private static object Scalar(string rest)
    {
        rest = rest.Trim();
        if (rest.StartsWith('"') || rest.StartsWith('\''))
        {
            var (value, _) = Quoted(rest, 0);
            return value;
        }
        if (rest.StartsWith('['))
        {
            int close = rest.LastIndexOf(']');
            var inner = close > 0 ? rest[1..close] : rest[1..];
            return SplitFlow(inner).Select(p => (object?)Unquote(p)).Where(p => ((string)p!).Length > 0).ToList();
        }
        int hash = rest.IndexOf(" #", StringComparison.Ordinal);
        return (hash >= 0 ? rest[..hash] : rest).Trim();
    }

    /// <summary>The quoted string starting at start, unescaped, and the index after its closing quote.</summary>
    private static (string Value, int End) Quoted(string s, int start)
    {
        char quote = s[start];
        var sb = new StringBuilder();
        int j = start + 1;
        while (j < s.Length)
        {
            char c = s[j];
            if (quote == '\'' && c == '\'' && j + 1 < s.Length && s[j + 1] == '\'') { sb.Append('\''); j += 2; continue; }
            if (c == quote) return (sb.ToString(), j + 1);
            if (quote == '"' && c == '\\' && j + 1 < s.Length)
            {
                sb.Append(s[j + 1] switch { 'n' => '\n', 't' => '\t', var other => other });
                j += 2;
                continue;
            }
            sb.Append(c);
            j++;
        }
        return (sb.ToString(), j);
    }

    /// <summary>The items of a flow list, split on commas outside quotes.</summary>
    private static IEnumerable<string> SplitFlow(string inner)
    {
        int j = 0, from = 0;
        while (j < inner.Length)
        {
            if (inner[j] is '"' or '\'') { j = Quoted(inner, j).End; continue; }
            if (inner[j] == ',') { yield return inner[from..j]; from = j + 1; }
            j++;
        }
        yield return inner[from..];
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && (s[0] is '"' or '\'') ? Quoted(s, 0).Value : s;
    }

    private static bool IsSeqItem(string trimmed) => trimmed == "-" || trimmed.StartsWith("- ", StringComparison.Ordinal);

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static void SkipBlank(string[] lines, ref int i)
    {
        while (i < lines.Length && (lines[i].Trim().Length == 0 || lines[i].TrimStart().StartsWith('#'))) i++;
    }

    /// <summary>"key: value" or "key:", the key plain or quoted; a colon inside a plain value doesn't end the key unless a space follows it.</summary>
    [GeneratedRegex(@"^(""[^""]*""|'[^']*'|[^\s""'#\-\[][^:]*?|-[^\s][^:]*?)\s*:(?:\s+(.*)|$)")]
    private static partial Regex KeyLine();
}
