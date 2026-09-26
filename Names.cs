using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;

namespace Apocaspawner
{
    // =====================================================================
    //  Display-name table, stored in the standard BepInEx config file
    //  BepInEx/config/com.denis.apocalypter.apocaspawner.cfg under [Names]:
    //
    //    [Names]
    //    9mm_borz_smg = Borz SMG (9mm)
    //    exhaust_the_six = The Six Exhaust
    //    some_new_item =                      <- empty = not named yet, menu shows the internal name
    //
    //  Items the game adds later are appended automatically with an empty value.
    // =====================================================================
    internal static class NameTable
    {
        private const string Section = "Names";
        private static ConfigFile _cfg;
        private static bool _hadNamesSection;
        private static readonly Dictionary<string, ConfigEntry<string>> _entries = new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);
        private static bool _dirty;

        public static void Init(ConfigFile cfg)
        {
            _cfg = cfg;
            _entries.Clear();
            // Bootstrap rule: if the config has never had a [Names] section, fill it with preset names;
            // afterwards, unknown items are added with an empty value.
            try { _hadNamesSection = File.Exists(cfg.ConfigFilePath) && File.ReadAllText(cfg.ConfigFilePath).Contains("[" + Section + "]"); }
            catch { _hadNamesSection = false; }
            SpawnerPlugin.Log.LogInfo(_hadNamesSection ? "cfg: [Names] section found" : "cfg: no [Names] section yet, presets will be written");
        }

        /// Display name for a prefab key; registers unknown keys in the config.
        public static string Resolve(string key, string preset)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                var initial = _hadNamesSection ? "" : preset;
                bool wasSaving = _cfg.SaveOnConfigSet;
                _cfg.SaveOnConfigSet = false;
                try { entry = _cfg.Bind(Section, Sanitize(key), initial, "Displayed name; leave empty to show the internal name"); }
                finally { _cfg.SaveOnConfigSet = wasSaving; }
                _entries[key] = entry;
                _dirty = true;
            }
            var v = entry.Value;
            return string.IsNullOrEmpty(v) ? key : v;
        }

        public static void SaveIfDirty()
        {
            if (!_dirty) return;
            try { _cfg.Save(); _hadNamesSection = true; _dirty = false; SpawnerPlugin.Log.LogInfo($"cfg: saved {_entries.Count} names"); }
            catch (Exception e) { SpawnerPlugin.Log.LogError("cfg: save failed: " + e.Message); }
        }

        // BepInEx forbids a few characters in config keys
        private static string Sanitize(string key)
        {
            foreach (var c in new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']' }) key = key.Replace(c, '_');
            return key;
        }
    }
}
