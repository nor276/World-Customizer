using System;
using System.IO;

namespace WorldCustomizer
{
    /// <summary>
    /// Settings round-trip + clamp self-tests, invoked from
    /// <see cref="KickStart.Initiate"/>. Catches regressions in
    /// <see cref="SettingsStore"/> load/save, <see cref="Settings.Sanitize"/>
    /// clamping, and <see cref="SettingsStore.Promote"/> state transitions.
    /// </summary>
    internal static class SettingsSelfTest
    {
        // Run only on the first Initiate. Subsequent mod recycles must NOT re-run because
        // PromoteAndDiscardWork temporarily writes to SettingsStore and clobbering production
        // state mid-session would wipe user-confirmed settings.
        private static bool s_HasRun;

        public static void Run()
        {
            if (s_HasRun) return;
            s_HasRun = true;

            Func<bool>[] tests = {
                RoundTripDefaults,
                RoundTripCustomized,
                SanitizeClampsBadValues,
                SanitizeSnapsTileResolution,
                LoadMissingFileReturnsNull,
                LoadMalformedFileReturnsNull,
                PromoteAndDiscardWork,
                SidecarPathSuffix,
                AtomicSaveLeavesNoTempFile,
                LoadForOrDefaultsSidecar,
                LoadForOrDefaultsMissing,
                LoadForOrDefaultsMalformed,
            };

            int passed = 0;
            int failed = 0;
            foreach (var test in tests)
            {
                if (test()) passed++;
                else failed++;
            }

            int total = passed + failed;
            if (failed == 0)
                KickStart.Log($"Settings self-test: {passed}/{total} passed");
            else
                KickStart.LogError($"Settings self-test: {failed}/{total} FAILED");
        }

        private static bool RoundTripDefaults()
        {
            string path = TempPath("rt-defaults");
            try
            {
                Settings original = Settings.CreateDefaults();
                if (!SettingsStore.SaveToFile(path, original))
                    return Fail(nameof(RoundTripDefaults), "save returned false");

                Settings loaded = SettingsStore.LoadFromFile(path);
                if (loaded == null)
                    return Fail(nameof(RoundTripDefaults), "load returned null");

                if (!SettingsEqual(original, loaded))
                    return Fail(nameof(RoundTripDefaults), "fields differ after round trip");

                return true;
            }
            finally { TryDelete(path); }
        }

        private static bool RoundTripCustomized()
        {
            string path = TempPath("rt-custom");
            try
            {
                Settings original = Settings.CreateDefaults();
                // Values must lie inside the clamp ranges declared in Settings.Sanitize;
                // otherwise the post-load Sanitize step changes them and round-trip equality fails.
                original.Creation.ContinentSize      = 1.5f;
                original.Creation.BiomeChaos         = 0.7f;
                original.Creation.BiomeEdgeSharpness = 2.5f;
                original.Creation.EnableRegions      = false;
                original.Creation.TileResolution     = 128;
                original.Creation.CellScale          = 5.0f;
                original.Creation.HeightmapDetail    = 2;
                original.Creation.SplatDetail        = 4;
                original.Creation.HeightMultiplier   = 1.8f;

                original.Live.ResourceDensityMultiplier   = 2.0f;
                original.Live.ResourceYieldMultiplier     = 3.0f;
                original.Live.DrillSpeedMultiplier        = 5.0f;
                original.Live.AutoMinerSpeedMultiplier    = 4.0f;
                original.Live.AtmosphereDensityMultiplier = 1.5f;
                original.Live.ScuPickupRangeMultiplier    = 2.5f;
                original.Live.ScuBeamStrengthMultiplier   = 3.5f;
                original.Live.ScuStackCapacityMultiplier  = 4.0f;
                original.Live.ScuLiftHeightMultiplier     = 2.0f;
                original.Live.ScuPickupSpeedMultiplier    = 3.0f;
                original.Live.ScuItemsPerTickMultiplier   = 4.5f;
                original.Live.MaxLooseItemCount           = 7500;
                original.Live.LooseItemLifetimeMultiplier = 0.5f;

                if (!SettingsStore.SaveToFile(path, original))
                    return Fail(nameof(RoundTripCustomized), "save returned false");

                Settings loaded = SettingsStore.LoadFromFile(path);
                if (loaded == null)
                    return Fail(nameof(RoundTripCustomized), "load returned null");

                if (!SettingsEqual(original, loaded))
                    return Fail(nameof(RoundTripCustomized), "fields differ after round trip");

                return true;
            }
            finally { TryDelete(path); }
        }

