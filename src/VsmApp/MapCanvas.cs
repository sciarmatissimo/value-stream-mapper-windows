// The map surface: draws grid, flows and icons in map coordinates with
// pan/zoom, and handles selection, dragging and connect mode. With a
// FlowSimulator attached it also draws the animation overlay.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VsmCore;
using VsmCore.Simulation;

namespace VsmApp;

public sealed class MapCanvas : Control
{
    public const double MapWidth = 3000, MapHeight = 2000;

    public ValueStreamMap? Map { get; set; }
    public double Scale { get; set; } = 0.7;
    public Vector Offset { get; set; }
    public Guid? SelectedElementId { get; set; }
    public Guid? SelectedConnectionId { get; set; }
    public Guid? ConnectSourceId { get; set; }
    public bool IsConnecting { get; set; }
    public bool Interactive { get; set; } = true;
    public FlowSimulator? Simulator { get; set; }

    public event Action<VsmElement>? ElementClicked;
    public event Action<VsmElement>? ElementDoubleClicked;
    public event Action<VsmConnection>? ConnectionClicked;
    public event Action? BackgroundClicked;
    public event Action<VsmElement>? ElementDragStarted;
    public event Action<VsmElement>? ElementDragEnded;

    private Point lastPointer;
    private bool panning;
    private VsmElement? dragging;
    private Point dragStartElement, dragStartPointer;
    private bool dragMoved;

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public Point ToMap(Point screen) => new((screen.X - Offset.X) / Scale, (screen.Y - Offset.Y) / Scale);

    public void ZoomToFit()
    {
        if (Map == null || Map.Elements.Count == 0 || Bounds.Width <= 0) { Scale = 0.7; Offset = default; InvalidateVisual(); return; }
        var b = ContentBounds(Map, 60, 100);
        var s = Math.Min(Math.Min(Bounds.Width / b.Width, Bounds.Height / b.Height), 1.6);
        Scale = Math.Max(s, 0.15);
        Offset = new Vector(-b.X * Scale + (Bounds.Width - b.Width * Scale) / 2, -b.Y * Scale + (Bounds.Height - b.Height * Scale) / 2);
        InvalidateVisual();
    }

