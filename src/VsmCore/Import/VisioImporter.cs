// Imports Microsoft Visio drawings (.vsdx). Shapes are recognised by their
// master (stencil) names, data-box metrics come from shape text and Shape
// Data, and connectors become flows based on which shapes they are glued to.

using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace VsmCore.Import;

public static class VisioImporter
{
    public static bool IsVisio(byte[] data)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            return zip.GetEntry("visio/document.xml") != null || zip.GetEntry("visio/pages/pages.xml") != null;
        }
        catch { return false; }
    }

    public sealed record PageRef(string Id, string Name, string RelId, double Width, double Height);

    public sealed class Shape
    {
        public string Id = "", MasterId = "", NameU = "", Text = "";
        public Dictionary<string, string> Cells = new();
        public List<(string Label, string Value)> Properties = new();
        public bool IsConnector => Cells.ContainsKey("BeginX") || Cells.ContainsKey("EndX");
    }

    public sealed class PageContent
    {
        public List<Shape> Shapes = new();
        public Dictionary<string, string> ChildToTop = new();
        public List<(string FromSheet, string FromCell, string ToSheet)> Connects = new();
    }

    public static ValueStreamMap ImportMap(byte[] data, string fallbackTitle)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var masters = XlsxReader.Entry(zip, "visio/masters/masters.xml") is byte[] m ? ParseMasters(m) : new();
        var pagesXml = XlsxReader.Entry(zip, "visio/pages/pages.xml") ?? throw new MapImportException("The Visio drawing has no pages.");
        var pages = ParsePages(pagesXml);
        if (pages.Count == 0) throw new MapImportException("The Visio drawing has no pages.");
        var rels = XlsxReader.ParseRelationships(XlsxReader.Entry(zip, "visio/pages/_rels/pages.xml.rels") ?? Array.Empty<byte>());

        (PageRef Page, PageContent Content, int Score)? best = null;
        foreach (var page in pages)
        {
            if (!rels.TryGetValue(page.RelId, out var target)) continue;
            var path = target.StartsWith('/') ? target[1..] : "visio/pages/" + target;
            if (XlsxReader.Entry(zip, path) is not byte[] xml) continue;
            var content = ParsePage(xml);
            var score = content.Shapes.Count(s => !s.IsConnector && ElementKindFor(MasterName(s, masters), s, page.Width) != null);
            if (best == null || score > best.Value.Score) best = (page, content, score);
        }
        if (best == null || best.Value.Score == 0) throw new MapImportException(
            "No value-stream shapes were recognised. Use shapes from Visio's “Value Stream Map Shapes” stencil (Process, Inventory, Customer/Supplier, Supermarket, Shipment, Push arrow…).");
        return Build(best.Value.Page, best.Value.Content, masters, fallbackTitle);
    }

    private static ValueStreamMap Build(PageRef page, PageContent content, Dictionary<string, string> masters, string fallbackTitle)
    {
        var map = new ValueStreamMap(PageTitle(page.Name) ?? fallbackTitle) { AvailableTimePerDay = 27_600 };
        var pageHeight = page.Height > 0 ? page.Height : content.Shapes.Select(s => Num(s.Cells, "PinY")).DefaultIfEmpty(8).Max() + 1;
        var pageWidth = page.Width > 0 ? page.Width : content.Shapes.Select(s => Num(s.Cells, "PinX")).DefaultIfEmpty(11).Max() + 1;

        var byShape = new Dictionary<string, Guid>();
        foreach (var shape in content.Shapes.Where(s => !s.IsConnector))
        {
            var master = MasterName(shape, masters);
            if (ElementKindFor(master, shape, pageWidth) is not ElementKind kind) continue;
            var text = new VisioText(shape.Text);
            var e = new VsmElement(kind, Num(shape.Cells, "PinX") * 100, (pageHeight - Num(shape.Cells, "PinY")) * 100, text.Name ?? kind.DefaultName());
            ApplyTextMetrics(text, e);
            ApplyProperties(shape.Properties, e);
            var nm = MapImportService.Normalize(master);
            if (nm.Contains("productioncontrol") || nm.Contains("mrp") || Regex.IsMatch(text.Raw, @"\b(MRP|ERP)\b")) e.Metrics.UsesITSystem = true;
            if (kind == ElementKind.Customer && text.DemandPerDay is double demand) map.CustomerDemandPerDay = demand;
            if (text.AvailableTimeSeconds is double avail) map.AvailableTimePerDay = avail;
            map.Elements.Add(e);
            byShape[shape.Id] = e.Id;
        }
        if (map.Elements.Count > 0)
        {
            var minX = map.Elements.Min(e => e.X); var minY = map.Elements.Min(e => e.Y);
            foreach (var e in map.Elements) { e.X += 120 - minX; e.Y += 120 - minY; }
        }

        var begins = new Dictionary<string, string>(); var ends = new Dictionary<string, string>();
        foreach (var (from, cell, to) in content.Connects)
        {
            var top = content.ChildToTop.TryGetValue(to, out var t) ? t : to;
            if (cell == "BeginX") begins[from] = top; else if (cell == "EndX") ends[from] = top;
        }
        foreach (var shape in content.Shapes.Where(s => s.IsConnector))
        {
            if (!begins.TryGetValue(shape.Id, out var fromShape) || !ends.TryGetValue(shape.Id, out var toShape)) continue;
            if (!byShape.TryGetValue(fromShape, out var fromId) || !byShape.TryGetValue(toShape, out var toId) || fromId == toId) continue;
            map.Connections.Add(new VsmConnection(ConnectionKindFor(MasterName(shape, masters)), fromId, toId, new VisioText(shape.Text).Name ?? ""));
        }
        return map;
    }

    private static double Num(Dictionary<string, string> cells, string key) =>
        cells.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static string MasterName(Shape shape, Dictionary<string, string> masters) =>
        masters.TryGetValue(shape.MasterId, out var n) && n.Length > 0 ? n : shape.NameU;

    private static string? PageTitle(string name)
    {
        var t = name.Trim();
        if (t.Length == 0) return null;
        return Regex.IsMatch(t, @"^(page|pagina|seite|página|pág\.?|feuille)[- ]?\d*$", RegexOptions.IgnoreCase) ? null : t;
    }

    // MARK: Classification

    public static ElementKind? ElementKindFor(string master, Shape shape, double pageWidth)
    {
        var n = MapImportService.Normalize(master);
        if (n.Length == 0) return null;
        var text = MapImportService.Normalize(shape.Text);

        if (n.Contains("timeline") || n.Contains("sequencedpull") || n.Contains("legend") || n.Contains("title") || n.Contains("connector") || n.Contains("arrow"))
            return null;
        if (n.Contains("customersupplier") || n.Contains("outsidesource") || n.Contains("customer") || n.Contains("supplier") || n.Contains("fornitore") || n.Contains("cliente"))
        {
            if (text.Contains("supplier") || text.Contains("vendor") || text.Contains("fornitore")) return ElementKind.Supplier;
            if (text.Contains("customer") || text.Contains("client") || text.Contains("cliente")) return ElementKind.Customer;
            if ((n.Contains("supplier") && !n.Contains("customer")) || n.Contains("fornitore")) return ElementKind.Supplier;
            if ((n.Contains("customer") && !n.Contains("supplier")) || n.Contains("cliente")) return ElementKind.Customer;
            return Num(shape.Cells, "PinX") > pageWidth / 2 ? ElementKind.Customer : ElementKind.Supplier;
        }
        if (n.Contains("kaizen")) return ElementKind.KaizenBurst;
        if (n.Contains("databox")) return ElementKind.DataBox;
        if (n.Contains("supermarket") || n.Contains("supermercato")) return ElementKind.Supermarket;
        if (n.Contains("safetystock")) return ElementKind.SafetyStock;
        if (n.Contains("bufferstock") || n == "buffer") return ElementKind.BufferStock;
        if (n.Contains("fifo")) return ElementKind.FifoLane;
        if (n.Contains("inventory") || n.Contains("scorte") || n.Contains("magazzino") || n == "stock" || n.Contains("wip")) return ElementKind.Inventory;
        if (n.Contains("airshipment") || n == "air" || n.Contains("plane") || n.Contains("aereo")) return ElementKind.PlaneShipment;
        if (n.Contains("railshipment") || n == "rail" || n.Contains("train") || n.Contains("treno")) return ElementKind.TrainShipment;
        if (n.Contains("seashipment") || n == "sea" || n.Contains("boat") || n.Contains("nave") || n == "ship") return ElementKind.BoatShipment;
        if (n.Contains("milkrun")) return ElementKind.MilkRun;
        if (n.Contains("expedit")) return ElementKind.ExpeditedTransport;
        if (n.Contains("shipment") || n.Contains("truck") || n.Contains("spedizione") || n.Contains("camion")) return ElementKind.TruckShipment;
        if (n.Contains("crossdock")) return ElementKind.CrossDock;
        if (n.Contains("warehouse")) return ElementKind.Warehouse;
        if (n.Contains("operator") || n.Contains("operatore")) return ElementKind.OperatorIcon;
        if (n.Contains("loadleveling") || n.Contains("heijunka")) return ElementKind.LoadLeveling;
        if (n.Contains("kanbanpost")) return ElementKind.KanbanPost;
        if (n.Contains("kanban")) return ElementKind.KanbanBatch;
        if (n.Contains("gosee")) return ElementKind.GoSeeScheduling;
        if (n.Contains("phone") || n.Contains("telefono")) return ElementKind.Phone;
        if (n.Contains("milestone")) return ElementKind.Milestone;
        if (n.Contains("rework") || n.Contains("iteration")) return ElementKind.IterationRework;
        if (n.Contains("delay")) return ElementKind.Delay;
        if (n.Contains("sla") || n.Contains("servicelevel")) return ElementKind.SlaHandoff;
        if (n.Contains("inbox") || n.Contains("backlog") || n.Contains("queue")) return ElementKind.PaperQueue;
        if (n.Contains("productioncontrol") || n == "mrp" || n == "erp") return ElementKind.Process;
        if (n.Contains("information") || n.Contains("schedule") || n.Contains("programmazione") || n.Contains("informazioni")) return ElementKind.ScheduleBox;
        if (n.Contains("process") || n.Contains("workcell") || n.Contains("processo") || n.Contains("operation")) return ElementKind.Process;
        return MapImportService.ElementKindFor(master);
    }

    public static ConnectionKind ConnectionKindFor(string master)
    {
        var n = MapImportService.Normalize(master);
        if (n.Contains("withdrawalkanban")) return ConnectionKind.WithdrawalKanban;
        if (n.Contains("productionkanban")) return ConnectionKind.ProductionKanban;
        if (n.Contains("signalkanban")) return ConnectionKind.SignalKanban;
        if (n.Contains("push")) return ConnectionKind.Push;
        if (n.Contains("electronic")) return ConnectionKind.ElectronicInfo;
        if (n.Contains("manual") || n.Contains("information") || n.Contains("info")) return ConnectionKind.ManualInfo;
        if (n.Contains("pull") || n.Contains("withdrawal")) return ConnectionKind.Withdrawal;
        return ConnectionKind.MaterialFlow;
    }

    // MARK: Metrics

    private static void ApplyTextMetrics(VisioText text, VsmElement e)
    {
        var m = text.Metrics;
        if (m.CycleTime is double ct) e.Metrics.CycleTime = ct;
        if (m.ValueCreatingTime is double vct) e.Metrics.ValueCreatingTime = vct;
        if (m.Changeover is double co) e.Metrics.ChangeoverTime = co;
        if (m.Uptime is double up) e.Metrics.UptimePercent = up;
        if (m.Operators is int ops) e.Metrics.Operators = ops;
        if (m.Scrap is double sc) e.Metrics.ScrapPercent = sc;
        if (m.Epe is string epe) e.Metrics.Epex = epe;
        if (e.Kind.HoldsInventory() && m.Quantity is double q) e.Metrics.InventoryQty = q;
        if (e.Kind is ElementKind.TruckShipment or ElementKind.TrainShipment or ElementKind.PlaneShipment or ElementKind.BoatShipment
            or ElementKind.MilkRun or ElementKind.ExpeditedTransport or ElementKind.Supplier or ElementKind.Customer or ElementKind.ScheduleBox)
            e.Metrics.Frequency = text.Frequency;
    }

    private static void ApplyProperties(List<(string Label, string Value)> props, VsmElement e)
    {
        foreach (var (label, raw) in props)
        {
            var key = MapImportService.Normalize(label);
            var value = raw.Trim();
            if (value.Length == 0) continue;
            var number = VisioText.FirstNumber(value);
            if (key.Contains("valuecreating") || key.StartsWith("vct")) e.Metrics.ValueCreatingTime = VisioText.Duration(value) ?? number;
            else if (key.Contains("cycle") || key == "ct") e.Metrics.CycleTime = VisioText.Duration(value) ?? number;
            else if (key.Contains("changeover") || key == "co" || key.Contains("setup")) e.Metrics.ChangeoverTime = VisioText.Duration(value) ?? number;
            else if (key.Contains("uptime") || key.Contains("availab")) e.Metrics.UptimePercent = number;
            else if (key.Contains("operator") || key.Contains("staff")) e.Metrics.Operators = number is double o ? (int)o : null;
            else if (key.Contains("shift")) e.Metrics.Shifts = number is double s ? (int)s : null;
            else if (key.StartsWith("epe")) e.Metrics.Epex = value;
            else if (key.Contains("scrap") || key.Contains("defect")) e.Metrics.ScrapPercent = number;
            else if (key.Contains("batch")) e.Metrics.BatchSize = number;
            else if (key.Contains("invent") || key.Contains("quantity") || key == "qty" || key.Contains("pieces")) e.Metrics.InventoryQty = number;
            else if (key.Contains("frequen")) e.Metrics.Frequency = value;
            else if (key.Contains("note") || key.Contains("comment")) e.Note = value;
        }
    }

    // MARK: XML parts

    private static Dictionary<string, string> ParseMasters(byte[] xml)
    {
        var masters = new Dictionary<string, string>();
        using var r = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "Master" && r.GetAttribute("ID") is string id)
                masters[id] = r.GetAttribute("NameU") ?? r.GetAttribute("Name") ?? "";
        return masters;
    }

    private static List<PageRef> ParsePages(byte[] xml)
    {
        var pages = new List<PageRef>();
        using var r = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        string id = "", name = "", relId = ""; double w = 0, h = 0; bool inPage = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "Page")
                {
                    inPage = true; id = r.GetAttribute("ID") ?? ""; name = r.GetAttribute("Name") ?? r.GetAttribute("NameU") ?? ""; relId = ""; w = 0; h = 0;
                    if (r.IsEmptyElement) { pages.Add(new PageRef(id, name, relId, w, h)); inPage = false; }
                }
                else if (inPage && r.LocalName == "Rel")
                {
                    for (int i = 0; i < r.AttributeCount; i++) { r.MoveToAttribute(i); if (r.LocalName == "id") relId = r.Value; }
                    r.MoveToElement();
                }
                else if (inPage && r.LocalName == "Cell")
                {
                    var n = r.GetAttribute("N");
                    if (double.TryParse(r.GetAttribute("V") ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        if (n == "PageWidth") w = v; else if (n == "PageHeight") h = v;
                    }
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "Page" && inPage)
            {
                pages.Add(new PageRef(id, name, relId, w, h)); inPage = false;
            }
        }
        return pages;
    }

    public static PageContent ParsePage(byte[] xml)
    {
        var content = new PageContent();
        using var r = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        int shapeDepth = 0, sectionDepth = 0, propertyDepth = 0;
        bool inText = false;
        string rowLabel = "", rowValue = "", rowName = "";
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                bool empty = r.IsEmptyElement;
                switch (r.LocalName)
                {
                    case "Shape":
                    {
                        shapeDepth++;
                        var id = r.GetAttribute("ID") ?? "";
                        if (shapeDepth == 1)
                        {
                            content.Shapes.Add(new Shape { Id = id, MasterId = r.GetAttribute("Master") ?? "", NameU = r.GetAttribute("NameU") ?? r.GetAttribute("Name") ?? "" });
                            content.ChildToTop[id] = id;
                        }
                        else if (content.Shapes.Count > 0) content.ChildToTop[id] = content.Shapes[^1].Id;
                        if (empty) shapeDepth--;
                        break;
                    }
                    case "Section":
                        sectionDepth++;
                        if (r.GetAttribute("N") == "Property") propertyDepth = sectionDepth;
                        if (empty) { if (sectionDepth == propertyDepth) propertyDepth = 0; sectionDepth--; }
                        break;
                    case "Row":
                        if (propertyDepth > 0) { rowLabel = ""; rowValue = ""; rowName = r.GetAttribute("N") ?? ""; }
                        break;
                    case "Cell":
                    {
                        if (shapeDepth < 1) break;
                        var n = r.GetAttribute("N"); var v = r.GetAttribute("V") ?? "";
                        if (n == null) break;
                        if (propertyDepth > 0) { if (n == "Label") rowLabel = v; if (n == "Value") rowValue = v; }
                        else if (sectionDepth == 0 && shapeDepth == 1 && content.Shapes.Count > 0) content.Shapes[^1].Cells[n] = v;
                        break;
                    }
                    case "Text": inText = shapeDepth >= 1 && !empty; break;
                    case "Connect":
                        content.Connects.Add((r.GetAttribute("FromSheet") ?? "", r.GetAttribute("FromCell") ?? "", r.GetAttribute("ToSheet") ?? ""));
                        break;
                }
            }
            else if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                if (inText && content.Shapes.Count > 0) content.Shapes[^1].Text += r.Value;
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                switch (r.LocalName)
                {
                    case "Shape": shapeDepth--; break;
                    case "Section": if (sectionDepth == propertyDepth) propertyDepth = 0; sectionDepth--; break;
                    case "Row":
                        if (propertyDepth > 0 && content.Shapes.Count > 0 && !(rowLabel.Length == 0 && rowName.Length == 0))
                            content.Shapes[^1].Properties.Add((rowLabel.Length == 0 ? rowName : rowLabel, rowValue));
                        break;
                    case "Text": if (inText && content.Shapes.Count > 0) content.Shapes[^1].Text += "\n"; inText = false; break;
                }
            }
        }
        return content;
    }
}

