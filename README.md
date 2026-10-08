# Apocaspawner

**Item, vehicle, trailer & NPC spawner** for Apocalypter (BepInEx 5 plugin).

Press **F4** to open the Apocapatrol-style spawner. Click a name to inspect it, then press **Enter** or the bottom-right **SPAWN** button.
Both keys are configurable. Spawning uses the game's naming counter and save-tracked item/car registries.

## Features

- **ITEMS / VEHICLES / NPC tabs**, with category/faction on the left, names in the middle, and a wider model/parameter panel on the right.
- Search text is retained when switching tabs. Editable field text is centered horizontally and vertically.
- VEHICLES contains chassis, trailers, vehicle parts, body elements, toolboxes and repair boxes. ITEMS contains the remaining inventory items.
- NPC factions: **Animals, Mutants, Coyotes, Scrapyard, Other**. Unknown/mod-added factions go in Other. BACKUP variants are hidden; corpses remain in ITEMS.
- With Women of Wasteland loaded, **Gungirl** and **Shoota** are explicit entries under NPC / Scrapyard, even before a female NPC has appeared in the world. Selection uses the mod's female appearance for the preview; spawning applies its conversion before activation and refreshes voice setup after activation. Explicit selection is independent of the random FemalePopulation percentage.
- Clicking a name selects it without spawning. World collider mouse callbacks, state-entry mouse picking and button polling are blocked until the window closes and the closing click is released.
- NPCs and vehicles spawn upright on loaded ground; multiple objects use size-aware spacing. NPC placement accounts for the full body collider beneath the prefab pivot and prepares registration/physics before activation. Ground NPCs use continuous collision detection. If ground is unavailable, spawning is refused rather than dropping them into empty space.
- Global spawn distance defaults to **10 m**, shared across all tabs. Quantity defaults to **1**, with direct entry and **− / +** controls (1–50). Footer controls sit in an inset bordered panel matching the main panels.
- The spawn key is shown above SPAWN; click it to rebind. Typing in search/distance/quantity fields never triggers the spawn shortcut. Escape closes the window or cancels a key change.
- Original game rust, metal frame, and menu font, matching Apocapatrol's ledger UI. The window scales to the screen and can be dragged by its title bar.
- A selected model is rendered once and cached as a PNG on this computer; future selections and sessions reuse it. Rendering copies meshes/materials only, without running the prefab's gameplay scripts.
- Vehicle adjustment-tool indicators are excluded from previews; cached previews from older rendering versions are regenerated automatically.
- Compact lists and a model/parameter split keep more information visible at once. Tall models use portrait framing. Parameters use a scrollable label/value list with sections, sources and units. Only actual available data is shown. No guessed armor tiers or filler values; uninitialized Buy value is not displayed.
- With **Gunplay** installed, firearms and armed NPCs show its live **Effective range** in metres. Loose firearm items are linked to their firing models using the game's weaponType value. Melee weapons do not receive firearm ranges.
- Weapon damage also covers melee models, normalized internal names such as folk17/folk_17, and referenced explosion prefabs. Distance-dependent explosion damage is labeled as its maximum.
- Optional parameter providers can add any number of rows; the displayed values refresh while the selection is open.
- **ApocalypterGarage support** (optional): when that mod is installed, its cars, engines and engine parts are read straight from its code and listed in the VEHICLES tab under **Garage Vehicles / Garage Engines / Garage Parts**. They are spawned through ApocalypterGarage's own functions (whole cars with wheels, seats, lamps and their own engine), so they look, work and save exactly like the ones it puts in the world. Turn them off with `[Spawn] ApocalypterGarage` (Apocasetter MODS menu, ON by default). The preview shows the vanilla car or item they are built on.
- Entries added by another mod (ApocalypterGarage cars/parts, Women of Wasteland NPCs, anything registered through the API) show a small **MOD · name version** badge next to their category in the detail panel. API registrations get the name from the calling plugin's BepInEx metadata.
- Original item groups and friendly-name ordering are retained (Grenade/Nuts pinned first; vehicle parts ordered by type/name).
- Friendly item names in the `[Names]` section of the config (generated from the game's item names / prefab names on first
  sight, e.g. `Trailer_Big = Big Trailer`; edit them freely, an emptied value is regenerated).
