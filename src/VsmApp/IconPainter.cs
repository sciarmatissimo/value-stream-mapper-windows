// Vector renderings of every value-stream-mapping icon, drawn into a
// DrawingContext in map coordinates (shared by the canvas, palette and exports).

using Avalonia;
using Avalonia.Media;
using VsmCore;

namespace VsmApp;

public static class IconPainter
{
    private static readonly Pen Stroke = Ui.Pen(Ui.Ink, 1.6);
    private static readonly Pen StrokeThin = Ui.Pen(Ui.Ink, 1.1);
    private static readonly Pen Selected = Ui.Pen(Ui.Accent, 1.5, new[] { 4.0, 3.0 });

    public static void Draw(DrawingContext ctx, VsmElement e, ValueStreamMap map, bool selected)
    {
        var (left, top, w, h) = e.Frame;
        var glyph = new Rect(left, top, w, h - 16);
        switch (e.Kind)
        {
            case ElementKind.Customer or ElementKind.Supplier: Factory(ctx, e, map, glyph); break;
            case ElementKind.Process: Process(ctx, e, glyph); break;
            case ElementKind.DataBox: DataBox(ctx, e, glyph); break;
            case ElementKind.CrossDock or ElementKind.Warehouse: Shed(ctx, e, glyph); break;
            case ElementKind.Inventory: Inventory(ctx, e, map, glyph, false); break;
            case ElementKind.BufferStock or ElementKind.SafetyStock: Inventory(ctx, e, map, glyph, true); break;
            case ElementKind.Supermarket: Supermarket(ctx, e, glyph); break;
            case ElementKind.FifoLane: Fifo(ctx, e, glyph); break;
            case ElementKind.SlaHandoff: Sla(ctx, e, glyph); break;
            case ElementKind.TruckShipment or ElementKind.ExpeditedTransport: Truck(ctx, e, glyph); break;
            case ElementKind.TrainShipment: Train(ctx, e, glyph); break;
            case ElementKind.PlaneShipment: Plane(ctx, e, glyph); break;
            case ElementKind.BoatShipment: Boat(ctx, e, glyph); break;
            case ElementKind.MilkRun: MilkRun(ctx, e, glyph); break;
            case ElementKind.PaperQueue: Tray(ctx, e, glyph, false); break;
            case ElementKind.ElectronicQueue: Tray(ctx, e, glyph, true); break;
            case ElementKind.Delay: Hourglass(ctx, e, glyph); break;
            case ElementKind.ScheduleBox: Box(ctx, e.Name, glyph); break;
            case ElementKind.LoadLeveling: Box(ctx, "O X O X", glyph, FontWeight.Black); break;
            case ElementKind.KanbanPost: KanbanPost(ctx, glyph); break;
            case ElementKind.KanbanBatch: KanbanBatch(ctx, glyph); break;
            case ElementKind.GoSeeScheduling: Glasses(ctx, glyph); break;
            case ElementKind.Phone: Phone(ctx, glyph); break;
            case ElementKind.OperatorIcon: Operator(ctx, glyph); break;
            case ElementKind.KaizenBurst: Kaizen(ctx, e, glyph); break;
            case ElementKind.Milestone: Flag(ctx, glyph); break;
            case ElementKind.IterationRework: Loop(ctx, e, glyph); break;
        }
        if (e.Kind is not (ElementKind.Process or ElementKind.DataBox or ElementKind.ScheduleBox or ElementKind.LoadLeveling))
            Ui.DrawCentered(ctx, e.Name, new Point(e.X, top + h - 8), 9, FontWeight.Medium, Ui.Ink, w + 20);
        if (selected)
            ctx.DrawRectangle(null, Selected, new Rect(left - 5, top - 5, w + 10, h + 10), 6, 6);
    }

    // MARK: Primitives

