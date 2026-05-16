using System;
using System.IO;
using Newtonsoft.Json;

namespace WorldCustomizer
{
    /// <summary>
    /// Holds the active settings payload and handles JSON sidecar I/O.
    /// </summary>
    /// <remarks>
    /// State machine:
    /// <list type="bullet">
    /// <item><see cref="Pending"/> is set by the new-game customization flow or by a save-load
    /// hook reading a sidecar. It is consumed by the <c>ModeSwitchEvent</c> handler.</item>
    /// <item><see cref="Current"/> holds the settings active in the running world. It is
    /// promoted from <see cref="Pending"/> at world-init and read by all generation patches.</item>
    /// </list>
    /// </remarks>
    public static class SettingsStore
    {
        private const string SidecarSuffix = ".worldgen.json";

        /// <summary>Newly chosen settings, awaiting application by the world-init hook.</summary>
        public static Settings Pending { get; set; }

        /// <summary>Settings currently in effect for the loaded world. Null when no world is loaded.</summary>
        public static Settings Current { get; private set; }

        /// <summary>
        /// Promote Pending to Current. Called from the ModeSwitchEvent handler at world
        /// load. If Pending is null, falls back to defaults.
        /// </summary>
        public static void Promote()
        {
            bool hadPending = Pending != null;
            bool hadCurrent = Current != null;

            // Promote priority: Pending (newly chosen) > Current (already in effect) > defaults.
            // The fallback to Current matters when the mod loader recycles us mid-session: a
            // re-fired ModeSwitchEvent with Pending=null should NOT overwrite the user's
            // settings with defaults.
            Settings s = Pending ?? Current ?? Settings.CreateDefaults();
            s.Sanitize();
            Current = s;
            Pending = null;

            KickStart.Log($"SettingsStore.Promote: hadPending={hadPending} hadCurrent={hadCurrent} → drill={Current.Live.DrillSpeedMultiplier:0.00} yield={Current.Live.ResourceYieldMultiplier:0.00}");
        }

        /// <summary>Discard pending settings without applying. Called when the user cancels new-game.</summary>
        public static void DiscardPending()
        {
            Pending = null;
        }

        /// <summary>Clear all in-memory state. Called on world-exit.</summary>
        public static void Clear()
        {
            Pending = null;
            Current = null;
        }

        /// <summary>
        /// Returns the sidecar path for a given save file. The sidecar is sibling to the
        /// save with a fixed suffix.
        /// </summary>
        public static string GetSidecarPath(string saveFilePath)
        {
            if (string.IsNullOrEmpty(saveFilePath))
                throw new ArgumentException("saveFilePath must be non-empty", nameof(saveFilePath));
            return saveFilePath + SidecarSuffix;
        }

