# World Customizer

A TerraTech mod that lets you tune terrain generation and gameplay multipliers
at the start of every new world. Pick your biome size, terrain height,
resource abundance, drill power, and more — settings are saved per world.

## What it does

When you start a new game, a **World Customization** panel appears. From it
you can dial in how the world is generated and how some core gameplay
mechanics behave, then hit Confirm to enter the world with those settings
applied.

Settings split into two groups:

**Geometry — locked after creation.** Affect the shape of the world; can
only be set at world creation because changing them mid-game would corrupt
the save's terrain.

| Setting | Range | Effect |
|---|---|---|
| Biome size | 0.5 – 5.0 | 1.0 = vanilla. 2.0 = biomes twice as big. 0.5 = half size. |
| Biome chaos | 0.0 – 1.0 | 0 = grid-regular biome layout. 1 = full Voronoi chaos. |
| Biome edge sharpness | 0.0 – 3.0 | 0 = soft fading transitions. 3 = razor-sharp biome borders. |
| Enable regions | toggle | Off makes the world a flat chaotic mix instead of the campaign's regional clustering. |
| Height multiplier | 0.1 – 5.0 | Vertical scale of terrain. 0.1 = nearly flat. 5.0 = towering mountains. |

**Gameplay — changeable later.** Multipliers that are safe to retune after
the world exists.

| Setting | Range | Effect |
|---|---|---|
| Resource density | 0.0 – 5.0 | Scales how much scatter (rocks, trees, ore) populates the world. |
| Resource yield | 0.0 – 20.0 | Chunks per surface deposit when mined. |
| Drill speed | 0.0 – 50.0 | Per-hit damage of manual drills. |
| AutoMiner speed | 0.0 – 50.0 | Extraction rate of underground reservoirs. |
| Atmosphere density | 0.0 – 10.0 | Wing lift force. 0 = vacuum (planes drop). 10 = dense (planes fly aggressively). |

Per-world settings are saved alongside the save file in
`<savefile>.worldgen.json`, so each world remembers its own customization.

## Installation

Not yet published to the Steam Workshop. For now, the only install path is
manual:

1. Build the mod from source (see "For developers" below) or grab a release
   `WorldCustomizer.dll` + `0Harmony.dll` from a future release.
2. Place both files in your TerraTech local-mods folder:
   `Documents\My Games\TerraTech\Mods\WorldCustomizer\`
3. You'll also need a Unity-built `WorldCustomizer_bundle` marker file in
   that folder — see "For developers" for how to author it.

## License

MIT — see [LICENSE](LICENSE). Fork it, modify it, republish it under your
own name on the Workshop. Keep the copyright notice in any redistribution.

---

## For developers

### Build

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

`0Harmony.dll` is vendored in `lib/` so the build doesn't require any
external mod to be installed. After build:

- `workshop-staging/` — Workshop-publishable payload
- `Documents\My Games\TerraTech\Mods\WorldCustomizer\` — mirror copy for
  local-development testing

### Layout

```
WorldCustomizer/
├── README.md                    This file
├── LICENSE                      MIT
├── FIELDS.md                    Per-field engine target + implementation reference
├── WorldCustomizer.sln
├── lib/0Harmony.dll             Vendored Harmony runtime (used at build time)
├── WorldCustomizer/             The mod project
│   ├── Settings.cs              Settings model + Sanitize/Clone
│   ├── SettingsStore.cs         Promote/Discard + JSON sidecar load/save
│   ├── WorldCustomizerMod.cs    ModBase entry point (loader-facing)
│   ├── KickStart.cs             Init/DeInit orchestration
│   ├── Patches/
│   │   ├── NewGameHook.cs       Customize-popup trigger
│   │   └── Generation/          Tier-1/Tier-2 apply, scenery, atmosphere, etc.
│   ├── UI/                      Settings popup (native uGUI + IMGUI fallback)
│   ├── Reflect.cs               Reflection helpers
│   └── SettingsSelfTest.cs      Round-trip + clamp tests, runs at init
├── workshop-staging/            Build-populated. Mirrors the Workshop folder layout.
└── workshop-assets/             Source assets (preview.png, _bundle when present)
```

For implementation details of how each user-facing setting maps to engine
fields and Harmony patches, see [FIELDS.md](FIELDS.md).

### Workshop publish prerequisites

The mod isn't on the Workshop yet because the loader (`ManMods`) requires a
Unity AssetBundle marker file (`WorldCustomizer_bundle`) per mod folder.
Producing one requires Unity Editor with TerraTech's ModTool installed:

1. Unity 2018.4.13f1 + the TerraTech ModTool unitypackage (from Payload
   Studios) + Steamworks.NET (pre-v20.0.0).
2. Create an empty 3D Unity project. Import both unitypackages.
3. Resolve any compile errors by enabling built-in packages (Physics,
   Image Conversion, etc.) in Window → Package Manager → Built-in.
4. TerraTech Tools → Mod Designer → `+` → name `WorldCustomizer` → Export
   to your TerraTech install folder.
5. Copy the resulting `WorldCustomizer_bundle` to `workshop-assets/`.
   Builds will then include it in the staged folder automatically.

### Development testing without the bundle

While the bundle is missing, you can still test locally by dropping the
built `WorldCustomizer.dll` + `0Harmony.dll` into a subscribed code-only
mod's workshop folder and neutralizing the original DLL with a `.bak`
rename. The neighbor's existing `_bundle` file serves as the marker.
Caveat: Steam can re-sync workshop folders at any time, so this is
development-only.

### Verifying a build is loaded

After launching TerraTech, the init log line appears in
`%LOCALAPPDATA%Low\Payload\TerraTech\output_log.txt`:

```
[World Customizer 0.1.0] init complete
```

### Status

v0.1.0. All listed settings are implemented and working in-game. Two
geometry fields (`TileResolution`, `CellScale`) are exposed read-only in
the IMGUI fallback panel — they correspond to engine static-cache
constants that can't be mutated at runtime without crashing tile
generation. Heightmap detail and splat detail are present in the model
and the IMGUI panel but not in the native popup (would need a
multi-button choice row that wasn't implemented).
