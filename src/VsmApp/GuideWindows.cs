// Import guide (how to lay out a spreadsheet, with rendered examples) and
// the in-app reference glossary.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using VsmCore;
using VsmCore.Import;

namespace VsmApp;

public sealed class ImportGuideWindow : Window
{
    public ImportGuideWindow(Func<Task> onImport, Func<Task> onTemplate)
    {
        Title = "Import Guide"; Width = 760; Height = 760;
        var ex = ImportGuideExamples.FromTemplate();
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18, MaxWidth = 700 };

        panel.Children.Add(new TextBlock { Text = "Build a map from a spreadsheet", FontSize = 22, FontWeight = FontWeight.Bold });
        panel.Children.Add(Para("Keep your process data in Excel? Put it in three sheets — Map, Elements and Connections — and the app draws the value stream for you. Only the Elements sheet is required."));
        var steps = new StackPanel { Spacing = 8 };
        steps.Children.Add(Step(1, "Get the template", "Open it in Excel. It already contains a complete example map."));
        steps.Children.Add(Step(2, "Replace the example rows", "Keep the header rows and sheet names; add one row per process, stock, supplier…"));
        steps.Children.Add(Step(3, "Save and import", "Save as .xlsx or .csv, then use Import… in the sidebar."));
        panel.Children.Add(Card(steps));

        panel.Children.Add(Sheet(1, "Map", "optional · one key per row", null, ex.Map, new[]
        {
            ("Title / Product Family / State", "Free text. State is Current, Future or Ideal."),
            ("Customer Demand (pcs/day)", "Pieces the customer needs per day."),
            ("Available Time (min/day)", "Working minutes per day. Together with demand this gives takt time."),
        }));
        panel.Children.Add(Sheet(2, "Elements", "required · one row per icon on the map", ex.ElementColumns, ex.Elements, new[]
        {
            ("Name", "Label shown on the map. Must be unique if you connect it later."),
            ("Type", "Which icon to draw — see the list below."),
            ("Cycle Time · Value-Creating Time · Changeover", "Data-box times, in seconds."),
            ("Uptime (%) · Scrap (%)", "Percentages, e.g. 85."),
            ("Operators · EPE", "People at the step; batch interval such as “2 weeks”."),
            ("Inventory (pcs)", "Pieces held — for Inventory, Supermarket, FIFO, Buffer and Safety stock rows."),
            ("Frequency", "For shipments, suppliers and customers, e.g. “1x day”, “Mon + Wed”."),
            ("X · Y", "Optional canvas position. Leave empty and the app lays the map out for you."),
        }));

        var types = new StackPanel { Spacing = 6 };
        types.Children.Add(new TextBlock { Text = "Accepted values for Type", FontWeight = FontWeight.SemiBold });
        foreach (var cat in Enum.GetValues<PaletteCategory>())
        {
            types.Children.Add(new TextBlock { Text = cat.Title(), FontSize = 11, Foreground = Ui.InkSecondary });
            var wrap = new WrapPanel();
            foreach (var k in cat.Kinds()) wrap.Children.Add(Chip(TypeLabel(k)));
            types.Children.Add(wrap);
        }
        types.Children.Add(Para("Spelling is forgiving: “process”, “Truck”, “Stock” or “Kaizen” all work. An unknown type stops the import and tells you the row."));
        panel.Children.Add(Card(types));

        panel.Children.Add(Sheet(3, "Connections", "optional · one row per arrow", ex.ConnectionColumns, ex.Connections, new[]
        {
            ("From · To", "Element names exactly as written on the Elements sheet (case doesn’t matter)."),
            ("Flow Type", "Which arrow to draw — see the list below. Empty means Push."),
            ("Label", "Optional text on the arrow, e.g. “Orders”, “Weekly fax”."),
        }));

        var flows = new StackPanel { Spacing = 6 };
        flows.Children.Add(new TextBlock { Text = "Accepted values for Flow Type", FontWeight = FontWeight.SemiBold });
        foreach (var k in ConnectionKindInfo.All)
            flows.Children.Add(new DockPanel { Children = { new Border { Child = Chip(FlowLabel(k)), Width = 170, [DockPanel.DockProperty] = Dock.Left }, new TextBlock { Text = k.GuidanceNote(), FontSize = 11, Foreground = Ui.InkSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } } });
        panel.Children.Add(Card(flows));

