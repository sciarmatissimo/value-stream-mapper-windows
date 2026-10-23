// PNG and PDF exports of a map and the flow-summary report.

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using VsmCore;
using VsmCore.Simulation;

namespace VsmApp;

public static class ExportService
{
    /// <summary>Renders the whole map (with a header line) to a PNG byte array at the given scale.</summary>
    public static byte[] MapPng(ValueStreamMap map, double scale = 2.0)
    {
        var b = MapCanvas.ContentBounds(map, 70, 90);
        var header = 40.0;
        var width = (int)Math.Ceiling(b.Width * scale); var height = (int)Math.Ceiling((b.Height + header) * scale);
        var control = new MapRenderControl(map, b, header);
        control.Measure(new Size(b.Width, b.Height + header));
        control.Arrange(new Rect(0, 0, b.Width, b.Height + header));
        var bmp = new RenderTargetBitmap(new PixelSize(Math.Max(width, 1), Math.Max(height, 1)), new Vector(96 * scale, 96 * scale));
        bmp.Render(control);
        using var ms = new MemoryStream();
        bmp.Save(ms);
        return ms.ToArray();
    }

    public static void MapPdf(ValueStreamMap map, string path) => ImagePdf(MapPng(map, 2.0), path);

    private static void ImagePdf(byte[] png, string path)
    {
        using var bitmap = SKBitmap.Decode(png);
        using var stream = File.Create(path);
        using var doc = SKDocument.CreatePdf(stream);
        using var canvas = doc.BeginPage(bitmap.Width / 2f, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, bitmap.Width / 2f, bitmap.Height / 2f), new SKPaint { FilterQuality = SKFilterQuality.High });
        doc.EndPage();
        doc.Close();
    }

    /// <summary>A one-page flow-summary report drawn directly with Skia.</summary>
    public static void FlowSummaryPdf(FlowSimulator.Summary s, string mapTitle, bool taktIsEstimated, string path)
    {
        const float W = 620, pad = 32;
        using var stream = File.Create(path);
        using var doc = SKDocument.CreatePdf(stream);
        var canvas = doc.BeginPage(W, 820);
        canvas.Clear(SKColors.White);
        using var title = new SKPaint { Color = SKColors.Black, TextSize = 22, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        using var sub = new SKPaint { Color = new SKColor(110, 110, 115), TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        using var head = new SKPaint { Color = SKColors.Black, TextSize = 13, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
        using var body = new SKPaint { Color = SKColors.Black, TextSize = 11, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        using var muted = new SKPaint { Color = new SKColor(110, 110, 115), TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI") };

        float y = pad + 22;
        canvas.DrawText("Flow Summary", pad, y, title); y += 20;
        canvas.DrawText(string.IsNullOrEmpty(mapTitle) ? "Value stream" : mapTitle, pad, y, sub); y += 16;
        canvas.DrawText($"Simulated {VsmFormat.Seconds(s.SimTime)} of flow · generated {DateTime.Now:g}", pad, y, muted); y += 24;

        var coverage = s.Demand > 0 ? s.Shipped / s.Demand : (double?)null;
        float cardW = (W - 2 * pad - 24) / 3;
        void Card(int i, string label, string value, SKColor color)
        {
            var x = pad + i * (cardW + 12);
            using var fill = new SKPaint { Color = new SKColor(242, 242, 247), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(x, y, x + cardW, y + 52), 8, 8, fill);
            canvas.DrawText(label, x + 10, y + 16, muted);
            using var big = new SKPaint { Color = color, TextSize = 16, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) };
            canvas.DrawText(value, x + 10, y + 40, big);
        }
        Card(0, "Shipped", $"{(int)s.Shipped} / {(int)s.Demand}", new SKColor(0, 122, 255));
        Card(1, "Demand coverage", coverage is double c ? $"{c * 100:0} %" : "—", coverage is double c2 && c2 < 0.95 ? new SKColor(255, 59, 48) : new SKColor(52, 199, 89));
        Card(2, "Takt" + (taktIsEstimated ? " (est.)" : ""), VsmFormat.Seconds(s.Takt), new SKColor(88, 86, 214));
        y += 76;

        void Section(string titleText, string empty, List<(string, string, string)> rows)
        {
            canvas.DrawText(titleText, pad, y, head); y += 16;
            if (rows.Count == 0) { canvas.DrawText(empty, pad, y, muted); y += 18; return; }
            int n = 1;
            foreach (var (a, b, c) in rows)
            {
                canvas.DrawText($"{n++}.", pad, y, muted);
                canvas.DrawText(a, pad + 22, y, body);
                canvas.DrawText(b, pad + 200, y, body);
                canvas.DrawText(c, pad + 360, y, muted);
                y += 17;
            }
            y += 10;
        }
        Section("Where material accumulated", "No significant accumulation — flow kept pace with demand.",
            s.Accumulations.Select(a => (a.Element.Name, $"+{(int)a.Delta} pcs", $"now {(int)a.Final} · peak {(int)a.Peak}")).ToList());
        Section("Constraint processes", "No process fell behind takt.",
            s.Constraints.Select(c => (c.Element.Name, $"{VsmFormat.Seconds(c.Interval)} vs takt {VsmFormat.Seconds(s.Takt)}", $"{(int)(c.Utilization * 100)} % busy · +{(int)c.UpstreamGrowth} pcs upstream")).ToList());
        if (s.StarvedSteps.Count > 0)
            Section("Starved by upstream constraints", "", s.StarvedSteps.Select(st => (st.Element.Name, $"{(int)(st.Fraction * 100)} % idle", "waiting for material")).ToList());
        doc.EndPage();
        doc.Close();
    }
}

/// <summary>Offscreen control used for PNG/PDF export: header plus the map at origin.</summary>
internal sealed class MapRenderControl : Avalonia.Controls.Control
{
    private readonly ValueStreamMap map; private readonly Rect bounds; private readonly double header;
    public MapRenderControl(ValueStreamMap map, Rect bounds, double header) { this.map = map; this.bounds = bounds; this.header = header; }

    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, bounds.Width, bounds.Height + header));
        ctx.DrawText(Ui.Text(map.Title, 18, FontWeight.Bold), new Point(16, 8));
        var parts = new List<string> { map.State.RawValue() };
        if (!string.IsNullOrEmpty(map.ProductFamily)) parts.Add("Family: " + map.ProductFamily);
        if (map.TaktTime is double takt) parts.Add("Takt: " + VsmFormat.Seconds(takt));
        parts.Add("PLT: " + VsmFormat.Days(map.ProductionLeadTimeDays));
        parts.Add("VCT: " + VsmFormat.Seconds(map.TotalValueCreatingTime));
        ctx.DrawText(Ui.Text(string.Join("   ", parts), 10, FontWeight.Normal, Ui.InkSecondary), new Point(16, 30));
        using (ctx.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y + header)))
            MapCanvas.DrawMap(ctx, map, null, null, null);
    }
}
