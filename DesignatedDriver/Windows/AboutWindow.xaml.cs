using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using DesignatedDriver.Helpers;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;

namespace DesignatedDriver.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow(Workspace workspace)
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        VersionText.Text = string.Format(Strings.About_Version, version.Major, version.Minor, version.Build);
        CopyrightText.Text = string.Format(Strings.About_Copyright, DateTime.Now.Year);

        // The links are the current repository's. Without one there is nothing to link to.
        var repo = workspace.Current;
        if (repo.OwnerAndName.Length == 0)
        {
            LinksCard.Visibility = Visibility.Collapsed;
            return;
        }
        RepositoryLink.NavigateUri = new Uri($"https://github.com/{repo.OwnerAndName}");
        ContributingLink.NavigateUri = new Uri($"https://github.com/{repo.OwnerAndName}/blob/{repo.DefaultBranch}/CONTRIBUTING.md");
        if (workspace.Board is { } board)
        {
            // A user's board lives under users/, an organisation's under orgs/. The signed-in user's own login is a user.
            var kind = board.Owner.Equals(workspace.Login, StringComparison.OrdinalIgnoreCase) || board.Owner.Equals(repo.Owner, StringComparison.OrdinalIgnoreCase) ? "users" : "orgs";
            BoardLink.NavigateUri = new Uri($"https://github.com/{kind}/{board.Owner}/projects/{board.Number}");
        }
        else
        {
            BoardLine.Visibility = Visibility.Collapsed;
        }
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        UiHelpers.OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
