// File-backed store for value-stream maps: one .vsmap (JSON) per map in
// Documents\ValueStreamMapper\Maps, plus the bundled example and import routing.

using System.Text;
using VsmCore.Import;

namespace VsmCore;

public sealed class MapStore
{
    public const string FileExtension = "vsmap";

    public string Directory { get; }
    public List<ValueStreamMap> Maps { get; } = new();

    public event Action? Changed;

    public MapStore(string? directory = null)
    {
        // VSM_MAPS_DIR lets tests and portable installs redirect the library folder.
        Directory = directory ?? Environment.GetEnvironmentVariable("VSM_MAPS_DIR") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ValueStreamMapper", "Maps");
        System.IO.Directory.CreateDirectory(Directory);
        Load();
    }

    public void Load()
    {
        Maps.Clear();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*." + FileExtension))
        {
            try { Maps.Add(MapJson.Deserialize(File.ReadAllBytes(file))); }
            catch { /* skip unreadable documents */ }
        }
        Maps.Sort((a, b) => b.ModifiedAt.CompareTo(a.ModifiedAt));
    }

    private string FilePath(Guid id) => Path.Combine(Directory, id.ToString("D").ToUpperInvariant() + "." + FileExtension);

    public ValueStreamMap? Get(Guid id) => Maps.FirstOrDefault(m => m.Id == id);

    public ValueStreamMap CreateMap(string title, MapState state = MapState.Current)
    {
        var map = new ValueStreamMap(title) { State = state, AvailableTimePerDay = 27_600 };
        Maps.Insert(0, map);
        Save(map);
        return map;
    }

    public void Update(ValueStreamMap map)
    {
        var idx = Maps.FindIndex(m => m.Id == map.Id);
        map.ModifiedAt = DateTime.UtcNow;
        if (idx >= 0) Maps[idx] = map; else Maps.Insert(0, map);
        Save(map);
    }

    public ValueStreamMap Duplicate(ValueStreamMap map, MapState? asState = null)
    {
        var copy = map.Clone();
        copy.Id = Guid.NewGuid();
        copy.CreatedAt = DateTime.UtcNow;
        copy.ModifiedAt = DateTime.UtcNow;
        if (asState is MapState s)
        {
            copy.State = s;
            copy.Title = map.Title.Replace(map.State.RawValue(), "").Trim() + " — " + s.RawValue();
        }
        else copy.Title = map.Title + " Copy";
        Maps.Insert(0, copy);
        Save(copy);
        return copy;
    }

    public void Delete(ValueStreamMap map)
    {
        Maps.RemoveAll(m => m.Id == map.Id);
        try { File.Delete(FilePath(map.Id)); } catch { }
        Changed?.Invoke();
    }

    public ValueStreamMap ImportMap(byte[] data)
    {
        var map = MapJson.Deserialize(data);
        if (Maps.Any(m => m.Id == map.Id))
        {
            map.Id = Guid.NewGuid();
            map.Title += " (Imported)";
        }
        Maps.Insert(0, map);
        Save(map);
        return map;
    }

    public ValueStreamMap InsertImported(ValueStreamMap map)
    {
        if (Maps.Any(m => m.Id == map.Id)) map.Id = Guid.NewGuid();
        map.CreatedAt = DateTime.UtcNow;
        map.ModifiedAt = DateTime.UtcNow;
        Maps.Insert(0, map);
        Save(map);
        return map;
    }

    /// <summary>Imports any supported file: .vsmap / JSON, CSV, Excel, Visio.</summary>
    public ValueStreamMap ImportFile(string path)
    {
        var data = File.ReadAllBytes(path);
        return LooksLikeJson(data)
            ? ImportMap(data)
            : InsertImported(MapImportService.ImportMap(data, Path.GetFileName(path)));
    }

    public static bool LooksLikeJson(byte[] data)
    {
        foreach (var b in data.Take(64))
        {
            switch (b)
            {
                case 0x20 or 0x09 or 0x0A or 0x0D or 0xEF or 0xBB or 0xBF: continue;
                case 0x7B: return true;
                default: return false;
            }
        }
        return false;
    }

    private void Save(ValueStreamMap map)
    {
        var tmp = FilePath(map.Id) + ".tmp";
        File.WriteAllText(tmp, MapJson.Serialize(map), new UTF8Encoding(false));
        File.Move(tmp, FilePath(map.Id), overwrite: true);
        Changed?.Invoke();
    }

    public static byte[] ExportData(ValueStreamMap map) => Encoding.UTF8.GetBytes(MapJson.Serialize(map));

