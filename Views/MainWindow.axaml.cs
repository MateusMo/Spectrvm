using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using WebViewControl;
using ReactiveUI;
using Spectrvm.Controls;
using Spectrvm.Models;
using Spectrvm.ViewModels;

namespace Spectrvm.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    private Panel? _webViewHost;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel { Window = this };
        DataContext = vm;

        this.Loaded += (_, _) =>
        {
            WireWebViewHost();
            vm.InitFirstTab();
            WireGraphCanvas();
        };

        LayoutUpdated += (_, _) => WireGraphCanvas();
    }

    // ── WebView host ──────────────────────────────────────────────────────────

    private void WireWebViewHost()
    {
        if (_webViewHost != null) return;
        _webViewHost = this.FindControl<Panel>("WebViewHost");
    }

    public void SwitchWebView(BrowserTab? tab)
    {
        if (_webViewHost == null) WireWebViewHost();
        if (_webViewHost == null) return;

        if (tab?.WebViewInstance != null &&
            !_webViewHost.Children.Contains(tab.WebViewInstance))
            _webViewHost.Children.Add(tab.WebViewInstance);

        foreach (var child in _webViewHost.Children)
        {
            if (child is WebView wv)
                wv.IsVisible = tab?.WebViewInstance == wv;
        }
    }

    public void RemoveWebView(BrowserTab tab)
    {
        if (tab.WebViewInstance == null || _webViewHost == null) return;
        _webViewHost.Children.Remove(tab.WebViewInstance);
        tab.WebViewInstance = null;
    }

    // ── Barra de endereço ─────────────────────────────────────────────────────

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

    // ── Voltar / Reload ───────────────────────────────────────────────────────

    private void OnGoBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.GoBack();
    }

    private async void OnReload(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.ReloadAsync();
    }

    // ── Toggle HTML cru ───────────────────────────────────────────────────────

    private void OnToggleRawHtml(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.ToggleRawHtml();
    }

    // ── GraphCanvas ───────────────────────────────────────────────────────────

    private GraphCanvas? _wiredCanvas;
    private IDisposable? _tabSub;
    private IDisposable? _nodesSub;

    private void WireGraphCanvas()
    {
        var canvas = this.FindDescendantOfType<GraphCanvas>();
        if (canvas == null || ReferenceEquals(canvas, _wiredCanvas)) return;
        _wiredCanvas = canvas;

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

        canvas.OnNodeClicked = async (url, node) =>
        {
            if (DataContext is MainWindowViewModel vm2)
                await vm2.NavigateToUrl(url, node);
        };

        canvas.OnAtomicExpand = async (primaryNode) =>
        {
            if (DataContext is not MainWindowViewModel vm2) return;
            canvas.MarkExpanding(primaryNode);
            try   { await vm2.AtomicExpandAsync(primaryNode, canvas); }
            finally { canvas.UnmarkExpanding(primaryNode); }
        };
    }
}