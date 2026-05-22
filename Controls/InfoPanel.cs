using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Spectrvm.Models;

namespace Spectrvm.Controls;

/// <summary>
/// Painel lateral flutuante sobre o GraphCanvas.
/// — Redimensionável horizontalmente via drag na borda esquerda
/// — Scrollável verticalmente (wheel do mouse)
/// — Atualiza via StyledProperty + OnPropertyChanged (funciona com binding AXAML)
/// </summary>
public class InfoPanel : Control
{
    // ── Propriedades ─────────────────────────────────────────────────────────

    public static readonly StyledProperty<BrowserResult?> ResultProperty =
        AvaloniaProperty.Register<InfoPanel, BrowserResult?>(nameof(Result));

    public BrowserResult? Result
    {
        get => GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    // Reage a qualquer mudança na StyledProperty (inclui binding AXAML)
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ResultProperty)
        {
            _scrollY = 0; // reseta scroll ao trocar de página
            InvalidateVisual();
        }
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private double _panelW    = 290;
    private const double MinW = 180;
    private const double MaxW = 520;
    private const double Margin = 14;
    private const double ResizeHitZone = 8; // px da borda esquerda do painel

    // Scroll vertical
    private double _scrollY   = 0;
    private double _contentH  = 0; // altura total do conteúdo renderizado

    // Drag de resize
    private bool   _resizing     = false;
    private double _resizeStartX = 0;
    private double _resizeStartW = 0;

    // ── Brushes ──────────────────────────────────────────────────────────────

    private static readonly ImmutableSolidColorBrush BgPanel   = new(Color.FromArgb(215, 7,  12, 22));
    private static readonly ImmutableSolidColorBrush BgSection = new(Color.FromArgb(130, 14, 24, 44));
    private static readonly ImmutableSolidColorBrush BorderBrush = new(Color.FromArgb(55, 79, 195, 247));
    private static readonly ImmutableSolidColorBrush ResizeHover = new(Color.FromArgb(90, 79, 195, 247));

    private static readonly ImmutableSolidColorBrush ColGood    = new(Color.FromRgb(34,  197, 94));
    private static readonly ImmutableSolidColorBrush ColWarning = new(Color.FromRgb(234, 179, 8));
    private static readonly ImmutableSolidColorBrush ColBad     = new(Color.FromRgb(239, 68,  68));
    private static readonly ImmutableSolidColorBrush ColInfo    = new(Color.FromRgb(100, 160, 220));
    private static readonly ImmutableSolidColorBrush ColLabel   = new(Color.FromArgb(160, 79, 195, 247));
    private static readonly ImmutableSolidColorBrush ColText    = new(Color.FromArgb(200, 176, 200, 232));
    private static readonly ImmutableSolidColorBrush ColDim     = new(Color.FromArgb(100, 120, 150, 180));
    private static readonly ImmutableSolidColorBrush ColAccent  = new(Color.FromRgb(79, 195, 247));
    private static readonly ImmutableSolidColorBrush ScrollBarBg   = new(Color.FromArgb(40,  79, 195, 247));
    private static readonly ImmutableSolidColorBrush ScrollBarThumb = new(Color.FromArgb(110, 79, 195, 247));

    private static readonly Pen BorderPen      = new(BorderBrush, 1.0);
    private static readonly Pen ResizeCursorPen = new(ResizeHover, 2.0);

    private static readonly Typeface Mono     = new("Courier New");
    private static readonly Typeface MonoBold  = new("Courier New", FontStyle.Normal, FontWeight.Bold);

    // hover na borda de resize
    private bool _hoverResize = false;

    // ── Input ────────────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Precisa receber eventos de ponteiro mesmo com IsHitTestVisible=False no AXAML
        // O AXAML define IsHitTestVisible=False para o grafo não ser bloqueado,
        // mas o InfoPanel precisa ser True para receber resize/scroll.
        // A solução é deixar IsHitTestVisible=True no InfoPanel e usar
        // e.Handled=false nos eventos que não são do painel.
        IsHitTestVisible = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        if (!IsInsidePanel(pos)) return;

