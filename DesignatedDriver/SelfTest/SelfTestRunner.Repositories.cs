using DesignatedDriver.Core;

namespace DesignatedDriver.SelfTest;

/// <summary>Repositories filter checks: text against owner, name and folder, case-blind and word by word, and the starred rule.</summary>
public static partial class SelfTestRunner
{
    private static void RepositoryFilterChecks(Action<bool, string> check)
    {
        var repo = new RepositorySettings { Owner = "paulfilkin", Name = "repo-starter", Folder = @"C:\Users\paul\Documents\GIT\.NET\GIT_Repo_Manager" };
        var other = new RepositorySettings { Owner = "example-org", Name = "example-app", Folder = @"C:\Users\paul\Documents\GIT\Example" };
        var starred = RepositoryFilter.StarredSet(["PaulFilkin/Repo-Starter"]);

        check(RepositoryFilter.MatchesText(repo, "") && RepositoryFilter.MatchesText(repo, "   "), "Filter: empty text matches everything");
        check(RepositoryFilter.MatchesText(repo, "STARTER") && RepositoryFilter.MatchesText(repo, "paulfilkin") && RepositoryFilter.MatchesText(repo, "git_repo"),
            "Filter: text matches the owner, the name or the folder without regard to case");
        check(RepositoryFilter.MatchesText(repo, "paul starter") && !RepositoryFilter.MatchesText(repo, "paul partner"), "Filter: every word must match");
        check(!RepositoryFilter.MatchesText(other, "starter") && RepositoryFilter.MatchesText(other, "org"), "Filter: a repository without the text is left out");

        check(RepositoryFilter.IsStarred(repo, starred) && !RepositoryFilter.IsStarred(other, starred), "Filter: starred is judged by owner/name without regard to case");
        check(RepositoryFilter.Matches(repo, "", starredOnly: true, starred) && !RepositoryFilter.Matches(other, "", starredOnly: true, starred)
            && RepositoryFilter.Matches(other, "", starredOnly: false, starred) && !RepositoryFilter.Matches(repo, "partner", starredOnly: true, starred),
            "Filter: starred only keeps the starred repositories, and the text still applies");
    }
}