/// <summary>Pulls a name, data-box metrics and frequencies out of free shape text.</summary>
public sealed class VisioText
{
    public sealed class MetricSet
    {
        public double? CycleTime, ValueCreatingTime, Changeover, Uptime, Scrap, Quantity;
        public int? Operators;
        public string? Epe;
    }

    public string Raw { get; }
    public List<string> Lines { get; }
    public MetricSet Metrics { get; } = new();
    public string? Name { get; private set; }
    public string? Frequency { get; private set; }
    public double? DemandPerDay { get; private set; }
    public double? AvailableTimeSeconds { get; private set; }

    private const string Unit = "(s|sec|secs|seconds|min|mins|minutes|h|hr|hrs|hours)?";
    private const string Num = @"(\d+(?:[.,]\d+)?)";
    private static readonly RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly (string Key, Regex Regex)[] Patterns =
    {
        ("vct", new Regex(@"\b(?:vct|va\s*time|value[- ]?(?:creating|added?)(?:\s*time)?)\b\s*[:=]?\s*" + Num + @"\s*" + Unit + @"\b", Opt)),
        ("ct", new Regex(@"\b(?:c\s*/\s*t|cycle\s*time|ct)\b\s*[:=]?\s*" + Num + @"\s*" + Unit + @"\b", Opt)),
        ("co", new Regex(@"\b(?:c\s*/\s*o|change\s*over|setup(?:\s*time)?|co)\b\s*[:=]?\s*" + Num + @"\s*" + Unit + @"\b", Opt)),
        ("uptime", new Regex(@"\b(?:uptime|up\s*time|availability)\b\s*[:=]?\s*" + Num + @"\s*%?", Opt)),
        ("ops", new Regex(@"\b(?:operators?|ops?|people|persons?)\b\s*[:=]?\s*(\d+)\b", Opt)),
        ("ops2", new Regex(@"\b(\d+)\s*(?:operators?|ops?|people|persons?)\b", Opt)),
        ("scrap", new Regex(@"\b(?:scrap|defects?|rejects?)\b\s*(?:rate)?\s*[:=]?\s*" + Num + @"\s*%?", Opt)),
        ("epe", new Regex(@"\b(?:epe[ix]?|every\s*part\s*every)\b\s*[:=]?\s*([^\n]+)", Opt)),
        ("demand", new Regex(@"\b(?:demand|domanda)\b\s*[:=]?\s*" + Num, Opt)),
        ("perday", new Regex(Num + @"\s*(?:pcs|pieces|units|parts|pz)?\s*/\s*(?:day|giorno|d)\b", Opt)),
        ("available", new Regex(@"\bavailable(?:\s*(?:work(?:ing)?\s*)?time)?\b\s*[:=]?\s*" + Num + @"\s*" + Unit + @"\b", Opt)),
        ("qty", new Regex(Num + @"\s*(?:pcs|pieces|units|parts|pz)\b", Opt)),
    };