        _scrollY = Math.Clamp(_scrollY - e.Delta.Y * 24,
            0, Math.Max(0, _contentH - VisibleHeight()));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (IsOnResizeEdge(pos))
        {
            _resizing     = true;
            _resizeStartX = pos.X;
            _resizeStartW = _panelW;
            e.Handled = true;
        }
        else if (IsInsidePanel(pos))
        {
            e.Handled = true; // consome clique dentro do painel
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_resizing)
        {
            // Arrastar borda esquerda → aumenta/diminui o painel
            double delta = _resizeStartX - pos.X;
            _panelW = Math.Clamp(_resizeStartW + delta, MinW, MaxW);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        bool onEdge = IsOnResizeEdge(pos);
        if (onEdge != _hoverResize)
        {
            _hoverResize = onEdge;
            Cursor = onEdge ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
            InvalidateVisual();
        }

        // Não consome o evento fora do painel → GraphCanvas recebe normalmente
        if (IsInsidePanel(pos)) e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_resizing)
        {
            _resizing = false;
            e.Handled = true;
        }
    }

    // ── Helpers de hit ────────────────────────────────────────────────────────

    private double PanelLeft() => Bounds.Width - _panelW - Margin;

    private bool IsInsidePanel(Point p) =>
        p.X >= PanelLeft() && p.X <= Bounds.Width - Margin &&
        p.Y >= Margin && p.Y <= Margin + VisibleHeight();

    private bool IsOnResizeEdge(Point p) =>
        Math.Abs(p.X - PanelLeft()) <= ResizeHitZone &&
        p.Y >= Margin && p.Y <= Margin + VisibleHeight();

    private double VisibleHeight() => Math.Min(Bounds.Height - Margin * 2, _contentH > 0 ? _contentH : 600);

    // ── Render ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var r = Result;
        if (r == null) return;

        double panelX  = PanelLeft();
        double panelY  = Margin;
        double visH    = Math.Min(Bounds.Height - Margin * 2, EstimateHeight(r));

        // Clip do painel para não vazar fora
        var clipRect = new Rect(panelX, panelY, _panelW, visH);
        ctx.DrawRectangle(BgPanel, BorderPen, clipRect, 10, 10);

        // Indicador de resize na borda esquerda
        if (_hoverResize || _resizing)
        {
            ctx.DrawLine(ResizeCursorPen,
                new Point(panelX + 1, panelY + 20),
                new Point(panelX + 1, panelY + visH - 20));
        }

        using (ctx.PushClip(clipRect))
        {
            double cx = panelX + 12;
            double cy = panelY + 12 - _scrollY;
            double innerW = _panelW - 24;

            // ── TECHNOLOGIES ────────────────────────────────────────────────
            cy = DrawSectionHeader(ctx, cx, cy, innerW, "⚙  TECNOLOGIAS", r.Technologies.Count);

            if (r.Technologies.Count == 0)
            {
                cy = DrawDimText(ctx, cx + 4, cy, "nenhuma detectada");
            }
            else
            {
                foreach (var group in r.Technologies.GroupBy(t => t.Category).OrderBy(g => g.Key))
                {
                    cy = DrawCategoryLabel(ctx, cx + 4, cy, group.Key);
                    foreach (var tech in group)
                        cy = DrawTechRow(ctx, cx + 4, cy, innerW - 8, tech);
                }
            }

            cy += 8;

            // ── SECURITY HEADERS ────────────────────────────────────────────
            int badCount  = r.SecurityHeaders.Count(h => h.Level == SecurityLevel.Bad);
            int warnCount = r.SecurityHeaders.Count(h => h.Level == SecurityLevel.Warning);
            string badge  = badCount > 0 ? $"⛔ {badCount} erro"
                          : warnCount > 0 ? $"⚠ {warnCount} aviso"
                          : "✓ ok";

            cy = DrawSectionHeader(ctx, cx, cy, innerW, "🔒  SEGURANÇA", -1, badge,
                badCount > 0 ? ColBad : warnCount > 0 ? ColWarning : ColGood);

            foreach (var h in r.SecurityHeaders)
                cy = DrawSecRow(ctx, cx + 4, cy, innerW - 8, h);

            cy += 8;

            // ── SUBDOMAINS — apenas contador (os nós estão no grafo) ─────────
            cy = DrawSectionHeader(ctx, cx, cy, innerW, "🌐  SUBDOMÍNIOS", r.Subdomains.Count);
            if (r.Subdomains.Count == 0)
                cy = DrawDimText(ctx, cx + 4, cy, "nenhum encontrado via crt.sh");
            else
                cy = DrawDimText(ctx, cx + 4, cy, "exibidos como nós no grafo ↗");

            // Guarda altura total do conteúdo para scroll
            _contentH = (cy + _scrollY) - panelY + 12;
        }

        // ── Scrollbar ────────────────────────────────────────────────────────
        DrawScrollbar(ctx, panelX, panelY, visH);
    }

    // ── Scrollbar ────────────────────────────────────────────────────────────

    private void DrawScrollbar(DrawingContext ctx, double px, double py, double visH)
    {
        if (_contentH <= visH) return;

        const double sbW  = 3;
        double sbX   = px + _panelW - sbW - 4;
        double ratio = visH / _contentH;
        double thumbH = Math.Max(20, visH * ratio);
        double thumbY = py + (_scrollY / _contentH) * visH;

        ctx.DrawRectangle(ScrollBarBg,    null, new Rect(sbX, py,     sbW, visH),   2, 2);
        ctx.DrawRectangle(ScrollBarThumb, null, new Rect(sbX, thumbY, sbW, thumbH), 2, 2);
    }

    // ── Helpers de desenho ────────────────────────────────────────────────────

    private static double DrawSectionHeader(DrawingContext ctx,
        double x, double y, double w, string title, int count,
        string? badge = null, ImmutableSolidColorBrush? badgeColor = null)
    {
        ctx.DrawRectangle(BgSection, null, new Rect(x - 4, y - 2, w + 8, 20), 4, 4);

        var ft = MakeText(title, 9, ColAccent, bold: true);
        ctx.DrawText(ft, new Point(x, y));

        if (badge != null)
        {
            var bft = MakeText(badge, 8, badgeColor ?? ColInfo);
            ctx.DrawText(bft, new Point(x + w - bft.Width, y + 1));
        }
        else if (count >= 0)
        {
            var cft = MakeText(count.ToString(), 8, ColDim);
            ctx.DrawText(cft, new Point(x + w - cft.Width, y + 1));
        }

        return y + 22;
    }

    private static double DrawCategoryLabel(DrawingContext ctx, double x, double y, string label)
    {
        ctx.DrawText(MakeText(label.ToUpperInvariant(), 7, ColLabel), new Point(x, y));
        return y + 13;
    }

    private static double DrawTechRow(DrawingContext ctx, double x, double y, double w, DetectedTechnology tech)
    {
        ctx.DrawText(MakeText(tech.Icon, 10, ColText),            new Point(x,      y - 1));
        ctx.DrawText(MakeText(tech.Name, 10, ColText, bold: true), new Point(x + 16, y));
        y += 13;
        if (!string.IsNullOrEmpty(tech.Evidence))
        {
            ctx.DrawText(MakeText(Truncate(tech.Evidence, (int)(w / 5.5)), 7, ColDim), new Point(x + 16, y));
            y += 12;
        }
        return y + 2;
    }

    private static double DrawSecRow(DrawingContext ctx, double x, double y, double w, SecurityHeaderResult h)
    {
        var color = h.Level switch
        {
            SecurityLevel.Good    => ColGood,
            SecurityLevel.Warning => ColWarning,
            SecurityLevel.Bad     => ColBad,
            _                     => ColInfo
        };
        var dot = h.Level switch
        {
            SecurityLevel.Good    => "●",
            SecurityLevel.Warning => "▲",
            SecurityLevel.Bad     => "✖",
            _                     => "·"
        };
        ctx.DrawText(MakeText(dot, 8, color), new Point(x, y + 1));
        ctx.DrawText(MakeText(Truncate(h.Description, (int)(w / 5.2)), 8, ColText), new Point(x + 10, y));
        return y + 13;
    }

    private static double DrawDimText(DrawingContext ctx, double x, double y, string text)
    {
        ctx.DrawText(MakeText(text, 8, ColDim), new Point(x, y));
        return y + 13;
    }

    private static double EstimateHeight(BrowserResult r)
    {
        double h = 12;
        h += 22;
        if (r.Technologies.Count == 0) h += 13;
        else
        {
            h += r.Technologies.GroupBy(t => t.Category).Count() * 13;
            foreach (var t in r.Technologies)
                h += string.IsNullOrEmpty(t.Evidence) ? 15 : 27;
        }
        h += 8 + 22 + r.SecurityHeaders.Count * 13 + 8;
        h += 22 + 13; // subdomains (só 1 linha de info)
        return h + 12;
    }

    private static FormattedText MakeText(string text, double size,
        ImmutableSolidColorBrush brush, bool bold = false)
        => new(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? MonoBold : Mono,
            size, brush);

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}