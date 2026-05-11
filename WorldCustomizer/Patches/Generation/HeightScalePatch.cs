using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Postfix on <c>TileManager.CreateTile</c>. Scales <c>terrainData.size.y</c> (the
    /// vertical world-height range, hard-coded to 100m in the original game) by
    /// <see cref="CreationSettings.HeightMultiplier"/>.
    /// </summary>
    /// <remarks>
    /// <para>Earlier attempt: multiply the heightmap buffer in
    /// <c>BiomeMap.GenerateHeightMap</c>. That approach hits Unity's [0,1] heightmap range —
    /// values above 1 get clamped, so multipliers &gt; 1 produce flat-topped mountains
    /// instead of taller ones (no visible difference).</para>
    /// <para>This approach instead scales the world-height range itself. The heightmap
    /// stays normalized [0,1], but each step of "1.0" now corresponds to (100 × mult)
    /// world units. Result: at mult = 5 mountains are five times taller; at mult = 0.1
    /// the world is nearly flat.</para>
    /// </remarks>
    [HarmonyPatch(typeof(TileManager), "CreateTile")]
    internal static class HeightScalePatch
    {
        /// <summary>The hard-coded default the game writes for terrainData.size.y.</summary>
        private const float DefaultTerrainSizeY = 100f;

        private static bool s_LoggedFirstFire;

        private static void Postfix(WorldTile tile)
        {
            CreationSettings c = SettingsStore.Current?.Creation;
            if (c == null) return;
            if (Mathf.Approximately(c.HeightMultiplier, 1f)) return;

            Terrain terrain = tile?.Terrain;
            if (terrain == null) return;
            TerrainData data = terrain.terrainData;
            if (data == null) return;

            Vector3 size = data.size;
            size.y = DefaultTerrainSizeY * c.HeightMultiplier;
            data.size = size;

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"HeightScalePatch.Postfix first fire: HeightMultiplier={c.HeightMultiplier}, size.y={size.y}");
            }
        }
    }
}
