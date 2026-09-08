using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    /// <summary>
    /// The read methods of the public inspection facade. Every method has a total contract: it
    /// returns a result for any input and throws in no case. A framework that is down yields
    /// <c>initialized = false</c>, a missing manager yields <c>hasManager = false</c>, and an
    /// unresolved store name yields <c>found = false</c>.
    ///
    /// <para><b>Main thread only.</b></para>
    /// </summary>
    public static partial class MosaicInspector
    {
        // ── Services and stores ───────────────────────────────────────────────

        /// <summary>Every registered service, sorted ordinal by the key type full name.</summary>
        public static ServiceListResult GetServices()
        {
            var result = new ServiceListResult();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            try
            {
                foreach (var pair in SortedServices())
                {
                    result.services.Add(new ServiceInfo
                    {
                        typeName = pair.Key != null ? pair.Key.FullName : null,
                        implTypeName = pair.Value != null ? pair.Value.GetType().FullName : null,
                        isStore = pair.Value is INotifyBindablePropertyChanged
                    });
                }
            }
            catch { /* play-exit race: return the partial result */ }

            return result;
        }

        /// <summary>
        /// One store by name. Resolution order: exact <c>Type.FullName</c>, exact <c>Type.Name</c>,
        /// then case-insensitive <c>Type.Name</c>. The first match in the sorted service snapshot
        /// wins, so the answer is stable rather than arbitrary when two types share a name.
        /// </summary>
        public static StoreInfo GetStore(string typeName)
        {
            var result = new StoreInfo();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            if (string.IsNullOrEmpty(typeName))
                return result;

            try
            {
                var services = SortedServices();

                var match = FindByFullName(services, typeName)
                            ?? FindByShortName(services, typeName, StringComparison.Ordinal)
                            ?? FindByShortName(services, typeName, StringComparison.OrdinalIgnoreCase);

                if (match == null)
                    return result;

                return DescribeStore(match.Key, match.Value);
            }
            catch { /* play-exit race: return the partial result */ }

            return result;
        }

        /// <summary>Every registered store, sorted ordinal by the key type full name.</summary>
        public static StoreListResult GetStores()
        {
            var result = new StoreListResult();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            try
            {
                foreach (var pair in SortedServices())
                {
                    if (pair.Value is INotifyBindablePropertyChanged)
                        result.stores.Add(DescribeStore(pair.Key, pair.Value));
                }
            }
            catch { /* play-exit race: return the partial result */ }

            return result;
        }

        // ── Composition ───────────────────────────────────────────────────────

        /// <summary>
        /// The live composition of <see cref="MosaicUIManager.Instance"/>. Returns
        /// <c>hasManager = false</c> when no manager exists or the manager is destroyed. Each
        /// collection is read under its own guard, so one failing read cannot lose the others.
        /// </summary>
        public static CompositionResult GetComposition()
        {
            var result = new CompositionResult();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            var manager = MosaicUIManager.Instance;

            // The Unity null check reports a destroyed manager as null.
            if (manager == null)
                return result;

            result.hasManager = true;

            try
            {
                result.currentMode = manager.CurrentMode != null
                    ? manager.CurrentMode.ModeName ?? string.Empty
                    : string.Empty;
            }
            catch { /* play-exit race */ }

            try
            {
                // Stack order, most recent first. Never sorted: the order carries the meaning.
                foreach (var mode in manager.History.Items)
                    result.history.Add(mode != null ? mode.ModeName ?? string.Empty : string.Empty);
            }
            catch { /* play-exit race */ }

            try
            {
                foreach (var slotName in manager.Slots.Keys)
                    result.slots.Add(slotName);
                result.slots.Sort(StringComparer.Ordinal);
            }
            catch { /* play-exit race */ }

            try
            {
                foreach (var pair in manager.ActivePanels)
                {
                    var definition = pair.Key;
                    var instance = pair.Value;

                    result.panels.Add(new PanelInfo
                    {
                        panelName = definition != null ? definition.PanelName : null,
                        slotName = instance != null ? instance.SlotName : null,
                        sortOrder = instance != null ? instance.SortOrder : 0,
                        isActive = instance != null && instance.IsActive,
                        // Passed through with no validation: a broken binding is the useful diagnostic.
                        controllerTypeName = definition != null ? definition.ControllerTypeName : null
                    });
                }

                result.panels.Sort((a, b) => string.CompareOrdinal(a.panelName, b.panelName));
            }
            catch { /* play-exit race */ }

            try
            {
                foreach (var map in manager.ActiveActionMaps)
                    result.actionMaps.Add(map);
                result.actionMaps.Sort(StringComparer.Ordinal);
            }
            catch { /* play-exit race */ }

            try
            {
                foreach (var pair in manager.ActiveWorldFeatures)
                    result.worldFeatures.Add(WorldObjectName(pair.Key, pair.Value));
                result.worldFeatures.Sort(StringComparer.Ordinal);
            }
            catch { /* play-exit race */ }

            try
            {
                foreach (var pair in manager.ActiveWorldControllers)
                    result.worldControllers.Add(WorldObjectName(pair.Key, pair.Value));
                result.worldControllers.Sort(StringComparer.Ordinal);
            }
            catch { /* play-exit race */ }

            return result;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Every registered command id, sorted ordinal.</summary>
        public static CommandListResult GetCommands()
        {
            var result = new CommandListResult();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            try
            {
                if (MosaicUI.Commands != null)
                {
                    foreach (var id in MosaicUI.Commands.RegisteredIds)
                        result.commands.Add(id);
                    result.commands.Sort(StringComparer.Ordinal);
                }
            }
            catch { /* play-exit race: return the partial result */ }

            return result;
        }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// The newest <paramref name="max"/> recorded events, oldest first. The recorder is
        /// editor-only, so a player build always returns an empty list with
        /// <c>initialized = true</c>. A non-positive <paramref name="max"/> returns an empty list.
        /// </summary>
        public static EventListResult GetRecentEvents(int max)
        {
            var result = new EventListResult();
            if (!MosaicUI.IsInitialized) return result;
            if (MosaicUI.Services == null) return result;
            result.initialized = true;

            if (max <= 0)
                return result;

            try
            {
#if UNITY_EDITOR
                EventRecorder.CopyTo(result.events, max);
#endif
            }
            catch { /* play-exit race: return the partial result */ }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// A deterministic snapshot of the service registry, sorted ordinal by key
        /// <c>Type.FullName</c>. Shared by every service read, so two calls with no state change
        /// produce identical output.
        /// </summary>
        private static List<KeyValuePair<Type, object>> SortedServices()
        {
            var snapshot = new List<KeyValuePair<Type, object>>();

            foreach (var pair in MosaicUI.Services.Entries)
                snapshot.Add(pair);

            snapshot.Sort((a, b) => string.CompareOrdinal(
                a.Key != null ? a.Key.FullName : string.Empty,
                b.Key != null ? b.Key.FullName : string.Empty));

            return snapshot;
        }

        private static ServiceMatch FindByFullName(List<KeyValuePair<Type, object>> services, string name)
        {
            foreach (var pair in services)
            {
                if (pair.Key != null && string.Equals(pair.Key.FullName, name, StringComparison.Ordinal))
                    return new ServiceMatch(pair.Key, pair.Value);
            }
            return null;
        }

        private static ServiceMatch FindByShortName(
            List<KeyValuePair<Type, object>> services, string name, StringComparison comparison)
        {
            foreach (var pair in services)
            {
                if (pair.Key != null && string.Equals(pair.Key.Name, name, comparison))
                    return new ServiceMatch(pair.Key, pair.Value);
            }
            return null;
        }

        /// <summary>Fills a <see cref="StoreInfo"/> from the member cache and the value formatter.</summary>
        private static StoreInfo DescribeStore(Type key, object value)
        {
            var info = new StoreInfo
            {
                initialized = true,
                found = true,
                typeName = key != null ? key.FullName : null
            };

            if (value is IDataSourceViewHashProvider versioned)
            {
                try { info.version = versioned.GetViewHashCode(); }
                catch { info.version = 0; }
            }

            if (value == null)
                return info;

            foreach (var entry in StoreReflectionCache.GetMembers(value.GetType()))
                info.values.Add(InspectionValueFormatter.Read(value, entry));

            return info;
        }

        private static string WorldObjectName(UnityEngine.GameObject prefab, UnityEngine.GameObject instance)
        {
            if (instance != null)
                return instance.name;
            return prefab != null ? prefab.name : string.Empty;
        }

        /// <summary>A resolved service pair. A class, so a failed lookup can return null.</summary>
        private sealed class ServiceMatch
        {
            internal readonly Type Key;
            internal readonly object Value;

            internal ServiceMatch(Type key, object value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
