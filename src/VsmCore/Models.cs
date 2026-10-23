// Core domain model for value-stream mapping. Field names and enum raw
// values mirror the Apple app's .vsmap JSON so files round-trip between
// platforms unchanged.

using System.Text.Json.Serialization;

namespace VsmCore;

public enum MapState { Current, Future, Ideal }

public static class MapStateExtensions
{
    public static string RawValue(this MapState s) => s switch
    {
        MapState.Current => "Current State",
        MapState.Future => "Future State",
        _ => "Ideal State",
    };

    public static string ShortLabel(this MapState s) => s switch
    {
        MapState.Current => "Current",
        MapState.Future => "Future",
        _ => "Ideal",
    };

    public static MapState FromRaw(string raw) => raw switch
    {
        "Future State" => MapState.Future,
        "Ideal State" => MapState.Ideal,
        _ => MapState.Current,
    };
}

/// <summary>Icon kinds. Member names, camel-cased, are the JSON raw values.</summary>
public enum ElementKind
{
    Customer, Supplier, Process, DataBox, CrossDock, Warehouse,
    TruckShipment, TrainShipment, PlaneShipment, BoatShipment, MilkRun, ExpeditedTransport,
    Inventory, PaperQueue, ElectronicQueue, Delay, Supermarket, FifoLane, SlaHandoff, BufferStock, SafetyStock,
    ScheduleBox, LoadLeveling, KanbanPost, KanbanBatch, GoSeeScheduling, Phone,
    OperatorIcon, KaizenBurst, Milestone, IterationRework,
}

public enum ConnectionKind
{
    Push, MaterialFlow, Withdrawal, ManualInfo, ElectronicInfo,
    ProductionKanban, WithdrawalKanban, SignalKanban,
}

public enum PaletteCategory { Flow, Material, Information, People }

public static class ElementKindInfo
{
    public static IReadOnlyList<ElementKind> All { get; } = Enum.GetValues<ElementKind>();

    public static string DisplayName(this ElementKind k) => k switch
    {
        ElementKind.Customer => "Customer",
        ElementKind.Supplier => "Supplier / Outside Source",
        ElementKind.Process => "Process",
        ElementKind.DataBox => "Data Box",
        ElementKind.CrossDock => "Cross-Dock",
        ElementKind.Warehouse => "Warehouse",
        ElementKind.TruckShipment => "Truck Shipment",
        ElementKind.TrainShipment => "Train Shipment",
        ElementKind.PlaneShipment => "Plane Shipment",
        ElementKind.BoatShipment => "Boat Shipment",
        ElementKind.MilkRun => "Milk Run",
        ElementKind.ExpeditedTransport => "Expedited Transport",
        ElementKind.Inventory => "Inventory",
        ElementKind.PaperQueue => "In-Box / Paper Queue",
        ElementKind.ElectronicQueue => "Electronic In-Box",
        ElementKind.Delay => "Delay Time",
        ElementKind.Supermarket => "Supermarket",
        ElementKind.FifoLane => "FIFO Lane",
        ElementKind.SlaHandoff => "SLA Handoff",
        ElementKind.BufferStock => "Buffer Stock",
        ElementKind.SafetyStock => "Safety Stock",
        ElementKind.ScheduleBox => "Information / Schedule",
        ElementKind.LoadLeveling => "Load Leveling",
        ElementKind.KanbanPost => "Kanban Post",
        ElementKind.KanbanBatch => "Kanban in Batches",
        ElementKind.GoSeeScheduling => "Go-See Scheduling",
        ElementKind.Phone => "Phone (Expedited Info)",
        ElementKind.OperatorIcon => "Operator",
        ElementKind.KaizenBurst => "Kaizen Burst",
        ElementKind.Milestone => "Milestone Pacing",
        ElementKind.IterationRework => "Iteration / Rework",
        _ => k.ToString(),
    };

