#!/bin/sh
# Builds Apocaspawner.dll against the game's own libraries + Apocasetter.dll (shared GUI). Usage: ./build.sh [out.dll]
M=${MANAGED:-/e/SteamLibrary/steamapps/common/Apocalypter/Apocalypter_Data/Managed}; B=${BEPCORE:-/e/SteamLibrary/steamapps/common/Apocalypter/BepInEx/core}; A=${APOCASETTER:-/e/SteamLibrary/steamapps/common/Apocalypter/BepInEx/plugins/Apocasetter/Apocasetter.dll}
mcs -nostdlib -noconfig -target:library -langversion:latest -optimize+ -out:${1:-Apocaspawner.dll} \
  -r:$M/mscorlib.dll -r:$M/System.dll -r:$M/System.Core.dll -r:$M/netstandard.dll \
  -r:$B/BepInEx.dll -r:$B/0Harmony.dll \
  -r:$M/UnityEngine.dll -r:$M/UnityEngine.CoreModule.dll -r:$M/UnityEngine.InputLegacyModule.dll -r:$M/UnityEngine.IMGUIModule.dll \
  -r:$M/UnityEngine.PhysicsModule.dll -r:$M/UnityEngine.TextRenderingModule.dll -r:$M/Unity.InputSystem.dll \
  -r:$M/PlayMaker.dll -r:$M/Assembly-CSharp.dll -r:$A \
  Spawner.cs Names.cs
