using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace Apocaspawner
{
    // =====================================================================
    //  Display-name table, stored in the standard BepInEx config file
    //  BepInEx/config/com.denis.apocalypter.apocaspawner.cfg under [Names]:
    //
    //    [Names]
    //    9mm_borz_smg = Borz SMG (9mm)
    //    exhaust_the_six = The Six Exhaust
    //    Trailer_Big = Big Trailer
    //
    //  Items the game adds later are appended automatically with a generated name (from the game's ItemName
    //  or the prefab name); an empty value is filled in with that generated name on the next run.
    //  BepInEx writes "# Setting type / # Default value" comments before every entry and keeps bind order; a Harmony
    //  postfix on ConfigFile.Save rewrites the [Names] section afterwards: one header comment, entries sorted by key.
    // =====================================================================
    internal static class NameTable
    {
        private const string Section = "Names";
        private static ConfigFile _cfg;
        private static bool _hadNamesSection;
        private static readonly Dictionary<string, ConfigEntry<string>> _entries = new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);
        private static bool _dirty;
        private const string Description = "";   // no per-entry text; the section header in the file explains the format
        private const string SectionHeader = "## Display names, one line per prefab: <prefab name> = <name shown in the spawner>. Leave a value empty to regenerate it on the next run.";
        private static bool _patched;

        public static void Init(ConfigFile cfg)
        {
            _cfg = cfg;
            _entries.Clear();
            try { _hadNamesSection = File.Exists(cfg.ConfigFilePath) && File.ReadAllText(cfg.ConfigFilePath).Contains("[" + Section + "]"); }
            catch { _hadNamesSection = false; }
            SpawnerPlugin.Log.LogInfo(_hadNamesSection ? "cfg: [Names] section found" : "cfg: no [Names] section yet, presets will be written");
            if (!_patched)
            {
                try
                {
                    new Harmony(SpawnerPlugin.GUID + ".names").Patch(AccessTools.Method(typeof(ConfigFile), "Save"),
                        postfix: new HarmonyMethod(typeof(NameTable).GetMethod("AfterSave", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                    _patched = true;
                }
                catch (Exception e) { SpawnerPlugin.Log.LogWarning("cfg: could not patch ConfigFile.Save, [Names] will keep BepInEx's layout: " + e.Message); }
            }
            // Every start: bind the keys already in the file in alphabetical order, so the in-game settings list and the
            // saved file follow that order (new items are appended by Resolve and fall into place on the next start).
            try
            {
                int n = 0;
                bool wasSaving = cfg.SaveOnConfigSet;
                cfg.SaveOnConfigSet = false;
                try
                {
                    foreach (var key in ReadFileKeys(cfg.ConfigFilePath).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    {
                        if (_entries.ContainsKey(key)) continue;
                        _entries[key] = cfg.Bind(Section, key, "", Description);   // picks up the value stored in the file
                        n++;
                    }
                }
                finally { cfg.SaveOnConfigSet = wasSaving; }
                SpawnerPlugin.Log.LogInfo("cfg: " + n + " names loaded in alphabetical order");
            }
            catch (Exception e) { SpawnerPlugin.Log.LogWarning("cfg: preload failed: " + e.Message); }
            // tidy the file written by an older version even if nothing gets saved this run
            try { TidyNamesSection(cfg.ConfigFilePath); } catch (Exception e) { SpawnerPlugin.Log.LogWarning("cfg: tidy [Names] failed: " + e.Message); }
        }

        // keys of the [Names] section as they are in the file (already sanitized)
        private static List<string> ReadFileKeys(string path)
        {
            var keys = new List<string>();
            if (!File.Exists(path)) return keys;
            bool inSection = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var l = raw.Trim();
                if (l.StartsWith("[")) { inSection = l.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSection || l.Length == 0 || l.StartsWith("#")) continue;
                int eq = l.IndexOf('=');
                if (eq > 0) keys.Add(l.Substring(0, eq).Trim());
            }
            return keys;
        }

        // Harmony postfix: after BepInEx wrote our config, tidy the [Names] section (sorted, no per-entry comments).
        private static void AfterSave(ConfigFile __instance)
        {
            if (__instance != _cfg) return;
            try { TidyNamesSection(_cfg.ConfigFilePath); }
            catch (Exception e) { SpawnerPlugin.Log.LogWarning("cfg: tidy [Names] failed: " + e.Message); }
        }

        internal static void TidyNamesSection(string path)
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            int start = Array.FindIndex(lines, l => l.Trim().Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0) return;
            int end = start + 1;
            while (end < lines.Length && !lines[end].TrimStart().StartsWith("[")) end++;
            var entries = new List<KeyValuePair<string, string>>();
            for (int i = start + 1; i < end; i++)
            {
                var l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int eq = l.IndexOf('=');
                if (eq < 0) continue;
                entries.Add(new KeyValuePair<string, string>(l.Substring(0, eq).Trim(), l.Substring(eq + 1).Trim()));
            }
            var outLines = new List<string>();
            for (int i = 0; i < start; i++) outLines.Add(lines[i]);
            outLines.Add("[" + Section + "]");
            outLines.Add("");
            outLines.Add(SectionHeader);
            outLines.Add("");
            foreach (var kv in entries.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)) outLines.Add(kv.Key + " = " + kv.Value);
            if (end < lines.Length) outLines.Add("");
            for (int i = end; i < lines.Length; i++) outLines.Add(lines[i]);
            File.WriteAllText(path, string.Join(Environment.NewLine, outLines.ToArray()) + Environment.NewLine);
        }

        /// Display name for a prefab key; registers unknown keys in the config with the generated preset name.
        public static string Resolve(string key, string preset)
        {
            if (string.IsNullOrEmpty(preset)) preset = key;
            var cfgKey = Sanitize(key);
            if (!_entries.TryGetValue(cfgKey, out var entry))
            {
                bool wasSaving = _cfg.SaveOnConfigSet;
                _cfg.SaveOnConfigSet = false;
                try { entry = _cfg.Bind(Section, cfgKey, preset, Description); }
                finally { _cfg.SaveOnConfigSet = wasSaving; }
                _entries[cfgKey] = entry;
                _dirty = true;
            }
            if (string.IsNullOrEmpty(entry.Value))   // empty = regenerate
            {
                bool wasSaving = _cfg.SaveOnConfigSet;
                _cfg.SaveOnConfigSet = false;
                try { entry.Value = preset; } finally { _cfg.SaveOnConfigSet = wasSaving; }
                _dirty = true;
            }
            var v = entry.Value;
            return string.IsNullOrEmpty(v) ? preset : v;
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
