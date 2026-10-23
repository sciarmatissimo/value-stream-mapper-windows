// Flow arrows: push (striped), material flow, withdrawal (pull), manual and
// electronic information, and kanban paths.

using Avalonia;
using Avalonia.Media;
using VsmCore;
using VsmCore.Simulation;

namespace VsmApp;

public static class ConnectionPainter
{
    public static void Draw(DrawingContext ctx, VsmConnection c, ConnectionGeometry g, bool selected)
    {
        var color = selected ? Ui.Accent : (c.Kind.IsInformation() ? Ui.Info : Ui.Ink);
        var start = new Point(g.StartX, g.StartY); var end = new Point(g.EndX, g.EndY);
        switch (c.Kind)
        {
            case ConnectionKind.Push: Striped(ctx, g, color); break;
            case ConnectionKind.MaterialFlow:
                ctx.DrawLine(Ui.Pen(color, 2.4), start, end);
                Head(ctx, end, g.Angle, color, 11, true); break;
            case ConnectionKind.Withdrawal: Withdrawal(ctx, g, color); break;
            case ConnectionKind.ManualInfo:
                ctx.DrawLine(Ui.Pen(color, 1.3), start, end);
                Head(ctx, end, g.Angle, color, 8, false); break;
            case ConnectionKind.ElectronicInfo: Lightning(ctx, g, color); break;
            default:
                ctx.DrawLine(Ui.Pen(color, 1.2, new[] { 5.0, 4.0 }), start, end);
                Head(ctx, end, g.Angle, color, 8, false);
                KanbanGlyph(ctx, c.Kind, new Point(g.MidX, g.MidY), g.Angle, color); break;
        }
        if (!string.IsNullOrEmpty(c.Label))
            Ui.DrawCentered(ctx, c.Label, new Point(g.MidX, g.MidY - 12), 8, FontWeight.Medium, color);
    }

    private static Point Rot(Point origin, double angle, double x, double y) =>
        new(origin.X + x * Math.Cos(angle) - y * Math.Sin(angle), origin.Y + x * Math.Sin(angle) + y * Math.Cos(angle));

    private static void Head(DrawingContext ctx, Point tip, double angle, IBrush color, double size, bool filled)
    {
        var a1 = angle + Math.PI - 0.42; var a2 = angle + Math.PI + 0.42;
        var p1 = new Point(tip.X + Math.Cos(a1) * size, tip.Y + Math.Sin(a1) * size);
        var p2 = new Point(tip.X + Math.Cos(a2) * size, tip.Y + Math.Sin(a2) * size);
        var geo = new StreamGeometry();
        using (var g = geo.Open()) { g.BeginFigure(p1, filled); g.LineTo(tip); g.LineTo(p2); g.EndFigure(filled); }
        if (filled) ctx.DrawGeometry(color, null, geo); else ctx.DrawGeometry(null, Ui.Pen(color, 1.4), geo);
    }

