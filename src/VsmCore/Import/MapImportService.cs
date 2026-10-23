// Imports a map from spreadsheet data: the bundled CSV template, a plain
// element table (including this app's own CSV export), a native Excel
// workbook, or a Visio drawing. Types resolve via forgiving aliases, numbers
// accept "." and "," decimals, and maps without coordinates are laid out.

using System.Globalization;
using System.Text;

namespace VsmCore.Import;

public sealed class MapImportException : Exception
{
    public MapImportException(string message) : base(message) { }
}

public static class MapImportService
{
    public const string TemplateFilename = "VSM-Import-Template.csv";

    public static ValueStreamMap ImportMap(byte[] data, string filename)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filename);
        if (XlsxReader.IsZip(data))
        {
            if (VisioImporter.IsVisio(data))
                return VisioImporter.ImportMap(data, fallbackTitle);
            return Build(XlsxReader.Sheets(data), fallbackTitle);
        }
        var text = DecodeText(data) ?? throw new MapImportException(
            "The file is not readable text, an Excel workbook, a Visio drawing, or a .vsmap document.");
        return Build(new[] { ("", CsvTable.Parse(text)) }, fallbackTitle);
    }

    private static string? DecodeText(byte[] data)
    {
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return utf8.GetString(data);
        }
        catch
        {
            try { return Encoding.Latin1.GetString(data); } catch { return null; }
        }
    }

    private sealed class Sections
    {
        public List<List<string>> Settings = new(), Elements = new(), Connections = new();
    }

    public static ValueStreamMap Build(IEnumerable<(string Name, List<List<string>> Rows)> sheets, string fallbackTitle)
    {
        var list = sheets.ToList();
        var sections = new Sections();
        var elementSheet = list.FirstOrDefault(s => Normalize(s.Name).Contains("element"));
        if (elementSheet.Rows != null)
        {
            sections.Elements = elementSheet.Rows;
            sections.Connections = list.FirstOrDefault(s => Normalize(s.Name) is var n && (n.Contains("connection") || n.Contains("flow"))).Rows ?? new();
            sections.Settings = list.FirstOrDefault(s => Normalize(s.Name) is var n && (n.Contains("map") || n.Contains("setting"))).Rows ?? new();
        }
        else if (list.Count > 0)
        {
            sections = SplitSections(list[0].Rows);
        }

        if (sections.Elements.Count == 0) throw new MapImportException(
            "No elements found. The file needs an [Elements] section (or an Elements sheet) with at least Name and Type columns.");

        var map = new ValueStreamMap(string.IsNullOrEmpty(fallbackTitle) ? "Imported Map" : fallbackTitle)
        {
            AvailableTimePerDay = 27_600,
        };
        ApplySettings(sections.Settings, map);
        var elements = ParseElements(sections.Elements);
        if (elements.Count == 0) throw new MapImportException(
            "No elements found. The file needs an [Elements] section (or an Elements sheet) with at least Name and Type columns.");
        map.Elements = elements;
        map.Connections = ParseConnections(sections.Connections, elements);
        LayOutElements(map);
        if (map.Connections.Count == 0) map.Connections = AutoChain(map.Elements);
        return map;
    }

    private static Sections SplitSections(List<List<string>> rows)
    {
        var sections = new Sections();
        List<List<string>>? current = null;
        bool sawMarker = false;
        foreach (var row in rows)
        {
            var first = (row.Count > 0 ? row[0] : "").Trim();
            if (first.StartsWith('#')) continue;
            if (first.StartsWith('[') && first.EndsWith(']'))
            {
                sawMarker = true;
                var name = Normalize(first[1..^1]);
                current = name.Contains("element") ? sections.Elements
                    : (name.Contains("connection") || name.Contains("flow")) ? sections.Connections
                    : (name.Contains("map") || name.Contains("setting")) ? sections.Settings : null;
                continue;
            }
            if (!row.Any(c => c.Trim().Length > 0)) continue;
            if (current != null) current.Add(row);
            else if (!sawMarker) sections.Elements.Add(row);
        }
        return sections;
    }

    private static void ApplySettings(List<List<string>> rows, ValueStreamMap map)
    {
        foreach (var row in rows.Where(r => r.Count >= 2))
        {
            var key = Normalize(row[0]);
            var value = row[1].Trim();
            if (key.Length == 0 || value.Length == 0) continue;
            if (key.Contains("title") || key.Contains("mapname")) map.Title = value;
            else if (key.Contains("family") || key.Contains("product")) map.ProductFamily = value;
            else if (key.Contains("state"))
            {
                var v = Normalize(value);
                map.State = v.Contains("future") ? MapState.Future : v.Contains("ideal") ? MapState.Ideal : MapState.Current;
            }
            else if (key.Contains("demand")) map.CustomerDemandPerDay = ParseNumber(value) ?? map.CustomerDemandPerDay;
            else if (key.Contains("available") || key.Contains("workingtime"))
            {
                if (ParseNumber(value) is not double n) continue;
                map.AvailableTimePerDay = key.Contains("min") ? n * 60 : (key.Contains("hour") || key.Contains("hr")) ? n * 3600 : n;
            }
        }
    }

    private sealed class ElementColumns
    {
        public int Name = -1, Type = -1, Cycle = -1, Vct = -1, Changeover = -1, Uptime = -1, Operators = -1, Shifts = -1,
            Epe = -1, Scrap = -1, Batch = -1, Inventory = -1, Frequency = -1, MaxQty = -1, X = -1, Y = -1, Note = -1, It = -1;
    }

    private static ElementColumns? MapElementHeader(List<string> row)
    {
        var cols = new ElementColumns();
        for (int i = 0; i < row.Count; i++)
        {
            var h = Normalize(row[i]);
            if (h.Length == 0) continue;
            if (h is "name" or "element" or "step" or "processstep") cols.Name = i;
            else if (h is "type" or "kind" or "icon") cols.Type = i;
            else if (h.Contains("valuecreating") || h.StartsWith("vct")) cols.Vct = i;
            else if (h.Contains("cycletime") || h is "ct" or "cts") cols.Cycle = i;
            else if (h.Contains("changeover") || h is "co" or "cos") cols.Changeover = i;
            else if (h.Contains("uptime") || h.Contains("availability")) cols.Uptime = i;
            else if (h.Contains("operator") || h.Contains("staff")) cols.Operators = i;
            else if (h.Contains("shift")) cols.Shifts = i;
            else if (h.StartsWith("epe") || h.Contains("batchinterval")) cols.Epe = i;
            else if (h.Contains("scrap") || h.Contains("defect")) cols.Scrap = i;
            else if (h.Contains("batchsize")) cols.Batch = i;
            else if ((h.Contains("inventory") || h.Contains("stock") || h == "qty" || h.Contains("quantitypcs")) && !h.Contains("day"))
            {
                if (h.Contains("max")) cols.MaxQty = i; else cols.Inventory = i;
            }
            else if (h.Contains("frequency")) cols.Frequency = i;
            else if (h.Contains("maxqty") || h.Contains("maxquantity")) cols.MaxQty = i;
            else if (h == "x") cols.X = i;
            else if (h == "y") cols.Y = i;
            else if (h.Contains("note") || h.Contains("comment")) cols.Note = i;
            else if (h == "it" || h.Contains("itsystem")) cols.It = i;
        }
        return cols.Name >= 0 && cols.Type >= 0 ? cols : null;
    }

    private static string Cell(List<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim() : "";

    private static List<VsmElement> ParseElements(List<List<string>> rows)
    {
        var headerIndex = rows.FindIndex(r => MapElementHeader(r) != null);
        if (headerIndex < 0) throw new MapImportException(
            "No elements found. The file needs an [Elements] section (or an Elements sheet) with at least Name and Type columns.");
        var cols = MapElementHeader(rows[headerIndex])!;
        var elements = new List<VsmElement>();
        for (int offset = headerIndex + 1; offset < rows.Count; offset++)
        {
            var row = rows[offset];
            var name = Cell(row, cols.Name);
            var typeRaw = Cell(row, cols.Type);
            // "Summary" ends an element table (this app's CSV export); blank or numeric types are skipped.
            if (Normalize(name) == "summary" && typeRaw.Length == 0) break;
            if (typeRaw.Length == 0 || ParseNumber(typeRaw) != null) continue;
            var kind = ElementKindFor(typeRaw) ?? throw new MapImportException(
                $"Row {offset + 1}: unknown element type “{typeRaw}”. Use types like Process, Inventory, Supplier, Customer, Supermarket, FIFO, Truck, Kaizen…");
            var e = new VsmElement(kind, -1, -1, name.Length == 0 ? kind.DefaultName() : name);
            var m = e.Metrics;
            m.CycleTime = ParseNumber(Cell(row, cols.Cycle));
            m.ValueCreatingTime = ParseNumber(Cell(row, cols.Vct));
            m.ChangeoverTime = ParseNumber(Cell(row, cols.Changeover));
            m.UptimePercent = ParseNumber(Cell(row, cols.Uptime));
            m.Operators = ParseNumber(Cell(row, cols.Operators)) is double ops ? (int)ops : null;
            m.Shifts = ParseNumber(Cell(row, cols.Shifts)) is double sh ? (int)sh : null;
            var epe = Cell(row, cols.Epe); m.Epex = epe.Length == 0 ? null : epe;
            m.ScrapPercent = ParseNumber(Cell(row, cols.Scrap));
            m.BatchSize = ParseNumber(Cell(row, cols.Batch));
            m.InventoryQty = ParseNumber(Cell(row, cols.Inventory));
            var freq = Cell(row, cols.Frequency); m.Frequency = freq.Length == 0 ? null : freq;
            m.MaxQuantity = ParseNumber(Cell(row, cols.MaxQty));
            e.Note = Cell(row, cols.Note);
            var it = Normalize(Cell(row, cols.It));
            m.UsesITSystem = it is "yes" or "y" or "true" or "1" or "x";
            if (ParseNumber(Cell(row, cols.X)) is double x && ParseNumber(Cell(row, cols.Y)) is double y) { e.X = x; e.Y = y; }
            elements.Add(e);
        }
        return elements;
    }

    private static List<VsmConnection> ParseConnections(List<List<string>> rows, List<VsmElement> elements)
    {
        var result = new List<VsmConnection>();
        if (rows.Count == 0) return result;
        int fromCol = 0, toCol = 1, kindCol = 2, labelCol = 3, start = 0;
        var header = rows[0];
        bool mapped = false;
        for (int i = 0; i < header.Count; i++)
        {
            var h = Normalize(header[i]);
            if (h is "from" or "source") { fromCol = i; mapped = true; }
            else if (h is "to" or "target" or "destination") { toCol = i; mapped = true; }
            else if (h.Contains("flow") || h is "type" or "kind") kindCol = i;
            else if (h.Contains("label") || h.Contains("note")) labelCol = i;
        }
        if (mapped) start = 1;

        var byName = new Dictionary<string, Guid>();
        foreach (var e in elements) byName.TryAdd(Normalize(e.Name), e.Id);

        for (int offset = start; offset < rows.Count; offset++)
        {
            var row = rows[offset];
            var fromName = Cell(row, fromCol);
            var toName = Cell(row, toCol);
            if (fromName.Length == 0 && toName.Length == 0) continue;
            if (!byName.TryGetValue(Normalize(fromName), out var fromId))
                throw new MapImportException($"Connections row {offset + 1}: no element named “{fromName}” exists in the Elements section.");
            if (!byName.TryGetValue(Normalize(toName), out var toId))
                throw new MapImportException($"Connections row {offset + 1}: no element named “{toName}” exists in the Elements section.");
            var kindRaw = Cell(row, kindCol);
            ConnectionKind kind;
            if (kindRaw.Length == 0) kind = ConnectionKind.Push;
            else kind = ConnectionKindFor(kindRaw) ?? throw new MapImportException(
                $"Connections row {offset + 1}: unknown flow type “{kindRaw}”. Use Push, Material, Withdrawal, Manual Information, Electronic Information, or a kanban type.");
            result.Add(new VsmConnection(kind, fromId, toId, Cell(row, labelCol)));
        }
        return result;
    }

    // MARK: Layout

    private static bool IsTopBand(ElementKind k) => k is ElementKind.Customer or ElementKind.Supplier or ElementKind.ScheduleBox
        or ElementKind.LoadLeveling or ElementKind.KanbanPost or ElementKind.KanbanBatch or ElementKind.Phone or ElementKind.GoSeeScheduling;

    private static bool IsMidBand(ElementKind k) => k is ElementKind.KaizenBurst or ElementKind.OperatorIcon
        or ElementKind.Milestone or ElementKind.IterationRework;

    private static void LayOutElements(ValueStreamMap map)
    {
        static bool Needs(VsmElement e) => e.X < 0 || e.Y < 0;
        var material = map.Elements.Where(e => Needs(e) && !IsTopBand(e.Kind) && !IsMidBand(e.Kind)).ToList();
        const double spacing = 170;
        var materialWidth = Math.Max(material.Count * spacing, 900);
        double materialX = 220, supplierX = 140, customerX = materialWidth + 120, infoX = materialWidth * 0.35, midX = 320;
        foreach (var e in map.Elements)
        {
            if (!Needs(e)) continue;
            if (e.Kind == ElementKind.Supplier) { e.X = supplierX; e.Y = 140; supplierX += 180; }
            else if (e.Kind == ElementKind.Customer) { e.X = customerX; e.Y = 140; customerX += 180; }
            else if (IsTopBand(e.Kind)) { e.X = infoX; e.Y = 150; infoX += 150; }
            else if (IsMidBand(e.Kind)) { e.X = midX; e.Y = 380; midX += 160; }
            else { e.X = materialX; e.Y = 560; materialX += spacing; }
        }
    }

    private static List<VsmConnection> AutoChain(List<VsmElement> elements)
    {
        var material = elements.Where(e => !IsTopBand(e.Kind) && !IsMidBand(e.Kind)).ToList();
        var result = new List<VsmConnection>();
        for (int i = 0; i + 1 < material.Count; i++)
            result.Add(new VsmConnection(ConnectionKind.Push, material[i].Id, material[i + 1].Id));
        var supplier = elements.FirstOrDefault(e => e.Kind == ElementKind.Supplier);
        if (supplier != null && material.Count > 0) result.Add(new VsmConnection(ConnectionKind.MaterialFlow, supplier.Id, material[0].Id));
        var customer = elements.FirstOrDefault(e => e.Kind == ElementKind.Customer);
        if (customer != null && material.Count > 0) result.Add(new VsmConnection(ConnectionKind.MaterialFlow, material[^1].Id, customer.Id));
        return result;
    }

    // MARK: Aliases

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static readonly Dictionary<string, ElementKind> KindAliases = new()
    {
        ["customer"] = ElementKind.Customer, ["client"] = ElementKind.Customer,
        ["supplier"] = ElementKind.Supplier, ["vendor"] = ElementKind.Supplier, ["outsidesource"] = ElementKind.Supplier, ["source"] = ElementKind.Supplier,
        ["process"] = ElementKind.Process, ["processstep"] = ElementKind.Process, ["step"] = ElementKind.Process, ["operation"] = ElementKind.Process,
        ["activity"] = ElementKind.Process, ["machine"] = ElementKind.Process, ["workstation"] = ElementKind.Process, ["cell"] = ElementKind.Process,
        ["databox"] = ElementKind.DataBox, ["data"] = ElementKind.DataBox,
        ["crossdock"] = ElementKind.CrossDock, ["xdock"] = ElementKind.CrossDock,
        ["warehouse"] = ElementKind.Warehouse,
        ["truck"] = ElementKind.TruckShipment, ["truckshipment"] = ElementKind.TruckShipment, ["lorry"] = ElementKind.TruckShipment,
        ["train"] = ElementKind.TrainShipment, ["rail"] = ElementKind.TrainShipment,
        ["plane"] = ElementKind.PlaneShipment, ["air"] = ElementKind.PlaneShipment, ["airfreight"] = ElementKind.PlaneShipment, ["flight"] = ElementKind.PlaneShipment,
        ["boat"] = ElementKind.BoatShipment, ["ship"] = ElementKind.BoatShipment, ["sea"] = ElementKind.BoatShipment, ["vessel"] = ElementKind.BoatShipment,
        ["milkrun"] = ElementKind.MilkRun,
        ["expedited"] = ElementKind.ExpeditedTransport, ["express"] = ElementKind.ExpeditedTransport, ["rush"] = ElementKind.ExpeditedTransport,
        ["inventory"] = ElementKind.Inventory, ["stock"] = ElementKind.Inventory, ["wip"] = ElementKind.Inventory,
        ["paperqueue"] = ElementKind.PaperQueue, ["inbox"] = ElementKind.PaperQueue, ["backlog"] = ElementKind.PaperQueue, ["queue"] = ElementKind.PaperQueue,
        ["electronicqueue"] = ElementKind.ElectronicQueue, ["einbox"] = ElementKind.ElectronicQueue, ["electronicinbox"] = ElementKind.ElectronicQueue,
        ["delay"] = ElementKind.Delay, ["wait"] = ElementKind.Delay, ["delaytime"] = ElementKind.Delay,
        ["supermarket"] = ElementKind.Supermarket, ["market"] = ElementKind.Supermarket,
        ["fifo"] = ElementKind.FifoLane, ["fifolane"] = ElementKind.FifoLane,
        ["sla"] = ElementKind.SlaHandoff, ["servicelevel"] = ElementKind.SlaHandoff,
        ["buffer"] = ElementKind.BufferStock, ["bufferstock"] = ElementKind.BufferStock,
        ["safety"] = ElementKind.SafetyStock, ["safetystock"] = ElementKind.SafetyStock,
        ["schedule"] = ElementKind.ScheduleBox, ["schedulebox"] = ElementKind.ScheduleBox, ["information"] = ElementKind.ScheduleBox,
        ["informationbox"] = ElementKind.ScheduleBox, ["mrp"] = ElementKind.ScheduleBox, ["erp"] = ElementKind.ScheduleBox,
        ["loadleveling"] = ElementKind.LoadLeveling, ["heijunka"] = ElementKind.LoadLeveling, ["leveling"] = ElementKind.LoadLeveling,
        ["kanbanpost"] = ElementKind.KanbanPost,
        ["kanbanbatch"] = ElementKind.KanbanBatch, ["kanbanbatches"] = ElementKind.KanbanBatch,
        ["gosee"] = ElementKind.GoSeeScheduling, ["goseescheduling"] = ElementKind.GoSeeScheduling,
        ["phone"] = ElementKind.Phone, ["telephone"] = ElementKind.Phone,
        ["operator"] = ElementKind.OperatorIcon, ["person"] = ElementKind.OperatorIcon, ["worker"] = ElementKind.OperatorIcon,
        ["kaizen"] = ElementKind.KaizenBurst, ["kaizenburst"] = ElementKind.KaizenBurst, ["burst"] = ElementKind.KaizenBurst, ["improvement"] = ElementKind.KaizenBurst,
        ["milestone"] = ElementKind.Milestone, ["pacing"] = ElementKind.Milestone,
        ["iteration"] = ElementKind.IterationRework, ["rework"] = ElementKind.IterationRework,
    };

    public static ElementKind? ElementKindFor(string raw)
    {
        var n = Normalize(raw);
        if (n.Length == 0) return null;
        if (KindAliases.TryGetValue(n, out var k)) return k;
        foreach (var kind in ElementKindInfo.All)
            if (Normalize(kind.DisplayName()) == n || Normalize(kind.ToString()) == n) return kind;
        return null;
    }

    public static ConnectionKind? ConnectionKindFor(string raw)
    {
        var n = Normalize(raw);
        if (n.Length == 0) return null;
        if (n.Contains("withdrawalkanban")) return ConnectionKind.WithdrawalKanban;
        if (n.Contains("productionkanban")) return ConnectionKind.ProductionKanban;
        if (n.Contains("signalkanban") || n.Contains("signal")) return ConnectionKind.SignalKanban;
        if (n == "kanban") return ConnectionKind.ProductionKanban;
        if (n.Contains("withdrawal") || n.Contains("pull")) return ConnectionKind.Withdrawal;
        if (n.Contains("push")) return ConnectionKind.Push;
        if (n.Contains("electronic") || n.Contains("edi") || n == "einfo") return ConnectionKind.ElectronicInfo;
        if (n.Contains("manual") || n.Contains("information") || n == "info") return ConnectionKind.ManualInfo;
        if (n.Contains("material") || n.Contains("finishedgoods") || n.Contains("shipment") || n.Contains("transport")) return ConnectionKind.MaterialFlow;
        foreach (var kind in ConnectionKindInfo.All)
            if (Normalize(kind.DisplayName()) == n || Normalize(kind.ToString()) == n) return kind;
        return null;
    }

    /// <summary>Accepts "460", "460.5", "460,5", "2 300" — both decimal styles.</summary>
    public static double? ParseNumber(string raw)
    {
        var s = raw.Trim().Replace(" ", "").Replace(" ", "");
        if (s.Length == 0) return null;
        var inv = CultureInfo.InvariantCulture;
        if (double.TryParse(s, NumberStyles.Float, inv, out var d)) return d;
        if (s.Contains(',') && !s.Contains('.') && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, inv, out d)) return d;
        if (double.TryParse(s.Replace(",", ""), NumberStyles.Float, inv, out d)) return d;
        return null;
    }

    // MARK: Bundled template

    public const string TemplateCsv =
