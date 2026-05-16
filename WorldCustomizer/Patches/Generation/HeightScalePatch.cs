using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Transpiler on <c>TileManager.CreateTile</c>. Rewrites the literal <c>100f</c>
    /// inside the line
    ///   <c>component.terrainData.size = new Vector3(TileSize, 100f, TileSize);</c>
    /// to a call to <see cref="GetScaledTerrainSizeY"/>, which multiplies the vanilla
    /// 100m vertical world-range by <see cref="CreationSettings.HeightMultiplier"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a transpiler, not a postfix.</b> <c>CreateTile</c> writes the
    /// (unscaled) size.y, then — still inside the same method — calls
    /// <c>SpawnLandmarks</c>, <c>SpawnOverlappingSceneryBlockers</c>, and
    /// <c>SpawnStuntRamps</c>, all of which sample <c>Terrain.SampleHeight</c> to
    /// position monuments / vendors / ramps. A postfix runs after the spawners, so it
    /// can stretch the visible terrain mesh but leaves the just-placed assets at the
    /// pre-scaled Y — floating above the ground at mult&gt;1 or buried at mult&lt;1.
    /// Replacing the literal at IL level makes the engine itself set the correct size
    /// before any downstream spawner reads it.</para>
    ///
    /// <para><b>Why heightmap-buffer scaling doesn't work.</b> An earlier attempt
    /// multiplied the heightmap buffer in <c>BiomeMap.GenerateHeightMap</c>. Unity's
    /// heightmap range is [0,1]; values &gt; 1 clamp, so multipliers above 1 flatten
    /// the peaks instead of raising them. Scaling <c>size.y</c> instead keeps the
    /// heightmap normalized and stretches the world-unit range each step represents.</para>
    /// </remarks>
    [HarmonyPatch(typeof(TileManager), "CreateTile")]
    internal static class HeightScalePatch
    {
        /// <summary>The hard-coded vanilla value of <c>terrainData.size.y</c>.</summary>
        public const float DefaultTerrainSizeY = 100f;

        private static bool s_LoggedFirstFire;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Decompiled CreateTile (2026-05 build) contains exactly one `ldc.r4 100`
            // — the vertical size in the `new Vector3(TileSize, 100f, TileSize)`
            // expression on the `terrainData.size = ...` line. We replace it with a
            // call to GetScaledTerrainSizeY(). If a future engine update changes the
            // method body so the count is not 1, the warning below makes the failure
            // visible instead of silently no-op'ing.
            MethodInfo helper = AccessTools.Method(typeof(HeightScalePatch), nameof(GetScaledTerrainSizeY));
            int replacements = 0;
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && f == DefaultTerrainSizeY)
                {
                    yield return new CodeInstruction(OpCodes.Call, helper);
                    replacements++;
                    continue;
                }
                yield return ci;
            }
            if (replacements != 1)
            {
                KickStart.LogWarning($"HeightScalePatch transpiler: expected 1 replacement of `100f` in CreateTile, found {replacements}. Height multiplier will not apply correctly.");
            }
        }

        /// <summary>
        /// Returns <c>100f × HeightMultiplier</c>, read from the live settings. Public
        /// so the transpiler's <c>Call</c> target can resolve it.
        /// </summary>
        public static float GetScaledTerrainSizeY()
        {
            CreationSettings c = SettingsStore.Current?.Creation;
            float mult = c?.HeightMultiplier ?? 1f;
            float result = DefaultTerrainSizeY * mult;
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"HeightScalePatch first fire: HeightMultiplier={mult}, size.y={result}");
            }
            return result;
        }
    }
}
