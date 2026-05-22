using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Spectrvm.Controls;
using Spectrvm.Models;
using Spectrvm.Services;

namespace Spectrvm.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly BrowserService         _browser = new();
    private readonly NavigationGraphService _graph   = new();

    public ObservableCollection<BrowserTab> Tabs { get; } = new();

    private BrowserTab? _selectedTab;
    public BrowserTab? SelectedTab
    {
        get => _selectedTab;
        set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    public ViewMode[] ViewModes { get; } = Enum.GetValues<ViewMode>();

    public MainWindowViewModel() => AddTab();

    public void AddTab()
    {
        var tab = new BrowserTab { Title = "New Tab" };
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public void CloseTab(BrowserTab tab)
    {
        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0) { AddTab(); return; }
        SelectedTab = Tabs[Math.Max(0, idx - 1)];
    }

    public async Task NavigateAsync()
    {
        if (SelectedTab is not { } tab) return;
        if (string.IsNullOrWhiteSpace(tab.CurrentUrl)) return;
        await DoNavigate(tab, tab.CurrentUrl, parentNode: null);
    }

    /// <summary>
    /// Chamado ao clicar num nó orbital.
    /// Sempre atualiza tab.Result para o InfoPanel refletir o novo site.
    /// </summary>
    public async Task NavigateToUrl(string url, NavigationNode? fromNode = null)
    {
        if (SelectedTab is not { } tab) return;
        tab.CurrentUrl = url;
        await DoNavigate(tab, url, parentNode: fromNode);
    }

    /// <summary>
    /// Expansão atômica: navega em paralelo para todos os orbitais do nó primário.
    /// Atualiza tab.Result com o resultado do último orbital que completou com sucesso.
    /// </summary>
    public async Task AtomicExpandAsync(NavigationNode primaryNode, GraphCanvas canvas)
    {
        if (SelectedTab is not { } tab) return;

        var orbits = primaryNode.OrbitNodes
            .Where(o => o.Kind != NodeKind.Subdomain) // subdomínios não expandem automaticamente
            .ToList();
        if (orbits.Count == 0) return;

        var fetches = orbits.Select(async orbit =>
        {
            try
            {
                var result = await _browser.NavigateAsync(orbit.Url);
                return (orbit, result, ok: true);
            }
            catch
            {
                return (orbit, result: (BrowserResult?)null, ok: false);
            }
        });

        var results = await Task.WhenAll(fetches);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            BrowserResult? lastResult = null;

            foreach (var (orbit, result, ok) in results)
            {
                if (!ok || result == null) continue;
                lastResult = result;

                tab.GraphNodes = _graph.AppendNavigation(
                    tab.GraphNodes,
                    tab.GraphEdges,
                    orbit.Url,
                    result.Links,
                    result.Subdomains,
                    parentNode: orbit);
            }

            // Atualiza InfoPanel com o resultado do último orbital expandido
            if (lastResult != null)
                tab.Result = lastResult;

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

    // ── Navegação central ─────────────────────────────────────────────────────

    private async Task DoNavigate(BrowserTab tab, string url, NavigationNode? parentNode)
    {
        tab.IsLoading = true;
        try
        {
            var result = await _browser.NavigateAsync(url);

            // Sempre atualiza Result — isso aciona o binding do InfoPanel
            tab.Result = result;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                tab.Title = uri.Host;

            if (tab.History.Count == 0 || tab.History[^1] != url)
                tab.History.Add(url);

            tab.GraphNodes = _graph.AppendNavigation(
                tab.GraphNodes,
                tab.GraphEdges,
                url,
                result.Links,
                result.Subdomains,
                parentNode);
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