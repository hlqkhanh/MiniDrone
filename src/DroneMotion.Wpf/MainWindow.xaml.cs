using System.Windows;
using System.Windows.Media;
using DroneMotion.Wpf.ViewModels;

namespace DroneMotion.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        CompositionTarget.Rendering += CompositionTarget_OnRendering;
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e) =>
        _viewModel.OnRenderFrame();

    private async void Window_OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= CompositionTarget_OnRendering;
        await _viewModel.DisposeAsync();
    }
}
