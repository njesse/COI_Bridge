using System.Collections.Generic;
using System.Runtime.Serialization;
#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    [DataContract]
    public sealed class StorageSettings
    {
        [DataMember] public bool alerts_available;
        [DataMember] public bool alert_above_enabled;
        [DataMember] public double alert_above_ratio;
        [DataMember] public bool alert_below_enabled;
        [DataMember] public double alert_below_ratio;
        [DataMember] public double import_until_ratio;
        [DataMember] public double export_from_ratio;
        [DataMember] public double transport_from_ratio;
        [DataMember] public double transport_until_ratio;
        [DataMember] public int import_priority;
        [DataMember] public int export_priority;
        [DataMember] public int usable_capacity;
        [DataMember] public bool cleaning_in_progress;
        [DataMember] public bool only_assigned_vehicles_allowed;
        [DataMember] public ulong zone_mask;
        [DataMember] public List<int> assigned_vehicle_ids;
        [DataMember] public List<string> allowed_truck_group_ids;
    }
    [DataContract]
    public sealed class EntityStatusSnapshot
    {
        [DataMember] public int schema_version;
        [DataMember] public string snapshot_id;
        [DataMember] public string captured_at_utc;
        [DataMember] public List<EntityStatus> entities;
        [DataMember] public string[] limitations;
    }
    [DataContract]
    public sealed class EntityStatus
    {
        [DataMember] public int entity_id;
        [DataMember] public bool can_be_paused;
        [DataMember] public bool is_destroyed;
        [DataMember] public bool? is_constructed;
        [DataMember] public bool? is_being_upgraded;
        [DataMember] public int? general_priority;
        [DataMember] public bool? general_priority_visible;
        [DataMember] public bool? cargo_affected_by_priority;
        [DataMember] public int? workers_needed;
        [DataMember] public bool? has_workers_cached;
        [DataMember] public bool? boost_requested;
        [DataMember] public double? boost_cost_unity;
        [DataMember] public bool supports_electricity;
        [DataMember] public int? power_required_raw;
        [DataMember] public ConsumerStatus electricity;
        [DataMember] public bool supports_computing;
        [DataMember] public int? computing_required_raw;
        [DataMember] public ConsumerStatus computing;
        [DataMember] public bool supports_maintenance;
        [DataMember] public MaintenanceState maintenance;
        [DataMember] public GeneratorStatus electricity_generation;
        [DataMember] public LogisticsState logistics;
        [DataMember] public SimpleLogisticsState simple_logistics;
        [DataMember] public List<int> assigned_input_entity_ids;
        [DataMember] public List<int> assigned_output_entity_ids;
        [DataMember] public bool? allow_non_assigned_output;
    }
    [DataContract]
    public sealed class ConsumerStatus
    {
        [DataMember] public bool is_enabled;
        [DataMember] public int priority;
        [DataMember] public bool insufficient_supply;
        [DataMember] public bool did_consume_last_tick;
        [DataMember] public int required_raw;
        [DataMember] public int charged_raw;
        [DataMember] public bool? is_surplus_consumer;
    }
    [DataContract]
    public sealed class MaintenanceState
    {
        [DataMember] public bool is_broken;
        [DataMember] public bool is_idle;
        [DataMember] public double points_current;
        [DataMember] public double points_max;
        [DataMember] public double breakdown_chance_ratio;
        [DataMember] public double broken_duration_days;
        [DataMember] public double? quick_repair_cost_unity;
        [DataMember] public string product_id;
        [DataMember] public double per_month;
        [DataMember] public double max_per_month;
    }
    [DataContract]
    public sealed class GeneratorStatus
    {
        [DataMember] public int priority;
        [DataMember] public bool is_surplus_generator;
        [DataMember] public int max_capacity_raw;
        [DataMember] public int capacity_this_tick_raw;
        [DataMember] public int generated_this_tick_raw;
    }
    [DataContract]
    public sealed class LogisticsState
    {
        [DataMember] public bool can_disable_input;
        [DataMember] public bool can_disable_output;
        [DataMember] public string input_mode;
        [DataMember] public string output_mode;
    }
    [DataContract]
    public sealed class SimpleLogisticsState
    {
        [DataMember] public string input_control;
        [DataMember] public string output_control;
        [DataMember] public bool input_disabled;
        [DataMember] public bool output_disabled;
    }
}
