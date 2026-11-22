using System.Windows;
using DesignatedDriver.Helpers;

namespace DesignatedDriver.Windows;

/// <summary>Shows a block of text, such as a diff, in a monospaced box.</summary>
public partial class TextWindow : Window
{
    public TextWindow(string title, string heading, string body)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        BodyText.Text = body;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => UiHelpers.CopyToClipboard(BodyText.Text);
}
