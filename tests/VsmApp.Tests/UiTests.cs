// Headless UI tests: drive the real windows with Avalonia's headless
// platform and render actual frames (Skia) to PNG for inspection.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VsmApp;
using VsmApp.Tests;
using VsmCore;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace VsmApp.Tests;

public class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class UiTests
{
    private static readonly string ShotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    private static MainWindow NewWindow()
    {
        Environment.SetEnvironmentVariable("VSM_MAPS_DIR", Path.Combine(Path.GetTempPath(), "vsm-ui-" + Guid.NewGuid()));
        Directory.CreateDirectory(ShotDir);
        var w = new MainWindow { Width = 1380, Height = 860 };
        w.Show();
        Pump();
        return w;
    }

    private static void Pump(int times = 3)
    {
        for (int i = 0; i < times; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    private static void Shot(Avalonia.Controls.Window w, string name)
    {
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(ShotDir, name + ".png"));
    }

    [AvaloniaFact]
    public void EmptyLibraryThenExampleMapEditor()
    {
        var w = NewWindow();
        Assert.Equal(0, w.MapCount);
        Shot(w, "01-empty");
        w.LoadExample();
        Pump();
        Assert.Equal(1, w.MapCount);
        Assert.NotNull(w.CurrentMap);
        Assert.Equal(13, w.Canvas.Map!.Elements.Count);
        w.Canvas.ZoomToFit();
        Shot(w, "02-editor");
        w.SelectElementNamed("Welding");
        Shot(w, "03-inspector-welding");
        w.ShowAnalysisTab();
        Shot(w, "04-analysis");
    }

    [AvaloniaFact]
    public void AddElementAndUndo()
    {
        var w = NewWindow();
        w.LoadExample();
        Pump();
        w.AddElementForTest(ElementKind.Supermarket);
        Assert.Equal(14, w.CurrentMap!.Elements.Count);
        Assert.Equal(ElementKind.Supermarket, w.CurrentMap.Elements[^1].Kind);
        w.UndoForTest();
        Assert.Equal(13, w.CurrentMap.Elements.Count);
    }

    [AvaloniaFact]
    public void FlowAnimationRunsAndSummarizes()
    {
        var w = NewWindow();
        w.LoadExample();
        Pump();
        var anim = w.OpenAnimation();
        anim.Width = 1100; anim.Height = 720;
        Pump();
        anim.Canvas.ZoomToFit();
        for (int i = 0; i < 120; i++) anim.Step(1.0 / 30, 5);
        Assert.True(anim.Simulator.SimTime > 1800);
        Assert.True(anim.Simulator.Shipped > 20);
        Shot(anim, "05-animation");
        var summary = anim.OpenSummary();
        summary.Width = 560; summary.Height = 640;
        Shot(summary, "06-summary");
        var s = anim.Simulator.GetSummary();
        Assert.Contains(s.Constraints, c => c.Element.Name == "Welding");
    }

    [AvaloniaFact]
    public void ImportGuideAndGlossaryOpen()
    {
        var w = NewWindow();
        var guide = w.OpenGuide();
        guide.Width = 760; guide.Height = 760;
        Shot(guide, "07-import-guide");
        var glossary = new GlossaryWindow { Width = 640, Height = 720 };
        glossary.Show();
        Shot(glossary, "08-glossary");
    }

    [AvaloniaFact]
    public void ExportsProducePngAndPdf()
    {
        var w = NewWindow();
        w.LoadExample();
        Pump();
        var png = ExportService.MapPng(w.CurrentMap!, 2.0);
        Assert.True(png.Length > 10_000);
        File.WriteAllBytes(Path.Combine(ShotDir, "09-export.png"), png);
        var pdf = Path.Combine(Path.GetTempPath(), "vsm-ui-" + Guid.NewGuid() + ".pdf");
        ExportService.MapPdf(w.CurrentMap!, pdf);
        Assert.True(new FileInfo(pdf).Length > 5_000);
        Assert.StartsWith("%PDF", File.ReadAllText(pdf).Substring(0, 4));
        var report = Path.Combine(Path.GetTempPath(), "vsm-ui-" + Guid.NewGuid() + ".pdf");
        var sim = new VsmCore.Simulation.FlowSimulator(w.CurrentMap!);
        for (int i = 0; i < 300; i++) sim.Advance(1.0 / 30, 5);
        ExportService.FlowSummaryPdf(sim.GetSummary(), w.CurrentMap!.Title, false, report);
        Assert.True(new FileInfo(report).Length > 1_000);
    }
}
