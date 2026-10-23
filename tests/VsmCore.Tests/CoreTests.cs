using VsmCore;
using VsmCore.Import;
using VsmCore.Simulation;
using Xunit;

namespace VsmCore.Tests;

public class ModelAndJsonTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void TaktTimeMatchesWorkedExample()
    {
        var map = new ValueStreamMap("t") { CustomerDemandPerDay = 240, AvailableTimePerDay = 480 * 60 };
        Assert.Equal(120, map.TaktTime);
        Assert.Null(new ValueStreamMap("x").TaktTime);
    }

    [Fact]
    public void EffectiveMachineCycleTimeMatchesWorkedExample()
    {
        var m = new ElementMetrics { CycleTime = 50, ChangeoverTime = 30, BatchSize = 30 };
        Assert.Equal(51, m.EffectiveCycleTime);
    }

    [Fact]
    public void OpensVsmapWrittenByTheAppleApp()
    {
        var map = MapJson.Deserialize(File.ReadAllBytes(Fixture("sample.vsmap")));
        Assert.Equal("Stamping Value Stream (Example)", map.Title);
        Assert.Equal(MapState.Current, map.State);
        Assert.Equal(13, map.Elements.Count);
        Assert.Equal(15, map.Connections.Count);
        Assert.Equal(60, map.TaktTime);
        var stamping = map.Elements.Single(e => e.Name == "Stamping");
        Assert.Equal(ElementKind.Process, stamping.Kind);
        Assert.Equal(1, stamping.Metrics.CycleTime);
        Assert.Equal(3600, stamping.Metrics.ChangeoverTime);
        Assert.Equal("2 weeks", stamping.Metrics.Epex);
        Assert.Contains(map.Elements, e => e.Kind == ElementKind.TruckShipment);
        var orders = map.Connections.Single(c => c.Label == "Orders");
        Assert.Equal(ConnectionKind.ElectronicInfo, orders.Kind);
        Assert.Equal(map.Elements.Single(e => e.Kind == ElementKind.Customer).Id, orders.FromId);
    }

    [Fact]
    public void JsonRoundTripKeepsAppleFormat()
    {
        var original = File.ReadAllText(Fixture("sample.vsmap"));
        var map = MapJson.Deserialize(original);
        var json = MapJson.Serialize(map);
        Assert.Contains("\"kind\": \"truckShipment\"", json);
        Assert.Contains("\"state\": \"Current State\"", json);
        Assert.Contains("\"fromID\":", json);
        Assert.Contains("\"usesITSystem\":", json);
        Assert.Matches("\"createdAt\": \"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z\"", json);
        Assert.Matches("\"id\": \"[0-9A-F-]{36}\"", json); // uppercase UUIDs like Swift
        Assert.DoesNotContain("\"cycleTime\": null", json);  // nil keys omitted like Swift
        var again = MapJson.Deserialize(json);
        Assert.Equal(map.Elements.Count, again.Elements.Count);
        Assert.Equal(map.Connections[0].FromId, again.Connections[0].FromId);
        Assert.Equal(map.Elements.Single(e => e.Name == "Coils").Metrics.InventoryQty, again.Elements.Single(e => e.Name == "Coils").Metrics.InventoryQty);
    }

    [Fact]
    public void SampleMapAnalysisMatchesAppleApp()
    {
        var store = new MapStore(Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid()));
        var map = store.CreateSampleMap();
        Assert.Equal(60, map.TaktTime);
        Assert.Equal(new[] { "Coils", "Stamping", "Stamped", "Production Control", "Welding", "Welded", "Assembly", "Finished" }, map.TimelineElements.Select(e => e.Name));
        Assert.Equal(5, map.InventoryDays(map.Elements.Single(e => e.Name == "Coils")));
        Assert.Equal(86, map.TotalProcessingTime);
        Assert.Equal(76, map.TotalValueCreatingTime);
        Assert.InRange(map.ProductionLeadTimeDays, 16.7, 16.8);
        Assert.NotNull(map.ProcessCycleEfficiency);
    }

    [Fact]
    public void StorePersistsAndReloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid());
        var store = new MapStore(dir);
        var map = store.CreateMap("Persist me");
        map.CustomerDemandPerDay = 100;
        store.Update(map);
        var reloaded = new MapStore(dir);
        Assert.Single(reloaded.Maps);
        Assert.Equal("Persist me", reloaded.Maps[0].Title);
        Assert.Equal(100, reloaded.Maps[0].CustomerDemandPerDay);
        reloaded.Delete(reloaded.Maps[0]);
        Assert.Empty(new MapStore(dir).Maps);
    }

    [Fact]
    public void DeriveFutureStateDuplicatesWithNewIdentity()
    {
        var store = new MapStore(Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid()));
        var current = store.CreateSampleMap();
        var future = store.Duplicate(current, MapState.Future);
        Assert.NotEqual(current.Id, future.Id);
        Assert.Equal(MapState.Future, future.State);
        Assert.EndsWith("Future State", future.Title);
        Assert.Equal(current.Elements.Count, future.Elements.Count);
    }
}

