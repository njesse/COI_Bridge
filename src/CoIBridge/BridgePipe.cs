using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace CoIBridge
{
    public static class BridgeWire
    {
        public const int Limit = 1024 * 1024;
        private static void ReadExactly(Stream stream, byte[] bytes, int count, int timeout, Stopwatch watch)
        {
            int offset = 0;
            while (offset < count) {
                var ar = stream.BeginRead(bytes, offset, count - offset, null, null);
                using (ar.AsyncWaitHandle) {
                    int remaining = (int)Math.Max(0, timeout - watch.ElapsedMilliseconds);
                    if (!ar.AsyncWaitHandle.WaitOne(remaining)) { stream.Dispose(); throw new TimeoutException("Pipe read timed out; command outcome may be unknown"); }
                    int n = stream.EndRead(ar); if (n == 0) throw new EndOfStreamException(); offset += n;
                }
            }
        }
        public static string Read(Stream stream, int timeout)
        {
            var watch = Stopwatch.StartNew();
            byte[] header = new byte[4]; ReadExactly(stream, header, 4, timeout, watch);
            int count = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
            if (count <= 0 || count > Limit) throw new InvalidDataException("Invalid frame length");
            byte[] bytes = new byte[count]; ReadExactly(stream, bytes, count, timeout, watch);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        public static void Write(Stream stream, string text, int timeout)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
            if (bytes.Length == 0 || bytes.Length > Limit) throw new InvalidDataException("Frame exceeds 1 MiB");
            byte[] frame = new byte[bytes.Length + 4]; int count = bytes.Length;
            for (int i = 0; i < 4; i++) frame[i] = (byte)(count >> (i * 8));
            Buffer.BlockCopy(bytes, 0, frame, 4, bytes.Length);
            var ar = stream.BeginWrite(frame, 0, frame.Length, null, null);
            using (ar.AsyncWaitHandle) { if (!ar.AsyncWaitHandle.WaitOne(timeout)) { stream.Dispose(); throw new TimeoutException("Pipe write timed out"); } stream.EndWrite(ar); }
        }
    }
    public sealed class BridgePipe : IDisposable
    {
        private readonly BridgeBroker broker;
        private readonly string name;
        private readonly object gate = new object();
        private NamedPipeServerStream pipe;
        private volatile bool stopped;
        private readonly Thread thread;
        public BridgePipe(string name, BridgeBroker broker) { this.name = name; this.broker = broker; thread = new Thread(Run) { IsBackground = true, Name = "CoI bridge pipe" }; }
        public void Start() { lock (gate) { if (stopped) throw new ObjectDisposedException("BridgePipe"); pipe = CreatePipe(); } thread.Start(); }
        private NamedPipeServerStream CreatePipe()
        {
            return NativeLocalPipe.Create(name);
        }
        private void Run()
        {
            while (!stopped) {
                try {
                    NamedPipeServerStream next;
                    lock (gate) { if (stopped) return; next = pipe ?? (pipe = CreatePipe()); }
                    using (var server = next) {
                        var wait = server.BeginWaitForConnection(null, null);
                        using (wait.AsyncWaitHandle) { wait.AsyncWaitHandle.WaitOne(); server.EndWaitForConnection(wait); }
                        string request = BridgeWire.Read(server, 10000);
                        string response = broker.Handle(request);
                        BridgeWire.Write(server, response, 10000);
                    }
                } catch (Exception) { if (!stopped) Thread.Sleep(50); }
                finally { lock (gate) pipe = null; }
            }
        }
        public void Dispose() { stopped = true; lock (gate) { if (pipe != null) pipe.Dispose(); } if (thread.IsAlive) thread.Join(1500); }
    }
    // Unity's Mono does not implement WindowsIdentity.User. Obtain the process SID
    // and create the secured pipe directly; there is no permissive fallback.
    internal static class NativeLocalPipe
    {
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int size, out int needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes security);
        private static string ProcessSid()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 8, out token)) throw new Win32Exception();
            IntPtr buffer = IntPtr.Zero, text = IntPtr.Zero;
            try {
                int needed; GetTokenInformation(token, 1, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new Win32Exception();
                buffer = Marshal.AllocHGlobal(needed);
                if (!GetTokenInformation(token, 1, buffer, needed, out needed) || !ConvertSidToStringSidW(Marshal.ReadIntPtr(buffer), out text)) throw new Win32Exception();
                return Marshal.PtrToStringUni(text);
            } finally { if (text != IntPtr.Zero) LocalFree(text); if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); CloseHandle(token); }
        }
        public static NamedPipeServerStream Create(string name)
        {
            IntPtr descriptor; uint size;
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(D;;GA;;;NU)(A;;GA;;;" + ProcessSid() + ")", 1, out descriptor, out size)) throw new Win32Exception();
            try {
                var security = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor, Inherit = 0 };
                // Duplex + overlapped + first-instance. Byte mode + reject remote clients.
                IntPtr raw = CreateNamedPipeW("\\\\.\\pipe\\" + name, 3 | 0x40000000 | 0x00080000, 8, 1, 65536, 65536, 10000, ref security);
                if (raw == new IntPtr(-1)) throw new Win32Exception();
                var handle = new SafePipeHandle(raw, true);
                try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
                catch { handle.Dispose(); throw; }
            } finally { LocalFree(descriptor); }
        }
    }
}
