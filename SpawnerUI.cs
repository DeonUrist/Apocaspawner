using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apocasetter;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Apocaspawner
{
    internal sealed class SpawnerUI : MonoBehaviour
    {
        private const float Width = 1120, Height = 755;
        private bool _open, _npc, _vehicles, _binding, _editing, _unblockNextFrame;
        private Rect _win;
        private Vector2 _catScroll, _listScroll, _parameterScroll;
        private readonly string[] _categories = { "all", "all", "all" };
        private readonly string[] _selections = { "", "", "" };
        private string _search = "", _countText = "1", _distanceText, _status = "Select an entry to inspect it.";
        private int _count = 1;
        private Entry _selected;
        private Texture2D _preview;
        private List<SpawnParameter> _parameters = new List<SpawnParameter>();
        private List<Entry> _catalog = new List<Entry>();
        private CursorLockMode _prevLock;
        private bool _prevVisible;
        private Action _pending;
        private float _nextParameters;
        private int _screenWidth, _screenHeight;
        private int Tab => _npc ? 2 : _vehicles ? 1 : 0;
        private static int EntryTab(Entry e) => e.IsNpc ? 2 : Catalog.IsVehicleEntry(e) ? 1 : 0;

        private void Update()
        {
            InputShield.Tick();
            if (_unblockNextFrame && !InputShield.Blocking) { _unblockNextFrame = false; if (!_open) InputBlocker.Set(false); }
            var action = _pending; _pending = null; action?.Invoke();
            if (!_binding && SpawnerPlugin.Pressed(SpawnerPlugin.MenuKey)) { Toggle(); return; }
            if (!_open) return;
            if (_binding)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null) foreach (var key in keyboard.allKeys)
                {
                    if (!key.wasPressedThisFrame) continue;
                    if (key.keyCode == Key.Escape) { _binding = false; _status = "Key change cancelled."; }
                    else if (key.keyCode == SpawnerPlugin.MenuKey) { _status = "Choose a key different from the menu key."; }
                    else if (key.keyCode != Key.LeftShift && key.keyCode != Key.RightShift && key.keyCode != Key.LeftCtrl && key.keyCode != Key.RightCtrl && key.keyCode != Key.LeftAlt && key.keyCode != Key.RightAlt)
                    { SpawnerPlugin.SpawnKeyEntry.Value = key.keyCode; _binding = false; _status = "Spawn key: " + key.keyCode; }
                    break;
                }
                return;
            }
            if (SpawnerPlugin.Pressed(Key.Escape)) { Toggle(); return; }
            if (!_editing && SpawnerPlugin.Pressed(SpawnerPlugin.SpawnKeyEntry.Value)) SpawnSelected();
            if (_selected != null && Time.unscaledTime >= _nextParameters)
            {
                _nextParameters = Time.unscaledTime + 1;
                _parameters = Parameters.Read(_selected);
                if (_preview == null) _preview = PreviewCache.GetOrCreate(Replacements.PreviewModel(_selected));
            }
        }
        private void LateUpdate()
        { if (_open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }
        private void Toggle()
        {
            _open = !_open; _binding = false;
            InputShield.Set(_open);
            if (_open)
            {
                _prevLock = Cursor.lockState; _prevVisible = Cursor.visible; InputBlocker.Set(true);
                Catalog.Invalidate(); _catalog = Catalog.Entries();
                _distanceText = SpawnerPlugin.SpawnDistance.Value.ToString("0.##", CultureInfo.InvariantCulture);
                Select(_catalog.FirstOrDefault(e => EntryTab(e) == Tab && e.Key == _selections[Tab]));
            }
            else { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; _editing = false; _unblockNextFrame = true; }
        }
        private void OnDisable()
        {
            if (!_open) return;
            _open = false; Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; InputShield.Reset(); InputBlocker.Set(false);
        }
        private void Select(Entry entry)
        {
            _selected = entry; _selections[Tab] = entry?.Key ?? ""; _parameterScroll = Vector2.zero;
            _parameters = entry == null ? new List<SpawnParameter>() : Parameters.Read(entry);
            _preview = entry == null ? null : PreviewCache.GetOrCreate(Replacements.PreviewModel(entry));
            _nextParameters = Time.unscaledTime + 1;
        }
        private void SwitchTab(int tab)
        {
            if (Tab == tab) return;
            _npc = tab == 2; _vehicles = tab == 1; _catScroll = _listScroll = Vector2.zero; _editing = false;
            Select(_catalog.FirstOrDefault(e => EntryTab(e) == Tab && e.Key == _selections[Tab]));
        }
        private void SpawnSelected()
        {
            if (_selected == null || !Catalog.InGame || _binding) return;
            if (!float.TryParse(_distanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) || float.IsNaN(distance) || float.IsInfinity(distance))
            { _status = "Enter a valid spawn distance."; return; }
            int count = 1;
            if (!_selected.IsPortableRadio && !int.TryParse(_countText, out count)) { _status = "Enter a whole-number quantity."; return; }
            distance = Mathf.Clamp(distance, .5f, 1000f); _count = Mathf.Clamp(count, 1, 50);
            _distanceText = distance.ToString("0.##", CultureInfo.InvariantCulture); _countText = _count.ToString();
            SpawnerPlugin.SpawnDistance.Value = distance;
            try { Spawner.Spawn(_selected, _count); _status = _selected.IsPortableRadio ? "Retrieved your Portable Radio at " + _distanceText + " m." : "Spawned " + _count + " × " + _selected.Display + " at " + _distanceText + " m."; }
            catch (Exception e) { _status = "Spawn failed: " + e.Message; SpawnerPlugin.Log.LogError(e); }
        }
        private void OnGUI()
        {
            if (!_open) return;
            float scale = Mathf.Min(Screen.width * .94f / Width, Screen.height * .94f / Height);
            if (_screenWidth != Screen.width || _screenHeight != Screen.height || _win.width == 0)
            { _screenWidth = Screen.width; _screenHeight = Screen.height; _win = new Rect((Screen.width / scale - Width) / 2, (Screen.height / scale - Height) / 2, Width, Height); }
            _win.x = Mathf.Clamp(_win.x, 0, Screen.width / scale - Width); _win.y = Mathf.Clamp(_win.y, 0, Screen.height / scale - Height);
            var matrix = GUI.matrix; var oldSkin = SpawnerSkin.Begin(); int depth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.depth = -1200;
            try { _win = GUI.Window(0xA90CB, _win, Draw, "", GUIStyle.none); }
            finally { GUI.matrix = matrix; GUI.skin = oldSkin; GUI.depth = depth; }
        }
        private void Draw(int id)
        {
            SpawnerSkin.Window(Width, Height);
            if (SpawnerSkin.Button(new Rect(Width - 118, 16, 92, 30), "CLOSE", gameFont: true, transparent: true)) _pending = Toggle;
            SpawnerSkin.Fill(new Rect(6, 64, Width - 12, 36), SpawnerSkin.Surface);
            for (int i = 0; i < 3; i++)
            {
                int tab = i; var r = new Rect(26 + i * 150, 64, 150, 36);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { GUI.FocusControl(null); _pending = () => SwitchTab(tab); }
                if (Tab == tab) { SpawnerSkin.Fill(r, SpawnerSkin.Selected); SpawnerSkin.Fill(new Rect(r.x, r.yMax - 3, r.width, 3), SpawnerSkin.Accent); }
                SpawnerSkin.Fill(new Rect(r.xMax - 1, r.y, 1, r.height), SpawnerSkin.Line);
                SpawnerSkin.Label(r, tab == 0 ? "ITEMS" : tab == 1 ? "VEHICLES" : "NPC", SpawnerSkin.GameButton, Tab == tab ? SpawnerSkin.Accent : SpawnerSkin.Text, TextAnchor.MiddleCenter);
            }
            SpawnerSkin.Label(new Rect(Width - 360, 67, 58, 28), "Search", SpawnerSkin.Small, SpawnerSkin.Muted);
            GUI.SetNextControlName("Spawner.Search");
            _search = GUI.TextField(new Rect(Width - 296, 68, 270, 28), _search, SpawnerSkin.Input);
            SpawnerSkin.Rule(6, 99, Width - 12);
            if (Catalog.InGame) DrawCatalog();
            else SpawnerSkin.Label(new Rect(26, 160, Width - 52, 60), "Load into the game to browse and spawn.", SpawnerSkin.Heading);
            DrawFooter();
            string focus = GUI.GetNameOfFocusedControl(); _editing = focus == "Spawner.Search" || focus == "Spawner.Distance" || focus == "Spawner.Count";
            GUI.DragWindow(new Rect(6, 6, Width - 130, 57));
        }
        private void DrawCatalog()
        {
            const float top = 118, height = Height - 238;
            var left = new Rect(26, top, 168, height); var middle = new Rect(210, top, 272, height); var right = new Rect(498, top, Width - 524, height);
            foreach (var r in new[] { left, middle, right }) SpawnerSkin.Panel(r, new Color(23 / 255f, 19 / 255f, 13 / 255f, .91f));
            var entries = _catalog.Where(e => EntryTab(e) == Tab).ToList();
            var order = _vehicles ? new[] { "Vehicles", "Trailers", "Vehicle Parts", "Vehicle Body", "Tools", GarageBridge.GroupCars, GarageBridge.GroupEngines, GarageBridge.GroupParts } : Catalog.GroupOrder;
            var categories = _npc ? NpcCatalog.Factions : order.Where(g => entries.Any(e => Catalog.DisplayGroup(e) == g)).ToArray();
            SpawnerSkin.Label(new Rect(left.x + 12, top + 6, left.width - 24, 26), _npc ? "Faction" : "Category", SpawnerSkin.Heading);
            _catScroll = SpawnerSkin.Scroll(new Rect(left.x + 8, top + 38, left.width - 16, height - 48), _catScroll, (categories.Length + 1) * 34, width =>
            {
                DrawCategory(new Rect(0, 0, width, 34), "all", "All", entries.Count);
                for (int i = 0; i < categories.Length; i++) { string name = categories[i]; DrawCategory(new Rect(0, (i + 1) * 34, width, 34), name, _vehicles && name == "Vehicles" ? "Chassis" : name, entries.Count(e => Catalog.DisplayGroup(e) == name)); }
            });
            SpawnerSkin.Label(new Rect(middle.x + 12, top + 6, middle.width - 24, 26), _npc ? "NPCs" : _vehicles ? "Vehicles & parts" : "Items", SpawnerSkin.Heading);
            string search = _search.Trim(); var visible = entries.Where(e => (_categories[Tab] == "all" || Catalog.DisplayGroup(e) == _categories[Tab]) &&
                (search.Length == 0 || Contains(e.Display, search) || Contains(e.Key, search) || Contains(e.Group, search) || Contains(e.Category, search))).ToList();
            _listScroll = SpawnerSkin.Scroll(new Rect(middle.x + 8, top + 38, middle.width - 16, height - 48), _listScroll, visible.Count * 34, width =>
            {
                for (int i = 0; i < visible.Count; i++)
                {
                    var entry = visible[i]; var row = new Rect(0, i * 34, width, 34); bool selected = entry == _selected;
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { GUI.FocusControl(null); _pending = () => Select(entry); }
                    SpawnerSkin.Fill(row, selected ? SpawnerSkin.Selected : SpawnerSkin.Surface); SpawnerSkin.Rule(0, row.yMax - 1, width);
                    if (selected) SpawnerSkin.Fill(new Rect(0, row.y + 6, 2, row.height - 12), SpawnerSkin.Accent);
                    SpawnerSkin.Label(new Rect(10, row.y, width - 20, row.height), entry.Display, SpawnerSkin.Wrapped, selected ? SpawnerSkin.Accent : SpawnerSkin.Text);
                }
                if (visible.Count == 0) SpawnerSkin.Label(new Rect(10, 8, width - 20, 30), "No matches.", SpawnerSkin.Small, SpawnerSkin.Muted);
            });
            DrawDetail(right);
        }
        private static bool Contains(string value, string search) { return value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0; }
        private void DrawCategory(Rect row, string key, string name, int count)
        {
            bool selected = _categories[Tab] == key;
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { GUI.FocusControl(null); _pending = () => { _categories[Tab] = key; _listScroll = Vector2.zero; Select(null); }; }
            if (selected) SpawnerSkin.Fill(row, SpawnerSkin.Selected);
            SpawnerSkin.Label(new Rect(9, row.y, row.width - 42, row.height), name, SpawnerSkin.Wrapped, selected ? SpawnerSkin.Accent : SpawnerSkin.Text);
            SpawnerSkin.Label(new Rect(row.width - 33, row.y, 24, row.height), count.ToString(), SpawnerSkin.Small, SpawnerSkin.Muted, TextAnchor.MiddleRight);
        }
        private void DrawDetail(Rect right)
        {
            float x = right.x + 12, width = right.width - 24;
            if (_selected == null) { SpawnerSkin.Label(new Rect(x, right.y + 20, width, 35), "Select an item or NPC", SpawnerSkin.Heading); SpawnerSkin.Label(new Rect(x, right.y + 58, width, 40), "Choose a name to view its model and parameters.", SpawnerSkin.Small, SpawnerSkin.Muted); return; }
            SpawnerSkin.Label(new Rect(x, right.y + 8, width, 27), _selected.Display, SpawnerSkin.Heading);
            SpawnerSkin.Label(new Rect(x, right.y + 34, width, 20), Catalog.DisplayGroup(_selected), SpawnerSkin.Small, SpawnerSkin.Muted);
            if (!string.IsNullOrEmpty(_selected.Mod)) DrawModTag(new Rect(x, right.y + 34, width, 20), _selected.Mod);
            var preview = new Rect(x, right.y + 64, width * .43f, right.height - 76); SpawnerSkin.Panel(preview, SpawnerSkin.Background);
            if (_preview != null) GUI.DrawTexture(new Rect(preview.x + 8, preview.y + 8, preview.width - 16, preview.height - 16), _preview, ScaleMode.ScaleToFit, true);
            else SpawnerSkin.Label(preview, "Model preview unavailable", SpawnerSkin.Small, SpawnerSkin.Muted, TextAnchor.MiddleCenter);
            var table = new Rect(preview.xMax + 14, preview.y, width - preview.width - 14, preview.height);
            float height = ParameterHeight(table.width - 10);
            _parameterScroll = SpawnerSkin.Scroll(table, _parameterScroll, height, available => DrawParameters(available));
        }
        // Small "MOD · name version" badge at the right of the category line, for entries a mod adds
        private static void DrawModTag(Rect line, string mod)
        {
            var content = new GUIContent("MOD · " + mod);
            float w = Mathf.Min(line.width * .6f, SpawnerSkin.Small.CalcSize(content).x + 16);
            var tag = new Rect(line.xMax - w, line.y, w, line.height);
            SpawnerSkin.Panel(tag, SpawnerSkin.Selected, SpawnerSkin.Accent);
            SpawnerSkin.Label(tag, content.text, SpawnerSkin.Small, SpawnerSkin.Accent, TextAnchor.MiddleCenter);
        }
        private float RowHeight(SpawnParameter row, float width)
        {
            string value = row.Value + (string.IsNullOrEmpty(row.Unit) ? "" : " " + row.Unit);
            return Mathf.Max(25, Mathf.Max(SpawnerSkin.Wrapped.CalcHeight(new GUIContent(row.Label), width * .56f), SpawnerSkin.Wrapped.CalcHeight(new GUIContent(value), width * .40f)) + 6);
        }
        private float ParameterHeight(float width)
        { return _parameters.GroupBy(p => new { p.Section, p.Source }).Sum(g => 22 + g.Sum(p => RowHeight(p, width)) + 8); }
        private void DrawParameters(float width)
        {
            float y = 0;
            foreach (var section in _parameters.GroupBy(p => new { p.Section, p.Source }))
            {
                string heading = section.Key.Section.ToUpperInvariant();
                if (!string.IsNullOrEmpty(section.Key.Source) && section.Key.Source != section.Key.Section) heading += " · " + section.Key.Source;
                SpawnerSkin.Label(new Rect(0, y, width, 20), heading, SpawnerSkin.Small, SpawnerSkin.Muted); y += 22;
                foreach (var row in section)
                {
                    float height = RowHeight(row, width); string value = row.Value + (string.IsNullOrEmpty(row.Unit) ? "" : " " + row.Unit);
                    SpawnerSkin.Label(new Rect(0, y + 2, width * .56f, height - 4), row.Label, SpawnerSkin.Wrapped, SpawnerSkin.Muted);
                    SpawnerSkin.Label(new Rect(width * .60f, y + 2, width * .40f, height - 4), value, SpawnerSkin.Wrapped, SpawnerSkin.Text, TextAnchor.MiddleRight);
                    SpawnerSkin.Rule(0, y + height - 1, width); y += height;
                }
                y += 8;
            }
        }
        private void DrawFooter()
        {
            const float y = Height - 100;
            SpawnerSkin.Rule(6, y - 10, Width - 12);
            SpawnerSkin.Label(new Rect(26, y, 485, 74), _status, SpawnerSkin.Small, SpawnerSkin.Muted);
            SpawnerSkin.Panel(new Rect(Width - 541, y, 515, 74), new Color(23 / 255f, 19 / 255f, 13 / 255f, .91f));
            const float distanceX = Width - 523, countX = Width - 357, spawnX = Width - 198;
            SpawnerSkin.Label(new Rect(distanceX, y + 8, 164, 18), "Spawn distance · global", SpawnerSkin.Small, SpawnerSkin.Muted);
            GUI.SetNextControlName("Spawner.Distance");
            string next = GUI.TextField(new Rect(distanceX, y + 32, 125, 30), _distanceText ?? "10", SpawnerSkin.Input);
            if (next != _distanceText)
            {
                _distanceText = next;
                if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= .5f && value <= 1000) SpawnerPlugin.SpawnDistance.Value = value;
            }
            SpawnerSkin.Label(new Rect(distanceX + 133, y + 32, 20, 30), "m", SpawnerSkin.Small, SpawnerSkin.Muted);
            SpawnerSkin.Label(new Rect(countX, y + 8, 143, 18), "Quantity", SpawnerSkin.Small, SpawnerSkin.Muted);
            if (_selected != null && _selected.IsPortableRadio)
                SpawnerSkin.Label(new Rect(countX, y + 32, 143, 30), "1 · existing radio", SpawnerSkin.Small, SpawnerSkin.Muted);
            else
            {
                if (SpawnerSkin.Button(new Rect(countX, y + 32, 33, 30), "−")) { GUI.FocusControl(null); ChangeCount(-1); }
                GUI.SetNextControlName("Spawner.Count"); _countText = GUI.TextField(new Rect(countX + 38, y + 32, 54, 30), _countText, SpawnerSkin.Input);
                if (SpawnerSkin.Button(new Rect(countX + 97, y + 32, 33, 30), "+")) { GUI.FocusControl(null); ChangeCount(1); }
            }
            if (SpawnerSkin.Button(new Rect(spawnX, y + 32, 154, 30), _selected != null && _selected.IsPortableRadio ? "RETRIEVE" : "SPAWN", enabled: _selected != null && Catalog.InGame && !_binding, primary: true, gameFont: true)) { GUI.FocusControl(null); _pending = SpawnSelected; }
            var keyRect = new Rect(spawnX, y + 6, 154, 20);
            if (GUI.Button(keyRect, GUIContent.none, GUIStyle.none)) { GUI.FocusControl(null); _binding = !_binding; }
            SpawnerSkin.Label(keyRect, _binding ? "Press a key…" : "[" + SpawnerPlugin.SpawnKeyEntry.Value + "] · Change key", SpawnerSkin.Small, SpawnerSkin.Muted, TextAnchor.MiddleCenter);
        }
        private void ChangeCount(int delta)
        { if (int.TryParse(_countText, out var n)) _count = n; _count = Mathf.Clamp(_count + delta, 1, 50); _countText = _count.ToString(); }
    }
}
