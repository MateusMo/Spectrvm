using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Spectrvm.Models;

namespace Spectrvm.Controls;

public class GraphCanvas : Control
{
    // ── Propriedade de dados ─────────────────────────────────────────────────

    public static readonly StyledProperty<List<NavigationNode>?> NodesProperty =
        AvaloniaProperty.Register<GraphCanvas, List<NavigationNode>?>(nameof(Nodes));

    public List<NavigationNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set
        {
            SetValue(NodesProperty, value);
            _labelCache.Clear();          // invalida cache de labels ao trocar os nós
            UpdateTimerState(value);
            InvalidateVisual();
        }
    }

    public Action<string, NavigationNode>? OnNodeClicked { get; set; }

    // ── Brushes / Pens ESTÁTICOS (zero alocação por frame) ──────────────────

    // Fundos
    private static readonly ImmutableSolidColorBrush BgBrush   = new(Color.FromRgb(11, 16, 32));
    private static readonly ImmutableSolidColorBrush GridBrush  = new(Color.FromArgb(18, 255, 255, 255));

    // Arestas de navegação
    private static readonly Pen ChainEdgePen = new(
        new ImmutableSolidColorBrush(Color.FromArgb(210, 59, 130, 246)), 2.0)
        { DashStyle = DashStyle.Dash };

    private static readonly Pen NavEdgePen = new(
        new ImmutableSolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 2.2);

    // Cores por NodeKind (fills sólidos)
    private static readonly ImmutableSolidColorBrush[] KindFills = new ImmutableSolidColorBrush[]
    {
        new(Color.FromRgb(59,  130, 246)),  // Primary
        new(Color.FromRgb(34,  197, 94)),   // Internal
        new(Color.FromRgb(100, 160, 220)),  // External
        new(Color.FromRgb(249, 115, 22)),   // Dependency
        new(Color.FromRgb(239, 68,  68)),   // Tracker
        new(Color.FromRgb(6,   182, 212)),  // Cdn
        new(Color.FromRgb(234, 179, 8)),    // Api
        new(Color.FromRgb(168, 85,  247)),  // Suspicious
    };

    // Brushes de aresta por NodeKind (semi-transparentes)
    private static readonly ImmutableSolidColorBrush[] OrbitEdgeBrushes = new ImmutableSolidColorBrush[]
    {
        new(Color.FromArgb(80, 59,  130, 246)),
        new(Color.FromArgb(80, 34,  197, 94)),
        new(Color.FromArgb(80, 100, 160, 220)),
        new(Color.FromArgb(80, 249, 115, 22)),
        new(Color.FromArgb(80, 239, 68,  68)),
        new(Color.FromArgb(80, 6,   182, 212)),
        new(Color.FromArgb(80, 234, 179, 8)),
        new(Color.FromArgb(80, 168, 85,  247)),
    };

    // Pens de aresta por NodeKind (sólido e tracejado, pré-alocados)
    private static readonly Pen[] OrbitEdgePensSolid;
    private static readonly Pen[] OrbitEdgePensDash;

    // Halos por NodeKind (muito transparentes)
    private static readonly ImmutableSolidColorBrush[] HaloBrushes = new ImmutableSolidColorBrush[]
    {
        new(Color.FromArgb(38, 59,  130, 246)),
        new(Color.FromArgb(20, 34,  197, 94)),
        new(Color.FromArgb(20, 100, 160, 220)),
        new(Color.FromArgb(20, 249, 115, 22)),
        new(Color.FromArgb(20, 239, 68,  68)),
        new(Color.FromArgb(20, 6,   182, 212)),
        new(Color.FromArgb(20, 234, 179, 8)),
        new(Color.FromArgb(20, 168, 85,  247)),
    };

    // Brushes de label por NodeKind
    private static readonly ImmutableSolidColorBrush[] LabelBrushes = new ImmutableSolidColorBrush[]
    {
        new(Color.FromArgb(215, 59,  130, 246)),
        new(Color.FromArgb(215, 34,  197, 94)),
        new(Color.FromArgb(215, 100, 160, 220)),
        new(Color.FromArgb(215, 249, 115, 22)),
        new(Color.FromArgb(215, 239, 68,  68)),
        new(Color.FromArgb(215, 6,   182, 212)),
        new(Color.FromArgb(215, 234, 179, 8)),
        new(Color.FromArgb(215, 168, 85,  247)),
    };

    // Legendas
    private static readonly (NodeKind kind, string label)[] LegendItems =
    {
        (NodeKind.Primary,    "primary"),
        (NodeKind.Internal,   "internal"),
        (NodeKind.External,   "external"),
        (NodeKind.Api,        "api"),
        (NodeKind.Cdn,        "cdn"),
        (NodeKind.Dependency, "dependency"),
        (NodeKind.Tracker,    "tracker"),
        (NodeKind.Suspicious, "suspicious"),
    };

    private static readonly ImmutableSolidColorBrush LabelBg =
        new(Color.FromArgb(175, 11, 16, 32));

    private static readonly ImmutableSolidColorBrush HintBrush =
        new(Color.FromArgb(60, 200, 220, 255));

    private static readonly ImmutableSolidColorBrush HudBrush =
        new(Color.FromArgb(45, 200, 220, 255));

    private static readonly Typeface MonoFont = new("Courier New");

    static GraphCanvas()
    {
        // Inicializa pens de aresta pré-alocados
        int n = KindFills.Length;
        OrbitEdgePensSolid = new Pen[n];
        OrbitEdgePensDash  = new Pen[n];
        for (int i = 0; i < n; i++)
        {
            OrbitEdgePensSolid[i] = new Pen(OrbitEdgeBrushes[i], 0.8);
            OrbitEdgePensDash[i]  = new Pen(OrbitEdgeBrushes[i], 0.8) { DashStyle = DashStyle.Dash };
        }

        FocusableProperty.OverrideDefaultValue<GraphCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCanvas>(true);
    }

    // ── Cache de FormattedText ────────────────────────────────────────────────

    // Chave: (url, isPrimary) → FormattedText pronto
    private readonly Dictionary<(string, bool), FormattedText> _labelCache = new();

    private FormattedText GetLabel(NavigationNode node)
    {
        var key = (node.Url, node.IsPrimary);
        if (_labelCache.TryGetValue(key, out var cached)) return cached;

        var ft = new FormattedText(
            ShortenUrl(node.Url),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoFont,
            node.IsPrimary ? 11.0 : 8.0,
            LabelBrushes[(int)node.Kind]);

        _labelCache[key] = ft;
        return ft;
    }

    // ── Animação — timer só ativo se há nós animados ─────────────────────────

    private DispatcherTimer? _pulseTimer;
    private double           _pulsePhase;

    private static bool NeedsAnimation(List<NavigationNode>? nodes)
    {
        if (nodes == null) return false;
        foreach (var n in nodes)
            if (n.IsPrimary || n.Kind == NodeKind.Tracker || n.Kind == NodeKind.Suspicious)
                return true;
        return false;
    }

    private void UpdateTimerState(List<NavigationNode>? nodes)
    {
        bool needs = NeedsAnimation(nodes);

        if (needs && _pulseTimer == null)
        {
            _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; // 20fps — suficiente para pulso suave
            _pulseTimer.Tick += OnPulseTick;
            _pulseTimer.Start();
        }
        else if (!needs && _pulseTimer != null)
        {
            _pulseTimer.Stop();
            _pulseTimer.Tick -= OnPulseTick;
            _pulseTimer = null;
        }
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        _pulsePhase = (_pulsePhase + 0.10) % (2 * Math.PI);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimerState(Nodes);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_pulseTimer != null)
        {
            _pulseTimer.Stop();
            _pulseTimer.Tick -= OnPulseTick;
            _pulseTimer = null;
        }
    }

    // ── Viewport ─────────────────────────────────────────────────────────────

    private double _scale   = 1.0;
    private double _offsetX = 0;
    private double _offsetY = 0;

    private const double MinScale = 0.04;
    private const double MaxScale = 5.0;

    private bool            _isDragging;
    private Point           _dragStart;
    private double          _offsetXAtDragStart;
    private double          _offsetYAtDragStart;

    private bool            _isDraggingNode;
    private NavigationNode? _draggedNode;
    private double          _nodeXAtDragStart;
    private double          _nodeYAtDragStart;

    private Point ToWorld(Point screen) => new(
        (screen.X - _offsetX) / _scale,
        (screen.Y - _offsetY) / _scale);

    // ── Input ────────────────────────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var mouse    = e.GetPosition(this);
        var factor   = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        var newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);

        _offsetX = mouse.X - (mouse.X - _offsetX) * (newScale / _scale);
        _offsetY = mouse.Y - (mouse.Y - _offsetY) * (newScale / _scale);
        _scale   = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pos   = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        var hit = HitTest(ToWorld(pos));
        if (hit != null)
        {
            _isDraggingNode   = true;
            _draggedNode      = hit;
            _dragStart        = pos;
            _nodeXAtDragStart = hit.X;
            _nodeYAtDragStart = hit.Y;
        }
        else
        {
            _isDragging         = true;
            _dragStart          = pos;
            _offsetXAtDragStart = _offsetX;
            _offsetYAtDragStart = _offsetY;
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isDraggingNode && _draggedNode != null)
        {
            var delta      = pos - _dragStart;
            _draggedNode.X = _nodeXAtDragStart + delta.X / _scale;
            _draggedNode.Y = _nodeYAtDragStart + delta.Y / _scale;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isDragging)
        {
            var delta = pos - _dragStart;
            _offsetX  = _offsetXAtDragStart + delta.X;
            _offsetY  = _offsetYAtDragStart + delta.Y;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        if (_isDraggingNode && _draggedNode != null)
        {
            var dist = pos - _dragStart;
            if (Math.Abs(dist.X) < 5 && Math.Abs(dist.Y) < 5)
                OnNodeClicked?.Invoke(_draggedNode.Url, _draggedNode);
        }

        _isDragging     = false;
        _isDraggingNode = false;
        _draggedNode    = null;
        e.Handled       = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if      (e.Key == Key.R) { ResetView(); e.Handled = true; }
        else if (e.Key == Key.F) { FitAll();    e.Handled = true; }
    }

    // ── HitTest ──────────────────────────────────────────────────────────────

    private static NavigationNode? HitTest(Point world, List<NavigationNode> nodes)
    {
        foreach (var node in nodes)
        {
            double r  = VisualRadius(node) + 6;
            double dx = world.X - node.X;
            double dy = world.Y - node.Y;
            if (dx * dx + dy * dy <= r * r) return node;
        }
        return null;
    }

    private NavigationNode? HitTest(Point world)
    {
        var nodes = Nodes;
        return nodes == null ? null : HitTest(world, nodes);
    }

    // ── View ─────────────────────────────────────────────────────────────────

    private void ResetView()
    {
        _scale = 1.0; _offsetX = 0; _offsetY = 0;
        InvalidateVisual();
    }

    private void FitAll()
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var n in nodes)
        {
            if (n.X < minX) minX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.X > maxX) maxX = n.X;
            if (n.Y > maxY) maxY = n.Y;
        }

        double pad    = 120;
        double graphW = maxX - minX + pad * 2;
        double graphH = maxY - minY + pad * 2;
        double w      = Bounds.Width  > 0 ? Bounds.Width  : 1200;
        double h      = Bounds.Height > 0 ? Bounds.Height : 800;

        _scale   = Math.Clamp(Math.Min(w / graphW, h / graphH), MinScale, MaxScale);
        _offsetX = (w - graphW * _scale) / 2 - (minX - pad) * _scale;
        _offsetY = (h - graphH * _scale) / 2 - (minY - pad) * _scale;
        InvalidateVisual();
    }

    // ── Helpers visuais ───────────────────────────────────────────────────────

    private static double VisualRadius(NavigationNode n) =>
        n.Kind == NodeKind.Primary ? 11.0 : 6.0;

    private static bool IsFastPulse(NodeKind k) =>
        k == NodeKind.Tracker || k == NodeKind.Suspicious;

    private static bool HasDashedEdge(NodeKind k) =>
        k == NodeKind.Dependency || k == NodeKind.Tracker || k == NodeKind.Suspicious;

    // ── Render ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var bounds = Bounds;

        ctx.DrawRectangle(BgBrush, null, new Rect(bounds.Size));
        DrawGrid(ctx, bounds);

        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
        {
            DrawHint(ctx, bounds);
            DrawHud(ctx, bounds);
            return;
        }

        var transform = Matrix.CreateScale(_scale, _scale) *
                        Matrix.CreateTranslation(_offsetX, _offsetY);

        using (ctx.PushTransform(transform))
        {
            // 1. Arestas de órbita
            foreach (var node in nodes)
            {
                if (!node.IsPrimary) continue;
                foreach (var orbit in node.OrbitNodes)
                {
                    int ki  = (int)orbit.Kind;
                    var pen = HasDashedEdge(orbit.Kind)
                        ? OrbitEdgePensDash[ki]
                        : OrbitEdgePensSolid[ki];
                    ctx.DrawLine(pen,
                        new Point(node.X, node.Y),
                        new Point(orbit.X, orbit.Y));
                }
            }

            // 2. Arestas de navegação
            foreach (var node in nodes)
            {
                if (!node.IsPrimary || node.Next == null) continue;

                bool viaOrbit = node.Next.Parent != null && !node.Next.Parent.IsPrimary;
                var  edgePen  = viaOrbit ? NavEdgePen : ChainEdgePen;
                double fromX  = viaOrbit && node.Next.Parent != null ? node.Next.Parent.X : node.X;
                double fromY  = viaOrbit && node.Next.Parent != null ? node.Next.Parent.Y : node.Y;

                ctx.DrawLine(edgePen, new Point(fromX, fromY), new Point(node.Next.X, node.Next.Y));
                DrawArrow(ctx, edgePen.Brush!, new Point(fromX, fromY), new Point(node.Next.X, node.Next.Y));
            }

            // 3. Nós + halos + labels
            double pulse     = Math.Abs(Math.Sin(_pulsePhase));
            double fastPulse = Math.Abs(Math.Sin(_pulsePhase * 2.8));

            foreach (var node in nodes)
            {
                int    ki = (int)node.Kind;
                double r  = VisualRadius(node);
                double pt = IsFastPulse(node.Kind) ? fastPulse : pulse;

                double haloBase = r + (node.IsPrimary ? 10 : 6);
                double haloR    = haloBase + pt * (node.IsPrimary ? 7 : 3);

                ctx.DrawEllipse(HaloBrushes[ki], null, new Point(node.X, node.Y), haloR, haloR);
                ctx.DrawEllipse(KindFills[ki],   null, new Point(node.X, node.Y), r,     r);

                DrawLabel(ctx, node, r);
            }
        }

        DrawLegend(ctx, bounds);
        DrawHud(ctx, bounds);
    }

    // ── Seta ─────────────────────────────────────────────────────────────────

    private static readonly StreamGeometry _arrowGeom = new();

    private static void DrawArrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        double dx  = to.X - from.X;
        double dy  = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double ux = dx / len, uy = dy / len;
        double tx = to.X - ux * 13;
        double ty = to.Y - uy * 13;

        double bx1 = tx - ux * 10 - uy * 5;
        double by1 = ty - uy * 10 + ux * 5;
        double bx2 = tx - ux * 10 + uy * 5;
        double by2 = ty - uy * 10 - ux * 5;

        // Reutiliza a geometria (recria apenas os pontos — sem alloc de objeto)
        var geom = new StreamGeometry();
        using (var sg = geom.Open())
        {
            sg.BeginFigure(new Point(tx, ty), true);
            sg.LineTo(new Point(bx1, by1));
            sg.LineTo(new Point(bx2, by2));
            sg.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geom);
    }

    // ── Label ─────────────────────────────────────────────────────────────────

    private void DrawLabel(DrawingContext ctx, NavigationNode node, double r)
    {
        var ft = GetLabel(node);

        double lx = node.X - ft.Width / 2;
        double ly = node.IsPrimary
            ? node.Y - r - 15
            : node.Y < node.OrbitParentY
                ? node.Y - r - 12
                : node.Y + r + 3;

        ctx.DrawRectangle(LabelBg, null,
            new Rect(lx - 3, ly - 1, ft.Width + 6, ft.Height + 2), 3, 3);
        ctx.DrawText(ft, new Point(lx, ly));
    }

    // ── Grid / HUD / Hint / Legend ────────────────────────────────────────────

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        double gridSize = 40 * _scale;
        if (gridSize < 6) return;

        double startX = _offsetX % gridSize;
        double startY = _offsetY % gridSize;

        for (double x = startX; x < bounds.Width;  x += gridSize)
        for (double y = startY; y < bounds.Height; y += gridSize)
            ctx.DrawEllipse(GridBrush, null, new Point(x, y), 1, 1);
    }

    // Hint e HUD são raros (sem nós / static) — FormattedText criado uma vez por sessão
    private FormattedText? _hintFt;
    private FormattedText? _hudFt;

    private void DrawHint(DrawingContext ctx, Rect bounds)
    {
        _hintFt ??= new FormattedText(
            "Navegue para uma URL para ver o grafo aparecer aqui",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont, 13, HintBrush);

        ctx.DrawText(_hintFt, new Point(
            bounds.Width  / 2 - _hintFt.Width  / 2,
            bounds.Height / 2 - _hintFt.Height / 2));
    }

    private void DrawHud(DrawingContext ctx, Rect bounds)
    {
        _hudFt ??= new FormattedText(
            "scroll = zoom  •  drag fundo = pan  •  drag nó = mover  •  clique nó = navegar  •  R = reset  •  F = fit",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont, 9, HudBrush);

        ctx.DrawText(_hudFt, new Point(12, bounds.Height - _hudFt.Height - 8));
    }

    // Legenda também pré-computada
    private (FormattedText ft, ImmutableSolidColorBrush dot)[]? _legendItems;

    private void DrawLegend(DrawingContext ctx, Rect bounds)
    {
        if (_legendItems == null)
        {
            _legendItems = new (FormattedText, ImmutableSolidColorBrush)[LegendItems.Length];
            for (int i = 0; i < LegendItems.Length; i++)
            {
                var (kind, label) = LegendItems[i];
                int ki = (int)kind;
                _legendItems[i] = (
                    new FormattedText(label,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, MonoFont, 9,
                        new ImmutableSolidColorBrush(Color.FromArgb(155,
                            KindFills[ki].Color.R,
                            KindFills[ki].Color.G,
                            KindFills[ki].Color.B))),
                    KindFills[ki]
                );
            }
        }

        double x = bounds.Width - 140;
        double y = 14;

        foreach (var (ft, dot) in _legendItems)
        {
            ctx.DrawEllipse(dot, null, new Point(x, y + 4), 4, 4);
            ctx.DrawText(ft, new Point(x + 10, y));
            y += 16;
        }
    }

    // ── Util ─────────────────────────────────────────────────────────────────

    private static string ShortenUrl(string url)
    {
        if (url.Length <= 36) return url;
        try
        {
            var uri  = new Uri(url);
            var path = uri.AbsolutePath.Length > 14
                ? uri.AbsolutePath[..14] + "…"
                : uri.AbsolutePath;
            return uri.Host + path;
        }
        catch { return url[..33] + "…"; }
    }
}