    public static string GuidanceNote(this ElementKind k) => k switch
    {
        ElementKind.Customer => "Used to show external and internal customers.",
        ElementKind.Supplier => "Used to show suppliers, outside services, and sources.",
        ElementKind.Process => "One process box equals an area of flow. All processes should be labeled. Toggle the IT badge to indicate an information-technology system.",
        ElementKind.DataBox => "Used to record information about a process or department (C/T, C/O, uptime, available time, operators…).",
        ElementKind.CrossDock => "Materials are not stored but moved from in-bound trucks to shipping lanes for out-bound trucks.",
        ElementKind.Warehouse => "Materials are placed in storage locations (binned) and picked for out-bound shipment later.",
        ElementKind.TruckShipment or ElementKind.TrainShipment or ElementKind.PlaneShipment or ElementKind.BoatShipment
            => "Note the frequency of shipments (e.g. 2× day, Mon + Wed).",
        ElementKind.MilkRun => "A vehicle route that makes multiple pickups or drop-offs at different locations.",
        ElementKind.ExpeditedTransport => "Rush transport used to recover from a problem.",
        ElementKind.Inventory => "Count and time should be noted. Time = quantity ÷ daily customer demand.",
        ElementKind.PaperQueue => "Indicates where a physical order backlog exists.",
        ElementKind.ElectronicQueue => "Indicates a backlog that exists electronically.",
        ElementKind.Delay => "Delays not caused by a backlog — commonly caused by unavailable resources.",
        ElementKind.Supermarket => "A controlled inventory used to schedule production at an upstream process. The open side faces the supplying process.",
        ElementKind.FifoLane => "First-in, first-out flow between processes. Note the maximum quantity or time.",
        ElementKind.SlaHandoff => "A handoff governed by a service-level agreement with defined metrics (lead time, delivery time…).",
        ElementKind.BufferStock => "Buffer stock protects against variation in customer demand — must be noted.",
        ElementKind.SafetyStock => "Safety stock protects against internal problems — must be noted.",
        ElementKind.ScheduleBox => "Describes an information flow or a scheduling method (e.g. Weekly Schedule, MRP).",
        ElementKind.LoadLeveling => "Tool to intercept batches of kanban and level their volume and mix over time (heijunka).",
        ElementKind.KanbanPost => "Place where kanban are collected and held for conveyance.",
        ElementKind.KanbanBatch => "Kanban arriving in batches.",
        ElementKind.GoSeeScheduling => "Adjusts schedules based on checking work on the gemba.",
        ElementKind.Phone => "Usually for expedited information.",
        ElementKind.OperatorIcon => "Represents a person viewed from above.",
        ElementKind.KaizenBurst => "Specific process-improvement countermeasures required to achieve the future state. Use to plan kaizen workshops.",
        ElementKind.Milestone => "Milestones corresponding to specific dates; indicates a timeline or frequency of checks.",
        ElementKind.IterationRework => "A process repeats — label as iteration (planned) vs. rework (unplanned).",
        _ => "",
    };

    public static bool SupportsProcessMetrics(this ElementKind k) =>
        k is ElementKind.Process or ElementKind.DataBox or ElementKind.CrossDock or ElementKind.Warehouse;

    public static bool HoldsInventory(this ElementKind k) =>
        k is ElementKind.Inventory or ElementKind.Supermarket or ElementKind.FifoLane or ElementKind.BufferStock
            or ElementKind.SafetyStock or ElementKind.PaperQueue or ElementKind.ElectronicQueue or ElementKind.Warehouse;

