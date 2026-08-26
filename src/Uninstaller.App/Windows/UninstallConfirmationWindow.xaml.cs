using System.Windows;
using Uninstaller.App.ViewModels;

namespace Uninstaller.App.Windows;

public partial class UninstallConfirmationWindow : Window
{
    public UninstallConfirmationWindow(UninstallConfirmationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
