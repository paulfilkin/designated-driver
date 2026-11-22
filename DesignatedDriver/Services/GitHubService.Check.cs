using System.IO;
using DesignatedDriver.Core;

namespace DesignatedDriver.Services;

/// <summary>The gh commands behind the public-repository steps: the ruleset and secret scanning push protection.</summary>
public sealed partial class GitHubService
{
    /// <summary>POST /repos/{o}/{r}/rulesets with the definition passed through a file.</summary>
    public async Task CreateRulesetAsync(string ownerAndName, RulesetDefinition definition, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"designated-driver-ruleset-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(file, definition.ToJson(), new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
            var r = await ApiAsync(ct, "-X", "POST", $"repos/{ownerAndName}/rulesets", "--input", file).ConfigureAwait(false);
            if (!r.Ok) throw new CommandFailedException(new CommandResult("gh", ["api", "-X", "POST", $"repos/{ownerAndName}/rulesets"], 1, r.Output, r.Message, TimeSpan.Zero));
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>Secret scanning and its push protection, which GitHub offers on public repositories and paid plans.</summary>
    public Task EnablePushProtectionAsync(string ownerAndName, CancellationToken ct)
        => MustInFolderAsync(null, ct, "repo", "edit", ownerAndName, "--enable-secret-scanning", "--enable-secret-scanning-push-protection");
}