    public static (double Width, double Height) DefaultSize(this ElementKind k) => k switch
    {
        ElementKind.Customer or ElementKind.Supplier => (120, 78),
        ElementKind.Process => (120, 120),
        ElementKind.DataBox => (120, 90),
        ElementKind.CrossDock or ElementKind.Warehouse => (110, 84),
        ElementKind.TruckShipment or ElementKind.TrainShipment or ElementKind.PlaneShipment or ElementKind.BoatShipment
            or ElementKind.MilkRun or ElementKind.ExpeditedTransport => (96, 70),
        ElementKind.Inventory or ElementKind.Delay => (84, 74),
        ElementKind.PaperQueue or ElementKind.ElectronicQueue => (84, 70),
        ElementKind.Supermarket => (96, 78),
        ElementKind.FifoLane => (110, 56),
        ElementKind.SlaHandoff => (96, 60),
        ElementKind.BufferStock or ElementKind.SafetyStock => (84, 74),
        ElementKind.ScheduleBox => (110, 56),
        ElementKind.LoadLeveling => (96, 50),
        ElementKind.KanbanPost or ElementKind.KanbanBatch => (76, 64),
        ElementKind.GoSeeScheduling or ElementKind.Phone => (76, 60),
        ElementKind.OperatorIcon => (64, 60),
        ElementKind.KaizenBurst => (104, 90),
        ElementKind.Milestone => (84, 60),
        ElementKind.IterationRework => (80, 64),
        _ => (100, 80),
    };

    public static string DefaultName(this ElementKind k) => k switch
    {
        ElementKind.Customer => "Customer",
        ElementKind.Supplier => "Supplier",
        ElementKind.Process => "Process",
        ElementKind.DataBox => "Data",
        ElementKind.Inventory => "I",
        ElementKind.ScheduleBox => "Weekly Schedule",
        ElementKind.KaizenBurst => "Kaizen",
        ElementKind.FifoLane => "FIFO",
        _ => k.DisplayName(),
    };

    public static IReadOnlyList<ElementKind> Kinds(this PaletteCategory c) => c switch
    {
        PaletteCategory.Flow => new[] { ElementKind.Process, ElementKind.Customer, ElementKind.Supplier, ElementKind.DataBox, ElementKind.CrossDock, ElementKind.Warehouse },
        PaletteCategory.Material => new[] { ElementKind.Inventory, ElementKind.Supermarket, ElementKind.FifoLane, ElementKind.BufferStock, ElementKind.SafetyStock,
            ElementKind.TruckShipment, ElementKind.TrainShipment, ElementKind.PlaneShipment, ElementKind.BoatShipment, ElementKind.MilkRun, ElementKind.ExpeditedTransport,
            ElementKind.PaperQueue, ElementKind.ElectronicQueue, ElementKind.Delay, ElementKind.SlaHandoff },
        PaletteCategory.Information => new[] { ElementKind.ScheduleBox, ElementKind.LoadLeveling, ElementKind.KanbanPost, ElementKind.KanbanBatch, ElementKind.GoSeeScheduling, ElementKind.Phone },
        _ => new[] { ElementKind.OperatorIcon, ElementKind.KaizenBurst, ElementKind.Milestone, ElementKind.IterationRework },
    };

    public static string Title(this PaletteCategory c) => c switch
    {
        PaletteCategory.Flow => "Flow",
        PaletteCategory.Material => "Material",
        PaletteCategory.Information => "Information",
        _ => "People & Kaizen",
    };
}

public static class ConnectionKindInfo
{
    public static IReadOnlyList<ConnectionKind> All { get; } = Enum.GetValues<ConnectionKind>();

    public static string DisplayName(this ConnectionKind k) => k switch
    {
        ConnectionKind.Push => "Push Arrow",
        ConnectionKind.MaterialFlow => "Material / Finished Goods",
        ConnectionKind.Withdrawal => "Withdrawal (Pull)",
        ConnectionKind.ManualInfo => "Manual Information",
        ConnectionKind.ElectronicInfo => "Electronic Information",
        ConnectionKind.ProductionKanban => "Production Kanban",
        ConnectionKind.WithdrawalKanban => "Withdrawal Kanban",
        _ => "Signal Kanban",
    };

    public static string GuidanceNote(this ConnectionKind k) => k switch
    {
        ConnectionKind.Push => "Materials or work pushed forward before the next process needs it.",
        ConnectionKind.MaterialFlow => "Movement of material or finished goods.",
        ConnectionKind.Withdrawal => "A downstream process pulling from an upstream supermarket.",
        ConnectionKind.ManualInfo => "For example, a production schedule or a phone conversation.",
        ConnectionKind.ElectronicInfo => "For example, via electronic data interchange or an on-line form.",
        ConnectionKind.ProductionKanban => "The one-per-container kanban: tells a process how many of what to produce, and gives permission to do so.",
        ConnectionKind.WithdrawalKanban => "Instructs the material handler to get and transfer parts (e.g. from a supermarket to the consuming process).",
        _ => "The one-per-batch kanban: signals when a reorder point is reached and another batch must be produced.",
    };

