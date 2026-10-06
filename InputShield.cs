using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Apocaspawner
{
    internal static class InputShield
    {
        private static bool _open, _releasePending;
        private static int _releaseFrame;
        internal static bool Blocking => _open || _releasePending;
        internal static void Set(bool open)
        {
            _open = open; _releasePending = !open; _releaseFrame = Time.frameCount + 1;
        }
        internal static void Reset() { _open = _releasePending = false; }
        internal static void Tick()
        {
            if (_open || !_releasePending) return;
            var mouse = Mouse.current;
            bool held = mouse != null && (mouse.leftButton.ReadValue() > .5f || mouse.rightButton.ReadValue() > .5f || mouse.middleButton.ReadValue() > .5f);
            held |= UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButton(2);
            if (held) _releaseFrame = Time.frameCount + 1;
            else if (Time.frameCount > _releaseFrame) _releasePending = false;
        }
        internal static void Install()
        {
            var harmony = new Harmony(SpawnerPlugin.GUID + ".input-shield"); int count = 0;
            var mousePrefix = new HarmonyMethod(typeof(InputShield), nameof(MouseControl));
            foreach (string property in new[] { "isPressed", "wasPressedThisFrame", "wasReleasedThisFrame" })
            {
                var getter = AccessTools.PropertyGetter(typeof(ButtonControl), property);
                if (getter != null) { harmony.Patch(getter, prefix: mousePrefix); count++; }
            }
            // Unity's collider mouse callbacks bypass Apocasetter's PlayMaker input-action blocker.
            var proxy = typeof(PlayMakerFSM).Assembly.GetType("PlayMakerMouseEvents");
            if (proxy != null) foreach (var method in proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name.StartsWith("OnMouse", StringComparison.Ordinal)))
            { harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputShield), nameof(WorldMouse))); count++; }
            // Polling helpers also run from OnEnter; suppressing only OnUpdate leaves those clicks exposed.
            var input = new Regex(@"^(GetAxis|GetButton|GetKey|GetMouse|MouseLook|MousePick|AnyKey|GetTouch|Input|Mouse|PlayerInput)");
            foreach (var assembly in new[] { typeof(PlayMakerFSM).Assembly, typeof(PlayMakerArrayListProxy).Assembly }.Distinct())
            {
                Type[] types; try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types.Where(t => !t.IsAbstract && typeof(FsmStateAction).IsAssignableFrom(t) && input.IsMatch(t.Name)))
                    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.ReturnType == typeof(void) && (m.Name.StartsWith("DoGet", StringComparison.Ordinal) || m.Name.StartsWith("DoMouse", StringComparison.Ordinal) || m.Name == "OnPerformed" || m.Name == "OnCanceled")))
                    {
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputShield), nameof(InputPoll))); count++;
                    }
            }
            SpawnerPlugin.Log.LogInfo("Spawner click shield: " + count + " mouse callbacks, polling helpers and button getters patched");
        }
        private static bool WorldMouse() { return !Blocking; }
        private static bool MouseControl(ButtonControl __instance, ref bool __result)
        {
            if (!Blocking || !(__instance.device is Mouse)) return true;
            __result = false; return false;
        }
        private static bool InputPoll(FsmStateAction __instance)
        {
            if (!Blocking) return true;
            foreach (var field in __instance.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.FieldType == typeof(FsmBool) && field.Name.StartsWith("store", StringComparison.OrdinalIgnoreCase))
                { var value = field.GetValue(__instance) as FsmBool; if (value != null) value.Value = false; }
            return false;
        }
    }
}
