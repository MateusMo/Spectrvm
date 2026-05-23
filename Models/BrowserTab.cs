using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;
using Spectrvm.Models;
using WebViewControl;

namespace Spectrvm.Models;

public class BrowserTab : ReactiveObject
{
    public Guid Id { get; } = Guid.NewGuid();

    // ── WebView dedicado a esta aba ───────────────────────────────────────────
    // Cada aba tem sua própria instância de WebView para isolamento total.
    public WebView? WebViewInstance { get; set; }

    // ── URL atual ─────────────────────────────────────────────────────────────
    private string _currentUrl = "https://";
    public string CurrentUrl
    {
        get => _currentUrl;
        set => this.RaiseAndSetIfChanged(ref _currentUrl, value);
    }

    // ── Título ────────────────────────────────────────────────────────────────
    private string _title = "New Tab";
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    // ── Resultado da análise HTTP ─────────────────────────────────────────────
    private BrowserResult _result = new();
    public BrowserResult Result
    {
        get => _result;
        set => this.RaiseAndSetIfChanged(ref _result, value);
    }

    // ── Modo de visualização ──────────────────────────────────────────────────
    private ViewMode _viewMode = ViewMode.Interpreter;
    public ViewMode ViewMode
    {
        get => _viewMode;
        set => this.RaiseAndSetIfChanged(ref _viewMode, value);
    }

    // ── Loading / Analyzing ───────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set => this.RaiseAndSetIfChanged(ref _isAnalyzing, value);
    }

    // ── Toggle HTML cru / renderizado (Interpreter) ───────────────────────────
    private bool _showRawHtml;
    public bool ShowRawHtml
    {
        get => _showRawHtml;
        set => this.RaiseAndSetIfChanged(ref _showRawHtml, value);
    }

    // ── Grafo ────────────────────────────────────────────────────────────────
    private List<NavigationNode> _graphNodes = new();
    public List<NavigationNode> GraphNodes
    {
        get => _graphNodes;
        set => this.RaiseAndSetIfChanged(ref _graphNodes, value);
    }

    public List<NavigationEdge>         GraphEdges { get; } = new();
    public ObservableCollection<string> History    { get; } = new();

    // ── Pilha de navegação interna (Voltar) ───────────────────────────────────
    // Guardamos URLs confirmadas para poder voltar.
    public Stack<string> BackStack { get; } = new();

    // ── Pode voltar? ──────────────────────────────────────────────────────────
    private bool _canGoBack;
    public bool CanGoBack
    {
        get => _canGoBack;
        set => this.RaiseAndSetIfChanged(ref _canGoBack, value);
    }
}