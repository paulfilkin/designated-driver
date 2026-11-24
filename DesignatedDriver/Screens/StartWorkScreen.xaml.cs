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
    // New issue's choices and the controls of the chosen one's fields, by field id (issue 88).
    private List<IssueChoice> _choices = [];
    private IssueChoice? _choice;
    private readonly Dictionary<string, TextBox> _issueBoxes = [];
    private readonly Dictionary<string, ComboBox> _issueCombos = [];
    private readonly Dictionary<string, List<CheckBox>> _issueTicks = [];
    private bool _clean;
    private bool _hasBoard;

    public StartWorkScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        // The built-in choices until the load has read the repository's issue forms.
        ShowChoices([]);
        ChoiceSource.Text = "";
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

        // 3. New issue's choices: the repository's own issue forms on GitHub, or the built-in four (issue 88).
        ShowChoices(Repo.OwnerAndName.Length > 0 ? await Workspace.GitHub.IssueFormsAsync(Repo.OwnerAndName, ct) : []);
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
        UpdateStartButton();
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

    private void NewIssueToggle_Click(object sender, RoutedEventArgs e)
    {
        bool open = NewIssuePanel.Visibility != Visibility.Visible;
        NewIssuePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NewIssueToggle.Content = open ? Strings.NewIssue_Hide : Strings.NewIssue_Show;
        AutomationProperties.SetName(NewIssueToggle, open ? Strings.NewIssue_Hide : Strings.NewIssue_Show);
        if (open) IssueTitleBox.Focus();
    }

    /// <summary>
    /// One radio button per choice: the repository's issue forms (null when they couldn't be read) with Chore and
    /// Docs where no form carries their label, or the built-in four. The chosen one stays chosen if it is still offered.
    /// </summary>
    private void ShowChoices(List<IssueChoice>? forms)
    {
        var previous = _choice?.Name;
        _choices = IssueTexts.Choices(forms ?? []);
        ChoiceSource.Text = forms is null ? Strings.NewIssue_FormsUnreadable : forms.Count > 0 ? Strings.NewIssue_FromForms : Strings.NewIssue_BuiltIn;
        var pick = _choices.FirstOrDefault(c => c.Name == previous) ?? _choices[0];
        _choice = null;
        ChoicePanel.Children.Clear();
        foreach (var choice in _choices)
        {
            var radio = new RadioButton { Content = choice.Name, GroupName = "IssueChoice", Margin = new Thickness(0, 0, 16, 6), Tag = choice, IsChecked = ReferenceEquals(choice, pick) };
            AutomationProperties.SetName(radio, choice.Name);
            radio.Checked += IssueChoice_Changed;
            ChoicePanel.Children.Add(radio);
        }
        SelectChoice(pick);
    }

    private void IssueChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: IssueChoice choice } && !ReferenceEquals(choice, _choice)) SelectChoice(choice);
    }

    /// <summary>Shows the choice's description and fields, and puts its title prefix in the title box unless the user has written a title.</summary>
    private void SelectChoice(IssueChoice choice)
    {
        var old = _choice;
        _choice = choice;
        ChoiceDescription.Text = choice.Description;
        ChoiceDescription.Visibility = choice.Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var title = IssueTitleBox.Text.Trim();
        if (title.Length == 0 || (old is not null && old.TitlePrefix.Trim().Length > 0 && title == old.TitlePrefix.Trim()))
            IssueTitleBox.Text = choice.TitlePrefix;
        BuildIssueFields();
    }

    private void IssueField_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreateButton();
    private void IssueTick_Changed(object sender, RoutedEventArgs e) => UpdateCreateButton();

    /// <summary>
    /// The chosen form's fields in the order the body will have them, each with the control for its type: a box
    /// for textarea and input, a list for a dropdown (tick boxes when it allows several), a tick box per checkbox
    /// option, and help text for markdown. Answers to a field with the same id are kept across choices.
    /// </summary>
    private void BuildIssueFields(bool keep = true)
    {
        if (_choice is null) return;
        var kept = keep ? IssueAnswersNow() : new IssueAnswers();
        IssueFieldsPanel.Children.Clear();
        _issueBoxes.Clear();
        _issueCombos.Clear();
        _issueTicks.Clear();
        foreach (var field in _choice.Fields)
        {
            if (field.Type == IssueFieldType.Markdown)
            {
                var help = UiHelpers.Text(field.Value, "HelperText");
                help.TextWrapping = TextWrapping.Wrap;
                help.Margin = new Thickness(0, 8, 0, 0);
                IssueFieldsPanel.Children.Add(help);
                continue;
            }
            bool optional = field.Type != IssueFieldType.Checkboxes && !field.Required;
            var label = optional ? string.Format(Strings.NewIssue_Optional, field.Label) : field.Label;
            IssueFieldsPanel.Children.Add(UiHelpers.Text(label, "LabelText"));
            if (field.Description.Trim().Length > 0)
            {
                var description = UiHelpers.Text(field.Description.Trim(), "HelperText");
                description.TextWrapping = TextWrapping.Wrap;
                description.Margin = new Thickness(0, 0, 0, 4);
                IssueFieldsPanel.Children.Add(description);
            }
            if (field.Type == IssueFieldType.Dropdown && !field.Multiple) AddIssueCombo(field, label, kept);
            else if (field.Type is IssueFieldType.Dropdown or IssueFieldType.Checkboxes) AddIssueTicks(field, kept);
            else AddIssueBox(field, label, kept);
        }
        UpdateCreateButton();
    }

    private void AddIssueBox(IssueField field, string label, IssueAnswers kept)
    {
        bool multi = field.Type == IssueFieldType.Textarea;
        var box = new TextBox
        {
            Style = (Style)FindResource(!multi ? "ModernTextBox" : field.Render.Length > 0 ? "MonoTextBox" : "MultilineTextBox"),
            AcceptsReturn = multi,
            MinHeight = multi ? 64 : 0,
            Text = kept.TextOf(field.Id) is { Length: > 0 } v ? v : field.Value.TrimEnd('\n'),
        };
        AutomationProperties.SetName(box, label);
        if (field.Description.Trim().Length > 0) AutomationProperties.SetHelpText(box, field.Description.Trim());
        box.TextChanged += IssueField_TextChanged;
        _issueBoxes[field.Id] = box;
        IssueFieldsPanel.Children.Add(box);
    }

    /// <summary>A single-choice dropdown; an optional one starts with None, and the form's default is chosen first.</summary>
    private void AddIssueCombo(IssueField field, string label, IssueAnswers kept)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260, Padding = new Thickness(8, 5, 8, 5) };
        if (!field.Required) combo.Items.Add(new ComboBoxItem { Content = Strings.NewIssue_NoChoice, Tag = "" });
        foreach (var option in field.Options) combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Label });
        string? start = kept.Picks.ContainsKey(field.Id) ? kept.PicksOf(field.Id).FirstOrDefault()
            : field.Default is int d && d >= 0 && d < field.Options.Count ? field.Options[d].Label : null;
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => start is not null && (string)i.Tag == start) ?? (field.Required ? null : combo.Items[0]);
        AutomationProperties.SetName(combo, label);
        if (field.Description.Trim().Length > 0) AutomationProperties.SetHelpText(combo, field.Description.Trim());
        combo.SelectionChanged += (_, _) => UpdateCreateButton();
        _issueCombos[field.Id] = combo;
        IssueFieldsPanel.Children.Add(combo);
    }

    /// <summary>A tick box per option, for checkboxes and for a dropdown that allows several choices; a required option says so.</summary>
    private void AddIssueTicks(IssueField field, IssueAnswers kept)
    {
        var panel = new StackPanel();
        var ticks = new List<CheckBox>();
        IReadOnlyList<string> picked = kept.Picks.ContainsKey(field.Id) ? kept.PicksOf(field.Id)
            : field.Type == IssueFieldType.Dropdown && field.Default is int d && d >= 0 && d < field.Options.Count ? [field.Options[d].Label] : [];
        foreach (var option in field.Options)
        {
            var text = option.Required ? string.Format(Strings.NewIssue_RequiredTick, option.Label) : option.Label;
            var tick = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Tag = option.Label, IsChecked = picked.Contains(option.Label) };
            AutomationProperties.SetName(tick, text);
            tick.Checked += IssueTick_Changed;
            tick.Unchecked += IssueTick_Changed;
            ticks.Add(tick);
            panel.Children.Add(tick);
        }
        _issueTicks[field.Id] = ticks;
        IssueFieldsPanel.Children.Add(panel);
    }

    /// <summary>What has been entered: each box's text, and the options chosen or ticked, by field id.</summary>
    private IssueAnswers IssueAnswersNow()
    {
        var answers = new IssueAnswers();
        foreach (var (id, box) in _issueBoxes) answers.Text[id] = box.Text;
        foreach (var (id, combo) in _issueCombos) answers.Picks[id] = combo.SelectedItem is ComboBoxItem { Tag: string t } && t.Length > 0 ? [t] : [];
        foreach (var (id, ticks) in _issueTicks) answers.Picks[id] = [.. ticks.Where(t => t.IsChecked == true).Select(t => (string)t.Tag)];
        return answers;
    }

    private void UpdateCreateButton()
    {
        if (CreateIssueButton is null || _choice is null) return;
        NewIssueNote.Text = _choice.Labels.Count > 0 ? string.Format(Strings.NewIssue_Note, string.Join(", ", _choice.Labels)) : Strings.NewIssue_NoteNoLabel;
        CreateIssueButton.IsEnabled = IssueTexts.IsComplete(_choice, IssueTitleBox.Text, IssueAnswersNow()) && !Shell.IsBusy;
    }

    /// <summary>Creates the issue with gh, puts it on the board so it lands in Todo, then reloads and selects it.</summary>
    private void CreateIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_choice is not { } choice) return;
        var title = IssueTitleBox.Text.Trim();
        var answers = IssueAnswersNow();
        if (!IssueTexts.IsComplete(choice, title, answers)) return;
        var body = IssueTexts.Body(choice, answers);

        _ = Shell.RunAsync(async ct =>
        {
            // GitHub's form skips a label the repository doesn't have, where gh issue create would fail, so only
            // the labels the repository has are passed. If the labels can't be read, gh is left to say.
            var labels = choice.Labels.ToList();
            if (labels.Count > 0)
            {
                try
                {
                    var have = (await Workspace.GitHub.LabelsAsync(Repo.OwnerAndName, ct)).Select(l => l.Name).ToList();
                    var (use, missing) = IssueTexts.SplitLabels(choice, have);
                    foreach (var label in missing) Workspace.Runner.Note(string.Format(Strings.NewIssue_LabelMissing, label));
                    labels = use;
                }
                catch (Services.CommandFailedException) { }
            }
            var (number, url) = await Workspace.GitHub.CreateIssueAsync(Repo.OwnerAndName, title, body, labels, ct);
            if (Workspace.Board is { } board && number > 0)
            {
                try { await Workspace.GitHub.AddToBoardAsync(board, url, ct); }
                catch (Services.CommandFailedException ex) { Workspace.Runner.Note(string.Format(Strings.NewIssue_BoardFailed, number, ex.Message)); }
            }
            IssueTitleBox.Text = choice.TitlePrefix;
            BuildIssueFields(keep: false);
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
