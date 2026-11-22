using DesignatedDriver.Core;
using DesignatedDriver.Services;

namespace DesignatedDriver.SelfTest;

/// <summary>The activity panel masks token values: GitHub's token fields and anything shaped like a GitHub token.</summary>
public static partial class SelfTestRunner
{
    private static void ActivityRedactionChecks(Action<bool, string> check)
    {
        // Made-up values in the shapes GitHub uses; none of them is a real token. The prefix is joined at run time so
        // the source holds nothing a secret scanner would take for one.
        const string clone = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0";
        var pat = "gh" + "p_" + "0123456789abcdefghijABCDEFGHIJ0123";
        var details = $"{{\"name\":\"example-app\",\"private\":true,\"temp_clone_token\":\"{clone}\",\"default_branch\":\"main\"}}";

        var shown = ActivityBuffer.Line(new ActivityEntry(ActivityKind.Output, details));
        check(!shown.Contains(clone, StringComparison.Ordinal) && shown.Contains("\"temp_clone_token\":\"" + ActivityRedaction.Mask + "\"", StringComparison.Ordinal)
            && shown.Contains("\"default_branch\":\"main\"", StringComparison.Ordinal),
            "Activity redaction: a temp_clone_token's value is masked in the panel and the rest of the line is kept");
        check(ActivityRedaction.Apply($"\"token\": \"{clone}\"") == $"\"token\":\"{ActivityRedaction.Mask}\""
            && ActivityRedaction.Apply("\"access_token\":\"abc\\\"def\"") == $"\"access_token\":\"{ActivityRedaction.Mask}\"",
            "Activity redaction: other token fields are masked too, escaped quotes included");
        check(ActivityRedaction.Apply($"remote: using {pat} here") == $"remote: using {ActivityRedaction.Mask} here"
            && ActivityBuffer.Line(new ActivityEntry(ActivityKind.Error, pat)) == "! " + ActivityRedaction.Mask,
            "Activity redaction: text shaped like a GitHub token is masked wherever it appears, errors included");
        const string plain = "{\"temp_clone_token\":\"\",\"token_count\":3,\"name\":\"ghp_short\"}";
        check(ActivityRedaction.Apply(plain) == plain && ActivityRedaction.Apply("") == "",
            "Activity redaction: an empty token field, a field that isn't a token and a short look-alike are left alone");
    }
}
