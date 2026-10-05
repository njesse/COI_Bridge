using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;

namespace CoIBridge
{
    // External requests are dispatched through the game input scheduler.
    public sealed class BridgeMod : IMod
    {
        public const string BridgeVersion = "0.9.0";
        private GameBridge bridge;
        private readonly ModManifest manifest;
        private readonly ModJsonConfig jsonConfig;

        public BridgeMod(ModManifest manifest)
        {
            this.manifest = manifest;
            jsonConfig = new ModJsonConfig(this);
            Log.Info("CoIBridge: constructed (" + BridgeVersion + ")");
        }

        ModManifest IMod.Manifest { get { return manifest; } }
        bool IMod.IsUiOnly { get { return false; } }
        Option<IConfig> IMod.ModConfig { get { return Option<IConfig>.None; } }
        ModJsonConfig IMod.JsonConfig { get { return jsonConfig; } }
        void IMod.RegisterPrototypes(ProtoRegistrator registrator) { }
        void IMod.RegisterDependencies(DependencyResolverBuilder builder, ProtosDb protos, bool gameWasLoaded) { }
        void IMod.EarlyInit(DependencyResolver resolver) { }
        void IMod.MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }
        void IDisposable.Dispose() { if (bridge != null) bridge.Dispose(); bridge = null; }

        void IMod.Initialize(DependencyResolver resolver, bool gameWasLoaded)
        {
            if (bridge != null) bridge.Dispose();
            try { bridge = new GameBridge(resolver); bridge.Start(); }
            catch (Exception error) { if (bridge != null) bridge.Dispose(); bridge = null; Log.Error("CoI bridge failed to start: " + error); }
        }
    }
}
