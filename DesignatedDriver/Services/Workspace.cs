using System.IO;
using DesignatedDriver.Core;
using DesignatedDriver.Models;

namespace DesignatedDriver.Services;

/// <summary>
/// What every screen shares: the settings, the current repository, its manifest, the command runner, the two
/// services and who the user is.
/// </summary>
public sealed class Workspace
{
    private static readonly RepositorySettings None = new();

    public Workspace(Settings settings) : this(settings, null) { }

    /// <summary>With a runner of the self-test's choosing standing in for git and gh. The activity runner is still the real one.</summary>
    public Workspace(Settings settings, ICommandRunner? commandRunner)
    {
        Settings = settings;
        Runner = new CommandRunner();
        var runner = commandRunner ?? Runner;
        Git = new GitService(runner, () => Current.Folder);
        GitHub = new GitHubService(runner, () => Current, () => Board);
        ReadManifest();
    }

    public Settings Settings { get; }
    public CommandRunner Runner { get; }
    public GitService Git { get; }
    public GitHubService GitHub { get; }

    /// <summary>The current repository; an empty one, with no folder, when none is chosen.</summary>
    public RepositorySettings Current => Settings.CurrentRepository ?? None;
    public bool HasRepository => Current.HasFolder;

    /// <summary>The current repository's manifest, when it has been set up by the tool.</summary>
    public Manifest? Manifest { get; private set; }

    /// <summary>The board for the current repository: from the manifest, else the settings override, else none.</summary>
    public BoardReference? Board
        => Manifest?.Board is { Number: > 0 } fromManifest ? fromManifest
         : Current.HasBoardOverride ? new BoardReference(Current.BoardOwner, Current.BoardNumber)
         : null;

    /// <summary>The signed-in GitHub user, once known.</summary>
    public string Login { get; private set; } = "";
    /// <summary>ADMIN, WRITE and so on, as gh repo view reports it.</summary>
    public string Permission { get; private set; } = "";
    /// <summary>The account's plan name (free, pro, ...), once probed. Empty until then, and empty when the token hides it.</summary>
    public string PlanName { get; set; } = "";
    public bool PlanProbed { get; set; }
    public bool IsAdmin => string.Equals(Permission, "ADMIN", StringComparison.OrdinalIgnoreCase);
    public bool IdentityKnown => Login.Length > 0;

