using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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

    public Action<string>? OnNodeClicked { get; set; }

    static GraphCanvas()
    {
        NodesProperty.Changed.AddClassHandler<GraphCanvas>((c, _) => c.InvalidateVisual());
        FocusableProperty.OverrideDefaultValue<GraphCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCanvas>(true);
    }

    // ── Estado do viewport ───────────────────────────────────────────────────

    private double _scale    = 1.0;
    private double _offsetX  = 0;
    private double _offsetY  = 0;

    private const double MinScale = 0.08;
    private const double MaxScale = 5.0;

    // Estado do drag
    private bool   _isDragging;
    private bool   _isDraggingNode;
    private Point  _dragStart;           // posição do mouse ao iniciar drag (screen)
    private double _offsetXAtDragStart;
    private double _offsetYAtDragStart;

    private NavigationNode? _draggedNode;
    private double _nodeXAtDragStart;
    private double _nodeYAtDragStart;

    // ── Conversões screen ↔ world ────────────────────────────────────────────

    private Point ToWorld(Point screen) => new(
        (screen.X - _offsetX) / _scale,
        (screen.Y - _offsetY) / _scale);

    private Point ToScreen(double worldX, double worldY) => new(
        worldX * _scale + _offsetX,
        worldY * _scale + _offsetY);

    // ── Input ────────────────────────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var mouse   = e.GetPosition(this);
        var factor  = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        var newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);

        // Mantém o ponto do mundo sob o cursor fixo
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

        var world = ToWorld(pos);
        var hit   = HitTest(world);

        if (hit != null)
        {
            // Inicia drag de nó
            _isDraggingNode      = true;
            _draggedNode         = hit;
            _dragStart           = pos;
            _nodeXAtDragStart    = hit.X;
            _nodeYAtDragStart    = hit.Y;
        }
        else
        {
            // Inicia pan do canvas
            _isDragging          = true;
            _dragStart           = pos;
            _offsetXAtDragStart  = _offsetX;
            _offsetYAtDragStart  = _offsetY;
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

        // Se o drag foi mínimo (< 5px) e havia um nó, considera clique
        if (_isDraggingNode && _draggedNode != null)
        {
            var dist = pos - _dragStart;
            if (Math.Abs(dist.X) < 5 && Math.Abs(dist.Y) < 5)
                OnNodeClicked?.Invoke(_draggedNode.Url);
        }

        _isDragging     = false;
        _isDraggingNode = false;
        _draggedNode    = null;
        e.Handled       = true;
    }

    // Teclas: R = reset view, F = fit all nodes
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.R)      { ResetView();  e.Handled = true; }
        else if (e.Key == Key.F) { FitAll();     e.Handled = true; }
    }

    // ── HitTest ──────────────────────────────────────────────────────────────

    private NavigationNode? HitTest(Point world)
    {
        var nodes = Nodes;
        if (nodes == null) return null;

        // Hitbox em world-space (independente do zoom)
        foreach (var node in nodes)
        {
            double r  = node.IsPrimary ? 16 : 12;
            double dx = world.X - node.X;
            double dy = world.Y - node.Y;
            if (dx * dx + dy * dy <= r * r) return node;
        }
        return null;
    }

    // ── Utilitários de view ──────────────────────────────────────────────────

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

        double pad    = 80;
        double graphW = maxX - minX + pad * 2;
        double graphH = maxY - minY + pad * 2;
        double w      = Bounds.Width  > 0 ? Bounds.Width  : 1200;
        double h      = Bounds.Height > 0 ? Bounds.Height : 800;

        _scale   = Math.Clamp(Math.Min(w / graphW, h / graphH), MinScale, MaxScale);
        _offsetX = (w - graphW * _scale) / 2 - (minX - pad) * _scale;
        _offsetY = (h - graphH * _scale) / 2 - (minY - pad) * _scale;

        InvalidateVisual();
    }

    // ── Render ───────────────────────────────────────────────────────────────

    private static readonly Pen    PrimaryEdgePen = new(new SolidColorBrush(Color.FromArgb(180, 30, 144, 255)), 1.5) { DashStyle = DashStyle.Dash };
    private static readonly Pen    OrbitEdgePen   = new(new SolidColorBrush(Color.FromArgb(100, 50, 205, 50)), 1);
    private static readonly IBrush PrimaryFill    = new SolidColorBrush(Color.FromRgb(30, 144, 255));
    private static readonly IBrush OrbitFill      = new SolidColorBrush(Color.FromRgb(50, 205, 50));
    private static readonly IBrush BgBrush        = new SolidColorBrush(Color.FromRgb(6, 10, 15));
    private static readonly IBrush GridBrush      = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    private static readonly Typeface MonoFont     = new("Courier New");

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = Bounds;

        // Fundo
        ctx.DrawRectangle(BgBrush, null, new Rect(bounds.Size));

        // Grid de pontos (estilo Figma)
        DrawGrid(ctx, bounds);

        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
        {
            DrawHint(ctx, bounds);
            return;
        }

        // Tudo dentro do transform do viewport
        var transform = Matrix.CreateScale(_scale, _scale) *
                        Matrix.CreateTranslation(_offsetX, _offsetY);

        using (ctx.PushTransform(transform))
        {
            // Arestas primeiro (abaixo dos nós)
            foreach (var node in nodes)
            {
                if (!node.IsPrimary) continue;

                if (node.Next != null)
                    ctx.DrawLine(PrimaryEdgePen,
                        new Point(node.X, node.Y),
                        new Point(node.Next.X, node.Next.Y));

                foreach (var orbit in node.OrbitNodes)
                    ctx.DrawLine(OrbitEdgePen,
                        new Point(node.X, node.Y),
                        new Point(orbit.X, orbit.Y));
            }

            // Nós + labels
            foreach (var node in nodes)
            {
                double r    = node.IsPrimary ? 10 : 7;
                var    fill = node.IsPrimary ? PrimaryFill : OrbitFill;

                // Halo suave
                ctx.DrawEllipse(
                    new SolidColorBrush(node.IsPrimary
                        ? Color.FromArgb(30, 30, 144, 255)
                        : Color.FromArgb(20, 50, 205, 50)),
                    null,
                    new Point(node.X, node.Y),
                    r + 8, r + 8);

                ctx.DrawEllipse(fill, null, new Point(node.X, node.Y), r, r);

                // Label centralizado, acima ou abaixo dependendo da posição relativa
                var labelText = ShortenUrl(node.Url);
                var label = new FormattedText(
                    labelText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    MonoFont,
                    node.IsPrimary ? 11 : 9,
                    node.IsPrimary
                        ? Brushes.DodgerBlue
                        : (IBrush)new SolidColorBrush(Color.FromArgb(200, 50, 205, 50)));

                double labelX = node.X - label.Width / 2;
                double labelY = node.IsPrimary
                    ? node.Y - r - 16          // primário: sempre acima
                    : node.Y < node.OrbitParentY
                        ? node.Y - r - 14      // órbita acima do pai: label acima
                        : node.Y + r + 4;      // órbita abaixo do pai: label abaixo

                // Fundo do label para legibilidade
                ctx.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(160, 6, 10, 15)),
                    null,
                    new Rect(labelX - 2, labelY - 1, label.Width + 4, label.Height + 2),
                    3, 3);

                ctx.DrawText(label, new Point(labelX, labelY));
            }
        }

        // HUD — instruções no canto
        DrawHud(ctx, bounds);
    }

    private void DrawGrid(DrawingContext ctx, Rect bounds)
    {
        double gridSize = 40 * _scale;
        if (gridSize < 8) return; // muito pequeno, não desenha

        double startX = _offsetX % gridSize;
        double startY = _offsetY % gridSize;

        for (double x = startX; x < bounds.Width; x += gridSize)
            for (double y = startY; y < bounds.Height; y += gridSize)
                ctx.DrawEllipse(GridBrush, null, new Point(x, y), 1, 1);
    }

    private static void DrawHint(DrawingContext ctx, Rect bounds)
    {
        var text = new FormattedText(
            "Navegue para uma URL para ver o grafo aparecer aqui",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Courier New"),
            13,
            new SolidColorBrush(Color.FromArgb(60, 200, 220, 255)));

        ctx.DrawText(text, new Point(
            bounds.Width  / 2 - text.Width  / 2,
            bounds.Height / 2 - text.Height / 2));
    }

    private static void DrawHud(DrawingContext ctx, Rect bounds)
    {
        var hud = new FormattedText(
            "scroll = zoom  •  drag fundo = pan  •  drag nó = mover  •  clique nó = navegar  •  R = reset  •  F = fit",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Courier New"),
            9,
            new SolidColorBrush(Color.FromArgb(50, 200, 220, 255)));

        ctx.DrawText(hud, new Point(12, bounds.Height - hud.Height - 8));
    }

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