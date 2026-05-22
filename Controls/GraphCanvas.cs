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
    // ── Propriedades ─────────────────────────────────────────────────────────

    public static readonly StyledProperty<List<NavigationNode>?> NodesProperty =
        AvaloniaProperty.Register<GraphCanvas, List<NavigationNode>?>(nameof(Nodes));

    public List<NavigationNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set
        {
            SetValue(NodesProperty, value);
            _labelCache.Clear();
            UpdateTimerState(value);
            InvalidateVisual();
        }
    }

    public List<NavigationEdge>? Edges { get; set; }

    public Action<string, NavigationNode>? OnNodeClicked { get; set; }
    public Action<NavigationNode>? OnAtomicExpand { get; set; }

    // ── Brushes / Pens ───────────────────────────────────────────────────────

    private static readonly ImmutableSolidColorBrush BgBrush  = new(Color.FromRgb(11, 16, 32));
    private static readonly ImmutableSolidColorBrush GridBrush = new(Color.FromArgb(18, 255, 255, 255));

    private static readonly Pen ChainEdgePen = new(
        new ImmutableSolidColorBrush(Color.FromArgb(210, 59, 130, 246)), 2.0)
        { DashStyle = DashStyle.Dash };

    private static readonly Pen SubLinkEdgePen = new(
        new ImmutableSolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2.0);

    private static readonly ImmutableSolidColorBrush[] KindFills =
    [
        new(Color.FromRgb(59,  130, 246)),  // Primary
        new(Color.FromRgb(34,  197, 94)),   // Internal
        new(Color.FromRgb(100, 160, 220)),  // External
        new(Color.FromRgb(249, 115, 22)),   // Dependency
        new(Color.FromRgb(239, 68,  68)),   // Tracker
        new(Color.FromRgb(6,   182, 212)),  // Cdn
        new(Color.FromRgb(234, 179, 8)),    // Api
        new(Color.FromRgb(168, 85,  247)),  // Suspicious
        new(Color.FromRgb(20,  184, 166)),  // Subdomain  ← novo
    ];

    private static readonly ImmutableSolidColorBrush[] OrbitEdgeBrushes =
    [
        new(Color.FromArgb(80, 59,  130, 246)),
        new(Color.FromArgb(80, 34,  197, 94)),
        new(Color.FromArgb(80, 100, 160, 220)),
        new(Color.FromArgb(80, 249, 115, 22)),
        new(Color.FromArgb(80, 239, 68,  68)),
        new(Color.FromArgb(80, 6,   182, 212)),
        new(Color.FromArgb(80, 234, 179, 8)),
        new(Color.FromArgb(80, 168, 85,  247)),
        new(Color.FromArgb(80, 20,  184, 166)), // Subdomain
    ];

    private static readonly Pen[] OrbitEdgePensSolid;
    private static readonly Pen[] OrbitEdgePensDash;

    private static readonly ImmutableSolidColorBrush[] HaloBrushes =
    [
        new(Color.FromArgb(38, 59,  130, 246)),
        new(Color.FromArgb(20, 34,  197, 94)),
        new(Color.FromArgb(20, 100, 160, 220)),
        new(Color.FromArgb(20, 249, 115, 22)),
        new(Color.FromArgb(20, 239, 68,  68)),
        new(Color.FromArgb(20, 6,   182, 212)),
        new(Color.FromArgb(20, 234, 179, 8)),
        new(Color.FromArgb(20, 168, 85,  247)),
        new(Color.FromArgb(20, 20,  184, 166)), // Subdomain
    ];

    private static readonly ImmutableSolidColorBrush[] LabelBrushes =
    [
        new(Color.FromArgb(215, 59,  130, 246)),
        new(Color.FromArgb(215, 34,  197, 94)),
        new(Color.FromArgb(215, 100, 160, 220)),
        new(Color.FromArgb(215, 249, 115, 22)),
        new(Color.FromArgb(215, 239, 68,  68)),
        new(Color.FromArgb(215, 6,   182, 212)),
        new(Color.FromArgb(215, 234, 179, 8)),
        new(Color.FromArgb(215, 168, 85,  247)),
        new(Color.FromArgb(215, 20,  184, 166)), // Subdomain
    ];

    // Legenda agora inclui subdomain e ficará no canto INFERIOR ESQUERDO
    private static readonly (NodeKind kind, string label)[] LegendItems =
    [
        (NodeKind.Primary,    "primary"),
        (NodeKind.Internal,   "internal"),
        (NodeKind.External,   "external"),
        (NodeKind.Api,        "api"),
        (NodeKind.Cdn,        "cdn"),
        (NodeKind.Dependency, "dependency"),
        (NodeKind.Tracker,    "tracker"),
        (NodeKind.Suspicious, "suspicious"),
        (NodeKind.Subdomain,  "subdomain"),
    ];

    private static readonly ImmutableSolidColorBrush LabelBg   = new(Color.FromArgb(175, 11, 16, 32));
    private static readonly ImmutableSolidColorBrush HintBrush = new(Color.FromArgb(60,  200, 220, 255));
    private static readonly ImmutableSolidColorBrush HudBrush  = new(Color.FromArgb(45,  200, 220, 255));

    private static readonly ImmutableSolidColorBrush ExpandFill  = new(Color.FromArgb(200, 20,  60,  120));
    private static readonly ImmutableSolidColorBrush ExpandHover = new(Color.FromArgb(240, 30,  100, 200));
    private static readonly ImmutableSolidColorBrush ExpandText  = new(Color.FromArgb(230, 100, 200, 255));
    private static readonly Pen ExpandBorder = new(new ImmutableSolidColorBrush(Color.FromArgb(160, 59, 130, 246)), 1.0);

    private static readonly Typeface MonoFont = new("Courier New");

    static GraphCanvas()
    {
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

    // ── Cache ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<(string, bool), FormattedText> _labelCache = new();

    private FormattedText GetLabel(NavigationNode node)
    {
        var key = (node.Url, node.IsPrimary);
        if (_labelCache.TryGetValue(key, out var cached)) return cached;
        var kindIdx = Math.Min((int)node.Kind, LabelBrushes.Length - 1);
        var ft = new FormattedText(
            ShortenUrl(node.Url),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont,
            node.IsPrimary ? 11.0 : 8.0,
            LabelBrushes[kindIdx]);
        _labelCache[key] = ft;
        return ft;
    }

    // ── Expansão atômica ─────────────────────────────────────────────────────

    private readonly HashSet<Guid> _expandingNodes = new();

    public void MarkExpanding(NavigationNode node)   { _expandingNodes.Add(node.Id);    InvalidateVisual(); }
    public void UnmarkExpanding(NavigationNode node) { _expandingNodes.Remove(node.Id); InvalidateVisual(); }

    private NavigationNode? _hoveredExpandBtn;

    // ── Animação ─────────────────────────────────────────────────────────────

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
            _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
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

    private double _scale = 1.0, _offsetX = 0, _offsetY = 0;
    private const double MinScale = 0.04, MaxScale = 5.0;

    private bool            _isDragging;
    private Point           _dragStart;
    private double          _offsetXAtDragStart, _offsetYAtDragStart;

    private bool            _isDraggingNode;
    private NavigationNode? _draggedNode;
    private double          _nodeXAtDragStart, _nodeYAtDragStart;
    private bool            _pressedExpandBtn;

    private readonly List<(NavigationNode orbit, double dx, double dy)> _orbitDragOffsets = new();

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
        var pos = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var world = ToWorld(pos);

        var expandHit = HitTestExpandBtn(world);
        if (expandHit != null)
        {
            _pressedExpandBtn = true;
            _draggedNode = expandHit;
            _dragStart = pos;
            e.Handled = true;
            return;
        }

        _pressedExpandBtn = false;
        var hit = HitTest(world);

        if (hit != null)
        {
            _isDraggingNode = true;
            _draggedNode = hit;
            _dragStart = pos;
            _nodeXAtDragStart = hit.X;
            _nodeYAtDragStart = hit.Y;
            _orbitDragOffsets.Clear();
            if (hit.IsPrimary)
                foreach (var orbit in hit.OrbitNodes)
                    _orbitDragOffsets.Add((orbit, orbit.X - hit.X, orbit.Y - hit.Y));
        }
        else
        {
            _isDragging = true;
            _dragStart = pos;
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
            var delta = pos - _dragStart;
            double newX = _nodeXAtDragStart + delta.X / _scale;
            double newY = _nodeYAtDragStart + delta.Y / _scale;
            _draggedNode.X = newX;
            _draggedNode.Y = newY;
            if (_draggedNode.IsPrimary)
                foreach (var (orbit, dx, dy) in _orbitDragOffsets)
                {
                    orbit.X = newX + dx;
                    orbit.Y = newY + dy;
                    orbit.OrbitParentY = newY;
                }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isDragging)
        {
            var delta = pos - _dragStart;
            _offsetX = _offsetXAtDragStart + delta.X;
            _offsetY = _offsetYAtDragStart + delta.Y;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var world     = ToWorld(pos);
        var prevHover = _hoveredExpandBtn;
        _hoveredExpandBtn = HitTestExpandBtn(world);
        if (!ReferenceEquals(prevHover, _hoveredExpandBtn))
        {
            Cursor = _hoveredExpandBtn != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        if (_pressedExpandBtn && _draggedNode != null)
        {
            var dist = pos - _dragStart;
            if (Math.Abs(dist.X) < 8 && Math.Abs(dist.Y) < 8)
                OnAtomicExpand?.Invoke(_draggedNode);
            _pressedExpandBtn = false;
            _draggedNode      = null;
            e.Handled = true;
            return;
        }

        if (_isDraggingNode && _draggedNode != null)
        {
            var dist = pos - _dragStart;
            if (Math.Abs(dist.X) < 5 && Math.Abs(dist.Y) < 5)
                OnNodeClicked?.Invoke(_draggedNode.Url, _draggedNode);
        }

        _isDragging       = false;
        _isDraggingNode   = false;
        _pressedExpandBtn = false;
        _draggedNode      = null;
        _orbitDragOffsets.Clear();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if      (e.Key == Key.R) { ResetView(); e.Handled = true; }
        else if (e.Key == Key.F) { FitAll();    e.Handled = true; }
    }

    // ── HitTest ──────────────────────────────────────────────────────────────

    private NavigationNode? HitTest(Point world)
    {
        var nodes = Nodes;
        if (nodes == null) return null;
        foreach (var node in nodes)
        {
            double r  = VisualRadius(node) + 6;
            double dx = world.X - node.X;
            double dy = world.Y - node.Y;
            if (dx * dx + dy * dy <= r * r) return node;
        }
        return null;
    }

    private const double ExpandBtnRadius = 7.0;

    private static (double x, double y) ExpandBtnCenter(NavigationNode node) =>
        (node.X + VisualRadius(node) + ExpandBtnRadius + 3, node.Y);

    private NavigationNode? HitTestExpandBtn(Point world)
    {
        var nodes = Nodes;
        if (nodes == null) return null;
        foreach (var node in nodes)
        {
            if (!node.IsPrimary) continue;
            var (bx, by) = ExpandBtnCenter(node);
            double dx = world.X - bx, dy = world.Y - by;
            if (dx * dx + dy * dy <= ExpandBtnRadius * ExpandBtnRadius) return node;
        }
        return null;
    }

    // ── View controls ────────────────────────────────────────────────────────

    private void ResetView() { _scale = 1.0; _offsetX = 0; _offsetY = 0; InvalidateVisual(); }

    private void FitAll()
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in nodes)
        {
            if (n.X < minX) minX = n.X; if (n.Y < minY) minY = n.Y;
            if (n.X > maxX) maxX = n.X; if (n.Y > maxY) maxY = n.Y;
        }
        double pad = 120;
        double w   = Bounds.Width  > 0 ? Bounds.Width  : 1200;
        double h   = Bounds.Height > 0 ? Bounds.Height : 800;
        _scale   = Math.Clamp(Math.Min(w / (maxX - minX + pad * 2), h / (maxY - minY + pad * 2)), MinScale, MaxScale);
        _offsetX = (w - (maxX - minX + pad * 2) * _scale) / 2 - (minX - pad) * _scale;
        _offsetY = (h - (maxY - minY + pad * 2) * _scale) / 2 - (minY - pad) * _scale;
        InvalidateVisual();
    }

    // ── Helpers visuais ───────────────────────────────────────────────────────

    private static double VisualRadius(NavigationNode n) =>
        n.Kind == NodeKind.Subdomain ? 7.0 : n.IsPrimary ? 11.0 : 6.0;

    private static bool IsFastPulse(NodeKind k) => k == NodeKind.Tracker || k == NodeKind.Suspicious;
    private static bool HasDashedEdge(NodeKind k) => k == NodeKind.Dependency || k == NodeKind.Tracker
                                                   || k == NodeKind.Suspicious || k == NodeKind.Subdomain;

    private static int KindIndex(NodeKind k) => Math.Min((int)k, KindFills.Length - 1);

    // ── Render ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var bounds = Bounds;
        ctx.DrawRectangle(BgBrush, null, new Rect(bounds.Size));
        DrawGrid(ctx, bounds);

        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) { DrawHint(ctx, bounds); DrawHud(ctx, bounds); return; }

        var transform = Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_offsetX, _offsetY);
        using (ctx.PushTransform(transform))
        {
            // 1. Arestas órbita
            foreach (var node in nodes)
            {
                if (!node.IsPrimary) continue;
                foreach (var orbit in node.OrbitNodes)
                {
                    int ki  = KindIndex(orbit.Kind);
                    var pen = HasDashedEdge(orbit.Kind) ? OrbitEdgePensDash[ki] : OrbitEdgePensSolid[ki];
                    ctx.DrawLine(pen, new Point(node.X, node.Y), new Point(orbit.X, orbit.Y));
                }
            }

            // 2. Arestas de navegação explícitas
            var edges = Edges;
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    var from = new Point(edge.Source.X, edge.Source.Y);
                    var to   = new Point(edge.Target.X, edge.Target.Y);
                    ctx.DrawLine(ChainEdgePen, from, to);
                    DrawArrow(ctx, ChainEdgePen.Brush!, from, to);
                }
            }

            // 3. Nós + halos + labels + botão ⊕
            double pulse     = Math.Abs(Math.Sin(_pulsePhase));
            double fastPulse = Math.Abs(Math.Sin(_pulsePhase * 2.8));

            foreach (var node in nodes)
            {
                int    ki = KindIndex(node.Kind);
                double r  = VisualRadius(node);
                double pt = IsFastPulse(node.Kind) ? fastPulse : pulse;
                double hr = r + (node.IsPrimary ? 10 : 5) + pt * (node.IsPrimary ? 7 : 3);

                ctx.DrawEllipse(HaloBrushes[ki], null, new Point(node.X, node.Y), hr, hr);
                ctx.DrawEllipse(KindFills[ki],   null, new Point(node.X, node.Y), r,  r);
                DrawLabel(ctx, node, r);

                if (node.IsPrimary)
                    DrawExpandButton(ctx, node, pulse);
            }
        }

        // Legenda agora no canto INFERIOR ESQUERDO (longe do InfoPanel)
        DrawLegend(ctx, bounds);
        DrawHud(ctx, bounds);
    }

    // ── Botão ⊕ ──────────────────────────────────────────────────────────────

    private void DrawExpandButton(DrawingContext ctx, NavigationNode node, double pulse)
    {
        var (bx, by)    = ExpandBtnCenter(node);
        bool isExpanding = _expandingNodes.Contains(node.Id);
        bool isHovered   = ReferenceEquals(_hoveredExpandBtn, node);
        double br        = ExpandBtnRadius + (isExpanding ? pulse * 2.5 : 0);

        ctx.DrawEllipse(isHovered && !isExpanding ? ExpandHover : ExpandFill, ExpandBorder,
            new Point(bx, by), br, br);

        string icon = isExpanding ? "…" : "⊕";
        var ft = new FormattedText(icon,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont,
            isExpanding ? 7.0 : 9.0, ExpandText);
        ctx.DrawText(ft, new Point(bx - ft.Width / 2, by - ft.Height / 2));
    }

    // ── Seta ─────────────────────────────────────────────────────────────────

    private static void DrawArrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double ux = dx / len, uy = dy / len;
        double tx = to.X - ux * 13, ty = to.Y - uy * 13;
        var geom = new StreamGeometry();
        using (var sg = geom.Open())
        {
            sg.BeginFigure(new Point(tx, ty), true);
            sg.LineTo(new Point(tx - ux * 10 - uy * 5, ty - uy * 10 + ux * 5));
            sg.LineTo(new Point(tx - ux * 10 + uy * 5, ty - uy * 10 - ux * 5));
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
            : node.Y < node.OrbitParentY ? node.Y - r - 12 : node.Y + r + 3;
        ctx.DrawRectangle(LabelBg, null, new Rect(lx - 3, ly - 1, ft.Width + 6, ft.Height + 2), 3, 3);
        ctx.DrawText(ft, new Point(lx, ly));
    }

    // ── Grid / HUD / Hint ────────────────────────────────────────────────────

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        double gs = 40 * _scale;
        if (gs < 6) return;
        for (double x = _offsetX % gs; x < bounds.Width;  x += gs)
        for (double y = _offsetY % gs; y < bounds.Height; y += gs)
            ctx.DrawEllipse(GridBrush, null, new Point(x, y), 1, 1);
    }

    private FormattedText? _hintFt, _hudFt;

    private void DrawHint(DrawingContext ctx, Rect bounds)
    {
        _hintFt ??= new FormattedText("Navegue para uma URL para ver o grafo aparecer aqui",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont, 13, HintBrush);
        ctx.DrawText(_hintFt, new Point(bounds.Width / 2 - _hintFt.Width / 2, bounds.Height / 2 - _hintFt.Height / 2));
    }

    private void DrawHud(DrawingContext ctx, Rect bounds)
    {
        _hudFt ??= new FormattedText(
            "scroll = zoom  •  drag fundo = pan  •  drag nó = mover  •  clique nó = navegar  •  ⊕ = expandir tudo  •  R = reset  •  F = fit",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoFont, 9, HudBrush);
        ctx.DrawText(_hudFt, new Point(12, bounds.Height - _hudFt.Height - 8));
    }

    // ── Legenda — CANTO INFERIOR ESQUERDO ────────────────────────────────────

    private (FormattedText ft, ImmutableSolidColorBrush dot)[]? _legendItems;

    private void DrawLegend(DrawingContext ctx, Rect bounds)
    {
        if (_legendItems == null)
        {
            _legendItems = new (FormattedText, ImmutableSolidColorBrush)[LegendItems.Length];
            for (int i = 0; i < LegendItems.Length; i++)
            {
                var (kind, label) = LegendItems[i];
                int ki = KindIndex(kind);
                _legendItems[i] = (
                    new FormattedText(label,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, MonoFont, 9,
                        new ImmutableSolidColorBrush(Color.FromArgb(155,
                            KindFills[ki].Color.R, KindFills[ki].Color.G, KindFills[ki].Color.B))),
                    KindFills[ki]);
            }
        }

        // Posição: inferior esquerdo, acima do HUD
        double hudH  = 18;
        double itemH = 16;
        double totalH = LegendItems.Length * itemH;
        double x = 14;
        double y = bounds.Height - hudH - totalH - 8;

        foreach (var (ft, dot) in _legendItems)
        {
            ctx.DrawEllipse(dot, null, new Point(x, y + 5), 4, 4);
            ctx.DrawText(ft, new Point(x + 10, y));
            y += itemH;
        }
    }

    // ── Util ─────────────────────────────────────────────────────────────────

    private static string ShortenUrl(string url)
    {
        if (url.Length <= 36) return url;
        try
        {
            var uri  = new Uri(url);
            var path = uri.AbsolutePath.Length > 14 ? uri.AbsolutePath[..14] + "…" : uri.AbsolutePath;
            return uri.Host + path;
        }
        catch { return url[..33] + "…"; }
    }
}