        panel.Children.Add(Card(Info("No Connections sheet? That's fine.", "The app chains the elements left to right in the order you listed them: supplier → first step → … → last step → customer, using push arrows. Add the Connections sheet when you need pull loops, kanban or information flows.")));
        var csv = Info("Prefer a single CSV file?", "Put all three blocks in one file, each introduced by a line in square brackets. The template is exactly this shape. Commas, semicolons (as Excel saves in Italy and most of Europe) and tabs are detected automatically, and “12,5” is read as 12.5.");
        csv.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(236, 236, 241)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Child = new TextBlock { Text = ex.CsvSnippet, FontFamily = Ui.Mono, FontSize = 10.5 } });
        panel.Children.Add(Card(csv));
        panel.Children.Add(Card(Info("Also accepted by Import…", "• A plain table with just Name and Type columns, no sections — including this app's own CSV export.\n• Microsoft Visio drawings (.vsdx) made with the Value Stream Map stencil.\n• .vsmap documents from the iPhone, iPad or Mac app — the same file format.")));
        panel.Children.Add(Card(Info("Common mistakes", "• A Connections row names an element that isn't on the Elements sheet — check spelling.\n• Times typed with units in numeric columns (“39 s”). Use plain numbers; the unit is in the header.\n• Header row deleted. The importer finds columns by their names, not their order.\n• Demand or available time left empty — the map still imports, but takt time stays blank until set in Map Settings.")));

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var tpl = new Button { Content = "⎘ Get Template" }; tpl.Click += async (_, _) => await onTemplate();
        var imp = new Button { Content = "⤓ Import a File…" }; imp.Click += async (_, _) => { Close(); await onImport(); };
        bar.Children.Add(tpl); bar.Children.Add(imp);
        panel.Children.Add(bar);
        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Para(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = Ui.InkSecondary };
    private static Border Card(Control c) => new() { Background = new SolidColorBrush(Color.FromRgb(242, 242, 247)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = c };
    private static Border Chip(string t) => new() { Background = Ui.AccentSoft, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeight.Medium } };
    private static StackPanel Info(string title, string body) => new() { Spacing = 6, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.InkSecondary } } };

    private static Control Step(int n, string title, string detail) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 12,
        Children =
        {
            new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Accent, Child = new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            new StackPanel { Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = detail, FontSize = 11, Foreground = Ui.InkSecondary } } },
        },
    };

    private static Control Sheet(int n, string title, string subtitle, List<string>? header, List<List<string>> rows, (string, string)[] notes)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = $"Sheet {n} · {title}", FontWeight = FontWeight.Bold, FontSize = 15 }, new TextBlock { Text = subtitle, FontSize = 11, Foreground = Ui.InkSecondary, VerticalAlignment = VerticalAlignment.Bottom } } });
        p.Children.Add(Para(n == 1 ? "In a CSV file this is the block under a [Map] line." : $"In a CSV file this is the block under a [{title}] line. The first row must be the column headers."));
        p.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = SpreadsheetGrid(header, rows, title) });
        foreach (var (k, v) in notes)
            p.Children.Add(new DockPanel { Children = { new TextBlock { Text = k, FontSize = 11, FontWeight = FontWeight.SemiBold, Width = 200, [DockPanel.DockProperty] = Dock.Left }, new TextBlock { Text = v, FontSize = 11, Foreground = Ui.InkSecondary, TextWrapping = TextWrapping.Wrap } } });
        return p;
    }

    /// <summary>An Excel-like grid: column letters, row numbers, tinted header row, sheet tab.</summary>
    private static Control SpreadsheetGrid(List<string>? header, List<List<string>> rows, string sheetName)
    {
        var all = header != null ? new[] { header }.Concat(rows).ToList() : rows;
        var cols = all.Count == 0 ? 0 : all.Max(r => r.Count);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r <= all.Count; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var line = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)); var headBg = new SolidColorBrush(Color.FromRgb(236, 236, 241)); var tint = new SolidColorBrush(Color.FromArgb(36, 52, 199, 89));
        Border Cell(string text, IBrush bg, bool bold, double minWidth = 54) => new()
        {
            BorderBrush = line, BorderThickness = new Thickness(0.5), Background = bg, Padding = new Thickness(6, 2), MinWidth = minWidth,
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Foreground = bg == headBg ? Ui.InkSecondary : Ui.Ink },
        };
        void Put(Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); grid.Children.Add(c); }
        Put(Cell("", headBg, false, 28), 0, 0);
        for (int c = 0; c < cols; c++) Put(Cell(ColumnLetter(c), headBg, false), 0, c + 1);
        for (int r = 0; r < all.Count; r++)
        {
            Put(Cell((r + 1).ToString(), headBg, false, 28), r + 1, 0);
            bool isHeader = header != null && r == 0;
            for (int c = 0; c < cols; c++) Put(Cell(c < all[r].Count ? all[r][c] : "", isHeader ? tint : Brushes.White, isHeader || (header == null && c == 0)), r + 1, c + 1);
        }
        var tab = new Border { Background = Brushes.White, BorderBrush = Ui.Success, BorderThickness = new Thickness(0, 2, 0, 0), Padding = new Thickness(10, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = sheetName, FontSize = 9, FontWeight = FontWeight.SemiBold } };
        return new Border { BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Brushes.White, Child = new StackPanel { Children = { grid, new Border { Background = headBg, Child = tab } } } };
    }

    private static string ColumnLetter(int index)
    {
        var i = index + 1; var s = "";
        while (i > 0) { var r = (i - 1) % 26; s = (char)('A' + r) + s; i = (i - 1) / 26; }
        return s;
    }

    private static string TypeLabel(ElementKind k) => k switch
    {
        ElementKind.Supplier => "Supplier", ElementKind.TruckShipment => "Truck", ElementKind.TrainShipment => "Train", ElementKind.PlaneShipment => "Plane",
        ElementKind.BoatShipment => "Boat", ElementKind.PaperQueue => "Paper Queue", ElementKind.ElectronicQueue => "Electronic Queue", ElementKind.FifoLane => "FIFO",
        ElementKind.SlaHandoff => "SLA", ElementKind.ScheduleBox => "Schedule", ElementKind.GoSeeScheduling => "Go See", ElementKind.OperatorIcon => "Operator",
        ElementKind.KaizenBurst => "Kaizen", ElementKind.IterationRework => "Rework", _ => k.DisplayName(),
    };

    private static string FlowLabel(ConnectionKind k) => k switch
    {
        ConnectionKind.Push => "Push", ConnectionKind.MaterialFlow => "Material", ConnectionKind.Withdrawal => "Withdrawal (Pull)",
        ConnectionKind.ManualInfo => "Manual Information", ConnectionKind.ElectronicInfo => "Electronic Information",
        ConnectionKind.ProductionKanban => "Production Kanban", ConnectionKind.WithdrawalKanban => "Withdrawal Kanban", _ => "Signal Kanban",
    };
}

