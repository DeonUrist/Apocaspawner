using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocaspawner
{
    // Fill level of containers, following the game's own save logic instead of guessing from variable names.
    // Every saved item has a root FSM "saveItemVar" whose save state copies the real values out of other FSMs:
    //   GetFsmFloat <LiquidAmount>.Liquid  - cans/barrels (root), engine oil and radiator coolant ("cap" child),
    //                                        stove ("Gasoline"), dew collector and plants ("Water"), vehicle tank ("Fuel")
    //   GetFsmInt   <Ammo>.ammo            - ammo boxes and arrow bundles (root)
    // Many other FSMs carry local copies called Liquid/ammo (UI text, pouring, weapons' magazines, grenades); those are
    // never saved and are left alone. Only an item whose save reads one of these counts as a container.
    // Full = the FSM's LiquidCapacity (prefabs are not always full: the stove holds 0 of 3 L); ammo full = the prefab amount.
    // Empty = 0, except plants, which wither at 0 and get 0.1.
    internal static class Contents
    {
        private const string SaveFsm = "saveItemVar", LiquidVar = "Liquid", CapacityVar = "LiquidCapacity", AmmoVar = "ammo";
        private const float PlantMinimum = .1f;

        internal sealed class Target { internal string Fsm, Var; internal bool Owner, IsInt; }
        internal sealed class Info
        {
            internal List<Target> Targets = new List<Target>();
            internal bool IsAmmo, IsVehicle;
            internal float Full, EmptyValue;
            internal string Label => IsAmmo ? "Rounds (full)" : IsVehicle ? "Fuel capacity" : "Capacity";
            internal string Unit => IsAmmo ? "" : "L";
        }

        private static readonly Dictionary<int, Info> Cache = new Dictionary<int, Info>();
        private static readonly Info None = new Info();

        // What the entry holds when full, or null when it has no saved fill level
        internal static Info Of(Entry e)
        {
            if (e == null || e.Prefab == null || e.IsNpc || e.IsPortableRadio) return null;
            int id = e.Prefab.GetInstanceID();
            if (!Cache.TryGetValue(id, out var info)) Cache[id] = info = Read(e) ?? None;
            return info == None ? null : info;
        }

        internal static void Invalidate() => Cache.Clear();

        private static Info Read(Entry e)
        {
            try
            {
                var save = e.Prefab.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f != null && f.Fsm != null && f.FsmName == SaveFsm);
                if (save == null) return null;
                if (!save.Fsm.Initialized) save.Fsm.InitData();   // serialized action data only; nothing runs
                var info = new Info { IsVehicle = e.IsVehicle || e.GarageKind == GarageBridge.KindCar };
                var state = save.FsmStates?.FirstOrDefault(s => s.Name == "save");
                foreach (var action in state?.Actions ?? new FsmStateAction[0])
                {
                    if (action is GetFsmFloat gf && gf.variableName?.Value == LiquidVar && !string.IsNullOrEmpty(gf.fsmName?.Value))
                        Add(info, new Target { Fsm = gf.fsmName.Value, Var = LiquidVar, Owner = gf.gameObject?.OwnerOption == OwnerDefaultOption.UseOwner });
                    else if (action is GetFsmInt gi && gi.variableName?.Value == AmmoVar && !string.IsNullOrEmpty(gi.fsmName?.Value))
                        Add(info, new Target { Fsm = gi.fsmName.Value, Var = AmmoVar, Owner = gi.gameObject?.OwnerOption == OwnerDefaultOption.UseOwner, IsInt = true });
                }
                if (info.Targets.Count == 0) return null;
                // read the full amount from the prefab itself; drop targets that do not resolve to a real variable
                info.Targets.RemoveAll(t => !Resolve(e.Prefab, t).Any());
                if (info.Targets.Count == 0) return null;
                var first = info.Targets[0];
                info.IsAmmo = first.IsInt;
                info.Full = Resolve(e.Prefab, first).Select(f => FullOf(f, first)).DefaultIfEmpty(0).Max();
                info.EmptyValue = e.Prefab.GetComponents<PlayMakerFSM>().Any(f => f != null && f.FsmName == "Plant") ? PlantMinimum : 0;
                return info;
            }
            catch (Exception ex) { SpawnerPlugin.Log.LogDebug($"Contents {e.Key}: {ex.Message}"); return null; }
        }

        private static void Add(Info info, Target t)
        { if (!info.Targets.Any(x => x.Fsm == t.Fsm && x.Var == t.Var && x.Owner == t.Owner)) info.Targets.Add(t); }

        private static float FullOf(PlayMakerFSM f, Target t)
        {
            if (t.IsInt) return f.FsmVariables.FindFsmInt(t.Var)?.Value ?? 0;
            float cap = f.FsmVariables.FindFsmFloat(CapacityVar)?.Value ?? 0;
            return cap > 0 ? cap : f.FsmVariables.FindFsmFloat(t.Var)?.Value ?? 0;
        }

        // Owner targets live on the root; others on a child of this item. Children that are items of their own
        // (an engine fitted in a Garage car, parts on a chassis) carry their own ID FSM and are skipped.
        private static IEnumerable<PlayMakerFSM> Resolve(GameObject go, Target t)
        {
            var candidates = t.Owner ? go.GetComponents<PlayMakerFSM>() : go.GetComponentsInChildren<PlayMakerFSM>(true).Where(f => f.transform != go.transform && OwnPart(f.transform, go.transform));
            return candidates.Where(f => f != null && f.Fsm != null && f.FsmName == t.Fsm && (t.IsInt ? f.FsmVariables.FindFsmInt(t.Var) != null : f.FsmVariables.FindFsmFloat(t.Var) != null));
        }

        private static bool OwnPart(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.GetComponents<PlayMakerFSM>().Any(f => f != null && f.FsmName == "ID")) return false;
            return t == root;
        }

        // Sets a fresh clone to full or empty. Called while it is still inactive in staging, or right after a mod's spawn call.
        internal static void Apply(GameObject go, Entry e, bool empty)
        {
            var info = Of(e);
            if (go == null || info == null) return;
            var set = new List<string>();
            foreach (var t in info.Targets)
                foreach (var f in Resolve(go, t))
                {
                    float value = Write(f, t, empty, info);
                    set.Add($"{f.FsmName}.{t.Var}={value.ToString("0.##", CultureInfo.InvariantCulture)}");
                }
            if (set.Count == 0) { SpawnerPlugin.Log.LogWarning($"{go.name}: fill variable not found on the clone, spawned as is"); return; }
            Watcher.Queue(go, e, empty);
            SpawnerPlugin.Log.LogInfo($"{(empty ? "Emptied" : "Filled")} {go.name} ({string.Join(", ", set)})");
        }

        private static float Want(PlayMakerFSM f, Target t, bool empty, Info info)
        { return empty ? (t.IsInt ? 0 : info.EmptyValue) : t.IsInt ? info.Full : FullOf(f, t); }

        private static float Write(PlayMakerFSM f, Target t, bool empty, Info info)
        {
            float want = Want(f, t, empty, info);
            if (t.IsInt) f.FsmVariables.FindFsmInt(t.Var).Value = Mathf.RoundToInt(want);
            else f.FsmVariables.FindFsmFloat(t.Var).Value = want;
            return want;
        }

        // Re-applies the chosen level a few times right after activation, in case a start state or a mod's finishing step
        // (ApocalypterGarage's Items.Finish runs 2.5 s after creation) changes it. The window is short enough that it never
        // fights the player.
        private sealed class Watcher : MonoBehaviour
        {
            private static readonly float[] Checks = { 0f, .5f, 3.5f };
            private struct Job { public GameObject Go; public Entry Entry; public bool Empty; public float Start; public int Next; }
            private static Watcher _instance;
            private readonly List<Job> _jobs = new List<Job>();

            internal static void Queue(GameObject go, Entry e, bool empty)
            {
                if (_instance == null)
                {
                    var host = new GameObject("Apocaspawner.ContentsWatcher") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(host);
                    _instance = host.AddComponent<Watcher>();
                }
                _instance._jobs.Add(new Job { Go = go, Entry = e, Empty = empty, Start = Time.time });
            }

            private void Update()
            {
                for (int i = _jobs.Count - 1; i >= 0; i--)
                {
                    var job = _jobs[i];
                    if (job.Go == null) { _jobs.RemoveAt(i); continue; }
                    if (Time.time - job.Start < Checks[job.Next]) continue;
                    try
                    {
                        var info = Of(job.Entry);
                        if (info != null)
                            foreach (var t in info.Targets)
                                foreach (var f in Resolve(job.Go, t))
                                {
                                    float want = Want(f, t, job.Empty, info);
                                    float now = t.IsInt ? f.FsmVariables.FindFsmInt(t.Var).Value : f.FsmVariables.FindFsmFloat(t.Var).Value;
                                    if (Mathf.Abs(now - want) > .001f)
                                    {
                                        Write(f, t, job.Empty, info);
                                        SpawnerPlugin.Log.LogInfo($"{job.Go.name}: {f.FsmName}.{t.Var} changed to {now.ToString("0.##", CultureInfo.InvariantCulture)} after spawn, set back to {want.ToString("0.##", CultureInfo.InvariantCulture)}");
                                    }
                                }
                    }
                    catch (Exception e) { SpawnerPlugin.Log.LogDebug($"Contents check {job.Go.name}: {e.Message}"); }
                    job.Next++;
                    if (job.Next >= Checks.Length) _jobs.RemoveAt(i); else _jobs[i] = job;
                }
            }
        }
    }
}
