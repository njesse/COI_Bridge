using System;
using Mafi;
using Mafi.Core;
using Mafi.Core.GameLoop;
using Mafi.Core.Terrain;
using Mafi.Core.UiState;
using Mafi.Unity.Camera;

namespace CoIBridge
{
    // Only immutable arguments cross from simulation to rendering. Camera objects
    // are resolved, used, and released inside RenderUpdate, never in Poll.
    public sealed class BridgeCamera : IDisposable
    {
        private readonly DependencyResolver resolver;
        private readonly IGameLoopEvents loop;
        private readonly object gate = new object();
        private Request pending;
        private bool disposed, started;

        public BridgeCamera(DependencyResolver resolver, IGameLoopEvents loop)
        { this.resolver = resolver; this.loop = loop; }

        public void Start()
        {
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("BridgeCamera");
                if (started) return;
                loop.RenderUpdate.AddNonSaveable(this, RenderUpdate); started = true;
            }
        }

        public IBridgePending Center(BridgeArgs args)
        {
            var terrain = resolver.Resolve<TerrainManager>();
            var position = new BridgeArgs(args.Take("position"));
            int x = position.Int("x", 0, terrain.TerrainWidth - 1);
            int y = position.Int("y", 0, terrain.TerrainHeight - 1);
            position.Done();
            Fix32? distance = Number(args, "distance_tiles", null, true);
            Fix32? yaw = Number(args, "yaw_degrees", 360, false);
            Fix32? pitch = Number(args, "pitch_degrees", 90, false);
            args.Done();
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("BridgeCamera");
                if (pending != null) throw new InvalidOperationException("Camera request already pending");
                pending = new Request(this, x, y, distance, yaw, pitch);
                return pending;
            }
        }

        private static Fix32? Number(BridgeArgs args, string key, int? maximum, bool positive)
        {
            object value = args.Take(key, false);
            if (!args.Values.ContainsKey(key)) return null;
            if (!(value is decimal)) throw new ArgumentException("Expected finite number: " + key);
            decimal n = (decimal)value;
            Fix32 number;
            if (n < 0 || (positive && n == 0) || (maximum.HasValue && n > maximum.Value)
                || !Fix32.TryCreateFromDouble((double)n, out number) || (positive && !number.IsPositive))
                throw new ArgumentException("Out of range or not representable by the native camera: " + key);
            return number;
        }

        private void RenderUpdate(GameTime time)
        {
            lock (gate) {
                if (disposed || pending == null) return;
                Request request = pending;
                try {
                    var controller = resolver.Resolve<CameraController>();
                    var model = controller.CameraModel as OrbitalCameraModel;
                    if (model == null || controller.IsInFreeLookMode || controller.CameraMode != CameraMode.DefaultGameplay)
                        throw new InvalidOperationException("camera.center requires the normal gameplay orbital camera; Dolly, film and free-look modes are unsupported. Return to the gameplay camera first.");
                    if (!controller.IsEnabled) throw new InvalidOperationException("Gameplay camera is currently disabled");
                    if (request.Applied) {
                        request.Result = BridgeJson.Obj("target_position", BridgeJson.Obj("x", request.X, "y", request.Y),
                            "previous_view", request.Previous, "effective_view", View(model.State.CameraPose),
                            "effect_completion", "view_updated");
                        request.Done = true; pending = null;
                        return;
                    }
                    request.Previous = View(model.State.CameraPose);
                    if (request.Distance.HasValue) model.SetOrbitRadius(new RelTile1f(request.Distance.Value));
                    if (request.Yaw.HasValue) model.SetYaw(AngleDegrees1f.FromDegrees(request.Yaw.Value));
                    if (request.Pitch.HasValue) model.SetPitch(AngleDegrees1f.FromDegrees(request.Pitch.Value));
                    controller.PanTo(new Tile2f(Fix32.FromInt(request.X), Fix32.FromInt(request.Y)));
                    model.ReleaseDamping();
                    request.Applied = true;
                    // Finish at the next RenderUpdate, after this frame's native camera update.
                }
                catch (Exception error) {
                    request.Error = error.Message; request.Done = true; pending = null;
                }
            }
        }

        private static object View(UiCameraState.Pose pose)
        {
            return BridgeJson.Obj("position", BridgeJson.Obj("x", pose.PivotPosition.X.ToDouble(), "y", pose.PivotPosition.Y.ToDouble()),
                "distance_tiles", pose.OrbitRadius.Value.ToDouble(), "yaw_degrees", pose.YawAngle.Degrees.ToDouble(),
                "pitch_degrees", pose.PitchAngle.Degrees.ToDouble());
        }

        public void Dispose()
        {
            lock (gate) {
                if (disposed) return;
                disposed = true;
                if (pending != null) { pending.Error = "Camera request cancelled: game session unloaded"; pending.Done = true; pending = null; }
                if (started) { loop.RenderUpdate.RemoveNonSaveable(this, RenderUpdate); started = false; }
            }
        }

        private sealed class Request : IBridgePending
        {
            private readonly BridgeCamera owner;
            public readonly int X, Y;
            public readonly Fix32? Distance, Yaw, Pitch;
            public bool Applied, Done;
            public object Previous, Result;
            public string Error;
            public Request(BridgeCamera owner, int x, int y, Fix32? distance, Fix32? yaw, Fix32? pitch)
            { this.owner = owner; X = x; Y = y; Distance = distance; Yaw = yaw; Pitch = pitch; }
            public bool Poll(out object result, out string error)
            { lock (owner.gate) { result = Result; error = Error; return Done; } }
            public void Dispose()
            {
                lock (owner.gate) {
                    if (owner.pending == this) owner.pending = null;
                    if (!Done) { Error = "Camera request cancelled"; Done = true; }
                }
            }
        }
    }
}
