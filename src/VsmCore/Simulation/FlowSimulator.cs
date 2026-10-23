// Discrete flow simulation of a value-stream map using its own parameters:
// the supplier releases at takt, each process works at its effective cycle
// time (derated by uptime), the customer pulls at takt. Accumulation,
// starvation and bottlenecks emerge from the data.

namespace VsmCore.Simulation;

public sealed class FlowSimulator
{
    public enum Role { Source, Sink, Process, Relay, Buffer }

    public sealed class Node
    {
        public VsmElement Element { get; }
        public Role Role { get; }
        public double Interval { get; set; }
        public double Queue { get; set; }
        public double InitialQueue { get; }
        public double Remaining { get; set; }
        public bool Busy { get; set; }
        public double BusyTime { get; set; }
        public double StarvedTime { get; set; }
        public double PeakQueue { get; set; }
        public double Completed { get; set; }
        public List<Node> Preds { get; } = new();
        public List<Node> Succs { get; } = new();
        private int nextSucc;

        public Node(VsmElement element, Role role, double interval, double queue)
        {
            Element = element; Role = role; Interval = interval; Queue = queue; InitialQueue = queue; PeakQueue = queue;
        }

        public bool IsActive => Role != Role.Buffer;
        public double QueueDelta => Queue - InitialQueue;

        public Node? NextSuccessor()
        {
            if (Succs.Count == 0) return null;
            var s = Succs[nextSucc % Succs.Count];
            nextSucc++;
            return s;
        }
    }

    public readonly record struct Token(double FromX, double FromY, double ToX, double ToY, double Depart, double Arrive, bool IsInfo);

    private readonly record struct InfoEdge(double StartX, double StartY, double EndX, double EndY, double Phase);

    public ValueStreamMap Map { get; }
    public double Takt { get; }
    public bool TaktIsEstimated { get; }
    public double DayLength { get; }
    /// <summary>Simulated seconds per real second at 1× (a full day in ~3 minutes).</summary>
    public double SimPerReal { get; }

    public List<Node> Nodes { get; } = new();
    private readonly Dictionary<Guid, Node> nodeById = new();
    public List<Node> ActiveDownstreamFirst { get; private set; } = new();
    private readonly List<InfoEdge> infoEdges = new();
    private double lastInfoPulse = -1000;

    public double SimTime { get; private set; }
    public double Shipped { get; private set; }
    public double MissedDemand { get; private set; }
    public List<Token> Tokens { get; } = new();

    public bool IsFinished => SimTime >= DayLength;
    public double DemandForDay => DayLength / Takt;

    public FlowSimulator(ValueStreamMap map)
    {
        Map = map;
        if (map.TaktTime is double t) { Takt = Math.Max(t, 0.5); TaktIsEstimated = false; }
        else
        {
            var cts = map.Elements.Where(e => e.Metrics.CycleTime != null).Select(e => e.Metrics.CycleTime!.Value).ToList();
            Takt = Math.Max(cts.Count > 0 ? cts.Max() : 60, 1);
            TaktIsEstimated = true;
        }
        DayLength = map.AvailableTimePerDay > 0 ? map.AvailableTimePerDay : Takt * 400;
        SimPerReal = DayLength / 180;
        BuildGraph();
    }

    private void BuildGraph()
    {
        var materialKinds = new HashSet<ConnectionKind> { ConnectionKind.Push, ConnectionKind.MaterialFlow, ConnectionKind.Withdrawal };
        var materialConnections = Map.Connections.Where(c => materialKinds.Contains(c.Kind)).ToList();
        var inPath = new HashSet<Guid>();
        foreach (var c in materialConnections) { inPath.Add(c.FromId); inPath.Add(c.ToId); }

        foreach (var e in Map.Elements.Where(e => inPath.Contains(e.Id)))
        {
            var node = MakeNode(e);
            Nodes.Add(node);
            nodeById[e.Id] = node;
        }
        foreach (var c in materialConnections)
        {
            if (!nodeById.TryGetValue(c.FromId, out var u) || !nodeById.TryGetValue(c.ToId, out var v) || u == v) continue;
            if (!u.Succs.Contains(v)) u.Succs.Add(v);
            if (!v.Preds.Contains(u)) v.Preds.Add(u);
        }
        ActiveDownstreamFirst = Nodes.Where(n => n.IsActive).OrderByDescending(n => n.Element.X).ToList();

        int index = 0;
        foreach (var c in Map.Connections.Where(c => c.Kind.IsInformation()))
        {
            var from = Map.Element(c.FromId); var to = Map.Element(c.ToId);
            if (from == null || to == null) continue;
            var g = ConnectionGeometry.Between(from, to);
            infoEdges.Add(new InfoEdge(g.StartX, g.StartY, g.EndX, g.EndY, index * 0.9));
            index++;
        }
    }

