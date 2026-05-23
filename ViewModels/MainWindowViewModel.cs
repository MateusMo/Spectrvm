using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using ReactiveUI;
using Spectrvm.Controls;
using Spectrvm.Models;
using Spectrvm.Services;
using Spectrvm.Views;
using WebViewControl;

namespace Spectrvm.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly BrowserService         _browser = new();
    private readonly NavigationGraphService _graph   = new();

    public MainWindow? Window { get; set; }

    public ObservableCollection<BrowserTab> Tabs { get; } = new();

    private BrowserTab? _selectedTab;
    public BrowserTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTab, value);
            // Quando a aba muda, mostramos o WebView correto
            Window?.SwitchWebView(value);
        }
    }

    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    public MainWindowViewModel() { } 

    // ── Abas ──────────────────────────────────────────────────────────────────
    public void InitFirstTab() => AddTab();
    public void AddTab()
    {
        var tab = new BrowserTab { Title = "New Tab" };
        var wv = new WebView
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch
        };

        // AddressChanged é AvaloniaProperty — assina via ObserveOn
        wv.GetObservable(WebView.AddressProperty).Subscribe(url =>
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Dispatcher.UIThread.Post(() =>
            {
                tab.CurrentUrl = url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    tab.Title = uri.Host.Length > 0 ? uri.Host : tab.Title;

                if (tab.BackStack.Count == 0 || tab.BackStack.Peek() != url)
                    tab.BackStack.Push(url);
                tab.CanGoBack = tab.BackStack.Count > 1;
            });
        });

        // Opcional: sync durante navegação (antes de carregar)
        wv.Navigated += (url, frameName) =>
        {
            if (!string.IsNullOrWhiteSpace(frameName)) return; // ignora iframes
            Dispatcher.UIThread.Post(() =>
            {
                tab.CurrentUrl = url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    tab.Title = uri.Host.Length > 0 ? uri.Host : tab.Title;
            });
        };

        tab.WebViewInstance = wv;
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public void CloseTab(BrowserTab tab)
    {
        var idx = Tabs.IndexOf(tab);
        Window?.RemoveWebView(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0) { AddTab(); return; }
        SelectedTab = Tabs[Math.Max(0, idx - 1)];
    }

    // ── Navegação pela barra de endereço ──────────────────────────────────────

    public async Task NavigateAsync()
    {
        if (SelectedTab is not { } tab) return;
        if (string.IsNullOrWhiteSpace(tab.CurrentUrl)) return;
        await DoNavigate(tab, tab.CurrentUrl, parentNode: null);
    }

    // ── Voltar ────────────────────────────────────────────────────────────────

    public void GoBack()
    {
        if (SelectedTab is not { } tab) return;
        if (tab.WebViewInstance?.CanGoBack == true)
            tab.WebViewInstance.GoBack();
        else if (tab.BackStack.Count > 1)
        {
            tab.BackStack.Pop();
            var prevUrl = tab.BackStack.Peek();
            tab.CanGoBack = tab.BackStack.Count > 1;
            tab.CurrentUrl = prevUrl;
            _ = DoNavigate(tab, prevUrl, parentNode: null, addToHistory: false);
        }
    }

    // ── Atualizar ─────────────────────────────────────────────────────────────

    public async Task ReloadAsync()
    {
        if (SelectedTab is not { } tab) return;
        if (string.IsNullOrWhiteSpace(tab.CurrentUrl)) return;

        if (tab.ViewMode == ViewMode.Interpreter && tab.WebViewInstance != null)
        {
            // WebView tem método Reload nativo
            tab.WebViewInstance.Reload();
        }
        else
        {
            await DoNavigate(tab, tab.CurrentUrl, parentNode: null, addToHistory: false);
        }
    }

    // ── Navegação por clique em nó orbital ────────────────────────────────────

    public async Task NavigateToUrl(string url, NavigationNode? fromNode = null)
    {
        if (SelectedTab is not { } tab) return;
        tab.CurrentUrl = url;
        await DoNavigate(tab, url, parentNode: fromNode);
    }

    // ── Expansão atômica ──────────────────────────────────────────────────────

    public async Task AtomicExpandAsync(NavigationNode primaryNode, GraphCanvas canvas)
    {
        if (SelectedTab is not { } tab) return;

        var orbits = primaryNode.OrbitNodes
            .Where(o => o.Kind != NodeKind.Subdomain)
            .ToList();
        if (orbits.Count == 0) return;

        var fetches = orbits.Select(async orbit =>
        {
            try
            {
                var result = await _browser.NavigateAsync(orbit.Url);
                return (orbit, result, ok: true);
            }
            catch { return (orbit, result: (BrowserResult?)null, ok: false); }
        });

        var results = await Task.WhenAll(fetches);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BrowserResult? lastResult = null;
            foreach (var (orbit, result, ok) in results)
            {
                if (!ok || result == null) continue;
                lastResult = result;
                tab.GraphNodes = _graph.AppendNavigation(
                    tab.GraphNodes, tab.GraphEdges,
                    orbit.Url, result.Links, result.Subdomains,
                    parentNode: orbit);
            }
            if (lastResult != null) tab.Result = lastResult;
            canvas.InvalidateVisual();
        });
    }

    public void SortLinks()
    {
        if (SelectedTab?.Result is not { } result) return;
        var sorted = result.Links.OrderBy(l => l.Type).ThenBy(l => l.Url).ToList();
        SelectedTab.Result = new BrowserResult
        {
            Html            = result.Html,
            CurlCommand     = result.CurlCommand,
            RequestInfo     = result.RequestInfo,
            Links           = sorted,
            Subdomains      = result.Subdomains,
            SecurityHeaders = result.SecurityHeaders,
            Technologies    = result.Technologies,
            StatusCode      = result.StatusCode,
            ContentType     = result.ContentType,
            Timestamp       = result.Timestamp
        };
    }

    // ── Toggle HTML cru ───────────────────────────────────────────────────────

    public void ToggleRawHtml()
    {
        if (SelectedTab is not { } tab) return;
        tab.ShowRawHtml = !tab.ShowRawHtml;
    }

    // ── Núcleo de navegação ───────────────────────────────────────────────────

    private async Task DoNavigate(BrowserTab tab, string url, NavigationNode? parentNode,
        bool addToHistory = true)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        tab.IsLoading = true;
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                tab.Title = uri.Host.Length > 0 ? uri.Host : "…";

            if (addToHistory)
            {
                if (tab.History.Count == 0 || tab.History[^1] != url)
                    tab.History.Add(url);
            }

            if (tab.ViewMode == ViewMode.Interpreter)
            {
                // Navega no WebView dedicado desta aba
                if (tab.WebViewInstance != null)
                    tab.WebViewInstance.Address = url;

                // Análise em background (para Navigator, Links, etc.)
                tab.IsAnalyzing = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _browser.AnalyzeOnlyAsync(url);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            tab.Result     = result;
                            tab.GraphNodes = _graph.AppendNavigation(
                                tab.GraphNodes, tab.GraphEdges,
                                url, result.Links, result.Subdomains, parentNode);
                        });
                    }
                    finally
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => tab.IsAnalyzing = false);
                    }
                });
            }
            else
            {
                var result = await _browser.NavigateAsync(url);
                tab.Result     = result;
                tab.GraphNodes = _graph.AppendNavigation(
                    tab.GraphNodes, tab.GraphEdges,
                    url, result.Links, result.Subdomains, parentNode);
            }
        }
        catch (Exception ex)
        {
            tab.Result = new BrowserResult
            {
                Html        = $"<!-- Erro: {ex.Message} -->",
                CurlCommand = $"curl \"{url}\"",
                RequestInfo = $"Erro: {ex.Message}"
            };
        }
        finally
        {
            tab.IsLoading = false;
        }
    }
}