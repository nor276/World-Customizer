using System;
using UnityEngine;

namespace WorldCustomizer
{
    /// <summary>
    /// Per-world customization payload. Serialized as JSON in the sidecar file
    /// next to the game's save file (see <see cref="SettingsStore"/>).
    /// </summary>
    [Serializable]
    public class Settings
    {
        /// <summary>Sidecar file format. Bump and migrate when fields change incompatibly.</summary>
        public int FormatVersion;

        /// <summary>Mod version that created or last wrote this payload. Informational.</summary>
        public string CreatedByModVersion;

        /// <summary>Tier-1 (geometry-affecting). Locked after world creation.</summary>
        public CreationSettings Creation;

        /// <summary>Tier-2 (gameplay multipliers). Safe to change mid-game.</summary>
        public LiveSettings Live;

        public Settings()
        {
            FormatVersion = CurrentFormatVersion;
            CreatedByModVersion = KickStart.Version;
            Creation = new CreationSettings();
            Live = new LiveSettings();
        }

        public const int CurrentFormatVersion = 1;

        public static Settings CreateDefaults() => new Settings();

        /// <summary>Deep copy. Used by the settings UIs so the caller's instance
        /// isn't mutated while the user is dragging sliders.</summary>
        public static Settings Clone(Settings src)
        {
            if (src == null) return CreateDefaults();
            var copy = new Settings();
            copy.Creation.ContinentSize      = src.Creation.ContinentSize;
            copy.Creation.BiomeChaos         = src.Creation.BiomeChaos;
            copy.Creation.BiomeEdgeSharpness = src.Creation.BiomeEdgeSharpness;
            copy.Creation.EnableRegions      = src.Creation.EnableRegions;
            copy.Creation.TileResolution     = src.Creation.TileResolution;
            copy.Creation.CellScale          = src.Creation.CellScale;
            copy.Creation.HeightmapDetail    = src.Creation.HeightmapDetail;
            copy.Creation.SplatDetail        = src.Creation.SplatDetail;
            copy.Creation.HeightMultiplier   = src.Creation.HeightMultiplier;

            copy.Live.ResourceDensityMultiplier   = src.Live.ResourceDensityMultiplier;
            copy.Live.ResourceYieldMultiplier     = src.Live.ResourceYieldMultiplier;
            copy.Live.DrillSpeedMultiplier        = src.Live.DrillSpeedMultiplier;
            copy.Live.AutoMinerSpeedMultiplier    = src.Live.AutoMinerSpeedMultiplier;
            copy.Live.AtmosphereDensityMultiplier = src.Live.AtmosphereDensityMultiplier;
            return copy;
        }

        /// <summary>Clamp every field to its valid range. Idempotent.</summary>
        public void Sanitize()
        {
            if (Creation == null) Creation = new CreationSettings();
            if (Live == null) Live = new LiveSettings();
            Creation.Sanitize();
            Live.Sanitize();
        }

        /// <summary>True iff every field equals the default value.</summary>
        public bool IsAllDefaults()
        {
            return (Creation?.IsAllDefaults() ?? true)
                && (Live?.IsAllDefaults() ?? true);
        }
    }

    /// <summary>
    /// Tier-1 settings. Mutating these after world creation can corrupt save geometry,
    /// so they are presented read-only on the live (mid-game) panel.
    /// </summary>
    [Serializable]
    public class CreationSettings
    {
        /// <summary>User-facing "Biome size" multiplier — 1.0 = vanilla, 2.0 = biomes
        /// twice as big, 0.5 = biomes half as big. Range 0.5–5.0.
        /// On apply, the engine's <c>m_BiomeDistributionScaleMacro</c> (which is a
        /// density, not a size) is computed as <c>0.7 / slider</c>, then clamped to
        /// [0.05, 1.3] so the terrain renderer doesn't go white at very high engine
        /// values (>~1.5).
        /// • Slider 0.5 → engine 1.4 (clamped to 1.3, ~half-size biomes)
        /// • Slider 1.0 → engine 0.7 (vanilla default)
        /// • Slider 5.0 → engine 0.14 (biomes 5× bigger)</summary>
        public float ContinentSize = 1.0f;

        /// <summary>Voronoi point jitter. 0 = grid-regular biomes, 1 = full chaos. Range 0–1.</summary>
        public float BiomeChaos = 1.0f;

        /// <summary>Inverse of biome edge band tolerance. 0 = razor edges, higher = smooth blends. Range 0–3.</summary>
        public float BiomeEdgeSharpness = 1.0f;

        /// <summary>If false, single-layer Voronoi (chaotic biome layout, no regional structure).</summary>
        public bool EnableRegions = true;

        /// <summary>Heightmap dimension per tile edge. Power of 2 in {32, 64, 128, 256}.</summary>
        public int TileResolution = 64;

        /// <summary>World units per cell. Range 3–12. Combined with <see cref="TileResolution"/> determines tile world size.</summary>
        public float CellScale = 6.0f;

        /// <summary>Per-cell heightmap subdivision factor. {1, 2, 4}. Quadratic memory cost.</summary>
        public int HeightmapDetail = 2;

