using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CoIBridge
{
    public interface IBridgePending : IDisposable { bool Poll(out object result, out string error); }
    public sealed class BridgeImmediate : IBridgePending
    {
        private readonly object value;
        public BridgeImmediate(object value) { this.value = value; }
        public bool Poll(out object result, out string error) { result = value; error = null; return true; }
        public void Dispose() { }
    }
    public sealed class BridgeBroker : IDisposable
    {
        private sealed class Job { public string Id, Fingerprint, Operation, Reply; public Dictionary<string, object> Args; public int Bytes; }
        private readonly object gate = new object();
        private readonly object pumpGate = new object();
        private readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        private readonly Queue<Job> queue = new Queue<Job>();
        private readonly Dictionary<string, bool> operations;
        private readonly Func<string, Dictionary<string, object>, IBridgePending> execute;
        private readonly Func<bool> writeAllowed;
        private Job current;
        private IBridgePending pending;
        private bool ended;
        private int queuedBytes;
        public readonly string Session = Guid.NewGuid().ToString("N");
        public BridgeBroker(Dictionary<string, bool> operations, Func<string, Dictionary<string, object>, IBridgePending> execute, Func<bool> writeAllowed)
        { this.operations = operations; this.execute = execute; this.writeAllowed = writeAllowed; }
        private string Reply(string id, string state, object result, string error)
        { return BridgeJson.Encode(BridgeJson.Obj("version", 1, "session", Session, "id", id, "state", state, "result", result, "error", error)); }
        public string Handle(string text)
        {
            string id = null;
            try {
                var request = new BridgeArgs(BridgeJson.Parse(text));
                request.Int("version", 1, 1); id = request.Text("id"); string session = request.Text("session");
                string operation = request.Text("operation"); var args = new BridgeArgs(request.Take("args")); request.Done();
                lock (gate) {
                    if (ended || session != Session) return Reply(id, "session_ended", null, "Session is no longer active");
                    if (operation == "job.status") {
                        string target = args.Text("request_id"); args.Done(); Job found;
                        return jobs.TryGetValue(target, out found) ? found.Reply : Reply(target, "failed", null, "Unknown request_id; do not automatically resubmit");
                    }
                    if (operation == "bridge.status") { args.Done(); return Reply(id, "processed", BridgeJson.Obj("write_enabled", writeAllowed(), "queued", queue.Count, "active", current == null ? null : current.Id, "retained_jobs", jobs.Count), null); }
                    if (operation == "bridge.capabilities") {
                        args.Done(); var rows = new List<object>();
                        foreach (var op in operations) rows.Add(BridgeJson.Obj("operation", op.Key, "writes_game", op.Value, "changes_view", op.Key == "camera.center", "runtime_verified", false));
                        return Reply(id, "processed", rows, null);
                    }
                    bool writes;
                    if (!operations.TryGetValue(operation, out writes)) return Reply(id, "failed", null, "Unknown operation");
                    string canonical = BridgeJson.Encode(BridgeJson.Obj("operation", operation, "args", args.Values));
                    string fingerprint;
                    using (var hash = SHA256.Create()) fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
                    Job existing;
                    if (jobs.TryGetValue(id, out existing)) return existing.Fingerprint == fingerprint ? existing.Reply : Reply(id, "failed", null, "request_id reused with different contents");
                    if (writes && !writeAllowed()) return Reply(id, "failed", null, "Writing disabled by bridge configuration");
                    int bytes = Encoding.UTF8.GetByteCount(canonical);
                    if (jobs.Count >= 2048 || queue.Count >= 128 || queuedBytes + bytes > 8 * 1024 * 1024) return Reply(id, "failed", null, "Session request capacity reached; no command accepted");
                    var job = new Job { Id = id, Operation = operation, Args = args.Values, Fingerprint = fingerprint, Bytes = bytes };
                    job.Reply = Reply(id, "accepted", null, null); jobs.Add(id, job); queue.Enqueue(job); queuedBytes += bytes;
                    return job.Reply;
                }
            } catch (Exception error) { return Reply(id, "failed", null, error.Message); }
        }
        // Called only from the simulation thread. No game callbacks run under gate.
        public void Pump() { lock (pumpGate) PumpLocked(); }
        private void PumpLocked()
        {
            lock (gate) {
                if (ended) return;
                if (current == null && queue.Count != 0) { current = queue.Dequeue(); queuedBytes -= current.Bytes; }
            }
            if (current == null) return;
            try {
                if (pending == null) {
                    if (operations[current.Operation] && !writeAllowed()) throw new InvalidOperationException("Writing disabled before execution");
                    pending = execute(current.Operation, current.Args);
                    current.Args = null;
                }
                object result; string error;
                if (pending.Poll(out result, out error)) Finish(result, error);
            } catch (Exception error) { Finish(null, error.Message); }
        }
        private void Finish(object result, string error)
        {
            string reply = Reply(current.Id, error == null ? "processed" : "failed", result, error);
            // Bound session ledger memory; commands remain recorded even if their result is large.
            if (Encoding.UTF8.GetByteCount(reply) > 32 * 1024) reply = Reply(current.Id, error == null ? "processed" : "failed", BridgeJson.Obj("result_omitted", true, "reason", "Result exceeds 32 KiB; use a smaller page or snapshot"), error);
            if (pending != null) pending.Dispose(); pending = null;
            lock (gate) { current.Reply = reply; current.Args = null; current = null; }
        }
        public void Dispose() { lock (pumpGate) DisposeLocked(); }
        private void DisposeLocked()
        {
            lock (gate) {
                ended = true;
                foreach (Job job in jobs.Values) if (job.Args != null || job == current) { job.Reply = Reply(job.Id, "session_ended", null, "Session ended; an already scheduled command is not rolled back"); job.Args = null; }
                queue.Clear(); queuedBytes = 0;
            }
            if (pending != null) pending.Dispose(); pending = null;
        }
    }
}
