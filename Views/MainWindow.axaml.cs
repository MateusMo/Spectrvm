using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Spectrvm.Controls;
using Spectrvm.Models;
using Spectrvm.ViewModels;

namespace Spectrvm.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        // Reconecta o callback sempre que o layout muda (troca de aba, novo conteúdo gerado)
        LayoutUpdated += (_, _) => WireGraphCanvas();
    }

    private async void OnNavigate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.NavigateAsync();
    }

    private async void OnUrlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && DataContext is MainWindowViewModel vm)
            await vm.NavigateAsync();
    }

    private void OnAddTab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.AddTab();
    }

    private void OnCloseTab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Button { Tag: BrowserTab tab })
            vm.CloseTab(tab);
    }

    private void OnSortLinks(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SortLinks();
    }

    private GraphCanvas? _wiredCanvas;

    private void WireGraphCanvas()
    {
        var canvas = this.FindDescendantOfType<GraphCanvas>();
        if (canvas == null || ReferenceEquals(canvas, _wiredCanvas)) return;

        _wiredCanvas = canvas;

        // O callback agora recebe (url, node) — o nó é usado como pai no grafo
        canvas.OnNodeClicked = async (url, node) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.NavigateToUrl(url, node);
        };
    }
}