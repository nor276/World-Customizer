# World Customizer

A TerraTech code mod that exposes terrain-generation parameters to the player
at world creation, and a subset of safe-to-change settings during play.

## Layout

```
WorldCustomizer/
├── README.md                    This file
├── FIELDS.md                    Per-field engine target + implementation reference
├── WorldCustomizer.sln
├── lib/
│   └── 0Harmony.dll             Vendored Harmony runtime (used at build time)
├── WorldCustomizer/             The mod project
│   ├── WorldCustomizer.csproj
│   ├── KickStart.cs
│   ├── Settings.cs              Settings model + Sanitize/Clone
│   ├── SettingsStore.cs         Promote/Discard + JSON sidecar load/save
│   ├── WorldCustomizerMod.cs    ModBase entry point (loader-facing)
│   ├── Patches/                 Harmony patches by category
│   │   ├── NewGameHook.cs
│   │   └── Generation/          Tier-1/Tier-2 apply, scenery, atmosphere, etc.
│   ├── UI/                      Settings popup (IMGUI fallback + native uGUI)
│   ├── Reflect.cs               Reflection helpers
│   └── SettingsSelfTest.cs      Round-trip + clamp tests, runs at init
├── workshop-staging/            Build-populated. Mirrors the Workshop folder layout.
└── workshop-assets/             Source assets used by the staged folder
                                 (preview.png, WorldCustomizer_bundle when present)
```

## Build

```
dotnet build -c Release
```

Default paths assume a standard Steam install of TerraTech. Override with
MSBuild properties if needed:

```
dotnet build -c Release \
  -p:GameDir="<path>\TerraTechWin64_Data\Managed" \
  -p:HarmonyDir="<path containing 0Harmony.dll>"
```

`0Harmony.dll` is vendored in `lib/` so the build doesn't require any external
mod to be installed. Update by replacing that file.

After build:

* `workshop-staging/` contains the Workshop-publishable payload.
* `Documents\My Games\TerraTech\Mods\WorldCustomizer\` receives a mirror copy
  for local-development testing.

## Development testing

The mod can be exercised in either of two ways. They are mutually exclusive —
running both at once registers the same ModBase twice and Harmony patches
collide.

### Option 1 — Workshop folder hijack (current dev approach)

The fastest loop: drop `WorldCustomizer.dll` and `0Harmony.dll` into an
existing subscribed code-only mod's workshop folder, rename the other mod's
DLL to `.bak` to neutralize it, and let TerraTech load our DLL alongside the
neighbour's `_bundle` marker. No Unity / no AssetBundle authoring required.

This is what's wired up by default — see the `until cp …` snippets in the
git history for the manual deploy commands. Steam can re-sync workshop
folders at any time, so this is *development only*; the trade-off is the
zero-friction setup.

### Option 2 — Local mods folder (Steam-immune)

`Documents\My Games\TerraTech\Mods\WorldCustomizer\` is untouched by Steam,
but requires a `WorldCustomizer_bundle` marker file to register with the
loader. That bundle has to be authored in Unity Editor with TerraTech's
ModTool (Unity 2018.4.13f1 + Payload Studios' modding unitypackage). Once
the bundle is in `workshop-assets/`, the build's `StageWorkshopBuild` target
copies it into the local folder automatically.

## Verifying a build is live

After launching TerraTech, the init log line appears in
`%LOCALAPPDATA%Low\Payload\TerraTech\output_log.txt` (note: `LocalLow`, not
`Local`, and not under the Steam directory):

```
[World Customizer 0.1.0] init complete
```

If the line is missing, the mod folder isn't being discovered (no bundle, or
the folder isn't in a location the loader scans).

## Workshop publish

Not yet performed. Producing a valid `WorldCustomizer_bundle` via Unity
Editor + Payload Studios' ModTool is the prerequisite.

## Status

v0.1.0. Working in-game via the dev-workflow hijack.

Settings exposed in the **native** popup:

* Tier-1 (creation-locked, geometry-affecting): biome size, biome chaos,
  biome edge sharpness, regions toggle, height multiplier.
* Tier-2 (live-tunable): resource density, resource yield, drill speed,
  AutoMiner speed, atmosphere density.

Additionally present in the **Settings model and the IMGUI fallback popup**,
but not in the native popup:

* Heightmap detail and splat detail (choice rows — implementing them in
  native uGUI was deferred; they'd need a multi-button row pattern).
* Tile resolution and world cell scale (documented as no-op v0.1 because
  changing them at runtime breaks static-cache invariants in the engine).

Other working pieces:

* Settings popup on every new-game start (native uGUI on top of cloned
  options-screen row prefabs; IMGUI used as a fallback if template
  discovery fails).
* JSON sidecar persistence per save (`<savepath>.worldgen.json`).
* Per-tile scenery density rebalance (multi-wave expansion with clutter
  stripping) — the resource-density slider actually changes scatter density
  uniformly across biomes, not just rare-overlay layers.
* Reciprocal biome-size mapping: slider 1.0 = vanilla, 2.0 = 2× bigger
  biomes, 0.5 = half-size. Engine value clamped under the white-landscape
  threshold.

What's missing for a public Workshop release:

* A real `WorldCustomizer_bundle` (currently the dev workflow piggybacks on
  a neighbour's bundle).
* A 512×512 `preview.png`.
* First-time upload via the in-game Mods uploader.
