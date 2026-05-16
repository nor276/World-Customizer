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

*Resources & combat:*

| Setting | Range | Effect |
|---|---|---|
| Resource density | 0.0 – 5.0 | Scales how much scatter (rocks, trees, ore) populates the world. |
| Resource yield | 0.0 – 20.0 | Chunks per surface deposit when mined. |
| Drill speed | 0.0 – 50.0 | Per-hit damage of manual drills. |
| AutoMiner speed | 0.0 – 50.0 | Extraction rate of underground reservoirs. |
| Atmosphere density | 0.0 – 10.0 | Wing lift force. 0 = vacuum (planes drop). 10 = dense (planes fly aggressively). |

*SCU & pickup behavior:*

| Setting | Range | Effect |
|---|---|---|
| Pickup range | 0.1 – 10.0 | Scales every pickup module's reach (SCUs, conveyor pickups). Vanilla relative ranges are preserved — long-range SCUs stay long-range. |
| Beam pull strength | 0.1 – 10.0 | Scales the tractor-beam force on held items. Engine caps the final force, so very high multipliers saturate. |
| Stack capacity | 0.5 – 10.0 | How many items a single stack on an SCU / conveyor / holder can hold. Visual stacks just grow taller. |
| Lift height | 0.5 – 5.0 | Raises items higher above the holder during the in-flight pull so they clear obstacles before settling onto the stack. |
| Pickup speed | 0.1 – 10.0 | How often the SCU "looks" for new items in range. Higher = faster sustained pickup throughput. |
| Items per tick | 1.0 – 10.0 | How many items the SCU can grab per game tick. Stacks multiplicatively with Pickup speed. |

*Loose-item budget:*

| Setting | Range | Effect |
|---|---|---|
| Max loose items | 500 – 20000 | Hard cap on total loose chunks + dropped blocks in the world. When exceeded, the game culls the worst-scoring loose item (far from player + clumped + many duplicates of that type) until back under cap. |
| Loose item lifetime | 0.1 – 5.0 | Multiplier on how long a loose chunk/block/crate stays in the world before despawning. Vanilla ≈ 5 min × this value. Held items aren't affected. |

Per-world settings are saved alongside the save file in
`<savefile>.worldgen.json`, so each world remembers its own customization.

### Retuning mid-game

The "Gameplay" multipliers above can be retuned after a world exists. If you
have [Native Options](https://steamcommunity.com/sharedfiles/filedetails/?id=2685130411)
installed, a **World Customizer** page appears in the in-game Options menu
with sliders for every live-tunable field — changes take effect on the
running world immediately, no save reload needed. Native Options is optional;
without it the mod still works for new-world setup, you just won't have a
mid-game UI.

### Notes on extreme settings

Pushing several sliders toward the high end of their range at the same
time can make the world fill up faster than TerraTech's physics engine
was designed to handle. The mod includes:

* A **confirm-time warning** if `Resource density × Resource yield`
  exceeds the danger zone, so you can dial back before entering.
* The **Max loose items** and **Loose item lifetime** sliders give you
  direct control over how aggressively old loose items get culled.

If you want to push past where the engine is comfortable, there is an
optional offline utility at [`scripts/patch_broadphase.py`](scripts/patch_broadphase.py)
that switches Unity's PhysX broadphase algorithm from the per-region-capped
default to one with no hard ceiling. It's run once per game install; it
trades a hard crash for slower performance under extreme loads. Read the
script header and [FIELDS.md](FIELDS.md#physics-engine-limits--read-before-pushing-sliders-to-the-maximum)
before running. Most players won't need this — keeping the high sliders
moderate is enough.

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
