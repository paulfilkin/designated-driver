using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Screens;

/// <summary>Start work: pick a Ready issue, then switch to main, pull, assign, move the card and create the branch.</summary>
public partial class StartWorkScreen : ScreenBase
{
    private sealed record IssueRow(GhIssue Issue, string? ProjectItemId, string BoardStatus);

    private List<IssueRow> _rows = [];
    private readonly Dictionary<string, TextBox> _issueBoxes = [];
    private bool _clean;
    private bool _hasBoard;

    public StartWorkScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        BuildIssueFields();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Shell.RunAsync(RefreshAsync);
    private void ShowAll_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) ShowIssues(); }
    private void GoToSave_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.SaveAndShare);
    private void GoToFix_Click(object sender, RoutedEventArgs e) => Shell.Navigate(Screen.FixProblem);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        // 1. Stop if there are uncommitted changes.
        var status = await Workspace.Git.StatusAsync(ct);
        _clean = status.IsClean;
        DirtyNotice.Visibility = _clean ? Visibility.Collapsed : Visibility.Visible;
        DirtyText.Text = string.Format(Strings.StartWork_Dirty, status.Changes.Count, status.Branch);

        // 2. Issues: the open ones from the repository (for labels and colours) joined with the board for the column.
        var issues = await Workspace.GitHub.OpenIssuesAsync(ct);
        var board = new Dictionary<int, GhProjectItem>();
        _hasBoard = false;
        if (Workspace.Board is { } reference)
        {
            try
            {
                foreach (var item in (await Workspace.GitHub.ProjectItemsAsync(ct)).Items.Where(i => i.IsIssue))
                    board[item.Number] = item;
                IssuesNote.Text = string.Format(Strings.StartWork_BoardNote, reference.Owner, reference.Number);
                _hasBoard = true;
            }
            catch (Services.CommandFailedException ex)
            {
                // Without the project scope the board can't be read; all open issues are still offered.
                IssuesNote.Text = string.Format(Strings.StartWork_BoardUnavailable, ex.Message);
            }
        }
        else
        {
            // A repository that hasn't been set up yet, with no board named in its settings either.
            IssuesNote.Text = Strings.StartWork_NoBoard;
        }
        if (!_hasBoard) ShowAllBox.IsChecked = true;

        _rows = issues.Select(i => new IssueRow(i, board.TryGetValue(i.Number, out var item) ? item.Id : null, board.TryGetValue(i.Number, out var it) ? it.Status : ""))
                      .OrderBy(r => r.Issue.Number).ToList();
        ShowIssues();
        UpdateStartButton();
    }

    private void ShowIssues()
    {
        bool all = ShowAllBox.IsChecked == true;
        var shown = all ? _rows : _rows.Where(r => BoardColumns.IsPickUp(r.BoardStatus, Repo.ReadyColumn)).ToList();
        IssuesTitle.Text = all ? Strings.StartWork_AllIssues : Strings.StartWork_ReadyIssues;

        IssueList.Items.Clear();
        foreach (var row in shown)
        {
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new TextBlock { Text = $"#{row.Issue.Number}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = row.Issue.Title });
            var labels = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var l in row.Issue.Labels) labels.Children.Add(UiHelpers.LabelChip(l));
            if (row.Issue.Assignees.Count > 0)
                labels.Children.Add(UiHelpers.Tag(string.Format(Strings.StartWork_AssignedTo, string.Join(", ", row.Issue.Assignees.Select(a => a.Login))), "InfoSoftBrush", "TertiaryBrush"));
            body.Children.Add(labels);
            var column = UiHelpers.Tag(row.BoardStatus.Length > 0 ? row.BoardStatus : Strings.StartWork_NotOnBoard);
            column.VerticalAlignment = VerticalAlignment.Top;
            if (!_hasBoard) column.Visibility = Visibility.Collapsed;

            Grid.SetColumn(body, 1); Grid.SetColumn(column, 2);
            panel.Children.Add(number); panel.Children.Add(body); panel.Children.Add(column);

            var item = new ListBoxItem { Content = panel, Tag = row };
            AutomationProperties.SetName(item, $"#{row.Issue.Number} {row.Issue.Title}");
            if (!IsReadyToWorkOn(row.Issue)) item.ToolTip = Strings.StartWork_NotReadyTip;
            IssueList.Items.Add(item);
        }
        EmptyText.Text = all ? Strings.StartWork_NoIssues : Strings.StartWork_NoReadyIssues;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IssueList.Visibility = shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The convention's only hold on an issue is the blocked label; readiness is the board's Ready column.</summary>
    private static bool IsReadyToWorkOn(GhIssue issue)
        => !issue.LabelNames.Any(n => n.Equals("blocked", StringComparison.OrdinalIgnoreCase));

    private IssueRow? Selected => (IssueList.SelectedItem as ListBoxItem)?.Tag as IssueRow;

    private void IssueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = Selected;
        if (row is null)
        {
            SelectedIssueText.Text = Strings.StartWork_NoneSelected;
            BranchBox.Text = "";
        }
        else
        {
            SelectedIssueText.Text = $"#{row.Issue.Number}  {row.Issue.Title}";
            BranchBox.Text = BranchNames.Build(row.Issue.LabelNames, row.Issue.Number, row.Issue.Title);
        }
        ShowIssueCard(row?.Issue);
        UpdateStartButton();
    }

    // The picked issue's card (issue 89). The list already holds each issue's body, so nothing more is read.

    private string _pickedUrl = "";

    private void OpenIssue_Click(object sender, RoutedEventArgs e) { if (_pickedUrl.Length > 0) Shell.OpenUrl(_pickedUrl); }

    private void ShowIssueCard(GhIssue? issue)
    {
        IssueCard.Visibility = issue is null ? Visibility.Collapsed : Visibility.Visible;
        _pickedUrl = issue?.Url ?? "";
        if (issue is null) return;

        IssueCardTitle.Text = $"#{issue.Number}  {issue.Title}";
        IssueCardTags.Children.Clear();
        foreach (var label in issue.Labels)
        {
            var chip = UiHelpers.LabelChip(label);
            chip.Margin = new Thickness(0, 0, 6, 4);
            IssueCardTags.Children.Add(chip);
        }
        IssueCardTags.Visibility = issue.Labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var opened = issue.CreatedAt == default ? "" : issue.CreatedAt.ToLocalTime().ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        IssueCardOpened.Text = opened.Length == 0 ? "" : issue.Author is { Login.Length: > 0 } author ? string.Format(Strings.StartWork_OpenedBy, author.Login, opened) : string.Format(Strings.StartWork_OpenedOn, opened);
        IssueCardOpened.Visibility = IssueCardOpened.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        IssueBodyPanel.Children.Clear();
        var blocks = IssueBodyLayout.Parse(issue.Body);
        if (blocks.Count == 0) IssueBodyPanel.Children.Add(UiHelpers.Text(Strings.StartWork_NoDescription, "HelperText"));
        foreach (var block in blocks) IssueBodyPanel.Children.Add(BlockElement(block));
        IssueBodyScroller.ScrollToTop();
    }

    /// <summary>One block of the body as WPF: headings bolder, lists and task lines indented with their marks, code in a box, No response greyed.</summary>
    private static FrameworkElement BlockElement(BodyBlock block)
    {
        switch (block.Kind)
        {
            case BodyBlockKind.Heading:
                var heading = SpanText(block.Spans);
                heading.FontWeight = FontWeights.SemiBold;
                heading.FontSize = block.Level <= 2 ? 17 : 15;
                heading.Margin = new Thickness(0, 10, 0, 4);
                return heading;
            case BodyBlockKind.NoResponse:
                var none = SpanText(block.Spans);
                none.FontStyle = FontStyles.Italic;
                none.Foreground = UiHelpers.Brush("TextTertiaryBrush");
                none.Margin = new Thickness(0, 0, 0, 6);
                return none;
            case BodyBlockKind.Code:
                return new Border
                {
                    Background = UiHelpers.Brush("NeutralSoftestBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = new TextBlock { Text = block.Text, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap },
                };
            case BodyBlockKind.Bullet:
            case BodyBlockKind.Task:
                var row = new Grid { Margin = new Thickness(block.Level * 18, 0, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var mark = block.Kind == BodyBlockKind.Task ? (block.Ticked ? "☑" : "☐") : block.Marker.Length > 0 ? block.Marker : "•";
                row.Children.Add(new TextBlock { Text = mark, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol, Segoe UI"), Foreground = UiHelpers.Brush("TextSecondaryBrush") });
                var text = SpanText(block.Spans);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                AutomationProperties.SetName(row, block.Kind != BodyBlockKind.Task ? block.Text
                    : string.Format(block.Ticked ? Strings.StartWork_Ticked : Strings.StartWork_NotTicked, block.Text));
                return row;
            default:
                var paragraph = SpanText(block.Spans);
                paragraph.Margin = new Thickness(0, 0, 0, 6);
                return paragraph;
        }
    }

    /// <summary>A wrapping text block of the spans, bold and code runs marked.</summary>
    private static TextBlock SpanText(IReadOnlyList<BodySpan> spans)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiHelpers.Brush("TextPrimaryBrush") };
        foreach (var span in spans)
        {
            var run = new System.Windows.Documents.Run(span.Text);
            if (span.Bold) run.FontWeight = FontWeights.SemiBold;
            if (span.Code)
            {
                run.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
                run.Background = UiHelpers.Brush("NeutralSoftestBrush");
            }
            text.Inlines.Add(run);
        }
        return text;
    }

    private void BranchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateStartButton();

    private void UpdateStartButton()
    {
        var row = Selected;
        var branch = BranchBox.Text.Trim();
        string note = "";
        if (row is not null)
        {
            if (branch.Length == 0) note = Strings.StartWork_BranchEmpty;
            else if (!BranchNames.FollowsConvention(branch)) note = Strings.StartWork_BranchConvention;
            else if (BranchNames.IssueNumber(branch) != row.Issue.Number) note = string.Format(Strings.StartWork_BranchWrongIssue, row.Issue.Number);
            else if (!IsReadyToWorkOn(row.Issue)) note = Strings.StartWork_NotReadyTip;
        }
        BranchNote.Text = note;
        BranchNote.Foreground = UiHelpers.Brush(note.Length == 0 ? "TextTertiaryBrush" : "WarningBrush");
        StartButton.IsEnabled = row is not null && _clean && branch.Length > 0 && !Shell.IsBusy;
    }

    // New issue

    private IssueKind Kind => FeatureOption.IsChecked == true ? IssueKind.Feature : ChoreOption.IsChecked == true ? IssueKind.Chore : DocsOption.IsChecked == true ? IssueKind.Docs : IssueKind.Bug;

    private void NewIssueToggle_Click(object sender, RoutedEventArgs e)
    {
        bool open = NewIssuePanel.Visibility != Visibility.Visible;
        NewIssuePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NewIssueToggle.Content = open ? Strings.NewIssue_Hide : Strings.NewIssue_Show;
        AutomationProperties.SetName(NewIssueToggle, open ? Strings.NewIssue_Hide : Strings.NewIssue_Show);
        if (open) IssueTitleBox.Focus();
    }

    private void IssueKind_Changed(object sender, RoutedEventArgs e)
    {
        if (IssueFieldsPanel is null) return;
        BuildIssueFields();
    }

    private void IssueField_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreateButton();

    /// <summary>The fields of the chosen kind, one labelled box each, in the order the body will have them.</summary>
    private void BuildIssueFields()
    {
        var kept = IssueValues();
        IssueFieldsPanel.Children.Clear();
        _issueBoxes.Clear();
        foreach (var field in IssueTexts.FieldsFor(Kind))
        {
            var label = field.Required ? field.Label : string.Format(Strings.NewIssue_Optional, field.Label);
            IssueFieldsPanel.Children.Add(UiHelpers.Text(label, "LabelText"));
            var box = new TextBox
            {
                Style = (Style)FindResource(field.MultiLine ? "MultilineTextBox" : "ModernTextBox"),
                AcceptsReturn = field.MultiLine,
                MinHeight = field.MultiLine ? 64 : 0,
                Text = kept.TryGetValue(field.Id, out var v) ? v : "",
            };
            AutomationProperties.SetName(box, label);
            box.TextChanged += IssueField_TextChanged;
            _issueBoxes[field.Id] = box;
            IssueFieldsPanel.Children.Add(box);
        }
        UpdateCreateButton();
    }

    private Dictionary<string, string> IssueValues() => _issueBoxes.ToDictionary(p => p.Key, p => p.Value.Text);

    private void UpdateCreateButton()
    {
        if (CreateIssueButton is null) return;
        NewIssueNote.Text = string.Format(Strings.NewIssue_Note, IssueTexts.Label(Kind));
        CreateIssueButton.IsEnabled = IssueTexts.IsComplete(Kind, IssueTitleBox.Text, IssueValues()) && !Shell.IsBusy;
    }

    /// <summary>Creates the issue with gh, puts it on the board so it lands in Todo, then reloads and selects it.</summary>
    private void CreateIssue_Click(object sender, RoutedEventArgs e)
    {
        var kind = Kind;
        var title = IssueTitleBox.Text.Trim();
        var body = IssueTexts.Body(kind, IssueValues());
        if (!IssueTexts.IsComplete(kind, title, IssueValues())) return;

        _ = Shell.RunAsync(async ct =>
        {
            var (number, url) = await Workspace.GitHub.CreateIssueAsync(Repo.OwnerAndName, title, body, IssueTexts.Label(kind), ct);
            if (Workspace.Board is { } board && number > 0)
            {
                try { await Workspace.GitHub.AddToBoardAsync(board, url, ct); }
                catch (Services.CommandFailedException ex) { Workspace.Runner.Note(string.Format(Strings.NewIssue_BoardFailed, number, ex.Message)); }
            }
            IssueTitleBox.Text = "";
            foreach (var box in _issueBoxes.Values) box.Text = "";
            await RefreshAsync(ct);
            SelectIssue(number);
            Shell.Announce(string.Format(Strings.NewIssue_Done, number));
        });
    }

    /// <summary>Selects the issue in the list, showing all issues if the board has not yet put it in Todo.</summary>
    private void SelectIssue(int number)
    {
        if (number <= 0) return;
        var item = FindIssueItem(number);
        if (item is null && ShowAllBox.IsChecked != true)
        {
            ShowAllBox.IsChecked = true;
            item = FindIssueItem(number);
        }
        if (item is null) return;
        IssueList.SelectedItem = item;
        IssueList.ScrollIntoView(item);
    }

    private ListBoxItem? FindIssueItem(int number)
        => IssueList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is IssueRow row && row.Issue.Number == number);

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) return;
        var branch = BranchBox.Text.Trim();
        var message = string.Format(Strings.StartWork_Confirm, Repo.DefaultBranch, row.Issue.Number, branch);
        if (!Shell.Confirm(message, Strings.StartWork_Start)) return;

        _ = Shell.RunAsync(async ct =>
        {
            var git = Workspace.Git;
            var gh = Workspace.GitHub;

            if (!await git.IsValidBranchNameAsync(branch, ct)) throw new InvalidOperationException(string.Format(Strings.StartWork_BranchInvalid, branch));
            if (await git.LocalBranchExistsAsync(branch, ct)) throw new InvalidOperationException(string.Format(Strings.StartWork_BranchExists, branch));

            var status = await git.StatusAsync(ct);
            if (!status.IsClean) throw new InvalidOperationException(Strings.StartWork_StillDirty);

            await git.SwitchAsync(Repo.DefaultBranch, ct);
            await git.PullAsync(ct);
            await gh.AssignToMeAsync(row.Issue.Number, ct);

            if (row.ProjectItemId is not null)
            {
                var column = BoardColumns.InProgressFor(Repo.InProgressColumn);
                if (!await gh.MoveCardAsync(row.ProjectItemId, column, ct))
                    Workspace.Runner.Note(string.Format(Strings.StartWork_NoInProgressColumn, column));
            }
            else if (_hasBoard)
            {
                Workspace.Runner.Note(string.Format(Strings.StartWork_IssueNotOnBoard, row.Issue.Number));
            }

            await git.CreateBranchAsync(branch, ct);
            Shell.Announce(string.Format(Strings.StartWork_Done, branch, row.Issue.Number));
            Invalidate();
            Shell.ShowInfo(string.Format(Strings.StartWork_Done, branch, row.Issue.Number));
            Shell.Navigate(Screen.SaveAndShare);
        });
    }
}