    private Node MakeNode(VsmElement e)
    {
        var m = e.Metrics;
        var uptime = Math.Max((m.UptimePercent ?? 100) / 100, 0.05);
        if (e.Kind == ElementKind.Customer) return new Node(e, Role.Sink, Takt, 0);
        if (e.Kind == ElementKind.Supplier) return new Node(e, Role.Source, Takt, 0);
        if (e.Kind.SupportsProcessMetrics())
        {
            if (m.EffectiveCycleTime is double ct) return new Node(e, Role.Process, Math.Max(ct / uptime, 0.1), m.InventoryQty ?? 0);
            return new Node(e, Role.Process, Takt, m.InventoryQty ?? 0);
        }
        if (e.Kind.HoldsInventory()) return new Node(e, Role.Buffer, 0, m.InventoryQty ?? 0);
        return new Node(e, Role.Relay, Math.Max(Takt / 6, 0.5), 0);
    }

    public void Advance(double realSeconds, double speed)
    {
        var dt = Math.Min(Math.Max(realSeconds, 0), 0.1) * SimPerReal * speed;
        if (dt <= 0 || IsFinished) return;
        var target = Math.Min(SimTime + dt, DayLength);
        var step = target - SimTime;
        foreach (var node in ActiveDownstreamFirst) Run(node, step, speed);
        SimTime = target;
        SpawnInfoPulses(speed);
        Tokens.RemoveAll(t => t.Arrive < SimTime - 1);
    }

    private void Run(Node node, double budget, double speed)
    {
        int cycles = 0;
        while (budget > 0 && cycles < 500)
        {
            cycles++;
            if (node.Busy)
            {
                var used = Math.Min(node.Remaining, budget);
                node.Remaining -= used; node.BusyTime += used; budget -= used;
                if (node.Remaining <= 0) { node.Busy = false; Complete(node, speed); }
                continue;
            }
            if (node.Role == Role.Relay && !node.Succs.Any(s => s.Queue < 3)) return;
            if (Pull(node, speed)) { node.Busy = true; node.Remaining = node.Interval; }
            else
            {
                node.StarvedTime += budget;
                if (node.Role == Role.Sink) MissedDemand += budget / Takt;
                return;
            }
        }
    }

    private bool Pull(Node node, double speed)
    {
        if (node.Role == Role.Source) return true;
        if (node.Queue >= 1) { node.Queue -= 1; return true; }
        var upstream = node.Preds
            .Where(p => p.Role == Role.Source || (!p.IsActive && p.Queue >= 1))
            .OrderBy(p => p.Role == Role.Source ? 1 : 0)
            .ThenByDescending(p => p.Queue)
            .FirstOrDefault();
        if (upstream == null) return false;
        if (upstream.Role != Role.Source) upstream.Queue -= 1;
        SpawnToken(upstream, node, speed);
        return true;
    }

    private void Complete(Node node, double speed)
    {
        node.Completed += 1;
        if (node.Role == Role.Sink) { Shipped += 1; return; }
        var succ = node.NextSuccessor();
        if (succ == null) { Shipped += 1; return; }
        succ.Queue += 1;
        succ.PeakQueue = Math.Max(succ.PeakQueue, succ.Queue);
        SpawnToken(node, succ, speed);
    }

    private void SpawnToken(Node from, Node to, double speed)
    {
        if (Tokens.Count >= 400) return;
        var g = ConnectionGeometry.Between(from.Element, to.Element);
        var travel = 0.45 * SimPerReal * speed;
        Tokens.Add(new Token(g.StartX, g.StartY, g.EndX, g.EndY, SimTime, SimTime + travel, false));
    }

    private void SpawnInfoPulses(double speed)
    {
        var interval = Math.Max(Takt * 6, DayLength / 40);
        if (SimTime - lastInfoPulse < interval) return;
        lastInfoPulse = SimTime;
        var travel = 0.9 * SimPerReal * speed;
        foreach (var edge in infoEdges)
        {
            if (Tokens.Count >= 400) break;
            var delay = edge.Phase * 0.12 * SimPerReal * speed;
            Tokens.Add(new Token(edge.StartX, edge.StartY, edge.EndX, edge.EndY, SimTime + delay, SimTime + delay + travel, true));
        }
    }

