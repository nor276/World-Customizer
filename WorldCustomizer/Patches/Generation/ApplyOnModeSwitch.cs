using System;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Subscribed to <c>ManGameMode.ModeSwitchEvent</c>. Promotes
    /// <see cref="SettingsStore.Pending"/> into <see cref="SettingsStore.Current"/>, then
    /// applies the active settings to the live <c>ManWorld</c> + <c>BiomeMap</c> plus all
    /// downstream subsystems before the first tile streams.
    /// </summary>
    /// <remarks>
    /// Tier-1 fields are reflection-mutated on the singletons. Tier-2 fields are applied
    /// either by walking loaded instances now (resource yield, AutoMiner speed) or by
    /// being read on demand by Harmony Postfix patches (drill speed, height multiplier).
    /// Atmosphere is a curve replacement on the ManWorld field.
    /// </remarks>
    internal static class ApplyOnModeSwitch
    {
        public static void OnModeSwitch()
        {
            try
            {
                SettingsStore.Promote();
                Settings active = SettingsStore.Current;
                if (active == null)
                {
                    KickStart.Log("OnModeSwitch: no settings to apply (defaults assumed)");
                    return;
                }

                ApplyTier1(active.Creation);
                ApplyTier2(active.Live);

                KickStart.Log("OnModeSwitch: settings applied");
            }
            catch (Exception ex)
            {
                KickStart.LogError("OnModeSwitch: apply failed", ex);
            }
        }

        // ------------------------------------------------------------
        // Tier 1 — geometry-affecting; logged only.
        // ------------------------------------------------------------
        // Biome-side Tier-1 settings (ContinentSize, BiomeChaos, EdgeSharpness, EnableRegions,
        // HeightmapDetail, SplatDetail) are NOT applied here — at ModeSwitchEvent time
        // CurrentBiomeMap is still null. They moved to BiomeMapApplyPatch which Postfixes
        // ManWorld.Reset(BiomeMap) for guaranteed-non-null timing.
        //
        // m_CellsPerTileEdge and m_CellScale (TileResolution / CellScale) are NOT mutated.
        // The engine snapshots both into static caches (invCellScale, s_NumTileInfoPoints,
        // kInvTileSize, kTileHalfDiagonal) plus several JIT-inlined references at startup;
        // changing the live field at runtime would desync those and crash tile generation.
        // The UI shows them as read-only.
        private static void ApplyTier1(CreationSettings c)
        {
            if (c == null) return;

            ManWorld world = Singleton.Manager<ManWorld>.inst;
            if (world == null)
            {
                KickStart.LogWarning("ApplyTier1: ManWorld.inst is null; skipping");
                return;
            }

            int curRes = Reflect.GetField<int>(world, "m_CellsPerTileEdge");
            float curScale = Reflect.GetField<float>(world, "m_CellScale");
            if (c.TileResolution != curRes || !Mathf.Approximately(c.CellScale, curScale))
            {
                KickStart.Log($"ApplyTier1: TileResolution/CellScale change requested ({curRes}/{curScale} -> {c.TileResolution}/{c.CellScale}) but suppressed (engine static caches forbid runtime change)");
            }

            KickStart.Log("ApplyTier1: biome-side settings deferred to BiomeMapApplyPatch (fires when ManWorld.Reset assigns the BiomeMap)");
        }

        // ------------------------------------------------------------
        // Tier 2 — gameplay multipliers; safe to re-apply at any time.
        // ------------------------------------------------------------
        private static void ApplyTier2(LiveSettings l)
        {
            if (l == null) return;

            // Atmosphere is read on demand by AtmospherePatch's postfix on
            // ModuleWing+AerofoilState.CalculateForce, so there's nothing to push here.
            // The call kept for symmetry; AtmospherePatch.Apply is intentionally a no-op
            // stub.
            AtmospherePatch.Apply(l.AtmosphereDensityMultiplier);

            // Resource yield + AutoMiner speed: walk currently loaded instances and apply
            // (uses cached originals so re-application is idempotent).
            ResourceYieldPatch.ApplyToAllLoaded(l.ResourceYieldMultiplier);
            AutoMinerSpeedPatch.ApplyToAllLoaded(l.AutoMinerSpeedMultiplier);

            // SCU / pickup / item-holder family — same walk-then-apply pattern.
            PickupRangePatch.ApplyToAllLoaded(l.ScuPickupRangeMultiplier);
            BeamStrengthPatch.ApplyToAllLoaded(l.ScuBeamStrengthMultiplier);
            StackCapacityPatch.ApplyToAllLoaded(l.ScuStackCapacityMultiplier);
            LiftHeightPatch.ApplyToAllLoaded(l.ScuLiftHeightMultiplier);
            PickupSpeedPatch.ApplyToAllLoaded(l.ScuPickupSpeedMultiplier);

            // Global lift-correction (paired with LiftHeightPatch's per-instance work).
            // MultiPickPatch reads the multiplier directly each tick — no apply-time walk
            // needed for that one.
            ScuGlobals.ApplyLiftCorrection(l.ScuLiftHeightMultiplier);

            // Loose-item physics-pressure relief: scale autoExpire timers. The MaxLooseItemCount
            // cap is applied via Harmony getter Postfix (MaxLooseItemPatch), so no call needed
            // here for that piece.
            LooseItemPatches.ApplyLooseItemLifetime(l.LooseItemLifetimeMultiplier);

            // Drill speed and HeightMultiplier are read on demand by their own postfixes —
            // no walk needed at apply time.

            KickStart.Log($"ApplyTier2: density={l.ResourceDensityMultiplier:0.00} yield={l.ResourceYieldMultiplier:0.00} drill={l.DrillSpeedMultiplier:0.00} miner={l.AutoMinerSpeedMultiplier:0.00} atmo={l.AtmosphereDensityMultiplier:0.00} scuRange={l.ScuPickupRangeMultiplier:0.00} scuBeam={l.ScuBeamStrengthMultiplier:0.00} scuStack={l.ScuStackCapacityMultiplier:0.00} scuLift={l.ScuLiftHeightMultiplier:0.00} scuSpeed={l.ScuPickupSpeedMultiplier:0.00} scuMulti={l.ScuItemsPerTickMultiplier:0.00} maxLoose={l.MaxLooseItemCount} lifetime×{l.LooseItemLifetimeMultiplier:0.00}");
        }

    }
}
