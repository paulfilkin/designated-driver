using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesignatedDriver.Core;
using DesignatedDriver.Resources;

namespace DesignatedDriver.Windows;

/// <summary>
/// Review's confirmation before it acts on a pull request: the repository's owner and name across the top in large
/// type, what will happen, and a button that names the repository. For a merge the name is typed first, as GitHub
/// asks before deleting a repository, unless the repository's settings turn that off. Accepted only by the button.
/// </summary>
public partial class ConfirmActionWindow : Window
{
    private readonly string _ownerAndName;
    private readonly bool _typeName;

    /// <param name="note">A line under the repository's name, such as who owns it; empty for none.</param>
    public ConfirmActionWindow(ReviewAction action, string ownerAndName, string actionText, string note, bool typeName)
    {
        InitializeComponent();
        _ownerAndName = ownerAndName;
        _typeName = typeName;

        Title = ReviewConfirm.Title(action);
        RepositoryText.Text = ownerAndName;
        RepositoryNote.Text = note;
        RepositoryNote.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionText.Text = actionText;

        var label = ReviewConfirm.ButtonLabel(action, ownerAndName);
        ConfirmButton.Content = label;
        AutomationProperties.SetName(ConfirmButton, label);
        AutomationProperties.SetHelpText(ConfirmButton, actionText);

        TypePanel.Visibility = typeName ? Visibility.Visible : Visibility.Collapsed;
        TypeLabel.Text = string.Format(Strings.Confirm_TypeLabel, ownerAndName);
        AutomationProperties.SetName(TypeBox, TypeLabel.Text);
        ConfirmButton.IsEnabled = !typeName;

        // The typing box when there is one, so the name is typed at once; otherwise Cancel, so Enter doesn't confirm.
        Loaded += (_, _) => (typeName ? TypeBox : (Control)CancelButton).Focus();
    }

    private void TypeBox_TextChanged(object sender, TextChangedEventArgs e)
        => ConfirmButton.IsEnabled = !_typeName || ReviewConfirm.Matches(TypeBox.Text, _ownerAndName);

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
