using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Postfix on <c>ModuleDrill.GetHitDamage</c>. Scales the per-hit damage by
    /// <see cref="LiveSettings.DrillSpeedMultiplier"/>. Cheap; runs every drill tick.
    /// </summary>
    /// <remarks>
    /// Affects manual drills only. AutoMiner extraction speed is handled separately via
    /// <see cref="AutoMinerSpeedPatch"/> on <c>ResourceReservoir.m_ExtractionSpeedMultiplier</c>.
    /// Both player and enemy drills are affected; if per-team scaling is wanted, branch on
    /// <c>__instance.block?.tank?.Team</c>.
    /// </remarks>
    [HarmonyPatch(typeof(ModuleDrill), nameof(ModuleDrill.GetHitDamage))]
    internal static class DrillSpeedPatch
    {
        private static void Postfix(ref float __result)
        {
            LiveSettings l = SettingsStore.Current?.Live;
            if (l == null) return;
            if (Mathf.Approximately(l.DrillSpeedMultiplier, 1f)) return;
            __result *= l.DrillSpeedMultiplier;
        }
    }
}
