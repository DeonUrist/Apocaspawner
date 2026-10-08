using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Apocaspawner
{
    // =====================================================================
    //  Model previews for ApocalypterGarage entries.
    //  Garage dresses a vanilla prefab at runtime (hidden base meshes, its own OBJ body, moved hinges, bolted parts), so the base
    //  prefab's preview shows the wrong thing. Here the same dressing is redone on a VISUAL COPY only (Replacements.VisualCopy:
    //  transforms + renderers, no scripts, nothing registered, nothing saved), using Garage's own data and look builders:
    //    cars    - CarDef.Look.Hide / Moves, Wheels positions, new Hinges, EngineLook extras (Cars.looks cache), Fit parts on hinges
    //    engines - Engines.LookOf(def).Apply on the copy with a GarageEngine marker (block mesh, children, parts, extras)
    //    parts   - extras, Reshape (own model), Repaint, and GarageItem's "hide the base" rule
    //  Looks Garage has to build are stored in Garage's own caches, exactly as its spawn code would.
    //  Any failure falls back to the base prefab (the old behaviour) and is logged once per entry.
    // =====================================================================
    internal static class GaragePreview
    {
        private sealed class Built { internal GameObject Visual; internal readonly List<Material> Materials = new List<Material>(); }
        private static readonly Dictionary<string, Built> Cache = new Dictionary<string, Built>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        private const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static bool _resolved, _ok;
        private static Type _engineLook, _garageItem, _garageEngine;
        private static MethodInfo _child, _build, _addExtra, _apply, _lookOf, _reshape, _repaint;
        private static FieldInfo _carLooks, _itemLooks, _lookExtras;
        private static PropertyInfo _itemPrefabs;

        internal static void Clear()
        {
            foreach (var built in Cache.Values) Dispose(built);
            Cache.Clear(); Failed.Clear();
        }

        private static void Dispose(Built built)
        {
            if (built == null) return;
            foreach (var m in built.Materials) if (m != null) UnityEngine.Object.Destroy(m);
            if (built.Visual != null) UnityEngine.Object.Destroy(built.Visual);
        }

        private static bool Resolve()
        {
            if (_resolved) return _ok;
            _resolved = true;
            try
            {
                var asm = GarageBridge.Asm;
                if (asm == null) throw new MissingMemberException("assembly");
                var cars = Need(asm.GetType("ApocalypterGarage.Cars"));
                var items = Need(asm.GetType("ApocalypterGarage.Items"));
                var engines = Need(asm.GetType("ApocalypterGarage.Engines"));
                var world = Need(asm.GetType("ApocalypterGarage.GameWorld"));
                var engineDef = Need(asm.GetType("ApocalypterGarage.EngineDef"));
                var itemDef = Need(asm.GetType("ApocalypterGarage.ItemDef"));
                _engineLook = Need(asm.GetType("ApocalypterGarage.EngineLook"));
                _garageItem = Need(asm.GetType("ApocalypterGarage.GarageItem"));
                _garageEngine = Need(asm.GetType("ApocalypterGarage.GarageEngine"));
                var extraLook = Need(_engineLook.GetNestedType("ExtraLook", BindingFlags.Public | BindingFlags.NonPublic));
                _child = Need(cars.GetMethod("Child", S, null, new[] { typeof(Transform), typeof(string) }, null));
                _carLooks = Need(cars.GetField("looks", S));
                _itemLooks = Need(items.GetField("looks", S));
                _build = Need(_engineLook.GetMethod("Build", S, null, new[] { engineDef, typeof(GameObject), typeof(ArrayList) }, null));
                _addExtra = Need(_engineLook.GetMethod("AddExtra", S, null, new[] { typeof(Transform), extraLook, typeof(int), typeof(Vector3), typeof(int) }, null));
                _apply = Need(_engineLook.GetMethod("Apply", I, null, new[] { typeof(GameObject), engineDef, _garageEngine }, null));
                _lookExtras = Need(_engineLook.GetField("Extras", I));
                _lookOf = Need(engines.GetMethod("LookOf", S, null, new[] { engineDef }, null));
                _reshape = Need(items.GetMethod("Reshape", S, null, new[] { typeof(GameObject), _garageItem, _engineLook }, null));
                _repaint = Need(items.GetMethod("Repaint", S, null, new[] { typeof(GameObject), itemDef }, null));
                _itemPrefabs = Need(world.GetProperty("ItemPrefabs", S));
                return _ok = true;
            }
            catch (Exception e)
            {
                SpawnerPlugin.Log.LogWarning("ApocalypterGarage previews unavailable (" + e.Message + "); the base model is shown instead.");
                return _ok = false;
            }
        }
        private static T Need<T>(T value) where T : class { if (value == null) throw new MissingMemberException(); return value; }

        private static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(member, I); if (f != null) return f.GetValue(o);
            var p = t.GetProperty(member, I); return p != null ? p.GetValue(o, null) : null;
        }
        private static void Set(object o, string member, object value)
        {
            var f = o.GetType().GetField(member, I); if (f != null) f.SetValue(o, value);
        }
        private static Transform Child(Transform root, string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            try { return _child.Invoke(null, new object[] { root, spec }) as Transform; } catch { return null; }
        }
        private static ArrayList ItemPrefabs() { try { return _itemPrefabs.GetValue(null, null) as ArrayList; } catch { return null; } }

        /// <summary>A dressed visual copy for a Garage entry, or null to use the plain base prefab.</summary>
        internal static GameObject Model(Entry entry)
        {
            if (entry == null || entry.Prefab == null || entry.GarageDef == null) return null;
            Built built;
            if (Cache.TryGetValue(entry.Key, out built) && built.Visual != null) return built.Visual;
            if (Failed.Contains(entry.Key) || !Resolve()) return null;
            built = new Built();
            try
            {
                built.Visual = Replacements.VisualCopy(entry.Prefab, built.Materials);
                built.Visual.name = entry.Key;
                switch (entry.GarageKind)
                {
                    case GarageBridge.KindCar: DressCar(built, entry.GarageDef, entry.Prefab); break;
                    case GarageBridge.KindEngine: DressEngine(built.Visual, entry.GarageDef); break;
                    default: DressItem(built.Visual, entry.GarageDef, entry.Prefab); break;
                }
                Cache[entry.Key] = built;
                return built.Visual;
            }
            catch (Exception e)
            {
                Dispose(built); Failed.Add(entry.Key);
                SpawnerPlugin.Log.LogWarning("Garage preview " + entry.Key + ": " + (e.InnerException ?? e).Message + " (base model shown)");
                return null;
            }
        }

        // ---- cars: Cars.Wear's visual half, plus the parts a whole car comes with ----
        private static void DressCar(Built built, object def, GameObject basePrefab)
        {
            var car = built.Visual.transform;
            var look = Get(def, "Look");
            // new hinges copied from an existing one (only their place matters here)
            var hinges = Get(def, "Hinges") as IList;
            if (hinges != null)
                foreach (var h in hinges)
                {
                    string name = Get(h, "Item1") as string, like = Get(h, "Item2") as string;
                    var source = Child(car, like); if (source == null || name == null) continue;
                    var node = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
                    node.transform.SetParent(source.parent, false);
                    node.transform.localPosition = (Vector3)Get(h, "Item3");
                    node.transform.localRotation = Quaternion.Euler((Vector3)Get(h, "Item4"));
                }
            var hide = Get(look, "Hide") as IList;
            if (hide != null)
                foreach (string spec in hide)
                {
                    var t = Child(car, spec); if (t == null) continue;
                    foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                }
            var moves = Get(look, "Moves") as IList;
            if (moves != null)
                foreach (var m in moves)
                {
                    var t = Child(car, Get(m, "Child") as string); if (t == null) continue;
                    t.localPosition = (Vector3)Get(m, "Position");
                    t.localRotation = Quaternion.Euler((Vector3)Get(m, "Rotation"));
                }
            var unsolid = Get(def, "Unsolid") as IList;
            if (unsolid != null)
                foreach (string path in unsolid) { var t = car.Find(path); if (t != null) t.gameObject.SetActive(false); }
            var wheels = Get(def, "Wheels") as IList;
            if (wheels != null)
                foreach (var w in wheels)
                {
                    string hinge = Get(w, "Item1") as string;
                    var t = car.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == hinge);
                    if (t != null) t.localPosition = (Vector3)Get(w, "Item2");
                }
            // the body: EngineLook extras, built the way Cars.Wear builds them (and cached in Garage's own dictionary)
            var lookCache = _carLooks.GetValue(null) as IDictionary;
            string id = Get(def, "Id") as string;
            object engineLook = lookCache != null && id != null && lookCache.Contains(id) ? lookCache[id] : null;
            if (engineLook == null)
            {
                var model = basePrefab.transform.Find("parts/model");
                engineLook = _build.Invoke(null, new object[] { look, (model != null ? model : basePrefab.transform).gameObject, ItemPrefabs() });
                if (engineLook != null && lookCache != null && id != null) lookCache[id] = engineLook;
            }
            if (engineLook != null)
            {
                var vmodel = car.Find("parts/model");
                int layer = vmodel != null ? vmodel.gameObject.layer : car.gameObject.layer;
                var extras = _lookExtras.GetValue(engineLook) as IList;
                if (extras != null)
                    for (int i = 0; i < extras.Count; i++)
                        _addExtra.Invoke(null, new object[] { car, extras[i], i, car.localScale, layer });
            }
            // the parts it comes with (wheels, seats, lamps, steering wheel); a part that only carries a piece of the body is not drawn
            var fits = Get(def, "Fits") as IList;
            if (fits != null)
                foreach (var fit in fits)
                {
                    if (Get(fit, "Wears") != null) continue;
                    var hinge = Child(car, Get(fit, "Hinge") as string); if (hinge == null) continue;
                    var prefab = PartPrefab(Get(fit, "Item") as string); if (prefab == null) continue;
                    var part = Replacements.VisualCopy(prefab, built.Materials);
                    // wheels sit on the hinge's wheel_hub (mirrored on the right side), everything else on the hinge itself
                    // a wheel goes where the hinge's wheel_hub is (mirrored when the hub is), without taking the hub's size
                    var hub = hinge.Find("wheel_hub");
                    part.transform.SetParent(hinge, false);
                    part.transform.localPosition = hub != null ? hub.localPosition : Vector3.zero;
                    part.transform.localRotation = hub != null ? hub.localRotation : Quaternion.identity;
                    if (hub != null && hub.localScale.x < 0) { var sc = part.transform.localScale; sc.x = -sc.x; part.transform.localScale = sc; }
                    part.SetActive(true);
                }
        }

        private static GameObject PartPrefab(string item)
        {
            if (string.IsNullOrEmpty(item)) return null;
            var entries = Catalog.Entries();
            var e = entries.FirstOrDefault(x => !x.IsNpc && x.GarageKind == 0 && string.Equals(x.Key, item, StringComparison.OrdinalIgnoreCase))
                 ?? entries.FirstOrDefault(x => x.GarageKind == GarageBridge.KindItem && string.Equals(x.Key, "garage-part:" + item, StringComparison.OrdinalIgnoreCase));
            return e != null ? e.Prefab : null;
        }

        // ---- engines: the engine's EngineLook applied to the copy (GarageEngine marker only holds the stock look) ----
        private static void DressEngine(GameObject visual, object def)
        {
            var look = _lookOf.Invoke(null, new[] { def });
            if (look == null) return;   // no look of its own: the base engine is what it looks like
            var mark = visual.AddComponent(_garageEngine);
            _apply.Invoke(look, new object[] { visual, def, mark });
        }

        // ---- parts: Items.Finish's visual half + GarageItem.LateUpdate's hide rule ----
        private static void DressItem(GameObject visual, object def, GameObject basePrefab)
        {
            var mark = visual.AddComponent(_garageItem);
            Set(mark, "Def", def);
            var lookCache = _itemLooks.GetValue(null) as IDictionary;
            string id = Get(def, "Id") as string;
            object look = lookCache != null && id != null && lookCache.Contains(id) ? lookCache[id] : null;
            if (look == null)
            {
                look = _build.Invoke(null, new object[] { Get(def, "Look"), basePrefab, ItemPrefabs() });
                if (look != null && lookCache != null && id != null) lookCache[id] = look;
            }
            bool ownModel = Get(def, "OwnModel") is bool && (bool)Get(def, "OwnModel");
            bool repainted = Get(def, "Repainted") is bool && (bool)Get(def, "Repainted");
            if (look != null)
            {
                var extras = _lookExtras.GetValue(look) as IList;
                if (extras != null)
                    for (int i = ownModel ? 1 : 0; i < extras.Count; i++)
                        _addExtra.Invoke(null, new object[] { visual.transform, extras[i], i, visual.transform.localScale, visual.layer });
            }
            if (ownModel) _reshape.Invoke(null, new object[] { visual, mark, look });
            if (repainted) _repaint.Invoke(null, new object[] { visual, def });
            if (Get(mark, "Shape") == null && !repainted)
            {
                // GarageItem.LateUpdate: with no shape of its own and no repaint, every base renderer outside the garage: pieces is hidden
                foreach (var r in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool ours = false;
                    for (var t = r.transform; t != null && t != visual.transform; t = t.parent) if (t.name.StartsWith("garage:")) { ours = true; break; }
                    if (!ours) r.enabled = false;
                }
            }
        }
    }
}
