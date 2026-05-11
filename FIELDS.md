# Field reference

For each settings field exposed by the mod: what the user sees, what the
engine target is, the patch path that wires the two together, and explicit
notes about parts that are empirically-verified-but-not-fully-understood.

Read alongside `Settings.cs` (field definitions, ranges, defaults) and the
patch files in `Patches/Generation/`. Where the implementation is doing
something non-obvious or working from incomplete reverse-engineering, the
"Doubt / caveats" block calls it out.

## Pipeline overview

Settings reach the engine via three stages:

1. **User picks values** in the new-game popup (`UI/UIScreenWorldCustomizeNative.cs`
   for the native uGUI variant; `UIScreenWorldCustomize.cs` as IMGUI fallback).
   On Confirm, the chosen `Settings` object lands in `SettingsStore.Pending`.
2. **World load** fires `ManGameMode.ModeSwitchEvent`, observed by
   `Patches/Generation/ApplyOnModeSwitch.cs`. That handler calls
   `SettingsStore.Promote()` (Pending → Current) and then dispatches the
   Tier-2 apply (`ApplyTier2`). Tier-1 biome-side mutations are *not* applied
   here — see the next step.
3. **Biome map assignment** is later: when `ManWorld.Reset(BiomeMap, bool)`
   runs, `BiomeMapApplyPatch` postfixes it and applies most Tier-1 fields
   at the exact moment `CurrentBiomeMap` becomes non-null. Some fields
   (HeightMultiplier, drill/wing/yield multipliers) attach directly to other
   Harmony postfixes that fire later — see per-field rows below.