public class ImportTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static ValueStreamMap Import(string csv, string name = "x.csv") =>
        MapImportService.ImportMap(System.Text.Encoding.UTF8.GetBytes(csv), name);

    [Fact]
    public void CsvParserHandlesQuotesSemicolonsAndCrlf()
    {
        var rows = CsvTable.Parse("﻿Name;Type;Note\r\n\"Steel; Co.\";Supplier;\"She said \"\"hi\"\"\"\r\nCoils;Inventory;\"multi\nline\"\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Name", "Type", "Note" }, rows[0]);
        Assert.Equal(new[] { "Steel; Co.", "Supplier", "She said \"hi\"" }, rows[1]);
        Assert.Equal(new[] { "Coils", "Inventory", "multi\nline" }, rows[2]);
    }

    [Fact]
    public void NumbersAcceptBothDecimalStyles()
    {
        Assert.Equal(460, MapImportService.ParseNumber("460"));
        Assert.Equal(12.5, MapImportService.ParseNumber("12,5"));
        Assert.Equal(2300, MapImportService.ParseNumber("2 300"));
        Assert.Null(MapImportService.ParseNumber("n/a"));
    }

    [Fact]
    public void BundledTemplateImportsCompletely()
    {
        var map = Import(MapImportService.TemplateCsv, MapImportService.TemplateFilename);
        Assert.Equal("My Value Stream", map.Title);
        Assert.Equal(460, map.CustomerDemandPerDay);
        Assert.Equal(60, map.TaktTime);
        Assert.Equal(10, map.Elements.Count);
        Assert.Equal(10, map.Connections.Count);
        var stamping = map.Elements.Single(e => e.Name == "Stamping");
        Assert.Equal(1, stamping.Metrics.CycleTime);
        Assert.Equal(3600, stamping.Metrics.ChangeoverTime);
        Assert.Equal("2 weeks", stamping.Metrics.Epex);
        var supplier = map.Elements.Single(e => e.Kind == ElementKind.Supplier);
        Assert.Equal((140d, 140d), (supplier.X, supplier.Y));
        Assert.Equal(6, map.Connections.Count(c => c.Kind == ConnectionKind.Push));
        Assert.Equal("Orders", map.Connections.Single(c => c.Kind == ConnectionKind.ElectronicInfo).Label);
        Assert.Equal(new[] { "Coils", "Stamping", "Stamped", "Welding", "Welded", "Assembly", "Finished" }, map.TimelineElements.Select(e => e.Name));
    }

    [Fact]
    public void FlatTableAutoChains()
    {
        var map = Import("Name,Type,C/T (s),Inventory (pcs)\nVendor,Supplier,,\nCut,Process,20,\nWIP,Inventory,,300\nWeld,Process,35,\nClient,Customer,,\n", "flat.csv");
        Assert.Equal("flat", map.Title);
        Assert.Equal(5, map.Elements.Count);
        Assert.Equal(4, map.Connections.Count);
        Assert.Equal(2, map.Connections.Count(c => c.Kind == ConnectionKind.Push));
    }

    [Fact]
    public void EuropeanSemicolonCsv()
    {
        var map = Import("[Map]\nTitle;Linea Staffe\nCustomer Demand (pcs/day);920\nAvailable Time (min/day);450,5\n\n[Elements]\nName;Type;Cycle Time (s);Uptime (%);X;Y\nTaglio;Process;12,5;87,5;300;500\nPiegatura;Process;28;100;;\n");
        Assert.Equal("Linea Staffe", map.Title);
        Assert.Equal(450.5 * 60, map.AvailableTimePerDay, 3);
        var taglio = map.Elements.Single(e => e.Name == "Taglio");
        Assert.Equal(12.5, taglio.Metrics.CycleTime);
        Assert.Equal((300d, 500d), (taglio.X, taglio.Y));
        Assert.Equal(560, map.Elements.Single(e => e.Name == "Piegatura").Y);
        Assert.Single(map.Connections);
    }

    [Fact]
    public void OwnCsvExportRoundTrips()
    {
        var store = new MapStore(Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid()));
        var sample = store.CreateSampleMap();
        var imported = Import(Exports.MapCsv(sample), "Stamping.csv");
        Assert.Equal(sample.TimelineElements.Select(e => e.Name), imported.Elements.Select(e => e.Name));
        Assert.Equal(2300, imported.Elements.Single(e => e.Name == "Coils").Metrics.InventoryQty);
    }

    [Fact]
    public void UnknownTypeAndEndpointThrow()
    {
        var ex = Assert.Throws<MapImportException>(() => Import("Name,Type\nCut,Banana\n"));
        Assert.Contains("Banana", ex.Message);
        Assert.Throws<MapImportException>(() => Import("[Elements]\nName,Type\nCut,Process\n[Connections]\nFrom,To,Flow Type\nCut,Nowhere,Push\n"));
        Assert.Throws<MapImportException>(() => Import("hello\n"));
    }

    [Fact]
    public void ExcelWorkbookImports()
    {
        var data = File.ReadAllBytes(Fixture("ImportFixture.xlsx"));
        var sheets = XlsxReader.Sheets(data);
        Assert.Equal(new[] { "Map", "Elements", "Connections" }, sheets.Select(s => s.Name));
        Assert.Equal("12.5", sheets[1].Rows[3][2]);

        var map = MapImportService.ImportMap(data, "ImportFixture.xlsx");
        Assert.Equal("Fixture Stream", map.Title);
        Assert.Equal(MapState.Future, map.State);
        Assert.Equal(920, map.CustomerDemandPerDay);
        Assert.Equal(7, map.Elements.Count);
        var cutting = map.Elements.Single(e => e.Name == "Cutting");
        Assert.Equal(12.5, cutting.Metrics.CycleTime);
        Assert.Equal(300, cutting.Metrics.ChangeoverTime);
        Assert.Equal("Laser", cutting.Note);
        Assert.Equal(ElementKind.Supermarket, map.Elements.Single(e => e.Name == "Cut parts").Kind);
        Assert.Equal(7, map.Connections.Count);
        Assert.Contains(map.Connections, c => c.Kind == ConnectionKind.Withdrawal);
        Assert.Equal("Orders", map.Connections.Single(c => c.Kind == ConnectionKind.ElectronicInfo).Label);
    }

    [Fact]
    public void VisioDrawingImports()
    {
        var data = File.ReadAllBytes(Fixture("ImportFixture.vsdx"));
        Assert.True(VisioImporter.IsVisio(data));
        Assert.False(VisioImporter.IsVisio(File.ReadAllBytes(Fixture("ImportFixture.xlsx"))));

        var map = MapImportService.ImportMap(data, "ImportFixture.vsdx");
        Assert.Equal("Current State", map.Title);
        Assert.Equal(920, map.CustomerDemandPerDay);
        Assert.Equal(8, map.Elements.Count);
        Assert.Equal(new[] { ElementKind.Supplier, ElementKind.Customer, ElementKind.Inventory, ElementKind.Process, ElementKind.Supermarket, ElementKind.Process, ElementKind.TruckShipment, ElementKind.KaizenBurst },
            map.Elements.Select(e => e.Kind));
        var supplier = map.Elements.Single(e => e.Kind == ElementKind.Supplier);
        Assert.Equal("Acme Steel", supplier.Name);
        Assert.Equal("Weekly", supplier.Metrics.Frequency);
        var cutting = map.Elements.Single(e => e.Name == "Cutting");
        Assert.Equal(12.5, cutting.Metrics.CycleTime);
        Assert.Equal(300, cutting.Metrics.ChangeoverTime);
        Assert.Equal(90, cutting.Metrics.UptimePercent);
        Assert.Equal(1, cutting.Metrics.Operators);
        Assert.Equal(2.5, cutting.Metrics.ScrapPercent);
        Assert.Equal(2, map.Elements.Single(e => e.Name == "Bending").Metrics.Operators);
        Assert.Equal("Daily", map.Elements.Single(e => e.Kind == ElementKind.TruckShipment).Metrics.Frequency);
        Assert.Equal("Reduce C/O", map.Elements.Single(e => e.Kind == ElementKind.KaizenBurst).Name);
        var raw = map.Elements.Single(e => e.Kind == ElementKind.Inventory);
        Assert.True(supplier.Y < raw.Y);
        Assert.True(supplier.X < raw.X && raw.X < cutting.X);
        Assert.Equal(7, map.Connections.Count);
        var market = map.Elements.Single(e => e.Kind == ElementKind.Supermarket);
        var bending = map.Elements.Single(e => e.Name == "Bending");
        Assert.Equal(ConnectionKind.Withdrawal, map.Connections.Single(c => c.FromId == market.Id && c.ToId == bending.Id).Kind);
        Assert.Equal(ConnectionKind.Push, map.Connections.Single(c => c.FromId == raw.Id && c.ToId == cutting.Id).Kind); // glued to a group child
        Assert.Equal("Orders", map.Connections.Single(c => c.Kind == ConnectionKind.ElectronicInfo).Label);
    }

    [Fact]
    public void VisioTextParsesMetrics()
    {
        var t = new VisioText("Welding\nC/T = 39 s\nC/O = 10 min\nUptime 85%\n1 operator\nScrap: 2 %\nEPE 2 weeks");
        Assert.Equal("Welding", t.Name);
        Assert.Equal(39, t.Metrics.CycleTime);
        Assert.Equal(600, t.Metrics.Changeover);
        Assert.Equal(85, t.Metrics.Uptime);
        Assert.Equal(1, t.Metrics.Operators);
        Assert.Equal("2 weeks", t.Metrics.Epe);
        Assert.Null(new VisioText("Product 5").Metrics.CycleTime);
        Assert.Equal("Reduce C/O", new VisioText("Reduce C/O").Name);
        Assert.Equal(920, new VisioText("Plant B\n920 pcs/day").DemandPerDay);
        Assert.Equal("Daily", new VisioText("Daily").Frequency);
    }
}