    private static void Striped(DrawingContext ctx, ConnectionGeometry g, IBrush color)
    {
        const double width = 9, headLen = 14;
        var len = Math.Max(g.Length - headLen, 1);
        var o = new Point(g.StartX, g.StartY); var a = g.Angle;
        var body = new StreamGeometry();
        using (var b = body.Open())
        {
            b.BeginFigure(Rot(o, a, 0, -width / 2), false); b.LineTo(Rot(o, a, len, -width / 2)); b.LineTo(Rot(o, a, len, width / 2)); b.LineTo(Rot(o, a, 0, width / 2)); b.EndFigure(true);
        }
        ctx.DrawGeometry(null, Ui.Pen(color, 1.3), body);
        var head = new StreamGeometry();
        using (var h = head.Open())
        {
            h.BeginFigure(Rot(o, a, len, -width), true); h.LineTo(Rot(o, a, len + headLen, 0)); h.LineTo(Rot(o, a, len, width)); h.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)), Ui.Pen(color, 1.3), head);
        var pen = Ui.Pen(color, 1.1);
        for (double x = 6; x < len - 3; x += 8) ctx.DrawLine(pen, Rot(o, a, x, -width / 2), Rot(o, a, x, width / 2));
    }

    private static void Withdrawal(DrawingContext ctx, ConnectionGeometry g, IBrush color)
    {
        var start = new Point(g.StartX, g.StartY); var end = new Point(g.EndX, g.EndY);
        var nx = -Math.Sin(g.Angle); var ny = Math.Cos(g.Angle);
        var bow = -Math.Max(18, g.Length * 0.12);
        var control = new Point(g.MidX + nx * bow, g.MidY + ny * bow);
        var geo = new StreamGeometry();
        using (var p = geo.Open()) { p.BeginFigure(start, false); p.QuadraticBezierTo(control, end); p.EndFigure(false); }
        ctx.DrawGeometry(null, Ui.Pen(color, 1.8), geo);
        Head(ctx, end, Math.Atan2(end.Y - control.Y, end.X - control.X), color, 10, true);
        ctx.DrawEllipse(color, null, start, 2.5, 2.5);
    }

    private static void Lightning(DrawingContext ctx, ConnectionGeometry g, IBrush color)
    {
        var len = g.Length; if (len <= 8) return;
        const double amp = 6;
        var o = new Point(g.StartX, g.StartY); var a = g.Angle;
        var geo = new StreamGeometry();
        using (var p = geo.Open())
        {
            p.BeginFigure(o, false);
            p.LineTo(Rot(o, a, len * 0.42, 0)); p.LineTo(Rot(o, a, len * 0.48, -amp * 1.8)); p.LineTo(Rot(o, a, len * 0.56, amp)); p.LineTo(Rot(o, a, len * 0.62, 0)); p.LineTo(Rot(o, a, len, 0));
            p.EndFigure(false);
        }
        ctx.DrawGeometry(null, Ui.Pen(color, 1.4), geo);
        Head(ctx, new Point(g.EndX, g.EndY), a, color, 8, false);
    }

    private static void KanbanGlyph(DrawingContext ctx, ConnectionKind kind, Point mid, double angle, IBrush color)
    {
        const double w = 16, h = 11;
        var lift = new Point(mid.X + Math.Sin(angle) * 14, mid.Y - Math.Cos(angle) * 14);
        var geo = new StreamGeometry();
        using (var p = geo.Open())
        {
            switch (kind)
            {
                case ConnectionKind.ProductionKanban:
                    p.BeginFigure(new Point(lift.X - w / 2, lift.Y - h / 2), true); p.LineTo(new Point(lift.X + w / 2, lift.Y - h / 2)); p.LineTo(new Point(lift.X + w / 2, lift.Y + h / 2)); p.LineTo(new Point(lift.X - w / 2, lift.Y + h / 2)); break;
                case ConnectionKind.WithdrawalKanban:
                    p.BeginFigure(new Point(lift.X - w / 2, lift.Y - h / 2), true); p.LineTo(new Point(lift.X + w / 2, lift.Y - h / 2)); p.LineTo(new Point(lift.X + w / 2, lift.Y + h / 6)); p.LineTo(new Point(lift.X + w / 4, lift.Y + h / 2)); p.LineTo(new Point(lift.X - w / 2, lift.Y + h / 2)); break;
                default:
                    p.BeginFigure(new Point(lift.X - w / 2, lift.Y - h / 2), true); p.LineTo(new Point(lift.X + w / 2, lift.Y - h / 2)); p.LineTo(new Point(lift.X, lift.Y + h / 2 + 2)); break;
            }
            p.EndFigure(true);
        }
        ctx.DrawGeometry(Ui.Paper, Ui.Pen(color, 1.2), geo);
        ctx.DrawLine(Ui.Pen(color, 0.8, new[] { 2.0, 2.0 }), mid, lift);
    }
}
