// Analysis: takt, lead time, PCE cards; cycle-time-vs-takt chart; the
// castellated lead-time ladder; kaizen bursts. Drawn directly.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VsmCore;

namespace VsmApp;

public sealed class AnalysisPanel : Control
{
    public ValueStreamMap? Map { get; set; }

    public AnalysisPanel() { Height = 980; MinWidth = 280; }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (Map == null) { ctx.DrawText(Ui.Text("Select a map to analyse.", 12, FontWeight.Normal, Ui.InkSecondary), new Point(4, 4)); return; }
        var map = Map;
        double y = 0;
        var cardW = (w - 10) / 2;
        Card(ctx, new Rect(0, y, cardW, 64), "Takt Time", VsmFormat.Seconds(map.TaktTime), "available time ÷ demand", Ui.Info);
        Card(ctx, new Rect(cardW + 10, y, cardW, 64), "Demand", map.CustomerDemandPerDay > 0 ? VsmFormat.Trim(map.CustomerDemandPerDay) + " /day" : "—", "customer requirement", new SolidColorBrush(Color.FromRgb(88, 86, 214)));
        y += 74;
        Card(ctx, new Rect(0, y, cardW, 64), "Lead Time (PLT)", VsmFormat.Days(map.ProductionLeadTimeDays), "door-to-door", Ui.Material);
        Card(ctx, new Rect(cardW + 10, y, cardW, 64), "Processing Time", VsmFormat.Seconds(map.TotalProcessingTime), "sum of C/T", Ui.Success);
        y += 74;
        if (map.ProcessCycleEfficiency is double pce)
        {
            Card(ctx, new Rect(0, y, w, 64), "Process-Cycle Efficiency", $"{pce * 100:0.00} %", "value-creating time ÷ lead time — usually VCT < C/T < PLT", pce > 0.05 ? Ui.Success : Ui.Danger);
            y += 74;
        }

        // Chart
        var processes = map.TimelineElements.Where(e => e.Kind.SupportsProcessMetrics() && e.Metrics.CycleTime != null).ToList();
        var chart = new Rect(0, y, w, 230);
        ctx.DrawRectangle(Ui.CanvasBackground, null, chart, 10, 10);
        ctx.DrawText(Ui.Text("Cycle Time vs. Takt", 13, FontWeight.Bold), new Point(12, y + 10));
        ctx.DrawText(Ui.Text("Processes above takt cannot keep up with demand.", 10, FontWeight.Normal, Ui.InkSecondary), new Point(12, y + 28));
        if (processes.Count > 0)
        {
            var plot = new Rect(chart.X + 16, chart.Y + 50, chart.Width - 32, chart.Height - 90);
            var max = Math.Max(processes.Max(p => p.Metrics.CycleTime!.Value), map.TaktTime ?? 0) * 1.15;
            if (max <= 0) max = 1;
            var slot = plot.Width / processes.Count;
            for (int i = 0; i < processes.Count; i++)
            {
                var p = processes[i]; var ct = p.Metrics.CycleTime!.Value;
                var hgt = ct / max * plot.Height;
                var bar = new Rect(plot.X + i * slot + slot * 0.2, plot.Bottom - hgt, slot * 0.6, hgt);
                var over = map.TaktTime is double tk && ct > tk;
                ctx.DrawRectangle(over ? Ui.Danger : Ui.Success, null, bar, 3, 3);
                Ui.DrawCentered(ctx, VsmFormat.Seconds(ct), new Point(bar.Center.X, bar.Y - 8), 8, FontWeight.Normal, Ui.InkSecondary);
                Ui.DrawCentered(ctx, p.Name, new Point(bar.Center.X, plot.Bottom + 10), 9, FontWeight.Normal, Ui.Ink, slot);
            }
            if (map.TaktTime is double takt)
            {
                var ty = plot.Bottom - takt / max * plot.Height;
                ctx.DrawLine(Ui.Pen(Ui.Info, 1.5, new[] { 6.0, 3.0 }), new Point(plot.X, ty), new Point(plot.Right, ty));
                var label = Ui.Text("Takt " + VsmFormat.Seconds(takt), 9, FontWeight.Normal, Ui.Info);
                ctx.DrawText(label, new Point(plot.Right - label.Width, ty - 12));
            }
        }
        else ctx.DrawText(Ui.Text("Add processes with cycle times to see the chart.", 10, FontWeight.Normal, Ui.InkSecondary), new Point(12, y + 60));
        y += 240;

