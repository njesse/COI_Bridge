using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Numerics;

namespace CoIBridge
{
    // Stateless translation into the game's own designations and tower areas.
    internal sealed class BridgeAreas
    {
        private readonly DependencyResolver resolver;
        private readonly TerrainManager terrain;
        private readonly TerrainDesignationsManager manager;
        public BridgeAreas(DependencyResolver resolver)
        { this.resolver = resolver; terrain = resolver.Resolve<TerrainManager>(); manager = resolver.Resolve<TerrainDesignationsManager>(); }

        public static bool Handles(string op)
        { return op.StartsWith("designation.area.", StringComparison.Ordinal) || op == "mine.area" || op.StartsWith("mine.area.", StringComparison.Ordinal) || op == "forestry.area" || op.StartsWith("forestry.area.", StringComparison.Ordinal); }

        public IBridgePending Execute(string op, BridgeArgs args)
        { return op.StartsWith("designation.", StringComparison.Ordinal) ? Designations(op, args) : Tower(op, args); }

        private Vector2i Point(object value)
        {
            var a = new BridgeArgs(value);
            var p = new Vector2i(a.Int("x",0,terrain.TerrainWidth-1), a.Int("y",0,terrain.TerrainHeight-1)); a.Done(); return p;
        }
        private List<Vector2i> Vertices(BridgeArgs a)
        {
            var points = new List<Vector2i>(); foreach (var value in a.List("vertices",64)) points.Add(Point(value));
            AreaGeometry.Validate(points); return points;
        }
        private List<Vector2i> Shape(BridgeArgs args)
        {
            var a = new BridgeArgs(args.Take("shape")); var type = a.Text("type"); List<Vector2i> points;
            if (type == "rectangle") {
                var origin = Point(a.Take("origin")); var size = new BridgeArgs(a.Take("size"));
                int w = size.Int("x",1,terrain.TerrainWidth-1-origin.X), h = size.Int("y",1,terrain.TerrainHeight-1-origin.Y); size.Done();
                points = new List<Vector2i> { origin, new Vector2i(origin.X+w,origin.Y), new Vector2i(origin.X+w,origin.Y+h), new Vector2i(origin.X,origin.Y+h) };
            } else if (type == "polygon") points = Vertices(a);
            else throw new ArgumentException("shape.type must be rectangle or polygon");
            a.Done(); return points;
        }
        private static Proto.ID Kind(string kind)
        {
            switch (kind) {
                case "forestry": return IdsCore.TerrainDesignators.ForestryDesignator;
                case "mining": return IdsCore.TerrainDesignators.MiningDesignator;
                case "level": return IdsCore.TerrainDesignators.LevelDesignator;
                case "dumping": return IdsCore.TerrainDesignators.DumpingDesignator;
                default: throw new ArgumentException("kind must be forestry, mining, level or dumping");
            }
        }
        private List<Tile2i> Cells(List<Vector2i> polygon)
        {
            int minX = Int32.MaxValue, minY = Int32.MaxValue, maxX = 0, maxY = 0;
            foreach (var p in polygon) { minX = Math.Min(minX,p.X); minY = Math.Min(minY,p.Y); maxX = Math.Max(maxX,p.X); maxY = Math.Max(maxY,p.Y); }
            var first = TerrainDesignation.GetOrigin(new Tile2i(minX,minY)); var result = new List<Tile2i>();
            int size = TerrainDesignation.SIZE_TILES;
            for (int y = first.Y; y < maxY; y += size) for (int x = first.X; x < maxX; x += size) {
                if (!AreaGeometry.OverlapsCell(polygon,x,y,size)) continue;
                if (x+size >= terrain.TerrainWidth || y+size >= terrain.TerrainHeight) throw new ArgumentException("Raster expansion exceeds terrain boundary");
                result.Add(new Tile2i(x,y));
                if (result.Count > 256) throw new ArgumentException("Area exceeds 256 native raster cells; split the request");
            }
            if (result.Count == 0) throw new ArgumentException("Empty raster selection");
            return result;
        }
        private static object Origin(Tile2i p) { return BridgeJson.Obj("x",p.X,"y",p.Y); }
        private static object Data(DesignationData d)
        { return BridgeJson.Obj("origin",Origin(d.OriginTile),"size_tiles",TerrainDesignation.SIZE_TILES,
            "height_origin",d.OriginTargetHeight.Value,"height_x",d.PlusXTargetHeight.Value,"height_xy",d.PlusXyTargetHeight.Value,"height_y",d.PlusYTargetHeight.Value); }
        private object ReadCells(List<Tile2i> cells)
        {
            var rows = new List<object>();
            foreach (var origin in cells) {
                var value = manager.GetDesignationAt(origin); object current = null;
                if (value.HasValue) {
                    var d = value.Value;
                    current = BridgeJson.Obj("prototype_id",d.ProtoId.ToString(),"data",Data(d.Data),"is_fulfilled",d.IsFulfilled,
                        "is_mining_fulfilled",d.IsMiningFulfilled,"is_dumping_fulfilled",d.IsDumpingFulfilled,"is_forestry",d.IsForestry);
                }
                rows.Add(BridgeJson.Obj("origin",Origin(origin),"designation",current));
            }
            return rows;
        }
        private IBridgePending Designations(string op, BridgeArgs a)
        {
            string kind = a.Text("kind"); var protoId = Kind(kind); var cells = Cells(Shape(a));
            if (op == "designation.area.get") { a.Done(); return new BridgeImmediate(BridgeJson.Obj("kind",kind,"cells",ReadCells(cells))); }
            if (op == "designation.area.remove") {
                a.Done(); var origins = new List<Tile2i>();
                foreach (var cell in cells) { var d = manager.GetDesignationAt(cell); if (d.HasValue && d.Value.ProtoId == protoId) origins.Add(cell); }
                if (origins.Count == 0) return new BridgeImmediate(BridgeJson.Obj("requested_count",0,"matched_count",0,"partial",false,"cells",ReadCells(cells)));
                var cmd = new RemoveDesignationsCmd(ImmutableArray.CreateRange(origins));
                return Schedule(cmd, () => Outcome(cells,origins,null,protoId));
            }
            var proto = resolver.Resolve<ProtosDb>().Get<TerrainDesignationProto>(protoId);
            if (!proto.HasValue) throw new ArgumentException("Designation prototype unavailable");
            if (!proto.Value.IsUnlockedAndAvailable) throw new ArgumentException("Designation prototype is locked");
            var data = new List<DesignationData>();
            if (kind == "forestry") {
                foreach (var cell in cells) data.Add(DesignationDataFactory.CreateFlatNoSnapping(cell,proto.Value.PreferInitialBelowTerrain,terrain));
            } else {
                int height = a.Int("target_height",-1024,1024); string profile = a.Text("profile",false) ?? "flat";
                DesignationType type; var direction = Direction90.PlusX;
                switch (profile) {
                    case "flat": type = DesignationType.Flat; break;
                    case "ramp_up": type = DesignationType.RampUp; break;
                    case "ramp_down": type = DesignationType.RampDown; break;
                    default: throw new ArgumentException("profile must be flat, ramp_up or ramp_down");
                }
                if (type != DesignationType.Flat) {
                    switch (a.Text("direction")) {
                        case "+x": direction = Direction90.PlusX; break; case "-x": direction = Direction90.MinusX; break;
                        case "+y": direction = Direction90.PlusY; break; case "-y": direction = Direction90.MinusY; break;
                        default: throw new ArgumentException("direction must be +x, -x, +y or -y");
                    }
                }
                int minX = Int32.MaxValue,minY = Int32.MaxValue,maxX = 0,maxY = 0;
                var selected = new HashSet<Tile2i>(cells);
                foreach (var c in cells) { minX=Math.Min(minX,c.X);minY=Math.Min(minY,c.Y);maxX=Math.Max(maxX,c.X);maxY=Math.Max(maxY,c.Y); }
                foreach (var item in DesignationDataFactory.CreateArea(type,new Tile2i(minX,minY),new Tile2i(maxX,maxY),new HeightTilesI(height),direction))
                    if (selected.Contains(item.OriginTile)) data.Add(item);
                if (data.Count != cells.Count) throw new InvalidOperationException("Native designation factory did not cover the selected cells");
                // Native traversal order need not match our polygon raster traversal.
                var byOrigin = new Dictionary<Tile2i,DesignationData>(); foreach (var item in data) byOrigin.Add(item.OriginTile,item);
                data.Clear(); foreach (var cell in cells) data.Add(byOrigin[cell]);
            }
            a.Done(); // Parse all arguments before scheduling any command.
            var planned = new List<object>(); var errors = new List<object>();
            foreach (var item in data) {
                planned.Add(Data(item)); Mafi.Localization.LocStrFormatted error;
                bool allowed = manager.IsDesignationAllowed(item,out error);
                bool heightsValid = item.OriginTargetHeight.Value >= -1024 && item.OriginTargetHeight.Value <= 1024
                    && item.PlusXTargetHeight.Value >= -1024 && item.PlusXTargetHeight.Value <= 1024
                    && item.PlusYTargetHeight.Value >= -1024 && item.PlusYTargetHeight.Value <= 1024
                    && item.PlusXyTargetHeight.Value >= -1024 && item.PlusXyTargetHeight.Value <= 1024;
                if (!allowed || !heightsValid) errors.Add(BridgeJson.Obj("origin",Origin(item.OriginTile),"error",!heightsValid ? "Ramp height exceeds -1024..1024" : error.ToString()));
            }
            if (op == "designation.area.validate") return new BridgeImmediate(BridgeJson.Obj("valid",errors.Count==0,"kind",kind,"prototype_id",protoId.ToString(),"planned",planned,"errors",errors,"cells",ReadCells(cells)));
            if (errors.Count != 0) throw new ArgumentException("Designation area rejected: " + BridgeJson.Encode(errors));
            return Schedule(new AddTerrainDesignationsCmd(protoId,ImmutableArray.CreateRange(data)), () => Outcome(cells,cells,data,protoId));
        }
        private object Outcome(List<Tile2i> cells, List<Tile2i> requested, List<DesignationData> expected, Proto.ID proto)
        {
            int matched = 0;
            for (int i = 0; i < requested.Count; i++) {
                var actual = manager.GetDesignationAt(requested[i]);
                if (expected == null ? (!actual.HasValue || actual.Value.ProtoId != proto)
                    : (actual.HasValue && actual.Value.ProtoId == proto && actual.Value.Data.Equals(expected[i]))) matched++;
            }
            return BridgeJson.Obj("requested_count",requested.Count,"matched_count",matched,"partial",matched!=requested.Count,"cells",ReadCells(cells));
        }
        private IBridgePending Tower(string op, BridgeArgs a)
        {
            int id = a.Int("entity_id",0); var entity = resolver.Resolve<EntitiesManager>().GetEntity(new EntityId(id));
            bool mine = op.StartsWith("mine.",StringComparison.Ordinal);
            if (!entity.HasValue || entity.Value.IsDestroyed || (mine ? !(entity.Value is MineTower) : !(entity.Value is ForestryTower)))
                throw new ArgumentException("Entity missing/destroyed or wrong tower type");
            var tower = (IAreaManagingTower)entity.Value;
            if (op.EndsWith(".get",StringComparison.Ordinal)) { a.Done(); return new BridgeImmediate(TowerState(id,tower.Area)); }
            PolygonTerrainArea2i area = default(PolygonTerrainArea2i);
            if (!op.EndsWith(".clear",StringComparison.Ordinal)) {
                var points = a.Values.ContainsKey("shape") ? Shape(a) : Vertices(a);
                string error;
                if (!PolygonTerrainArea2i.TryCreate(new Polygon2i(ImmutableArray.CreateRange(points)),out area,out error)) throw new ArgumentException(error);
            }
            a.Done();
            IInputCommand cmd = mine ? (IInputCommand)new MineTowerAreaChangeCmd(entity.Value.Id,area) : new ForestryTowerAreaChangeCmd(entity.Value.Id,area);
            return Schedule(cmd, () => BridgeJson.Obj("area",TowerState(id,tower.Area),"matches_requested",SameArea(area,tower.Area),"partial",!SameArea(area,tower.Area)));
        }
        private static bool SameArea(PolygonTerrainArea2i left, PolygonTerrainArea2i right)
        {
            if (left.IsEmpty || right.IsEmpty) return left.IsEmpty == right.IsEmpty;
            var expected = new HashSet<Vector2i>(); foreach (var p in left.Polygon.Vertices) expected.Add(p);
            var actual = new HashSet<Vector2i>(); foreach (var p in right.Polygon.Vertices) actual.Add(p);
            return expected.SetEquals(actual);
        }
        private static object TowerState(int id, PolygonTerrainArea2i area)
        {
            var points = new List<object>(); if (!area.IsEmpty) foreach (var p in area.Polygon.Vertices) points.Add(BridgeJson.Obj("x",p.X,"y",p.Y));
            return BridgeJson.Obj("entity_id",id,"vertices",points,"is_empty",area.IsEmpty);
        }
        private IBridgePending Schedule(IInputCommand command, Func<object> after)
        { return new BridgeAreaCommand(resolver.Resolve<IInputScheduler>().ScheduleInputCmd(command),after); }
    }
    internal sealed class BridgeAreaCommand : IBridgePending
    {
        private readonly BridgeCommand command;
        private readonly Func<object> after;
        public BridgeAreaCommand(IInputCommand command, Func<object> after) { this.command = new BridgeCommand(command); this.after = after; }
        public bool Poll(out object result, out string error)
        {
            object native;
            if (!command.Poll(out native,out error)) { result = null; return false; }
            result = BridgeJson.Obj("native",native,"observed",after()); return true;
        }
        public void Dispose() { command.Dispose(); }
    }
}
