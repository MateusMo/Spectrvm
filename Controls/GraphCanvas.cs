using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
        set => SetValue(NodesProperty, value);
    }

    /// <summary>
    /// Callback: (url, node) — devolve o nó clicado para ser usado como pai no grafo.
    /// </summary>
    public Action<string, NavigationNode>? OnNodeClicked { get; set; }

    static GraphCanvas()
    {
        NodesProperty.Changed.AddClassHandler<GraphCanvas>((c, _) => c.InvalidateVisual());
        FocusableProperty.OverrideDefaultValue<GraphCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCanvas>(true);
    }

    // ── Animação ─────────────────────────────────────────────────────────────

    private DispatcherTimer? _pulseTimer;
    private double           _pulsePhase;

    private void EnsurePulseTimer()
    {
        if (_pulseTimer != null) return;
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulsePhase = (_pulsePhase + 0.07) % (2 * Math.PI);
            InvalidateVisual();
        };
        _pulseTimer.Start();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsurePulseTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _pulseTimer?.Stop();
        _pulseTimer = null;
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

    // ── View helpers ─────────────────────────────────────────────────────────

    private void ResetView()
    {
        _scale   = 1.0;
        _offsetX = 0;
        _offsetY = 0;
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

    // ── NodeKind → visual ────────────────────────────────────────────────────

    private static Color KindColor(NodeKind kind) => kind switch
    {
        NodeKind.Primary    => Color.FromRgb(59,  130, 246),
        NodeKind.Internal   => Color.FromRgb(34,  197, 94),
        NodeKind.External   => Color.FromRgb(100, 160, 220),
        NodeKind.Dependency => Color.FromRgb(249, 115, 22),
        NodeKind.Tracker    => Color.FromRgb(239, 68,  68),
        NodeKind.Cdn        => Color.FromRgb(6,   182, 212),
        NodeKind.Api        => Color.FromRgb(234, 179, 8),
        NodeKind.Suspicious => Color.FromRgb(168, 85,  247),
        _                   => Color.FromRgb(100, 160, 220)
    };

    private static double VisualRadius(NavigationNode n) => n.Kind switch
    {
        NodeKind.Primary => 11,
        _                => 6
    };

    private static bool IsFastPulse(NodeKind k) =>
        k == NodeKind.Tracker || k == NodeKind.Suspicious;

    private static bool HasDashedEdge(NodeKind k) =>
        k == NodeKind.Dependency || k == NodeKind.Tracker || k == NodeKind.Suspicious;

    // ── Render ───────────────────────────────────────────────────────────────

    // Aresta cadeia principal (azul tracejado)
    private static readonly Pen ChainEdgePen = new(
        new SolidColorBrush(Color.FromArgb(210, 59, 130, 246)), 2.0)
        { DashStyle = DashStyle.Dash };

    // Aresta de navegação por clique de órbita (branco brilhante, sólido, mais grosso)
    private static readonly Pen NavEdgePen = new(
        new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 2.2);

    private static readonly Typeface MonoFont = new("Courier New");
    private static readonly IBrush   BgBrush  = new SolidColorBrush(Color.FromRgb(11, 16, 32));
    private static readonly IBrush   GridBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));

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
            // ── 1. Arestas de órbita (mais ao fundo) ─────────────────────────
            foreach (var node in nodes)
            {
                if (!node.IsPrimary) continue;

                foreach (var orbit in node.OrbitNodes)
                {
                    var   ec    = KindColor(orbit.Kind);
                    var   eb    = new SolidColorBrush(Color.FromArgb(80, ec.R, ec.G, ec.B));
                    var   dash  = HasDashedEdge(orbit.Kind) ? DashStyle.Dash : DashStyle.Dot;
                    var   pen   = new Pen(eb, 0.8) { DashStyle = dash };
                    ctx.DrawLine(pen,
                        new Point(node.X, node.Y),
                        new Point(orbit.X, orbit.Y));
                }
            }

            // ── 2. Arestas de navegação (cadeia principal + clique em órbita) ─
            foreach (var node in nodes)
            {
                if (!node.IsPrimary || node.Next == null) continue;

                // Se o filho tem Parent != este nó primário (veio de clique em órbita),
                // desenha com NavEdgePen (branco sólido); senão usa ChainEdgePen.
                bool viaOrbit = node.Next.Parent != null && !node.Next.Parent.IsPrimary;
                var  edgePen  = viaOrbit ? NavEdgePen : ChainEdgePen;

                // Ponto de origem: o nó clicado (Parent do filho) ou este primário
                double fromX = viaOrbit && node.Next.Parent != null ? node.Next.Parent.X : node.X;
                double fromY = viaOrbit && node.Next.Parent != null ? node.Next.Parent.Y : node.Y;

                ctx.DrawLine(edgePen,
                    new Point(fromX, fromY),
                    new Point(node.Next.X, node.Next.Y));

                // Seta na ponta (triângulo pequeno apontando pro filho)
                DrawArrow(ctx, edgePen,
                    new Point(fromX, fromY),
                    new Point(node.Next.X, node.Next.Y));
            }

            // ── 3. Nós + labels ──────────────────────────────────────────────
            foreach (var node in nodes)
            {
                var    color = KindColor(node.Kind);
                double r     = VisualRadius(node);

                // Pulso animado
                double pulseT  = IsFastPulse(node.Kind)
                    ? Math.Abs(Math.Sin(_pulsePhase * 2.8))
                    : Math.Abs(Math.Sin(_pulsePhase));

                double haloBase = r + (node.IsPrimary ? 10 : 6);
                double haloR    = haloBase + pulseT * (node.IsPrimary ? 7 : 3);
                byte   haloA    = node.IsPrimary ? (byte)38 : (byte)18;

                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(haloA, color.R, color.G, color.B)),
                    null, new Point(node.X, node.Y), haloR, haloR);

                ctx.DrawEllipse(
                    new SolidColorBrush(color), null,
                    new Point(node.X, node.Y), r, r);

                DrawLabel(ctx, node, color, r);
            }
        }

        DrawLegend(ctx, bounds);
        DrawHud(ctx, bounds);
    }

    // ── Seta na ponta da aresta de navegação ──────────────────────────────────

    private static void DrawArrow(DrawingContext ctx, Pen pen, Point from, Point to)
    {
        double dx    = to.X - from.X;
        double dy    = to.Y - from.Y;
        double len   = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double ux    = dx / len;
        double uy    = dy / len;

        // Recua a ponta até a superfície do nó destino
        double nodeR = 13;
        double tx    = to.X - ux * nodeR;
        double ty    = to.Y - uy * nodeR;

        double arrowLen = 10;
        double arrowW   = 5;

        // Base do triângulo
        double bx1 = tx - ux * arrowLen - uy * arrowW;
        double by1 = ty - uy * arrowLen + ux * arrowW;
        double bx2 = tx - ux * arrowLen + uy * arrowW;
        double by2 = ty - uy * arrowLen - ux * arrowW;

        var geom = new StreamGeometry();
        using (var sg = geom.Open())
        {
            sg.BeginFigure(new Point(tx, ty), true);
            sg.LineTo(new Point(bx1, by1));
            sg.LineTo(new Point(bx2, by2));
            sg.EndFigure(true);
        }

        ctx.DrawGeometry(pen.Brush, null, geom);
    }

    // ── Label ────────────────────────────────────────────────────────────────

    private static void DrawLabel(DrawingContext ctx, NavigationNode node, Color color, double r)
    {
        var    text     = ShortenUrl(node.Url);
        double fontSize = node.IsPrimary ? 11.0 : 8.0;
        var    brush    = new SolidColorBrush(Color.FromArgb(215, color.R, color.G, color.B));

        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoFont, fontSize, brush);

        // Posição: primário sempre acima; órbita acima ou abaixo dependendo de onde está
        double lx = node.X - ft.Width / 2;
        double ly = node.IsPrimary
            ? node.Y - r - 15
            : node.Y < node.OrbitParentY
                ? node.Y - r - 12
                : node.Y + r + 3;

        // Fundo do label
        ctx.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(175, 11, 16, 32)),
            null,
            new Rect(lx - 3, ly - 1, ft.Width + 6, ft.Height + 2),
            3, 3);

        ctx.DrawText(ft, new Point(lx, ly));
    }

    // ── Grid ─────────────────────────────────────────────────────────────────

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

    // ── HUD / Hint ───────────────────────────────────────────────────────────

    private static void DrawHint(DrawingContext ctx, Rect bounds)
    {
        var ft = new FormattedText(
            "Navegue para uma URL para ver o grafo aparecer aqui",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Courier New"), 13,
            new SolidColorBrush(Color.FromArgb(60, 200, 220, 255)));

        ctx.DrawText(ft, new Point(
            bounds.Width  / 2 - ft.Width  / 2,
            bounds.Height / 2 - ft.Height / 2));
    }

    private static void DrawHud(DrawingContext ctx, Rect bounds)
    {
        var ft = new FormattedText(
            "scroll = zoom  •  drag fundo = pan  •  drag nó = mover  •  clique nó = navegar  •  R = reset  •  F = fit",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Courier New"), 9,
            new SolidColorBrush(Color.FromArgb(45, 200, 220, 255)));

        ctx.DrawText(ft, new Point(12, bounds.Height - ft.Height - 8));
    }

    // ── Legenda ───────────────────────────────────────────────────────────────

    private static void DrawLegend(DrawingContext ctx, Rect bounds)
    {
        var items = new (NodeKind kind, string label)[]
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

        double x  = bounds.Width - 140;
        double y  = 14;

        foreach (var (kind, label) in items)
        {
            var color = KindColor(kind);
            ctx.DrawEllipse(
                new SolidColorBrush(color), null,
                new Point(x, y + 4), 4, 4);

            var ft = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Courier New"), 9,
                new SolidColorBrush(Color.FromArgb(155, color.R, color.G, color.B)));

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