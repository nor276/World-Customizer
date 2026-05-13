using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Two-pronged loose-item physics-pressure relief:
    /// <list type="bullet">
    /// <item><see cref="MaxLooseItemPatch"/> postfixes the static getter
    /// <c>QualitySettingsExtended.MaxLooseItemCount</c> to return our chosen cap. TT's existing
    /// <c>ManLooseChunkLimiter</c> reads that getter once per tick and recycles the worst-scoring
    /// loose Visible (by weighted distance × clump density × per-type duplicate count) once
    /// exceeded — so junk piles up = junk gets removed first, automatically.</item>
    /// <item><see cref="LooseItemPatches.ApplyLooseItemLifetime"/> scales the three
    /// <c>Globals.autoExpireTimeout*</c> fields. Vanilla = 300s per loose chunk/block/crate;
    /// multiplier 0.2 = 60s, 5.0 = 25 min. Shorter timer keeps the steady-state count down
    /// without relying on the limiter's emergency-recycle path.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>The cap is applied via a Harmony getter Postfix rather than writing to the
    /// underlying field, because <c>QualitySettingsExtended.CurrentQualityLevel</c> is private
    /// and returns its struct by value (mutating the returned copy is a no-op). Postfixing the
    /// getter is cleaner — no original to cache, no risk of leaving the game's quality preset
    /// altered.</para>
    /// <para>The autoExpireTimeout fields on <c>Globals.inst</c> are plain public fields, so
    /// those are written directly with originals cached for <see cref="Restore"/> on
    /// <c>KickStart.DeInit</c>.</para>
    /// </remarks>
    [HarmonyPatch(typeof(QualitySettingsExtended), nameof(QualitySettingsExtended.MaxLooseItemCount), MethodType.Getter)]
    internal static class MaxLooseItemPatch
    {
        private static bool s_LoggedFirstFire;

        private static void Postfix(ref int __result)
        {
            LiveSettings l = SettingsStore.Current?.Live;
            if (l == null) return;

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"MaxLooseItemPatch.Postfix first fire: vanilla={__result}, override={l.MaxLooseItemCount}");
            }

            __result = l.MaxLooseItemCount;
        }

        public static void ClearCache() => s_LoggedFirstFire = false;
    }

    internal static class LooseItemPatches
    {
        private static float? s_OriginalChunksTimeout;
        private static float? s_OriginalBlocksTimeout;
        private static float? s_OriginalCratesTimeout;

        public static void ApplyLooseItemLifetime(float multiplier)
        {
            Globals g = Globals.inst;
            if (g == null)
            {
                KickStart.LogWarning("ApplyLooseItemLifetime: Globals.inst is null; skipping");
                return;
            }

            if (!s_OriginalChunksTimeout.HasValue) s_OriginalChunksTimeout = g.autoExpireTimeoutChunks;
            if (!s_OriginalBlocksTimeout.HasValue) s_OriginalBlocksTimeout = g.autoExpireTimeoutBlocks;
            if (!s_OriginalCratesTimeout.HasValue) s_OriginalCratesTimeout = g.autoExpireTimeoutCrates;

            g.autoExpireTimeoutChunks = Mathf.Max(1f, s_OriginalChunksTimeout.Value * multiplier);
            g.autoExpireTimeoutBlocks = Mathf.Max(1f, s_OriginalBlocksTimeout.Value * multiplier);
            g.autoExpireTimeoutCrates = Mathf.Max(1f, s_OriginalCratesTimeout.Value * multiplier);

            KickStart.Log($"LooseItemPatches: autoExpireTimeout chunks={g.autoExpireTimeoutChunks:0} blocks={g.autoExpireTimeoutBlocks:0} crates={g.autoExpireTimeoutCrates:0} (×{multiplier:0.00})");
        }

        public static void Restore()
        {
            Globals g = Globals.inst;
            if (g == null) return;
            if (s_OriginalChunksTimeout.HasValue) g.autoExpireTimeoutChunks = s_OriginalChunksTimeout.Value;
            if (s_OriginalBlocksTimeout.HasValue) g.autoExpireTimeoutBlocks = s_OriginalBlocksTimeout.Value;
            if (s_OriginalCratesTimeout.HasValue) g.autoExpireTimeoutCrates = s_OriginalCratesTimeout.Value;
            s_OriginalChunksTimeout = null;
            s_OriginalBlocksTimeout = null;
            s_OriginalCratesTimeout = null;
        }
    }
}