    public static Rect ContentBounds(ValueStreamMap map, double padX, double padY)
    {
        if (map.Elements.Count == 0) return new Rect(0, 0, 800, 500);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var e in map.Elements)
        {
            var (l, t, w, h) = e.Frame;
            minX = Math.Min(minX, l); minY = Math.Min(minY, t); maxX = Math.Max(maxX, l + w); maxY = Math.Max(maxY, t + h);
        }
        return new Rect(minX - padX, minY - padY, maxX - minX + 2 * padX, maxY - minY + 2 * padY);
    }

    public void ZoomBy(double factor)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ZoomAround(center, Math.Clamp(Scale * factor, 0.15, 3.0));
    }

    private void ZoomAround(Point screen, double newScale)
    {
        var f = newScale / Scale;
        Offset = new Vector(screen.X - (screen.X - Offset.X) * f, screen.Y - (screen.Y - Offset.Y) * f);
        Scale = newScale;
        InvalidateVisual();
    }

    // MARK: Rendering

    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(Ui.CanvasBackground, null, new Rect(Bounds.Size));
        if (Map == null) return;
        using (ctx.PushTransform(Matrix.CreateScale(Scale, Scale) * Matrix.CreateTranslation(Offset.X, Offset.Y)))
        {
            DrawGrid(ctx);
            DrawMap(ctx, Map, SelectedElementId, SelectedConnectionId, ConnectSourceId);
            if (Simulator != null) DrawOverlay(ctx, Simulator);
        }
    }

    private static void DrawGrid(DrawingContext ctx)
    {
        var pen = new Pen(Ui.Grid, 1);
        for (double x = 0; x <= MapWidth; x += 100) ctx.DrawLine(pen, new Point(x, 0), new Point(x, MapHeight));
        for (double y = 0; y <= MapHeight; y += 100) ctx.DrawLine(pen, new Point(0, y), new Point(MapWidth, y));
    }

    public static void DrawMap(DrawingContext ctx, ValueStreamMap map, Guid? selectedElement, Guid? selectedConnection, Guid? connectSource)
    {
        foreach (var c in map.Connections)
        {
            var from = map.Element(c.FromId); var to = map.Element(c.ToId);
            if (from == null || to == null) continue;
            ConnectionPainter.Draw(ctx, c, ConnectionGeometry.Between(from, to), c.Id == selectedConnection);
        }
        foreach (var e in map.Elements)
            IconPainter.Draw(ctx, e, map, e.Id == selectedElement || e.Id == connectSource);
    }

    private static void DrawOverlay(DrawingContext ctx, FlowSimulator sim)
    {
        var t = sim.SimTime;
        foreach (var node in sim.Nodes)
        {
            var (l, top, w, h) = node.Element.Frame;
            var frame = new Rect(l, top, w, h);
            var level = sim.BottleneckLevel(node);
            if (level > 0)
            {
                var pulse = 0.5 + 0.5 * Math.Sin(t / Math.Max(sim.Takt, 1) * 2);
                var alpha = level == 2 ? 0.45 + 0.3 * pulse : 0.25 + 0.15 * pulse;
                var halo = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 59, 48)), level == 2 ? 5 : 3);
                ctx.DrawRectangle(null, halo, frame.Inflate(10), 10, 10);
                if (level == 2) Ui.DrawChip(ctx, "⚠ Bottleneck", new Point(frame.Center.X, frame.Y - 22), Ui.Danger, Brushes.White);
            }
            else if (sim.IsStarving(node))
            {
                Ui.DrawChip(ctx, "Starved", new Point(frame.Center.X, frame.Y - 22), Ui.Info, Brushes.White);
            }
            if (!node.IsActive || (node.Role == FlowSimulator.Role.Process && node.Queue >= 1)) DrawQueue(ctx, node, frame);
        }
        foreach (var token in sim.Tokens)
        {
            if (token.Depart > t) continue;
            var span = Math.Max(token.Arrive - token.Depart, 0.001);
            var p = Math.Min((t - token.Depart) / span, 1);
            var x = token.FromX + (token.ToX - token.FromX) * p; var y = token.FromY + (token.ToY - token.FromY) * p;
            if (token.IsInfo)
            {
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 122, 255)), null, new Point(x, y), 3.5, 3.5);
                ctx.DrawEllipse(Ui.Info, null, new Point(x, y), 2, 2);
            }
            else ctx.DrawEllipse(Ui.Material, new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1), new Point(x, y), 5, 5);
        }
    }

    private static void DrawQueue(DrawingContext ctx, FlowSimulator.Node node, Rect frame)
    {
        var count = (int)Math.Round(node.Queue);
        var delta = node.QueueDelta;
        var visible = Math.Clamp((int)Math.Round(delta), 0, 24);
        const int cols = 6; const double unit = 6;
        for (int i = 0; i < visible; i++)
        {
            var col = i % cols; var row = i / cols;
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(216, 255, 149, 0)), null,
                new Rect(frame.Center.X - cols * unit / 2 + col * unit, frame.Bottom + 6 + row * unit, unit - 1.2, unit - 1.2), 1, 1);
        }
        var label = count.ToString("N0") + " pcs";
        IBrush bg = Ui.InkSecondary;
        if (delta >= 1) { label += "  ▲" + (int)Math.Round(delta); bg = delta > 15 ? Ui.Danger : Ui.Material; }
        else if (delta <= -1) { label += "  ▼" + (int)Math.Round(-delta); bg = Ui.Success; }
        var y = frame.Bottom + 6 + ((visible + 5) / 6) * 6 + 10;
        Ui.DrawChip(ctx, label, new Point(frame.Center.X, y), bg, Brushes.White);
    }

    // MARK: Interaction

    private VsmElement? HitElement(Point map)
    {
        if (Map == null) return null;
        for (int i = Map.Elements.Count - 1; i >= 0; i--)
        {
            var e = Map.Elements[i];
            var (l, t, w, h) = e.Frame;
            if (map.X >= l && map.X <= l + w && map.Y >= t && map.Y <= t + h) return e;
        }
        return null;
    }

    private VsmConnection? HitConnection(Point map)
    {
        if (Map == null) return null;
        var tolerance = 14 / Scale;
        foreach (var c in Map.Connections)
        {
            var from = Map.Element(c.FromId); var to = Map.Element(c.ToId);
            if (from == null || to == null) continue;
            var g = ConnectionGeometry.Between(from, to);
            if (ConnectionGeometry.DistanceToSegment(map.X, map.Y, g.StartX, g.StartY, g.EndX, g.EndY) < tolerance) return c;
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        lastPointer = e.GetPosition(this);
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && !Interactive))
        {
            panning = true; e.Pointer.Capture(this); return;
        }
        if (!props.IsLeftButtonPressed) return;
        var map = ToMap(lastPointer);
        var hit = HitElement(map);
        if (hit != null)
        {
            if (e.ClickCount == 2) { ElementDoubleClicked?.Invoke(hit); return; }
            ElementClicked?.Invoke(hit);
            if (!IsConnecting)
            {
                dragging = hit; dragStartElement = new Point(hit.X, hit.Y); dragStartPointer = lastPointer; dragMoved = false;
                e.Pointer.Capture(this);
            }
            InvalidateVisual();
            return;
        }
        if (!IsConnecting && HitConnection(map) is VsmConnection c) { ConnectionClicked?.Invoke(c); InvalidateVisual(); return; }
        if (!IsConnecting) BackgroundClicked?.Invoke();
        panning = true; e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (dragging != null)
        {
            if (!dragMoved && (Math.Abs(p.X - dragStartPointer.X) > 3 || Math.Abs(p.Y - dragStartPointer.Y) > 3))
            {
                dragMoved = true; ElementDragStarted?.Invoke(dragging);
            }
            if (dragMoved)
            {
                dragging.X = dragStartElement.X + (p.X - dragStartPointer.X) / Scale;
                dragging.Y = dragStartElement.Y + (p.Y - dragStartPointer.Y) / Scale;
                InvalidateVisual();
            }
        }
        else if (panning)
        {
            Offset += p - lastPointer;
            InvalidateVisual();
        }
        lastPointer = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging != null && dragMoved) ElementDragEnded?.Invoke(dragging);
        dragging = null; panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        ZoomAround(e.GetPosition(this), Math.Clamp(Scale * factor, 0.15, 3.0));
        e.Handled = true;
    }
}
