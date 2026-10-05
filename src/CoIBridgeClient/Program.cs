using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using CoIBridge;

public static class Program
{
    private static string Exchange(string pipe, string request, int timeout)
    {
        using (var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous)) {
            stream.Connect(timeout); BridgeWire.Write(stream, request, timeout); return BridgeWire.Read(stream, timeout);
        }
    }
    public static int Main(string[] argv)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try {
            if (argv.Length == 0 || argv[0] == "help") {
                Console.WriteLine("CoIBridgeClient discover [directory]\nCoIBridgeClient request <discovery.json> <request.json> [timeout_seconds]\nCoIBridgeClient status <discovery.json> <request_id> [timeout_seconds]\nrequest.json: {\"id\":\"unique-id\",\"operation\":\"game.status\",\"args\":{}}\nNo automatic retries. Preserve the id and query status after any uncertain outcome."); return 0;
            }
            if (argv[0] == "discover") {
                string root = argv.Length > 1 ? argv[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Captain of Industry", "StateReporter");
                foreach (string file in Directory.GetFiles(root, "bridge-*.json")) if (Path.GetFileName(file) != "bridge-config.json") Console.WriteLine(file);
                return 0;
            }
            if (argv.Length < 3 || (argv[0] != "request" && argv[0] != "status")) throw new ArgumentException("See help");
            int seconds = argv.Length > 3 ? Int32.Parse(argv[3]) : 60;
            if (seconds < 1 || seconds > 600) throw new ArgumentException("timeout_seconds must be 1..600");
            var discovery = (Dictionary<string, object>)BridgeJson.Parse(File.ReadAllText(argv[1]));
            string pipe = (string)discovery["pipe"], session = (string)discovery["session"];
            Dictionary<string, object> request;
            if (argv[0] == "status") request = BridgeJson.Obj("id", Guid.NewGuid().ToString("N"), "operation", "job.status", "args", BridgeJson.Obj("request_id", argv[2]));
            else request = (Dictionary<string, object>)BridgeJson.Parse(File.ReadAllText(argv[2]));
            request["version"] = 1; request["session"] = session;
            string requestId = (string)request["id"];
            Console.Error.WriteLine("Session " + session + ", request " + requestId);
            var watch = Stopwatch.StartNew();
            string response = Exchange(pipe, BridgeJson.Encode(request), Math.Min(seconds * 1000, 10000));
            while (true) {
                var parsed = (Dictionary<string, object>)BridgeJson.Parse(response);
                string state = (string)parsed["state"];
                if (state != "accepted") { Console.WriteLine(response); return state == "processed" ? 0 : 2; }
                if (watch.ElapsedMilliseconds >= seconds * 1000) { Console.WriteLine(response); Console.Error.WriteLine("Still accepted; query status with this request id. No resubmission performed."); return 3; }
                Thread.Sleep(200);
                string id = (string)parsed["id"];
                var query = BridgeJson.Obj("version", 1, "session", session, "id", Guid.NewGuid().ToString("N"), "operation", "job.status", "args", BridgeJson.Obj("request_id", id));
                response = Exchange(pipe, BridgeJson.Encode(query), Math.Max(1, (int)Math.Min(10000, seconds * 1000 - watch.ElapsedMilliseconds)));
            }
        } catch (Exception error) { Console.Error.WriteLine(error.Message + "\nNo command was retried. If the request was sent, query its status before taking further action."); return 1; }
    }
}
