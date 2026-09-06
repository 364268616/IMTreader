using System.Windows;
using System.Windows.Input;

namespace IMTReader.App.Dialogs;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    public static string? Show(Window owner, string title, string prompt, string defaultValue = "")
    {
        var dlg = new InputDialog { Owner = owner, Title = title };
        dlg.PromptText.Text = prompt;
        dlg.ValueBox.Text = defaultValue;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }
}
