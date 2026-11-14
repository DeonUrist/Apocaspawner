using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocaspawner
{
    // Fill level of containers. The game keeps it in one FSM variable on the item root (it is what the save stores):
    //   float "Liquid": fuel/water/oil cans and barrels, engine oil, radiator coolant, stove fuel, dew collector, plants,
    //                   and a vehicle chassis's fuel tank (root FSM only, attached parts are left alone)
    //   int   "ammo"  : ammo boxes and arrow bundles
    // Prefabs carry the full amount, so a plain Instantiate is always full. "Empty" zeroes the variable on the clone before
    // activation and re-checks it for a few seconds in case a start state (or ApocalypterGarage's Items.Finish) refills it.
    internal static class Contents
    {
        private static readonly string[] FloatNames = { "Liquid" };
        private static readonly string[] IntNames = { "ammo" };

        internal sealed class Info { internal string Label; internal float Full; }

        // What the prefab holds when full, or null when this entry has no fill level we can empty
        internal static Info Of(Entry e)
        {
            if (e == null || e.Prefab == null || e.IsNpc || e.IsPortableRadio) return null;
            bool vehicle = IsVehicle(e);
            foreach (var f in Fsms(e.Prefab, vehicle))
            {
                foreach (var n in FloatNames) { var v = f.FsmVariables.FindFsmFloat(n); if (v != null) return new Info { Label = vehicle ? "Fuel (as spawned)" : "Contents (full)", Full = v.Value }; }
                foreach (var n in IntNames) { var v = f.FsmVariables.FindFsmInt(n); if (v != null) return new Info { Label = "Rounds (full)", Full = v.Value }; }
            }
            return null;
        }

        internal static bool Applies(Entry e) => Of(e) != null;

        // Chassis and ApocalypterGarage cars: the root's Liquid is the fuel tank
        private static bool IsVehicle(Entry e) => e.IsVehicle || e.GarageKind == GarageBridge.KindCar;

        internal static void Empty(GameObject go, Entry e)
        {
            if (go == null) return;
            bool vehicle = IsVehicle(e);
            var cleared = Clear(go, vehicle);
            if (cleared.Count == 0) { SpawnerPlugin.Log.LogWarning($"{go.name}: no fill variable found, spawned as is"); return; }
            Watcher.Queue(go, cleared, vehicle);
            SpawnerPlugin.Log.LogInfo($"Emptied {go.name} ({string.Join(", ", cleared.Select(c => c.Name + " " + c.Was.ToString("0.##", CultureInfo.InvariantCulture)))} -> 0)");
        }

        private struct Cleared { internal string Name; internal float Was; }

        private static List<Cleared> Clear(GameObject go, bool rootOnly)
        {
            var result = new List<Cleared>();
            foreach (var f in Fsms(go, rootOnly))
            {
                foreach (var n in FloatNames) { var v = f.FsmVariables.FindFsmFloat(n); if (v != null) { result.Add(new Cleared { Name = n, Was = v.Value }); v.Value = 0; } }
                foreach (var n in IntNames) { var v = f.FsmVariables.FindFsmInt(n); if (v != null) { result.Add(new Cleared { Name = n, Was = v.Value }); v.Value = 0; } }
            }
            return result;
        }

        // The variable lives on the item root; fall back to children only when the root has none.
        // Vehicles are root only: their children are attached parts (engine oil, radiator) that the switch must not touch.
        private static IEnumerable<PlayMakerFSM> Fsms(GameObject go, bool rootOnly)
        {
            var root = go.GetComponents<PlayMakerFSM>().Where(f => f != null && f.Fsm != null).ToArray();
            if (rootOnly || root.Any(Has)) return root.Where(Has);
            return go.GetComponentsInChildren<PlayMakerFSM>(true).Where(f => f != null && f.Fsm != null && Has(f));
        }

        private static bool Has(PlayMakerFSM f)
            => FloatNames.Any(n => f.FsmVariables.FindFsmFloat(n) != null) || IntNames.Any(n => f.FsmVariables.FindFsmInt(n) != null);

        // Re-empties a fresh clone if something puts the full prefab amount back shortly after activation.
        // Only a value equal to what we cleared counts as a refill, so the player pouring something in is left alone.
        private sealed class Watcher : MonoBehaviour
        {
            private static readonly float[] Checks = { 0f, .5f, 3.5f };   // next frame, after start states, after Garage's 2.5 s Finish
            private struct Job { public GameObject Go; public List<Cleared> Vars; public bool RootOnly; public float Start; public int Next; }
            private static Watcher _instance;
            private readonly List<Job> _jobs = new List<Job>();

            internal static void Queue(GameObject go, List<Cleared> vars, bool rootOnly)
            {
                if (_instance == null)
                {
                    var host = new GameObject("Apocaspawner.ContentsWatcher") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(host);
                    _instance = host.AddComponent<Watcher>();
                }
                _instance._jobs.Add(new Job { Go = go, Vars = vars, RootOnly = rootOnly, Start = Time.time, Next = 0 });
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
                        // FSM variables can be replaced on init: look them up again by name
                        foreach (var c in job.Vars)
                            foreach (var f in Fsms(job.Go, job.RootOnly))
                            {
                                var fl = f.FsmVariables.FindFsmFloat(c.Name);
                                if (fl != null && fl.Value > 0 && Mathf.Approximately(fl.Value, c.Was)) fl.Value = 0;
                                var it = f.FsmVariables.FindFsmInt(c.Name);
                                if (it != null && it.Value > 0 && it.Value == (int)c.Was) it.Value = 0;
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
