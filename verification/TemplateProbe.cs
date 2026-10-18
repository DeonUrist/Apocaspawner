using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Apocaspawner;
using ES3Internal;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

// Separate-runtime checks; NativeProbe suppresses gameplay FSM callbacks before scene loading.
public sealed class TemplateProbe : MonoBehaviour
{
    public string Output;
    private readonly List<string> results = new List<string>();
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private readonly Assembly asm = typeof(SpawnerPlugin).Assembly;
    private Type Catalog { get { return asm.GetType("Apocaspawner.Catalog"); } }
    private object Field(object entry, string name) { return entry.GetType().GetField(name, Flags).GetValue(entry); }
    private List<object> Entries()
    {
        Catalog.GetMethod("Invalidate", Flags).Invoke(null, null);
        return ((IEnumerable)Catalog.GetMethod("Entries", Flags).Invoke(null, null)).Cast<object>().ToList();
    }
    private void Check(bool pass, string text)
    {
        results.Add((pass ? "PASS: " : "FAIL: ") + text);
        if (!pass) throw new Exception(text);
    }
    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(2);
        yield return SceneManager.LoadSceneAsync(1);
        // Allow the unmodified Weapons Pack's staggered maintenance to publish its templates.
        yield return new WaitForSecondsRealtime(10);
        try { Run(); }
        catch (Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(Output, "template-checks.txt"), results);
        Application.Quit();
    }
    private void Run()
    {
        var cassettePlugin = BepInEx.Bootstrap.Chainloader.PluginInfos["local.cassettecatalog"].Instance;
        Check(cassettePlugin != null, "CassetteCatalog plugin survives startup and scene transition");
        var initial = Entries();
        File.WriteAllLines(Path.Combine(Output, "catalog.txt"), initial.Select(e => Field(e, "Key") + " | " + Field(e, "Group")));
        Check(initial.Count > 100, "vanilla catalogue remains populated");
        foreach (string name in new[] { "Chainsaw", "Flamethrower", "cassette_catalog_blasted_mix", "cassette_catalog_chubby_mix", "cassette_catalog_metal_mix" })
        {
            bool cassette = name.StartsWith("cassette_");
            var matches = initial.Where(e => (string)Field(e, "Key") == name).ToArray();
            Check(matches.Length == 1, name + " discovered once from unmodified provider");
            var entry = matches[0];
            var go = (GameObject)Field(entry, "Prefab");
            Check((string)Field(entry, "Group") == (cassette ? "Cassettes" : "Weapons"), name + " in correct item group");
            if (cassette) Check(((string)Field(entry, "Display")).StartsWith("Cassette ") && !((string)Field(entry, "Display")).Contains("catalog_"), name + " uses the tape display name");
            Check((bool)Field(entry, "IsRegisteredTemplate") && go.transform.parent != null && !go.activeInHierarchy,
                name + " uses the original inactive parented template");
            var parent = go.transform.parent;
            var position = go.transform.localPosition;
            bool active = go.activeSelf;
            var clone = (GameObject)asm.GetType("Apocaspawner.Placement").GetMethod("Create", Flags).Invoke(null,
                new object[] { entry, new Vector3(0, 4000, 0), Quaternion.identity,
                    new Action<GameObject>(g => asm.GetType("Apocaspawner.Spawner").GetMethod("Register", Flags).Invoke(null, new object[] { g, entry })) });
            var proxies = Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>().Where(p => p.gameObject.name == "NewGO_ArrayList").ToArray();
            Check(proxies.Any(p => p.referenceName == "ArrayList_Items" && p.arrayList != null && p.arrayList.Contains(clone)), name + " spawn is save-tracked in ArrayList_Items");
            Check(clone.activeInHierarchy && clone.transform.parent == null && clone != go, name + " spawn is a separate active root");
            Check(go.transform.parent == parent && go.transform.localPosition == position && go.activeSelf == active && !go.activeInHierarchy,
                name + " source transform/activity unchanged after spawning");
            foreach (var proxy in proxies)
                if (proxy.referenceName == "ArrayList_Items" && proxy.arrayList != null) proxy.arrayList.Remove(clone);
            clone.name = name + "_arbitrary_uuid";
            Check(!Entries().Any(e => (GameObject)Field(e, "Prefab") == clone), name + " untracked UUID-named copy excluded by prefab ID");
            DestroyImmediate(clone);
        }
        var holder = new GameObject("Arbitrary provider holder"); holder.SetActive(false);
        var native = initial.First(e => (string)Field(e, "Key") == "9mm_borz_smg");
        var asset = (GameObject)Field(native, "Prefab");
        var manager = Resources.FindObjectsOfTypeAll<ES3ReferenceMgrBase>().First(m => m.gameObject.scene.IsValid() && m.gameObject.scene.isLoaded);
        var itemList = Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>().First(p => p.gameObject.name == "NewGO_ArrayList" && p.referenceName == "ItemList");
        var saveList = Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>().First(p => p.gameObject.name == "NewGO_ArrayList" && p.referenceName == "ArrayList_Items");
        var es3Source = Make(asset, holder, "Verifier_ES3Source", 910000001);
        manager.AddPrefab(es3Source.GetComponent<ES3Prefab>());
        var listSource = Make(asset, holder, "Verifier_ItemListSource", 910000002);
        itemList.arrayList.Add(listSource);
        var prefillSource = Make(asset, holder, "Verifier_PrefillSource", 910000003);
        itemList.preFillGameObjectList.Add(prefillSource);
        var attached = Make(asset, holder, "Verifier_UnregisteredAttachment", 910000004);
        var tracked = Make(asset, holder, "Verifier_TrackedObject", 910000005);
        tracked.transform.SetParent(null, true);
        manager.AddPrefab(tracked.GetComponent<ES3Prefab>()); saveList.arrayList.Add(tracked);
        var nullFsm = new GameObject("Verifier_UninitializedFSM"); nullFsm.SetActive(false); nullFsm.AddComponent<PlayMakerFSM>();
        var entries = Entries();
        foreach (var source in new[] { es3Source, listSource, prefillSource })
            Check(entries.Any(e => (GameObject)Field(e, "Prefab") == source && (string)Field(e, "Group") == "Weapons"), source.name + " discovered without provider-specific integration");
        Check(!entries.Any(e => (GameObject)Field(e, "Prefab") == attached), "unregistered parented attachment excluded");
        Check(!entries.Any(e => (GameObject)Field(e, "Prefab") == tracked), "owned save-tracked root not promoted to template or loose-scene fallback");
        Check(nullFsm.GetComponent<PlayMakerFSM>().Fsm == null, "uninitialized FSM safely skipped by catalogue");
        Check(entries.Any(e => (string)Field(e, "Key") == "9mm_borz_smg"), "vanilla weapon remains available");
    }
    private GameObject Make(GameObject source, GameObject holder, string name, long id)
    {
        var go = Instantiate(source, holder.transform, false); go.name = name;
        var prefab = go.GetComponent<ES3Prefab>() ?? go.AddComponent<ES3Prefab>(); prefab.prefabId = id;
        return go;
    }
}
