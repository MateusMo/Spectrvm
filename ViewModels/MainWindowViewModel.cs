using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
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
        await DoNavigate(tab, tab.CurrentUrl);
    }

    /// <summary>Chamado pelo clique num nó do grafo.</summary>
    public async Task NavigateToUrl(string url)
    {
        if (SelectedTab is not { } tab) return;
        tab.CurrentUrl = url;
        await DoNavigate(tab, url);
    }

    /// <summary>Alterna a ordenação dos links extraídos por Type.</summary>
    public void SortLinks()
    {
        if (SelectedTab?.Result is not { } result) return;
        var sorted = result.Links.OrderBy(l => l.Type).ThenBy(l => l.Url).ToList();
        result.Links = sorted;
        // Força notificação
        SelectedTab.Result = new BrowserResult
        {
            Html        = result.Html,
            CurlCommand = result.CurlCommand,
            RequestInfo = result.RequestInfo,
            Links       = sorted,
            StatusCode  = result.StatusCode,
            ContentType = result.ContentType,
            Timestamp   = result.Timestamp
        };
    }

    private async Task DoNavigate(BrowserTab tab, string url)
    {
        tab.IsLoading = true;
        try
        {
            var result = await _browser.NavigateAsync(url);
            tab.Result = result;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                tab.Title = uri.Host;

            if (tab.History.Count == 0 || tab.History[^1] != url)
                tab.History.Add(url);

            tab.GraphNodes = _graph.AppendNavigation(
                tab.GraphNodes, url, result.Links);
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