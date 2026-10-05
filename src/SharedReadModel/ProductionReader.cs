using System.Collections.Generic;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Products;
using Mafi.Core.Buildings.Storages;

#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    public static class ProductionReader
    {
        public static ProductionSnapshot Capture(EntitiesManager entities, string id, string time, out RecipeCatalog catalog)
        {
            var result = new ProductionSnapshot {
                schema_version = 1, snapshot_id = id, captured_at_utc = time,
                machines = new List<MachineState>(), storages = new List<StorageState>(),
                limitations = new[] { "Machine/StorageBase instances only; other production building classes are not covered.", "Buffers are queried by products in all recipes of that machine prototype; unrelated or stale buffers may be absent.", "Last recipe is historical and may not currently be running; CurrentState is exported separately.", "Quantities are game Quantity units. Ratios use 1.0=100%. Recipe quantities are base definitions, with binding multiplier separate.", "Port connections are in ports.json. Storage thresholds/settings are exported where supported; shared status is in entity-status.json. Research and global utility statistics are not exported yet." }
            };
            catalog = new RecipeCatalog { schema_version = 1, snapshot_id = id, captured_at_utc = time,
                scope = "Recipe bindings of machine prototypes present in this snapshot, not the entire game catalogue.", bindings = new List<RecipeDefinition>() };
            var seenPrototypes = new HashSet<string>();
            foreach (var entity in entities.Entities)
            {
                var storage = entity as StorageBase;
                if (storage != null) {
                    var storageState = new StorageState {
                    entity_id = entity.Id.Value, product_id = storage.StoredProduct.HasValue ? storage.StoredProduct.Value.Id.ToString() : null,
                    quantity = storage.CurrentQuantity.Value, capacity = storage.Capacity.Value,
                    logistics_input_disabled = storage.IsLogisticsInputDisabled, logistics_output_disabled = storage.IsLogisticsOutputDisabled,
                    is_full = storage.IsFull, is_empty = storage.IsEmpty, fill_ratio = storage.PercentFull.ToDouble(),
                    logistics_input_control = storage.LogisticsInputControl.ToString(), logistics_output_control = storage.LogisticsOutputControl.ToString(),
                    linked_station_disables_input = storage.LinkedStationDisablesLogisticsInput,
                    linked_station_disables_output = storage.LinkedStationDisablesLogisticsOutput,
                    connected_storage_ids = new List<int>()
                    };
                    foreach (var connected in storage.GetConnectedStorages()) storageState.connected_storage_ids.Add(connected.Id.Value);
                    storageState.connected_storage_ids.Sort();
                    var configured = storage as Storage;
                    if (configured != null) {
                        var settings = new StorageSettings {
                            alerts_available = configured.AreAlertsAvailable,
                            alert_above_enabled = configured.AlertWhenAboveEnabled, alert_above_ratio = configured.AlertWhenAbove.ToDouble(),
                            alert_below_enabled = configured.AlertWhenBelowEnabled, alert_below_ratio = configured.AlertWhenBelow.ToDouble(),
                            import_until_ratio = configured.ImportUntilPercent.ToDouble(), export_from_ratio = configured.ExportFromPercent.ToDouble(),
                            transport_from_ratio = configured.TransportFromPercent.ToDouble(), transport_until_ratio = configured.TransportUntilPercent.ToDouble(),
                            import_priority = configured.ImportPriority, export_priority = configured.ExportPriority,
                            usable_capacity = configured.UsableCapacity.Value, cleaning_in_progress = configured.CleaningInProgress,
                            only_assigned_vehicles_allowed = configured.AreOnlyAssignedVehiclesAllowed, zone_mask = configured.ZoneMask,
                            assigned_vehicle_ids = new List<int>(), allowed_truck_group_ids = new List<string>()
                        };
                        foreach (var vehicle in configured.AllVehicles) settings.assigned_vehicle_ids.Add(vehicle.Id.Value);
                        foreach (var group in configured.AllowedTruckGroups) settings.allowed_truck_group_ids.Add(group.Id.ToString());
                        settings.assigned_vehicle_ids.Sort(); settings.allowed_truck_group_ids.Sort(System.StringComparer.Ordinal);
                        storageState.settings = settings;
                    }
                    result.storages.Add(storageState);
                }
                var machine = entity as Machine;
                if (machine == null) continue;
                var state = new MachineState { entity_id = entity.Id.Value, state = machine.CurrentState.ToString(),
                    worked_this_tick = machine.WorkedThisTick, progress_ratio = machine.ProgressPerc.ToDouble(),
                    utilization_ratio = machine.Utilization.ToDouble(), assigned_recipe_ids = new List<string>(),
                    last_recipe_id = machine.LastRecipeInProgress.HasValue ? machine.LastRecipeInProgress.Value.Id.ToString() : null,
                    is_boosted = machine.IsBoosted, speed_ratio = machine.SpeedFactor.ToDouble(),
                    duration_multiplier = machine.DurationMultiplier.ToDouble(), virtual_output_multiplier = machine.VirtualOutputMultiplier.ToDouble(),
                    recipe_production_ticks = machine.RecipeProductionTicks.Ticks,
                    monthly_unity_consumed = machine.MonthlyUnityConsumed.Value.ToDouble(),
                    max_monthly_unity_consumed = machine.MaxMonthlyUnityConsumed.Value.ToDouble(),
                    buffers = new List<BufferState>() };
                foreach (var recipe in machine.RecipesAssigned) state.assigned_recipe_ids.Add(recipe.Id.ToString());
                var inputs = new Dictionary<string, ProductProto>();
                var outputs = new Dictionary<string, ProductProto>();
                foreach (var recipe in machine.Prototype.Recipes)
                {
                    foreach (var input in recipe.AllInputs) inputs[input.Product.Id.ToString()] = input.Product;
                    foreach (var output in recipe.AllOutputs) outputs[output.Product.Id.ToString()] = output.Product;
                }
                foreach (var pair in inputs) state.buffers.Add(new BufferState { product_id = pair.Key, direction = "input",
                    quantity = machine.GetInputQuantityFor(pair.Value).Value, capacity = machine.GetInputCapacityFor(pair.Value).Value });
                foreach (var pair in outputs) state.buffers.Add(new BufferState { product_id = pair.Key, direction = "output",
                    quantity = machine.GetOutputQuantityFor(pair.Value).Value, capacity = machine.GetOutputCapacityFor(pair.Value).Value });
                result.machines.Add(state);
                if (!seenPrototypes.Add(machine.Prototype.Id.ToString())) continue;
                foreach (var binding in machine.Prototype.RecipeBindings)
                {
                    var definition = new RecipeDefinition { machine_prototype_id = machine.Prototype.Id.ToString(),
                        recipe_id = binding.Recipe.Id.ToString(), duration_ticks = binding.Duration.Ticks, multiplier = binding.Multiplier,
                        inputs = new List<RecipeAmount>(), outputs = new List<RecipeAmount>() };
                    foreach (var input in binding.Recipe.AllInputs) definition.inputs.Add(new RecipeAmount { product_id = input.Product.Id.ToString(), quantity = input.Quantity.Value });
                    foreach (var output in binding.Recipe.AllOutputs) definition.outputs.Add(new RecipeAmount { product_id = output.Product.Id.ToString(), quantity = output.Quantity.Value });
                    catalog.bindings.Add(definition);
                }
            }
            result.machines.Sort((a,b) => a.entity_id.CompareTo(b.entity_id));
            result.storages.Sort((a,b) => a.entity_id.CompareTo(b.entity_id));
            return result;
        }
    }
}
