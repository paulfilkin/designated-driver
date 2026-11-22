using DesignatedDriver.Resources;

namespace DesignatedDriver.Core;

/// <summary>Lists in running text: "A", "A and B", "A, B and C".</summary>
public static class TextList
{
    public static string And(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " " + Strings.Common_And + " " + items[^1],
    };
}
