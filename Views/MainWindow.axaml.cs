using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ReactiveUI;
using Spectrvm.Controls;
using Spectrvm.Models;
using Spectrvm.ViewModels;

namespace Spectrvm.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        LayoutUpdated += (_, _) => WireGraphCanvas();
    }

    private async void OnNavigate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.NavigateAsync();
    }

    private async void OnUrlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && DataContext is MainWindowViewModel vm)
            await vm.NavigateAsync();
    }

    private void OnAddTab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.AddTab();
    }

    private void OnCloseTab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Button { Tag: BrowserTab tab })
            vm.CloseTab(tab);
    }

    private void OnSortLinks(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.SortLinks();
    }

    private GraphCanvas? _wiredCanvas;
    private IDisposable? _tabSub;
    private IDisposable? _nodesSub;

    private void WireGraphCanvas()
    {
        var canvas = this.FindDescendantOfType<GraphCanvas>();
        if (canvas == null || ReferenceEquals(canvas, _wiredCanvas)) return;
        _wiredCanvas = canvas;

        // Sincroniza canvas.Edges quando a aba selecionada ou seus nós mudam
        if (DataContext is MainWindowViewModel vm)
        {
            _tabSub?.Dispose();
            _tabSub = vm.WhenAnyValue(x => x.SelectedTab).Subscribe(tab =>
            {
                _nodesSub?.Dispose();
                if (tab == null) return;

                canvas.Edges = tab.GraphEdges;

                _nodesSub = tab.WhenAnyValue(t => t.GraphNodes).Subscribe(_ =>
                {
                    canvas.Edges = tab.GraphEdges;
                    canvas.InvalidateVisual();
                });
            });
        }

        // Clique em nó orbital → linha de navegação do orbital para o novo primário
        canvas.OnNodeClicked = async (url, node) =>
        {
            if (DataContext is MainWindowViewModel vm2)
                await vm2.NavigateToUrl(url, node);
        };

        // Clique em ⊕ → expansão atômica (todos os orbitais em paralelo)
        canvas.OnAtomicExpand = async (primaryNode) =>
        {
            if (DataContext is not MainWindowViewModel vm2) return;
            canvas.MarkExpanding(primaryNode);
            try   { await vm2.AtomicExpandAsync(primaryNode, canvas); }
            finally { canvas.UnmarkExpanding(primaryNode); }
        };
    }
}