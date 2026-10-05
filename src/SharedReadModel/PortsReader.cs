using System.Collections.Generic;
using System.Runtime.Serialization;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Ports;

#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    [DataContract]
    public sealed class PortsSnapshot
    {
        [DataMember] public int schema_version;
        [DataMember] public string snapshot_id;
        [DataMember] public string captured_at_utc;
        [DataMember] public List<int> supported_entity_ids;
        [DataMember] public List<PortState> ports;
        [DataMember] public string[] limitations;
    }

    [DataContract]
    public sealed class PortState
    {
        [DataMember] public int port_id;
        [DataMember] public int entity_id;
        [DataMember] public int port_index;
        [DataMember] public string name;
        [DataMember] public TilePosition position;
        [DataMember] public TilePosition direction_vector;
        [DataMember] public string direction;
        [DataMember] public string io_type;
        [DataMember] public string shape_prototype_id;
        [DataMember] public string allowed_product_type;
        [DataMember] public bool is_connected;
        [DataMember] public bool is_connected_as_input;
        [DataMember] public bool is_connected_as_output;
        [DataMember] public int? connected_port_id;
        [DataMember] public int? connected_entity_id;
        [DataMember] public TilePosition expected_connected_port_position;
    }

    public static class PortsReader
    {
        private static TilePosition Position(Tile3i tile)
        {
            return new TilePosition { X = tile.X, Y = tile.Y, Z = tile.Z };
        }

        public static PortsSnapshot Capture(EntitiesManager entities, string id, string time)
        {
            var result = new PortsSnapshot {
                schema_version = 1, snapshot_id = id, captured_at_utc = time,
                supported_entity_ids = new List<int>(), ports = new List<PortState>(),
                limitations = new[] {
                    "Runtime IEntityWithPorts instances only, including transports; absent entity IDs mean unsupported, not zero ports.",
                    "Positions are absolute game Tile3i coordinates. Direction vectors point outwards, including input ports; they are not product flow vectors.",
                    "Runtime positions/directions already include building rotation/reflection; do not transform them again.",
                    "Shape and allowed product type are native API identifiers, not a per-port recipe/product whitelist.",
                    "Disconnected does not imply buildable: terrain, obstacles, transport constraints and construction state must be checked separately.",
                    "Port IDs describe this snapshot; stability across saves/rebuilds is not established. Footprints and non-port utility connections are not covered."
                }
            };
            foreach (var entity in entities.Entities)
            {
                var owner = entity as IEntityWithPorts;
                if (owner == null) continue;
                result.supported_entity_ids.Add(entity.Id.Value);
                foreach (var port in owner.Ports)
                {
                    var vector = port.Direction.DirectionVector;
                    var other = port.ConnectedPort;
                    result.ports.Add(new PortState {
                        port_id = port.Id.Value, entity_id = entity.Id.Value, port_index = port.PortIndex,
                        name = port.Name.ToString(), position = Position(port.Position),
                        direction = port.Direction.ToString(),
                        direction_vector = new TilePosition { X = vector.X, Y = vector.Y, Z = vector.Z },
                        io_type = port.Type.ToString(), shape_prototype_id = port.ShapePrototype.Id.ToString(),
                        allowed_product_type = port.ShapePrototype.AllowedProductType.ToString(),
                        is_connected = port.IsConnected, is_connected_as_input = port.IsConnectedAsInput,
                        is_connected_as_output = port.IsConnectedAsOutput,
                        connected_port_id = other.HasValue ? (int?)other.Value.Id.Value : null,
                        connected_entity_id = other.HasValue ? (int?)other.Value.OwnerEntity.Id.Value : null,
                        expected_connected_port_position = Position(port.ExpectedConnectedPortCoord)
                    });
                }
            }
            result.supported_entity_ids.Sort();
            result.ports.Sort((a, b) => a.port_id.CompareTo(b.port_id));
            return result;
        }
    }
}