        /// <summary>Per-cell splat resolution factor. {2, 4, 8}.</summary>
        public int SplatDetail = 4;

        /// <summary>Multiplier on generated heightmap. Effective vertical range = 100 × this. Range 0.5–3.0.</summary>
        public float HeightMultiplier = 1.0f;

        public void Sanitize()
        {
            ContinentSize      = Mathf.Clamp(ContinentSize, 0.5f, 5.0f);
            BiomeChaos         = Mathf.Clamp01(BiomeChaos);
            BiomeEdgeSharpness = Mathf.Clamp(BiomeEdgeSharpness, 0f, 3f);
            // CellScale: conservative 4–8 range. Wider values risk breaking tile-pool reinit;
            // the field is currently a no-op anyway (engine static caches forbid runtime change).
            CellScale          = Mathf.Clamp(CellScale, 4f, 8f);
            // HeightMultiplier: scales terrainData.size.y directly (no heightmap clamping
            // issue). Wide range to give visibly different terrain from very flat to towering.
            HeightMultiplier   = Mathf.Clamp(HeightMultiplier, 0.1f, 5f);

            TileResolution  = SnapToValid(TileResolution,  ValidTileResolutions);
            HeightmapDetail = SnapToValid(HeightmapDetail, ValidHeightmapDetails);
            SplatDetail     = SnapToValid(SplatDetail,     ValidSplatDetails);
        }

        public bool IsAllDefaults()
        {
            var d = new CreationSettings();
            return Mathf.Approximately(ContinentSize, d.ContinentSize)
                && Mathf.Approximately(BiomeChaos, d.BiomeChaos)
                && Mathf.Approximately(BiomeEdgeSharpness, d.BiomeEdgeSharpness)
                && EnableRegions == d.EnableRegions
                && TileResolution == d.TileResolution
                && Mathf.Approximately(CellScale, d.CellScale)
                && HeightmapDetail == d.HeightmapDetail
                && SplatDetail == d.SplatDetail
                && Mathf.Approximately(HeightMultiplier, d.HeightMultiplier);
        }

        // Conservative choice sets. The wider sets exist in the engine but changing these
        // at runtime breaks static-cache invariants (crashes observed at TileResolution=128+
        // before tile-pool reinit was attempted). Exposed read-only in the UI as no-op choice
        // rows for visibility into what the engine is using.
        public static readonly int[] ValidTileResolutions  = { 64, 128 };
        public static readonly int[] ValidHeightmapDetails = { 1, 2 };
        public static readonly int[] ValidSplatDetails     = { 2, 4 };

        private static int SnapToValid(int value, int[] allowed)
        {
            int best = allowed[0];
            int bestDiff = Math.Abs(value - best);
            for (int i = 1; i < allowed.Length; i++)
            {
                int diff = Math.Abs(value - allowed[i]);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = allowed[i];
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Tier-2 settings. Safe to change mid-game. Effects apply to newly streamed tiles
    /// and freshly evaluated game functions; existing tile geometry is unaffected.
    /// </summary>
    [Serializable]
    public class LiveSettings
    {
        /// <summary>Multiplier on resource-tagged scenery distribution weight. Range 0–5.</summary>
        public float ResourceDensityMultiplier = 1.0f;

        /// <summary>Multiplier on chunks-per-node. Range 0–20.</summary>
        public float ResourceYieldMultiplier = 1.0f;

        /// <summary>Multiplier on manual drill DPS. Range 0–50.</summary>
        public float DrillSpeedMultiplier = 1.0f;

        /// <summary>Multiplier on AutoMiner extraction speed. Range 0–50.</summary>
        public float AutoMinerSpeedMultiplier = 1.0f;

        /// <summary>Multiplier on wing lift force. 0 = vacuum (planes drop), 10 = thick. Range 0–10.</summary>
        public float AtmosphereDensityMultiplier = 1.0f;

        public void Sanitize()
        {
            ResourceDensityMultiplier   = Mathf.Clamp(ResourceDensityMultiplier,   0f, 5f);
            ResourceYieldMultiplier     = Mathf.Clamp(ResourceYieldMultiplier,     0f, 20f);
            DrillSpeedMultiplier        = Mathf.Clamp(DrillSpeedMultiplier,        0f, 50f);
            AutoMinerSpeedMultiplier    = Mathf.Clamp(AutoMinerSpeedMultiplier,    0f, 50f);
            AtmosphereDensityMultiplier = Mathf.Clamp(AtmosphereDensityMultiplier, 0f, 10f);
        }

        public bool IsAllDefaults()
        {
            var d = new LiveSettings();
            return Mathf.Approximately(ResourceDensityMultiplier,   d.ResourceDensityMultiplier)
                && Mathf.Approximately(ResourceYieldMultiplier,     d.ResourceYieldMultiplier)
                && Mathf.Approximately(DrillSpeedMultiplier,        d.DrillSpeedMultiplier)
                && Mathf.Approximately(AutoMinerSpeedMultiplier,    d.AutoMinerSpeedMultiplier)
                && Mathf.Approximately(AtmosphereDensityMultiplier, d.AtmosphereDensityMultiplier);
        }
    }
}
