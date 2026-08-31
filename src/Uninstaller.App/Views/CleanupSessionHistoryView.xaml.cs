using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace Uninstaller.App.Views;

public partial class CleanupSessionHistoryView : UserControl
{
    public CleanupSessionHistoryView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Log.Information("[Navigation] CleanupSessionHistoryView Loaded event. DataContext={DataContextType} (#{Hash})", 
                DataContext?.GetType().FullName, DataContext?.GetHashCode());
        };
        DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] CleanupSessionHistoryView DataContext assigned to {DataContextType} (#{Hash})", 
                e.NewValue?.GetType().FullName, e.NewValue?.GetHashCode());
        };
    }
}
