using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Apocaspawner
{
    // The source mod owns one interactive, saved radio per session. Retrieve it through its own physics path.
    internal static class PortableRadioBridge
    {
        internal const string Guid = "com.fived.apocalypter.fallout_nv_radio";
        internal const string Key = "portable-radio:retrieve";
        private static bool _resolved, _ok;
        private static Type _managerType;
        private static FieldInfo _radio, _session, _pending;
        private static PropertyInfo _physics, _inHand;
        private static MethodInfo _restore, _clearHover;
        private static object _none;
        private static string _label;

        private static T Need<T>(T member, string name) where T : class
        { if (member == null) throw new MissingMemberException(name); return member; }

        private static bool Resolve()
        {
            if (_resolved) return _ok;
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out info)) return false;
            _resolved = true;
            try
            {
                var asm = !ReferenceEquals(info.Instance, null) ? info.Instance.GetType().Assembly : ModNames.AssemblyOf(info);
                Need(asm, "radio assembly");
                _label = ModNames.Label(info);
                const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _managerType = Need(asm.GetType("ApocFalloutNVRadio.RadioManager"), "RadioManager");
                var radioType = Need(asm.GetType("ApocFalloutNVRadio.RadioObject"), "RadioObject");
                var physicsType = Need(asm.GetType("ApocFalloutNVRadio.RadioPhysics"), "RadioPhysics");
                _radio = Need(_managerType.GetField("_radio", I), "RadioManager._radio");
                _session = Need(_managerType.GetField("_sessionActive", I), "RadioManager._sessionActive");
                _pending = Need(_managerType.GetField("_pending", I), "RadioManager._pending");
                _clearHover = Need(_managerType.GetMethod("ClearHover", I, null, Type.EmptyTypes, null), "RadioManager.ClearHover");
                _physics = Need(radioType.GetProperty("Phys", I), "RadioObject.Phys");
                _inHand = Need(physicsType.GetProperty("InHand", I), "RadioPhysics.InHand");
                _restore = Need(physicsType.GetMethod("RestoreWorld", I, null, new[] { typeof(Vector3), typeof(Quaternion) }, null), "RadioPhysics.RestoreWorld");
                if (_radio.FieldType != radioType || _session.FieldType != typeof(bool) || !_pending.FieldType.IsEnum
                    || _physics.PropertyType != physicsType || !_physics.CanRead || _inHand.PropertyType != typeof(bool) || !_inHand.CanRead
                    || _restore.ReturnType != typeof(void) || _clearHover.ReturnType != typeof(void) || !typeof(Component).IsAssignableFrom(_managerType)
                    || !typeof(Component).IsAssignableFrom(radioType)) throw new MissingMemberException("radio member signatures changed");
                _none = Enum.Parse(_pending.FieldType, "None");
                SpawnerPlugin.Log.LogInfo("Portable Radio compatibility ready: retrieve the existing per-save radio under ITEMS / Gear.");
                return _ok = true;
            }
            catch (Exception e)
            {
                SpawnerPlugin.Log.LogWarning("Portable Radio integration unavailable: " + e.Message);
                return false;
            }
        }

        private static Component Current(out Component manager)
        {
            manager = null;
            if (!Resolve()) return null;
            foreach (var source in Resources.FindObjectsOfTypeAll(_managerType))
            {
                var candidate = source as Component;
                if (candidate == null || !candidate.gameObject.activeInHierarchy || !(bool)_session.GetValue(candidate)) continue;
                var radio = _radio.GetValue(candidate) as Component;
                if (radio == null || !radio.gameObject.scene.IsValid() || !radio.gameObject.scene.isLoaded) continue;
                manager = candidate; return radio;
            }
            return null;
        }

        internal static void AddEntries(Dictionary<string, Entry> entries)
        {
            try
            {
                Component manager;
                var radio = Current(out manager);
                if (radio != null) entries[Key] = new Entry { Key = Key, Display = "Portable Radio (retrieve)", Category = "portable-radio", Prefab = radio.gameObject, Mod = _label };
            }
            catch (Exception e) { SpawnerPlugin.Log.LogWarning("Portable Radio catalog: " + e.Message); }
        }

        internal static void Retrieve(Vector3 candidate, Quaternion rotation, int count)
        {
            if (count != 1) throw new InvalidOperationException("Portable Radio supports one radio per save. Set quantity to 1.");
            Component manager;
            var radio = Current(out manager);
            if (radio == null) throw new InvalidOperationException("The radio has not initialized for this save. Reopen F4 after it appears.");
            var physics = _physics.GetValue(radio, null) as Component;
            if (physics == null) throw new InvalidOperationException("The radio physics is not ready.");
            if ((bool)_inHand.GetValue(physics, null)) throw new InvalidOperationException("Drop the radio before retrieving it.");
            Vector3 ground;
            if (!Placement.Ground(candidate, out ground)) throw new InvalidOperationException("No loaded ground at the spawn location. Choose a shorter distance or a different direction.");
            _clearHover.Invoke(manager, null);
            _restore.Invoke(physics, new object[] { ground + Vector3.up * .002f, rotation });
            radio.gameObject.SetActive(true);
            _pending.SetValue(manager, _none);
            SpawnerPlugin.Log.LogInfo("Portable Radio: retrieved existing radio at " + radio.transform.position + "; original controls and save tracking retained.");
        }
    }
}
