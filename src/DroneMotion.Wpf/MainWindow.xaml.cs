using System.Windows;
using System.Windows.Media;
using System.IO;
using DroneMotion.Wpf.Services;
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
        Loaded += Window_OnLoaded;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "drone.glb");
            var model = await Task.Run(() => DroneModelLoader.Load(path));
            if (!IsLoaded) return;
            DroneModel.Content = model;
            ModelLoadStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            ModelLoadStatus.Text = $"Could not load drone model: {exception.Message}";
        }
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e) =>
        _viewModel.OnRenderFrame();

    private async void Window_OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= CompositionTarget_OnRendering;
        await _viewModel.DisposeAsync();
    }
}