        // Ladder
        var ladder = new Rect(0, y, w, 190);
        ctx.DrawRectangle(Ui.CanvasBackground, null, ladder, 10, 10);
        ctx.DrawText(Ui.Text("Lead-Time Ladder", 13, FontWeight.Bold), new Point(12, y + 10));
        ctx.DrawText(Ui.Text("High: inventory waiting (days). Low: processing time.", 10, FontWeight.Normal, Ui.InkSecondary), new Point(12, y + 28));
        var segs = new List<(string Label, string Value, bool Wait)>();
        foreach (var e in map.TimelineElements)
        {
            if (map.InventoryDays(e) is double d) segs.Add((e.Name, VsmFormat.Days(d), true));
            else if (e.Kind.HoldsInventory() && e.Kind != ElementKind.FifoLane && (e.Metrics.InventoryQty != null || e.Kind == ElementKind.Inventory)) segs.Add((e.Name, "?", true));
            if (e.Kind.SupportsProcessMetrics()) segs.Add((e.Name, VsmFormat.Seconds(e.Metrics.CycleTime), false));
        }
        if (segs.Count > 0)
        {
            using (ctx.PushClip(new Rect(ladder.X + 8, ladder.Y + 44, ladder.Width - 16, 120)))
            {
                var segW = Math.Max(54, (ladder.Width - 16) / segs.Count);
                var top = ladder.Y + 60; var bottom = ladder.Y + 110;
                for (int i = 0; i < segs.Count; i++)
                {
                    var x = ladder.X + 8 + i * segW; var (label, value, wait) = segs[i];
                    var rail = wait ? top : bottom;
                    ctx.DrawLine(Ui.Pen(wait ? Ui.Material : Ui.Success, 3), new Point(x, rail), new Point(x + segW, rail));
                    if (i > 0 && segs[i - 1].Wait != wait) ctx.DrawLine(Ui.Pen(Ui.InkSecondary, 1.2), new Point(x, top), new Point(x, bottom));
                    Ui.DrawCentered(ctx, value, new Point(x + segW / 2, wait ? top - 9 : bottom + 9), 9, FontWeight.Bold, wait ? Ui.Material : Ui.Success);
                    Ui.DrawCentered(ctx, label, new Point(x + segW / 2, bottom + 24), 8, FontWeight.Normal, Ui.InkSecondary, segW);
                }
            }
            ctx.DrawText(Ui.Text("PLT " + VsmFormat.Days(map.ProductionLeadTimeDays), 11, FontWeight.SemiBold, Ui.Material), new Point(12, ladder.Bottom - 22));
            var vct = Ui.Text("VCT " + VsmFormat.Seconds(map.TotalValueCreatingTime), 11, FontWeight.SemiBold, Ui.Success);
            ctx.DrawText(vct, new Point(ladder.Right - 12 - vct.Width, ladder.Bottom - 22));
        }
        y += 200;

        // Kaizen
        var bursts = map.KaizenBursts;
        var kh = Math.Max(60, 40 + bursts.Count * 34);
        var box = new Rect(0, y, w, kh);
        ctx.DrawRectangle(Ui.CanvasBackground, null, box, 10, 10);
        ctx.DrawText(Ui.Text("Kaizen Bursts", 13, FontWeight.Bold), new Point(12, y + 10));
        if (bursts.Count == 0) ctx.DrawText(Ui.Text("Add Kaizen Burst icons to mark countermeasures for the future state.", 10, FontWeight.Normal, Ui.InkSecondary, TextAlignment.Left, w - 24), new Point(12, y + 30));
        for (int i = 0; i < bursts.Count; i++)
        {
            var b = bursts[i]; var by = y + 34 + i * 34;
            ctx.DrawGeometry(Ui.Danger, null, IconPainter.Burst(new Rect(12, by, 16, 16)));
            ctx.DrawText(Ui.Text(b.Name, 11, FontWeight.SemiBold), new Point(36, by));
            if (!string.IsNullOrEmpty(b.Note)) ctx.DrawText(Ui.Text(b.Note, 9, FontWeight.Normal, Ui.InkSecondary, TextAlignment.Left, w - 48), new Point(36, by + 15));
        }
        var desired = y + kh + 10;
        // Resizing during a render pass is not allowed; defer it to the next layout.
        if (Math.Abs(Height - desired) > 0.5) Avalonia.Threading.Dispatcher.UIThread.Post(() => Height = desired);
    }

    private static void Card(DrawingContext ctx, Rect r, string title, string value, string note, IBrush color)
    {
        ctx.DrawRectangle(Ui.CanvasBackground, null, r, 10, 10);
        ctx.DrawText(Ui.Text(title, 10, FontWeight.Normal, Ui.InkSecondary), new Point(r.X + 10, r.Y + 8));
        ctx.DrawText(Ui.Text(value, 17, FontWeight.Bold, color), new Point(r.X + 10, r.Y + 22));
        ctx.DrawText(Ui.Text(note, 8.5, FontWeight.Normal, Ui.InkSecondary, TextAlignment.Left, r.Width - 20), new Point(r.X + 10, r.Y + 46));
    }
}
