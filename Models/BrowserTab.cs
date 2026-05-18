using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;
using Spectrvm.Models;

namespace Spectrvm.Models;

public class BrowserTab : ReactiveObject
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _currentUrl = "https://";
    public string CurrentUrl
    {
        get => _currentUrl;
        set => this.RaiseAndSetIfChanged(ref _currentUrl, value);
    }

    private string _title = "New Tab";
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    private BrowserResult _result = new();
    public BrowserResult Result
    {
        get => _result;
        set => this.RaiseAndSetIfChanged(ref _result, value);
    }

    private ViewMode _viewMode = ViewMode.Interpreter;
    public ViewMode ViewMode
    {
        get => _viewMode;
        set => this.RaiseAndSetIfChanged(ref _viewMode, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private List<NavigationNode> _graphNodes = new();
    public List<NavigationNode> GraphNodes
    {
        get => _graphNodes;
        set => this.RaiseAndSetIfChanged(ref _graphNodes, value);
    }

    /// <summary>
    /// Arestas de navegação explícitas entre nós.
    /// Substituiu a lógica Node.Next/Parent no render porque arestas
    /// saindo de orbitais (não-primários) não eram visitadas pelo loop de primários.
    /// </summary>
    public List<NavigationEdge> GraphEdges { get; } = new();

    /// <summary>Histórico de URLs visitadas nesta aba.</summary>
    public ObservableCollection<string> History { get; } = new();
}