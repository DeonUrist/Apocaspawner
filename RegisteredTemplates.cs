using System.Collections.Generic;
using ES3Internal;
using UnityEngine;

namespace Apocaspawner
{
    // Snapshot exact source references published through the game's existing registries.
    // An ES3Prefab component on a live clone does not make that clone a template.
    internal sealed class RegisteredTemplates
    {
        private readonly HashSet<GameObject> _sources = new HashSet<GameObject>();
        private readonly HashSet<GameObject> _instances = new HashSet<GameObject>();
        private readonly HashSet<long> _prefabIds = new HashSet<long>();

        internal RegisteredTemplates()
        {
            foreach (var manager in Resources.FindObjectsOfTypeAll<ES3ReferenceMgrBase>())
            {
                if (manager == null || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded || manager.prefabs == null) continue;
                foreach (var prefab in manager.prefabs)
                    if (prefab != null) _sources.Add(prefab.gameObject);
            }

            foreach (var proxy in Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>())
            {
                if (proxy == null || proxy.gameObject.name != "NewGO_ArrayList" || !proxy.gameObject.scene.IsValid() || !proxy.gameObject.scene.isLoaded) continue;
                if (proxy.referenceName == "ItemList")
                {
                    if (proxy.arrayList != null)
                        foreach (var value in proxy.arrayList) { var go = value as GameObject; if (go != null) _sources.Add(go); }
                    if (proxy.preFillGameObjectList != null)
                        foreach (var go in proxy.preFillGameObjectList) if (go != null) _sources.Add(go);
                }
                else if ((proxy.referenceName == "ArrayList_Items" || proxy.referenceName == "ArrayList_Cars") && proxy.arrayList != null)
                    foreach (var value in proxy.arrayList) { var go = value as GameObject; if (go != null) _instances.Add(go); }
            }
            // Owned save-tracked objects remain instances even if a provider also put them in a source registry.
            _sources.ExceptWith(_instances);
            foreach (var go in _sources)
            {
                var prefab = go.GetComponent<ES3Prefab>();
                if (prefab != null && prefab.prefabId != 0) _prefabIds.Add(prefab.prefabId);
            }
        }

        internal bool Contains(GameObject go) { return go != null && _sources.Contains(go); }

        internal bool IsInstance(GameObject go)
        {
            if (go == null || !go.scene.IsValid() || Contains(go)) return false;
            if (_instances.Contains(go)) return true;
            var prefab = go.GetComponent<ES3Prefab>();
            return prefab != null && prefab.prefabId != 0 && _prefabIds.Contains(prefab.prefabId);
        }
    }
}