- Uses game textures/fonts and input blocking from [Apocasetter](../Apocasetter).
- ESC closes the window. The mod shows up in the Apocasetter MODS menu (hotkey, distances, item names).

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (win_x64) and run the game once.
2. Install **Apocasetter** (hard dependency) into `BepInEx\plugins\Apocasetter\`.
3. Copy `Apocaspawner.dll` to `BepInEx\plugins\`.

Config: `BepInEx\config\com.denis.apocalypter.apocaspawner.cfg` — `[Keys] ToggleMenu` (default `F4`), `SpawnSelected` (default `Enter`), `[Spawn] Distance` (default `10`), `[General] Apocasetter`,
`[Spawn] ApocalypterGarage` (default `true`), `[Names]`.
Old ItemDistance/VehicleDistance entries are superseded by the global Distance setting.

## Building

- `dotnet build` (override the game path with `-p:GameDir=...`); the csproj references
  `BepInEx\plugins\Apocasetter\Apocasetter.dll`, so build/install Apocasetter first, or
- `./build.sh` with mono `mcs` (`MANAGED` / `BEPCORE` / `APOCASETTER` env vars).
- Use `dotnet build -c Release -p:DeployToGame=false` to build without installing.

## How spawning works

Item prefabs are discovered at runtime (`Resources.FindObjectsOfTypeAll<PlayMakerFSM>()`, assets = objects not in a scene) by their
`ID` FSM (category) plus optional `ItemName` / `Value` — body panels such as `poloska_hood`, `door_car_1_L` or `metal_plate_1` only carry `ID`, they get a name generated from the prefab name; vehicles by `RpmGear` / `getFuel` / `CrashDamage`; trailers (`Trailer_Big` / `Trailer_Small`) by
`TrailerAttached` (they use the car recipe → `ArrayList_Cars`). Spawn = `Instantiate`, increment the game's
`itemNameID` counter, name it `<prefab>(Clone)<n>`, and add it to the `NewGO_ArrayList` (`ArrayList_Items` / `ArrayList_Cars`).
`toolset_*` / `PartAdjusterTools` (static part meshes) and the `*explode` effect prefabs are blacklisted.

NPC roots are discovered by their Health and Movement/Detection/Attack FSMs and classified using their actual faction tag. Explicit registrations support custom NPCs with different scripting systems. Actors seated inside other actors/vehicles are excluded as standalone scene templates; prefab assets are preferred over live instances.

ApocalypterGarage has no prefabs of its own (it re-dresses vanilla prefabs and keeps a per-save sidecar), so its entries come from its
`Cars.Defs` / `Engines.Defs` / `Items.Defs` lists via reflection and spawn via `Cars.Spawn(def, ground + Cars.Clearance(def), rot, CarKit.Whole)`,
`Engines.Spawn` and `Items.Create`, followed by `Items.Finish` after 2.5 s — our own naming/registration is skipped for them (Garage registers them itself).
If a future ApocalypterGarage version renames any of these, the integration switches itself off with one warning in the log.

## Integration API

All calls must run on Unity's main thread. `Apocaspawner.Api.ContractVersion` and `PreviewCache.ContractVersion` are `1`.

```csharp
Api.RegisterNpc(customPrefab, "Custom guard", "NewFaction"); // appears under NPC / Other
Api.RegisterNpcReplacement("MyVariant", "Bodyguard", ApplyVariant,
    convertPreview: ApplyVariantVisuals, afterSpawn: InitializeVariantVoice);
Api.RegisterParameterProvider("MyMod", (prefab, isNpc) => new[] {
    new SpawnParameter("Effective range", "125", "m", "Combat", "MyMod")
});
// Use UnregisterNpc / UnregisterNpcReplacement / UnregisterParameterProvider when unloading.
```

Provider rows have Section, Label, Value, Unit and Source. Return only rows with available values; providers are isolated so a failing optional mod cannot break the window. Custom NPC prefabs should supply their own ES3/save support if they need persistence.

Replacement mods can use `RegisterNpcReplacement` when their NPC is a converted base prefab rather than a new asset. `convert` receives an inactive gameplay clone and must return true on success. `convertPreview` receives only cloned transforms, skeletons and mesh renderers, with private copies of the original materials; it must not require gameplay components. If omitted, the main converter is used for both. Optional `afterSpawn` runs after activation for voice/FSM setup. Faction and parameters inherit the base NPC, with an optional faction override for catalog grouping. Register the name that your mod recognizes during save restoration. These APIs do not automatically infer arbitrary third-party conversions; such mods must register their replacement metadata. Women of Wasteland is integrated automatically through optional reflection and remains an optional dependency.

`PreviewCache.GetOrCreate(prefab)` returns a shared Texture2D, rendering only on a cache miss. `TryGetCached(prefab, out texture)`, `GetKey(prefab)`, and `GetFilePath(prefab)` let other mod functions reuse the same preview. The cache owns returned textures; do not destroy or modify them. Resident textures may be evicted, so request them again before later reuse.

Persistent previews live in `BepInEx/cache/Apocalypter/ModelPreviews/v1`. Keys include the prefab's visual structure, game-data revision and installed mod versions. The memory cache retains up to 64 previews; its PNGs survive memory eviction and game restarts.

## Verification

`verification/NativeVerifier.csproj` and `verification/setup-runtime.ps1` exercise the real Unity runtime in a separate checkout with gameplay FSMs suppressed. They verify real faction discovery, tab membership, BACKUP exclusion, model pixels, no preview gameplay components, resident/disk reuse, optional Gunplay ranges, custom providers, keyboard/mouse blocking, and NPC spawn distance/registration. Real NPC rigidbodies are simulated on a thin ground collider to verify placement and collision support. Captures cover all tabs and the inset footer. This does not verify NPC combat or a save/load gameplay round trip.