        private static bool SanitizeClampsBadValues()
        {
            Settings s = Settings.CreateDefaults();
            s.Creation.ContinentSize = 999f;
            s.Creation.BiomeChaos = -1f;
            s.Live.DrillSpeedMultiplier = -5f;
            s.Live.AtmosphereDensityMultiplier = 100f;
            s.Live.ScuPickupRangeMultiplier = 999f;
            s.Live.ScuBeamStrengthMultiplier = -1f;
            s.Live.ScuStackCapacityMultiplier = 0f;
            s.Live.ScuLiftHeightMultiplier = 100f;
            s.Live.ScuPickupSpeedMultiplier = -5f;
            s.Live.ScuItemsPerTickMultiplier = 0.1f;
            s.Live.MaxLooseItemCount = 9999999;
            s.Live.LooseItemLifetimeMultiplier = -2f;
            s.Sanitize();

            if (s.Creation.ContinentSize > 5.0f)            return Fail(nameof(SanitizeClampsBadValues), "ContinentSize not clamped high");
            if (s.Creation.BiomeChaos < 0f)                 return Fail(nameof(SanitizeClampsBadValues), "BiomeChaos not clamped low");
            if (s.Live.DrillSpeedMultiplier < 0f)           return Fail(nameof(SanitizeClampsBadValues), "DrillSpeed not clamped low");
            if (s.Live.AtmosphereDensityMultiplier > 10f)   return Fail(nameof(SanitizeClampsBadValues), "Atmosphere not clamped high");
            if (s.Live.ScuPickupRangeMultiplier > 10f)      return Fail(nameof(SanitizeClampsBadValues), "ScuPickupRange not clamped high");
            if (s.Live.ScuBeamStrengthMultiplier < 0.1f)    return Fail(nameof(SanitizeClampsBadValues), "ScuBeamStrength not clamped low");
            if (s.Live.ScuStackCapacityMultiplier < 0.5f)   return Fail(nameof(SanitizeClampsBadValues), "ScuStackCapacity not clamped low");
            if (s.Live.ScuLiftHeightMultiplier > 5f)        return Fail(nameof(SanitizeClampsBadValues), "ScuLiftHeight not clamped high");
            if (s.Live.ScuPickupSpeedMultiplier < 0.1f)     return Fail(nameof(SanitizeClampsBadValues), "ScuPickupSpeed not clamped low");
            if (s.Live.ScuItemsPerTickMultiplier < 1f)      return Fail(nameof(SanitizeClampsBadValues), "ScuItemsPerTick not clamped low");
            if (s.Live.MaxLooseItemCount > 20000)           return Fail(nameof(SanitizeClampsBadValues), "MaxLooseItemCount not clamped high");
            if (s.Live.LooseItemLifetimeMultiplier < 0.1f)  return Fail(nameof(SanitizeClampsBadValues), "LooseItemLifetime not clamped low");
            return true;
        }

        private static bool SanitizeSnapsTileResolution()
        {
            Settings s = Settings.CreateDefaults();
            s.Creation.TileResolution  = 100;   // not in {32, 64, 128, 256}
            s.Creation.HeightmapDetail = 3;     // not in {1, 2, 4}
            s.Creation.SplatDetail     = 5;     // not in {2, 4, 8}
            s.Sanitize();

            if (s.Creation.TileResolution != 128)  return Fail(nameof(SanitizeSnapsTileResolution), $"TileResolution snapped to {s.Creation.TileResolution} expected 128");
            if (s.Creation.HeightmapDetail != 2 && s.Creation.HeightmapDetail != 4)
                return Fail(nameof(SanitizeSnapsTileResolution), $"HeightmapDetail snapped to {s.Creation.HeightmapDetail}");
            if (s.Creation.SplatDetail != 4)       return Fail(nameof(SanitizeSnapsTileResolution), $"SplatDetail snapped to {s.Creation.SplatDetail} expected 4");
            return true;
        }

        private static bool LoadMissingFileReturnsNull()
        {
            string path = TempPath("missing");
            TryDelete(path);
            Settings loaded = SettingsStore.LoadFromFile(path);
            if (loaded != null) return Fail(nameof(LoadMissingFileReturnsNull), "expected null");
            return true;
        }

