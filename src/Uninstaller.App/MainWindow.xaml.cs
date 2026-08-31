using System.Windows;
using Serilog;

namespace Uninstaller.App;

public partial class MainWindow : Window
{
    public MainWindow(ViewModels.MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        MainContentControl.DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] MainContentControl DataContext changed to {DataContextType} (#{Hash})",
                e.NewValue?.GetType().FullName, e.NewValue?.GetHashCode());
        };
    }
}
