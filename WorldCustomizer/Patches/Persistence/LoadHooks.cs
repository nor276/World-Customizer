using System;
using System.IO;
using HarmonyLib;

namespace WorldCustomizer.Patches.Persistence
{
    /// <summary>
    /// Sync-load hook. Postfix on
    /// <c>ManSaveGame.Load(GameType, saveName, saveWorkshopPath)</c>. Reads the
    /// sidecar for the loaded save and parks it in <see cref="SettingsStore.Pending"/>
    /// so the next <c>ModeSwitchEvent</c> promotes it into <see cref="SettingsStore.Current"/>.
    /// </summary>
    /// <remarks>
    /// Always populates <c>Pending</c> on a successful load — either with the parsed
    /// sidecar or with <see cref="Settings.CreateDefaults"/>. Leaving <c>Pending</c> as
    /// null would cause <see cref="SettingsStore.Promote"/>'s
    /// <c>Pending ?? Current ?? defaults</c> chain to reuse the previous world's
    /// <c>Current</c>, which is the exact bug this fix exists to close.
    /// </remarks>
    [HarmonyPatch(typeof(ManSaveGame), nameof(ManSaveGame.Load))]
    internal static class LoadHook
    {
        private static void Postfix(ManGameMode.GameType gameType, string saveName, string saveWorkshopPath, bool __result)
        {
            // Note: we deliberately do NOT clear Pending on load failure. The previously-set
            // Pending may be the user's new-game customization that they'll proceed with
            // after the failed load attempt — clearing it would discard their picks.
            if (!__result) return;

            try
            {
                // Engine treats `!= null` as the workshop-path signal (ManSaveGame.cs:2750).
                // Match that exactly: empty-string would be a malformed call, but if it ever
                // happened the engine would use it verbatim, so we should too.
                string savePath = saveWorkshopPath != null
                    ? saveWorkshopPath
                    : ManSaveGame.CreateGameSaveFilePath(gameType, saveName);

                SidecarLoadHelper.LoadIntoPending(savePath);
            }
            catch (Exception ex)
            {
                KickStart.LogError($"LoadHook: loading sidecar for '{saveName}' threw", ex);
            }
        }
    }

    /// <summary>
    /// Async-load hook. Prefix on <c>ManSaveGame.LoadAsync(GameType, saveName, callback)</c>
    /// that wraps the user-supplied callback. Our wrapper runs first when the engine
    /// invokes the callback at load completion, so <see cref="SettingsStore.Pending"/>
    /// is populated before the callback's continuation triggers <c>ModeSwitchEvent</c>.
    /// </summary>
    /// <remarks>
    /// <para>Wrapping the callback (rather than patching the eventual
    /// <c>CompleteLoad</c> funnel) is necessary because the engine's
    /// <c>LoadSaveDataAsync</c> path doesn't populate <c>SaveInfo.FullFilePath</c> — the
    /// only reliable place to know the source path is right here, where the caller's
    /// arguments are still in scope.</para>
    ///
    /// <para>The path we compute matches the engine: <c>CreateGameSaveFilePath</c>. The
    /// engine's <c>LoadAsync</c> doesn't accept a workshop path, so workshop saves go
    /// through the sync <c>Load</c> path and the postfix above handles them.</para>
    /// </remarks>
    [HarmonyPatch(typeof(ManSaveGame), nameof(ManSaveGame.LoadAsync))]
    internal static class LoadAsyncHook
    {
        private static void Prefix(ManGameMode.GameType gameType, string saveName, ref Action<bool> callback)
        {
            string savePath = ManSaveGame.CreateGameSaveFilePath(gameType, saveName);
            Action<bool> original = callback;
            callback = success =>
            {
                // On success: load sidecar into Pending before the original callback runs
                // (the original is what eventually triggers ModeSwitchEvent → Promote).
                // On failure: leave Pending alone — see LoadHook for the rationale.
                if (success)
                {
                    try { SidecarLoadHelper.LoadIntoPending(savePath); }
                    catch (Exception ex) { KickStart.LogError($"LoadAsyncHook: loading sidecar for '{saveName}' threw", ex); }
                }
                original?.Invoke(success);
            };
        }
    }

    /// <summary>
    /// Mirror the engine's autosave-backup rotation for our sidecar. Postfix on the
    /// private <c>ManSaveGame.BackupSaveFile(string fullPathWithFilename, int maxHistoryCount)</c>
    /// — the method that shifts <c>&lt;save&gt;.sav.bak_N → .bak_(N+1)</c> and copies
    /// the current save to <c>.bak_1</c>. We apply the same shuffle to
    /// <c>&lt;save&gt;.sav.worldgen.json</c> so worldgen settings survive backup rotation.
    /// </summary>
    /// <remarks>
    /// <para><b>Timing.</b> The engine calls <c>BackupSaveFile</c> *before* writing the
    /// new save data. At postfix time the on-disk sidecar is still the OLD world's, which
    /// is what we want to back up. The new sidecar is written later by
    /// <see cref="SaveHook"/>.</para>
    ///
    /// <para><b>Reach.</b> The engine only invokes this for autosaves &gt;10 minutes
    /// apart (or for any save when <c>DebugUtil.DebugSavesEnabled</c>). Player-named
    /// saves are never backed up by the engine, so they aren't here either.</para>
    /// </remarks>
    [HarmonyPatch(typeof(ManSaveGame), "BackupSaveFile")]
    internal static class BackupSidecarHook
    {
        private static void Postfix(string fullPathWithFilename, int maxHistoryCount)
        {
            try
            {
                string sidecarPath = SettingsStore.GetSidecarPath(fullPathWithFilename);
                if (!File.Exists(sidecarPath)) return;  // vanilla / pre-mod save — nothing to back up

                // Engine names backups "<dir>/<file><.ext>.bak_N" (see BackupSaveFile).
                // Applied to "<save>.sav.worldgen.json" that yields
                // "<save>.sav.worldgen.json.bak_N".
                int existingCount = CountExistingBackups(sidecarPath, maxHistoryCount);
                for (int n = existingCount; n >= 1; n--)
                {
                    string from = sidecarPath + $".bak_{n}";
                    string to   = sidecarPath + $".bak_{n + 1}";
                    if (!File.Exists(from)) continue;
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(from, to);
                }
                File.Copy(sidecarPath, sidecarPath + ".bak_1", overwrite: true);

                // Mirror the engine's overflow-delete guard exactly (ManSaveGame.cs:2596 uses
                // `!= -1`). The only non-positive value the engine actually passes is -1
                // (unlimited), but matching the guard keeps the patch's behavior identical
                // to the engine's for any caller, including hypothetical callers passing 0.
                if (maxHistoryCount != -1)
                {
                    string overflow = sidecarPath + $".bak_{maxHistoryCount + 1}";
                    if (File.Exists(overflow)) File.Delete(overflow);
                }
            }
            catch (Exception ex)
            {
                KickStart.LogError($"BackupSidecarHook: rotating sidecar backups for '{fullPathWithFilename}' threw", ex);
            }
        }

