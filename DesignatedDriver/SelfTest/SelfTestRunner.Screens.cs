using System.Windows;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Resources;
using DesignatedDriver.Screens;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver.SelfTest;

/// <summary>
/// Every screen and window is built once, so a XAML resource that doesn't resolve fails the self-test rather
/// than the first click. Nothing is shown and nothing is loaded; git and gh are a recorder that answers nothing.
/// </summary>
public static partial class SelfTestRunner
{
    private sealed class StubShell(Workspace workspace) : IShell
    {
        private Window? _window;
        public Workspace Workspace { get; } = workspace;
        public Window Window => _window ??= new Window();
        public bool IsBusy => false;
        public Task RunAsync(Func<CancellationToken, Task> work) => Task.CompletedTask;
        public Task RunPickAsync(Func<CancellationToken, Task> work) => Task.CompletedTask;
        public void Navigate(Screen screen, int? pullRequest = null) { }
        public void NavigateToScan(string? folder, string ownerAndName) { }
        public void NavigateToClone(string ownerAndName) { }
        public void NavigateToSetup(string? folder, string ownerAndName, ScanFacts? facts = null) { }
        public void EditRepository(RepositorySettings? repository, bool readFromGitHub = false) { }
        public bool ShowDialog(Window dialog) => false;
        public bool Confirm(string message, string title) => false;
        public void ShowError(string message) { }
        public void ShowInfo(string message) { }
        public void OpenUrl(string url) { }
        public void Announce(string message) { }
    }

    private static void ScreenXamlChecks(Action<bool, string> check)
    {
        if (Application.Current is null)
        {
            check(true, "Screens: no WPF application, so the XAML checks were skipped");
            return;
        }
        var workspace = new Workspace(new Settings(), new RecordingRunner());
        var shell = new StubShell(workspace);
        var builders = new (string Name, Func<object> Build)[]
        {
            ("Repositories", () => new RepositoriesScreen(shell)),
            ("Scan", () => new ScanScreen(shell)),
            ("Set up a repository", () => new NewRepositoryScreen(shell)),
            ("Status", () => new StatusScreen(shell)),
            ("Check", () => new CheckScreen(shell)),
            ("Start work", () => new StartWorkScreen(shell)),
            ("Save and share", () => new SaveShareScreen(shell)),
            ("Submit", () => new SubmitScreen(shell)),
            ("Review", () => new ReviewScreen(shell)),
            ("Tidy up", () => new TidyUpScreen(shell)),
            ("Fix a problem", () => new FixProblemScreen(shell)),
            ("Settings window", () => new SettingsWindow(shell, null)),
            ("Token window", () => new TokenWindow()),
            ("About window", () => new AboutWindow(workspace)),
        };
        foreach (var (name, build) in builders)
        {
            try
            {
                var built = build();
                check(built is not null, $"Screens: {name} builds from its XAML");
                if (built is DependencyObject root)
                {
                    var placeholders = AccessibleTexts(root).Where(t => Placeholder.IsMatch(t)).ToList();
                    check(placeholders.Count == 0, $"Screens: no accessible name or help on {name} is a string with a placeholder{(placeholders.Count > 0 ? ": " + string.Join(", ", placeholders) : "")}");
                }
            }
            catch (Exception ex)
            {
                check(false, $"Screens: {name} failed to build: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Scan's current-repository option is announced with the text it shows, filled in when the screen loads.
        var scan = new ScanScreen(shell);
        scan.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        var spoken = new System.Windows.Automation.Peers.RadioButtonAutomationPeer(scan.CurrentOption).GetName();
        check(spoken == scan.CurrentOption.Content as string && spoken == string.Format(Strings.Scan_TargetCurrent, Strings.Header_NoRepository),
            "Scan: the current-repository option's accessible name is its text, with the repository filled in");

        // Copyright is multifarious's, in the About window and in the assembly the exe's properties show.
        var about = new AboutWindow(workspace);
        var assemblyCopyright = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyCopyrightAttribute>(typeof(SelfTestRunner).Assembly)?.Copyright ?? "";
        check(about.FindName("CopyrightText") is System.Windows.Controls.TextBlock copyright && copyright.Text.Contains("multifarious", StringComparison.Ordinal)
            && assemblyCopyright.Contains("multifarious", StringComparison.Ordinal),
            "About: the copyright names multifarious, in the window and in the assembly");

        // The wizard's own pieces: the pages, the language list and the checklist.
        var wizard = new NewRepositoryScreen(shell);
        check(wizard.FindName("WherePage") is FrameworkElement where && where.Visibility == Visibility.Visible && wizard.FindName("OptionsPage") is FrameworkElement options && options.Visibility == Visibility.Collapsed, "Wizard: starts on the Where page");
        check(wizard.FindName("LanguageCombo") is System.Windows.Controls.ComboBox combo && combo.Items.Count == 7, "Wizard: seven languages to choose from");
        check(wizard.FindName("ChecklistPanel") is System.Windows.Controls.Panel panel && panel.Children.Count == ChecklistItems.All.Count, "Wizard: one checkbox per checklist item");

        // From Scan: a public repository's clone opens with GitHub's visibility, fixed, and its description.
        var scanned = new ScanFacts
        {
            Folder = @"C:\Users\paul\Documents\GIT\example", OwnerAndName = "paulfilkin/example", IsGitRepository = true,
            Details = new Models.GhRepositoryDetails { Private = false, Description = "An example" },
        };
        var prefilled = new NewRepositoryScreen(shell);
        prefilled.Prefill(scanned.Folder, scanned.OwnerAndName, scanned);
        check(prefilled.PublicOption.IsChecked == true && !prefilled.PublicOption.IsEnabled && !prefilled.PrivateOption.IsEnabled
            && prefilled.VisibilityHelp.Text == Strings.Setup_VisibilityFromGitHub && prefilled.DescriptionBox.Text == "An example",
            "Wizard: from Scan, a public clone opens as Public, fixed, with its description");
        prefilled.NewOption.IsChecked = true;
        check(prefilled.PrivateOption.IsEnabled && prefilled.PublicOption.IsEnabled, "Wizard: choosing a new repository gives the visibility choice back");
    }

    private static readonly System.Text.RegularExpressions.Regex Placeholder = new(@"\{\d+\}");

    /// <summary>Every accessible name and help text in a built screen, walking the logical tree.</summary>
    private static IEnumerable<string> AccessibleTexts(DependencyObject root)
    {
        var name = System.Windows.Automation.AutomationProperties.GetName(root);
        if (!string.IsNullOrEmpty(name)) yield return name;
        var help = System.Windows.Automation.AutomationProperties.GetHelpText(root);
        if (!string.IsNullOrEmpty(help)) yield return help;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var text in AccessibleTexts(child)) yield return text;
    }
}
