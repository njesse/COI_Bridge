using System.Collections.Generic;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Priorities;
using Mafi.Core.Population;
using Mafi.Core.Maintenance;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.ComputingPower;

#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    public static class EntityStatusReader
    {
        public static EntityStatusSnapshot Capture(EntitiesManager manager, string id, string time)
        {
            var result = new EntityStatusSnapshot { schema_version = 1, snapshot_id = id,
                captured_at_utc = time, entities = new List<EntityStatus>(), limitations = new[] {
                    "Shared read-only interfaces only; not every specialized entity state is covered.",
                    "Null means unsupported or unavailable, never a healthy/zero state. Consumer support flags distinguish missing interface from absent consumer.",
                    "Ratios use 1=100%. Electricity/computing values are native raw units, not labeled as kW or TFLOPS.",
                    "Cached worker and last-tick utility states are captured before simulation starts and may not describe subsequent operation.",
                    "Entity assignments are logistics preferences, not physical transport connections."
                } };
            foreach (var entity in manager.Entities)
            {
                var item = new EntityStatus { entity_id = entity.Id.Value,
                    can_be_paused = entity.CanBePaused, is_destroyed = entity.IsDestroyed };
                var building = entity as StaticEntity;
                if (building != null) { item.is_constructed = building.IsConstructed; item.is_being_upgraded = building.IsBeingUpgraded; }
                var priority = entity as IEntityWithGeneralPriority;
                if (priority != null) { item.general_priority = priority.GeneralPriority; item.general_priority_visible = priority.IsGeneralPriorityVisible; item.cargo_affected_by_priority = priority.IsCargoAffectedByGeneralPriority; }
                var workers = entity as IEntityWithWorkers;
                if (workers != null) { item.workers_needed = workers.WorkersNeeded; item.has_workers_cached = workers.HasWorkersCached; }
                var boost = entity as IEntityWithBoost;
                if (boost != null) { item.boost_requested = boost.IsBoostRequested; item.boost_cost_unity = boost.BoostCost.HasValue ? (double?)boost.BoostCost.Value.Value.ToDouble() : null; }
                var electric = entity as IElectricityConsumingEntity;
                item.supports_electricity = electric != null;
                if (electric != null) {
                    item.power_required_raw = electric.PowerRequired.Value;
                    if (electric.ElectricityConsumer.HasValue) {
                        var c = electric.ElectricityConsumer.Value;
                        item.electricity = new ConsumerStatus { is_enabled = c.IsEnabled, priority = c.Priority,
                            insufficient_supply = c.NotEnoughPower, did_consume_last_tick = c.DidConsumeLastTick,
                            required_raw = c.PowerRequired.Value, charged_raw = c.PowerCharged.Value, is_surplus_consumer = c.IsSurplusConsumer };
                    }
                }
                var computing = entity as IComputingConsumingEntity;
                item.supports_computing = computing != null;
                if (computing != null) {
                    item.computing_required_raw = computing.ComputingRequired.Value;
                    if (computing.ComputingConsumer.HasValue) {
                        var c = computing.ComputingConsumer.Value;
                        item.computing = new ConsumerStatus { is_enabled = c.IsEnabled, priority = c.Priority,
                            insufficient_supply = c.NotEnoughComputing, did_consume_last_tick = c.DidConsumeLastTick,
                            required_raw = c.ComputingRequired.Value, charged_raw = c.ComputingCharged.Value };
                    }
                }
                var maintained = entity as IMaintainedEntity;
                item.supports_maintenance = maintained != null;
                if (maintained != null && maintained.Maintenance != null) {
                    var provider = maintained.Maintenance;
                    var status = provider.Status;
                    var cost = provider.Costs;
                    item.maintenance = new MaintenanceState { is_broken = status.IsBroken, is_idle = maintained.IsIdleForMaintenance,
                        points_current = status.MaintenancePointsCurrent.Value.ToDouble(), points_max = status.MaintenancePointsMax.Value.ToDouble(),
                        breakdown_chance_ratio = status.CurrentBreakdownChance.ToDouble(), broken_duration_days = status.BrokenDurationDays.ToDouble(),
                        quick_repair_cost_unity = provider.QuickRepairCost.HasValue ? (double?)provider.QuickRepairCost.Value.Value.ToDouble() : null,
                        product_id = cost.Product == null ? null : cost.Product.Id.ToString(),
                        per_month = cost.MaintenancePerMonth.Value.ToDouble(), max_per_month = cost.MaxMaintenancePerMonth.Value.ToDouble() };
                }
                var generator = entity as IElectricityGeneratingEntity;
                if (generator != null && generator.ElectricityGenerator != null) {
                    var g = generator.ElectricityGenerator;
                    item.electricity_generation = new GeneratorStatus { priority = g.GenerationPriority, is_surplus_generator = g.IsSurplusGenerator,
                        max_capacity_raw = g.MaxGenerationCapacity.Value, capacity_this_tick_raw = g.GenerationCapacityThisTick.Value,
                        generated_this_tick_raw = g.GeneratedThisTick.Value };
                }
                var logistics = entity as IEntityWithLogisticsControl;
                if (logistics != null) item.logistics = new LogisticsState { can_disable_input = logistics.CanDisableLogisticsInput,
                    can_disable_output = logistics.CanDisableLogisticsOutput, input_mode = logistics.LogisticsInputMode.ToString(), output_mode = logistics.LogisticsOutputMode.ToString() };
                var simple = entity as IEntityWithSimpleLogisticsControl;
                if (simple != null) item.simple_logistics = new SimpleLogisticsState { input_control = simple.LogisticsInputControl.ToString(),
                    output_control = simple.LogisticsOutputControl.ToString(), input_disabled = simple.IsLogisticsInputDisabled, output_disabled = simple.IsLogisticsOutputDisabled };
                var input = entity as IEntityAssignedAsOutput;
                if (input != null) {
                    item.assigned_input_entity_ids = new List<int>();
                    foreach (var source in input.AssignedInputs) item.assigned_input_entity_ids.Add(source.Id.Value);
                    item.assigned_input_entity_ids.Sort();
                }
                var output = entity as IEntityAssignedAsInput;
                if (output != null) {
                    item.allow_non_assigned_output = output.AllowNonAssignedOutput;
                    item.assigned_output_entity_ids = new List<int>();
                    foreach (var target in output.AssignedOutputs) item.assigned_output_entity_ids.Add(target.Id.Value);
                    item.assigned_output_entity_ids.Sort();
                }
                result.entities.Add(item);
            }
            result.entities.Sort((a, b) => a.entity_id.CompareTo(b.entity_id));
            return result;
        }
    }
}
