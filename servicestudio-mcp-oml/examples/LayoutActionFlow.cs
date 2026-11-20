// Lay out every node of the server action SumBigOrders on a grid: branch-aware placement (If False, Switch conditions and For Each bodies in their own columns), merge points below their deepest branch, each For Each loop as a block whose exit sits below its body, exception-handler paths in their own columns; then a completion check that rejects shared cells, upward edges, loop blocks holding foreign nodes and edges drawn through nodes
// Context: typed Model API only (the MCP sandbox rejects reflection). Run it in the same lambda as the edit, or as a follow-up call, before omlMerge. Verified live 2026-10-17 (If merge + 3-way Switch + For Each + exception handler: 0 overlaps, 0 upward edges, 0 crossings; the previous BFS gave 6 overlaps / 6 crossings on the same flow). Loop blocks and the extended check added 2026-11-09 after a new action with three sequential For Each loops passed the old check and was unreadable; verified offline on that flow's reconstructed graph (old check passes the agent's layout, new check rejects it, this pass's layout passes it), not yet live.

using OutSystems.Model;
using OutSystems.Model.Enumerations;
using OutSystems.Model.Expressions;
using OutSystems.Model.Factory;
using OutSystems.Model.Types;

eSpace => {
    var act = eSpace.ServerActions.Named("SumBigOrders");

    // Outgoing edges with their placement offset: (target, column offset, row offset).
    // If: True straight down, False one column right. Switch: condition i at +i columns, Otherwise straight down.
    // For Each: body one column right on the SAME row, exit straight down (and below the body, see rowAfter).
    List<(OutSystems.Model.Logic.Nodes.IActionNode t, int dc, int dr)> Succ(OutSystems.Model.Logic.Nodes.IActionNode n) {
        var r = new List<(OutSystems.Model.Logic.Nodes.IActionNode, int, int)>();
        if (n is OutSystems.Model.Logic.Nodes.IIfNode i) {
            if (i.TrueTarget != null) r.Add((i.TrueTarget, 0, 1));
            if (i.FalseTarget != null) r.Add((i.FalseTarget, 1, 1));
        } else if (n is OutSystems.Model.Logic.Nodes.ISwitchNode s) {
            int k = 1;
            foreach (var c in s.Conditions) if (c.Target != null) r.Add((c.Target, k++, 1));
            if (s.OtherwiseTarget != null) r.Add((s.OtherwiseTarget, 0, 1));
        } else if (n is OutSystems.Model.Logic.Nodes.IForEachNode f) {
            if (f.CycleTarget != null) r.Add((f.CycleTarget, 1, 0));
            if (f.Target != null) r.Add((f.Target, 0, 1));
        } else {
            foreach (var c in n.Connectors) if (c.Target != null) r.Add((c.Target, 0, 1));
        }
        return r;
    }

    var col = new Dictionary<OutSystems.Model.Logic.Nodes.IActionNode, int>();
    var row = new Dictionary<OutSystems.Model.Logic.Nodes.IActionNode, int>();
    var used = new HashSet<(int, int)>();
    var back = new HashSet<(OutSystems.Model.Logic.Nodes.IActionNode, OutSystems.Model.Logic.Nodes.IActionNode)>();

    // Nodes reachable from 'from' over forward (non-back) edges, without passing through 'stop'.
    HashSet<OutSystems.Model.Logic.Nodes.IActionNode> Reach(OutSystems.Model.Logic.Nodes.IActionNode from, OutSystems.Model.Logic.Nodes.IActionNode stop) {
        var seen = new HashSet<OutSystems.Model.Logic.Nodes.IActionNode>();
        var todo = new Stack<OutSystems.Model.Logic.Nodes.IActionNode>();
        if (from != null) todo.Push(from);
        while (todo.Count > 0) {
            var n = todo.Pop();
            if (n == stop || !seen.Add(n)) continue;
            foreach (var e in Succ(n)) if (!back.Contains((n, e.t))) todo.Push(e.t);
        }
        return seen;
    }
    // A loop's body: what the cycle reaches before coming back, minus what the exit reaches.
    HashSet<OutSystems.Model.Logic.Nodes.IActionNode> Body(OutSystems.Model.Logic.Nodes.IForEachNode f) {
        var b = Reach(f.CycleTarget, f);
        b.ExceptWith(Reach(f.Target, f));
        return b;
    }

    // Place everything reachable from root that isn't placed yet.
    void Place(OutSystems.Model.Logic.Nodes.IActionNode root, int rootCol) {
        // 1) Back edges (For Each loop-backs) via DFS, so loops never inflate depth.
        var state = new Dictionary<OutSystems.Model.Logic.Nodes.IActionNode, int>();
        void Dfs(OutSystems.Model.Logic.Nodes.IActionNode n) {
            state[n] = 1;
            foreach (var e in Succ(n)) {
                if (col.ContainsKey(e.t)) continue;
                if (!state.ContainsKey(e.t)) Dfs(e.t);
                else if (state[e.t] == 1) back.Add((n, e.t));
            }
            state[n] = 2;
        }
        Dfs(root);
        // 2) Topological placement over forward edges: row = max over predecessors (merges sit below the
        //    deepest branch), column = min over predecessors (merges return to the main column).
        var preds = state.Keys.ToDictionary(n => n, n => new List<(OutSystems.Model.Logic.Nodes.IActionNode p, int dc, int dr)>());
        foreach (var n in state.Keys)
            foreach (var e in Succ(n))
                if (state.ContainsKey(e.t) && !back.Contains((n, e.t))) preds[e.t].Add((n, e.dc, e.dr));
        //    Loop blocks: dropping the loop-back edge also drops the only link between a body and the
        //    loop's exit, so without this the exit (and every later loop) lands one row under the For Each,
        //    beside the body. rowAfter makes the exit wait for the whole body and sit below its deepest node.
        var rowAfter = state.Keys.ToDictionary(n => n, n => new List<OutSystems.Model.Logic.Nodes.IActionNode>());
        foreach (var f in state.Keys.OfType<OutSystems.Model.Logic.Nodes.IForEachNode>())
            if (f.Target != null && state.ContainsKey(f.Target))
                rowAfter[f.Target].AddRange(Body(f).Where(b => state.ContainsKey(b)));
        var pending = new List<OutSystems.Model.Logic.Nodes.IActionNode>(state.Keys);
        while (pending.Count > 0) {
            var n = pending.FirstOrDefault(x => preds[x].All(p => col.ContainsKey(p.p)) && rowAfter[x].All(b => col.ContainsKey(b)));
            if (n == null) break;   // not a DAG after removing back edges - leave the rest where it is
            pending.Remove(n);
            int c = preds[n].Count == 0 ? rootCol : preds[n].Min(p => col[p.p] + p.dc);
            int r = preds[n].Count == 0 ? 0 : preds[n].Max(p => row[p.p] + p.dr);
            if (rowAfter[n].Count > 0) r = Math.Max(r, rowAfter[n].Max(b => row[b]) + 1);
            while (used.Contains((c, r))) c++;   // 3) collision shift: never two nodes on one cell
            col[n] = c; row[n] = r; used.Add((c, r));
        }
    }

    Place(act.Nodes.OfType<OutSystems.Model.Logic.Nodes.IStartNode>().Single(), 0);

    // Exception-handler paths are not reachable from Start: give each its own columns to the right.
    foreach (var handler in act.Nodes.OfType<OutSystems.Model.Logic.Nodes.IExceptionHandlerNode>())
        if (!col.ContainsKey(handler)) Place(handler, (col.Count == 0 ? 0 : col.Values.Max()) + 2);

    // Final check (the completion gate). A cell check alone passed a flow whose three loops were interleaved.
    string At(OutSystems.Model.Logic.Nodes.IActionNode n) => n.GetType().Name + "(" + col[n] + "," + row[n] + ")";
    var fails = new List<string>();
    // a) no two nodes on one cell
    if (used.Count != col.Count) fails.Add("shared cell");
    // b) no forward edge pointing upward
    foreach (var n in col.Keys)
        foreach (var e in Succ(n))
            if (col.ContainsKey(e.t) && !back.Contains((n, e.t)) && row[e.t] < row[n]) fails.Add("upward " + At(n) + "->" + At(e.t));
    // c) a loop block (For Each + body) owns its bounding box: no other node inside it
    var bodies = col.Keys.OfType<OutSystems.Model.Logic.Nodes.IForEachNode>().ToDictionary(f => (OutSystems.Model.Logic.Nodes.IActionNode)f, f => Body(f));
    foreach (var kv in bodies) {
        var block = new HashSet<OutSystems.Model.Logic.Nodes.IActionNode>(kv.Value.Where(b => col.ContainsKey(b))) { kv.Key };
        int c0 = block.Min(b => col[b]), c1 = block.Max(b => col[b]), r0 = block.Min(b => row[b]), r1 = block.Max(b => row[b]);
        int foreign = col.Keys.Count(m => !block.Contains(m) && col[m] >= c0 && col[m] <= c1 && row[m] >= r0 && row[m] <= r1);
        if (foreign > 0) fails.Add("loop " + At(kv.Key) + " box holds " + foreign + " foreign node(s)");
    }
    // d) no connector drawn through another node: connectors are straight lines between node positions,
    //    so a node closer than 0.1 cell to that line sits under it. A loop-back edge may run alongside
    //    the nodes of its own body.
    double Dist(OutSystems.Model.Logic.Nodes.IActionNode m, OutSystems.Model.Logic.Nodes.IActionNode a, OutSystems.Model.Logic.Nodes.IActionNode b) {
        double dx = col[b] - col[a], dy = row[b] - row[a], l2 = dx * dx + dy * dy;
        double t = l2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((col[m] - col[a]) * dx + (row[m] - row[a]) * dy) / l2));
        double px = col[a] + t * dx - col[m], py = row[a] + t * dy - row[m];
        return Math.Sqrt(px * px + py * py);
    }
    foreach (var n in col.Keys)
        foreach (var e in Succ(n)) {
            if (!col.ContainsKey(e.t)) continue;
            var own = back.Contains((n, e.t)) && bodies.ContainsKey(e.t) ? bodies[e.t] : null;
            foreach (var m in col.Keys)
                if (m != n && m != e.t && (own == null || !own.Contains(m)) && Dist(m, n, e.t) < 0.1)
                    fails.Add("edge " + At(n) + "->" + At(e.t) + " through " + At(m));
        }
    if (fails.Count > 0) throw new Exception("LAYOUT-CHECK failed: " + string.Join("; ", fails));

    // Grid: 1828 x 1371 units, origin (3200, 800). Nodes never reached are left where they are.
    foreach (var n in col.Keys) {
        n.HorizontalPosition = 3200 + col[n] * 1828;
        n.VerticalPosition = 800 + row[n] * 1371;
    }
}