    public static bool IsInformation(this ConnectionKind k) =>
        k is ConnectionKind.ManualInfo or ConnectionKind.ElectronicInfo or ConnectionKind.ProductionKanban
            or ConnectionKind.WithdrawalKanban or ConnectionKind.SignalKanban;
}

/// <summary>Metrics recorded in a data box. Times in seconds, inventory in pieces, percentages 0–100.</summary>
public sealed class ElementMetrics
{
    public double? CycleTime { get; set; }
    public double? ValueCreatingTime { get; set; }
    public double? ChangeoverTime { get; set; }
    public double? UptimePercent { get; set; }
    public double? AvailableTime { get; set; }
    public int? Operators { get; set; }
    public int? Shifts { get; set; }
    public string? Epex { get; set; }
    public double? ScrapPercent { get; set; }
    public double? BatchSize { get; set; }
    public double? InventoryQty { get; set; }
    public string? Frequency { get; set; }
    public double? MaxQuantity { get; set; }
    [JsonPropertyName("usesITSystem")] public bool UsesITSystem { get; set; }
    public bool IsRework { get; set; }

    /// <summary>Effective machine cycle time: C/T + C/O ÷ batch size.</summary>
    [JsonIgnore]
    public double? EffectiveCycleTime
    {
        get
        {
            if (CycleTime is not double ct) return null;
            if (ChangeoverTime is double co && BatchSize is double batch && batch > 0) return ct + co / batch;
            return ct;
        }
    }

    public ElementMetrics Clone() => (ElementMetrics)MemberwiseClone();
}

public sealed class VsmElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ElementKind Kind { get; set; }
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public ElementMetrics Metrics { get; set; } = new();
    public string Note { get; set; } = "";

    public VsmElement() { }

    public VsmElement(ElementKind kind, double x, double y, string? name = null)
    {
        Kind = kind;
        Name = name ?? kind.DefaultName();
        X = x;
        Y = y;
    }

    [JsonIgnore] public (double Width, double Height) Size => Kind.DefaultSize();

    /// <summary>Bounding box (left, top, width, height) centred on the position.</summary>
    [JsonIgnore]
    public (double Left, double Top, double Width, double Height) Frame
    {
        get { var (w, h) = Size; return (X - w / 2, Y - h / 2, w, h); }
    }

    public VsmElement Clone() => new()
    {
        Id = Id, Kind = Kind, Name = Name, X = X, Y = Y, Metrics = Metrics.Clone(), Note = Note,
    };
}

public sealed class VsmConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ConnectionKind Kind { get; set; }
    [JsonPropertyName("fromID")] public Guid FromId { get; set; }
    [JsonPropertyName("toID")] public Guid ToId { get; set; }
    public string Label { get; set; } = "";

    public VsmConnection() { }

    public VsmConnection(ConnectionKind kind, Guid from, Guid to, string label = "")
    {
        Kind = kind; FromId = from; ToId = to; Label = label;
    }

    public VsmConnection Clone() => new() { Id = Id, Kind = Kind, FromId = FromId, ToId = ToId, Label = Label };
}