    public VisioText(string text)
    {
        Raw = text;
        Lines = text.Replace("\r", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var consumed = new HashSet<int>();
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            foreach (var (key, regex) in Patterns)
            {
                var m = regex.Match(line);
                if (!m.Success) continue;
                var value = m.Groups[1].Value;
                var unit = m.Groups.Count > 2 ? m.Groups[2].Value : "";
                switch (key)
                {
                    case "vct": Metrics.ValueCreatingTime ??= Seconds(value, unit); break;
                    case "ct": Metrics.CycleTime ??= Seconds(value, unit); break;
                    case "co": Metrics.Changeover ??= Seconds(value, unit); break;
                    case "uptime": Metrics.Uptime ??= Number(value); break;
                    case "ops": case "ops2": Metrics.Operators ??= Number(value) is double o ? (int)o : null; break;
                    case "scrap": Metrics.Scrap ??= Number(value); break;
                    case "epe": Metrics.Epe ??= value.Trim(); break;
                    case "demand": case "perday": DemandPerDay ??= Number(value); break;
                    case "available": AvailableTimeSeconds ??= Seconds(value, unit.Length == 0 ? "min" : unit); break;
                    case "qty": Metrics.Quantity ??= Number(value); break;
                }
                consumed.Add(i);
            }
            if (!consumed.Contains(i) && Regex.IsMatch(line, @"^[\d.,]+$") && Number(line) is double n)
            {
                Metrics.Quantity ??= n;
                consumed.Add(i);
            }
        }
        var remaining = Lines.Where((_, i) => !consumed.Contains(i)).ToList();
        Name = remaining.FirstOrDefault();
        Frequency = remaining.Skip(1).FirstOrDefault(LooksLikeFrequency);
        if (Frequency == null && remaining.Count == 1 && LooksLikeFrequency(remaining[0])) { Frequency = remaining[0]; Name = null; }
    }

    public static double? Number(string s) => MapImportService.ParseNumber(s);

    public static double? Seconds(string value, string unit)
    {
        if (Number(value) is not double n) return null;
        return unit.ToLowerInvariant() switch
        {
            "min" or "mins" or "minutes" => n * 60,
            "h" or "hr" or "hrs" or "hours" => n * 3600,
            _ => n,
        };
    }

    public static double? Duration(string text)
    {
        var m = Regex.Match(text, Num + @"\s*" + Unit + @"\b", Opt);
        if (!m.Success || m.Groups[2].Length == 0) return null;
        return Seconds(m.Groups[1].Value, m.Groups[2].Value);
    }

    public static double? FirstNumber(string text)
    {
        var m = Regex.Match(text, Num);
        return m.Success ? Number(m.Groups[1].Value) : null;
    }

    private static bool LooksLikeFrequency(string line) =>
        Regex.IsMatch(line, @"(daily|weekly|monthly|day|week|month|shift|\d\s*[x×]|[x×]\s*\d|per\b|giorn|settiman|mens)", Opt);
}