    /// <summary>The last scan of each repository this session, by owner and name, for the Repositories screen.</summary>
    public Dictionary<string, (ScanReport Report, DateTime When)> LastScans { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last Check of each repository this session, by owner and name, for Status and Repositories.</summary>
    public Dictionary<string, (DriftReport Report, DateTime When)> LastChecks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This repository's billable Actions minutes for the month, summed once per session from its runs.</summary>
    public Dictionary<string, (int Minutes, int Runs, DateTime When)> ActionsMinutes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The repositories starred on GitHub, read for the Repositories screen; null until read, or when the read failed.</summary>
    public HashSet<string>? Starred { get; private set; }
    /// <summary>Why the last read of the starred repositories failed; empty when it worked.</summary>
    public string StarredError { get; private set; } = "";

    /// <summary>Reads the starred repositories, keeping the last good set when the read fails, and tells the header to show the stars.</summary>
    public async Task<HashSet<string>?> ReadStarredAsync(CancellationToken ct)
    {
        try
        {
            Starred = await GitHub.StarredAsync(ct).ConfigureAwait(false);
            StarredError = "";
        }
        catch (CommandFailedException ex)
        {
            StarredError = ex.Message;
        }
        StarredChanged?.Invoke(this, EventArgs.Empty);
        return Starred;
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler? IdentityChanged;
    public event EventHandler? StarredChanged;

    // Repositories

    /// <summary>Makes a known repository the current one.</summary>
    public void SelectRepository(string ownerAndName)
    {
        if (Settings.Find(ownerAndName) is null) return;
        if (Settings.Current.Equals(ownerAndName, StringComparison.OrdinalIgnoreCase)) return;
        Settings.Current = ownerAndName;
        Settings.Save();
        ReadManifest();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a repository, or replaces the entry with the same owner and name, and makes it current.</summary>
    public void SaveRepository(RepositorySettings repository)
    {
        var existing = Settings.Find(repository.OwnerAndName);
        if (existing is not null) Settings.Repositories[Settings.Repositories.IndexOf(existing)] = repository;
        else Settings.Repositories.Add(repository);
        Settings.Current = repository.OwnerAndName;
        Settings.Save();
        ReadManifest();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets a repository. The clone and the repository on GitHub are untouched.</summary>
    public void RemoveRepository(string ownerAndName)
    {
        var existing = Settings.Find(ownerAndName);
        if (existing is null) return;
        Settings.Repositories.Remove(existing);
        if (Settings.Current.Equals(ownerAndName, StringComparison.OrdinalIgnoreCase))
            Settings.Current = Settings.Repositories.FirstOrDefault()?.OwnerAndName ?? "";
        Settings.Save();
        ReadManifest();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReadManifest() => Manifest = Current.HasFolder ? Manifest.Load(Current.Folder) : null;

    /// <summary>
    /// Clones a repository that already exists on GitHub and adds it to the list as it is, making it current. Only
    /// gh reads and git clone run: nothing changes on GitHub or in the repository. The clone uses the protocol and SSH
    /// host from Settings, the alias found for the signed-in login rather than the repository's owner, which may be an
    /// organisation. A clone of a repository set up to the convention has its hooks pointed at, as adding a clone
    /// promises; that is local configuration only.
    /// </summary>
    public async Task<RepositorySettings> CloneAndAddAsync(string ownerAndName, string parentFolder, CancellationToken ct)
    {
        var name = ownerAndName.Trim().Trim('/');
        var parts = name.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Trim().Length == 0)) throw new InvalidOperationException(string.Format(Resources.Strings.Clone_NameInvalid, name));
        if (parentFolder.Trim().Length == 0) throw new InvalidOperationException(Resources.Strings.Setup_ParentMissing);
        if (Settings.Find(name) is { HasFolder: true } known) throw new InvalidOperationException(string.Format(Resources.Strings.Clone_AlreadyKnown, known.OwnerAndName, known.Folder));

        var details = await GitHub.RepositoryDetailsAsync(name, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Format(Resources.Strings.Scan_RepositoryNotFound, name));
        if (!details.IsNamed(name)) throw new InvalidOperationException(string.Format(Resources.Strings.Scan_RepositoryMoved, name, details.FullName));
        // GitHub's spelling of the owner and name, whatever case was typed.
        var fullName = details.FullName.Length > 0 ? details.FullName : name;
        var owner = fullName.Split('/')[0];
        var repoName = fullName.Split('/')[1];

        var folder = Path.Combine(parentFolder.Trim(), repoName);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException(string.Format(Resources.Strings.Setup_FolderNotEmpty, folder));
        Directory.CreateDirectory(parentFolder.Trim());

        var login = await GitHub.LoginAsync(ct).ConfigureAwait(false);
        var (protocol, host) = await ResolveTransportAsync(login, ct).ConfigureAwait(false);
        await Git.CloneAsync(RemoteUrl.For(protocol, host, fullName), folder, ct).ConfigureAwait(false);

        var repository = new RepositorySettings
        {
            Folder = folder,
            Owner = owner,
            Name = repoName,
            DefaultBranch = details.DefaultBranch.Length > 0 ? details.DefaultBranch : RepositorySettings.DefaultDefaultBranch,
        };
        // A repository set up to the convention says in its manifest what it checks; its hooks are pointed at.
        if (Manifest.Load(folder) is { } manifest)
        {
            repository.SelfTestCommand = manifest.SelfTestCommand;
            if (manifest.RequiredCheck.Length > 0) repository.RequiredCheck = manifest.RequiredCheck;
            if (Directory.Exists(Path.Combine(folder, ConventionFiles.HooksFolder)))
                await Git.SetHooksPathInAsync(folder, ConventionFiles.HooksFolder, ct).ConfigureAwait(false);
        }
        SaveRepository(repository);
        return repository;
    }

    /// <summary>
    /// How to clone and push: the tool's setting when chosen, else gh's own protocol for github.com, else HTTPS; and for SSH
    /// the host or alias from the setting, else the one in ~/.ssh/config that names the login, else github.com.
    /// </summary>
    public async Task<(string Protocol, string SshHost)> ResolveTransportAsync(string login, CancellationToken ct)
    {
        var protocol = Settings.GitProtocol;
        if (protocol.Length == 0)
        {
            var fromGh = await GitHub.GitProtocolAsync(ct).ConfigureAwait(false);
            protocol = fromGh.Equals(RemoteUrl.Ssh, StringComparison.OrdinalIgnoreCase) ? RemoteUrl.Ssh : RemoteUrl.Https;
        }
        var host = Settings.SshHost.Length > 0 ? Settings.SshHost : SshConfig.HostFor(login);
        return (protocol, host);
    }

    /// <summary>Reads the origin remote and stores owner and name in the current repository's settings.</summary>
    public async Task<bool> ReadOriginAsync(CancellationToken ct)
    {
        if (!HasRepository) return false;
        var url = await Git.OriginUrlAsync(ct).ConfigureAwait(false);
        var parsed = RemoteUrl.ParseOwnerAndRepository(url);
        if (parsed is null) return false;
        var current = Current;
        if (current.Owner != parsed.Value.Owner || current.Name != parsed.Value.Repository)
        {
            current.Owner = parsed.Value.Owner;
            current.Name = parsed.Value.Repository;
            Settings.Current = current.OwnerAndName;
            Settings.Save();
        }
        return true;
    }

    /// <summary>Who is signed in and what role they have. Failures leave the identity unknown rather than throwing.</summary>
    public async Task RefreshIdentityAsync(CancellationToken ct)
    {
        string login = "", permission = "";
        try
        {
            login = await GitHub.LoginAsync(ct).ConfigureAwait(false);
            if (Current.OwnerAndName.Length > 0)
                permission = (await GitHub.RepositoryAsync(ct).ConfigureAwait(false)).ViewerPermission;
        }
        catch (CommandFailedException)
        {
            // Not signed in, no network, or no access: the Status screen explains.
        }
        var changed = login != Login || permission != Permission;
        Login = login;
        Permission = permission;
        if (changed) IdentityChanged?.Invoke(this, EventArgs.Empty);
    }
}
