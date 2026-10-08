using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace Apocaspawner
{
    // =====================================================================
    //  ApocalypterGarage (third-party, GUID "ApocalypterGarage") integration.
    //  Garage has no prefabs of its own: its cars, engines and parts are vanilla prefabs re-dressed at runtime and tracked by a
    //  sidecar per save. So we read its definition lists straight from its code (reflection, no compile-time reference) and spawn
    //  ONLY through its own functions (Cars.Spawn / Engines.Spawn / Items.Create + Items.Finish). Never run our Register on these:
    //  Garage already names them and adds them to ArrayList_Items/Cars; a second add would duplicate them on load.
    //  Any missing member (Garage update) switches the integration off with one warning.
    // =====================================================================
    internal static class GarageBridge
    {
        public const string GarageGuid = "ApocalypterGarage";
        public const string GroupCars = "Garage Vehicles";
        public const string GroupParts = "Garage Parts";
        public const string GroupEngines = "Garage Engines";
        public const int KindCar = 1, KindItem = 2, KindEngine = 3;
        private const float FinishDelay = 2.5f;   // Garage's own merchant/loot recipe waits 2.5 s before Items.Finish

        internal static ConfigEntry<bool> Show;

        private static bool _resolved, _ok;
        private static IList _carDefs, _itemDefs, _engineDefs;
        private static MethodInfo _carSpawn, _carClearance, _carBasePrefab, _itemCreate, _itemFinish, _engineSpawn, _engineTemplate;
        private static PropertyInfo _enginesReady;
        private static object _kitWhole;
        private static string _version = "";

        internal static void Init(ConfigFile config)
        {
            Show = config.Bind("Spawn", "ApocalypterGarage", true,
                "Show ApocalypterGarage's cars, engines and engine parts in the spawner (only has an effect when that mod is installed).");
            Show.SettingChanged += (s, e) => Catalog.Invalidate();
        }

        internal static bool Installed => Chainloader.PluginInfos.ContainsKey(GarageGuid);
        internal static bool Active => Show != null && Show.Value && Resolve();

        private static bool Resolve()
        {
            if (_resolved) return _ok;
            _resolved = true;
            try
            {
                PluginInfo info;
                if (!Chainloader.PluginInfos.TryGetValue(GarageGuid, out info) || info == null) return _ok = false;
                Assembly asm = info.Instance != null ? info.Instance.GetType().Assembly : null;
                if (asm == null) return _ok = false;
                _version = info.Metadata != null ? info.Metadata.Version.ToString() : "";
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type cars = Need(asm.GetType("ApocalypterGarage.Cars"), "Cars");
                Type items = Need(asm.GetType("ApocalypterGarage.Items"), "Items");
                Type engines = Need(asm.GetType("ApocalypterGarage.Engines"), "Engines");
                Type carDef = Need(asm.GetType("ApocalypterGarage.CarDef"), "CarDef");
                Type engineDef = Need(asm.GetType("ApocalypterGarage.EngineDef"), "EngineDef");
                Type kit = Need(asm.GetType("ApocalypterGarage.CarKit"), "CarKit");
                _carDefs = Need(cars.GetField("Defs", S), "Cars.Defs").GetValue(null) as IList;
                _itemDefs = Need(items.GetField("Defs", S), "Items.Defs").GetValue(null) as IList;
                _engineDefs = Need(engines.GetField("Defs", S), "Engines.Defs").GetValue(null) as IList;
                _carSpawn = Need(cars.GetMethod("Spawn", S, null, new[] { carDef, typeof(Vector3), typeof(Quaternion), kit }, null), "Cars.Spawn");
                _carClearance = Need(cars.GetMethod("Clearance", S, null, new[] { carDef }, null), "Cars.Clearance");
                _carBasePrefab = Need(cars.GetMethod("BasePrefab", S, null, new[] { carDef }, null), "Cars.BasePrefab");
                _itemCreate = Need(items.GetMethod("Create", S, null, new[] { typeof(string), typeof(Vector3), typeof(Quaternion) }, null), "Items.Create");
                _itemFinish = Need(items.GetMethod("Finish", S, null, new[] { typeof(GameObject) }, null), "Items.Finish");
                _engineSpawn = Need(engines.GetMethod("Spawn", S, null, new[] { engineDef, typeof(Vector3), typeof(Quaternion) }, null), "Engines.Spawn");
                _engineTemplate = Need(engines.GetMethod("TemplateOf", S, null, new[] { engineDef }, null), "Engines.TemplateOf");
                _enginesReady = Need(engines.GetProperty("Ready", S), "Engines.Ready");
                _kitWhole = Enum.Parse(kit, "Whole");
                if (_carDefs == null || _itemDefs == null || _engineDefs == null) throw new MissingMemberException("Defs lists are null");
                SpawnerPlugin.Log.LogInfo($"ApocalypterGarage {_version} found: {_carDefs.Count} car(s), {_engineDefs.Count} engine(s), {_itemDefs.Count} part(s) available to spawn");
                return _ok = true;
            }
            catch (Exception e)
            {
                SpawnerPlugin.Log.LogWarning($"ApocalypterGarage {_version} is installed but its spawn functions could not be found ({e.Message}); its cars and parts are not offered.");
                return _ok = false;
            }
        }

        private static T Need<T>(T value, string what) where T : class
        {
            if (value == null) throw new MissingMemberException(what);
            return value;
        }

        private static object Get(object def, string member)
        {
            if (def == null) return null;
            var t = def.GetType();
            const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = t.GetField(member, I); if (f != null) return f.GetValue(def);
            var p = t.GetProperty(member, I); if (p != null) return p.GetValue(def, null);
            return null;
        }
        private static string Str(object def, string member) { try { return Get(def, member) as string; } catch { return null; } }
        private static float Num(object def, string member, float fallback)
        {
            try { var v = Get(def, member); return v is float ? (float)v : fallback; } catch { return fallback; }
        }

        internal static bool IsGarageObject(GameObject go)
        {
            // live objects Garage already dressed (loose engines, re-skinned parts/cars): never offer them as "prefabs"
            return go != null && (go.GetComponent("GarageEngine") != null || go.GetComponent("GarageItem") != null || go.GetComponent("GarageCar") != null);
        }

        // ---- catalog ----
        internal static void AddEntries(Dictionary<string, Entry> byKey)
        {
            if (!Active) return;
            int cars = 0, engines = 0, items = 0;
            foreach (var def in _carDefs)
            {
                try
                {
                    string id = Str(def, "Id"), name = Str(def, "Name"), baseName = Str(def, "Base");
                    if (string.IsNullOrEmpty(id)) continue;
                    var prefab = _carBasePrefab.Invoke(null, new[] { def }) as GameObject;
                    if (prefab == null) continue;   // its base car is not in this game build
                    byKey["garage-car:" + id] = new Entry
                    {
                        Key = "garage-car:" + id, Display = string.IsNullOrEmpty(name) ? id : name, Category = "garage-vehicle",
                        Prefab = prefab, IsAsset = !prefab.scene.IsValid(), IsVehicle = true, GarageDef = def, GarageKind = KindCar, GarageBase = baseName
                    };
                    cars++;
                }
                catch (Exception e) { SpawnerPlugin.Log.LogDebug("Garage car entry: " + e.Message); }
            }
            bool ready = EnginesReady();
            foreach (var def in _engineDefs)
            {
                try
                {
                    string id = Str(def, "Id"), name = Str(def, "Name"), baseName = Str(def, "Base");
                    if (string.IsNullOrEmpty(id) || id.StartsWith("bench_", StringComparison.OrdinalIgnoreCase)) continue;   // Garage self-test engines
                    GameObject prefab = ready ? _engineTemplate.Invoke(null, new[] { def }) as GameObject : null;
                    Entry baseEntry;
                    if (prefab == null && baseName != null && byKey.TryGetValue(baseName, out baseEntry)) prefab = baseEntry.Prefab;
                    if (prefab == null) continue;
                    byKey["garage-engine:" + id] = new Entry
                    {
                        Key = "garage-engine:" + id, Display = string.IsNullOrEmpty(name) ? id : name, Category = "engine",
                        Prefab = prefab, IsAsset = true, GarageDef = def, GarageKind = KindEngine, GarageBase = baseName
                    };
                    engines++;
                }
                catch (Exception e) { SpawnerPlugin.Log.LogDebug("Garage engine entry: " + e.Message); }
            }
            foreach (var def in _itemDefs)
            {
                try
                {
                    string id = Str(def, "Id"), baseName = Str(def, "Base");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(baseName)) continue;
                    Entry baseEntry;
                    if (!byKey.TryGetValue(baseName, out baseEntry) || baseEntry.Prefab == null) continue;   // base item missing: Items.Create would fail
                    byKey["garage-part:" + id] = new Entry
                    {
                        Key = "garage-part:" + id, Display = ItemDisplay(id, Str(def, "Shown")), Category = baseEntry.Category,
                        Prefab = baseEntry.Prefab, IsAsset = baseEntry.IsAsset, GarageDef = def, GarageKind = KindItem, GarageBase = baseName
                    };
                    items++;
                }
                catch (Exception e) { SpawnerPlugin.Log.LogDebug("Garage part entry: " + e.Message); }
            }
            SpawnerPlugin.Log.LogInfo($"ApocalypterGarage: {cars} car(s), {engines} engine(s), {items} part(s) added to the spawner list" + (ready ? "" : " (engine templates not ready yet)"));
        }

        // "Intercooler - with a turbo: Power x1.08, Heat x0.9" -> "Intercooler"; no Shown -> "oil_cooler_twin" -> "Oil Cooler Twin"
        private static string ItemDisplay(string id, string shown)
        {
            if (!string.IsNullOrEmpty(shown))
            {
                int cut = shown.IndexOf(" - ", StringComparison.Ordinal);
                return (cut > 0 ? shown.Substring(0, cut) : shown).Trim();
            }
            var words = id.Replace('-', '_').Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)).ToArray());
        }

        internal static string GroupFor(Entry e)
        {
            return e.GarageKind == KindCar ? GroupCars : e.GarageKind == KindEngine ? GroupEngines : GroupParts;
        }

        private static bool EnginesReady()
        {
            try { return (bool)_enginesReady.GetValue(null, null); } catch { return false; }
        }

        // ---- parameters panel ----
        internal static List<SpawnParameter> Parameters(Entry e)
        {
            var rows = new List<SpawnParameter>();
            const string src = "ApocalypterGarage";
            rows.Add(new SpawnParameter("Category", Catalog.DisplayGroup(e), section: "Overview", source: src));
            if (!string.IsNullOrEmpty(e.GarageBase)) rows.Add(new SpawnParameter(e.GarageKind == KindCar ? "Built on" : "Based on", e.GarageBase, section: "Overview", source: src));
            try
            {
                if (e.GarageKind == KindCar)
                {
                    string engine = Str(e.GarageDef, "Engine");
                    if (!string.IsNullOrEmpty(engine)) rows.Add(new SpawnParameter("Engine", engine, section: "Overview", source: src));
                    float mass = Num(e.GarageDef, "Mass", -1);
                    if (mass > 0) rows.Add(new SpawnParameter("Mass", mass.ToString("0", CultureInfo.InvariantCulture), "kg", "Overview", src));
                    rows.Add(new SpawnParameter("Spawns", "Whole: wheels, seats, lamps and its engine", section: "Details", source: src));
                }
                else if (e.GarageKind == KindEngine)
                {
                    var stats = Get(e.GarageDef, "Stats") as IDictionary;
                    if (stats != null)
                        foreach (DictionaryEntry kv in stats)
                        {
                            float value = Num(kv.Value, "Value", float.NaN); bool relative = Get(kv.Value, "Relative") is bool && (bool)Get(kv.Value, "Relative");
                            if (float.IsNaN(value)) continue;
                            rows.Add(new SpawnParameter(kv.Key.ToString(), (relative ? "x" : "") + value.ToString("0.##", CultureInfo.InvariantCulture), section: "Engine", source: src));
                        }
                }
                else
                {
                    string shown = Str(e.GarageDef, "Shown");
                    if (!string.IsNullOrEmpty(shown)) rows.Add(new SpawnParameter("Description", shown, section: "Details", source: src));
                    float worth = Num(e.GarageDef, "Worth", -1);
                    if (worth >= 0) rows.Add(new SpawnParameter("Value", worth.ToString("0", CultureInfo.InvariantCulture), section: "Details", source: src));
                }
            }
            catch (Exception ex) { SpawnerPlugin.Log.LogDebug("Garage parameters: " + ex.Message); }
            return rows;
        }

        // ---- spawning ----
        internal static Vector3 Place(Entry e, Vector3 candidate)
        {
            if (e.GarageKind != KindCar) return candidate + Vector3.up * 1.2f;
            Vector3 ground;
            if (!Placement.Ground(candidate, out ground)) throw new InvalidOperationException("No loaded ground at the spawn location. Choose a shorter distance or a different direction.");
            float clearance = 0.8f;
            try { clearance = (float)_carClearance.Invoke(null, new[] { e.GarageDef }); } catch { }
            return ground + Vector3.up * clearance;   // exactly what Garage's own car key does
        }

        internal static GameObject Spawn(Entry e, Vector3 position, Quaternion rotation)
        {
            if (!Resolve()) throw new InvalidOperationException("ApocalypterGarage is not available.");
            GameObject go;
            switch (e.GarageKind)
            {
                case KindCar:
                    if (!EnginesReady()) throw new InvalidOperationException("ApocalypterGarage is still preparing its engines; try again in a moment.");
                    go = Call(_carSpawn, e.GarageDef, position, rotation, _kitWhole);
                    break;
                case KindEngine:
                    if (!EnginesReady()) throw new InvalidOperationException("ApocalypterGarage is still preparing its engines; try again in a moment.");
                    go = Call(_engineSpawn, e.GarageDef, position, rotation);
                    if (go != null) Finisher.Queue(go, _itemFinish, FinishDelay);
                    break;
                default:
                    go = Call(_itemCreate, Str(e.GarageDef, "Id"), position, rotation);
                    if (go != null) Finisher.Queue(go, _itemFinish, FinishDelay);
                    break;
            }
            if (go == null) throw new InvalidOperationException("ApocalypterGarage could not create " + e.Display + " (see its log lines).");
            SpawnerPlugin.Log.LogInfo($"Spawned {go.name} via ApocalypterGarage ({e.Key})");
            return go;
        }

        private static GameObject Call(MethodInfo method, params object[] args)
        {
            try { return method.Invoke(null, args) as GameObject; }
            catch (TargetInvocationException tie) { throw new InvalidOperationException(tie.InnerException != null ? tie.InnerException.Message : tie.Message); }
        }

        // Calls Items.Finish a moment after creation, like Garage's own loot/merchant spawns. Lives on a hidden object of its own.
        private class Finisher : MonoBehaviour
        {
            private struct Job { public GameObject Go; public MethodInfo Finish; public float At; }
            private static Finisher _instance;
            private readonly List<Job> _jobs = new List<Job>();

            internal static void Queue(GameObject go, MethodInfo finish, float delay)
            {
                if (_instance == null)
                {
                    var host = new GameObject("Apocaspawner.GarageFinisher") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(host);
                    _instance = host.AddComponent<Finisher>();
                }
                _instance._jobs.Add(new Job { Go = go, Finish = finish, At = Time.time + delay });
            }

            private void Update()
            {
                for (int i = _jobs.Count - 1; i >= 0; i--)
                {
                    var job = _jobs[i];
                    if (job.Go == null) { _jobs.RemoveAt(i); continue; }
                    if (Time.time < job.At) continue;
                    _jobs.RemoveAt(i);
                    try { job.Finish.Invoke(null, new object[] { job.Go }); }
                    catch (Exception e) { SpawnerPlugin.Log.LogWarning($"ApocalypterGarage could not finish {job.Go.name}: {(e.InnerException ?? e).Message}"); }
                }
            }
        }
    }
}