        private static int CountExistingBackups(string sidecarPath, int maxHistoryCount)
        {
            // Engine uses -1 to signal "unlimited" history. We cap at a large finite value to
            // keep the scan cost bounded; in practice the rotation runs every 10 min and the
            // standard cap is 3, so this loop almost always exits in 1–4 iterations.
            int limit = maxHistoryCount > 0 ? maxHistoryCount : 256;
            for (int i = 1; i <= limit; i++)
            {
                if (!File.Exists(sidecarPath + $".bak_{i}"))
                    return i - 1;
            }
            return limit;
        }
    }

    /// <summary>
    /// Delete the sidecar (and its backup chain) when the engine deletes its save file.
    /// Postfix on the private <c>UISave.Delete()</c> — the actual delete entry point hit
    /// by the in-game Delete button (via <c>UIScreenLoadSave.PromptDeleteSavedGame</c> /
    /// <c>UIScreenSaveGame.PromptDelete</c> → <c>AskDelete</c> → <c>Delete</c>). That
    /// method calls <c>File.Delete</c> directly on the .sav rather than routing through
    /// <c>SaveDataConsoles.DeleteData</c> (which is reserved for snapshots and caches),
    /// so we have to hook it specifically.
    /// </summary>
    /// <remarks>
    /// <c>m_SaveFileName</c> and <c>m_SaveGameType</c> on the <c>UISave</c> instance are
    /// private; we read them via <see cref="Reflect"/>. If either is unset (defensive null
    /// guard) we no-op — orphan sidecars are harmless, a wrong sidecar deletion isn't.
    /// </remarks>
    [HarmonyPatch(typeof(UISave), "Delete")]
    internal static class DeleteSidecarHook
    {
        private static void Postfix(UISave __instance)
        {
            if (__instance == null) return;

            try
            {
                // Check saveName first so a degenerate UISave (uninitialized slot) is a clean
                // no-op rather than a logged reflection exception. Game-type reflection only
                // runs once we know there's actually a save to clean up.
                string saveName = Reflect.GetField<string>(__instance, "m_SaveFileName");
                if (string.IsNullOrEmpty(saveName)) return;
                ManGameMode.GameType gameType = Reflect.GetField<ManGameMode.GameType>(__instance, "m_SaveGameType");

                string savePath = ManSaveGame.CreateGameSaveFilePath(gameType, saveName);
                string sidecarPath = SettingsStore.GetSidecarPath(savePath);

                if (File.Exists(sidecarPath))
                {
                    File.Delete(sidecarPath);
                    KickStart.Log($"DeleteSidecarHook: deleted sidecar '{sidecarPath}'");
                }

                // Sweep the backup chain too. Engine caps autosave history at 3 but the chain
                // length is theoretically unbounded; 16 covers any realistic configuration
                // without rolling forever on hostile input.
                for (int n = 1; n <= 16; n++)
                {
                    string bak = sidecarPath + $".bak_{n}";
                    if (File.Exists(bak)) File.Delete(bak);
                    else break;
                }
            }
            catch (Exception ex)
            {
                KickStart.LogError("DeleteSidecarHook: deleting sidecar threw", ex);
            }
        }
    }

    /// <summary>Shared sidecar→Pending plumbing for both sync and async load hooks.</summary>
    internal static class SidecarLoadHelper
    {
        public static void LoadIntoPending(string savePath)
        {
            Settings loaded = SettingsStore.LoadForOrDefaults(savePath, out SidecarLoadSource source);
            SettingsStore.Pending = loaded;

            switch (source)
            {
                case SidecarLoadSource.Sidecar:
                    KickStart.Log($"Sidecar load: '{savePath}' → HM={loaded.Creation.HeightMultiplier:0.00} density={loaded.Live.ResourceDensityMultiplier:0.00} yield={loaded.Live.ResourceYieldMultiplier:0.00}");
                    break;
                case SidecarLoadSource.MissingDefaulted:
                    KickStart.Log($"Sidecar load: no sidecar at '{SettingsStore.GetSidecarPath(savePath)}' (pre-persistence save?) — using defaults");
                    break;
                case SidecarLoadSource.MalformedDefaulted:
                    KickStart.LogWarning($"Sidecar load: '{SettingsStore.GetSidecarPath(savePath)}' was malformed — using defaults. The customization for this world has been reset.");
                    break;
            }
        }
    }
}