public class SimulationTests
{
    [Fact]
    public void ExampleMapShowsStampingOverproductionAndWeldingConstraint()
    {
        var store = new MapStore(Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid()));
        var map = store.CreateSampleMap();
        var sim = new FlowSimulator(map);
        Assert.Equal(60, sim.Takt);
        Assert.False(sim.TaktIsEstimated);
        for (int i = 0; i < 550; i++) sim.Advance(1.0 / 30.0, 5);
        var summary = sim.GetSummary();
        Assert.True(summary.SimTime > 3600);
        Assert.True(summary.Shipped > 50);
        Assert.Equal("Stamped", summary.Accumulations[0].Element.Name);
        Assert.Contains(summary.Constraints, c => c.Element.Name == "Welding");
        var csv = Exports.FlowSummaryCsv(summary, map.Title);
        Assert.StartsWith("Section,Item,Value,Detail", csv);
        Assert.Contains("Constraint,Welding,", csv);
    }

    [Fact]
    public void SimulationEndsAtDayLengthAndTokensStayBounded()
    {
        var store = new MapStore(Path.Combine(Path.GetTempPath(), "vsm-tests-" + Guid.NewGuid()));
        var sim = new FlowSimulator(store.CreateSampleMap());
        for (int i = 0; i < 20_000 && !sim.IsFinished; i++) sim.Advance(0.1, 5);
        Assert.True(sim.IsFinished);
        Assert.Equal(sim.DayLength, sim.SimTime);
        Assert.True(sim.Tokens.Count <= 400);
        Assert.InRange(sim.Shipped, 400, 470);
    }
}