        /// <summary>
        /// Read settings from the given path. Returns null when the file is absent,
        /// unreadable, or contains malformed JSON. Failure is logged, not thrown.
        /// </summary>
        public static Settings LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                Settings result = JsonConvert.DeserializeObject<Settings>(json);
                if (result == null) return null;
                if (result.Creation == null) result.Creation = new CreationSettings();
                if (result.Live == null) result.Live = new LiveSettings();
                result.Sanitize();
                return result;
            }
            catch (Exception ex)
            {
                KickStart.LogError($"Failed to read settings from '{path}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Write settings to the given path. Returns true on success. Failure is logged,
        /// not thrown.
        /// </summary>
        /// <remarks>
        /// <para><b>Atomic-replace pattern.</b> Serialize to a same-directory temp file,
        /// then <c>File.Replace</c> onto the target (or <c>File.Move</c> for first-write
        /// when the target doesn't exist yet). Both operations are atomic on NTFS for
        /// same-volume sources, so a crash mid-write leaves either the old sidecar fully
        /// intact or the new one fully written — never a truncated mix. This is stronger
        /// than the engine's <c>WriteSaveDataToDisk</c> pattern (which uses
        /// <c>File.Copy(overwrite: true)</c>, a non-atomic open-truncate-stream sequence)
        /// because the engine's temp lives in <c>%TEMP%</c>, often a different volume.
        /// We keep the temp next to the target so <c>File.Replace</c> is available.</para>
        ///
        /// <para>On serializer failure the target is never touched. On a crash between
        /// <c>WriteAllText</c> and <c>File.Replace/Move</c> the temp may be left behind;
        /// the <c>finally</c> deletes it on the normal path, and any orphan from a hard
        /// crash gets overwritten on the next save (random temp names don't collide).</para>
        /// </remarks>
        public static bool SaveToFile(string path, Settings settings)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (settings == null) return false;

            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
            {
                KickStart.LogError($"SaveToFile: cannot resolve directory for '{path}'");
                return false;
            }

            string tempPath = null;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                do
                {
                    tempPath = Path.Combine(dir, Path.GetRandomFileName() + ".worldgen.tmp");
                } while (File.Exists(tempPath));

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(tempPath, json);

                // File.Replace requires the target to exist; File.Move can't overwrite on
                // .NET Framework 4.6.1. Branch on existence — both calls consume the temp,
                // so the finally's Delete is a no-op on success.
                if (File.Exists(path))
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                else
                    File.Move(tempPath, path);

                return true;
            }
            catch (Exception ex)
            {
                KickStart.LogError($"Failed to write settings to '{path}'", ex);
                return false;
            }
            finally
            {
                if (tempPath != null)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch (Exception ex) { KickStart.LogWarning($"Failed to delete temp file '{tempPath}': {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// Convenience: load the sidecar associated with a save file path.
        /// </summary>
        public static Settings LoadFor(string saveFilePath)
        {
            return LoadFromFile(GetSidecarPath(saveFilePath));
        }

        /// <summary>
        /// Convenience: write the sidecar associated with a save file path.
        /// </summary>
        public static bool SaveFor(string saveFilePath, Settings settings)
        {
            return SaveToFile(GetSidecarPath(saveFilePath), settings);
        }

        /// <summary>
        /// Load the sidecar for <paramref name="saveFilePath"/>, returning a usable
        /// <see cref="Settings"/> object in every case — never null. Distinguishes the
        /// three outcomes via <paramref name="source"/> so the caller can log
        /// appropriately and the malformed case can surface a warning.
        /// </summary>
        /// <remarks>
        /// Used by the save/load hooks instead of raw <see cref="LoadFor"/> so they don't
        /// each have to branch on missing/malformed/valid, and so a null sidecar is
        /// guaranteed to become explicit defaults rather than falling through
        /// <see cref="Promote"/>'s <c>Pending ?? Current</c> chain (which would otherwise
        /// leak the previous world's settings into the just-loaded one).
        /// </remarks>
        public static Settings LoadForOrDefaults(string saveFilePath, out SidecarLoadSource source)
        {
            string sidecar = GetSidecarPath(saveFilePath);
            if (!File.Exists(sidecar))
            {
                source = SidecarLoadSource.MissingDefaulted;
                return Settings.CreateDefaults();
            }
            Settings loaded = LoadFromFile(sidecar);
            if (loaded == null)
            {
                source = SidecarLoadSource.MalformedDefaulted;
                return Settings.CreateDefaults();
            }
            source = SidecarLoadSource.Sidecar;
            return loaded;
        }
    }

    /// <summary>How <see cref="SettingsStore.LoadForOrDefaults"/> resolved its return value.</summary>
    public enum SidecarLoadSource
    {
        /// <summary>Sidecar file existed and parsed cleanly.</summary>
        Sidecar,
        /// <summary>Sidecar file was absent; defaults returned. Typical for saves created
        /// before this mod was installed (or before this version added persistence).</summary>
        MissingDefaulted,
        /// <summary>Sidecar existed but failed to parse; defaults returned. The player's
        /// per-world customization is effectively lost — worth a visible warning.</summary>
        MalformedDefaulted,
    }
}
