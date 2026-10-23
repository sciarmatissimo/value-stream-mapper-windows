// Shared brushes, pens and text helpers for custom-drawn controls.

using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace VsmApp;

public static class Ui
{
    public static readonly Color AccentColor = Color.FromRgb(19, 107, 194);
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentSoft = new SolidColorBrush(Color.FromArgb(36, 19, 107, 194));
    public static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(28, 28, 30));
    public static readonly IBrush InkSecondary = new SolidColorBrush(Color.FromRgb(110, 110, 115));
    public static readonly IBrush Paper = Brushes.White;
    public static readonly IBrush CanvasBackground = new SolidColorBrush(Color.FromRgb(242, 242, 247));
    public static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0));
    public static readonly IBrush Info = new SolidColorBrush(Color.FromRgb(0, 122, 255));
    public static readonly IBrush Material = new SolidColorBrush(Color.FromRgb(255, 149, 0));
    public static readonly IBrush Danger = new SolidColorBrush(Color.FromRgb(255, 59, 48));
    public static readonly IBrush Success = new SolidColorBrush(Color.FromRgb(52, 199, 89));
    public static readonly IBrush InventoryFill = new SolidColorBrush(Color.FromArgb(64, 255, 204, 0));
    public static readonly IBrush BufferFill = new SolidColorBrush(Color.FromArgb(46, 255, 149, 0));
    public static readonly IBrush KaizenFill = new SolidColorBrush(Color.FromArgb(20, 255, 59, 48));

    public static readonly FontFamily Font = new("fonts:Inter#Inter, Segoe UI, Helvetica, Arial");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    public static Pen Pen(IBrush brush, double thickness = 1.5, double[]? dashes = null, PenLineCap cap = PenLineCap.Round) =>
        new(brush, thickness, dashes == null ? null : new DashStyle(dashes, 0), cap, PenLineJoin.Round);

    public static FormattedText Text(string text, double size, FontWeight weight = FontWeight.Normal, IBrush? brush = null,
        TextAlignment align = TextAlignment.Left, double maxWidth = double.PositiveInfinity, FontFamily? family = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family ?? Font, FontStyle.Normal, weight), size, brush ?? Ink)
        {
            TextAlignment = align,
        };
        if (!double.IsInfinity(maxWidth)) ft.MaxTextWidth = maxWidth;
        return ft;
    }

    /// <summary>Draws text centred on a point.</summary>
    public static void DrawCentered(DrawingContext ctx, string text, Point center, double size,
        FontWeight weight = FontWeight.Normal, IBrush? brush = null, double maxWidth = double.PositiveInfinity)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = Text(text, size, weight, brush, TextAlignment.Center, maxWidth);
        var w = double.IsInfinity(maxWidth) ? ft.Width : maxWidth;
        ctx.DrawText(ft, new Point(center.X - w / 2, center.Y - ft.Height / 2));
    }

    public static void DrawChip(DrawingContext ctx, string text, Point center, IBrush bg, IBrush fg, double size = 10)
    {
        var ft = Text(text, size, FontWeight.Bold, fg);
        var rect = new Rect(center.X - ft.Width / 2 - 6, center.Y - ft.Height / 2 - 3, ft.Width + 12, ft.Height + 6);
        ctx.DrawRectangle(bg, null, rect, rect.Height / 2, rect.Height / 2);
        ctx.DrawText(ft, new Point(rect.X + 6, rect.Y + 3));
    }
}
