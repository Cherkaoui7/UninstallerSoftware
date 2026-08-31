using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace Uninstaller.App.Views;

public partial class RecoverySessionHistoryView : UserControl
{
    public RecoverySessionHistoryView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            Log.Information("[Navigation] RecoverySessionHistoryView Loaded event. DataContext={DataContextType} (#{Hash})", 
                DataContext?.GetType().FullName, DataContext?.GetHashCode());
        };
        DataContextChanged += (s, e) =>
        {
            Log.Information("[Navigation] RecoverySessionHistoryView DataContext assigned to {DataContextType} (#{Hash})", 
                e.NewValue?.GetType().FullName, e.NewValue?.GetHashCode());
        };
    }
}
