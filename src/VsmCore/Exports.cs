// Text exports shared by the UI: the map metrics table and the flow summary.

using System.Globalization;
using System.Text;
using VsmCore.Simulation;

namespace VsmCore;

public static class Exports
{
    public static string MapCsv(ValueStreamMap map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Element,Type,C/T (s),VCT (s),C/O (s),Uptime (%),Operators,EPE,Scrap (%),Inventory (pcs),Inventory (days),Frequency,Note");
        foreach (var e in map.TimelineElements)
        {
            var m = e.Metrics;
            sb.AppendLine(string.Join(",", new[]
            {
                Escape(e.Name), Escape(e.Kind.DisplayName()),
                Num(m.CycleTime), Num(m.ValueCreatingTime), Num(m.ChangeoverTime), Num(m.UptimePercent),
                m.Operators?.ToString(CultureInfo.InvariantCulture) ?? "",
                Escape(m.Epex ?? ""), Num(m.ScrapPercent), Num(m.InventoryQty), Num(map.InventoryDays(e)),
                Escape(m.Frequency ?? ""), Escape(e.Note),
            }));
        }
        sb.AppendLine();
        sb.AppendLine("Summary,,,,,,,,,,,,");
        sb.AppendLine($"Takt time (s),{Num(map.TaktTime)},,,,,,,,,,,");
        sb.AppendLine($"Customer demand (pcs/day),{Num(map.CustomerDemandPerDay)},,,,,,,,,,,");
        sb.AppendLine($"Available time (s/day),{Num(map.AvailableTimePerDay)},,,,,,,,,,,");
        sb.AppendLine($"Production lead time (days),{Num(map.ProductionLeadTimeDays)},,,,,,,,,,,");
        sb.AppendLine($"Total processing time (s),{Num(map.TotalProcessingTime)},,,,,,,,,,,");
        sb.AppendLine($"Value-creating time (s),{Num(map.TotalValueCreatingTime)},,,,,,,,,,,");
        if (map.ProcessCycleEfficiency is double pce)
            sb.AppendLine($"Process-cycle efficiency (%),{(pce * 100).ToString("0.000", CultureInfo.InvariantCulture)},,,,,,,,,,,");
        return sb.ToString();
    }

    public static string FlowSummaryCsv(FlowSimulator.Summary s, string mapTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Item,Value,Detail");
        sb.AppendLine($"Throughput,Map,{Escape(mapTitle)},");
        sb.AppendLine($"Throughput,Shipped (pcs),{Num(s.Shipped)},");
        sb.AppendLine($"Throughput,Demanded (pcs),{Num(s.Demand)},");
        if (s.Demand > 0) sb.AppendLine($"Throughput,Demand coverage (%),{(s.Shipped / s.Demand * 100).ToString("0.0", CultureInfo.InvariantCulture)},");
        sb.AppendLine($"Throughput,Simulated time (s),{Num(s.SimTime)},");
        sb.AppendLine($"Throughput,Takt time (s),{Num(s.Takt)},");
        foreach (var a in s.Accumulations)
            sb.AppendLine($"Accumulation,{Escape(a.Element.Name)},{Num(a.Delta)},now {Num(a.Final)} pcs; peak {Num(a.Peak)} pcs");
        foreach (var c in s.Constraints)
            sb.AppendLine($"Constraint,{Escape(c.Element.Name)},{Num(c.Interval)},cycle (s) vs takt {Num(s.Takt)} s; {(int)(c.Utilization * 100)} % busy; +{Num(c.UpstreamGrowth)} pcs upstream");
        foreach (var st in s.StarvedSteps)
            sb.AppendLine($"Starved,{Escape(st.Element.Name)},{(int)(st.Fraction * 100)},% of time idle waiting for material");
        return sb.ToString();
    }

    public static string Num(double? v) => v is double d ? VsmFormat.Trim(d) : "";

    public static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static string SafeName(string title)
    {
        var cleaned = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        return cleaned.Length == 0 ? "ValueStreamMap" : cleaned;
    }
}
