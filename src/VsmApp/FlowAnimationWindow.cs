// Flow animation: runs the simulator against the map and shows material
// and information moving, WIP piling up, and bottlenecks emerging.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VsmCore;
using VsmCore.Simulation;

namespace VsmApp;

public sealed class FlowAnimationWindow : Window
{
    private readonly ValueStreamMap map;
    private FlowSimulator sim;
    private readonly MapCanvas canvas = new() { Interactive = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = new();
    private readonly Button playBtn = new() { Content = "❚❚ Pause", MinWidth = 90 };
    private readonly ComboBox speedBox = new() { ItemsSource = new[] { "0.5×", "1×", "2×", "5×" }, SelectedIndex = 1, Width = 80 };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 6, Width = 260 };
    private readonly TextBlock clockText = new() { FontFamily = Ui.Mono, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock shippedText = new() { Foreground = Ui.InkSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private bool playing = true, summarized;
    private readonly double[] speeds = { 0.5, 1, 2, 5 };

    public FlowAnimationWindow(ValueStreamMap map)
    {
        this.map = map;
        sim = new FlowSimulator(map);
        Title = "Flow Animation — " + map.Title;
        Width = 1100; Height = 720;
        canvas.Map = map; canvas.Simulator = sim;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Center };
        top.Children.Add(Legend(Ui.Material, "Material"));
        top.Children.Add(Legend(Ui.Info, "Information"));
        if (sim.TaktIsEstimated) top.Children.Add(new TextBlock { Text = "Takt estimated — set demand in Map Settings", Foreground = Ui.Material, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(progress);
        DockPanel.SetDock(top, Dock.Top);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(12, 8) };
        playBtn.Click += (_, _) => { playing = !playing; playBtn.Content = playing ? "❚❚ Pause" : "▶ Play"; clock.Restart(); };
        var restart = new Button { Content = "↺ Restart" };
        restart.Click += (_, _) => { sim = new FlowSimulator(map); canvas.Simulator = sim; summarized = false; playing = true; playBtn.Content = "❚❚ Pause"; clock.Restart(); canvas.InvalidateVisual(); };
        var summary = new Button { Content = "📋 Summary" };
        summary.Click += (_, _) => { playing = false; playBtn.Content = "▶ Play"; new FlowSummaryWindow(sim.GetSummary(), map.Title, sim.TaktIsEstimated).Show(this); };
        bottom.Children.Add(playBtn); bottom.Children.Add(restart);
        bottom.Children.Add(new TextBlock { Text = "Speed", VerticalAlignment = VerticalAlignment.Center }); bottom.Children.Add(speedBox);
        bottom.Children.Add(clockText); bottom.Children.Add(shippedText);
        bottom.Children.Add(summary);
        bottom.Children.Add(new Button { Content = "⊡ Fit", Command = new RelayCommand(() => canvas.ZoomToFit()) });
        DockPanel.SetDock(bottom, Dock.Bottom);

        Content = new DockPanel { Children = { top, bottom, canvas } };
        timer.Tick += (_, _) => Tick();
        Opened += (_, _) => { Dispatcher.UIThread.Post(() => canvas.ZoomToFit(), DispatcherPriority.Background); clock.Start(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }

    private static Control Legend(IBrush brush, string text) =>
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = brush, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center } } };

    /// <summary>Advances the simulation by a fixed amount of real time (used by tests).</summary>
    public void Step(double realSeconds, double speed = 1)
    {
        sim.Advance(realSeconds, speed);
        progress.Value = sim.SimTime / sim.DayLength;
        canvas.InvalidateVisual();
    }

    public FlowSimulator Simulator => sim;
    public MapCanvas Canvas => canvas;
    public FlowSummaryWindow OpenSummary() { var w = new FlowSummaryWindow(sim.GetSummary(), map.Title, sim.TaktIsEstimated); w.Show(this); return w; }

    private void Tick()
    {
        var dt = clock.Elapsed.TotalSeconds; clock.Restart();
        if (playing) sim.Advance(dt, speeds[Math.Max(speedBox.SelectedIndex, 0)]);
        progress.Value = sim.SimTime / sim.DayLength;
        var s = (int)sim.SimTime;
        clockText.Text = $"{s / 3600}:{(s % 3600) / 60:00} h";
        shippedText.Text = $"Shipped {(int)sim.Shipped}";
        canvas.InvalidateVisual();
        if (sim.IsFinished && !summarized)
        {
            summarized = true; playing = false; playBtn.Content = "▶ Play";
            new FlowSummaryWindow(sim.GetSummary(), map.Title, sim.TaktIsEstimated).Show(this);
        }
    }
}

public sealed class FlowSummaryWindow : Window
{
    public FlowSummaryWindow(FlowSimulator.Summary s, string mapTitle, bool taktIsEstimated)
    {
        Title = "Flow Summary";
        Width = 560; Height = 640;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Flow Summary", FontSize = 20, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = mapTitle, Foreground = Ui.InkSecondary });

        panel.Children.Add(Section("Throughput"));
        panel.Children.Add(Row("Shipped", $"{(int)s.Shipped} of {(int)s.Demand} demanded"));
        panel.Children.Add(Row("Simulated time", VsmFormat.Seconds(s.SimTime)));
        panel.Children.Add(Row("Takt" + (taktIsEstimated ? " (estimated)" : ""), VsmFormat.Seconds(s.Takt)));
        if (s.Demand > 0) panel.Children.Add(Row("Demand coverage", $"{s.Shipped / s.Demand * 100:0} %", s.Shipped / s.Demand < 0.95 ? Ui.Danger : Ui.Success));

        panel.Children.Add(Section("Where material accumulated"));
        if (s.Accumulations.Count == 0) panel.Children.Add(Muted("No significant accumulation — flow kept pace with demand."));
        foreach (var a in s.Accumulations) panel.Children.Add(Entry(a.Element.Name, $"Grew by {(int)a.Delta} pcs (now {(int)a.Final}, peak {(int)a.Peak})", Ui.Ink));

        panel.Children.Add(Section("Constraint processes"));
        if (s.Constraints.Count == 0) panel.Children.Add(Muted("No process fell behind takt."));
        foreach (var c in s.Constraints) panel.Children.Add(Entry("⚠ " + c.Element.Name, $"Effective cycle {VsmFormat.Seconds(c.Interval)} vs takt {VsmFormat.Seconds(s.Takt)} · {(int)(c.Utilization * 100)} % busy · +{(int)c.UpstreamGrowth} pcs upstream", Ui.Danger));

        if (s.StarvedSteps.Count > 0)
        {
            panel.Children.Add(Section("Starved by upstream constraints"));
            foreach (var st in s.StarvedSteps) panel.Children.Add(Entry(st.Element.Name, $"Idle waiting for material {(int)(st.Fraction * 100)} % of the time", Ui.Info));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var pdf = new Button { Content = "⇩ Download PDF Report" };
        pdf.Click += async (_, _) =>
        {
            var path = await Dialogs.SaveFile(this, "Save report", Exports.SafeName(mapTitle) + " — Flow Summary", "pdf", "PDF report");
            if (path != null) { try { ExportService.FlowSummaryPdf(s, mapTitle, taktIsEstimated, path); } catch (Exception ex) { await Dialogs.Message(this, "Export failed", ex.Message); } }
        };
        var csv = new Button { Content = "⇩ Download CSV" };
        csv.Click += async (_, _) =>
        {
            var path = await Dialogs.SaveFile(this, "Save summary", Exports.SafeName(mapTitle) + " — Flow Summary", "csv", "CSV file");
            if (path != null) await File.WriteAllTextAsync(path, Exports.FlowSummaryCsv(s, mapTitle));
        };
        var close = new Button { Content = "Done", IsDefault = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(pdf); buttons.Children.Add(csv); buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Section(string t) => new() { Text = t, FontWeight = FontWeight.Bold, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
    private static TextBlock Muted(string t) => new() { Text = t, Foreground = Ui.InkSecondary, TextWrapping = TextWrapping.Wrap };
    private static Control Row(string k, string v, IBrush? color = null) =>
        new DockPanel { Children = { new TextBlock { Text = v, Foreground = color ?? Ui.InkSecondary, [DockPanel.DockProperty] = Dock.Right }, new TextBlock { Text = k } } };
    private static Control Entry(string title, string detail, IBrush color) =>
        new StackPanel { Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = color }, new TextBlock { Text = detail, FontSize = 11, Foreground = Ui.InkSecondary, TextWrapping = TextWrapping.Wrap } } };
}

internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action action;
    public RelayCommand(Action action) => this.action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