public sealed class ValueStreamMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string ProductFamily { get; set; } = "";
    public MapState State { get; set; } = MapState.Current;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Customer demand in pieces per day.</summary>
    public double CustomerDemandPerDay { get; set; }
    /// <summary>Available production time per day, in seconds.</summary>
    public double AvailableTimePerDay { get; set; }
    public List<VsmElement> Elements { get; set; } = new();
    public List<VsmConnection> Connections { get; set; } = new();

    public ValueStreamMap() { }
    public ValueStreamMap(string title) { Title = title; }

    // MARK: Analysis

    /// <summary>Takt time = available production time ÷ customer demand.</summary>
    [JsonIgnore]
    public double? TaktTime =>
        CustomerDemandPerDay > 0 && AvailableTimePerDay > 0 ? AvailableTimePerDay / CustomerDemandPerDay : null;

    /// <summary>Processes and stocks along the material path, left to right.</summary>
    [JsonIgnore]
    public List<VsmElement> TimelineElements =>
        Elements.Where(e => (e.Kind.SupportsProcessMetrics() || e.Kind.HoldsInventory()) && e.Kind != ElementKind.DataBox)
                .OrderBy(e => e.X).ToList();

    public double? InventoryDays(VsmElement e)
    {
        if (!e.Kind.HoldsInventory() || e.Metrics.InventoryQty is not double qty || CustomerDemandPerDay <= 0) return null;
        return qty / CustomerDemandPerDay;
    }

    /// <summary>Total production lead time in days: inventory waits plus processing time.</summary>
    [JsonIgnore]
    public double ProductionLeadTimeDays
    {
        get
        {
            double days = 0;
            foreach (var e in TimelineElements)
            {
                if (InventoryDays(e) is double d) days += d;
                if (e.Kind.SupportsProcessMetrics() && e.Metrics.CycleTime is double ct && AvailableTimePerDay > 0)
                    days += ct / AvailableTimePerDay;
            }
            return days;
        }
    }

    [JsonIgnore]
    public double TotalProcessingTime =>
        TimelineElements.Where(e => e.Kind.SupportsProcessMetrics()).Sum(e => e.Metrics.CycleTime ?? 0);

    [JsonIgnore]
    public double TotalValueCreatingTime =>
        TimelineElements.Where(e => e.Kind.SupportsProcessMetrics())
                        .Sum(e => e.Metrics.ValueCreatingTime ?? e.Metrics.CycleTime ?? 0);

    /// <summary>Process-cycle efficiency: value-creating time ÷ production lead time.</summary>
    [JsonIgnore]
    public double? ProcessCycleEfficiency
    {
        get
        {
            var pltSeconds = ProductionLeadTimeDays * Math.Max(AvailableTimePerDay, 1);
            if (pltSeconds <= 0 || AvailableTimePerDay <= 0) return null;
            return TotalValueCreatingTime / pltSeconds;
        }
    }

    [JsonIgnore] public List<VsmElement> KaizenBursts => Elements.Where(e => e.Kind == ElementKind.KaizenBurst).ToList();

    public VsmElement? Element(Guid id) => Elements.FirstOrDefault(e => e.Id == id);

    public ValueStreamMap Clone() => new()
    {
        Id = Id, Title = Title, ProductFamily = ProductFamily, State = State, CreatedAt = CreatedAt, ModifiedAt = ModifiedAt,
        CustomerDemandPerDay = CustomerDemandPerDay, AvailableTimePerDay = AvailableTimePerDay,
        Elements = Elements.Select(e => e.Clone()).ToList(), Connections = Connections.Select(c => c.Clone()).ToList(),
    };
}

/// <summary>Number formatting shared by the UI and exports.</summary>
public static class VsmFormat
{
    public static string Seconds(double? value)
    {
        if (value is not double v) return "—";
        if (v < 60) return Trim(v) + " s";
        if (v < 3600)
        {
            int m = (int)v / 60, s = (int)v % 60;
            return s == 0 ? $"{m} min" : $"{m} min {s} s";
        }
        return Trim(v / 3600) + " hr";
    }

    public static string Days(double? value) =>
        value is double v ? Trim(v) + (Math.Abs(v - 1) < 0.001 ? " day" : " days") : "—";

    public static string Percent(double? value) => value is double v ? Trim(v) + " %" : "—";

    public static string Pieces(double? value) => value is double v ? Trim(v) + " pcs" : "—";

    public static string Trim(double v)
    {
        if (v == Math.Round(v) && Math.Abs(v) < 1e12) return ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