    // MARK: Diagnostics

    public double UpstreamGrowth(Node node) =>
        node.Preds.Where(p => !p.IsActive).Sum(p => p.QueueDelta) + Math.Max(node.QueueDelta, 0);

    public double Utilization(Node node) => SimTime > 0 ? node.BusyTime / SimTime : 0;
    public double StarvedFraction(Node node) => SimTime > 0 ? node.StarvedTime / SimTime : 0;

    /// <summary>0 = fine, 1 = accumulating, 2 = clear bottleneck.</summary>
    public int BottleneckLevel(Node node)
    {
        if (node.Role != Role.Process || SimTime <= Takt * 3) return 0;
        var growth = UpstreamGrowth(node);
        var overloaded = node.Interval > Takt * 1.02 && Utilization(node) > 0.9;
        if (growth > 20 || (overloaded && growth > 8)) return 2;
        if (growth > 8 || (overloaded && growth > 3)) return 1;
        return 0;
    }

    public bool IsStarving(Node node) =>
        (node.Role == Role.Process || node.Role == Role.Sink) && SimTime > Takt * 3 && StarvedFraction(node) > 0.25;

    // MARK: Summary

    public sealed record Accumulation(VsmElement Element, double Delta, double Final, double Peak);
    public sealed record Constraint(VsmElement Element, double Interval, double Utilization, double UpstreamGrowth);
    public sealed record Starved(VsmElement Element, double Fraction);

    public sealed record Summary(double Shipped, double Demand, double SimTime, double Takt,
        List<Accumulation> Accumulations, List<Constraint> Constraints, List<Starved> StarvedSteps);

    public Summary GetSummary()
    {
        var accumulations = Nodes
            .Where(n => !n.IsActive || n.Role == Role.Process)
            .Where(n => n.QueueDelta > 0.5)
            .OrderByDescending(n => n.QueueDelta).Take(5)
            .Select(n => new Accumulation(n.Element, n.QueueDelta, n.Queue, n.PeakQueue)).ToList();
        var constraints = ActiveDownstreamFirst
            .Where(n => n.Role == Role.Process && BottleneckLevel(n) > 0)
            .OrderByDescending(UpstreamGrowth).Take(3)
            .Select(n => new Constraint(n.Element, n.Interval, Utilization(n), UpstreamGrowth(n))).ToList();
        var starved = ActiveDownstreamFirst
            .Where(IsStarving)
            .OrderByDescending(StarvedFraction).Take(3)
            .Select(n => new Starved(n.Element, StarvedFraction(n))).ToList();
        return new Summary(Shipped, Math.Round(SimTime / Takt), SimTime, Takt, accumulations, constraints, starved);
    }
}

/// <summary>Anchor points on the edges of two element frames.</summary>
public readonly record struct ConnectionGeometry(double StartX, double StartY, double EndX, double EndY)
{
    public double MidX => (StartX + EndX) / 2;
    public double MidY => (StartY + EndY) / 2;
    public double Angle => Math.Atan2(EndY - StartY, EndX - StartX);
    public double Length => Math.Sqrt((EndX - StartX) * (EndX - StartX) + (EndY - StartY) * (EndY - StartY));

    public static ConnectionGeometry Between(VsmElement from, VsmElement to)
    {
        var (sx, sy) = EdgePoint(from, to.X, to.Y);
        var (ex, ey) = EdgePoint(to, from.X, from.Y);
        return new ConnectionGeometry(sx, sy, ex, ey);
    }

    /// <summary>Intersection of the centre→target segment with the element's frame.</summary>
    public static (double X, double Y) EdgePoint(VsmElement e, double targetX, double targetY)
    {
        var (w, h) = e.Size;
        var dx = targetX - e.X; var dy = targetY - e.Y;
        if (dx == 0 && dy == 0) return (e.X, e.Y);
        var sx = dx != 0 ? (w / 2) / Math.Abs(dx) : double.MaxValue;
        var sy = dy != 0 ? (h / 2) / Math.Abs(dy) : double.MaxValue;
        var t = Math.Min(sx, sy);
        return (e.X + dx * t, e.Y + dy * t);
    }

    public static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax; var aby = by - ay;
        var len = abx * abx + aby * aby;
        if (len <= 0) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        var t = Math.Clamp(((px - ax) * abx + (py - ay) * aby) / len, 0, 1);
        var cx = ax + abx * t; var cy = ay + aby * t;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
