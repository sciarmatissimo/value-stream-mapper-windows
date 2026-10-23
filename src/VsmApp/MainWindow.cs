// Main window: map library on the left, the editor canvas with its icon
// palette in the centre, inspector and analysis on the right.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VsmCore;
using VsmCore.Import;

namespace VsmApp;

public sealed class MainWindow : Window
{
    private readonly MapStore store = new();
    private ValueStreamMap? map;

    private readonly MapCanvas canvas = new();
    private readonly ListBox mapList = new();
    private readonly TextBlock titleBlock = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0), MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock statusBlock = new() { Foreground = Ui.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly ScrollViewer inspector = new() { Padding = new Thickness(12) };
    private readonly AnalysisPanel analysis = new();
    private readonly TabControl rightTabs = new();
    private readonly WrapPanel paletteItems = new() { Orientation = Orientation.Horizontal, ItemWidth = 78, ItemHeight = 66 };
    private readonly StackPanel categoryBar = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6, 8, 2) };
    private readonly Button undoBtn = new() { Content = "↶ Undo" }, redoBtn = new() { Content = "↷ Redo" }, connectBtn = new() { Content = "⤷ Connect" };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly Stack<(List<VsmElement>, List<VsmConnection>)> undo = new(), redo = new();
    private PaletteCategory category = PaletteCategory.Flow;
    private bool suppressSelection;

    public string[] PendingOpenPaths { get; set; } = Array.Empty<string>();

    // Hooks used by the headless UI tests.
    public MapCanvas Canvas => canvas;
    public ValueStreamMap? CurrentMap => map;
    public int MapCount => store.Maps.Count;
    public void LoadExample() => Select(store.CreateSampleMap(), refresh: true);
    public void SelectElementNamed(string name) { var e = map?.Elements.FirstOrDefault(x => x.Name == name); if (e != null) Select(e); }
    public void ShowAnalysisTab() => rightTabs.SelectedIndex = 1;
    public FlowAnimationWindow OpenAnimation() { var w = new FlowAnimationWindow(map!); w.Show(this); return w; }
    public ImportGuideWindow OpenGuide() { var w = new ImportGuideWindow(() => Task.CompletedTask, () => Task.CompletedTask); w.Show(this); return w; }
    public void AddElementForTest(ElementKind kind) => AddElement(kind);
    public void UndoForTest() => Undo();

    public MainWindow()
    {
        Title = "Value Stream Mapper";
        Width = 1380; Height = 860; MinWidth = 980; MinHeight = 600;
        Content = BuildLayout();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); if (map != null) store.Update(map); RefreshList(keepSelection: true); };
        Opened += async (_, _) =>
        {
            RefreshList();
            if (store.Maps.Count > 0) Select(store.Maps[0]);
            foreach (var path in PendingOpenPaths.Where(File.Exists)) await ImportPath(path);
        };
        KeyDown += OnKey;
        canvas.ElementClicked += OnElementClicked;
        canvas.ElementDoubleClicked += e => { Select(e); rightTabs.SelectedIndex = 0; };
        canvas.ConnectionClicked += c => { if (map == null) return; canvas.SelectedElementId = null; canvas.SelectedConnectionId = c.Id; BuildInspector(); };
        canvas.BackgroundClicked += () => { canvas.SelectedElementId = null; canvas.SelectedConnectionId = null; BuildInspector(); };
        canvas.ElementDragStarted += _ => Snapshot();
        canvas.ElementDragEnded += _ => { MarkChanged(); analysis.InvalidateVisual(); };
    }

    // MARK: Layout

    private Control BuildLayout()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("250,4,*,4,330") };

        // Sidebar
        var sidebar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(246, 246, 249)) };
        var header = new TextBlock { Text = "Value Streams", FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(14, 14, 14, 8) };
        DockPanel.SetDock(header, Dock.Top);
        var actions = new WrapPanel { Margin = new Thickness(10, 4, 10, 10), ItemWidth = 112, ItemHeight = 34 };
        actions.Children.Add(Btn("＋ New Map", async () => await NewMap()));
        actions.Children.Add(Btn("✦ Example", () => Select(store.CreateSampleMap(), refresh: true)));
        actions.Children.Add(Btn("⤓ Import…", async () => await Import()));
        actions.Children.Add(Btn("⎘ Template", async () => await SaveTemplate()));
        actions.Children.Add(Btn("? Import Guide", () => new ImportGuideWindow(async () => await Import(), async () => await SaveTemplate()).Show(this)));
        actions.Children.Add(Btn("📖 Reference", () => new GlossaryWindow().Show(this)));
        DockPanel.SetDock(actions, Dock.Top);
        var mapActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 0, 10, 10) };
        mapActions.Children.Add(Btn("Duplicate", () => { if (map != null) Select(store.Duplicate(map), refresh: true); }));
        mapActions.Children.Add(Btn("Future State", () => { if (map != null) Select(store.Duplicate(map, MapState.Future), refresh: true); }));
        mapActions.Children.Add(Btn("Delete", async () => await DeleteMap()));
        DockPanel.SetDock(mapActions, Dock.Bottom);
        mapList.ItemTemplate = new FuncDataTemplate<ValueStreamMap>((m, _) => MapRow(m), true);
        mapList.SelectionChanged += (_, _) => { if (!suppressSelection && mapList.SelectedItem is ValueStreamMap m && m != map) Select(m); };
        sidebar.Children.Add(header); sidebar.Children.Add(actions); sidebar.Children.Add(mapActions); sidebar.Children.Add(mapList);
        root.Children.Add(sidebar);

        // Centre: toolbar + canvas + palette
        var centre = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(8, 6, 8, 2) };
        toolbar.Children.Add(titleBlock);
        connectBtn.Click += (_, _) => StartConnect();
        toolbar.Children.Add(connectBtn);
        toolbar.Children.Add(Btn("▶ Animate", () => { if (map != null) new FlowAnimationWindow(map).Show(this); }));
        toolbar.Children.Add(Btn("📊 Analysis", () => rightTabs.SelectedIndex = 1));
        toolbar.Children.Add(Btn("⚙ Settings", () => { canvas.SelectedElementId = null; canvas.SelectedConnectionId = null; rightTabs.SelectedIndex = 0; BuildInspector(); canvas.InvalidateVisual(); }));
        undoBtn.Click += (_, _) => Undo(); redoBtn.Click += (_, _) => Redo();
        toolbar.Children.Add(undoBtn); toolbar.Children.Add(redoBtn);
        var export = new Button { Content = "⇪ Export" };
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("PDF Drawing…", async () => await ExportMap("pdf")));
        flyout.Items.Add(Item("PNG Image…", async () => await ExportMap("png")));
        flyout.Items.Add(Item("CSV Metrics…", async () => await ExportMap("csv")));
        flyout.Items.Add(Item("VSM Document (.vsmap)…", async () => await ExportMap("vsmap")));
        export.Flyout = flyout;
        toolbar.Children.Add(export);
        toolbar.Children.Add(Btn("⊡ Fit", () => canvas.ZoomToFit()));
        toolbar.Children.Add(Btn("＋", () => canvas.ZoomBy(1.25)));
        toolbar.Children.Add(Btn("－", () => canvas.ZoomBy(0.8)));
        toolbar.Children.Add(statusBlock);
        DockPanel.SetDock(toolbar, Dock.Top);
        centre.Children.Add(toolbar);

        var palette = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)) };
        foreach (var cat in Enum.GetValues<PaletteCategory>())
        {
            var tb = new ToggleButton { Content = cat.Title(), IsChecked = cat == category, Tag = cat };
            tb.Click += (s, _) => { category = (PaletteCategory)((ToggleButton)s!).Tag!; foreach (var o in categoryBar.Children.OfType<ToggleButton>()) o.IsChecked = Equals(o.Tag, category); BuildPalette(); };
            categoryBar.Children.Add(tb);
        }
        DockPanel.SetDock(categoryBar, Dock.Top);
        palette.Children.Add(categoryBar);
        palette.Children.Add(new ScrollViewer { Content = paletteItems, Height = 78, Margin = new Thickness(8, 0, 8, 6), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        DockPanel.SetDock(palette, Dock.Bottom);
        centre.Children.Add(palette);
        centre.Children.Add(canvas);
        Grid.SetColumn(centre, 2);
        root.Children.Add(centre);

        // Right: inspector / analysis
        rightTabs.Items.Add(new TabItem { Header = "Inspector", Content = inspector });
        rightTabs.Items.Add(new TabItem { Header = "Analysis", Content = new ScrollViewer { Content = analysis, Padding = new Thickness(8) } });
        Grid.SetColumn(rightTabs, 4);
        root.Children.Add(rightTabs);

        var s1 = new GridSplitter { Background = new SolidColorBrush(Color.FromRgb(225, 225, 230)) }; Grid.SetColumn(s1, 1); root.Children.Add(s1);
        var s2 = new GridSplitter { Background = new SolidColorBrush(Color.FromRgb(225, 225, 230)) }; Grid.SetColumn(s2, 3); root.Children.Add(s2);

        BuildPalette();
        BuildInspector();
        return root;
    }

    private static Button Btn(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 2, 2) };
        b.Click += (_, _) => action();
        return b;
    }

    private static Button Btn(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 2, 2) };
        b.Click += async (_, _) => await action();
        return b;
    }

    private static MenuItem Item(string text, Func<Task> action)
    {
        var m = new MenuItem { Header = text };
        m.Click += async (_, _) => await action();
        return m;
    }

    private static Control MapRow(ValueStreamMap m)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(m.State switch { MapState.Current => Color.FromArgb(40, 0, 122, 255), MapState.Future => Color.FromArgb(40, 52, 199, 89), _ => Color.FromArgb(40, 175, 82, 222) }),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = m.State.ShortLabel(), FontSize = 10, FontWeight = FontWeight.SemiBold },
        };
        var top = new DockPanel();
        DockPanel.SetDock(chip, Dock.Right);
        top.Children.Add(chip);
        top.Children.Add(new TextBlock { Text = m.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var sub = new TextBlock
        {
            Text = $"{m.Elements.Count} elements · {m.Connections.Count} flows" + (m.TaktTime is double t ? $" · takt {VsmFormat.Seconds(t)}" : "") + $" · {m.ModifiedAt.ToLocalTime():d}",
            FontSize = 11, Foreground = Ui.InkSecondary,
        };
        return new StackPanel { Spacing = 2, Margin = new Thickness(0, 4), Children = { top, sub } };
    }

    // MARK: Map selection & persistence

    private void RefreshList(bool keepSelection = false)
    {
        suppressSelection = true;
        var selected = map;
        mapList.ItemsSource = null;
        mapList.ItemsSource = store.Maps.ToList();
        if (keepSelection || selected != null) mapList.SelectedItem = store.Maps.FirstOrDefault(m => m.Id == selected?.Id);
        suppressSelection = false;
    }

    private void Select(ValueStreamMap m, bool refresh = false)
    {
        map = m;
        canvas.Map = m; canvas.SelectedElementId = null; canvas.SelectedConnectionId = null; canvas.IsConnecting = false; canvas.ConnectSourceId = null;
        undo.Clear(); redo.Clear(); UpdateUndoButtons();
        titleBlock.Text = m.Title;
        analysis.Map = m; analysis.InvalidateVisual();
        statusBlock.Text = "";
        BuildInspector();
        if (refresh) RefreshList();
        suppressSelection = true; mapList.SelectedItem = store.Maps.FirstOrDefault(x => x.Id == m.Id); suppressSelection = false;
        Dispatcher.UIThread.Post(() => canvas.ZoomToFit(), DispatcherPriority.Background);
    }

    private void Select(VsmElement e)
    {
        canvas.SelectedConnectionId = null; canvas.SelectedElementId = e.Id;
        BuildInspector(); canvas.InvalidateVisual();
    }

    private void MarkChanged()
    {
        if (map == null) return;
        titleBlock.Text = map.Title;
        canvas.InvalidateVisual();
        analysis.InvalidateVisual();
        saveTimer.Stop(); saveTimer.Start();
    }

    private void Snapshot()
    {
        if (map == null) return;
        undo.Push((map.Elements.Select(e => e.Clone()).ToList(), map.Connections.Select(c => c.Clone()).ToList()));
        if (undo.Count > 60) { var keep = undo.Take(60).ToList(); undo.Clear(); for (int i = keep.Count - 1; i >= 0; i--) undo.Push(keep[i]); }
        redo.Clear();
        UpdateUndoButtons();
    }

    private void Undo()
    {
        if (map == null || undo.Count == 0) return;
        redo.Push((map.Elements.Select(e => e.Clone()).ToList(), map.Connections.Select(c => c.Clone()).ToList()));
        (map.Elements, map.Connections) = undo.Pop();
        canvas.SelectedElementId = null; canvas.SelectedConnectionId = null;
        UpdateUndoButtons(); BuildInspector(); MarkChanged();
    }

    private void Redo()
    {
        if (map == null || redo.Count == 0) return;
        undo.Push((map.Elements.Select(e => e.Clone()).ToList(), map.Connections.Select(c => c.Clone()).ToList()));
        (map.Elements, map.Connections) = redo.Pop();
        canvas.SelectedElementId = null; canvas.SelectedConnectionId = null;
        UpdateUndoButtons(); BuildInspector(); MarkChanged();
    }

    private void UpdateUndoButtons() { undoBtn.IsEnabled = undo.Count > 0; redoBtn.IsEnabled = redo.Count > 0; }

    private async Task NewMap()
    {
        var title = await Dialogs.Prompt(this, "New Value-Stream Map", "Name the product family or process you are mapping.", "");
        if (title == null) return;
        Select(store.CreateMap(string.IsNullOrWhiteSpace(title) ? "Untitled Map" : title.Trim()), refresh: true);
    }

    private async Task DeleteMap()
    {
        if (map == null) return;
        if (!await Dialogs.Confirm(this, "Delete map", $"Delete “{map.Title}”? This cannot be undone.")) return;
        store.Delete(map);
        map = null; canvas.Map = null; analysis.Map = null; titleBlock.Text = "";
        RefreshList();
        if (store.Maps.Count > 0) Select(store.Maps[0]); else { canvas.InvalidateVisual(); analysis.InvalidateVisual(); BuildInspector(); }
    }

    private async Task Import()
    {
        var path = await Dialogs.OpenFile(this, "Import map",
            ("Value stream files", new[] { "*.vsmap", "*.json", "*.csv", "*.txt", "*.xlsx", "*.vsdx" }), ("All files", new[] { "*" }));
        if (path != null) await ImportPath(path);
    }

    private async Task ImportPath(string path)
    {
        try { Select(store.ImportFile(path), refresh: true); }
        catch (Exception ex) { await Dialogs.Message(this, "Import failed", $"Could not import “{Path.GetFileName(path)}”. {ex.Message}"); }
    }

    private async Task SaveTemplate()
    {
        var path = await Dialogs.SaveFile(this, "Save import template", MapImportService.TemplateFilename, "csv", "CSV file");
        if (path != null) await File.WriteAllTextAsync(path, MapImportService.TemplateCsv);
    }

    private async Task ExportMap(string format)
    {
        if (map == null) return;
        var name = Exports.SafeName(map.Title);
        var path = await Dialogs.SaveFile(this, "Export", name, format, format.ToUpperInvariant() + " file");
        if (path == null) return;
        try
        {
            switch (format)
            {
                case "pdf": ExportService.MapPdf(map, path); break;
                case "png": await File.WriteAllBytesAsync(path, ExportService.MapPng(map, 3.0)); break;
                case "csv": await File.WriteAllTextAsync(path, Exports.MapCsv(map)); break;
                default: await File.WriteAllBytesAsync(path, MapStore.ExportData(map)); break;
            }
        }
        catch (Exception ex) { await Dialogs.Message(this, "Export failed", ex.Message); }
    }

    // MARK: Editing

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (e.Key == Key.Escape) { CancelConnect(); e.Handled = true; }
        else if (e.Key == Key.Delete || e.Key == Key.Back) { DeleteSelected(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { Redo(); e.Handled = true; }
    }

    private void DeleteSelected()
    {
        if (map == null) return;
        if (canvas.SelectedElementId is Guid eid)
        {
            Snapshot();
            map.Elements.RemoveAll(x => x.Id == eid);
            map.Connections.RemoveAll(c => c.FromId == eid || c.ToId == eid);
            canvas.SelectedElementId = null;
        }
        else if (canvas.SelectedConnectionId is Guid cid)
        {
            Snapshot();
            map.Connections.RemoveAll(c => c.Id == cid);
            canvas.SelectedConnectionId = null;
        }
        BuildInspector(); MarkChanged();
    }

    private void AddElement(ElementKind kind)
    {
        if (map == null) return;
        Snapshot();
        var centre = canvas.ToMap(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
        var e = new VsmElement(kind, Math.Round(centre.X), Math.Round(centre.Y));
        map.Elements.Add(e);
        canvas.SelectedConnectionId = null; canvas.SelectedElementId = e.Id;
        rightTabs.SelectedIndex = 0;
        BuildInspector(); MarkChanged();
    }

    private void StartConnect()
    {
        if (map == null) return;
        canvas.IsConnecting = true; canvas.ConnectSourceId = canvas.SelectedElementId;
        statusBlock.Text = canvas.ConnectSourceId == null ? "Connect: click the source element (Esc to cancel)" : "Connect: now click the destination element (Esc to cancel)";
        canvas.InvalidateVisual();
    }

    private void CancelConnect()
    {
        canvas.IsConnecting = false; canvas.ConnectSourceId = null; statusBlock.Text = ""; canvas.InvalidateVisual();
    }

    private async void OnElementClicked(VsmElement e)
    {
        if (map == null) return;
        if (!canvas.IsConnecting) { Select(e); return; }
        if (canvas.ConnectSourceId == null)
        {
            canvas.ConnectSourceId = e.Id; statusBlock.Text = "Connect: now click the destination element (Esc to cancel)"; canvas.InvalidateVisual(); return;
        }
        if (canvas.ConnectSourceId == e.Id) return;
        var from = canvas.ConnectSourceId.Value;
        var kind = await PickConnectionKind();
        if (kind is ConnectionKind k)
        {
            Snapshot();
            var c = new VsmConnection(k, from, e.Id);
            map.Connections.Add(c);
            canvas.SelectedElementId = null; canvas.SelectedConnectionId = c.Id;
        }
        CancelConnect(); BuildInspector(); MarkChanged();
    }

    private async Task<ConnectionKind?> PickConnectionKind()
    {
        ConnectionKind? result = null;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Choose the type of flow to draw between the two elements.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        var dlg = new Window { Title = "Flow type", Width = 360, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        foreach (var kind in ConnectionKindInfo.All)
        {
            var b = new Button { Content = kind.DisplayName(), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = kind };
            b.Click += (s, _) => { result = (ConnectionKind)((Button)s!).Tag!; dlg.Close(); };
            panel.Children.Add(b);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.Click += (_, _) => dlg.Close();
        panel.Children.Add(cancel);
        await dlg.ShowDialog(this);
        return result;
    }

    // MARK: Palette

    private void BuildPalette()
    {
        paletteItems.Children.Clear();
        foreach (var kind in category.Kinds())
        {
            var b = new Button
            {
                Padding = new Thickness(2), Margin = new Thickness(2), Width = 72, Height = 62,
                Content = new StackPanel { Children = { new IconPreview { Kind = kind, Width = 56, Height = 38 }, new TextBlock { Text = kind.DisplayName(), FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 68 } } },
                Tag = kind,
            };
            ToolTip.SetTip(b, kind.DisplayName() + "\n" + kind.GuidanceNote());
            b.Click += (s, _) => AddElement((ElementKind)((Button)s!).Tag!);
            paletteItems.Children.Add(b);
        }
    }

    // MARK: Inspector

    private void BuildInspector()
    {
        var panel = new StackPanel { Spacing = 8 };
        inspector.Content = panel;
        if (map == null) { panel.Children.Add(new TextBlock { Text = "Create or select a map to begin.", Foreground = Ui.InkSecondary }); return; }

        if (canvas.SelectedElementId is Guid eid && map.Element(eid) is VsmElement e) BuildElementInspector(panel, e);
        else if (canvas.SelectedConnectionId is Guid cid && map.Connections.FirstOrDefault(c => c.Id == cid) is VsmConnection c) BuildConnectionInspector(panel, c);
        else BuildMapSettings(panel);
    }

    private void BuildMapSettings(StackPanel panel)
    {
        var m = map!;
        panel.Children.Add(Heading("Map Settings"));
        panel.Children.Add(TextRow("Title", m.Title, v => { m.Title = v; MarkChanged(); }));
        panel.Children.Add(TextRow("Product family", m.ProductFamily, v => { m.ProductFamily = v; MarkChanged(); }));
        var state = new ComboBox { ItemsSource = Enum.GetValues<MapState>().Select(s => s.RawValue()).ToList(), SelectedIndex = (int)m.State, HorizontalAlignment = HorizontalAlignment.Stretch };
        state.SelectionChanged += (_, _) => { m.State = (MapState)state.SelectedIndex; MarkChanged(); };
        panel.Children.Add(Labeled("State", state));
        panel.Children.Add(Heading("Demand & Takt"));
        var takt = new TextBlock { Foreground = Ui.InkSecondary, FontSize = 12 };
        void UpdateTakt() => takt.Text = m.TaktTime is double t ? $"Takt time: {VsmFormat.Seconds(t)}" : "Takt time: set demand and available time";
        panel.Children.Add(NumberRow("Customer demand (pcs/day)", m.CustomerDemandPerDay, v => { m.CustomerDemandPerDay = v ?? 0; UpdateTakt(); MarkChanged(); }));
        panel.Children.Add(NumberRow("Available time (min/day)", Math.Round(m.AvailableTimePerDay / 60), v => { m.AvailableTimePerDay = (v ?? 0) * 60; UpdateTakt(); MarkChanged(); }));
        UpdateTakt();
        panel.Children.Add(takt);
        panel.Children.Add(new TextBlock { Text = "Takt time = available production time ÷ customer demand — the heartbeat of a lean system. Select an element on the canvas to edit its data box.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.InkSecondary });
    }

    private void BuildElementInspector(StackPanel panel, VsmElement e)
    {
        panel.Children.Add(Heading(e.Kind.DisplayName()));
        panel.Children.Add(new TextBlock { Text = e.Kind.GuidanceNote(), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.InkSecondary });
        panel.Children.Add(TextRow("Name", e.Name, v => { e.Name = v; MarkChanged(); }));
        var m = e.Metrics;
        if (e.Kind.SupportsProcessMetrics())
        {
            panel.Children.Add(Heading("Data Box"));
            panel.Children.Add(NumberRow("Cycle time (C/T), s", m.CycleTime, v => { m.CycleTime = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Value-creating time, s", m.ValueCreatingTime, v => { m.ValueCreatingTime = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Changeover (C/O), s", m.ChangeoverTime, v => { m.ChangeoverTime = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Uptime, %", m.UptimePercent, v => { m.UptimePercent = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Scrap rate, %", m.ScrapPercent, v => { m.ScrapPercent = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Batch size, pcs", m.BatchSize, v => { m.BatchSize = v; MarkChanged(); }));
            panel.Children.Add(NumberRow("Operators", m.Operators, v => { m.Operators = v is double d ? (int)d : null; MarkChanged(); }));
            panel.Children.Add(NumberRow("Shifts / day", m.Shifts, v => { m.Shifts = v is double d ? (int)d : null; MarkChanged(); }));
            panel.Children.Add(TextRow("EPE (batch interval)", m.Epex ?? "", v => { m.Epex = v.Length == 0 ? null : v; MarkChanged(); }));
            var it = new CheckBox { Content = "Uses IT system", IsChecked = m.UsesITSystem };
            it.IsCheckedChanged += (_, _) => { m.UsesITSystem = it.IsChecked == true; MarkChanged(); };
            panel.Children.Add(it);
            panel.Children.Add(new TextBlock { Text = "Usually VCT ≤ C/T ≤ production lead time. Effective machine cycle time = C/T + C/O ÷ batch size.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.InkSecondary });
        }
        if (e.Kind.HoldsInventory())
        {
            panel.Children.Add(Heading("Inventory"));
            var days = new TextBlock { FontSize = 12, Foreground = Ui.InkSecondary };
            void UpdateDays() => days.Text = map!.InventoryDays(e) is double d ? $"Days of demand: {VsmFormat.Days(d)}" : "Days of demand: set customer demand in Map Settings";
            panel.Children.Add(NumberRow("Quantity, pcs", m.InventoryQty, v => { m.InventoryQty = v; UpdateDays(); MarkChanged(); }));
            UpdateDays(); panel.Children.Add(days);
        }
        if (e.Kind is ElementKind.TruckShipment or ElementKind.TrainShipment or ElementKind.PlaneShipment or ElementKind.BoatShipment or ElementKind.MilkRun
            or ElementKind.ExpeditedTransport or ElementKind.Customer or ElementKind.Supplier or ElementKind.ScheduleBox or ElementKind.Delay
            or ElementKind.PaperQueue or ElementKind.ElectronicQueue or ElementKind.SlaHandoff or ElementKind.Milestone)
            panel.Children.Add(TextRow("Frequency (e.g. 1× day)", m.Frequency ?? "", v => { m.Frequency = v.Length == 0 ? null : v; MarkChanged(); }));
        if (e.Kind == ElementKind.FifoLane)
            panel.Children.Add(NumberRow("Max quantity, pcs", m.MaxQuantity, v => { m.MaxQuantity = v; MarkChanged(); }));
        if (e.Kind == ElementKind.IterationRework)
        {
            var rw = new CheckBox { Content = "Unplanned (rework)", IsChecked = m.IsRework };
            rw.IsCheckedChanged += (_, _) => { m.IsRework = rw.IsChecked == true; MarkChanged(); };
            panel.Children.Add(rw);
        }
        panel.Children.Add(Heading("Notes"));
        var note = new TextBox { Text = e.Note, AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        note.TextChanged += (_, _) => { e.Note = note.Text ?? ""; MarkChanged(); };
        panel.Children.Add(note);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Btn("Connect from here", () => { canvas.SelectedElementId = e.Id; StartConnect(); }));
        row.Children.Add(Btn("Duplicate", () => { Snapshot(); var copy = e.Clone(); copy.Id = Guid.NewGuid(); copy.X += 40; copy.Y += 40; map!.Elements.Add(copy); canvas.SelectedElementId = copy.Id; BuildInspector(); MarkChanged(); }));
        row.Children.Add(Btn("Delete", DeleteSelected));
        panel.Children.Add(row);
    }

    private void BuildConnectionInspector(StackPanel panel, VsmConnection c)
    {
        panel.Children.Add(Heading("Flow"));
        var kind = new ComboBox { ItemsSource = ConnectionKindInfo.All.Select(k => k.DisplayName()).ToList(), SelectedIndex = (int)c.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        var note = new TextBlock { Text = c.Kind.GuidanceNote(), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.InkSecondary };
        kind.SelectionChanged += (_, _) => { c.Kind = (ConnectionKind)kind.SelectedIndex; note.Text = c.Kind.GuidanceNote(); MarkChanged(); };
        panel.Children.Add(Labeled("Flow type", kind));
        panel.Children.Add(note);
        panel.Children.Add(TextRow("Label (optional)", c.Label, v => { c.Label = v; MarkChanged(); }));
        panel.Children.Add(new TextBlock { Text = $"From: {map!.Element(c.FromId)?.Name ?? "?"}\nTo: {map.Element(c.ToId)?.Name ?? "?"}", FontSize = 12 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Btn("Reverse direction", () => { Snapshot(); (c.FromId, c.ToId) = (c.ToId, c.FromId); BuildInspector(); MarkChanged(); }));
        row.Children.Add(Btn("Delete", DeleteSelected));
        panel.Children.Add(row);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(0, 8, 0, 2) };

    private static Control Labeled(string label, Control control) =>
        new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = Ui.InkSecondary }, control } };

    private static Control TextRow(string label, string value, Action<string> commit)
    {
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => commit(box.Text ?? "");
        return Labeled(label, box);
    }

    private static Control NumberRow(string label, double? value, Action<double?> commit)
    {
        var box = new TextBox { Text = value is double v ? VsmFormat.Trim(v) : "", Watermark = "—" };
        box.TextChanged += (_, _) =>
        {
            var t = (box.Text ?? "").Trim();
            if (t.Length == 0) { commit(null); return; }
            if (MapImportService.ParseNumber(t) is double d) commit(d);
        };
        return Labeled(label, box);
    }
}

/// <summary>Small preview of an element kind for palette buttons.</summary>
public sealed class IconPreview : Control
{
    private static readonly ValueStreamMap Stub = new("preview");
    public ElementKind Kind { get; set; }

    public override void Render(DrawingContext ctx)
    {
        var e = new VsmElement(Kind, 0, 0);
        var (w, h) = e.Size;
        var s = Math.Min(Bounds.Width / w, Bounds.Height / h);
        using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2)))
            IconPainter.Draw(ctx, e, Stub, false);
    }
}
