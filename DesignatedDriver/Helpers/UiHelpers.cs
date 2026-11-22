using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DesignatedDriver.Models;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Helpers;

/// <summary>Small pieces of UI built in code: label chips, state tags, link buttons.</summary>
public static class UiHelpers
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>A rounded chip in the label's GitHub colour, lightened so dark text stays readable.</summary>
    public static Border LabelChip(GhLabel label)
    {
        var chip = Tag(label.Name);
        if (label.Color.Length == 6 && TryParseHex(label.Color, out var r, out var g, out var b))
        {
            chip.Background = new SolidColorBrush(Color.FromArgb(0x38, r, g, b));
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, r, g, b));
            chip.BorderThickness = new Thickness(1);
        }
        if (!string.IsNullOrEmpty(label.Description)) chip.ToolTip = label.Description;
        return chip;
    }

    public static Border Tag(string text, string? backgroundKey = null, string? foregroundKey = null)
    {
        var tb = new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("TagText") };
        if (foregroundKey is not null) tb.Foreground = Brush(foregroundKey);
        var border = new Border { Child = tb, Style = (Style)Application.Current.FindResource("TagBorder") };
        if (backgroundKey is not null) border.Background = Brush(backgroundKey);
        return border;
    }

    /// <summary>pass, fail, pending or none as a coloured tag.</summary>
    public static Border ChecksTag(string bucket) => bucket switch
    {
        "pass" => Tag(Strings.Checks_Pass, "SuccessSoftBrush", "SuccessBrush"),
        "fail" => Tag(Strings.Checks_Fail, "DangerSoftBrush", "DangerBrush"),
        "pending" => Tag(Strings.Checks_Pending, "QuaternarySofterBrush", "NeutralMutedBrush"),
        _ => Tag(Strings.Checks_None),
    };

    public static Border ReviewTag(string reviewDecision) => reviewDecision.ToUpperInvariant() switch
    {
        "APPROVED" => Tag(Strings.Review_Approved, "SuccessSoftBrush", "SuccessBrush"),
        "CHANGES_REQUESTED" => Tag(Strings.Review_ChangesRequested, "DangerSoftBrush", "DangerBrush"),
        "REVIEW_REQUIRED" => Tag(Strings.Review_Required, "InfoSoftBrush", "PrimaryStrongBrush"),
        _ => Tag(Strings.Review_NoReview),
    };

    public static Button LinkButton(string text, Action onClick, string? automationName = null)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, automationName ?? text);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static TextBlock Text(string text, string? styleKey = null)
    {
        var tb = new TextBlock { Text = text };
        if (styleKey is not null) tb.Style = (Style)Application.Current.FindResource(styleKey);
        return tb;
    }

    /// <summary>A tick or cross in front of a prerequisite line.</summary>
    public static TextBlock Mark(bool ok)
    {
        return new TextBlock
        {
            Text = ok ? "✓" : "✗",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(ok ? "SuccessBrush" : "DangerBrush"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 18,
            TextAlignment = TextAlignment.Center,
        };
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* another application holds the clipboard */ }
    }

    private static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}
