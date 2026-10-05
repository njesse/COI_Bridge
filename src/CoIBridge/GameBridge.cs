using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.GameLoop;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;

namespace CoIBridge
{
    public sealed class GameBridge : IDisposable
    {
        private readonly ISimLoopEvents loop;
        private readonly IGameLoopEvents gameLoop;
        private readonly BridgeCamera camera;
        private readonly BridgeBroker broker;
        private readonly BridgePipe pipe;
        private readonly string configPath, discoveryPath;
        private readonly string discovery;
        private volatile bool writes = true;
        private bool disposed;
        private DateTime nextConfigRead;
        public GameBridge(DependencyResolver resolver)
        {
            loop = resolver.Resolve<ISimLoopEvents>();
            gameLoop = resolver.Resolve<IGameLoopEvents>();
            string root = Path.Combine(resolver.Resolve<IFileSystemHelper>().GameDataRootDirPath, "StateReporter");
            Directory.CreateDirectory(root);
            configPath = Path.Combine(root, "bridge-config.json");
            if (!File.Exists(configPath)) File.WriteAllText(configPath, "{\"write_enabled\":true}");
            ReadConfig();
            camera = new BridgeCamera(resolver, gameLoop);
            var adapter = new BridgeGameAdapter(resolver, camera);
            broker = new BridgeBroker(BridgeGameAdapter.Operations, adapter.Execute, () => writes);
            int pid = Process.GetCurrentProcess().Id;
            string name = "CoIStateReporter-" + pid + "-" + broker.Session;
            pipe = new BridgePipe(name, broker);
            discoveryPath = Path.Combine(root, "bridge-" + pid + "-" + broker.Session + ".json");
            // No secret token: ACL on the pipe enforces user-local access; session guards stale requests.
            discovery = BridgeJson.Encode(BridgeJson.Obj("version", 1, "pid", pid, "pipe", name, "session", broker.Session,
                "bridge_version", BridgeMod.BridgeVersion, "reporter_version", BridgeMod.BridgeVersion, "game_name", resolver.Resolve<ISaveManager>().GameName));
        }
        public void Start() {
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
            loop.UpdateBeforeCmdProc.AddNonSaveable(this, BeforeCommands);
            camera.Start();
            pipe.Start();
            string temp = discoveryPath + ".tmp";
            try { File.WriteAllText(temp, discovery); File.Move(temp, discoveryPath); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            Log.Info("CoI bridge discovery: " + discoveryPath);
        }
        private void ReadConfig()
        {
            try { var a = new BridgeArgs(BridgeJson.Parse(File.ReadAllText(configPath))); writes = a.Bool("write_enabled"); a.Done(); }
            catch (Exception error) { writes = false; Log.Warning("CoI bridge: invalid configuration; writing disabled: " + error.Message); }
            nextConfigRead = DateTime.UtcNow.AddSeconds(1);
        }
        private void BeforeCommands()
        {
            if (disposed) return;
            if (DateTime.UtcNow >= nextConfigRead) ReadConfig();
            broker.Pump();
        }
        private void OnTerminate() { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            loop.UpdateBeforeCmdProc.RemoveNonSaveable(this, BeforeCommands);
            gameLoop.Terminate.RemoveNonSaveable(this, OnTerminate);
            camera.Dispose(); pipe.Dispose(); broker.Dispose();
            try { File.Delete(discoveryPath); } catch (IOException) { }
        }
    }
    public sealed class BridgeCommand : IBridgePending
    {
        private readonly IInputCommand command;
        public BridgeCommand(IInputCommand command) { this.command = command; }
        public bool Poll(out object result, out string error)
        {
            result = null; error = null;
            if (!command.IsProcessedAndSynced) return false;
            if (command.HasError) error = command.ErrorMessage;
            object raw = command.GetResultObject();
            object value = raw is EntityId ? (object)((EntityId)raw).Value : raw is bool ? raw : raw == null ? null : (object)raw.ToString();
            if (raw is ImmutableArray<Tile2i>) {
                var points = new List<object>(); foreach (var tile in (ImmutableArray<Tile2i>)raw) points.Add(BridgeJson.Obj("x", tile.X, "y", tile.Y)); value = points;
            }
            result = BridgeJson.Obj("command", command.GetType().FullName, "result", value, "processed_at_step", command.ProcessedAtStep.ToString(), "effect_completion", "command_processed_not_world_task_finished");
            return true;
        }
        public void Dispose() { }
    }
    public sealed class BridgeSave : IBridgePending
    {
        private readonly ISaveManager saves;
        private volatile bool done;
        private readonly string expectedName;
        private SaveResult result;
        public BridgeSave(ISaveManager saves, string name)
        {
            this.saves = saves; expectedName = name;
            if (saves.IsNonAutosaveInProgress()) throw new InvalidOperationException("A save is already running");
            saves.OnSaveDone += OnDone;
            try { saves.RequestGameSave(name); } catch { Dispose(); throw; }
        }
        private void OnDone(SaveResult value) {
            // Autosaves can overlap a request: only accept a successful save with our name.
            if (value.FilePath.HasValue && !String.Equals(Path.GetFileNameWithoutExtension(value.FilePath.Value), expectedName, StringComparison.OrdinalIgnoreCase)) return;
            result = value; done = true;
        }
        public bool Poll(out object value, out string error)
        {
            value = null; error = null; if (!done) return false;
            error = result.Error.HasValue ? result.Error.Value.ToString() : result.Exception.HasValue ? result.Exception.Value.Message : null;
            value = BridgeJson.Obj("file_path", result.FilePath.HasValue ? result.FilePath.Value : null); return true;
        }
        public void Dispose() { saves.OnSaveDone -= OnDone; }
    }
}
