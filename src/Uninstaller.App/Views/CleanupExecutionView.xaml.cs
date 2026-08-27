using System.Windows;
using System.Windows.Controls;
using Uninstaller.App.ViewModels;

namespace Uninstaller.App.Views;

public partial class CleanupExecutionView : UserControl
{
    public CleanupExecutionView()
    {
        InitializeComponent();
        Loaded += CleanupExecutionView_Loaded;
    }

    private async void CleanupExecutionView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CleanupExecutionViewModel vm)
        {
            await vm.StartExecutionAsync();
        }
    }
}
