using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Helpers;
using DesignatedDriver.Resources;
using DesignatedDriver.Services;
using DesignatedDriver.Windows;

namespace DesignatedDriver.Screens;

/// <summary>
/// Check (section 5 of the design): what has drifted from the convention in the current repository, grouped by
/// set-up step, with a tick box per item and Apply. Files a project may tailor are shown unticked. The apply
/// runs the same steps the set-up does, limited to what was ticked, through an issue and a pull request.
/// </summary>
public partial class CheckScreen : ScreenBase
{
    private CheckResult? _result;
    private DateTime _when;
    private readonly Dictionary<string, CheckBox> _ticks = [];
    private readonly SetupService _setup;
    private SetupPlan? _plan;
    private readonly Dictionary<PlanStep, (TextBlock Mark, TextBlock Message)> _rows = [];
    private bool _running;

    public CheckScreen(IShell shell) : base(shell)
    {
        InitializeComponent();
        _setup = new SetupService(Workspace);
        _setup.StepChanged += (_, step) => Dispatcher.BeginInvoke(() => ShowStep(step));
    }

    /// <summary>A Check costs a dozen gh calls; it runs when the screen is opened or refreshed, not on every return to the window.</summary>
    public override bool ReloadsOnReturn => false;

    protected override async Task LoadAsync(CancellationToken ct)
    {
        if (_running) return;
        await CheckAsync(ct);
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _ = Shell.RunAsync(CheckAsync);
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        if (!Repo.HasFolder)
        {
            ShowNotice(Strings.Check_NoClone);
            return;
        }
        var result = await new CheckService(Workspace).CheckAsync(Repo, ct);
        _result = result;
        _when = DateTime.Now;
        if (!result.IsSetUp)
        {
            ShowNotice(Strings.Check_NotSetUp);
            return;
        }
        NoticeCard.Visibility = Visibility.Collapsed;
        ShowReport(result.Report!);
        Shell.Announce(result.Report!.Summary);
    }

    private void ShowNotice(string text)
    {
        NoticeText.Text = text;
        NoticeCard.Visibility = Visibility.Visible;
        ResultCard.Visibility = Visibility.Collapsed;
        ProgressCard.Visibility = Visibility.Collapsed;
    }

    private void ShowReport(DriftReport report)
    {
        ResultCard.Visibility = Visibility.Visible;
        CheckedText.Text = string.Format(Strings.Check_CheckedAt, Repo.OwnerAndName, report.Manifest.ConventionVersion, report.Manifest.SetUp, _when.ToString("g"));

        SummaryNotice.Style = (Style)FindResource(report.IsClean ? "SuccessNoticeStyle" : "NoticeStyle");
        SummaryTitle.Text = report.Summary;
        SummaryText.Text = report.IsClean ? Strings.Check_CleanText
            : report.ManifestOlder ? string.Format(Strings.Check_UpdateAvailable, Manifest.CurrentConventionVersion, report.Manifest.ConventionVersion)
            : Strings.Check_DriftText;

        ItemsPanel.Children.Clear();
        _ticks.Clear();
        foreach (var group in report.Items.GroupBy(i => i.Step).OrderBy(g => g.Key))
        {
            var heading = new TextBlock { Text = ScanReportText.StepTitle(group.Key), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };
            ItemsPanel.Children.Add(heading);
            foreach (var item in group) ItemsPanel.Children.Add(ItemRow(item));
        }
        bool anyActionable = report.Count > 0 || report.ManifestOlder;
        ApplyPanel.Visibility = anyActionable ? Visibility.Visible : Visibility.Collapsed;
        ApplyNote.Visibility = anyActionable ? Visibility.Visible : Visibility.Collapsed;
        UpdateApplyButton();
    }

    private FrameworkElement ItemRow(DriftItem item)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new StackPanel();
        if (item.IsActionable)
        {
            var box = new CheckBox { IsChecked = item.TickedByDefault, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
            AutomationProperties.SetName(box, item.Text);
            box.Checked += (_, _) => UpdateApplyButton();
            box.Unchecked += (_, _) => UpdateApplyButton();
            _ticks[item.Key] = box;
            row.Children.Add(box);
        }
        else
        {
            var mark = UiHelpers.Mark(true);
            mark.Text = "–";
            mark.Foreground = UiHelpers.Brush("TextTertiaryBrush");
            mark.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(mark);
        }

        var line = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap };
        if (!item.IsActionable) line.Foreground = UiHelpers.Brush("TextSecondaryBrush");
        text.Children.Add(line);
        if (item.IsFile && item.Kind == DriftKind.Different)
        {
            var show = UiHelpers.LinkButton(Strings.Check_ShowDiff, () => Shell.ShowDialog(new TextWindow(Strings.Check_DiffTitle, string.Format(Strings.Check_DiffHeading, item.Path), item.Detail)), string.Format(Strings.Check_ShowDiffFor, item.Path));
            show.HorizontalAlignment = HorizontalAlignment.Left;
            show.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(show);
            if (!item.TickedByDefault) text.Children.Add(new TextBlock { Text = Strings.Check_TailoredNote, Style = (Style)FindResource("HelperText") });
        }
        else if (item.Detail.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = item.Detail, Style = (Style)FindResource("MonoText"), Foreground = UiHelpers.Brush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AutomationProperties.SetName(row, item.Text);
        return row;
    }

