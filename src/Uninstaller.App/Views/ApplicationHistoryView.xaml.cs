using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace Uninstaller.App.Views;

public partial class ApplicationHistoryView : UserControl
{
    public ApplicationHistoryView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Log.Information("[Navigation] ApplicationHistoryView Loaded event. DataContext={DataContextType} (#{Hash})", 
                DataContext?.GetType().FullName, DataContext?.GetHashCode());
        };
        DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] ApplicationHistoryView DataContext assigned to {DataContextType} (#{Hash})", 
                e.NewValue?.GetType().FullName, e.NewValue?.GetHashCode());
        };
    }
}
