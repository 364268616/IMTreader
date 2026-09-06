using System.Windows.Controls;
using System.Windows.Input;
using IMTReader.App.ViewModels;

namespace IMTReader.App.Views;

public partial class HistoryView : UserControl, IActivatableView
{
    private readonly LibraryViewModel _vm;
    private readonly MainWindow _owner;

    public HistoryView(LibraryViewModel vm, MainWindow owner)
    {
        InitializeComponent();
        _vm = vm;
        _owner = owner;
        DataContext = vm;
    }

    public void OnActivated() => _vm.Reload();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is DocumentItemViewModel item && item.Document.SourceExists())
            _owner.OpenDocument(item.Document, item.Document.LastPage);
    }
}