        private static bool LoadMalformedFileReturnsNull()
        {
            string path = TempPath("malformed");
            try
            {
                File.WriteAllText(path, "{not valid json");
                Settings loaded = SettingsStore.LoadFromFile(path);
                if (loaded != null) return Fail(nameof(LoadMalformedFileReturnsNull), "expected null");
                return true;
            }
            finally { TryDelete(path); }
        }

        private static bool PromoteAndDiscardWork()
        {
            // Save+restore production state so the test never clobbers user-confirmed settings.
            Settings savedPending = SettingsStore.Pending;
            Settings savedCurrent = SettingsStore.Current;

            try
            {
                SettingsStore.Pending = null;
                SettingsStore.Promote();
                if (SettingsStore.Current == null) return Fail(nameof(PromoteAndDiscardWork), "Promote with null Pending should yield defaults");
                if (SettingsStore.Pending != null) return Fail(nameof(PromoteAndDiscardWork), "Pending should be null after promote");

                Settings custom = Settings.CreateDefaults();
                custom.Live.DrillSpeedMultiplier = 7f;
                SettingsStore.Pending = custom;
                SettingsStore.Promote();
                if (SettingsStore.Current?.Live?.DrillSpeedMultiplier != 7f)
                    return Fail(nameof(PromoteAndDiscardWork), "Promote did not transfer Pending");

                SettingsStore.Pending = Settings.CreateDefaults();
                SettingsStore.DiscardPending();
                if (SettingsStore.Pending != null)
                    return Fail(nameof(PromoteAndDiscardWork), "DiscardPending did not clear");

                return true;
            }
            finally
            {
                SettingsStore.Pending = savedPending;
                // Current is private-set on SettingsStore; we promote saved as Current via a
                // round-trip if we need to restore it. For the first-run case both are null
                // anyway, so this finally is a no-op in practice.
                if (savedCurrent != null && SettingsStore.Current != savedCurrent)
                {
                    SettingsStore.Pending = savedCurrent;
                    SettingsStore.Promote();
                    SettingsStore.Pending = savedPending;
                }
            }
        }

        private static bool SidecarPathSuffix()
        {
            string sidecar = SettingsStore.GetSidecarPath(@"C:\saves\MyWorld.json");
            if (sidecar != @"C:\saves\MyWorld.json.worldgen.json")
                return Fail(nameof(SidecarPathSuffix), $"got '{sidecar}'");
            return true;
        }

        /// <summary>
        /// Atomic SaveToFile uses a same-directory temp file then File.Copy. The finally
        /// block must delete the temp file on every code path. A regression that drops the
        /// finally — or moves the temp to %TEMP% — would either leak .tmp files into the
        /// player's saves folder or silently revert to non-atomic File.WriteAllText.
        /// </summary>
        private static bool AtomicSaveLeavesNoTempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "WorldCustomizer-atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "save.worldgen.json");
                Settings original = Settings.CreateDefaults();
                if (!SettingsStore.SaveToFile(path, original))
                    return Fail(nameof(AtomicSaveLeavesNoTempFile), "save returned false");

                if (!File.Exists(path))
                    return Fail(nameof(AtomicSaveLeavesNoTempFile), "target not written");

                string[] siblings = Directory.GetFiles(dir);
                foreach (string s in siblings)
                {
                    if (s.EndsWith(".worldgen.tmp", StringComparison.OrdinalIgnoreCase))
                        return Fail(nameof(AtomicSaveLeavesNoTempFile), $"temp file leaked: '{s}'");
                }
                if (siblings.Length != 1)
                    return Fail(nameof(AtomicSaveLeavesNoTempFile), $"expected 1 file, found {siblings.Length}");
                return true;
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        private static bool LoadForOrDefaultsSidecar()
        {
            string path = TempPath("lfo-sidecar");
            try
            {
                Settings original = Settings.CreateDefaults();
                original.Live.DrillSpeedMultiplier = 9f;
                if (!SettingsStore.SaveFor(path, original))
                    return Fail(nameof(LoadForOrDefaultsSidecar), "save returned false");

                Settings loaded = SettingsStore.LoadForOrDefaults(path, out SidecarLoadSource source);
                if (source != SidecarLoadSource.Sidecar)
                    return Fail(nameof(LoadForOrDefaultsSidecar), $"expected Sidecar source, got {source}");
                if (loaded == null || loaded.Live.DrillSpeedMultiplier != 9f)
                    return Fail(nameof(LoadForOrDefaultsSidecar), "loaded value did not round-trip");
                return true;
            }
            finally { TryDelete(SettingsStore.GetSidecarPath(path)); }
        }

