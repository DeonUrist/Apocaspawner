using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Apocaspawner
{
    internal sealed class NpcReplacement
    {
        internal string Name, BaseName, Display, Faction, Mod;
        internal Func<GameObject, bool> Convert, ConvertPreview;
        internal Action<GameObject> AfterSpawn;
    }

    internal static class Replacements
    {
        internal static readonly Dictionary<string, NpcReplacement> Registered = new Dictionary<string, NpcReplacement>(StringComparer.OrdinalIgnoreCase);
        private sealed class Preview
        {
            internal GameObject Source, Visual;
            internal NpcReplacement Replacement;
            internal readonly List<Material> Materials = new List<Material>();
            internal bool Failed;
            internal void Dispose()
            {
                if (Visual != null) UnityEngine.Object.Destroy(Visual);
                foreach (var material in Materials) if (material != null) UnityEngine.Object.Destroy(material);
            }
        }
        private static readonly Dictionary<string, Preview> Previews = new Dictionary<string, Preview>(StringComparer.OrdinalIgnoreCase);
        private static bool _womenRegistered;

        internal static void Register(NpcReplacement replacement)
        {
            Release(replacement.Name); Registered[replacement.Name] = replacement; Catalog.Invalidate();
        }
        internal static void Unregister(string name)
        { Registered.Remove(name); Release(name); Catalog.Invalidate(); }
        private static void Release(string name)
        { if (Previews.TryGetValue(name, out var preview)) { preview.Dispose(); Previews.Remove(name); } }
        internal static void SceneChanged()
        { foreach (var preview in Previews.Values) preview.Dispose(); Previews.Clear(); }

        private static void RegisterWomenOfWasteland()
        {
            if (_womenRegistered) return;
            if (!Chainloader.PluginInfos.TryGetValue("com.denis.apocalypter.womenofwasteland", out var plugin) || ReferenceEquals(plugin.Instance, null)) return;
            var type = plugin.Instance.GetType().Assembly.GetType("WomenOfWasteland.Gungirl");
            var method = type?.GetMethod("MakeGungirl", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            if (method == null || method.ReturnType != typeof(bool)) return;
            var convert = (Func<GameObject, bool>)Delegate.CreateDelegate(typeof(Func<GameObject, bool>), method);
            // Voice references may not be initialized during the inactive model conversion. Refresh them once the spawned FSMs are live.
            var voice = plugin.Instance.GetType().Assembly.GetType("WomenOfWasteland.Voice")?.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(GameObject) }, null);
            var finish = voice != null ? (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), voice) : null;
            if (!Registered.ContainsKey("Gungirl")) Registered["Gungirl"] = new NpcReplacement { Name = "Gungirl", BaseName = "Flexa", Display = "Gungirl", Mod = ModNames.Label(plugin), Convert = convert, ConvertPreview = convert, AfterSpawn = finish };
            if (!Registered.ContainsKey("Shoota")) Registered["Shoota"] = new NpcReplacement { Name = "Shoota", BaseName = "Sprokka", Display = "Shoota", Mod = ModNames.Label(plugin), Convert = convert, ConvertPreview = convert, AfterSpawn = finish };
            _womenRegistered = true;
            SpawnerPlugin.Log.LogInfo("Women of Wasteland integration: Gungirl and Shoota registered as explicit NPC replacements");
        }
        internal static void AddEntries(Dictionary<string, Entry> byKey)
        {
            try { RegisterWomenOfWasteland(); }
            catch (Exception e) { SpawnerPlugin.Log.LogWarning("Replacement integration unavailable: " + e.Message); }
            foreach (var replacement in Registered.Values.ToArray())
            {
                if (NpcCatalog.IsBackup(replacement.Name) || Api.RegisteredNpcs.ContainsKey(replacement.Name)) continue;
                if (!byKey.TryGetValue(replacement.BaseName, out var source) || !source.IsNpc || source.Prefab == null) continue;
                // Always instantiate the base prefab, rather than cloning a possibly injured live replacement.
                byKey[replacement.Name] = new Entry { Key = replacement.Name, Display = NameTable.Resolve(replacement.Name, replacement.Display),
                    Prefab = source.Prefab, Category = replacement.Faction ?? source.Category, IsNpc = true, IsAsset = source.IsAsset, Replacement = replacement, Mod = replacement.Mod };
            }
        }
        internal static void Apply(Entry entry, GameObject instance)
        {
            if (entry.Replacement == null) return;
            if (!entry.Replacement.Convert(instance)) throw new InvalidOperationException(entry.Display + " conversion failed; check the replacement mod's model and texture settings.");
        }
        internal static void Complete(Entry entry, GameObject instance)
        {
            try { entry.Replacement?.AfterSpawn?.Invoke(instance); }
            catch (Exception e) { SpawnerPlugin.Log.LogWarning("Replacement post-spawn setup " + entry.Key + ": " + e.Message); }
        }
        internal static GameObject PreviewModel(Entry entry)
        {
            if (entry.Replacement == null) return entry.Prefab;
            if (Previews.TryGetValue(entry.Key, out var previous))
            {
                if (previous.Source == entry.Prefab && ReferenceEquals(previous.Replacement, entry.Replacement) && (previous.Visual != null || previous.Failed)) return previous.Visual;
                Release(entry.Key);
            }
            var preview = new Preview { Source = entry.Prefab, Replacement = entry.Replacement }; Previews[entry.Key] = preview;
            try
            {
                preview.Visual = VisualCopy(entry.Prefab, preview.Materials);
                if (!entry.Replacement.ConvertPreview(preview.Visual)) throw new InvalidOperationException("The replacement preview could not be applied.");
                preview.Visual.name = entry.Key;
                return preview.Visual;
            }
            catch (Exception e)
            {
                preview.Dispose(); preview.Visual = null; preview.Failed = true;
                SpawnerPlugin.Log.LogWarning("Replacement preview " + entry.Key + ": " + e.Message);
                return null;
            }
        }
        private static GameObject VisualCopy(GameObject source, List<Material> owned)
        {
            // Copy only transforms, skeletons and mesh renderers. No prefab instantiation, scripts, AI, colliders or save components.
            var root = new GameObject(Catalog.BaseName(source.name)) { hideFlags = HideFlags.HideAndDontSave, layer = source.layer };
            root.SetActive(false); root.tag = source.tag; root.transform.localScale = source.transform.localScale;
            UnityEngine.Object.DontDestroyOnLoad(root);
            var map = new Dictionary<Transform, Transform> { [source.transform] = root.transform };
            var materials = new Dictionary<Material, Material>();
            try
            {
                foreach (var transform in source.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == source.transform) continue;
                    var node = new GameObject(transform.name) { hideFlags = HideFlags.HideAndDontSave, layer = transform.gameObject.layer };
                    node.transform.SetParent(map[transform.parent], false); node.transform.localPosition = transform.localPosition;
                    node.transform.localRotation = transform.localRotation; node.transform.localScale = transform.localScale;
                    node.SetActive(transform.gameObject.activeSelf); map[transform] = node.transform;
                }
                Func<Material, Material> copyMaterial = material =>
                {
                    if (material == null) return null;
                    if (!materials.TryGetValue(material, out var copy))
                    { copy = new Material(material) { name = material.name, hideFlags = HideFlags.HideAndDontSave }; materials[material] = copy; owned.Add(copy); }
                    return copy;
                };
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    Renderer copy;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        var target = map[renderer.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                        target.sharedMesh = skin.sharedMesh;
                        target.bones = skin.bones.Select(b => b != null && map.TryGetValue(b, out var bone) ? bone : null).ToArray();
                        target.rootBone = skin.rootBone != null && map.TryGetValue(skin.rootBone, out var rootBone) ? rootBone : null;
                        target.localBounds = skin.localBounds; target.updateWhenOffscreen = true;
                        if (skin.sharedMesh != null) for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) target.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                        copy = target;
                    }
                    else if (renderer is MeshRenderer)
                    {
                        var filter = renderer.GetComponent<MeshFilter>(); if (filter?.sharedMesh == null) continue;
                        map[renderer.transform].gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        copy = map[renderer.transform].gameObject.AddComponent<MeshRenderer>();
                    }
                    else continue;
                    copy.sharedMaterials = renderer.sharedMaterials.Select(copyMaterial).ToArray(); copy.enabled = renderer.enabled;
                }
                return root;
            }
            catch { UnityEngine.Object.Destroy(root); throw; }
        }
    }
}
