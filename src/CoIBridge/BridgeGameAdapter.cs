using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Priorities;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Input;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Trains;
using Mafi.Core.Vehicles.Commands;

namespace CoIBridge
{
    public sealed class BridgeGameAdapter
    {
        public static readonly Dictionary<string, bool> Operations = MakeOperations();
        private static Dictionary<string, bool> MakeOperations()
        {
            var d = new Dictionary<string, bool>();
            d.Add("camera.center", false);
            foreach (string op in "game.status entities.list entity.get prototypes.list recipes.list trains.list train_lines.list build.validate deconstruct.validate transport.validate ports.compatible".Split(' ')) d.Add(op, false);
            foreach (string op in "build.create build.batch deconstruct.start ghost.transform transport.build transport.reverse transport.deconstruct recipe.set recipe.reorder boost.set priority.general priority.construction priority.custom priority.zipper vehicle.navigate vehicle.assign vehicle.unassign vehicle.build train.navigate train.line designation.add designation.remove surface.add mine.area storage.product storage.limits storage.alert_threshold storage.alert_enabled logistics.assign game.pause game.speed entity.enabled game.save".Split(' ')) d.Add(op, true);
            foreach (string op in "designation.area.validate designation.area.get mine.area.get forestry.area.get".Split(' ')) d.Add(op, false);
            foreach (string op in "designation.area.set designation.area.remove forestry.area mine.area.clear forestry.area.clear".Split(' ')) d.Add(op, true);
            return d;
        }
        private readonly DependencyResolver resolver;
        private readonly EntitiesManager entities;
        private readonly ProtosDb protos;
        private readonly IInputScheduler scheduler;
        private readonly BridgeCamera camera;
        public BridgeGameAdapter(DependencyResolver resolver, BridgeCamera camera)
        { this.resolver = resolver; this.camera = camera; entities = resolver.Resolve<EntitiesManager>(); protos = resolver.Resolve<ProtosDb>(); scheduler = resolver.Resolve<IInputScheduler>(); }
        private T Entity<T>(int id) where T : class, IEntity
        { var e = entities.GetEntity(new EntityId(id)); if (!e.HasValue || e.Value.IsDestroyed || !(e.Value is T)) throw new ArgumentException("Entity missing/destroyed or unsupported type: " + id + " / " + typeof(T).Name); return (T)e.Value; }
        private T Proto<T>(string id, bool unlocked = false) where T : Proto
        {
            var p = protos.Get<T>(new Proto.ID(id)); if (!p.HasValue) throw new ArgumentException("Unknown prototype: " + id);
            if (unlocked && !p.Value.IsUnlockedAndAvailable) throw new ArgumentException("Prototype is locked: " + id);
            return p.Value;
        }
        private LayoutEntityProto Building(string id)
        {
            var p = Proto<LayoutEntityProto>(id, true);
            if (p.CannotBeBuiltByPlayer || p.IsAutoBuiltByParent || p.IsPhantom || p.IsObsolete || (p.GetType().Namespace ?? "").Contains(".Sandbox"))
                throw new ArgumentException("Prototype is not available for regular player construction: " + id);
            return p;
        }
        private Tile3i Pos(object value)
        { var a = new BridgeArgs(value); var terrain = resolver.Resolve<TerrainManager>(); var p = new Tile3i(a.Int("x", 0, terrain.TerrainWidth - 1), a.Int("y", 0, terrain.TerrainHeight - 1), a.Int("z", -1024, 1024)); a.Done(); return p; }
        private Tile2i Pos2(object value)
        { var a = new BridgeArgs(value); var terrain = resolver.Resolve<TerrainManager>(); var p = new Tile2i(a.Int("x", 0, terrain.TerrainWidth - 1), a.Int("y", 0, terrain.TerrainHeight - 1)); a.Done(); return p; }
        private TileTransform Transform(BridgeArgs a)
        {
            Tile3i p = Pos(a.Take("position")); int degrees = a.Int("rotation", 0, 270, 0);
            if (degrees % 90 != 0) throw new ArgumentException("rotation must be 0,90,180,270");
            var rotation = new[] { Rotation90.Deg0, Rotation90.Deg90, Rotation90.Deg180, Rotation90.Deg270 }[degrees / 90];
            return new TileTransform(p, rotation, a.Bool("reflected", false));
        }
        private static Direction903d? Direction(BridgeArgs a, string key)
        {
            string v = a.Text(key, false); if (v == null) return null;
            switch (v) { case "+x": return Direction903d.PlusX; case "-x": return Direction903d.MinusX; case "+y": return Direction903d.PlusY; case "-y": return Direction903d.MinusY; default: throw new ArgumentException("Direction must be horizontal: " + key); }
        }
        private static Percent? Ratio(BridgeArgs a, string key) { double? v = a.Ratio(key); return v.HasValue ? (Percent?)Percent.FromDouble(v.Value) : null; }
        private IoPort Port(int id)
        {
            foreach (var entity in entities.Entities) { var owner = entity as IEntityWithPorts; if (owner != null) foreach (var p in owner.Ports) if (p.Id.Value == id) return p; }
            throw new ArgumentException("Port not found: " + id);
        }
        private TrainId TrainId(BridgeArgs a)
        {
            var id = new TrainId((ushort)a.Int("train_id", 0, UInt16.MaxValue));
            if (!resolver.Resolve<TrainsManager>().TrainsDict.ContainsKey(id)) throw new ArgumentException("Unknown train_id"); return id;
        }
        public IBridgePending Execute(string operation, Dictionary<string, object> values)
        {
            if (operation == "camera.center") return camera.Center(new BridgeArgs(values));
            if (BridgeAreas.Handles(operation)) return new BridgeAreas(resolver).Execute(operation, new BridgeArgs(values));
            var a = new BridgeArgs(values); IInputCommand command = null; Func<object> read = null; Func<IBridgePending> action = null;
            switch (operation) {
                case "game.status": read = () => { var l = resolver.Resolve<ISimLoopEvents>(); return BridgeJson.Obj("game_name", resolver.Resolve<ISaveManager>().GameName, "paused", l.IsSimPaused, "speed", l.SimSpeedMult, "step", l.CurrentStep.ToString()); }; break;
                case "entities.list": { int offset = a.Int("offset", 0, Int32.MaxValue, 0), limit = a.Int("limit", 1, 100, 25); string filter = a.Text("prototype_id", false); read = () => EntityPage(offset, limit, filter); break; }
                case "entity.get": { int id = a.Int("entity_id", 0); Entity<IEntity>(id); read = () => EntityDetails(id); break; }
                case "prototypes.list": { string kind = a.Text("kind"); int offset = a.Int("offset", 0, Int32.MaxValue, 0), limit = a.Int("limit", 1, 100, 25); read = () => PrototypePage(kind, offset, limit); break; }
                case "recipes.list": { string id = a.Text("machine_prototype_id"); int offset = a.Int("offset", 0, Int32.MaxValue, 0), limit = a.Int("limit", 1, 50, 10); var p = Proto<MachineProto>(id); read = () => RecipePage(p, offset, limit); break; }
                case "trains.list": { int offset = a.Int("offset", 0, Int32.MaxValue, 0), limit = a.Int("limit", 1, 100, 25); read = () => TrainPage(offset, limit); break; }
                case "train_lines.list": { int offset = a.Int("offset", 0, Int32.MaxValue, 0), limit = a.Int("limit", 1, 100, 25); read = () => TrainLinePage(offset, limit); break; }
                case "build.validate": case "build.create": {
                    var p = Building(a.Text("prototype_id")); var transform = Transform(a);
                    if (operation == "build.validate") read = () => ValidateBuilding(p, transform);
                    else { EnsureBuilding(p, transform); command = new CreateStaticEntityCmd(p.Id, transform, false, false); } break;
                }
                case "build.batch": {
                    var items = a.List("items", 64); if (items.Count == 0) throw new ArgumentException("Empty batch");
                    var configs = new List<EntityConfigData>();
                    foreach (var item in items) {
                        var b = new BridgeArgs(item); var p = Building(b.Text("prototype_id")); var transform = Transform(b);
                        var data = new EntityConfigData(p, resolver.Resolve<ConfigSerializationContext>()) { Transform = transform };
                        object recipeValue = b.Take("recipe_ids", false);
                        if (recipeValue != null) {
                            var list = recipeValue as List<object>; var mp = p as MachineProto;
                            if (list == null || list.Count > 64 || mp == null) throw new ArgumentException("recipe_ids needs a machine and at most 64 recipes");
                            var recipes = new List<RecipeProto>(); foreach (object rid in list) { var recipe = Proto<RecipeProto>(rid as string, true); bool supported = false; foreach (var r in mp.Recipes) if (r.Id == recipe.Id) supported = true; if (!supported) throw new ArgumentException("Recipe not supported by machine"); recipes.Add(recipe); }
                            data.Recipes = ImmutableArray.CreateRange(recipes);
                        }
                        b.Done(); EnsureBuilding(p, transform); configs.Add(data);
                    }
                    command = new BatchCreateStaticEntitiesCmd(ImmutableArray.CreateRange(configs), BuildMiniZippersMode.DeferToProto, false, false, true); break;
                }
                case "deconstruct.validate": case "deconstruct.start": {
                    var e = Entity<IStaticEntity>(a.Int("entity_id", 0));
                    var check = entities.CanRemoveEntity(e, EntityRemoveReason.Remove);
                    if (operation == "deconstruct.validate") read = () => BridgeJson.Obj("valid", check.IsSuccess && !check.IsSuppressedError, "error", check.ErrorMessage);
                    else { if (!check.IsSuccess || check.IsSuppressedError) throw new ArgumentException(check.ErrorMessage); command = new StartDeconstructionOfStaticEntityCmd(e, EntityRemoveReason.Remove, false); } break;
                }
                case "ghost.transform": { var e = Entity<StaticEntity>(a.Int("entity_id", 0)); if (e.IsConstructed) throw new ArgumentException("Only unconstructed ghosts can be transformed"); command = new TryTransformEntityCmd(e.Id, a.Bool("rotate", false), a.Bool("flip", false)); break; }
                case "transport.validate": case "transport.build": {
                    var p = Proto<TransportProto>(a.Text("prototype_id"), true); var points = new List<Tile3i>(); foreach (object v in a.List("pivots", 256)) points.Add(Pos(v)); if (points.Count < 2) throw new ArgumentException("At least two pivots required");
                    var pivots = ImmutableArray.CreateRange(points); var start = Direction(a, "start_direction"); var end = Direction(a, "end_direction");
                    var check = CheckTransport(p, pivots, start, end);
                    if (operation == "transport.validate") read = () => check;
                    else { if (!(bool)check["valid"]) throw new ArgumentException((string)check["error"]); command = new BuildTransportCmd(p.Id, pivots, ImmutableArray<Tile2i>.Empty, start, end, false, false, true); } break;
                }
                case "transport.reverse": command = new ReverseTransportCmd(Entity<Transport>(a.Int("entity_id", 0))); break;
                case "transport.deconstruct": command = new DeconstructTransportSegmentCmd(Entity<Transport>(a.Int("entity_id", 0)), Pos(a.Take("start")), Pos(a.Take("end")), false); break;
                case "ports.compatible": { var first = Port(a.Int("port_id", 0)); var other = Port(a.Int("other_port_id", 0)); read = () => BridgeJson.Obj("compatible", first.CanConnectTo(other)); break; }
                case "recipe.set": {
                    var m = Entity<Machine>(a.Int("entity_id", 0)); var r = Proto<RecipeProto>(a.Text("recipe_id"), true); bool matches = false;
                    foreach (var available in m.Prototype.Recipes) if (available.Id == r.Id) matches = true;
                    if (!matches) throw new ArgumentException("Recipe not supported by this machine"); command = new MachineSetRecipeActiveCmd(m.Id, r.Id, a.Bool("enabled")); break;
                }
                case "recipe.reorder": { var m = Entity<Machine>(a.Int("entity_id", 0)); int count = 0; foreach (var unused in m.RecipesAssigned) count++; if (count == 0) throw new ArgumentException("Machine has no assigned recipes"); command = new ReorderRecipeCmd(m.Id, a.Int("old_index", 0, count - 1), a.Int("new_index_after_remove", 0, count - 1)); break; }
                case "boost.set": { var m = Entity<Machine>(a.Int("entity_id", 0)); bool enabled = a.Bool("enabled"); if (m.IsBoostRequested == enabled) read = () => BridgeJson.Obj("unchanged", true); else command = new MachineBoostToggleCmd(m.Id); break; }
                case "priority.general": { var e = Entity<IEntityWithGeneralPriority>(a.Int("entity_id", 0)); if (!e.IsGeneralPriorityVisible) throw new ArgumentException("Priority not available"); command = new SetGeneralPriorityCmd(e.Id, a.Int("priority", 1, 15)); break; }
                case "priority.construction": command = new SetConstructionPriorityCmd(Entity<IStaticEntity>(a.Int("entity_id", 0)), a.Int("priority", 1, 15)); break;
                case "priority.custom": { var e = Entity<IEntityWithCustomPriority>(a.Int("entity_id", 0)); string key = a.Text("priority_id"); if (!e.IsCustomPriorityVisible(key)) throw new ArgumentException("Unknown/unavailable priority_id"); command = new SetCustomPriorityCmd(e, key, a.Int("priority", 1, 15)); break; }
                case "priority.zipper": { var e = Entity<Zipper>(a.Int("entity_id", 0)); string name = a.Text("port_name"); if (name.Length != 1) throw new ArgumentException("Port name must be one character"); bool found = false; foreach (var p in e.Ports) if (p.Name == name[0]) found = true; if (!found) throw new ArgumentException("Unknown port name"); command = new ZipperSetPriorityPortsCmd(e.Id, name[0], a.Bool("prioritized")); break; }
                case "vehicle.navigate": command = new NavigateVehicleToPositionCmd(Entity<PathFindingEntity>(a.Int("vehicle_id", 0)), Pos2(a.Take("position"))); break;
                case "vehicle.assign": command = new AssignVehicleToEntityCmd(Entity<Vehicle>(a.Int("vehicle_id", 0)), Entity<IEntityAssignedWithVehicles>(a.Int("entity_id", 0))); break;
                case "vehicle.unassign": command = new UnassignVehicleCmd(Entity<DynamicGroundEntity>(a.Int("vehicle_id", 0))); break;
                case "vehicle.build": command = new AddVehicleToBuildQueueCmd(Proto<DrivingEntityProto>(a.Text("prototype_id"), true), Entity<VehicleDepotBase>(a.Int("depot_id", 0)), a.Int("count", 1, 100)); break;
                case "train.navigate": command = new NavigateTrainToCmd(TrainId(a), Entity<IStaticEntity>(a.Int("target_entity_id", 0)).Id); break;
                case "train.line": {
                    var train = TrainId(a); int lineId = a.Int("line_id", -1, Int32.MaxValue);
                    var line = lineId == -1 ? TrainLineIdOrNone.None : new TrainLineIdOrNone(lineId);
                    if (line.HasValue) { TrainLine found; if (!resolver.Resolve<TrainLinesManager>().TryGetTrainLine(line.Value, out found)) throw new ArgumentException("Unknown line_id"); }
                    command = new AssignTrainLineToTrainCmd(train, line); break;
                }
                case "designation.add": {
                    var p = Proto<TerrainDesignationProto>(a.Text("prototype_id")); var data = new List<DesignationData>();
                    foreach (var value in a.List("items", 256)) { var b = new BridgeArgs(value); var origin = Pos2(b.Take("origin")); var item = new DesignationData(origin, new HeightTilesI(b.Int("height_origin", -1024, 1024)), new HeightTilesI(b.Int("height_x", -1024, 1024)), new HeightTilesI(b.Int("height_xy", -1024, 1024)), new HeightTilesI(b.Int("height_y", -1024, 1024))); b.Done(); Mafi.Localization.LocStrFormatted error; if (!resolver.Resolve<TerrainDesignationsManager>().IsDesignationAllowed(item, out error)) throw new ArgumentException(error.ToString()); data.Add(item); }
                    if (data.Count == 0) throw new ArgumentException("Empty designation list"); command = new AddTerrainDesignationsCmd(p.Id, ImmutableArray.CreateRange(data)); break;
                }
                case "designation.remove": { var origins = new List<Tile2i>(); foreach (var value in a.List("origins", 256)) origins.Add(Pos2(value)); command = new RemoveDesignationsCmd(ImmutableArray.CreateRange(origins)); break; }
                case "surface.add": {
                    var p = Proto<SurfaceDesignationProto>(a.Text("prototype_id")); var data = new List<SurfaceDesignationData>();
                    foreach (var value in a.List("items", 256)) { var b = new BridgeArgs(value); var origin = Pos2(b.Take("origin")); var surface = Proto<TerrainTileSurfaceProto>(b.Text("surface_prototype_id"), true); var item = new SurfaceDesignationData(origin, (uint)b.Int("tiles_bitmap", 1, 65535), surface.SlimId); b.Done(); Mafi.Localization.LocStrFormatted error; if (!resolver.Resolve<SurfaceDesignationsManager>().IsDesignationAllowed(p, item, out error)) throw new ArgumentException(error.ToString()); data.Add(item); }
                    command = new AddSurfaceDesignationsCmd(p.Id, ImmutableArray.CreateRange(data)); break;
                }
                case "storage.product": command = new StorageSetProductCmd(Entity<Storage>(a.Int("entity_id", 0)), Proto<ProductProto>(a.Text("product_id"))); break;
                case "storage.limits": { var e = Entity<Storage>(a.Int("entity_id", 0)); var i = Ratio(a, "import_until"); var o = Ratio(a, "export_from"); var f = Ratio(a, "transport_from"); var u = Ratio(a, "transport_until"); if (!i.HasValue && !o.HasValue && !f.HasValue && !u.HasValue) throw new ArgumentException("No limits supplied"); command = new StorageSetSliderStepCmd(e.Id, i, o, f, u); break; }
                case "storage.alert_threshold": { var e = Entity<Storage>(a.Int("entity_id", 0)); var ratio = Ratio(a, "ratio"); if (!ratio.HasValue) throw new ArgumentException("ratio required"); command = new StorageAlertSetThresholdCmd(e.Id, ratio.Value, a.Bool("above")); break; }
                case "storage.alert_enabled": command = new StorageAlertSetEnabledCmd(Entity<Storage>(a.Int("entity_id", 0)).Id, a.Bool("enabled"), a.Bool("above")); break;
                case "logistics.assign": command = new AssignStaticEntityCmd(Entity<IEntityAssignedAsOutput>(a.Int("output_entity_id", 0)), Entity<IEntityAssignedAsInput>(a.Int("input_entity_id", 0))); break;
                case "game.pause": command = new SetSimPauseStateCmd(a.Bool("paused")); break;
                case "game.speed": command = new GameSpeedChangeCmd(a.Int("multiplier", 1, 3)); break;
                case "entity.enabled": { var e = Entity<IEntity>(a.Int("entity_id", 0)); if (!e.CanBePaused) throw new ArgumentException("Entity cannot be paused/enabled manually"); command = new SetEntityEnabledCmd(e, a.Bool("enabled")); break; }
                case "game.save": { string name = a.Text("name"); if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..") throw new ArgumentException("Invalid save name"); action = () => new BridgeSave(resolver.Resolve<ISaveManager>(), name); break; }
                default: throw new ArgumentException("Unknown operation");
            }
            a.Done(); // Reject typos BEFORE scheduling any mutation.
            if (read != null) return new BridgeImmediate(read());
            if (action != null) return action();
            if (command == null) throw new InvalidOperationException("Operation has no handler");
            return new BridgeCommand(scheduler.ScheduleInputCmd(command));
        }

        private Dictionary<string, object> ValidateBuilding(LayoutEntityProto p, TileTransform transform)
        {
            var data = new EntityAddRequestData(transform, true, null, true, false, false);
            var request = LayoutEntityAddRequest.GetPooledInstanceToCreateEntity(p, data, EntityAddReason.New, false);
            try { var result = entities.CanAdd(request); bool valid = result.IsSuccess && !result.IsSuppressedError; return BridgeJson.Obj("valid", valid, "error", !valid && String.IsNullOrEmpty(result.ErrorMessage) ? "Game placement validation rejected this position (terrain, collision or placement rules)" : result.ErrorMessage); }
            finally { request.ReturnToPool(); }
        }
        private void EnsureBuilding(LayoutEntityProto p, TileTransform transform) { var check = ValidateBuilding(p, transform); if (!(bool)check["valid"]) throw new ArgumentException((string)check["error"]); }
        private Dictionary<string, object> CheckTransport(TransportProto p, ImmutableArray<Tile3i> pivots, Direction903d? start, Direction903d? end)
        {
            CanBuildTransportResult result; Mafi.Localization.LocStrFormatted error; Option<IStaticEntity> blocking;
            bool valid = resolver.Resolve<TransportsManager>().CanBuildOrJoinTransport(p, pivots, new Mafi.Collections.Set<Tile2i>(), start, end, false, out result, out error, out blocking);
            return BridgeJson.Obj("valid", valid, "error", error.ToString(), "blocking_entity_id", blocking.HasValue ? (object)blocking.Value.Id.Value : null);
        }
        private static object EntityRow(IEntity e)
        { var s = e as IStaticEntity; return BridgeJson.Obj("entity_id", e.Id.Value, "prototype_id", e.Prototype.Id.ToString(), "runtime_type", e.GetType().FullName, "enabled", e.IsEnabled, "paused", e.IsPaused, "position", s == null ? null : BridgeJson.Obj("x", s.CenterTile.X, "y", s.CenterTile.Y, "z", s.CenterTile.Z)); }
        private object EntityPage(int offset, int limit, string filter)
        {
            var list = new List<IEntity>(); foreach (var e in entities.Entities) if (filter == null || e.Prototype.Id.ToString() == filter) list.Add(e);
            list.Sort((x,y) => x.Id.Value.CompareTo(y.Id.Value)); var rows = new List<object>(); for (int i = offset; i < list.Count && rows.Count < limit; i++) rows.Add(EntityRow(list[i]));
            return Page(rows, offset, list.Count);
        }
        private object EntityDetails(int id)
        {
            var e = Entity<IEntity>(id); string time = DateTime.UtcNow.ToString("o"); RecipeCatalog recipes;
            var production = ProductionReader.Capture(entities, "query", time, out recipes);
            object machine = null, storage = null, status = null; var ports = new List<object>();
            foreach (var r in production.machines) if (r.entity_id == id) machine = BridgeJson.FromDto(r);
            foreach (var r in production.storages) if (r.entity_id == id) storage = BridgeJson.FromDto(r);
            foreach (var r in EntityStatusReader.Capture(entities, "query", time).entities) if (r.entity_id == id) status = BridgeJson.FromDto(r);
            foreach (var r in PortsReader.Capture(entities, "query", time).ports) if (r.entity_id == id) ports.Add(BridgeJson.FromDto(r));
            return BridgeJson.Obj("captured_at_utc", time, "entity", EntityRow(e), "machine", machine, "storage", storage, "status", status, "ports", ports);
        }
        private object PrototypePage(string kind, int offset, int limit)
        {
            var list = new List<Proto>();
            foreach (var p in protos.All<Proto>()) {
                bool match = kind == "building" ? p is LayoutEntityProto : kind == "transport" ? p is TransportProto : kind == "product" ? p is ProductProto : kind == "recipe" ? p is RecipeProto : kind == "vehicle" ? p is DrivingEntityProto : kind == "designation" ? p is TerrainDesignationProto : kind == "surface" ? p is TerrainTileSurfaceProto : kind == "surface_designation" ? p is SurfaceDesignationProto : false;
                if (match) list.Add(p);
            }
            if (Array.IndexOf(new[] { "building", "transport", "product", "recipe", "vehicle", "designation", "surface", "surface_designation" }, kind) < 0) throw new ArgumentException("Unknown prototype kind");
            list.Sort((x,y) => StringComparer.Ordinal.Compare(x.Id.ToString(), y.Id.ToString())); var rows = new List<object>();
            for (int i = offset; i < list.Count && rows.Count < limit; i++) rows.Add(BridgeJson.Obj("prototype_id", list[i].Id.ToString(), "runtime_type", list[i].GetType().FullName, "unlocked", list[i].IsUnlockedAndAvailable));
            return Page(rows, offset, list.Count);
        }
        private object RecipePage(MachineProto machine, int offset, int limit)
        {
            var rows = new List<object>(); int total = 0;
            foreach (var b in machine.RecipeBindings) {
                if (total++ < offset || rows.Count >= limit) continue;
                var inputs = new List<object>(); var outputs = new List<object>();
                foreach (var q in b.Recipe.AllInputs) inputs.Add(BridgeJson.Obj("product_id", q.Product.Id.ToString(), "quantity", q.Quantity.Value));
                foreach (var q in b.Recipe.AllOutputs) outputs.Add(BridgeJson.Obj("product_id", q.Product.Id.ToString(), "quantity", q.Quantity.Value));
                rows.Add(BridgeJson.Obj("recipe_id", b.Recipe.Id.ToString(), "duration_ticks", b.Duration.Ticks, "multiplier", b.Multiplier, "inputs", inputs, "outputs", outputs));
            }
            return Page(rows, offset, total);
        }
        private object TrainPage(int offset, int limit)
        {
            var list = new List<Train>(); foreach (var t in resolver.Resolve<TrainsManager>().Trains) list.Add(t); list.Sort((x,y) => x.TrainId.Value.CompareTo(y.TrainId.Value)); var rows = new List<object>();
            for (int i = offset; i < list.Count && rows.Count < limit; i++) rows.Add(BridgeJson.Obj("train_id", list[i].TrainId.Value, "paused", list[i].IsPaused, "driving_mode", list[i].DrivingMode.ToString())); return Page(rows, offset, list.Count);
        }
        private static object Page(List<object> rows, int offset, int total) { return BridgeJson.Obj("items", rows, "total", total, "next_offset", offset + rows.Count < total ? (object)(offset + rows.Count) : null); }
        private object TrainLinePage(int offset, int limit)
        {
            var list = new List<TrainLine>(); foreach (var line in resolver.Resolve<TrainLinesManager>().Lines) list.Add(line);
            list.Sort((x,y) => x.Id.Value.CompareTo(y.Id.Value)); var rows = new List<object>();
            for (int i = offset; i < list.Count && rows.Count < limit; i++) rows.Add(BridgeJson.Obj("line_id", list[i].Id.Value, "name", list[i].Name.ToString(), "stations_count", list[i].StationsCount));
            return Page(rows, offset, list.Count);
        }
    }
}