"""
# ValueStreamMapper import template
# Fill in the sections below, save as CSV (or keep the same columns in Excel
# sheets named Map / Elements / Connections), then import the file in the app.
# The Connections section is optional: without it the elements are chained
# left-to-right in the order they are listed. X,Y positions are optional too.
[Map]
Title,My Value Stream
Product Family,Widget family A
State,Current State
Customer Demand (pcs/day),460
Available Time (min/day),460

[Elements]
Name,Type,Cycle Time (s),Value-Creating Time (s),Changeover (s),Uptime (%),Operators,EPE,Scrap (%),Inventory (pcs),Frequency,Max Qty,X,Y,Note
Steel Co.,Supplier,,,,,,,,,2x week,,,,
Coils,Inventory,,,,,,,,2300,,,,,
Stamping,Process,1,1,3600,85,1,2 weeks,,,,,,,
Stamped,Inventory,,,,,,,,1600,,,,,
Welding,Process,39,35,600,100,1,,,,,,,,
Welded,Inventory,,,,,,,,1100,,,,,
Assembly,Process,46,40,,100,2,,,,,,,,
Finished,Inventory,,,,,,,,2700,,,,,
Shipping,Truck,,,,,,,,,1x day,,,,
Customer Plant,Customer,,,,,,,,,1x day,,,,

[Connections]
From,To,Flow Type,Label
Customer Plant,Stamping,Electronic Information,Orders
Steel Co.,Coils,Material,
Coils,Stamping,Push,
Stamping,Stamped,Push,
Stamped,Welding,Push,
Welding,Welded,Push,
Welded,Assembly,Push,
Assembly,Finished,Push,
Finished,Shipping,Material,
Shipping,Customer Plant,Material,
""";
}
