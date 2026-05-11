using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Postfix on <c>BiomeMap.GenerateSceneryMap</c>. Modifies the populated
    /// <c>sceneryPlacement[,]</c> SpawnDetail grid <em>before</em>
    /// <c>TileManager.AddSceneryPatchesToQueue</c> reads it, to scale actual visible
    /// scatter density uniformly across all biomes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this hook and not the cutoff:</b> TerraTech's scatter is cell-based.
    /// Each scenery cell holds up to 3 TerrainObject refs (t0/t1/t2), placed at one
    /// jittered world position. The vanilla pipeline either fills a cell (t0 != null)
    /// or skips it (t0 == null) via the DetailLayer's cutoff. Most biomes have one
    /// "common" DetailLayer with cutoff &lt;= 0 that always passes — so shifting
    /// cutoffs only adds density in biomes with rare/specialty overlay layers.</para>
    /// <para><b>Approach:</b> for M &gt; 1, three phases:
    /// (A) declutter — strip <c>IsSimpleMeshMergeable</c> objects (grass tufts, pebbles)
    /// with probability 1 - 1/M²;
    /// (B) multi-wave expansion — run ⌊M-1⌋ full waves + one fractional wave. Each wave
    /// re-snapshots populated cells (so cells filled in earlier waves participate),
    /// shuffles for fairness, and each cell fills one random empty neighbor with its
    /// first non-mergeable slot. Multi-wave lets density propagate beyond the immediate
    /// 8-neighbor cap of a single pass.
    /// For M &lt; 1, randomly drop populated cells with probability 1-M.</para>
    /// <para><b>Determinism:</b> seeded by tile coords so the same tile always mutates
    /// identically — important for tile streaming and reload-from-save consistency.</para>
    /// </remarks>
    [HarmonyPatch(typeof(BiomeMap), nameof(BiomeMap.GenerateSceneryMap))]
    internal static class SceneryCellDensityPatch
    {
        private static bool s_LoggedFirstFire;

        // 8-neighbor offsets. Order is arbitrary — we pick uniformly at random.
        private static readonly int[] s_NeighborDX = { -1,  0,  1,  0, -1,  1, -1,  1 };
        private static readonly int[] s_NeighborDY = {  0, -1,  0,  1, -1, -1,  1,  1 };

        private static void Postfix(BiomeMap __instance, WorldTile tile)
        {
            try
            {
                LiveSettings l = SettingsStore.Current?.Live;
                if (l == null) return;

                float multiplier = l.ResourceDensityMultiplier;
                if (Mathf.Approximately(multiplier, 1f)) return;

                if (tile == null || tile.BiomeMapData == null) return;
                BiomeMap.SpawnDetail[,] sp = tile.BiomeMapData.sceneryPlacement;
                if (sp == null) return;

                int rows = sp.GetLength(0);
                int cols = sp.GetLength(1);

                // Per-tile RNG seeded deterministically from tile coord so re-generation
                // and tile streaming produce identical results.
                uint seed = (uint)(tile.Coord.x * 73856093) ^ (uint)(tile.Coord.y * 19349663) ^ 0xA5C3F1u;
                var rng = new System.Random(unchecked((int)seed));

                int populatedBefore = 0;
                int populatedAfter;
                int strippedDecorations = 0;

                if (multiplier < 1f)
                {
                    // Sparsify: drop populated cells with probability 1 - M.
                    int cleared = 0;
                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                        {
                            if (sp[i, j].t0 != null)
                            {
                                populatedBefore++;
                                if (rng.NextDouble() > multiplier)
                                {
                                    sp[i, j].Clear();
                                    cleared++;
                                }
                            }
                        }
                    }
                    populatedAfter = populatedBefore - cleared;
                }
                else
                {
                    // Densify with aggressive clutter removal.
                    //
                    // PHASE A — Declutter pass. TerraTech tags small instanced scatter
                    // (grass tufts, pebbles, gravel, small bushes) with
                    // IsSimpleMeshMergeable=true so it renders via the cheap clutter
                    // pass. We use that same flag to identify clutter and strip it from
                    // any slot it occupies, with probability 1 - 1/M² (M=2→75%,
                    // M=3→89%, M=5→96%, M=10→99%). Slots are then compacted so non-null
                    // entries fill t0 → t1 → t2 in order.
                    float stripProb = 1f - 1f / (multiplier * multiplier);
                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                        {
                            TerrainObject t0 = sp[i, j].t0;
                            TerrainObject t1 = sp[i, j].t1;
                            TerrainObject t2 = sp[i, j].t2;
                            bool changed = false;

                            if (t0 != null && t0.IsSimpleMeshMergeable && rng.NextDouble() < stripProb) { t0 = null; changed = true; strippedDecorations++; }
                            if (t1 != null && t1.IsSimpleMeshMergeable && rng.NextDouble() < stripProb) { t1 = null; changed = true; strippedDecorations++; }
                            if (t2 != null && t2.IsSimpleMeshMergeable && rng.NextDouble() < stripProb) { t2 = null; changed = true; strippedDecorations++; }

                            if (changed)
                            {
                                TerrainObject keep0 = null, keep1 = null, keep2 = null;
                                if (t0 != null) keep0 = t0;
                                if (t1 != null) { if (keep0 == null) keep0 = t1; else keep1 = t1; }
                                if (t2 != null) { if (keep0 == null) keep0 = t2; else if (keep1 == null) keep1 = t2; else keep2 = t2; }
                                sp[i, j].t0 = keep0;
                                sp[i, j].t1 = keep1;
                                sp[i, j].t2 = keep2;
                                // Count populated cells incrementally — avoids a full extra
                                // grid scan before the wave loop. After strip + compaction,
                                // keep0 reflects the final t0 state for this cell.
                                if (keep0 != null) populatedBefore++;
                            }
                            else if (t0 != null)
                            {
                                populatedBefore++;
                            }
                        }
                    }

                    // PHASE B — multi-wave expansion. Each wave:
                    //   1. Re-snapshot the populated cells (cells filled in previous waves
                    //      participate, letting density propagate beyond the 8-neighbor cap).
                    //   2. Shuffle the snapshot (Fisher-Yates) so rare resource types get
                    //      fair-share opportunity to claim empty neighbors. Without the
                    //      shuffle, common cells claimed all the empty neighbors first in
                    //      row-major order, choking out rare resources.
                    //   3. For each populated cell, enumerate its empty in-bounds neighbors
                    //      and pick one of those at random (rather than picking a random
                    //      direction and wasting attempts when the chosen cell is already
                    //      filled). The cell's first non-mergeable slot — the actual
                    //      resource — is what gets copied.
                    //
                    // Wave count is floor(M-1) full waves + 1 fractional wave with prob
                    // (M - 1 - floor(M-1)). This gives roughly M× density (potentially
                    // exponential in dense regions, capped by saturation).
                    int wholeWaves = (int)Math.Floor(multiplier - 1f);
                    float fractional = (multiplier - 1f) - wholeWaves;

                    var waveRows = new List<int>(rows * cols / 4);
                    var waveCols = new List<int>(rows * cols / 4);
                    int[] neighborScratch = new int[8];

                    for (int wave = 0; wave < wholeWaves; wave++)
                    {
                        DoExpansionWave(sp, rows, cols, rng, fillProb: 1f, waveRows, waveCols, neighborScratch);
                    }
                    if (fractional > 0f)
                    {
                        DoExpansionWave(sp, rows, cols, rng, fractional, waveRows, waveCols, neighborScratch);
                    }

                    // The final count is only needed for the first-fire log, so skip the
                    // full grid scan on subsequent tiles. populatedAfter stays 0 in that
                    // case and only the (already-skipped) log line would read it.
                    populatedAfter = s_LoggedFirstFire ? 0 : CountPopulated(sp, rows, cols);
                }

                if (!s_LoggedFirstFire)
                {
                    s_LoggedFirstFire = true;
                    int total = rows * cols;
                    string stripNote = multiplier > 1f ? $", stripped {strippedDecorations} clutter slots (p={1f - 1f/(multiplier*multiplier):0.00})" : "";
                    KickStart.Log(
                        $"SceneryCellDensityPatch FIRST FIRE: tile=({tile.Coord.x},{tile.Coord.y}) " +
                        $"grid={rows}x{cols}={total} cells; M={multiplier:0.00}; " +
                        $"populated {populatedBefore} -> {populatedAfter} " +
                        $"({100f * populatedBefore / total:0.0}% -> {100f * populatedAfter / total:0.0}%){stripNote}");
                }
            }
            catch (Exception ex)
            {
                KickStart.LogError("SceneryCellDensityPatch.Postfix threw", ex);
            }
        }

        private static int CountPopulated(BiomeMap.SpawnDetail[,] sp, int rows, int cols)
        {
            int count = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    if (sp[i, j].t0 != null) count++;
            return count;
        }

        /// <summary>First non-mergeable TerrainObject among the slots, or null if every
        /// slot is null or mergeable.</summary>
        private static TerrainObject PickNonMergeable(TerrainObject t0, TerrainObject t1, TerrainObject t2)
        {
            if (t0 != null && !t0.IsSimpleMeshMergeable) return t0;
            if (t1 != null && !t1.IsSimpleMeshMergeable) return t1;
            if (t2 != null && !t2.IsSimpleMeshMergeable) return t2;
            return null;
        }

        /// <summary>
        /// One pass of the expansion: shuffle populated cells, then each cell tries
        /// to fill one random empty neighbor with its non-mergeable source object.
        /// Caller manages the wave loop and fractional probabilities.
        /// </summary>
        private static void DoExpansionWave(
            BiomeMap.SpawnDetail[,] sp, int rows, int cols,
            System.Random rng, float fillProb,
            List<int> tempRows, List<int> tempCols, int[] neighborScratch)
        {
            // Snapshot populated cells (including those filled by previous waves)
            tempRows.Clear();
            tempCols.Clear();
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (sp[i, j].t0 != null)
                    {
                        tempRows.Add(i);
                        tempCols.Add(j);
                    }
                }
            }
            int n = tempRows.Count;

            // Fisher-Yates shuffle so all source cells get fair-order opportunity to
            // claim neighbors. Without this, row-major iteration biases against
            // rare resource types (e.g. emeralds vs trees).
            for (int k = n - 1; k > 0; k--)
            {
                int swap = rng.Next(k + 1);
                if (swap != k)
                {
                    int tr = tempRows[k]; tempRows[k] = tempRows[swap]; tempRows[swap] = tr;
                    int tc = tempCols[k]; tempCols[k] = tempCols[swap]; tempCols[swap] = tc;
                }
            }

            for (int p = 0; p < n; p++)
            {
                if (fillProb < 1f && rng.NextDouble() >= fillProb) continue;

                int i = tempRows[p];
                int j = tempCols[p];

                TerrainObject sourceMain = PickNonMergeable(sp[i, j].t0, sp[i, j].t1, sp[i, j].t2);
                if (sourceMain == null) sourceMain = sp[i, j].t0;
                if (sourceMain == null) continue;
                float sourceScale = sp[i, j].scale;

                // Enumerate empty in-bounds neighbors so we don't waste attempts on
                // direction-pick collisions.
                int emptyCount = 0;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + s_NeighborDY[d];
                    int nj = j + s_NeighborDX[d];
                    if (ni < 0 || ni >= rows || nj < 0 || nj >= cols) continue;
                    if (sp[ni, nj].t0 != null) continue;
                    neighborScratch[emptyCount++] = d;
                }
                if (emptyCount == 0) continue;

                int chosenDir = neighborScratch[rng.Next(emptyCount)];
                int fi = i + s_NeighborDY[chosenDir];
                int fj = j + s_NeighborDX[chosenDir];
                sp[fi, fj].t0 = sourceMain;
                sp[fi, fj].scale = sourceScale;
            }
        }

        public static void ClearCache() { s_LoggedFirstFire = false; }
    }
}
