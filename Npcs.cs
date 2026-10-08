using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaspawner
{
    internal static class NpcCatalog
    {
        internal static readonly string[] Factions = { "Animals", "Mutants", "Coyotes", "Scrapyard", "Other" };
        internal static string Faction(string tag)
        {
            switch ((tag ?? "").ToLowerInvariant())
            {
                case "carnivore": case "herbivore": case "animal": case "animals": return "Animals";
                case "mutant": case "mutants": case "mutantsanity": return "Mutants";
                case "coyotes": case "coyote": return "Coyotes";
                case "scrapyard": return "Scrapyard";
                default: return "Other";
            }
        }
        internal static bool IsNpc(GameObject go, HashSet<string> names)
        {
            if (go == null || go.tag == "Player" || go.name.IndexOf("_Dead", StringComparison.OrdinalIgnoreCase) >= 0 || IsBackup(go.name)) return false;
            return names.Contains("Health") && (names.Contains("Detection") || names.Contains("Movement") || names.Contains("Attack"));
        }
        internal static bool IsBackup(string name) => name.IndexOf("_BACKUP", StringComparison.OrdinalIgnoreCase) >= 0;
        internal static bool NestedActor(GameObject go)
        {
            for (var t = go.transform.parent; t != null; t = t.parent)
            {
                var names = new HashSet<string>(t.GetComponents<PlayMakerFSM>().Where(f => f != null).Select(f => f.FsmName));
                if (IsNpc(t.gameObject, names) || names.Contains("RpmGear") || names.Contains("CrashDamage")) return true;
            }
            return false;
        }
        internal static Vector3 ModelSize(GameObject go)
        {
            bool found = false; var bounds = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
            }
            return found ? bounds.size : Vector3.one;
        }
        internal static void AddRegistered(Dictionary<string, Entry> byKey)
        {
            foreach (var pair in Api.RegisteredNpcs.ToArray())
            {
                var prefab = pair.Value.Prefab;
                if (prefab == null) { Api.RegisteredNpcs.Remove(pair.Key); continue; }
                if (IsBackup(pair.Key)) continue;
                byKey[pair.Key] = new Entry { Key = pair.Key, Display = NameTable.Resolve(pair.Key, pair.Value.Name),
                    Category = pair.Value.Faction, Prefab = prefab, IsAsset = !prefab.scene.IsValid(), IsNpc = true, Mod = pair.Value.Mod };
            }
        }
    }

    /// <summary>A row in the expandable parameter list. Missing values should be omitted by the provider.</summary>
    public sealed class SpawnParameter
    {
        public string Section, Label, Value, Unit, Source;
        public SpawnParameter(string label, string value, string unit = "", string section = "Details", string source = "")
        { Label = label; Value = value; Unit = unit; Section = section; Source = source; }
    }

    /// <summary>Optional integration API; call on Unity's main thread. Providers are read-only.</summary>
    public static class Api
    {
        public const int ContractVersion = 1;
        internal sealed class Npc { internal GameObject Prefab; internal string Name, Faction, Mod; }
        internal static readonly Dictionary<string, Npc> RegisteredNpcs = new Dictionary<string, Npc>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, Func<GameObject, bool, IEnumerable<SpawnParameter>>> Providers = new Dictionary<string, Func<GameObject, bool, IEnumerable<SpawnParameter>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register custom NPCs without vanilla Health/Movement FSMs. Unknown factions appear under Other.</summary>
        public static void RegisterNpc(GameObject prefab, string displayName = null, string faction = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            RegisteredNpcs[Catalog.BaseName(prefab.name)] = new Npc { Prefab = prefab, Name = displayName ?? Catalog.BaseName(prefab.name), Faction = faction ?? prefab.tag, Mod = ModNames.For(System.Reflection.Assembly.GetCallingAssembly()) };
            Catalog.Invalidate();
        }
        public static void UnregisterNpc(string prefabName) { RegisteredNpcs.Remove(prefabName); Catalog.Invalidate(); }
        /// <summary>Register a named NPC conversion. The preview callback receives a script-free visual/skeleton copy; the spawn callback receives an inactive gameplay clone.</summary>
        public static void RegisterNpcReplacement(string name, string basePrefabName, Func<GameObject, bool> convert, string displayName = null, string faction = null, Func<GameObject, bool> convertPreview = null, Action<GameObject> afterSpawn = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Replacement name required", nameof(name));
            if (string.IsNullOrWhiteSpace(basePrefabName)) throw new ArgumentException("Base prefab name required", nameof(basePrefabName));
            if (convert == null) throw new ArgumentNullException(nameof(convert));
            name = Catalog.BaseName(name); basePrefabName = Catalog.BaseName(basePrefabName);
            if (string.Equals(name, basePrefabName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The replacement needs its own name", nameof(name));
            Replacements.Register(new NpcReplacement { Name = name, BaseName = basePrefabName, Display = displayName ?? name,
                Faction = faction, Mod = ModNames.For(System.Reflection.Assembly.GetCallingAssembly()), Convert = convert, ConvertPreview = convertPreview ?? convert, AfterSpawn = afterSpawn });
        }
        public static void UnregisterNpcReplacement(string name) { Replacements.Unregister(name); }
        public static void RegisterParameterProvider(string id, Func<GameObject, bool, IEnumerable<SpawnParameter>> provider)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Provider ID required", nameof(id));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            Providers[id] = provider;
        }
        public static void UnregisterParameterProvider(string id) { Providers.Remove(id); }
    }
}