    // MARK: Example map

    public ValueStreamMap CreateSampleMap()
    {
        var map = new ValueStreamMap("Stamping Value Stream (Example)")
        {
            ProductFamily = "Steering brackets",
            CustomerDemandPerDay = 460,
            AvailableTimePerDay = 27_600,
        };

        var supplier = new VsmElement(ElementKind.Supplier, 140, 140, "Steel Co.");
        supplier.Metrics.Frequency = "2× week";
        var schedule = new VsmElement(ElementKind.ScheduleBox, 700, 130, "Weekly Schedule");
        var control = new VsmElement(ElementKind.Process, 700, 300, "Production Control");
        control.Metrics.UsesITSystem = true;
        var customer = new VsmElement(ElementKind.Customer, 1280, 140, "Assembly Plant");
        customer.Metrics.Frequency = "1× day";

        var inv1 = new VsmElement(ElementKind.Inventory, 280, 560, "Coils");
        inv1.Metrics.InventoryQty = 2300;
        var stamping = new VsmElement(ElementKind.Process, 440, 560, "Stamping");
        stamping.Metrics.CycleTime = 1; stamping.Metrics.ValueCreatingTime = 1; stamping.Metrics.ChangeoverTime = 3600;
        stamping.Metrics.UptimePercent = 85; stamping.Metrics.Operators = 1; stamping.Metrics.Epex = "2 weeks";
        var inv2 = new VsmElement(ElementKind.Inventory, 600, 560, "Stamped");
        inv2.Metrics.InventoryQty = 1600;
        var welding = new VsmElement(ElementKind.Process, 760, 560, "Welding");
        welding.Metrics.CycleTime = 39; welding.Metrics.ValueCreatingTime = 35; welding.Metrics.ChangeoverTime = 600;
        welding.Metrics.UptimePercent = 100; welding.Metrics.Operators = 1;
        var inv3 = new VsmElement(ElementKind.Inventory, 920, 560, "Welded");
        inv3.Metrics.InventoryQty = 1100;
        var assembly = new VsmElement(ElementKind.Process, 1080, 560, "Assembly");
        assembly.Metrics.CycleTime = 46; assembly.Metrics.ValueCreatingTime = 40;
        assembly.Metrics.UptimePercent = 100; assembly.Metrics.Operators = 2;
        var shipping = new VsmElement(ElementKind.Inventory, 1240, 560, "Finished");
        shipping.Metrics.InventoryQty = 2700;
        var truck = new VsmElement(ElementKind.TruckShipment, 1360, 420, "Daily");
        truck.Metrics.Frequency = "1× day";
        var burst = new VsmElement(ElementKind.KaizenBurst, 440, 400, "C/O < 10 min")
        {
            Note = "Reduce stamping changeover with SMED to enable smaller batches.",
        };

        map.Elements.AddRange(new[] { supplier, schedule, control, customer, inv1, stamping, inv2, welding, inv3, assembly, shipping, truck, burst });
        map.Connections.AddRange(new[]
        {
            new VsmConnection(ConnectionKind.ElectronicInfo, customer.Id, control.Id, "Orders"),
            new VsmConnection(ConnectionKind.ElectronicInfo, control.Id, supplier.Id, "Weekly fax"),
            new VsmConnection(ConnectionKind.ManualInfo, control.Id, schedule.Id),
            new VsmConnection(ConnectionKind.ManualInfo, schedule.Id, stamping.Id),
            new VsmConnection(ConnectionKind.ManualInfo, schedule.Id, welding.Id),
            new VsmConnection(ConnectionKind.ManualInfo, schedule.Id, assembly.Id),
            new VsmConnection(ConnectionKind.MaterialFlow, supplier.Id, inv1.Id),
            new VsmConnection(ConnectionKind.Push, inv1.Id, stamping.Id),
            new VsmConnection(ConnectionKind.Push, stamping.Id, inv2.Id),
            new VsmConnection(ConnectionKind.Push, inv2.Id, welding.Id),
            new VsmConnection(ConnectionKind.Push, welding.Id, inv3.Id),
            new VsmConnection(ConnectionKind.Push, inv3.Id, assembly.Id),
            new VsmConnection(ConnectionKind.Push, assembly.Id, shipping.Id),
            new VsmConnection(ConnectionKind.MaterialFlow, shipping.Id, truck.Id),
            new VsmConnection(ConnectionKind.MaterialFlow, truck.Id, customer.Id),
        });

        Maps.Insert(0, map);
        Save(map);
        return map;
    }
}
