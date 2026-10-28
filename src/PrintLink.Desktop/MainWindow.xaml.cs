using System.Windows;
using PrintLink.Desktop.SystemTray;

namespace PrintLink.Desktop;

public partial class MainWindow : Window
{
    private SystemTrayController? _trayController;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _trayController = new SystemTrayController(this);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _trayController?.Dispose();
        if (DataContext is ViewModels.MainViewModel vm)
        {
            await vm.DisposeStandaloneServicesAsync();
        }
    }
}