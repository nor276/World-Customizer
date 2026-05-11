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

## Layout

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
├── workshop-staging/            Build output, mirrors the Workshop folder layout
└── workshop-assets/             Source assets (preview.png, _bundle when present)
```

For per-field implementation details (engine targets, Harmony patches,
known doubts), see [FIELDS.md](FIELDS.md).

## License

MIT — see [LICENSE](LICENSE). Fork it, modify it, republish it under your
own name on the Workshop. Keep the copyright notice in any redistribution.
