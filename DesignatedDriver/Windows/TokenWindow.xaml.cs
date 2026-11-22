using System.Windows;
using DesignatedDriver.Helpers;

namespace DesignatedDriver.Windows;

/// <summary>
/// Asks for the classic personal access token the board workflow needs (section 3.3 of the design). The token
/// is handed back once, stored by gh secret set, and never written to settings or the activity panel.
/// </summary>
public partial class TokenWindow : Window
{
    /// <summary>The scopes the token needs, pre-filled on GitHub's new token page.</summary>
    public const string NewTokenUrl = "https://github.com/settings/tokens/new?scopes=repo,project&description=Repo%20Starter%20board";

    public TokenWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TokenBox.Focus();
    }

    public string Token { get; private set; } = "";

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e) => UseButton.IsEnabled = TokenBox.Password.Trim().Length > 0;

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenUrl(NewTokenUrl);

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        Token = TokenBox.Password.Trim();
        TokenBox.Clear();
        DialogResult = true;
    }
}
