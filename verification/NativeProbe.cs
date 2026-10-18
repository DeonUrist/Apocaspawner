using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Apocaspawner;
using BepInEx;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[BepInPlugin("com.denis.apocaspawner.native-verifier", "Spawner Native Verifier", "0.1.0")]
[BepInDependency(SpawnerPlugin.GUID)]
[BepInDependency("local.cassettecatalog", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class NativeProbe : BaseUnityPlugin
{
    private void Awake()
    {
        string argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-spawner-output="));
        if (argument == null) return;
        // Preserve the plugin host in this stripped-down fixture, which bypasses the normal
        // game startup flow and omits the user's other mods. Providers must survive scene loading.
        gameObject.hideFlags |= HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
        new Harmony("spawner.native-verifier").Patch(typeof(PlayMakerFSM).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), prefix: new HarmonyMethod(typeof(NativeProbe), nameof(PreventGameplay)));
        var host = new GameObject("Spawner verifier") { hideFlags = HideFlags.HideAndDontSave }; DontDestroyOnLoad(host);
        if (Environment.GetCommandLineArgs().Contains("-spawner-templates"))
        {
            host.AddComponent<TemplateProbe>().Output = argument.Substring(argument.IndexOf('=') + 1);
            return;
        }
        host.AddComponent<NativeRunner>().Output = argument.Substring(argument.IndexOf('=') + 1);
    }
    private static bool PreventGameplay() { return false; }
}
public sealed class NativeRunner : MonoBehaviour
{
    public string Output;
    private readonly List<string> lines = new List<string>(), errors = new List<string>();
    private readonly BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private Assembly asm;
    private Component ui;
    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output); Application.runInBackground = true;
        Application.logMessageReceived += (s, stack, type) => { if ((type == LogType.Error || type == LogType.Exception) && (s.Contains("preview") || s.Contains("GUI") || stack.Contains("Apocaspawner"))) errors.Add(s + " " + stack); };
        yield return new WaitForSecondsRealtime(2);
        yield return SceneManager.LoadSceneAsync(1);
        yield return new WaitForSecondsRealtime(2);
        try { RunChecks(); } catch (Exception e) { errors.Add(e.ToString()); }
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), lines);
        if (ui != null)
        {
            foreach (string name in new[] { "items", "vehicles", "vehicle-tools", "npc", "other", "extended-parameters" })
            {
                try { PrepareCapture(name); } catch (Exception e) { errors.Add(e.ToString()); }
                yield return new WaitForSecondsRealtime(.8f);
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png")); yield return new WaitForSecondsRealtime(.5f);
            }
        }
        File.WriteAllLines(Path.Combine(Output, "report.txt"), new[] { "Native checks=" + lines.Count, "Native errors=" + errors.Count, "Size=" + Screen.width + "x" + Screen.height }.Concat(errors));
        Application.Quit();
    }
    private void Check(bool pass, string text) { if (!pass) throw new Exception("FAIL: " + text); lines.Add("PASS: " + text); }
    private IList Entries() { return (IList)asm.GetType("Apocaspawner.Catalog").GetMethod("Entries", statics).Invoke(null, null); }
    private object Field(object o, string name) { return o.GetType().GetField(name, instance).GetValue(o); }
    private void Set(object o, string name, object value) { o.GetType().GetField(name, instance).SetValue(o, value); }
    private void RunChecks()
    {
        asm = typeof(SpawnerPlugin).Assembly;
        var catalog = Entries().Cast<object>().ToList();
        File.WriteAllLines(Path.Combine(Output, "catalog.txt"), catalog.Select(e => Field(e, "Key") + " | " + Field(e, "Group") + " | npc=" + Field(e, "IsNpc")));
        Check(catalog.Count > 100, "real item and NPC prefabs discovered");
        var npcs = catalog.Where(e => (bool)Field(e, "IsNpc")).ToArray(); Check(npcs.Length > 10, "real NPC catalog populated");
        File.WriteAllLines(Path.Combine(Output,"npc-physics.txt"),npcs.Select(e=>{
            var go=(GameObject)Field(e,"Prefab");var rb=go.GetComponent<Rigidbody>();
            return go.name+" layer="+go.layer+" gravity="+(rb!=null?rb.useGravity.ToString():"none")+" kinematic="+(rb!=null?rb.isKinematic.ToString():"none")+" collide="+(rb!=null?rb.detectCollisions.ToString():"none")+" "+string.Join(";",go.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).Select(c=>c.GetType().Name+" enabled="+c.enabled+" "+(c is CapsuleCollider?"center="+((CapsuleCollider)c).center+" height="+((CapsuleCollider)c).height:"")));
        }));
        File.WriteAllLines(Path.Combine(Output,"layers.txt"),Enumerable.Range(0,32).Select(i=>i+"="+LayerMask.LayerToName(i)+" actor10ignore="+Physics.GetIgnoreLayerCollision(10,i)));
        foreach (string faction in new[] { "Animals", "Mutants", "Coyotes", "Scrapyard" }) Check(npcs.Any(e => (string)Field(e, "Group") == faction), faction + " discovered from prefab tags");
        Check(npcs.All(e => !((string)Field(e, "Key")).Contains("_Dead")), "corpses excluded from NPC tab");
        Check(npcs.All(e => !((string)Field(e, "Key")).Contains("_BACKUP")), "BACKUP creatures removed from NPC catalog");
        var vehicleFilter = asm.GetType("Apocaspawner.Catalog").GetMethod("IsVehicleEntry",statics);
        Check(catalog.Where(e => new[]{"Vehicles","Trailers","Vehicle Parts","Vehicle Body"}.Contains((string)Field(e,"Group"))).All(e=>(bool)vehicleFilter.Invoke(null,new[]{e})),"all chassis, trailers, parts and body elements routed to VEHICLES");
        Check(catalog.Where(e => new[]{"Toolbox","Repairbox"}.Contains((string)Field(e,"Category"))).All(e=>(bool)vehicleFilter.Invoke(null,new[]{e})),"toolboxes and repair boxes routed to VEHICLES");
        Check(catalog.Where(e=>(bool)Field(e,"IsNpc")).All(e=>!(bool)vehicleFilter.Invoke(null,new[]{e})),"NPCs stay separate from VEHICLES");
        foreach (var entry in new[] { catalog.First(e => (string)Field(e, "Key") == "Sprokka"), catalog.First(e => (string)Field(e, "Key") == "9mm_borz_smg"), npcs.First(e => (string)Field(e, "Group") == "Animals") })
        {
            var prefab = (GameObject)Field(entry, "Prefab"); string key = PreviewCache.GetKey(prefab); var before = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Length;
            var texture = PreviewCache.GetOrCreate(prefab); Check(texture != null, "real model preview: " + prefab.name);
            var pixels = texture.GetPixels32(); Check(pixels.Count(p => p.a > 20) > 400, "preview contains visible model pixels: " + prefab.name);
            Check(File.Exists(PreviewCache.GetFilePath(prefab)), "persistent PNG saved: " + prefab.name);
            Check(ReferenceEquals(texture, PreviewCache.GetOrCreate(prefab)), "second selection reuses resident texture: " + prefab.name);
            Check(before == Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Length, "preview creates no gameplay FSMs: " + prefab.name);
            File.WriteAllBytes(Path.Combine(Output, CatalogName(prefab) + "-model.png"), ImageConversion.EncodeToPNG(texture));
            PreviewCache.ReleaseMemory();
            Check(PreviewCache.TryGetCached(prefab, out var loaded), "disk PNG reused after memory release: " + prefab.name);
            Check(PreviewCache.GetKey(prefab) == key && (loaded.width == 512 || loaded.width == 320), "stable key and decoded dimensions: " + prefab.name);
        }
        var custom = GameObject.CreatePrimitive(PrimitiveType.Capsule); custom.name = "Verifier_CustomNpc"; custom.SetActive(false); custom.GetComponent<Renderer>().sharedMaterial.color = new Color(.45f,.5f,.3f);
        Api.RegisterNpc(custom, "Mod-added test NPC", "UnrecognizedFaction");
        Check(Entries().Cast<object>().Any(e => (string)Field(e, "Key") == custom.name && (string)Field(e, "Group") == "Other"), "custom NPC registration appears under Other");
        var source = catalog.First(e => (string)Field(e, "Key") == "Sprokka");
        var parameterType = asm.GetType("Apocaspawner.Parameters");
        var read = parameterType.GetMethod("Read", statics);
        var rows = (List<SpawnParameter>)read.Invoke(null, new[] { source });
        Check(!rows.Any(r => r.Label == "Armor"), "no fabricated vanilla enemy armor");
        var playerGun = catalog.First(e => (string)Field(e, "Key") == "9mm_borz_smg");
        File.WriteAllLines(Path.Combine(Output, "reload-prefabs.txt"), Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f != null && f.FsmName == "Reload").Select(f => f.gameObject.name + " | " + string.Join(",", f.GetComponents<PlayMakerFSM>().Select(x => x.FsmName))));
        File.WriteAllLines(Path.Combine(Output, "weapon-fsms.txt"), ((GameObject)Field(playerGun, "Prefab")).GetComponents<PlayMakerFSM>().Select(f => f.FsmName));
        var gunRows = (List<SpawnParameter>)read.Invoke(null, new[] { playerGun });
        Check(!gunRows.Any(r=>r.Label=="Buy value"),"Buy value row removed");
        Check(gunRows.Any(r => r.Label == "Effective range" && r.Unit == "m" && r.Source == "Gunplay"), "live optional Gunplay range on item weapon");
        Check(rows.Any(r => r.Label == "Effective range" && r.Source == "Gunplay"), "live optional Gunplay range on armed NPC");
        var knife = catalog.First(e => (string)Field(e, "Key") == "old_knife");
        Check(!((List<SpawnParameter>)read.Invoke(null, new[] { knife })).Any(r => r.Label == "Effective range"), "melee weapons receive no firearm range");
        var rangeField = parameterType.GetField("_range",statics); var provider=rangeField.GetValue(null);
        rangeField.SetValue(null, (Func<int,float>)(kind => 123.45f));
        Check(((List<SpawnParameter>)read.Invoke(null, new[] { playerGun })).Any(r => r.Label == "Effective range" && r.Value == "123.45"), "range values queried live instead of copied defaults");
        rangeField.SetValue(null,null);parameterType.GetField("_resolveAfter",statics).SetValue(null,DateTime.UtcNow.AddMinutes(1));
        Check(!((List<SpawnParameter>)read.Invoke(null, new[] { playerGun })).Any(r=>r.Label=="Effective range"),"missing Gunplay provider omits unavailable range");
        rangeField.SetValue(null,provider);parameterType.GetField("_resolveAfter",statics).SetValue(null,DateTime.MinValue);
        File.WriteAllLines(Path.Combine(Output, "parameters.txt"), rows.Concat(gunRows).Select(r => r.Section + " | " + r.Label + " | " + r.Value + " " + r.Unit));
        Api.RegisterParameterProvider("Verifier", (go, npc) => Enumerable.Range(0, 16).Select(n => new SpawnParameter("Additional property " + n, n.ToString(), n % 2 == 0 ? "m" : "", "Mod parameters", "Verifier")));
        Check(((List<SpawnParameter>)read.Invoke(null, new[] { source })).Count >= rows.Count + 16, "variable provider row counts supported");
        VerifySpawning(source);
        ui = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(b => b != null && b.GetType().FullName == "Apocaspawner.SpawnerUI");
        ui.GetType().GetMethod("Toggle", instance).Invoke(ui, null);
        Set(ui, "_npc", true); Set(ui, "_catalog", Entries()); ui.GetType().GetMethod("Select", instance).Invoke(ui, new[] { source });
        Check(Apocasetter.InputBlocker.Active, "menu blocks game input");
        VerifyKeyboard();
        VerifyClickShield();
    }
    private void VerifyClickShield()
    {
        var shield=asm.GetType("Apocaspawner.InputShield");var mouse=InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));InputSystem.Update();
        Check(mouse.leftButton.ReadValue()>.5f,"mouse-click fixture supplies a real input-system button press");
        Check(!mouse.leftButton.isPressed&&!mouse.leftButton.wasPressedThisFrame,"world mouse-button polling suppressed while spawner is open");
        var action=new HutongGames.PlayMaker.Actions.GetMouseButtonDown{inUpdateOnly=false,storeResult=new FsmBool(true)};action.OnEnter();
        Check(!action.storeResult.Value,"OnEnter mouse polling clears stored world-click state");
        var pick=new HutongGames.PlayMaker.Actions.MousePickEvent{everyFrame=true};pick.OnEnter();
        Check(true,"state-entry mouse picking suppressed before world raycast");
        var host=new GameObject("Click-shield fixture");var proxy=host.AddComponent<PlayMakerMouseEvents>();
        var target=Resources.FindObjectsOfTypeAll<PlayMakerFSM>().First(f=>f.gameObject.name=="Sprokka"&&f.FsmName=="Health");
        bool original=target.Fsm.MouseEvents;target.Fsm.MouseEvents=true;proxy.TargetFSMs.Add(target);
        var sentinel=new GameObject("Last-click sentinel");Fsm.LastClickedObject=sentinel;proxy.OnMouseUp();
        Check(Fsm.LastClickedObject==sentinel,"Unity collider mouse-up callbacks cannot activate world objects");
        shield.GetMethod("Set",statics).Invoke(null,new object[]{false});
        Check(!mouse.leftButton.isPressed,"closing click remains blocked until release");
        shield.GetMethod("Reset",statics).Invoke(null,null);proxy.OnMouseUp();
        Check(Fsm.LastClickedObject==host&&mouse.leftButton.isPressed,"normal world clicks restored outside spawner");
        target.Fsm.MouseEvents=original;shield.GetMethod("Set",statics).Invoke(null,new object[]{true});
        InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();InputSystem.RemoveDevice(mouse);Destroy(host);Destroy(sentinel);
    }
    private void VerifyKeyboard()
    {
        int calls=0;Action observer=()=>calls++;
        SpawnObserver.OnSpawn=observer;
        new Harmony("spawner.keyboard-verifier").Patch(asm.GetType("Apocaspawner.Spawner").GetMethod("Spawn",statics),prefix:new HarmonyMethod(typeof(SpawnObserver),nameof(SpawnObserver.Before)));
        var keyboard=InputSystem.AddDevice<Keyboard>(); var update=ui.GetType().GetMethod("Update",instance);
        Set(ui,"_editing",true);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter));InputSystem.Update();update.Invoke(ui,null);
        Check(calls==0,"typing in a focused text field never triggers spawn key");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();Set(ui,"_editing",false);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter));InputSystem.Update();update.Invoke(ui,null);
        Check(calls==1,"Enter invokes selected spawn while menu is open");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();Set(ui,"_binding",true);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F8));InputSystem.Update();update.Invoke(ui,null);
        Check(!(bool)Field(ui,"_binding")&&calls==1,"key rebinding does not spawn on binding press");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F8));InputSystem.Update();update.Invoke(ui,null);
        Check(calls==2,"rebound F8 invokes spawn");
        Set(ui,"_distanceText","bad");ui.GetType().GetMethod("SpawnSelected",instance).Invoke(ui,null);
        Check(calls==2,"invalid distance prevents spawning");
        Set(ui,"_distanceText","10");Set(ui,"_countText","1");
        typeof(SpawnerPlugin).GetField("SpawnKeyEntry",statics).GetValue(null).GetType().GetProperty("Value").SetValue(typeof(SpawnerPlugin).GetField("SpawnKeyEntry",statics).GetValue(null),Key.Enter,null);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();InputSystem.RemoveDevice(keyboard);SpawnObserver.OnSpawn=null;
    }
    public static class SpawnObserver
    {
        public static Action OnSpawn;
        public static bool Before(){if(OnSpawn==null)return true;OnSpawn();return false;}
    }
    private static string CatalogName(GameObject go) { return new string(go.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()); }
    private void VerifySpawning(object source)
    {
        var player = GameObject.Find("Player"); Check(player != null, "player available for spawn-position test");
        player.transform.position = new Vector3(10, 12000, 30); player.transform.rotation = Quaternion.Euler(0, 37, 0);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Spawner ground fixture";floor.layer=14;floor.transform.position=new Vector3(10,12000,30);floor.transform.localScale=new Vector3(100,.2f,100);Physics.SyncTransforms();
        float floorY=floor.GetComponent<Collider>().bounds.max.y;
        var before = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Select(f => f.gameObject).Distinct().ToArray();
        asm.GetType("Apocaspawner.Spawner").GetMethod("Spawn", statics).Invoke(null, new[] { source, (object)3 });
        var spawned = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Select(f => f.gameObject).Distinct().Where(g => !before.Contains(g) && g.name.StartsWith("Sprokka(Clone)")).ToArray();
        Check(spawned.Length == 3, "quantity 3 creates three NPC roots");
        var direction = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        Check(spawned.All(g => Mathf.Abs(Vector3.Dot(g.transform.position - player.transform.position, direction) - 10) < .05f), "global default distance is 10 m for NPCs");
        Check(spawned.All(g => Vector3.Dot(g.transform.forward, direction) > .99f), "NPCs spawned upright with player heading");
        Check(spawned.All(g=>g.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger&&c.enabled).Min(c=>c.bounds.min.y)>=floorY+.20f),"NPC body colliders placed fully above the ground before physics");
        bool automatic=Physics.autoSimulation;Physics.autoSimulation=false;
        try{for(int i=0;i<75;i++)Physics.Simulate(.02f);}finally{Physics.autoSimulation=automatic;}
        Check(spawned.All(g=>g.GetComponent<Collider>().bounds.min.y>=floorY-.04f),"real NPC rigidbodies settle on a thin ground collider without falling through");
        var registry = GameObject.Find("NewGO_ArrayList");
        Check(registry != null && registry.GetComponents<PlayMakerArrayListProxy>().Any(p => spawned.All(g => p.arrayList.Contains(g))), "NPCs registered in game save-tracked list");
        foreach (var go in spawned) Destroy(go);
        var setting=typeof(SpawnerPlugin).GetField("SpawnDistance",statics).GetValue(null);var property=setting.GetType().GetProperty("Value");property.SetValue(setting,1000f,null);
        bool rejected=false;try{asm.GetType("Apocaspawner.Spawner").GetMethod("Spawn",statics).Invoke(null,new[]{source,(object)1});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}
        property.SetValue(setting,10f,null);Check(rejected,"no-ground spawn rejected instead of creating a falling NPC");Destroy(floor);
    }
    private void PrepareCapture(string name)
    {
        Set(ui, "_status", "Select an entry to inspect it.");
        var entries = Entries().Cast<object>().ToList();
        bool vehicle=name.StartsWith("vehicle");bool npc=name!="items"&&!vehicle;
        Set(ui, "_npc", npc);Set(ui,"_vehicles",vehicle); Set(ui, "_search", ""); Set(ui, "_parameters", new List<SpawnParameter>());
        object selected = name == "items" ? entries.First(e => (string)Field(e, "Key") == "9mm_borz_smg") : name=="vehicles"? entries.First(e=>(bool)Field(e,"IsVehicle")) : name=="vehicle-tools"?entries.First(e=>(string)Field(e,"Category")=="Toolbox") : name == "other" ? entries.First(e => (string)Field(e, "Key") == "Verifier_CustomNpc") : entries.First(e => (string)Field(e, "Key") == "Sprokka");
        Api.UnregisterParameterProvider("Verifier");
        if (name == "extended-parameters") Api.RegisterParameterProvider("Verifier", (go, npc) => Enumerable.Range(0, 14).Select(n => new SpawnParameter("Additional mod property " + n, n.ToString(), "m", "Mod parameters")));
        ui.GetType().GetMethod("Select", instance).Invoke(ui, new[] { selected });
        ((string[])Field(ui, "_categories"))[npc?2:vehicle?1:0] = name == "other" ? "Other" : name == "items" ? "Weapons" : name=="vehicles"?"Vehicles":name=="vehicle-tools"?"Tools":"Scrapyard";
        Set(ui, "_catalog", Entries());
    }
}
