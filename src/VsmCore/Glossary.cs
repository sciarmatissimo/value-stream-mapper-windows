// In-app reference to the value-stream-mapping method.

namespace VsmCore;

public sealed record GlossaryEntry(string Term, string Definition);

public static class Glossary
{
    public static readonly GlossaryEntry[] Method =
    {
        new("Value-Stream Mapping (VSM)", "A simple diagram of every step involved in the material and information flows needed to bring a product from order to delivery. Maps can be drawn for different points in time to raise consciousness of opportunities for improvement: a current-state map follows a product's path from order to delivery to determine current conditions; a future-state map deploys the opportunities identified in the current-state map to reach a higher level of performance; an ideal-state map shows the opportunities from employing all known lean methods."),
        new("Value Stream", "All of the actions, both value-creating and nonvalue-creating, required to bring a product from concept to launch and from order to delivery — including actions to process information from the customer and actions to transform the product on its way to the customer."),
        new("Material Flow", "The movement of physical items through the entire value stream. In lean production, process steps are moved into tight sequence so small amounts of product flow directly from step to step at the pull of the downstream process and the end customer."),
        new("Information Flow", "The movement of information on customer desires backward from the customer to the points where the information is needed to direct each operation. Lean producers compress schedules, shipping releases, and expediting into simple pull loops with a single scheduling point."),
        new("Value-Stream Improvement (PDCA)", "An improvement method based on plan-do-check-act. 1) Leadership defines the need and scope. 2) In a workshop, stakeholders develop a current-state map, analyze problems, and propose countermeasures as a future-state map. 3) In a 60–120 day improvement phase, the team runs rapid experiments, implements changes, and checks results. Changes are 'countermeasures', not 'solutions' — encouraging continuous improvement."),
    };

    public static readonly GlossaryEntry[] Metrics =
    {
        new("Takt Time", "Available production time divided by customer demand. If a factory operates 480 minutes per day and customers demand 240 widgets per day, takt time is two minutes. Takt time precisely matches production with demand — the heartbeat of a lean system."),
        new("Cycle Time (C/T)", "How often a part or product actually is completed by a process, as timed by observation; also the time an operator needs to go through all work elements before repeating them."),
        new("Value-Creating Time (VCT)", "Time of those work elements that actually transform the product in a way the customer is willing to pay for. Usually VCT < C/T < PLT."),
        new("Production Lead Time (PLT)", "The time one piece needs to move all the way through a process or value stream from start to finish — envision timing a marked part from beginning to end (door-to-door time)."),
        new("Effective Machine Cycle Time", "Machine cycle time plus load/unload time, plus changeover time divided by the number of pieces between changeovers. E.g. CT 20 s, load/unload 30 s, changeover 30 s ÷ batch of 30 → 51 s."),
        new("Changeover (C/O)", "The time required to switch a machine or process from producing one product or part to another."),
        new("Every Product Every Interval (EPEx)", "The frequency with which a process produces every part it makes — a measure of batch size. Smaller EPEx means more flexibility and less inventory."),
        new("Work-in-Process (WIP)", "Items of work between processing steps. In lean systems, standardized WIP is the minimum needed to keep a cell or process flowing smoothly."),
    };

    public static readonly GlossaryEntry[] Concepts =
    {
        new("Push vs. Pull", "Push: materials or work moved forward before the next process needs them, typically on a central schedule. Pull: the downstream process withdraws what it needs from an upstream supermarket, and the upstream process replenishes only what was taken."),
        new("Supermarket", "A controlled inventory used to schedule production at an upstream process. On the map, the open side faces the supplying process."),
        new("Kanban", "A card or device that gives permission and instructions to produce or withdraw items. Production kanban tell a process how many of what to produce; withdrawal kanban instruct the material handler to transfer parts; signal kanban mark a batch reorder point."),
        new("FIFO Lane", "A first-in, first-out flow of work between processes with a maximum quantity or time noted, used where a supermarket is impractical."),
        new("Heijunka / Load Leveling", "Leveling the type and quantity of production over a period of time, intercepting batches of kanban and spreading their volume and mix — enabling small batches and low inventories."),
        new("Kaizen Burst", "The starburst icon marking a specific process-improvement countermeasure required to achieve the future state; used to plan kaizen workshops."),
        new("Waste (Muda)", "Any activity that consumes resources but creates no value for the customer. Type one muda is currently unavoidable; type two can be eliminated immediately. Value-creating activities are usually a tiny fraction of total activities — eliminating waste is the greatest source of improvement."),
        new("Pacemaker Process", "Any process along a value stream that sets the pace for the entire stream — often the final assembly cell. Downstream of the pacemaker, work flows FIFO to the customer."),
    };
}
