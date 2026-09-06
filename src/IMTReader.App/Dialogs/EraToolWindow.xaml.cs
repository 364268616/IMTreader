using System.Windows;
using System.Windows.Input;
using IMTReader.Core.Text;

namespace IMTReader.App.Dialogs;

public partial class EraToolWindow : Window
{
    public EraToolWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
    }

    public void Convert(string input)
    {
        InputBox.Text = input;
        OutputBox.Text = EraCalendar.Describe(input);
    }

    private void Convert_Click(object sender, RoutedEventArgs e) => OutputBox.Text = EraCalendar.Describe(InputBox.Text);

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OutputBox.Text = EraCalendar.Describe(InputBox.Text);
    }
}
