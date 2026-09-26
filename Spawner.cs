using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Apocasetter;

namespace Apocaspawner
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency(Apocasetter.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class SpawnerPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocaspawner";
        public const string NAME = "Apocaspawner";
        public const string VERSION = "1.3.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<Key> MenuKeyEntry;
        internal static Key MenuKey => MenuKeyEntry.Value;
        internal static ConfigEntry<float> ItemDistance, VehicleDistance;
        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            MenuKeyEntry = Config.Bind("Keys", "ToggleMenu", Key.F4, "Open/close the spawner window (Unity Input System key name, e.g. F4, Insert, Backquote)");
            ItemDistance = Config.Bind("Spawn", "ItemDistance", 2.5f, "Metres in front of the player to drop items");
            VehicleDistance = Config.Bind("Spawn", "VehicleDistance", 8f, "Metres in front of the player to place vehicles");

            SceneManager.sceneLoaded += (s, m) => { Catalog.Invalidate(); EnsureRunner("scene " + s.name); };
            NameTable.Init(Config);
            EnsureRunner("Awake");
            Log.LogInfo($"{NAME} {VERSION} loaded. {MenuKey} opens the spawner.");
        }

        // The game destroys the BepInEx plugin object on scene load; keep our logic on a hidden object it can't find.
        private static void EnsureRunner(string why)
        {
            if (_runner != null && _runner.activeInHierarchy) return;
            _runner = new GameObject("Apocaspawner.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<SpawnerUI>();
            Log.LogInfo($"Runner created ({why})");
        }

        internal static bool Pressed(Key key) => Apocasetter.Plugin.Pressed(key);
    }

    // =====================================================================
    //  Catalog: discover spawnable prefabs from what the game has loaded
    // =====================================================================
    internal class Entry
    {
        public string Key;          // prefab base name, e.g. "9mm_borz_smg"
        public string Display;      // ItemName string, e.g. "Borz SMG (9mm)"
        public string Category;     // game's ID string ("weapon", ...) / "vehicle" / "dev-spawn"
        public string Group;        // our logical grouping for the UI
        public GameObject Prefab;   // asset (not in scene) when available
        public bool IsAsset;
        public bool IsVehicle;
        public PlayMakerFSM DevSpawnFsm; // Debug_Canvas button FSM, if this came from there
        public int Value;
    }

    internal static class Catalog
    {
        private static List<Entry> _entries;
        private static GameObject _player;
        private static readonly Regex CloneRx = new Regex(@"\(Clone\)\d*$|\s\(\d+\)$");

        public static void Invalidate() { _entries = null; _player = null; GameMenu.Invalidate(); }

        // ---- logical grouping of the game's ID strings ----
        private static readonly Dictionary<string, string> GroupById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["weapon"] = "Weapons",
            ["grenade"] = "Ammo", ["ammo"] = "Ammo",
            ["nuts"] = "Gear", ["armorplate"] = "Gear", ["bodyarmor"] = "Gear", ["backpack"] = "Gear",
            ["flashlight"] = "Gear", ["flashlight_batteries"] = "Gear", ["binocular"] = "Gear", ["Toolbox"] = "Gear", ["Repairbox"] = "Gear",
            ["bandage"] = "Drugs", ["first_aid"] = "Drugs", ["sanitypills"] = "Drugs",
            ["cigar"] = "Drugs", ["cigarette"] = "Drugs", ["weed"] = "Drugs",
            ["meat"] = "Food", ["meat_lizard"] = "Food", ["dogfood_can"] = "Food",
            ["diesel_barrel"] = "Cans & Barrels", ["diesel_can"] = "Cans & Barrels", ["gasoline_barrel"] = "Cans & Barrels",
            ["gasoline_can"] = "Cans & Barrels", ["motoroil_can_small"] = "Cans & Barrels",
            ["water_can_plastic"] = "Cans & Barrels", ["water_barrel"] = "Cans & Barrels",
            ["plant_tobacco"] = "Camp & Farming", ["plant_weed"] = "Camp & Farming", ["maggot_farm_portable"] = "Camp & Farming",
            ["portable_dew_collector"] = "Camp & Farming", ["portable_gasoline_stove"] = "Camp & Farming", ["bedroll"] = "Camp & Farming",
            ["can_light"] = "Camp & Farming",
            ["vehicle"] = "Vehicles",
            ["trailer"] = "Trailers",
            ["engine"] = "Vehicle Parts", ["exhaust"] = "Vehicle Parts", ["forcedinduction"] = "Vehicle Parts", ["gauge"] = "Vehicle Parts",
            ["gearlever"] = "Vehicle Parts", ["headlight"] = "Vehicle Parts", ["hood"] = "Vehicle Body", ["radiator"] = "Vehicle Parts",
            ["suspension"] = "Vehicle Parts", ["wheel"] = "Vehicle Parts", ["radio"] = "Vehicle Parts",
            // body panels & armour: ID FSM only, no ItemName (poloska_hood, door_car_1_L, metal_plate_1, ...)
            ["door"] = "Vehicle Body", ["enginedoor"] = "Vehicle Body", ["trunk"] = "Vehicle Body", ["bumper"] = "Vehicle Body",
            ["seat"] = "Vehicle Body", ["windshield"] = "Vehicle Body", ["firewall"] = "Vehicle Body", ["frontpart"] = "Vehicle Body",
            ["rearpart"] = "Vehicle Body", ["roofrack"] = "Vehicle Body", ["steeringwheel"] = "Vehicle Body",
            ["cassette"] = "Cassettes",
            ["attachable"] = "Crates",
            ["carcasse"] = "Carcasses",
            ["dev-spawn"] = "Dev Spawns",
        };
        // per-prefab overrides where the game's ID is misleading
        private static readonly Dictionary<string, string> GroupByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alcohol_canister"] = "Drugs",
            ["wire_plate"] = "Vehicle Body",
        };
        // prefab-name prefixes that override the group (ID "attachable" is shared with crates/trophies)
        private static readonly Dictionary<string, string> GroupByPrefix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["metal_plate_"] = "Vehicle Body", ["spikes_"] = "Vehicle Body",
        };
        // prefabs that must never be offered (static car-part meshes with no physics, etc.)
        private static readonly string[] BlacklistPrefixes = { "toolset_" };
        private static readonly string[] BlacklistCategories = { "PartAdjusterTools", "grenadeexplode", "blastlanceexplode" };   // explosion effect prefabs carry an ID too

        // sort priority inside a group (lower = higher up); everything else sorts after these
        private static readonly Dictionary<string, int> KeyPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["grenade"] = 0,
            ["tin_can_nuts"] = 0,
        };

        public static readonly string[] GroupOrder =
        {
            "Weapons", "Ammo", "Gear", "Drugs", "Food", "Cans & Barrels", "Camp & Farming",
            "Vehicles", "Trailers", "Vehicle Parts", "Vehicle Body", "Cassettes", "Crates", "Trophies", "Carcasses", "Dev Spawns", "Other"
        };

        public static bool Blacklisted(string key) => BlacklistPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        public static bool BlacklistedCategory(string cat) => cat != null && BlacklistCategories.Any(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));

        public static string GroupFor(Entry e)
        {
            if (e.Key != null && e.Key.StartsWith("trophy_", StringComparison.OrdinalIgnoreCase)) return "Trophies";
            if (e.Key != null && GroupByKey.TryGetValue(e.Key, out var g)) return g;
            if (e.Key != null) foreach (var kv in GroupByPrefix) if (e.Key.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            if (e.Category != null && GroupById.TryGetValue(e.Category, out g)) return g;
            return "Other";
        }

        public static int PriorityFor(Entry e) => e.Key != null && KeyPriority.TryGetValue(e.Key, out var p) ? p : 100;

        // ---- button labels ----
        private static readonly Dictionary<string, string> WordFix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rpm"] = "RPM", ["utv"] = "UTV", ["temp"] = "Temperature", ["speedo"] = "Speedometer", ["gearlever"] = "Gear Lever",
            ["v6"] = "V6", ["v8"] = "V8", ["steeringwheel"] = "Steering Wheel", ["roofrack"] = "Roof Rack",
        };
        // side suffixes: door_car_1_L -> "Car 1 Left Door"
        private static readonly Dictionary<string, string> SideWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["l"] = "Left", ["r"] = "Right", ["fl"] = "Front Left", ["fr"] = "Front Right", ["rl"] = "Rear Left", ["rr"] = "Rear Right",
        };
        // families whose family word should move to the end: "exhaust_the_six" -> "The Six Exhaust"
        private static readonly string[] TrailingWords = { "trailer", "door", "hood", "seat", "exhaust", "gauge", "gearlever", "wheel", "radiator", "headlight" };

        private static string Prettify(string raw)
        {
            var words = raw.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            // side suffix comes off first and goes back in front of the family word: door_car_1_L -> "Car 1 Left Door"
            string side = null;
            if (words.Count > 1 && SideWords.TryGetValue(words[words.Count - 1], out side)) words.RemoveAt(words.Count - 1);
            // pull trailing numbers off: "rustallion_exhaust_1" -> [rustallion, exhaust] + [1]
            var nums = new List<string>();
            while (words.Count > 1 && words[words.Count - 1].All(char.IsDigit)) { nums.Insert(0, words[words.Count - 1]); words.RemoveAt(words.Count - 1); }
            // family word first -> move it last: "exhaust_the_six" -> "The Six Exhaust", but not "wheel_2_armored"
            if (words.Count > 1 && TrailingWords.Contains(words[0].ToLowerInvariant()) && !words[1].All(char.IsDigit))
            {
                var fam = words[0]; words.RemoveAt(0); words.AddRange(nums); nums.Clear(); words.Add(fam);   // door_car_1 -> [car, 1, door]
            }
            words.AddRange(nums);
            if (side != null)
            {
                int fam = words.FindLastIndex(w => TrailingWords.Contains(w.ToLowerInvariant()));
                if (fam > 0) words.Insert(fam, side); else words.Add(side);
            }
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (WordFix.TryGetValue(w, out var fixedWord)) { words[i] = fixedWord; continue; }
                if (w.All(c => char.IsUpper(c) || char.IsDigit(c))) continue;          // UTV, 12 -> keep
                words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
            }
            return string.Join(" ", words);
        }

        private static bool LooksUgly(string display, string key)
            => string.IsNullOrEmpty(display) || display == key || (display.Contains("_") && !display.Contains(" ")) || display.EndsWith("_")
               || display.All(c => !char.IsUpper(c));   // all-lowercase like "cardboard box"

        public static string LabelFor(Entry e, string itemName)
        {
            var key = e.Key ?? "";
            var display = string.IsNullOrEmpty(itemName) ? key : itemName;

            if (key.Equals("tin_can_nuts", StringComparison.OrdinalIgnoreCase)) return "Nuts";

            if (string.Equals(e.Category, "carcasse", StringComparison.OrdinalIgnoreCase))
            {
                var n = display.EndsWith("_Dead") ? display.Substring(0, display.Length - 5) : display;
                return n.Replace('_', ' ') + " carcass";
            }

            if (display.StartsWith("Repairbox ", StringComparison.OrdinalIgnoreCase))
                return display.Substring(10) + " Repairbox";

            if (LooksUgly(display, key))
                return Prettify(display.TrimEnd(new[] { '_' }));

            return display;
        }


        public static GameObject Player => _player != null ? _player : (_player = GameObject.Find("Player"));
        public static bool InGame => GameMenu.InGame;

        public static string BaseName(string n) => CloneRx.Replace(n, "").Trim();

        public static List<Entry> Entries()
        {
            if (_entries != null) return _entries;
            var byKey = new Dictionary<string, Entry>();
            var all = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f != null).ToArray();

            // group FSMs by their GameObject
            var byGo = new Dictionary<GameObject, List<PlayMakerFSM>>();
            foreach (var f in all)
            {
                if (!byGo.TryGetValue(f.gameObject, out var l)) byGo[f.gameObject] = l = new List<PlayMakerFSM>();
                l.Add(f);
            }

            foreach (var kv in byGo)
            {
                var go = kv.Key; var fsms = kv.Value;
                bool isAsset = !go.scene.IsValid();
                var names = new HashSet<string>(fsms.Select(f => f.FsmName));

                // trailers (Trailer_Big/Trailer_Small) carry none of the car FSMs, only TrailerAttached/TireWear/DistanceKinematic;
                // the game spawns them with the car recipe (TrailerSpawn -> ArrayList_Cars), so treat them as vehicles
                bool isTrailer = names.Contains("TrailerAttached");
                bool isVehicle = isTrailer || names.Contains("RpmGear") || names.Contains("getFuel") || names.Contains("CrashDamage");
                // items = anything with an ID FSM; ItemName is optional (body panels/armour plates only have ID + ES3Prefab)
                bool isItem = names.Contains("ID");
                if (!isVehicle && !isItem) continue;
                if (go.transform.parent != null && !isVehicle && !isAsset) continue; // scene items nested in something: skip (parts, UI)
                if (isVehicle && go.transform.parent != null) continue;              // vehicle parts carry vehicle-ish FSMs too

                var key = BaseName(go.name);
                if (string.IsNullOrEmpty(key) || Blacklisted(key)) continue;

                var e = new Entry { Key = key, Prefab = go, IsAsset = isAsset, IsVehicle = isVehicle };
                var itemName = fsms.FirstOrDefault(f => f.FsmName == "ItemName")?.FsmVariables.GetFsmString("ItemName")?.Value;
                e.Category = isTrailer ? "trailer" : isVehicle ? "vehicle" : (fsms.FirstOrDefault(f => f.FsmName == "ID")?.FsmVariables.GetFsmString("ID")?.Value ?? "misc");
                if (string.IsNullOrEmpty(e.Category)) e.Category = "misc";
                if (BlacklistedCategory(e.Category)) continue;
                e.Display = NameTable.Resolve(e.Key, LabelFor(e, itemName));
                e.Value = fsms.FirstOrDefault(f => f.FsmName == "Value")?.FsmVariables.GetFsmInt("BuyValue")?.Value ?? 0;

                // prefer assets over scene instances for the same key
                if (byKey.TryGetValue(key, out var existing) && (existing.IsAsset || !isAsset)) continue;
                byKey[key] = e;
            }

            // Dev spawn buttons (Debug_Canvas/Debug/spawn*) – their prefabs may not be items at all (car bases etc.)
            foreach (var f in all.Where(f => f.FsmName == "Continue" && f.gameObject.scene.IsValid()
                                          && f.gameObject.name.StartsWith("spawn")
                                          && f.transform.parent != null && f.transform.parent.name == "Debug"))
            {
                GameObject prefab = null;
                try
                {
                    if (f.Fsm != null && f.Fsm.Initialized)
                    {
                        var create = f.FsmStates?.SelectMany(s => s.Actions ?? new FsmStateAction[0]).OfType<CreateObject>().FirstOrDefault();
                        prefab = create?.gameObject?.Value;
                    }
                }
                catch (Exception err) { SpawnerPlugin.Log.LogDebug($"dev spawn {f.gameObject.name}: {err.Message}"); }
                if (prefab == null) continue;   // uninitialised dev button: nothing usable
                var key = BaseName(prefab.name);
                if (Blacklisted(key)) continue;
                if (byKey.TryGetValue(key, out var ex)) { ex.DevSpawnFsm = f; continue; }
                byKey[key] = new Entry
                {
                    Key = key, Display = NameTable.Resolve(key, key), Category = "dev-spawn", Prefab = prefab,
                    IsAsset = prefab != null && !prefab.scene.IsValid(), IsVehicle = false, DevSpawnFsm = f
                };
            }

            foreach (var e in byKey.Values) e.Group = GroupFor(e);
            NameTable.SaveIfDirty();
            _entries = byKey.Values
                .OrderBy(e => { var i = Array.IndexOf(GroupOrder, e.Group); return i < 0 ? 999 : i; })
                .ThenBy(PriorityFor)
                .ThenBy(e => e.Group == "Vehicle Parts" || e.Group == "Vehicle Body" ? e.Category : "", StringComparer.OrdinalIgnoreCase)   // parts: by type first
                .ThenBy(e => e.Display, StringComparer.OrdinalIgnoreCase).ToList();
            SpawnerPlugin.Log.LogInfo($"Catalog: {_entries.Count} entries, categories: {string.Join(", ", _entries.GroupBy(e => e.Category).Select(g => $"{g.Key}({g.Count()})"))}");
            return _entries;
        }

    }

    // =====================================================================
    //  Spawning, replicating the game's own ItemSpawner recipe
    // =====================================================================
    internal static class Spawner
    {
        public static bool UseDevSpawner = true;

        public static void Spawn(Entry e, int count = 1)
        {
            if (e.DevSpawnFsm != null && UseDevSpawner && e.DevSpawnFsm.isActiveAndEnabled)
            {
                for (int i = 0; i < count; i++) e.DevSpawnFsm.SetState("clicked");
                SpawnerPlugin.Log.LogInfo($"Spawned {count}x {e.Key} via dev spawn button");
                return;
            }
            if (e.Prefab == null) { SpawnerPlugin.Log.LogWarning($"{e.Key}: no prefab"); return; }
            var player = Catalog.Player;
            if (player == null) { SpawnerPlugin.Log.LogWarning("Player not found"); return; }

            var t = player.transform;
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;

            for (int i = 0; i < count; i++)
            {
                float dist = e.IsVehicle ? SpawnerPlugin.VehicleDistance.Value : SpawnerPlugin.ItemDistance.Value;
                var pos = t.position + fwd * dist + Vector3.up * (e.IsVehicle ? 1.0f : 1.2f) + Vector3.right * (i * 0.4f);
                var rot = e.IsVehicle ? Quaternion.LookRotation(fwd) : UnityEngine.Random.rotation;
                var go = UnityEngine.Object.Instantiate(e.Prefab, pos, rot);
                go.SetActive(true);
                Register(go, e);
            }
        }

        // 1. bump the global itemNameID counter  2. name the clone "<prefab>(Clone)<n>"  3. add to the save-tracked list
        private static void Register(GameObject go, Entry e)
        {
            int id = -1;
            var counterGo = GameObject.Find("itemNameID");
            var counter = counterGo?.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "itemNameID")?.FsmVariables.GetFsmInt("intName");
            if (counter != null) { counter.Value += 1; id = counter.Value; }
            go.name = e.Prefab.name + "(Clone)" + (id >= 0 ? id.ToString() : "");

            var reg = GameObject.Find("NewGO_ArrayList");
            PlayMakerArrayListProxy list = null;
            if (reg != null)
            {
                var proxies = reg.GetComponents<PlayMakerArrayListProxy>();
                string want = e.IsVehicle ? "car" : "item";
                list = proxies.FirstOrDefault(p => (p.referenceName ?? "").ToLowerInvariant().Contains(want))
                    ?? proxies.FirstOrDefault(p => !(p.referenceName ?? "").ToLowerInvariant().Contains("sand"));
                if (list != null) list.arrayList.Add(go);
            }
            SpawnerPlugin.Log.LogInfo($"Spawned {go.name} [{e.Category}] registered={(list != null ? list.referenceName : "NO")}");
        }
    }

    // =====================================================================
    //  UI
    // =====================================================================
    internal class SpawnerUI : MonoBehaviour
    {
        private bool _open;
        private Rect _win = new Rect(40, 40, 640, 620);
        private Vector2 _catScroll, _listScroll;
        private string _search = "";
        private string _category = "all";
        private int _count = 1;
        private CursorLockMode _prevLock;
        private bool _prevVisible;

        private bool _unblockNextFrame;

        private void Update()
        {
            if (_unblockNextFrame) { _unblockNextFrame = false; if (!_open) InputBlocker.Set(false); }
            if (SpawnerPlugin.Pressed(SpawnerPlugin.MenuKey)) Toggle();
            else if (_open && SpawnerPlugin.Pressed(Key.Escape)) Toggle();
        }


        private void LateUpdate()
        {
            if (!_open) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Toggle()
        {
            _open = !_open;
            if (_open) { _prevLock = Cursor.lockState; _prevVisible = Cursor.visible; InputBlocker.Set(true); }
            else { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; _unblockNextFrame = true; } // keep blocking one more frame so the closing keypress isn't seen by the game
        }

        private void OnGUI()
        {
            if (!_open) return;
            Theme.Apply();
            _win = GUILayout.Window(0xA90CB, _win, Draw, "",
                GUILayout.Width(640), GUILayout.Height(620));
        }

        private void Draw(int id)
        {
            // title bar: bold title top-left, close control top-right, both on the same row
            var titleStyle = Theme.Header ?? new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            const float titleY = 8f;
            var titleText = $"Apocaspawner {SpawnerPlugin.VERSION}";
            var titleSize = titleStyle.CalcSize(new GUIContent(titleText));
            GUI.Label(new Rect(14, titleY, titleSize.x + 4, titleSize.y + 2), titleText, titleStyle);
            var closeText = $"[{SpawnerPlugin.MenuKey}] Close";
            var closeSize = titleStyle.CalcSize(new GUIContent(closeText));
            if (GUI.Button(new Rect(_win.width - closeSize.x - 14, titleY, closeSize.x + 4, closeSize.y + 2), closeText, titleStyle)) Toggle();

            if (!Catalog.InGame) { GUILayout.Label("Load into the game first."); GUI.DragWindow(); return; }

            var entries = Catalog.Entries();
            var cats = entries.GroupBy(e => e.Group).Select(g => new { Name = g.Key, Count = g.Count() })
                              .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // ---- top bar ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50));
            _search = GUILayout.TextField(_search, GUILayout.Width(260));
            GUILayout.Space(10);
            GUILayout.Label("Count", GUILayout.Width(44));
            int.TryParse(GUILayout.TextField(_count.ToString(), GUILayout.Width(36)), out _count);
            _count = Mathf.Clamp(_count, 1, 50);
            if (GUILayout.Button("-", GUILayout.Width(24))) _count = Mathf.Max(1, _count - 1);
            if (GUILayout.Button("+", GUILayout.Width(24))) _count = Mathf.Min(50, _count + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // ---- two panes ----
            GUILayout.BeginHorizontal();

            // left: categories
            var catStyle = Theme.Category ?? GUI.skin.button;
            GUILayout.BeginVertical(Theme.LeftPanel ?? GUI.skin.box, GUILayout.Width(190));
            _catScroll = GUILayout.BeginScrollView(_catScroll);
            if (GUILayout.Toggle(_category == "all", $" All ({entries.Count})", catStyle)) _category = "all";
            foreach (var c in cats)
                if (GUILayout.Toggle(_category == c.Name, $" {c.Name} ({c.Count})", catStyle)) _category = c.Name;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // right: entries
            GUILayout.BeginVertical(Theme.RightPanel ?? GUI.skin.box);
            _listScroll = GUILayout.BeginScrollView(_listScroll);
            var s = _search.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var e in entries)
            {
                if (_category != "all" && e.Group != _category) continue;
                if (s.Length > 0 && !e.Display.ToLowerInvariant().Contains(s) && !e.Key.ToLowerInvariant().Contains(s) && !e.Category.ToLowerInvariant().Contains(s)) continue;
                shown++;
                if (GUILayout.Button(e.Display)) Spawner.Spawn(e, _count);
            }
            if (shown == 0) GUILayout.Label("Nothing matches.");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}