/// <summary>Example rows for the guide, parsed from the real template.</summary>
public sealed class ImportGuideExamples
{
    public List<List<string>> Map = new(), Elements = new(), Connections = new();
    public List<string> ElementColumns = new(), ConnectionColumns = new();
    public string CsvSnippet = "";

    public static ImportGuideExamples FromTemplate()
    {
        var r = new ImportGuideExamples();
        var section = ""; bool sawE = false, sawC = false; var snippet = new List<string>();
        foreach (var row in CsvTable.Parse(MapImportService.TemplateCsv))
        {
            var first = (row.Count > 0 ? row[0] : "").Trim();
            if (first.StartsWith('#')) continue;
            if (first.StartsWith('[') && first.EndsWith(']')) { section = first.ToLowerInvariant(); snippet.Add(first); continue; }
            if (!row.Any(c => c.Length > 0)) continue;
            var trimmed = row.ToList(); while (trimmed.Count > 0 && trimmed[^1].Length == 0) trimmed.RemoveAt(trimmed.Count - 1);
            switch (section)
            {
                case "[map]": r.Map.Add(trimmed); snippet.Add(string.Join(",", trimmed)); break;
                case "[elements]":
                    if (!sawE) { r.ElementColumns = row; sawE = true; snippet.Add(string.Join(",", row.Take(4)) + ",…"); }
                    else { r.Elements.Add(Pad(row, r.ElementColumns.Count)); if (r.Elements.Count <= 2) snippet.Add(string.Join(",", row.Take(4)) + ",…"); else if (r.Elements.Count == 3) snippet.Add("…"); }
                    break;
                case "[connections]":
                    if (!sawC) { r.ConnectionColumns = row; sawC = true; snippet.Add(string.Join(",", row)); }
                    else { r.Connections.Add(Pad(row, r.ConnectionColumns.Count)); if (r.Connections.Count <= 2) snippet.Add(string.Join(",", trimmed)); else if (r.Connections.Count == 3) snippet.Add("…"); }
                    break;
            }
        }
        r.CsvSnippet = string.Join("\n", snippet);
        return r;
    }

    private static List<string> Pad(List<string> row, int n) => row.Count >= n ? row : row.Concat(Enumerable.Repeat("", n - row.Count)).ToList();
}

public sealed class GlossaryWindow : Window
{
    public GlossaryWindow()
    {
        Title = "VSM Reference"; Width = 640; Height = 720;
        var search = new TextBox { Watermark = "Search terms", Margin = new Thickness(16, 12, 16, 4) };
        var list = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        void Build()
        {
            list.Children.Clear();
            var q = (search.Text ?? "").Trim();
            bool Match(GlossaryEntry e) => q.Length == 0 || e.Term.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Definition.Contains(q, StringComparison.OrdinalIgnoreCase);
            void Section(string title, IEnumerable<GlossaryEntry> entries)
            {
                var items = entries.Where(Match).ToList();
                if (items.Count == 0) return;
                list.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 15, Margin = new Thickness(0, 10, 0, 0) });
                foreach (var e in items)
                    list.Children.Add(new StackPanel { Children = { new TextBlock { Text = e.Term, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = e.Definition, FontSize = 11.5, Foreground = Ui.InkSecondary, TextWrapping = TextWrapping.Wrap } } });
            }
            Section("The Method", Glossary.Method);
            Section("Time & Metrics", Glossary.Metrics);
            Section("Flow Concepts", Glossary.Concepts);
            Section("Icon Reference", ElementKindInfo.All.Select(k => new GlossaryEntry(k.DisplayName(), k.GuidanceNote())));
            Section("Flow Arrows", ConnectionKindInfo.All.Select(k => new GlossaryEntry(k.DisplayName(), k.GuidanceNote())));
        }
        search.TextChanged += (_, _) => Build();
        Build();
        Content = new DockPanel { Children = { search, new ScrollViewer { Content = list } } };
        DockPanel.SetDock(search, Dock.Top);
    }
}