    private static StreamGeometry Polygon(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], true);
        for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(true);
        return g;
    }

    private static StreamGeometry Polyline(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], false);
        for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(false);
        return g;
    }

    private static StreamGeometry FactoryShape(Rect r)
    {
        var roofH = r.Height * 0.35; var bodyTop = r.Y + roofH; const int teeth = 3; var toothW = r.Width / teeth;
        var pts = new List<Point> { new(r.X, r.Bottom), new(r.X, bodyTop) };
        for (int i = 0; i < teeth; i++)
        {
            var x0 = r.X + i * toothW;
            pts.Add(new Point(x0 + toothW, r.Y));
            pts.Add(new Point(x0 + toothW, bodyTop));
        }
        pts.Add(new Point(r.Right, r.Bottom));
        return Polygon(pts.ToArray());
    }

    private static StreamGeometry Triangle(Rect r) => Polygon(new Point(r.Center.X, r.Y), new Point(r.Right, r.Bottom), new Point(r.X, r.Bottom));

    public static StreamGeometry Burst(Rect r)
    {
        var c = r.Center; const int points = 12; var rx = r.Width / 2; var ry = r.Height / 2;
        var pts = new Point[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            var angle = i / (double)(points * 2) * 2 * Math.PI - Math.PI / 2;
            var s = i % 2 == 0 ? 1.0 : 0.68;
            pts[i] = new Point(c.X + Math.Cos(angle) * rx * s, c.Y + Math.Sin(angle) * ry * s);
        }
        return Polygon(pts);
    }

    private static StreamGeometry ShedShape(Rect r)
    {
        var roofH = r.Height * 0.3;
        return Polygon(new Point(r.X, r.Bottom), new Point(r.X, r.Y + roofH), new Point(r.Center.X, r.Y), new Point(r.Right, r.Y + roofH), new Point(r.Right, r.Bottom));
    }

    // MARK: Glyphs

    private static void Factory(DrawingContext ctx, VsmElement e, ValueStreamMap map, Rect r)
    {
        ctx.DrawGeometry(Ui.Paper, Stroke, FactoryShape(r));
        var y = r.Y + r.Height * 0.42;
        if (!string.IsNullOrEmpty(e.Metrics.Frequency))
        {
            Ui.DrawCentered(ctx, e.Metrics.Frequency, new Point(r.Center.X, y + 6), 8);
            y += 11;
        }
        if (e.Kind == ElementKind.Customer && map.CustomerDemandPerDay > 0)
            Ui.DrawCentered(ctx, VsmFormat.Trim(map.CustomerDemandPerDay) + "/day", new Point(r.Center.X, y + 6), 8, FontWeight.SemiBold);
    }

    private static void Process(DrawingContext ctx, VsmElement e, Rect r)
    {
        var header = new Rect(r.X, r.Y, r.Width, 42);
        ctx.DrawRectangle(Ui.Paper, Stroke, header);
        ctx.DrawRectangle(Ui.AccentSoft, null, new Rect(r.X + 1, r.Y + 1, r.Width - 2, 17));
        Ui.DrawCentered(ctx, e.Name, new Point(r.Center.X, r.Y + 9.5), 10, FontWeight.Bold, Ui.Ink, r.Width - 6);
        if (e.Metrics.Operators is int ops && ops > 0)
        {
            ctx.DrawEllipse(null, StrokeThin, new Point(r.Center.X - 7, r.Y + 30), 4.5, 4.5);
            Ui.DrawCentered(ctx, ops.ToString(), new Point(r.Center.X + 4, r.Y + 30), 9);
        }
        if (e.Metrics.UsesITSystem)
        {
            var badge = new Rect(r.Right - 20, r.Y + 20, 16, 9);
            ctx.DrawRectangle(Ui.Paper, StrokeThin, badge, 1.5, 1.5);
            Ui.DrawCentered(ctx, "IT", badge.Center, 6);
        }
        DataRows(ctx, e, new Rect(r.X, r.Y + 42, r.Width, r.Height - 42));
    }

    private static void DataBox(DrawingContext ctx, VsmElement e, Rect r)
    {
        var header = new Rect(r.X, r.Y, r.Width, 18);
        ctx.DrawRectangle(Ui.Paper, StrokeThin, header);
        Ui.DrawCentered(ctx, e.Name, header.Center, 9, FontWeight.Bold, Ui.Ink, r.Width - 4);
        DataRows(ctx, e, new Rect(r.X, r.Y + 18, r.Width, r.Height - 18));
    }

    private static void DataRows(DrawingContext ctx, VsmElement e, Rect r)
    {
        var m = e.Metrics;
        var rows = new List<(string, string)>();
        if (m.CycleTime is double ct) rows.Add(("C/T", VsmFormat.Seconds(ct)));
        if (m.ChangeoverTime is double co) rows.Add(("C/O", VsmFormat.Seconds(co)));
        if (m.UptimePercent is double up) rows.Add(("Uptime", VsmFormat.Percent(up)));
        if (!string.IsNullOrEmpty(m.Epex)) rows.Add(("EPE", m.Epex));
        if (m.ScrapPercent is double sc) rows.Add(("Scrap", VsmFormat.Percent(sc)));
        if (rows.Count == 0) return;
        var box = new Rect(r.X, r.Y, r.Width, Math.Min(rows.Count, 4) * 11);
        ctx.DrawRectangle(Ui.Paper, StrokeThin, box);
        for (int i = 0; i < Math.Min(rows.Count, 4); i++)
        {
            var y = box.Y + i * 11 + 1.5;
            ctx.DrawText(Ui.Text(rows[i].Item1, 7.5), new Point(box.X + 3, y));
            var val = Ui.Text(rows[i].Item2, 7.5, FontWeight.SemiBold);
            ctx.DrawText(val, new Point(box.Right - 3 - val.Width, y));
        }
    }

    private static void Shed(DrawingContext ctx, VsmElement e, Rect r)
    {
        ctx.DrawGeometry(Ui.Paper, Stroke, ShedShape(r));
        Ui.DrawCentered(ctx, e.Kind == ElementKind.CrossDock ? "X-DOCK" : "WHSE", new Point(r.Center.X, r.Y + r.Height * 0.55), 8, FontWeight.Bold);
        if (e.Metrics.InventoryQty is double q) Ui.DrawCentered(ctx, VsmFormat.Pieces(q), new Point(r.Center.X, r.Y + r.Height * 0.78), 7.5);
    }

    private static void Inventory(DrawingContext ctx, VsmElement e, ValueStreamMap map, Rect r, bool buffer)
    {
        var tri = new Rect(r.X + 10, r.Y, r.Width - 20, r.Height - 24);
        var pen = buffer ? Ui.Pen(Ui.Ink, 1.6, new[] { 4.0, 2.0 }) : Stroke;
        ctx.DrawGeometry(buffer ? Ui.BufferFill : Ui.InventoryFill, pen, Triangle(tri));
        var letter = buffer ? (e.Kind == ElementKind.BufferStock ? "B" : "S") : "I";
        Ui.DrawCentered(ctx, letter, new Point(tri.Center.X, tri.Y + tri.Height * 0.68), buffer ? 12 : 14, FontWeight.Bold);
        if (e.Metrics.InventoryQty is double q)
        {
            Ui.DrawCentered(ctx, VsmFormat.Pieces(q), new Point(r.Center.X, tri.Bottom + 6), 8, FontWeight.SemiBold);
            if (map.InventoryDays(e) is double d)
                Ui.DrawCentered(ctx, VsmFormat.Days(d), new Point(r.Center.X, tri.Bottom + 16), 8, FontWeight.Normal, Ui.InkSecondary);
        }
    }

    private static void Supermarket(DrawingContext ctx, VsmElement e, Rect r)
    {
        var box = new Rect(r.X + 8, r.Y + 4, r.Width - 16, r.Height - 10);
        ctx.DrawRectangle(Ui.Paper, null, box);
        ctx.DrawGeometry(null, Stroke, Polyline(new Point(box.X, box.Y), new Point(box.Right, box.Y), new Point(box.Right, box.Bottom), new Point(box.X, box.Bottom)));
        for (int i = 1; i < 3; i++)
        {
            var y = box.Y + i * box.Height / 3;
            ctx.DrawLine(Stroke, new Point(box.X, y), new Point(box.Right, y));
        }
        if (e.Metrics.InventoryQty is double q) Ui.DrawCentered(ctx, VsmFormat.Pieces(q), box.Center, 8, FontWeight.SemiBold);
    }

    private static void Fifo(DrawingContext ctx, VsmElement e, Rect r)
    {
        var y1 = r.Y + 8; var y2 = r.Bottom - 8;
        ctx.DrawLine(Stroke, new Point(r.X + 4, y1), new Point(r.Right - 4, y1));
        ctx.DrawLine(Stroke, new Point(r.X + 4, y2), new Point(r.Right - 4, y2));
        var label = "FIFO" + (e.Metrics.MaxQuantity is double mq ? $"  max {VsmFormat.Trim(mq)}" : "");
        Ui.DrawCentered(ctx, label, r.Center, 10, FontWeight.Black);
    }

    private static void Sla(DrawingContext ctx, VsmElement e, Rect r)
    {
        var box = new Rect(r.X + 10, r.Y + 6, r.Width - 20, r.Height - 12);
        ctx.DrawRectangle(Ui.Paper, Ui.Pen(Ui.Ink, 1.4, new[] { 5.0, 3.0 }), box, 4, 4);
        Ui.DrawCentered(ctx, "SLA", new Point(box.Center.X, box.Center.Y - (string.IsNullOrEmpty(e.Metrics.Frequency) ? 0 : 6)), 10, FontWeight.Black);
        if (!string.IsNullOrEmpty(e.Metrics.Frequency)) Ui.DrawCentered(ctx, e.Metrics.Frequency, new Point(box.Center.X, box.Center.Y + 8), 8);
    }

    private static void TransportLabel(DrawingContext ctx, VsmElement e, Rect r)
    {
        if (!string.IsNullOrEmpty(e.Metrics.Frequency))
            Ui.DrawCentered(ctx, e.Metrics.Frequency, new Point(r.Center.X, r.Bottom - 6), 8, FontWeight.SemiBold);
    }

    private static void Truck(DrawingContext ctx, VsmElement e, Rect r)
    {
        var pen = e.Kind == ElementKind.ExpeditedTransport ? Ui.Pen(Ui.Danger, 1.6) : Stroke;
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawRectangle(Ui.Paper, pen, new Rect(cx - 22, cy - 11, 28, 18), 2, 2);
        ctx.DrawGeometry(Ui.Paper, pen, Polygon(new Point(cx + 6, cy - 6), new Point(cx + 15, cy - 6), new Point(cx + 21, cy + 1), new Point(cx + 21, cy + 7), new Point(cx + 6, cy + 7)));
        ctx.DrawEllipse(Ui.Paper, pen, new Point(cx - 13, cy + 9), 3.5, 3.5);
        ctx.DrawEllipse(Ui.Paper, pen, new Point(cx + 12, cy + 9), 3.5, 3.5);
        if (e.Kind == ElementKind.ExpeditedTransport)
            for (int i = 0; i < 3; i++) ctx.DrawLine(pen, new Point(cx - 32, cy - 6 + i * 6), new Point(cx - 26, cy - 6 + i * 6));
        TransportLabel(ctx, e, r);
    }

    private static void Train(DrawingContext ctx, VsmElement e, Rect r)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawRectangle(Ui.Paper, Stroke, new Rect(cx - 18, cy - 12, 36, 22), 5, 5);
        ctx.DrawRectangle(null, StrokeThin, new Rect(cx - 13, cy - 7, 10, 7));
        ctx.DrawRectangle(null, StrokeThin, new Rect(cx + 3, cy - 7, 10, 7));
        ctx.DrawEllipse(Ui.Paper, Stroke, new Point(cx - 10, cy + 11), 3, 3);
        ctx.DrawEllipse(Ui.Paper, Stroke, new Point(cx + 10, cy + 11), 3, 3);
        ctx.DrawLine(Stroke, new Point(cx - 4, cy - 12), new Point(cx - 4, cy - 17));
        TransportLabel(ctx, e, r);
    }

    private static void Plane(DrawingContext ctx, VsmElement e, Rect r)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawGeometry(Ui.Paper, Stroke, Polygon(
            new Point(cx - 22, cy), new Point(cx - 4, cy - 4), new Point(cx + 4, cy - 18), new Point(cx + 9, cy - 18), new Point(cx + 6, cy - 4),
            new Point(cx + 20, cy - 2), new Point(cx + 22, cy), new Point(cx + 20, cy + 2), new Point(cx + 6, cy + 4), new Point(cx + 9, cy + 18),
            new Point(cx + 4, cy + 18), new Point(cx - 4, cy + 4)));
        TransportLabel(ctx, e, r);
    }

    private static void Boat(DrawingContext ctx, VsmElement e, Rect r)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawGeometry(Ui.Paper, Stroke, Polygon(new Point(cx - 24, cy + 2), new Point(cx + 24, cy + 2), new Point(cx + 16, cy + 12), new Point(cx - 16, cy + 12)));
        ctx.DrawRectangle(Ui.Paper, Stroke, new Rect(cx - 10, cy - 8, 20, 10));
        ctx.DrawLine(Stroke, new Point(cx + 3, cy - 8), new Point(cx + 3, cy - 18));
        TransportLabel(ctx, e, r);
    }

    private static void MilkRun(DrawingContext ctx, VsmElement e, Rect r)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawGeometry(null, Stroke, Polyline(new Point(cx - 22, cy + 10), new Point(cx - 12, cy - 10), new Point(cx, cy + 6), new Point(cx + 12, cy - 10), new Point(cx + 22, cy + 10)));
        foreach (var p in new[] { new Point(cx - 22, cy + 10), new Point(cx - 12, cy - 10), new Point(cx, cy + 6), new Point(cx + 12, cy - 10), new Point(cx + 22, cy + 10) })
            ctx.DrawEllipse(Ui.Ink, null, p, 2.5, 2.5);
        TransportLabel(ctx, e, r);
    }

    private static void Tray(DrawingContext ctx, VsmElement e, Rect r, bool electronic)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.45;
        ctx.DrawGeometry(Ui.Paper, Stroke, Polygon(new Point(cx - 20, cy - 10), new Point(cx + 20, cy - 10), new Point(cx + 20, cy + 10), new Point(cx - 20, cy + 10)));
        ctx.DrawGeometry(null, Stroke, Polyline(new Point(cx - 20, cy + 2), new Point(cx - 10, cy + 2), new Point(cx - 6, cy + 7), new Point(cx + 6, cy + 7), new Point(cx + 10, cy + 2), new Point(cx + 20, cy + 2)));
        for (int i = 0; i < 3; i++) ctx.DrawLine(StrokeThin, new Point(cx - 14, cy - 6 + i * 3), new Point(cx + 14, cy - 6 + i * 3));
        if (electronic)
            ctx.DrawGeometry(Ui.Material, null, Polygon(new Point(cx + 22, cy - 18), new Point(cx + 16, cy - 7), new Point(cx + 21, cy - 7), new Point(cx + 17, cy + 2), new Point(cx + 27, cy - 10), new Point(cx + 22, cy - 10)));
        TransportLabel(ctx, e, r);
    }

    private static void Hourglass(DrawingContext ctx, VsmElement e, Rect r)
    {
        var cx = r.Center.X; var cy = r.Y + r.Height * 0.42;
        ctx.DrawGeometry(Ui.Paper, Stroke, Polygon(new Point(cx - 12, cy - 14), new Point(cx + 12, cy - 14), new Point(cx + 2, cy), new Point(cx + 12, cy + 14), new Point(cx - 12, cy + 14), new Point(cx - 2, cy)));
        ctx.DrawGeometry(Ui.Ink, null, Polygon(new Point(cx - 7, cy + 13), new Point(cx + 7, cy + 13), new Point(cx, cy + 5)));
        TransportLabel(ctx, e, r);
    }

    private static void Box(DrawingContext ctx, string text, Rect r, FontWeight weight = FontWeight.SemiBold)
    {
        ctx.DrawRectangle(Ui.Paper, Ui.Pen(Ui.Ink, 1.4), r);
        Ui.DrawCentered(ctx, text, r.Center, weight == FontWeight.Black ? 12 : 9, weight, Ui.Ink, r.Width - 8);
    }

    private static void KanbanPost(DrawingContext ctx, Rect r)
    {
        var cx = r.Center.X; var cy = r.Center.Y;
        ctx.DrawGeometry(null, Stroke, Polyline(new Point(cx - 6, cy + 20), new Point(cx - 6, cy - 16), new Point(cx + 20, cy - 16), new Point(cx + 20, cy), new Point(cx - 6, cy)));
        ctx.DrawLine(StrokeThin, new Point(cx + 7, cy - 16), new Point(cx + 7, cy));
    }

    private static void KanbanBatch(DrawingContext ctx, Rect r)
    {
        for (int i = 0; i < 3; i++)
            ctx.DrawRectangle(Ui.Paper, StrokeThin, new Rect(r.Center.X - 13 + i * 7 - 7, r.Center.Y - 9 + i * 6 - 6, 26, 18), 2, 2);
    }

    private static void Glasses(DrawingContext ctx, Rect r)
    {
        var c = new Point(r.Center.X, r.Y + r.Height * 0.45);
        ctx.DrawEllipse(null, Stroke, new Point(c.X - 11, c.Y), 9, 8);
        ctx.DrawEllipse(null, Stroke, new Point(c.X + 11, c.Y), 9, 8);
        ctx.DrawLine(Stroke, new Point(c.X - 2, c.Y - 2), new Point(c.X + 2, c.Y - 2));
        ctx.DrawLine(Stroke, new Point(c.X - 20, c.Y - 3), new Point(c.X - 27, c.Y - 7));
        ctx.DrawLine(Stroke, new Point(c.X + 20, c.Y - 3), new Point(c.X + 27, c.Y - 7));
    }

    private static void Phone(DrawingContext ctx, Rect r)
    {
        var c = new Point(r.Center.X, r.Y + r.Height * 0.45);
        ctx.DrawGeometry(Ui.Ink, null, Polygon(new Point(c.X - 14, c.Y - 14), new Point(c.X - 6, c.Y - 16), new Point(c.X - 2, c.Y - 7), new Point(c.X - 7, c.Y - 3),
            new Point(c.X + 3, c.Y + 7), new Point(c.X + 7, c.Y + 2), new Point(c.X + 16, c.Y + 6), new Point(c.X + 14, c.Y + 14), new Point(c.X + 2, c.Y + 13), new Point(c.X - 13, c.Y - 2)));
    }

    private static void Operator(DrawingContext ctx, Rect r)
    {
        var c = new Point(r.Center.X, r.Y + r.Height * 0.45);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(c.X - 14, c.Y + 6), false);
            g.ArcTo(new Point(c.X + 14, c.Y + 6), new Size(15, 15), 0, true, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, Stroke, arc);
        ctx.DrawEllipse(Ui.Paper, Stroke, c, 6.5, 6.5);
    }

    private static void Kaizen(DrawingContext ctx, VsmElement e, Rect r)
    {
        ctx.DrawGeometry(Ui.KaizenFill, Ui.Pen(Ui.Danger, 1.8), Burst(new Rect(r.X, r.Y, r.Width, r.Height + 8)));
        Ui.DrawCentered(ctx, e.Name, new Point(r.Center.X, r.Center.Y + 4), 8.5, FontWeight.SemiBold, Ui.Ink, r.Width - 30);
    }

    private static void Flag(DrawingContext ctx, Rect r)
    {
        var c = new Point(r.Center.X, r.Y + r.Height * 0.45);
        ctx.DrawLine(Stroke, new Point(c.X - 10, c.Y - 18), new Point(c.X - 10, c.Y + 18));
        ctx.DrawGeometry(Ui.Ink, null, Polygon(new Point(c.X - 10, c.Y - 18), new Point(c.X + 14, c.Y - 11), new Point(c.X - 10, c.Y - 4)));
    }

    private static void Loop(DrawingContext ctx, VsmElement e, Rect r)
    {
        var c = new Point(r.Center.X, r.Y + r.Height * 0.45);
        var pen = Ui.Pen(e.Metrics.IsRework ? Ui.Danger : Ui.Ink, 1.8);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(c.X - 12, c.Y - 4), false);
            g.ArcTo(new Point(c.X + 12, c.Y - 4), new Size(13, 13), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
            g.BeginFigure(new Point(c.X + 12, c.Y + 4), false);
            g.ArcTo(new Point(c.X - 12, c.Y + 4), new Size(13, 13), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, arc);
        ctx.DrawGeometry(pen.Brush, null, Polygon(new Point(c.X + 12, c.Y - 10), new Point(c.X + 17, c.Y - 2), new Point(c.X + 7, c.Y - 2)));
        ctx.DrawGeometry(pen.Brush, null, Polygon(new Point(c.X - 12, c.Y + 10), new Point(c.X - 17, c.Y + 2), new Point(c.X - 7, c.Y + 2)));
    }
}
