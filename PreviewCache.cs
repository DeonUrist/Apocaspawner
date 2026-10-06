using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apocaspawner
{
    /// <summary>Shared, persistent model thumbnails. Main thread only; returned textures belong to the cache.</summary>
    public static class PreviewCache
    {
        public const int ContractVersion = 1;
        private const int Layer = 31, MaxResident = 64;
        private static readonly Dictionary<string, Texture2D> Memory = new Dictionary<string, Texture2D>();
        private static readonly LinkedList<string> Recent = new LinkedList<string>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static string _fingerprint;
        public static string DirectoryPath { get; private set; }
        internal static void Initialize()
        {
            DirectoryPath = Path.Combine(Paths.CachePath, "Apocalypter", "ModelPreviews", "v1");
            try { Directory.CreateDirectory(DirectoryPath); } catch (Exception e) { SpawnerPlugin.Log.LogWarning("Preview disk cache unavailable: " + e.Message); }
        }
        private static string Fingerprint()
        {
            if (_fingerprint != null) return _fingerprint;
            string game = Path.Combine(Paths.GameRootPath, "Apocalypter_Data", "data.unity3d");
            _fingerprint = "preview-v3-no-adjustment-markers|" + (File.Exists(game) ? new FileInfo(game).Length + ":" + File.GetLastWriteTimeUtc(game).Ticks : Application.version);
            _fingerprint += "|" + string.Join("|", Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID).Select(p => p.Metadata.GUID + ":" + p.Metadata.Version));
            return _fingerprint;
        }
        internal static bool Visible(Renderer r, Transform root)
        {
            if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) return false;
            if (r.gameObject.layer == LayerMask.NameToLayer("HingeAdjustVisible")) return false;
            for (var t = r.transform; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t.name.Equals("hingeAdjustVisibility", StringComparison.OrdinalIgnoreCase) || t.name.Equals("HingesVisible", StringComparison.OrdinalIgnoreCase)
                    || t.name.StartsWith("PartAdjustment.SuspensionIndicator", StringComparison.Ordinal)) return false;
            }
            return true;
        }
        private static Mesh MeshOf(Renderer r)
        {
            var skin = r as SkinnedMeshRenderer;
            return skin != null ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        }
        /// <summary>Stable content key shared across consumers, scenes and sessions.</summary>
        public static string GetKey(GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            var b = new StringBuilder(Fingerprint()).Append('|').Append(Catalog.BaseName(prefab.name));
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r, prefab.transform)))
            {
                var mesh = MeshOf(r); if (mesh == null) continue;
                b.Append('|').Append(r.name).Append(':').Append(mesh.name).Append(':').Append(mesh.vertexCount).Append(':').Append(mesh.subMeshCount);
                foreach (var mat in r.sharedMaterials) if (mat != null)
                {
                    b.Append(':').Append(Catalog.BaseName(mat.name)).Append(':').Append(mat.mainTexture != null ? mat.mainTexture.name : "");
                    if (mat.HasProperty("_Color")) { var color = mat.color; b.Append(':').Append(color.r.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(color.g.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(color.b.ToString("R", CultureInfo.InvariantCulture)); }
                }
                var matrix = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int n = 0; n < 16; n++) b.Append(':').Append(matrix[n].ToString("0.####", CultureInfo.InvariantCulture));
            }
            using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())).Select(v => v.ToString("x2")));
        }
        public static string GetFilePath(GameObject prefab)
        {
            if (DirectoryPath == null) Initialize();
            return Path.Combine(DirectoryPath, GetKey(prefab) + ".png");
        }
        public static bool TryGetCached(GameObject prefab, out Texture2D texture)
        {
            texture = null; if (prefab == null) return false;
            return TryLoad(GetKey(prefab), out texture);
        }
        private static bool TryLoad(string key, out Texture2D texture)
        {
            if (Memory.TryGetValue(key, out texture) && texture != null) { Touch(key); return true; }
            if (DirectoryPath == null) Initialize();
            var path = Path.Combine(DirectoryPath, key + ".png");
            if (!File.Exists(path)) return false;
            Texture2D loaded = null;
            try
            {
                loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(loaded, File.ReadAllBytes(path), false)) throw new IOException("Invalid PNG");
                texture = loaded; Remember(key, texture); return true;
            }
            catch (Exception e) { if (loaded != null) UnityEngine.Object.Destroy(loaded); SpawnerPlugin.Log.LogWarning("Cached preview could not be read: " + e.Message); return false; }
        }
        public static Texture2D GetOrCreate(GameObject prefab)
        {
            if (prefab == null) return null;
            string key = GetKey(prefab); Texture2D texture;
            if (TryLoad(key, out texture)) return texture;
            if (Failed.Contains(key)) return null;
            try
            {
                texture = Render(prefab);
                if (texture == null) { Failed.Add(key); return null; }
                Remember(key, texture);
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    var path = Path.Combine(DirectoryPath, key + ".png"); var temporary = path + ".new";
                    File.WriteAllBytes(temporary, ImageConversion.EncodeToPNG(texture));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
                catch (Exception e) { SpawnerPlugin.Log.LogWarning("Preview retained in memory; disk write failed: " + e.Message); }
                return texture;
            }
            catch (Exception e) { Failed.Add(key); SpawnerPlugin.Log.LogWarning("Model preview " + prefab.name + ": " + e.Message); return null; }
        }
        private static void Touch(string key) { Recent.Remove(key); Recent.AddLast(key); }
        private static void Remember(string key, Texture2D texture)
        {
            Memory[key] = texture; Touch(key);
            while (Recent.Count > MaxResident) { string oldest = Recent.First.Value; Recent.RemoveFirst(); if (Memory.TryGetValue(oldest, out var old) && old != null) UnityEngine.Object.Destroy(old); Memory.Remove(oldest); }
        }
        /// <summary>Drop resident textures without deleting persistent PNGs (e.g. before reusing them in another menu).</summary>
        public static void ReleaseMemory()
        {
            foreach (var texture in Memory.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            Memory.Clear(); Recent.Clear(); Failed.Clear();
        }
        private static Texture2D Render(GameObject prefab)
        {
            // Rebuild only visual components. Never instantiate the gameplay prefab: no Awake, AI, physics or save code executes.
            var root = new GameObject("Apocaspawner.Preview") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
            root.transform.position = new Vector3(0, -100000, 0);
            var owned = new List<UnityEngine.Object>(); var prior = RenderTexture.active; RenderTexture target = null; Texture2D output = null;
            try
            {
                foreach (var source in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (!Visible(source, prefab.transform)) continue;
                    var mesh = MeshOf(source); if (mesh == null) continue;
                    var skin = source as SkinnedMeshRenderer;
                    if (skin != null)
                    {
                        mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; skin.BakeMesh(mesh); mesh.RecalculateBounds(); owned.Add(mesh);
                        if (mesh.vertexCount == 0) continue;
                    }
                    var visual = new GameObject(source.name) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                    visual.transform.SetParent(root.transform, false);
                    // A transform-only chain preserves nested rotation and nonuniform scale, including sheared vehicle attachments.
                    var chain = new Stack<Transform>(); for (var t = source.transform; t != null && t != prefab.transform; t = t.parent) chain.Push(t);
                    var parent = root.transform;
                    while (chain.Count > 0)
                    {
                        var original = chain.Pop(); var node = new GameObject("Pose") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                        node.transform.SetParent(parent, false); node.transform.localPosition = original.localPosition; node.transform.localRotation = original.localRotation; node.transform.localScale = original.localScale; parent = node.transform;
                    }
                    visual.transform.SetParent(parent, false);
                    visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = visual.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = source.sharedMaterials.Select(m =>
                    {
                        if (m == null) return null;
                        var copy = new Material(m) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(copy); return copy;
                    }).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                root.transform.localScale = prefab.transform.lossyScale;
                var renderers = root.GetComponentsInChildren<MeshRenderer>(); if (renderers.Length == 0) return null;
                var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                if (bounds.size.sqrMagnitude < .000001f) return null;
                var direction = new Vector3(2.2f, .8f, 3.5f).normalized;
                var viewRotation = Quaternion.LookRotation(-direction, Vector3.up); var inverse = Quaternion.Inverse(viewRotation);
                var viewBounds = new Bounds(Vector3.zero, Vector3.zero);
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    viewBounds.Encapsulate(inverse * Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                // Portrait framing gives tall NPCs/items useful size beside a parameter list; wide models retain landscape framing.
                bool portrait = viewBounds.size.x < viewBounds.size.y * .85f;
                int width = portrait ? 320 : 512, height = portrait ? 512 : 384;
                float radius = Mathf.Max(.1f, bounds.extents.magnitude), aspect = width / (float)height;
                var cameraObject = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave, layer = Layer }; cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << Layer; camera.orthographic = true; camera.aspect = aspect; camera.orthographicSize = Mathf.Max(viewBounds.extents.y, viewBounds.extents.x / aspect) * 1.12f;
                camera.nearClipPlane = .01f; camera.farClipPlane = radius * 8 + 1; camera.allowHDR = false; camera.allowMSAA = true;
                camera.transform.position = bounds.center + direction * (radius * 3 + .1f); camera.transform.rotation = viewRotation;
                AddLight(root, bounds.center + (direction + Vector3.up) * radius * 2, radius * 8, 1.8f, new Color(1f, .93f, .8f));
                AddLight(root, bounds.center + new Vector3(-3, 1, -1) * radius, radius * 8, 1.1f, new Color(.75f, .85f, 1f));
                target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                output = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply(); camera.targetTexture = null;
                var result = output; output = null; return result;
            }
            finally
            {
                RenderTexture.active = prior; if (target != null) RenderTexture.ReleaseTemporary(target);
                if (output != null) UnityEngine.Object.Destroy(output);
                root.SetActive(false); UnityEngine.Object.Destroy(root); foreach (var obj in owned) UnityEngine.Object.Destroy(obj);
            }
        }
        private static void AddLight(GameObject root, Vector3 position, float range, float intensity, Color color)
        {
            var go = new GameObject("Light") { hideFlags = HideFlags.HideAndDontSave, layer = Layer }; go.transform.SetParent(root.transform, false); go.transform.position = position;
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.cullingMask = 1 << Layer; light.range = range; light.intensity = intensity; light.color = color; light.shadows = LightShadows.None;
        }
    }
}
