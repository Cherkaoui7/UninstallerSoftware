using System.Windows.Controls;
using Uninstaller.App.ViewModels;
using System.Windows;

namespace Uninstaller.App.Views;

public partial class RecoverySessionView : UserControl
{
    public RecoverySessionView()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoverySessionViewModel vm)
        {
            await vm.StartExecutionAsync();
        }
    }
}