        private static bool LoadForOrDefaultsMissing()
        {
            string path = TempPath("lfo-missing");
            TryDelete(SettingsStore.GetSidecarPath(path));

            Settings loaded = SettingsStore.LoadForOrDefaults(path, out SidecarLoadSource source);
            if (source != SidecarLoadSource.MissingDefaulted)
                return Fail(nameof(LoadForOrDefaultsMissing), $"expected MissingDefaulted, got {source}");
            if (loaded == null)
                return Fail(nameof(LoadForOrDefaultsMissing), "should never return null");
            if (!loaded.IsAllDefaults())
                return Fail(nameof(LoadForOrDefaultsMissing), "returned non-default Settings");
            return true;
        }

        private static bool LoadForOrDefaultsMalformed()
        {
            string path = TempPath("lfo-malformed");
            string sidecar = SettingsStore.GetSidecarPath(path);
            try
            {
                File.WriteAllText(sidecar, "{not valid json");

                Settings loaded = SettingsStore.LoadForOrDefaults(path, out SidecarLoadSource source);
                if (source != SidecarLoadSource.MalformedDefaulted)
                    return Fail(nameof(LoadForOrDefaultsMalformed), $"expected MalformedDefaulted, got {source}");
                if (loaded == null)
                    return Fail(nameof(LoadForOrDefaultsMalformed), "should never return null");
                if (!loaded.IsAllDefaults())
                    return Fail(nameof(LoadForOrDefaultsMalformed), "returned non-default Settings");
                return true;
            }
            finally { TryDelete(sidecar); }
        }

        private static bool SettingsEqual(Settings a, Settings b)
        {
            if (a.FormatVersion != b.FormatVersion) return false;
            if (a.CreatedByModVersion != b.CreatedByModVersion) return false;

            var ac = a.Creation; var bc = b.Creation;
            if (ac.ContinentSize      != bc.ContinentSize)      return false;
            if (ac.BiomeChaos         != bc.BiomeChaos)         return false;
            if (ac.BiomeEdgeSharpness != bc.BiomeEdgeSharpness) return false;
            if (ac.EnableRegions      != bc.EnableRegions)      return false;
            if (ac.TileResolution     != bc.TileResolution)     return false;
            if (ac.CellScale          != bc.CellScale)          return false;
            if (ac.HeightmapDetail    != bc.HeightmapDetail)    return false;
            if (ac.SplatDetail        != bc.SplatDetail)        return false;
            if (ac.HeightMultiplier   != bc.HeightMultiplier)   return false;

            var al = a.Live; var bl = b.Live;
            if (al.ResourceDensityMultiplier   != bl.ResourceDensityMultiplier)   return false;
            if (al.ResourceYieldMultiplier     != bl.ResourceYieldMultiplier)     return false;
            if (al.DrillSpeedMultiplier        != bl.DrillSpeedMultiplier)        return false;
            if (al.AutoMinerSpeedMultiplier    != bl.AutoMinerSpeedMultiplier)    return false;
            if (al.AtmosphereDensityMultiplier != bl.AtmosphereDensityMultiplier) return false;
            if (al.ScuPickupRangeMultiplier    != bl.ScuPickupRangeMultiplier)    return false;
            if (al.ScuBeamStrengthMultiplier   != bl.ScuBeamStrengthMultiplier)   return false;
            if (al.ScuStackCapacityMultiplier  != bl.ScuStackCapacityMultiplier)  return false;
            if (al.ScuLiftHeightMultiplier     != bl.ScuLiftHeightMultiplier)     return false;
            if (al.ScuPickupSpeedMultiplier    != bl.ScuPickupSpeedMultiplier)    return false;
            if (al.ScuItemsPerTickMultiplier   != bl.ScuItemsPerTickMultiplier)   return false;
            if (al.MaxLooseItemCount           != bl.MaxLooseItemCount)           return false;
            if (al.LooseItemLifetimeMultiplier != bl.LooseItemLifetimeMultiplier) return false;

            return true;
        }

        private static bool Fail(string testName, string detail)
        {
            KickStart.LogError($"Settings self-test '{testName}' failed: {detail}");
            return false;
        }

        private static string TempPath(string tag)
        {
            return Path.Combine(Path.GetTempPath(),
                $"WorldCustomizer-{tag}-{Guid.NewGuid():N}.json");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* swallow — temp file cleanup is best-effort */ }
        }
    }
}