    private IEnumerable<string> SelectedKeys => _ticks.Where(t => t.Value.IsChecked == true).Select(t => t.Key);

    private void UpdateApplyButton()
    {
        if (ApplyButton is null) return;
        ApplyButton.IsEnabled = !_running && (SelectedKeys.Any() || _result?.Report?.ManifestOlder == true);
    }

    private void TickAll_Click(object sender, RoutedEventArgs e) { foreach (var t in _ticks.Values) t.IsChecked = true; }
    private void UntickAll_Click(object sender, RoutedEventArgs e) { foreach (var t in _ticks.Values) t.IsChecked = false; }

    // Apply

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _result is not { Report: { } report, Answers: { } answers }) return;
        var selected = SelectedKeys.ToList();
        _plan = _setup.BuildUpdate(answers, _result.Facts, report, selected);
        if (_plan.Steps.Count == 0) return;
        ShowPlan(_plan);
        _running = true;
        UpdateApplyButton();
        Shell.Announce(Strings.Check_Applying);
        _ = Shell.RunAsync(async ct =>
        {
            bool ok;
            try { ok = await _setup.RunAsync(_plan, ct); }
            finally { _running = false; }
            ShowResult(ok);
            if (ok) await CheckAsync(ct);
        });
    }

    private void ShowPlan(SetupPlan plan)
    {
        ProgressCard.Visibility = Visibility.Visible;
        ResultNotice.Visibility = Visibility.Collapsed;
        PlanPanel.Children.Clear();
        _rows.Clear();
        foreach (var step in plan.Steps)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = UiHelpers.Mark(true);
            mark.Text = "·";
            mark.Foreground = UiHelpers.Brush("TextTertiaryBrush");
            mark.VerticalAlignment = VerticalAlignment.Top;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = step.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("HelperText") });
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
            text.Children.Add(message);
            Grid.SetColumn(text, 1);
            row.Children.Add(mark);
            row.Children.Add(text);
            AutomationProperties.SetName(row, step.Title);
            PlanPanel.Children.Add(row);
            _rows[step] = (mark, message);
        }
    }

    private void ShowStep(PlanStep step)
    {
        if (!_rows.TryGetValue(step, out var row)) return;
        var (mark, message) = row;
        switch (step.State)
        {
            case StepState.Checking:
            case StepState.Running: mark.Text = "…"; mark.Foreground = UiHelpers.Brush("PrimaryStrongBrush"); break;
            case StepState.AlreadyDone:
            case StepState.Applied: mark.Text = "✓"; mark.Foreground = UiHelpers.Brush("SuccessBrush"); break;
            case StepState.Failed: mark.Text = "✗"; mark.Foreground = UiHelpers.Brush("DangerBrush"); break;
            default: mark.Text = "·"; mark.Foreground = UiHelpers.Brush("TextTertiaryBrush"); break;
        }
        var note = step.State switch
        {
            StepState.AlreadyDone => step.Message.Length > 0 ? step.Message : Strings.Setup_StepAlreadyDone,
            StepState.Applied => step.Message.Length > 0 ? step.Message : Strings.Setup_StepApplied,
            StepState.Failed => step.Message,
            StepState.Running => Strings.Setup_StepRunning,
            StepState.Checking => Strings.Setup_StepChecking,
            _ => "",
        };
        message.Text = note;
        message.Foreground = UiHelpers.Brush(step.State == StepState.Failed ? "DangerBrush" : "TextSecondaryBrush");
        message.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowResult(bool ok)
    {
        ResultLinks.Children.Clear();
        ResultNotice.Style = (Style)FindResource(ok ? "SuccessNoticeStyle" : "DangerNoticeStyle");
        if (ok)
        {
            ResultTitle.Text = Strings.Check_AppliedTitle;
            ResultText.Text = _setup.PullRequestUrl.Length > 0 ? Strings.Check_AppliedPullRequest : Strings.Check_Applied;
            if (_setup.PullRequestUrl.Length > 0)
            {
                var link = UiHelpers.LinkButton(Strings.Setup_OpenPullRequest, () => Shell.OpenUrl(_setup.PullRequestUrl));
                link.Margin = new Thickness(0, 0, 16, 0);
                ResultLinks.Children.Add(link);
            }
        }
        else
        {
            var failed = _plan?.FailedStep;
            ResultTitle.Text = Strings.Setup_FailedTitle;
            ResultText.Text = failed is null ? Strings.Setup_FailedUnknown : string.Format(Strings.Setup_Failed, failed.Title, failed.Message);
        }
        ResultNotice.Visibility = Visibility.Visible;
        UpdateApplyButton();
        Shell.Announce(ResultTitle.Text);
    }
}
