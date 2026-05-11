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
        public static bool SaveToFile(string path, Settings settings)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (settings == null) return false;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                KickStart.LogError($"Failed to write settings to '{path}'", ex);
                return false;
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
    }
}
