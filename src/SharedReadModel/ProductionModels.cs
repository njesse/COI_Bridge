using System.Collections.Generic;
using System.Runtime.Serialization;
#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    [DataContract]
    public sealed class ProductionSnapshot
    {
        [DataMember(Order = 0)] public int schema_version;
        [DataMember(Order = 1)] public string snapshot_id;
        [DataMember(Order = 2)] public string captured_at_utc;
        [DataMember(Order = 3)] public List<MachineState> machines;
        [DataMember(Order = 4)] public List<StorageState> storages;
        [DataMember(Order = 5)] public string[] limitations;
    }
    [DataContract]
    public sealed class MachineState
    {
        [DataMember(Order = 0)] public int entity_id;
        [DataMember(Order = 1)] public string state;
        [DataMember(Order = 2)] public bool worked_this_tick;
        [DataMember(Order = 3)] public double progress_ratio;
        [DataMember(Order = 4)] public double utilization_ratio;
        [DataMember(Order = 5)] public List<string> assigned_recipe_ids;
        [DataMember(Order = 6)] public string last_recipe_id;
        [DataMember(Order = 7)] public List<BufferState> buffers;
        [DataMember] public bool is_boosted;
        [DataMember] public double speed_ratio;
        [DataMember] public double duration_multiplier;
        [DataMember] public double virtual_output_multiplier;
        [DataMember] public long recipe_production_ticks;
        [DataMember] public double monthly_unity_consumed;
        [DataMember] public double max_monthly_unity_consumed;
    }
    [DataContract]
    public sealed class BufferState
    {
        [DataMember(Order = 0)] public string product_id;
        [DataMember(Order = 1)] public string direction;
        [DataMember(Order = 2)] public int quantity;
        [DataMember(Order = 3)] public int capacity;
    }
    [DataContract]
    public sealed class StorageState
    {
        [DataMember(Order = 0)] public int entity_id;
        [DataMember(Order = 1)] public string product_id;
        [DataMember(Order = 2)] public int quantity;
        [DataMember(Order = 3)] public int capacity;
        [DataMember(Order = 4)] public bool logistics_input_disabled;
        [DataMember(Order = 5)] public bool logistics_output_disabled;
        [DataMember] public bool is_full;
        [DataMember] public bool is_empty;
        [DataMember] public double fill_ratio;
        [DataMember] public string logistics_input_control;
        [DataMember] public string logistics_output_control;
        [DataMember] public bool linked_station_disables_input;
        [DataMember] public bool linked_station_disables_output;
        [DataMember] public List<int> connected_storage_ids;
        [DataMember] public StorageSettings settings;
    }
    [DataContract]
    public sealed class RecipeCatalog
    {
        [DataMember(Order = 0)] public int schema_version;
        [DataMember(Order = 1)] public string snapshot_id;
        [DataMember(Order = 2)] public string captured_at_utc;
        [DataMember(Order = 3)] public string scope;
        [DataMember(Order = 4)] public List<RecipeDefinition> bindings;
    }
    [DataContract]
    public sealed class RecipeDefinition
    {
        [DataMember(Order = 0)] public string machine_prototype_id;
        [DataMember(Order = 1)] public string recipe_id;
        [DataMember(Order = 2)] public int duration_ticks;
        [DataMember(Order = 3)] public int multiplier;
        [DataMember(Order = 4)] public List<RecipeAmount> inputs;
        [DataMember(Order = 5)] public List<RecipeAmount> outputs;
    }
    [DataContract]
    public sealed class RecipeAmount
    {
        [DataMember(Order = 0)] public string product_id;
        [DataMember(Order = 1)] public int quantity;
    }
}
