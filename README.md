# Apocaspawner

**Item, vehicle & trailer spawner** for Apocalypter (BepInEx 5 plugin).

Press **F4** (configurable) to open a window listing every spawnable item and vehicle in the game, grouped by category, with search.
Spawned objects are registered the same way the game does it, so they save and load normally.

## Features

- Groups on the left (alphabetical), items on the right (alphabetical by display name; vehicle parts by type then name;
  Grenade/Nuts pinned to the top), search box, spawn count.
- Friendly item names in the `[Names]` section of the config (generated from the game's item names / prefab names on first
  sight, e.g. `Trailer_Big = Big Trailer`; edit them freely, an emptied value is regenerated).
- Uses the shared GUI (theme, input blocking, pause-menu detection) from [Apocasetter](../Apocasetter).
- ESC closes the window. The mod shows up in the Apocasetter MODS menu (hotkey, distances, item names).

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (win_x64) and run the game once.
2. Install **Apocasetter** (hard dependency) into `BepInEx\plugins\Apocasetter\`.
3. Copy `Apocaspawner.dll` to `BepInEx\plugins\`.

Config: `BepInEx\config\com.denis.apocalypter.apocaspawner.cfg` — `[Keys] ToggleMenu` (default `F4`), `[Spawn] ItemDistance` / `VehicleDistance`, `[General] Apocasetter`,
`[Names]`.

## Building

- `dotnet build` (override the game path with `-p:GameDir=...`); the csproj references
  `BepInEx\plugins\Apocasetter\Apocasetter.dll`, so build/install Apocasetter first, or
- `./build.sh` with mono `mcs` (`MANAGED` / `BEPCORE` / `APOCASETTER` env vars).

## How spawning works

Item prefabs are discovered at runtime (`Resources.FindObjectsOfTypeAll<PlayMakerFSM>()`, assets = objects not in a scene) by their
`ID` FSM (category) plus optional `ItemName` / `Value` — body panels such as `poloska_hood`, `door_car_1_L` or `metal_plate_1` only carry `ID`, they get a name generated from the prefab name; vehicles by `RpmGear` / `getFuel` / `CrashDamage`; trailers (`Trailer_Big` / `Trailer_Small`) by
`TrailerAttached` (they use the car recipe → `ArrayList_Cars`). Spawn = `Instantiate`, increment the game's
`itemNameID` counter, name it `<prefab>(Clone)<n>`, and add it to the `NewGO_ArrayList` (`ArrayList_Items` / `ArrayList_Cars`).
`toolset_*` / `PartAdjusterTools` (static part meshes) and the `*explode` effect prefabs are blacklisted.
