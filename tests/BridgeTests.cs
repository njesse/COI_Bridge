using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using CoIBridge;

public static class BridgeTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static Dictionary<string, object> Parse(string s) { return (Dictionary<string, object>)BridgeJson.Parse(s); }
    private static string Request(BridgeBroker b, string id, string op, object args, string session = null)
    { return BridgeJson.Encode(BridgeJson.Obj("version", 1, "session", session ?? b.Session, "id", id, "operation", op, "args", args)); }
    private sealed class Pending : IBridgePending {
        public bool Complete, Disposed; public string Error;
        public bool Poll(out object result, out string error) { result = BridgeJson.Obj("done", Complete); error = Error; return Complete; }
        public void Dispose() { Disposed = true; }
    }
    public static int Main()
    {
        Check(BridgeJson.Encode(BridgeJson.Parse("{\"b\":2,\"a\":1}")) == "{\"a\":1,\"b\":2}", "Canonical keys");
        string unicode = "Hafen \"Neu\" \\ \n \uD83D\uDEA2";
        Check((string)BridgeJson.Parse(BridgeJson.Encode(unicode)) == unicode, "Unicode JSON roundtrip");
        Check(BridgeJson.Encode(BridgeJson.Parse("{\"a b\":1,\"\":2}")) == "{\"\":2,\"a b\":1}", "Unusual object keys");
        Check(BridgeJson.Encode((ushort)65535) == "65535", "Train id serialization");
        bool invalid = false; try { BridgeJson.Parse("{\"a\":1,\"a\":2}"); } catch { invalid = true; } Check(invalid, "Duplicate JSON keys");
        invalid = false; try { var a = new BridgeArgs(BridgeJson.Parse("{\"id\":1.5}")); a.Int("id"); } catch { invalid = true; } Check(invalid, "Fractional integer accepted");
        invalid = false; try { var a = new BridgeArgs(BridgeJson.Parse("{\"typo\":true}")); a.Done(); } catch { invalid = true; } Check(invalid, "Unknown arguments accepted");
        var operations = new Dictionary<string, bool> { { "read", false }, { "write", true } };
        bool writes = true; var seen = new List<string>(); var first = new Pending();
        int pumpThread = Thread.CurrentThread.ManagedThreadId;
        using (var b = new BridgeBroker(operations, (op, a) => {
            Check(Thread.CurrentThread.ManagedThreadId == pumpThread, "Execution escaped simulation pump");
            seen.Add(op); return seen.Count == 1 ? (IBridgePending)first : new BridgeImmediate(BridgeJson.Obj("ok", true));
        }, () => writes)) {
            string req = Request(b,"a","write",BridgeJson.Obj("x",1));
            string reply = null; var producer = new Thread(() => reply = b.Handle(req)); producer.Start(); producer.Join();
            Check((string)Parse(reply)["state"] == "accepted" && seen.Count == 0, "Communication thread executed game action");
            Check(reply == b.Handle(req), "Duplicate accepted request changed");
            Check((string)Parse(b.Handle(Request(b,"a","write",BridgeJson.Obj("x",2))))["state"] == "failed", "Conflicting ID accepted");
            Check((string)Parse(b.Handle(Request(b,"old","write",BridgeJson.Obj(),"old-session")))["state"] == "session_ended", "Stale session accepted");
            b.Handle(Request(b,"b","write",BridgeJson.Obj())); b.Pump(); b.Pump(); Check(seen.Count == 1, "Command order violated");
            first.Complete = true; first.Error = "game rejected"; b.Pump(); Check(first.Disposed, "Pending action not disposed");
            Check((string)Parse(b.Handle(req))["state"] == "failed", "Scheduler error lost");
            writes = false; b.Pump(); Check(seen.Count == 1, "Queued write bypassed changed config");
            Check((string)Parse(b.Handle(Request(b,"c","write",BridgeJson.Obj())))["state"] == "failed", "Write switch bypassed");
            b.Handle(Request(b,"r","read",BridgeJson.Obj())); b.Pump(); Check(seen.Count == 2, "Read disabled with writes");
            b.Dispose(); Check((string)Parse(b.Handle(req))["state"] == "session_ended", "Disposed session accepted request");
        }
        // Frame format and disconnect failures, using the same implementation as client/server.
        using (var stream = new MemoryStream()) { BridgeWire.Write(stream,"{\"x\":1}",1000); stream.Position=0; Check(BridgeWire.Read(stream,1000)=="{\"x\":1}","Frame roundtrip"); }
        invalid=false; using(var stream=new MemoryStream(new byte[]{255,255,255,127})) { try { BridgeWire.Read(stream,1000); } catch(InvalidDataException) { invalid=true; } } Check(invalid,"Oversize accepted");
        invalid=false; using(var stream=new MemoryStream(new byte[]{4,0,0,0,65})) { try { BridgeWire.Read(stream,1000); } catch(EndOfStreamException) { invalid=true; } } Check(invalid,"Disconnect accepted as full request");
        invalid=false; using(var stream=new MemoryStream(new byte[]{1,0,0,0,255})) { try { BridgeWire.Read(stream,1000); } catch(System.Text.DecoderFallbackException) { invalid=true; } } Check(invalid,"Invalid UTF8 accepted");
        invalid=false; using(var stream=new MemoryStream()) { try { BridgeWire.Write(stream,new String('x',BridgeWire.Limit+1),1000); } catch(InvalidDataException) { invalid=true; } } Check(invalid,"Oversize output accepted");
        var outstanding = new Pending();
        using(var b=new BridgeBroker(operations,(op,a)=>outstanding,()=>true)) {
            string req=Request(b,"pending","write",BridgeJson.Obj()); b.Handle(req); b.Pump(); b.Dispose();
            Check(outstanding.Disposed,"Active pending leaked on unload");
            using(var next=new BridgeBroker(operations,(op,a)=>new BridgeImmediate(null),()=>true)) Check((string)Parse(next.Handle(req))["state"]=="session_ended","Old session crossed load boundary");
        }
        int executions=0;
        using(var b=new BridgeBroker(operations,(op,a)=> { executions++; return new BridgeImmediate(true); },()=>true)) {
            string req=Request(b,"once","write",BridgeJson.Obj()); b.Handle(req); b.Pump(); string completed=b.Handle(req); b.Pump();
            Check((string)Parse(completed)["state"]=="processed" && executions==1,"Completed request executed twice");
            Check((string)Parse(b.Handle("not json"))["state"]=="failed","Malformed JSON escaped handler");
            for(int i=0;i<128;i++) b.Handle(Request(b,"queued-"+i,"read",BridgeJson.Obj()));
            Check((string)Parse(b.Handle(Request(b,"overflow","read",BridgeJson.Obj())))["state"]=="failed","Queue limit ignored");
        }
        using(var b=new BridgeBroker(operations,(op,a)=>new BridgeImmediate(null),()=>true)) {
            string name="CoIBridgeTest-"+Guid.NewGuid().ToString("N");
            using(var server=new BridgePipe(name,b)) {
                server.Start();
                using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                    client.Connect(5000); BridgeWire.Write(client,Request(b,"status","bridge.status",BridgeJson.Obj()),1000);
                    Check((string)Parse(BridgeWire.Read(client,1000))["state"]=="processed","Real named pipe exchange failed");
                }
                // An accepted command survives the client closing before reading its reply.
                string found=null;
                using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                    client.Connect(5000); BridgeWire.Write(client,Request(b,"disconnect","write",BridgeJson.Obj()),1000);
                    // A successful Write alone does not mean the server accepted the frame.
                    for(int i=0;i<500;i++) { found=b.Handle(Request(b,"lookup","job.status",BridgeJson.Obj("request_id","disconnect"))); if((string)Parse(found)["state"]=="accepted") break; Thread.Sleep(10); }
                    Check((string)Parse(found)["state"]=="accepted","Request never accepted before disconnect");
                }
                found=b.Handle(Request(b,"lookup","job.status",BridgeJson.Obj("request_id","disconnect")));
                Check((string)Parse(found)["state"]=="accepted","Disconnected request not retained"); b.Pump();
                using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                    client.Connect(5000); BridgeWire.Write(client,Request(b,"lookup","job.status",BridgeJson.Obj("request_id","disconnect")),1000);
                    Check((string)Parse(BridgeWire.Read(client,1000))["state"]=="processed","Reconnect lost command outcome");
                }
            }
        }
        Console.WriteLine("PASS: JSON, framing, actual named pipe exchange, disconnect, queued execution, ordering, deduplication, stale session, write switch, scheduler failure, disposal."); return 0;
    }
}
