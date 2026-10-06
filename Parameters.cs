using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocaspawner
{
    internal static class Parameters
    {
        private static Func<int, float> _range;
        private static DateTime _resolveAfter;
        private static readonly Dictionary<string, GameObject> HeldWeapons = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        internal static void Invalidate() { HeldWeapons.Clear(); }
        internal static List<SpawnParameter> Read(Entry entry)
        {
            var result = new List<SpawnParameter>(); var go = entry?.Prefab;
            if (go == null) return result;
            result.Add(new SpawnParameter(entry.IsNpc ? "Faction" : "Category", Catalog.DisplayGroup(entry), section: "Overview"));
            var fsms = go.GetComponents<PlayMakerFSM>().Where(f => f != null).ToArray();
            var health = Float(fsms, "Health", "Health");
            if (health != null) AddNumber(result, "Health", health.Value, "", "Overview");
            var rb = go.GetComponent<Rigidbody>(); if (rb != null && !entry.IsNpc) AddNumber(result, "Mass", rb.mass, "kg", "Overview");
            // No guessed armor tiers: vanilla actors have no armor variable. A mod can expose a genuine value or add a provider.
            foreach (var f in fsms)
            {
                var armor = f.FsmVariables.FloatVariables?.FirstOrDefault(v => string.Equals(v.Name, "armor", StringComparison.OrdinalIgnoreCase));
                if (armor != null) { AddNumber(result, "Armor", armor.Value, "", "Protection"); break; }
                var armorInt = f.FsmVariables.IntVariables?.FirstOrDefault(v => string.Equals(v.Name, "armor", StringComparison.OrdinalIgnoreCase));
                if (armorInt != null) { AddNumber(result, "Armor", armorInt.Value, "", "Protection"); break; }
            }
            if (entry.IsNpc)
            {
                var damage = Float(fsms, "Damage Ranged", "Damage Value");
                if (damage != null) AddNumber(result, "Ranged damage", Mathf.Abs(damage.Value), "", "Combat");
                var melee = HitDamage(fsms.FirstOrDefault(f => f.FsmName == "Damage"));
                if (melee.HasValue) AddNumber(result, "Melee damage", Mathf.Abs(melee.Value), "", "Combat");
            }
            else
            {
                if (entry.Group == "Weapons")
                {
                    var damage = WeaponDamage(go, new HashSet<int>(), 0);
                    if (damage == null)
                    {
                        var held = HeldWeapon(go, fsms);
                        if (held != null) damage = WeaponDamage(held, new HashSet<int>(), 0);
                    }
                    if (damage != null) AddNumber(result, damage.Explosion ? "Explosion damage (max)" : "Damage", damage.Value, "", "Combat");
                }
                AddVariable(result, fsms, "Condition", new[] { "condition", "Condition" }, "Condition");
                AddVariable(result, fsms, "Fuel", new[] { "fuel", "Fuel" }, "Condition");
                AddVariable(result, fsms, "Capacity", new[] { "capacity", "Capacity" }, "Details");
            }
            string weapon = WeaponName(go, entry.IsNpc, fsms);
            if (weapon != null)
            {
                if (entry.IsNpc) result.Add(new SpawnParameter("Weapon", Friendly(weapon), section: "Combat"));
                int kind = Classify(weapon);
                if (!entry.IsNpc)
                {
                    var held = HeldWeapon(go, fsms);
                    var attack = held?.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Attack");
                    if (attack?.Fsm != null && attack.Fsm.Initialized)
                    {
                        var hit = attack.Fsm.GetState("hit");
                        bool pellets = (hit?.Actions ?? new FsmStateAction[0]).OfType<IntCompare>().Any(a => a.integer1?.Name == "pellets" && a.integer2 != null);
                        if (pellets) kind = 4; else if (kind == 4) kind = 2; // Gunplay treats a Rochester without a pellet loop as a rifle.
                    }
                }
                if (TryRange(kind, out var range)) AddNumber(result, "Effective range", range, "m", "Gunplay", "Gunplay");
            }
            foreach (var pair in Api.Providers.ToArray())
                try
                {
                    var rows = pair.Value(go, entry.IsNpc); if (rows == null) continue;
                    foreach (var row in rows) if (row != null && !string.IsNullOrWhiteSpace(row.Label) && !string.IsNullOrWhiteSpace(row.Value))
                        result.Add(new SpawnParameter(row.Label, row.Value, row.Unit ?? "", row.Section ?? "Details", string.IsNullOrEmpty(row.Source) ? pair.Key : row.Source));
                }
                catch (Exception e) { SpawnerPlugin.Log.LogDebug("Parameter provider " + pair.Key + ": " + e.Message); }
            return result;
        }
        private static FsmFloat Float(PlayMakerFSM[] fsms, string fsm, string variable)
        { return fsms.FirstOrDefault(f => f.FsmName == fsm)?.FsmVariables.FindFsmFloat(variable); }
        private static void AddNumber(List<SpawnParameter> rows, string label, float value, string unit, string section, string source = "")
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value)) rows.Add(new SpawnParameter(label, value.ToString("0.##", CultureInfo.InvariantCulture), unit, section, source));
        }
        private static void AddVariable(List<SpawnParameter> rows, PlayMakerFSM[] fsms, string label, string[] names, string section)
        {
            foreach (var f in fsms) foreach (var name in names)
            {
                var value = f.FsmVariables.FindFsmFloat(name); if (value == null) continue;
                AddNumber(rows, label, value.Value, "", section); return;
            }
        }
        private static float? HitDamage(PlayMakerFSM f)
        {
            foreach (var action in Actions(f))
            {
                var set = action as SetFsmFloat;
                if (set?.fsmName?.Value == "Bodypart" && set.variableName?.Value == "Damage" && set.setValue != null && !set.setValue.IsNone) return set.setValue.Value;
            }
            return null;
        }
        private static FsmStateAction[] Actions(PlayMakerFSM f)
        {
            if (f?.Fsm == null) return new FsmStateAction[0];
            try
            {
                // Load serialized action data only; do not start the FSM or run Awake/OnEnter.
                if (!f.Fsm.Initialized) f.Fsm.InitData();
                return (f.FsmStates ?? new FsmState[0]).SelectMany(s => s.Actions ?? new FsmStateAction[0]).ToArray();
            }
            catch (Exception e) { SpawnerPlugin.Log.LogDebug("Weapon data " + f.name + ": " + e.Message); return new FsmStateAction[0]; }
        }
        private sealed class DamageInfo { internal float Value; internal bool Explosion; }
        private static DamageInfo WeaponDamage(GameObject go, HashSet<int> visited, int depth)
        {
            if (go == null || depth > 3 || !visited.Add(go.GetInstanceID())) return null;
            DamageInfo best = null;
            foreach (var f in go.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var actions = Actions(f);
                foreach (var hit in actions.OfType<SetFsmFloat>().Where(a => a.fsmName?.Value == "Bodypart" && a.variableName?.Value == "Damage" && a.setValue != null && !a.setValue.IsNone))
                {
                    float value = Mathf.Abs(hit.setValue.Value); bool explosion = false;
                    if (hit.setValue.UseVariable && f.FsmName == "ExplosionDamage")
                    {
                        var remap = actions.OfType<FloatRemap>().FirstOrDefault(a => a.storeResult?.Name == hit.setValue.Name);
                        if (remap?.targetStart != null && remap.targetEnd != null)
                        { value = Mathf.Max(Mathf.Abs(remap.targetStart.Value), Mathf.Abs(remap.targetEnd.Value)); explosion = true; }
                    }
                    if (value > 0 && !float.IsNaN(value) && !float.IsInfinity(value) && (best == null || value > best.Value)) best = new DamageInfo { Value = value, Explosion = explosion };
                }
                foreach (var create in actions.OfType<CreateObject>())
                {
                    var child = WeaponDamage(create.gameObject?.Value, visited, depth + 1);
                    if (child != null && (best == null || child.Value > best.Value)) best = child;
                }
            }
            return best;
        }
        internal static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }
        internal static string WeaponName(GameObject go, bool npc, PlayMakerFSM[] fsms)
        {
            if (!npc)
            {
                // Loose item prefabs are separate from the first-person firing model. weaponType links the two, e.g. 9mm_borz_smg -> borz_smg.
                var held = HeldWeapon(go, fsms);
                return held != null && held.GetComponents<PlayMakerFSM>().Any(f => f.FsmName == "Reload") ? held.name : null;
            }
            if (!fsms.Any(f => f.FsmName == "Damage Ranged")) return null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "fire_effect" && t.parent != null && t.parent.name != "fire_effect" && ActiveUnder(t.parent, go.transform)) return t.parent.name;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("crossbow", StringComparison.OrdinalIgnoreCase) && t.GetComponent<Renderer>() != null && ActiveUnder(t, go.transform)) return t.name;
            return null;
        }
        private static GameObject HeldWeapon(GameObject go, PlayMakerFSM[] fsms)
        {
            if (fsms.Any(f => f.FsmName == "Attack")) return go;
            string type = fsms.FirstOrDefault(f => f.FsmName == "weaponType")?.FsmVariables.FindFsmString("weaponType")?.Value;
            if (string.IsNullOrEmpty(type)) return null;
            if (HeldWeapons.TryGetValue(type, out var cached) && cached != null) return cached;
            string key = WeaponKey(type);
            var held = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().FirstOrDefault(f => f != null && f.FsmName == "Attack" && WeaponKey(f.gameObject.name) == key && IsHeldModel(f.transform))?.gameObject;
            if (held != null) HeldWeapons[type] = held;
            return held;
        }
        private static string WeaponKey(string name) => new string(Catalog.BaseName(name).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        private static bool IsHeldModel(Transform transform)
        {
            for (var t = transform.parent; t != null; t = t.parent) if (t.name == "WeaponsArm") return true;
            return false;
        }
        internal static int Classify(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("crossbow")) return 5;
            if (n.Contains("shotgun") || n.Contains("slamfire") || n.Contains("slamberg") || n.Contains("rochester")) return 4;
            if (n.Contains("scoped") || n.Contains("sniper") || n.Contains("redmark")) return 3;
            if (n.Contains("smg") || n.Contains("borz")) return 1;
            if (n.Contains("pistol") || n.Contains("revolver") || n.Contains("folk_17")) return 0;
            return 2;
        }
        private static string Friendly(string name) { return Catalog.BaseName(name).Replace('_', ' ').Replace(" model", "").Trim(); }
        internal static bool TryRange(int kind, out float value)
        {
            value = 0;
            if (_range == null && DateTime.UtcNow >= _resolveAfter)
            {
                _resolveAfter = DateTime.UtcNow.AddSeconds(5);
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue("com.denis.apocalypter.gunplay", out var plugin) && !ReferenceEquals(plugin.Instance, null))
                    {
                        var api = plugin.Instance.GetType().Assembly.GetType("Gunplay.Api");
                        var version = api?.GetField("ContractVersion", BindingFlags.Static | BindingFlags.Public);
                        var method = api?.GetMethod("EffectiveRange", new[] { typeof(int) });
                        if (version != null && (int)version.GetValue(null) == 1 && method != null) _range = (Func<int, float>)Delegate.CreateDelegate(typeof(Func<int, float>), method);
                    }
                }
                catch (Exception e) { SpawnerPlugin.Log.LogDebug("Gunplay parameter integration: " + e.Message); }
            }
            if (_range == null) return false;
            try { value = _range(kind); return value > 0 && !float.IsNaN(value) && !float.IsInfinity(value); }
            catch (Exception e) { SpawnerPlugin.Log.LogDebug("Gunplay range unavailable: " + e.Message); return false; }
        }
    }
}