`SettingsStore.Current` survives mod recycles (DeInit doesn't clear it), so
re-loading the mod mid-session keeps the user's settings intact.

---

## Tier 1 — Creation-locked

Changing any of these mid-game can corrupt save geometry, so the popup only
exposes them at new-world creation. The UI panel labels these as
"Geometry — locked after creation".

### Biome size (`ContinentSize`)

| | |
|---|---|
| User-facing label | `Biome size` |
| Slider range | `0.5 – 5.0` (default `1.0`) |
| Display semantics | `1.0` = vanilla. `2.0` = biomes 2× bigger. `0.5` = half-size. |
| Settings field | `Creation.ContinentSize` |
| Engine target | `BiomeMap.m_BiomeDistributionScaleMacro` (private float) |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** The engine's `m_BiomeDistributionScaleMacro` is a *density*
value — higher = more biomes per area = smaller individual biomes. That's
the opposite of what users intuit from "size", so the slider is reciprocal:
`engine = 0.7 / slider`, with the result clamped to `[0.05, 1.3]`. The `0.7`
constant is the engine's vanilla default for this field.

**Doubt / caveats.** The clamp ceiling of `1.3` is empirical — we observed
that engine values above ~`1.5` make the terrain renderer fail and present
a white landscape (likely a Voronoi sampling-rate runaway, but not
confirmed from the engine source). The `0.5` slider value maps to `engine =
1.4`, which gets clamped to `1.3` — so the slider's left ~5% is a flat
floor. Acceptable for a safety margin.

### Biome chaos (`BiomeChaos`)

| | |
|---|---|
| User-facing label | `Biome chaos` |
| Slider range | `0.0 – 1.0` (default `1.0`) |
| Settings field | `Creation.BiomeChaos` |
| Engine target | `BiomeMap.m_AdvancedParameters.vCellVarianceMacro / Major / Minor / Micro` (all four set to the same value) |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** TerraTech's biome layout is a 4-level nested Voronoi
diagram. Each level has its own `vCellVariance*` parameter controlling how
much the Voronoi points jitter off the regular grid (0 = strict grid,
1 = full chaos). We broadcast the slider's single value to all four levels.

**Doubt / caveats.** Whether the four levels should track in lockstep or be
controlled independently is unclear. Setting them all to the same value
gives an intuitive single-knob behavior, but advanced users might want
different jitter on different scales. The 4 levels do not have independent
sliders in our UI; if you want that, expose four fields and wire each
separately.

### Biome edge sharpness (`BiomeEdgeSharpness`)

| | |
|---|---|
| User-facing label | `Biome edge sharpness` |
| Slider range | `0.0 – 3.0` (default `1.0`) |
| Settings field | `Creation.BiomeEdgeSharpness` |
| Engine target | `BiomeMap.bandTolerance` (inverse relationship) |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** The engine's `bandTolerance` is the width of the blend band
between adjacent biomes — wider = softer transition. Inverting:
`bandTolerance = 1 / sharpness`, with a guard: if `sharpness < 0.001`,
`bandTolerance = 1000` (effectively infinite-width blend → no visible biome
edges).

**Doubt / caveats.** None obvious. The relationship is clean and the
guard for the zero case behaves sensibly.

### Enable regions (`EnableRegions`)

| | |
|---|---|
| User-facing label | `Enable regions` |
| Type | toggle (default `true`) |
| Settings field | `Creation.EnableRegions` |
| Engine target | `BiomeMap.enableRegions` |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** Direct field copy. The engine flag toggles between a single
Voronoi layer (off) and a two-tier hierarchical Voronoi (on, default).
Turning it off gives a flat chaotic biome layout without the regional
clustering that the campaign biome map relies on.

**Doubt / caveats.** None.

### Height multiplier (`HeightMultiplier`)

| | |
|---|---|
| User-facing label | `Height multiplier` |
| Slider range | `0.1 – 5.0` (default `1.0`) |
| Settings field | `Creation.HeightMultiplier` |
| Engine target | `Terrain.terrainData.size.y` (per tile) |
| Patch | `Patches/Generation/HeightScalePatch.cs` (postfix on `TileManager.CreateTile`) |

**Mechanics.** Earlier attempts multiplied the heightmap buffer in
`BiomeMap.GenerateHeightMap`, but Unity's heightmap is normalized `[0, 1]`
and multiplying clamps — multipliers `> 1` produced flat-topped mountains
rather than taller ones. The working approach scales the terrain's
`size.y` directly, which is hard-coded to `100f` in the game. New
`size.y = 100 × multiplier`. At `5.0` mountains are five times taller; at
`0.1` the world is nearly flat.

**Doubt / caveats.** Affects only newly created tiles. Tiles already
streamed in keep their old `size.y` (and therefore old heights). For
practical purposes this matters only during a save load + re-stream.

### Heightmap detail (`HeightmapDetail`) — IMGUI fallback only

| | |
|---|---|
| User-facing label | `Heightmap detail` |
| Choice values | `{1, 2}` (default `2`) |
| Settings field | `Creation.HeightmapDetail` |
| Engine target | `BiomeMap.m_AdvancedParameters.heightmapResolutionPerCell` |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** Direct copy. Sub-cell heightmap subdivision factor.

**Doubt / caveats.** Not exposed in the native uGUI popup yet — that
panel uses slider/toggle row prefabs but no multi-button choice row was
implemented. The IMGUI fallback shows it.

### Splat detail (`SplatDetail`) — IMGUI fallback only

| | |
|---|---|
| User-facing label | `Splat detail` |
| Choice values | `{2, 4}` (default `4`) |
| Settings field | `Creation.SplatDetail` |
| Engine target | `BiomeMap.m_AdvancedParameters.multiTextureResolutionPerCell` |
| Patch | `BiomeMapApplyPatch.ApplyTier1Biome` |

**Mechanics.** Direct copy. Sub-cell splat-map subdivision factor.

**Doubt / caveats.** Same as `HeightmapDetail` — IMGUI only, missing a
native-UI choice row.

### Tile resolution (`TileResolution`) — no-op v0.1

| | |
|---|---|
| User-facing label | `Tile resolution (engine constant, no-op v0.1)` |
| Choice values | `{64, 128}` (default `64`) |
| Settings field | `Creation.TileResolution` |
| Engine target | `ManWorld.m_CellsPerTileEdge` (not actually mutated) |
| Patch | `BiomeMapApplyPatch.ApplyTier1` (logs requested change only) |

**Mechanics.** Field is exposed for transparency about the engine constant.
Attempting to mutate it crashes the game — multiple static caches
(`s_NumTileInfoPoints`, `kInvTileSize`, `kTileHalfDiagonal`, plus JIT-inlined
references) snapshot the value at startup, so changing it at runtime
breaks tile world math.

**Doubt / caveats.** Listed in the IMGUI panel because it appears as an
"advanced parameter" in the engine, but it does nothing. If someone hardens
tile-pool reinit and the cache rebuild, this could become live.

### World cell scale (`CellScale`) — no-op v0.1

| | |
|---|---|
| User-facing label | `World cell scale (engine constant, no-op v0.1)` |
| Slider range | `4.0 – 8.0` (default `6.0`) |
| Settings field | `Creation.CellScale` |
| Engine target | `ManWorld.m_CellScale` (not actually mutated) |
| Patch | `BiomeMapApplyPatch.ApplyTier1` (logs requested change only) |

**Mechanics.** Same situation as `TileResolution`. The engine uses
`invCellScale` (a precomputed reciprocal) and several other derived
constants. Mutating the live field doesn't update those.

**Doubt / caveats.** Same as above.

---

## Tier 2 — Live-tunable

Safe to change mid-game; effects apply to newly streamed tiles and freshly
evaluated game functions. The UI panel labels these "Gameplay — changeable
later".

### Resource density (`ResourceDensityMultiplier`)

| | |
|---|---|
| User-facing label | `Resource density` |
| Slider range | `0.0 – 5.0` (default `1.0`) |
| Settings field | `Live.ResourceDensityMultiplier` |
| Engine targets | Multiple — see Mechanics |
| Patches | `BiomeMapApplyPatch.ApplyResourceDensity` + `ResourceDensityRecalcPatch` + `SceneryCellDensityPatch` |

**Mechanics.** Three layered mechanisms, in order of impact:

1. **Per-tile cell-grid rebalance** (`SceneryCellDensityPatch`,
   postfix on `BiomeMap.GenerateSceneryMap`). After the engine populates
   the per-tile `sceneryPlacement[i, j]` 2D array of `SpawnDetail` records,
   we mutate it directly:
   * **Multi-wave expansion** for `multiplier > 1`. Each wave snapshots the
     populated cells, shuffles them (Fisher-Yates) for fairness, and each
     cell fills one random empty in-bounds neighbor with its non-mergeable
     source object. Newly-filled cells participate in subsequent waves,
     so density propagates outward beyond the immediate 8-neighbor cap.
     Wave count is `floor(M-1)` full waves plus a fractional wave with
     probability `M - 1 - floor(M-1)`. The non-mergeable preference comes
     from `TerrainObject.IsSimpleMeshMergeable` — the game uses this flag
     to identify scatter that renders via the cheap instanced-clutter pass
     (grass, pebbles, gravel) vs. real resource-bearing scenery.
   * **Aggressive declutter** in the same pass. With probability
     `1 - 1/M²` per slot (M=2→75%, M=3→89%, M=5→96%), strip mergeable
     objects from every cell. Slots are then compacted so non-null entries
     fill `t0 → t1 → t2` in order. This is what keeps grasslands from
     looking like overgrown fields of grass tufts when density goes high.
   * **Sparsify** for `multiplier < 1`. Each populated cell has
     probability `1 - M` of being cleared.
2. **DistributionWeight scaling** (`BiomeMapApplyPatch.ApplyResourceDensity`).
   Scales `BiomeMap.m_SceneryDistributionWeights[i].weight` and
   `calculatedMultiplier` by the user value, and sets `allowGreaterThanOne =
   true` when `multiplier > 1` so the engine's saturation cap doesn't clip
   above-vanilla values. Originals cached per `<biomeMapID>::<tag>` so
   re-apply is idempotent.
3. **Recalc backstop** (`ResourceDensityRecalcPatch`, postfix on
   `BiomeMap.RecalculateDistributionWeightMultipliers`). The engine
   recomputes `calculatedMultiplier` from `weight` after our apply runs,
   which would erase the scaling. We postfix that method and re-apply the
   multiplier so we're the last writer.

**Doubt / caveats.** This is the highest-uncertainty path in the project.
Two earlier approaches (Voronoi `layer0Scale`/`layer1Scale` mutation, and
DetailLayer `m_CutoffThreshold` shifting) had no effect or caused biome-
specific runaway respectively. The current three-layer hybrid is what
empirically produces uniform density scaling across biomes; the dominant
lever is the cell-grid mutation in `SceneryCellDensityPatch`. Why the
distribution-weight scaling matters in addition isn't fully verified — it
likely contributes by protecting rare prefabs from being culled by
`GetBestPrefabGroup`, but density was visible even with that piece omitted.

At very high `M` (say `M ≥ 4`) the cell grid saturates and further
density gains are minimal. The strip pass keeps the world from looking
visually crowded even at saturation.

### Resource yield (`ResourceYieldMultiplier`)

| | |
|---|---|
| User-facing label | `Resource yield` |
| Slider range | `0.0 – 20.0` (default `1.0`) |
| Settings field | `Live.ResourceYieldMultiplier` |
| Engine target | `ResourceDispenser.m_TotalChunks` (per surface chunk source) |
| Patch | `Patches/Generation/ResourceYieldPatch.cs` |

**Mechanics.** Postfix on `ResourceDispenser.OnSpawn` mutates
`m_TotalChunks` (the number of chunks a deposit will yield when fully
mined) to `original × multiplier`, clamped to a minimum of 1. Cached
originals keyed by `GetInstanceID` keep re-application idempotent.
`ApplyToAllLoaded` walks `FindObjectsOfType<ResourceDispenser>()` at
mode-switch time to catch instances that spawned before the patch was
ready.

**Doubt / caveats.** Affects *surface chunks* only — the visible ore
deposits the player mines manually. Distinct from `AutoMinerSpeedMultiplier`
which scales the *underground reservoir* extraction rate.

### Drill speed (`DrillSpeedMultiplier`)

| | |
|---|---|
| User-facing label | `Drill speed` |
| Slider range | `0.0 – 50.0` (default `1.0`) |
| Settings field | `Live.DrillSpeedMultiplier` |
| Engine target | `ModuleDrill.GetHitDamage` return value |
| Patch | `Patches/Generation/DrillSpeedPatch.cs` |

**Mechanics.** Postfix on `ModuleDrill.GetHitDamage` multiplies the
returned per-hit damage by the multiplier. Drill mining speed in TerraTech
is rate-of-damage × hits-per-second, and the per-hit damage is the only
side we touch — hit cadence is untouched.

**Doubt / caveats.** Affects every drill on every tech, friend or foe.
If you want player-only scaling, branch on
`__instance.block?.tank?.Team` inside the postfix.

### AutoMiner speed (`AutoMinerSpeedMultiplier`)

| | |
|---|---|
| User-facing label | `AutoMiner speed` |
| Slider range | `0.0 – 50.0` (default `1.0`) |
| Settings field | `Live.AutoMinerSpeedMultiplier` |
| Engine target | `ResourceReservoir.m_ExtractionSpeedMultiplier` |
| Patch | `Patches/Generation/AutoMinerSpeedPatch.cs` |

**Mechanics.** Postfix on `ResourceReservoir.OnSpawn` plus a walk through
`FindObjectsOfType<ResourceReservoir>()` at mode-switch. Caches originals
per `GetInstanceID` and writes `original × multiplier` (with `0.01` floor).
Distinct system from `ResourceYieldPatch` — reservoirs are the deep
underground pools AutoMiners draw from over time, not the surface chunks.

**Doubt / caveats.** Like drill speed, applies to all reservoirs without
team filtering.

### Atmosphere density (`AtmosphereDensityMultiplier`)

| | |
|---|---|
| User-facing label | `Atmosphere density` |
| Slider range | `0.0 – 10.0` (default `1.0`) |
| Settings field | `Live.AtmosphereDensityMultiplier` |
| Engine target | `ModuleWing+AerofoilState.CalculateForce` return value |
| Patch | `Patches/Generation/AtmospherePatch.cs` |

**Mechanics.** Postfix on the nested private class
`ModuleWing+AerofoilState.CalculateForce` multiplies the returned
`Vector3` lift force by the multiplier. Because the target is a private
nested type, the patch uses `TargetMethod()` with
`AccessTools.Method("ModuleWing+AerofoilState:CalculateForce")` rather than
the simpler `[HarmonyPatch(typeof(...))]` attribute (the attribute path
can't see private nested types).

**Doubt / caveats.** The first thing we tried was swapping
`ManWorld.UniversalAtmosphereDensityCurve` — the curve exists in the source
but nothing reads it at lift-compute time. Verified empirically that the
field is dead code by examining lift formulas in `ModuleWing`. So we patch
the wing force directly. `multiplier = 0` is functional vacuum (planes
drop); `multiplier = 10` is dense atmosphere (planes accelerate hard but
remain flyable).

---

## What's *not* covered here

* `FormatVersion`, `CreatedByModVersion` on the `Settings` object are
  metadata for the JSON sidecar, not user-facing settings.
* The `IsAllDefaults()` and `Sanitize()` methods on the Settings classes are
  serialization plumbing, not engine-targeted.
* `SettingsStore.GetSidecarPath` / `LoadFromFile` / `SaveToFile` handle the
  per-save JSON sidecar (`<savePath>.worldgen.json`); none of those touch
  the engine.

---

## Adding a new field

The recurring pattern, distilled:

1. Add the field to `Settings.cs` (either `CreationSettings` or
   `LiveSettings`), with a default value, a doc comment naming the
   engine target, and a clamp in `Sanitize()`.
2. Add a row to `UI/UIScreenWorldCustomizeNative.PopulateRows` — slider,
   toggle, or section header. Mirror in `UIScreenWorldCustomize.cs` (the
   IMGUI fallback) if you want the row in both popups.
3. Wire the apply path:
   * **Biome-side, geometry-affecting**: add to
     `Patches/Generation/BiomeMapApplyPatch.ApplyTier1Biome`. Cache the
     original per `BiomeMap.GetInstanceID()` if you need re-apply
     idempotency.
   * **Gameplay multiplier on a specific component method**: write a
     Harmony postfix on the target method, read the multiplier from
     `SettingsStore.Current?.Live` (null-safe; return early if null),
     and short-circuit when `Mathf.Approximately(multiplier, 1f)`.
   * **Live state on existing component instances**: write a postfix
     on the relevant `OnSpawn` plus an `ApplyToAllLoaded` static helper
     that walks `FindObjectsOfType<T>` and is called from
     `ApplyOnModeSwitch.ApplyTier2`.
4. Extend `SettingsSelfTest` if the field has interesting clamp behavior
   or round-trip quirks.
