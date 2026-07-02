using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ISGPrinter.App.ViewModels;

namespace ISGPrinter.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += MainWindowLoaded;
        StateChanged += OnWindowStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryApplyMicaBackdrop();
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        await viewModel.InitializeCommand.ExecuteAsync(null);
    }

    // Win11 22H2+ system backdrop. On older Windows the call fails and
    // the window falls back to the solid token background.
    private void TryApplyMicaBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int backdropType = DwmSystemBackdropTypeMica;
        int result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, sizeof(int));

        if (result != 0)
        {
            Background = (Brush)FindResource("WindowBackground");
        }
    }

    // WindowChrome lets a maximized window overhang the screen edges by
    // the resize border; pad the root grid to compensate.
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmSystemBackdropTypeMica = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
