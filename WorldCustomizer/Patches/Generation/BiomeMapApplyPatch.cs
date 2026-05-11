using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Postfix on <c>ManWorld.Reset(BiomeMap, bool)</c>. Applies all biome-side settings
    /// at the precise moment <c>CurrentBiomeMap</c> becomes available.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this hook:</b> <c>ModeSwitchEvent</c> fires before mode setup, when
    /// <c>CurrentBiomeMap</c> is still null. <c>ManWorld.Reset</c> is what assigns the
    /// BiomeMap (line 499 of the original) — Postfixing it gives us a guaranteed-non-null
    /// BiomeMap reference, which is exactly what biome-side mutations need.</para>
    /// <para><b>Settings applied here:</b>
    /// <list type="bullet">
    /// <item>Tier-1: ContinentSize, BiomeChaos (×4 octaves), BiomeEdgeSharpness, EnableRegions, HeightmapDetail, SplatDetail</item>
    /// <item>Tier-2: ResourceDensityMultiplier (scales <c>m_SceneryDistributionWeights</c>)</item>
    /// </list></para>
    /// <para><b>Multiple-call safety:</b> Reset can be called repeatedly (mode changes,
    /// reload from save). Cached originals make every re-apply idempotent —
    /// <c>scaled = original × multiplier</c> regardless of how many times we run.</para>
    /// </remarks>
    /// <summary>
    /// Postfix on <c>BiomeMap.RecalculateDistributionWeightMultipliers</c>. Ensures our
    /// resource-density multiplier is the LAST writer to <c>calculatedMultiplier</c>, since
    /// the game recomputes that field from <c>weight</c> after our apply runs and a natural
    /// saturation cap clips uniformly-scaled weights short of the user's chosen multiplier.
    /// </summary>
    [HarmonyPatch(typeof(BiomeMap), nameof(BiomeMap.RecalculateDistributionWeightMultipliers))]
    internal static class ResourceDensityRecalcPatch
    {
        private static bool s_LoggedFirstFire;

        private static void Postfix(BiomeMap __instance)
        {
            LiveSettings l = SettingsStore.Current?.Live;
            if (l == null) return;
            if (Mathf.Approximately(l.ResourceDensityMultiplier, 1f)) return;

            BiomeMap.DistributionWeight[] weights = Reflect.GetField<BiomeMap.DistributionWeight[]>(__instance, "m_SceneryDistributionWeights");
            if (weights == null) return;

            int count = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                BiomeMap.DistributionWeight w = weights[i];
                if (w == null) continue;
                w.calculatedMultiplier *= l.ResourceDensityMultiplier;
                if (l.ResourceDensityMultiplier > 1f)
                    w.allowGreaterThanOne = true;
                count++;
            }

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"ResourceDensityRecalcPatch.Postfix FIRST FIRE: scaled {count} calculatedMultipliers post-recalc by ×{l.ResourceDensityMultiplier:0.00}");
                int sample = Mathf.Min(weights.Length, 3);
                for (int i = 0; i < sample; i++)
                {
                    BiomeMap.DistributionWeight w = weights[i];
                    if (w == null) continue;
                    string tagDisplay = w.tag ?? "<null>";
                    KickStart.Log($"  [tag={tagDisplay}] post-recalc-postfix calculatedMultiplier={w.calculatedMultiplier:0.00}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(ManWorld), nameof(ManWorld.Reset), new[] { typeof(BiomeMap), typeof(bool) })]
    internal static class BiomeMapApplyPatch
    {
        // First-fire diagnostic
        private static bool s_LoggedFirstFire;

        // Cached originals so re-apply is idempotent.
        // Keyed by BiomeMap.GetInstanceID() so different biome maps cache independently.
        private static readonly Dictionary<int, float> s_OriginalContinentSize       = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_OriginalBandTolerance       = new Dictionary<int, float>();
        private static readonly Dictionary<int, bool>  s_OriginalEnableRegions       = new Dictionary<int, bool>();
        private static readonly Dictionary<int, float> s_OriginalVCellVarianceMacro  = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_OriginalVCellVarianceMajor  = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_OriginalVCellVarianceMinor  = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_OriginalVCellVarianceMicro  = new Dictionary<int, float>();
        private static readonly Dictionary<int, int>   s_OriginalHeightmapDetail     = new Dictionary<int, int>();
        private static readonly Dictionary<int, int>   s_OriginalSplatDetail         = new Dictionary<int, int>();
        // Distribution weights are PER ENTRY; key is "<biomeMapID>::<tag>"
        // We cache BOTH the base weight and the calculatedMultiplier — the scatter pipeline
        // actually consults calculatedMultiplier (which is normally derived from weight by
        // RecalculateDistributionWeightMultipliers). Mutating only weight without also
        // mutating calculatedMultiplier means the scatter doesn't see our change.
        private static readonly Dictionary<string, float> s_OriginalDistributionWeights = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> s_OriginalCalculatedMultipliers = new Dictionary<string, float>();

        // NOTE: Per-biome Voronoi layer scales and DetailLayer cutoff thresholds were
        // explored as density levers. Both had problems:
        //   - layer0Scale/layer1Scale: only affect variant selection, not spawn count
        //   - cutoff threshold: only affects rare/specialty overlays (biomes with
        //     cutoff > 0), causing the "specific resource in specific biome explodes
        //     while most biomes show no change" behavior.
        // The actual uniform density lever lives in SceneryCellDensityPatch, which
        // post-mutates the SpawnDetail grid produced by GenerateSceneryMap.

        private static void Postfix(ManWorld __instance, BiomeMap map)
        {
            if (map == null)
            {
                // Reset can be called with null at cleanup. Not a bug; just nothing for us.
                if (!s_LoggedFirstFire)
                {
                    KickStart.Log("BiomeMapApplyPatch.Postfix: ManWorld.Reset called with null BiomeMap (probably cleanup); skipping biome-side apply");
                }
                return;
            }

            Settings active = SettingsStore.Current;
            if (active == null)
            {
                KickStart.Log("BiomeMapApplyPatch.Postfix: SettingsStore.Current is null; skipping biome-side apply (defaults remain in effect)");
                return;
            }

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"BiomeMapApplyPatch.Postfix FIRST FIRE: BiomeMap={map.name}, applying biome-side settings");
            }

            try
            {
                ApplyToBiomeMap(map, active.Creation, active.Live);
            }
            catch (Exception ex)
            {
                KickStart.LogError("BiomeMapApplyPatch.Postfix threw during apply", ex);
            }
        }

        // ------------------------------------------------------------
        // Apply
        // ------------------------------------------------------------
        private static void ApplyToBiomeMap(BiomeMap bm, CreationSettings c, LiveSettings l)
        {
            int bmID = bm.GetInstanceID();

            if (c != null)
            {
                ApplyTier1Biome(bm, bmID, c);
            }

            if (l != null && !Mathf.Approximately(l.ResourceDensityMultiplier, 1f))
            {
                // calculatedMultiplier-side scaling protects rare prefabs from being culled by
                // GetBestPrefabGroup. SceneryCellDensityPatch handles the per-cell expansion.
                ApplyResourceDensity(bm, bmID, l.ResourceDensityMultiplier);
            }
            else if (l != null)
            {
                // multiplier == 1; restore originals so changing back to 1 actually undoes prior scaling
                RestoreResourceDensity(bm, bmID);
            }

            // Force the BiomeMap to recompute internal scratch buffers on next generation.
            try
            {
                BiomeMap.ResetGenerationBuffers();
            }
            catch (Exception ex)
            {
                KickStart.LogWarning($"BiomeMapApplyPatch: ResetGenerationBuffers threw: {ex.Message}");
            }
        }

        private static void ApplyTier1Biome(BiomeMap bm, int bmID, CreationSettings c)
        {
            // Capture originals on first encounter with this BiomeMap.
            CaptureOriginal(s_OriginalContinentSize,      bmID, () => Reflect.GetField<float>(bm, "m_BiomeDistributionScaleMacro"));
            CaptureOriginal(s_OriginalEnableRegions,      bmID, () => Reflect.GetField<bool>(bm,  "enableRegions"));
            CaptureOriginal(s_OriginalBandTolerance,      bmID, () => Reflect.GetField<float>(bm, "bandTolerance"));

            object advParams = Reflect.GetField<object>(bm, "m_AdvancedParameters");
            if (advParams == null)
            {
                KickStart.LogWarning("BiomeMapApplyPatch: m_AdvancedParameters is null on this BiomeMap");
                return;
            }
            CaptureOriginal(s_OriginalVCellVarianceMacro,  bmID, () => Reflect.GetField<float>(advParams, "vCellVarianceMacro"));
            CaptureOriginal(s_OriginalVCellVarianceMajor,  bmID, () => Reflect.GetField<float>(advParams, "vCellVarianceMajor"));
            CaptureOriginal(s_OriginalVCellVarianceMinor,  bmID, () => Reflect.GetField<float>(advParams, "vCellVarianceMinor"));
            CaptureOriginal(s_OriginalVCellVarianceMicro,  bmID, () => Reflect.GetField<float>(advParams, "vCellVarianceMicro"));
            CaptureOriginal(s_OriginalHeightmapDetail,     bmID, () => Reflect.GetField<int>(advParams,   "heightmapResolutionPerCell"));
            CaptureOriginal(s_OriginalSplatDetail,         bmID, () => Reflect.GetField<int>(advParams,   "multiTextureResolutionPerCell"));

            // Apply user values.
            // bandTolerance is the inverse of edge sharpness (sharpness 0 → very wide band /
            // smooth, sharpness 3 → narrow band / razor edges).
            float bandTolerance = c.BiomeEdgeSharpness > 0.001f ? 1f / c.BiomeEdgeSharpness : 1000f;
            // ContinentSize is a user-facing "biome size multiplier" where 1.0 = vanilla,
            // 2.0 = biomes twice as big, 0.5 = half. The engine's m_BiomeDistributionScaleMacro
            // is a density (inverse relationship), so engine = 0.7 (vanilla) / slider.
            // The clamp at 1.3 prevents the white-landscape glitch that occurs above ~1.5;
            // the lower bound 0.05 prevents pathological huge biomes.
            float sliderSafe = Mathf.Max(0.01f, c.ContinentSize);
            float biomeScaleMacro = Mathf.Clamp(0.7f / sliderSafe, 0.05f, 1.3f);

            Reflect.SetField(bm,        "m_BiomeDistributionScaleMacro", biomeScaleMacro);
            Reflect.SetField(bm,        "enableRegions",                 c.EnableRegions);
            Reflect.SetField(bm,        "bandTolerance",                 bandTolerance);
            Reflect.SetField(advParams, "vCellVarianceMacro",            c.BiomeChaos);
            Reflect.SetField(advParams, "vCellVarianceMajor",            c.BiomeChaos);
            Reflect.SetField(advParams, "vCellVarianceMinor",            c.BiomeChaos);
            Reflect.SetField(advParams, "vCellVarianceMicro",            c.BiomeChaos);
            Reflect.SetField(advParams, "heightmapResolutionPerCell",    c.HeightmapDetail);
            Reflect.SetField(advParams, "multiTextureResolutionPerCell", c.SplatDetail);

            KickStart.Log(
                $"BiomeMapApplyPatch Tier1: BiomeSize(user)={c.ContinentSize:0.00} → scaleMacro={biomeScaleMacro:0.00} " +
                $"BiomeChaos={c.BiomeChaos:0.00} " +
                $"EdgeSharpness={c.BiomeEdgeSharpness:0.00} (bandTolerance={bandTolerance:0.00}) " +
                $"EnableRegions={c.EnableRegions} " +
                $"HmDetail={c.HeightmapDetail} SplatDetail={c.SplatDetail}");
        }

        // ------------------------------------------------------------
        // Resource density
        // ------------------------------------------------------------
        private static void ApplyResourceDensity(BiomeMap bm, int bmID, float multiplier)
        {
            BiomeMap.DistributionWeight[] weights = Reflect.GetField<BiomeMap.DistributionWeight[]>(bm, "m_SceneryDistributionWeights");
            if (weights == null)
            {
                KickStart.LogWarning("BiomeMapApplyPatch: m_SceneryDistributionWeights is null on this BiomeMap");
                return;
            }

            int count = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                BiomeMap.DistributionWeight w = weights[i];
                if (w == null) continue;
                string key = bmID + "::" + (w.tag ?? "<null>");

                // Capture originals on first encounter
                if (!s_OriginalDistributionWeights.TryGetValue(key, out float origWeight))
                {
                    origWeight = w.weight;
                    s_OriginalDistributionWeights[key] = origWeight;
                }
                if (!s_OriginalCalculatedMultipliers.TryGetValue(key, out float origCalc))
                {
                    origCalc = w.calculatedMultiplier;
                    s_OriginalCalculatedMultipliers[key] = origCalc;
                }

                // Set BOTH fields. The scatter pipeline consumes calculatedMultiplier;
                // weight is the base from which calc is normally derived. We override both.
                w.weight = origWeight * multiplier;
                w.calculatedMultiplier = origCalc * multiplier;

                // allowGreaterThanOne controls whether oversaturation is permitted. If false,
                // the game caps spawn rates at the natural density. For multipliers > 1 we
                // need this on or our extra spawns get clipped.
                if (multiplier > 1f)
                    w.allowGreaterThanOne = true;

                count++;
            }
            KickStart.Log($"BiomeMapApplyPatch ResourceDensity: scaled {count} DistributionWeights by ×{multiplier:0.00} (both weight and calculatedMultiplier; allowGreaterThanOne forced true for mult>1)");

            // Sample log of first few entries so the user can see the actual values applied
            int sampleCount = Mathf.Min(weights.Length, 3);
            for (int i = 0; i < sampleCount; i++)
            {
                BiomeMap.DistributionWeight w = weights[i];
                if (w == null) continue;
                string tagDisplay = w.tag ?? "<null>";
                KickStart.Log($"  [tag={tagDisplay}] weight={w.weight:0.00} calculatedMultiplier={w.calculatedMultiplier:0.00} allowGreaterThanOne={w.allowGreaterThanOne}");
            }
        }

        private static void RestoreResourceDensity(BiomeMap bm, int bmID)
        {
            BiomeMap.DistributionWeight[] weights = Reflect.GetField<BiomeMap.DistributionWeight[]>(bm, "m_SceneryDistributionWeights");
            if (weights == null) return;
            int restored = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                BiomeMap.DistributionWeight w = weights[i];
                if (w == null) continue;
                string key = bmID + "::" + (w.tag ?? "<null>");
                if (s_OriginalDistributionWeights.TryGetValue(key, out float origWeight))
                {
                    w.weight = origWeight;
                    restored++;
                }
                if (s_OriginalCalculatedMultipliers.TryGetValue(key, out float origCalc))
                {
                    w.calculatedMultiplier = origCalc;
                }
            }
            if (restored > 0)
                KickStart.Log($"BiomeMapApplyPatch ResourceDensity: restored {restored} originals (mult=1)");
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static void CaptureOriginal<TKey, TVal>(Dictionary<TKey, TVal> cache, TKey key, Func<TVal> read)
        {
            if (cache.ContainsKey(key)) return;
            try { cache[key] = read(); }
            catch (Exception ex) { KickStart.LogWarning($"CaptureOriginal failed for key {key}: {ex.Message}"); }
        }

        public static void ClearCache()
        {
            s_OriginalContinentSize.Clear();
            s_OriginalBandTolerance.Clear();
            s_OriginalEnableRegions.Clear();
            s_OriginalVCellVarianceMacro.Clear();
            s_OriginalVCellVarianceMajor.Clear();
            s_OriginalVCellVarianceMinor.Clear();
            s_OriginalVCellVarianceMicro.Clear();
            s_OriginalHeightmapDetail.Clear();
            s_OriginalSplatDetail.Clear();
            s_OriginalDistributionWeights.Clear();
            s_OriginalCalculatedMultipliers.Clear();
        }
    }
}
