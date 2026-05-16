using System;
using System.Collections.Generic;
using HarmonyLib;

namespace WorldCustomizer.Patches.Persistence
{
    /// <summary>
    /// Postfix on the private <c>ManSaveGame.WriteSaveDataToDisk(List&lt;string&gt; data, string filePath)</c>
    /// — the sole disk-write funnel both sync and async save paths flow through. Writes
    /// <see cref="SettingsStore.Current"/> to the <c>&lt;save&gt;.worldgen.json</c> sidecar
    /// so the world's customization persists across game sessions.
    /// </summary>
    /// <remarks>
    /// <para><b>Why <c>WriteSaveDataToDisk</c> and not <c>Save</c>.</b> An earlier revision
    /// postfixed <c>ManSaveGame.Save</c> filtered on its <c>__result</c>, but <c>Save</c>
    /// returns <c>true</c> the moment it *queues* an async task — not when the .sav lands
    /// on disk. On the async codepath that meant we wrote the sidecar before the .sav,
    /// and a subsequent async-write failure (disk full / permission denied / process
    /// killed) would leave a sidecar describing the just-edited settings paired with the
    /// previous .sav's terrain — reproducing the exact "trading stations buried on
    /// reload" failure mode this whole subsystem exists to prevent. Hooking
    /// <c>WriteSaveDataToDisk</c> instead guarantees our postfix only fires after the
    /// .sav bytes are committed (the engine's atomic-copy at
    /// <c>ManSaveGame.cs:2535</c> has returned): if <c>WriteSaveDataToDisk</c> throws,
    /// our postfix never runs and no orphan sidecar is written.</para>
    ///
    /// <para><b>Threading.</b> On the sync path the postfix runs on the engine caller's
    /// thread (main). On the async path it runs on a <c>Task.Run</c> worker
    /// (<c>ManSaveGame.cs:2493</c>). Reading <c>SettingsStore.Current</c> (an atomic
    /// reference read) and writing JSON through <c>SettingsStore.SaveFor</c> (whose own
    /// atomic-replace pattern in <c>SaveToFile</c> is thread-safe with itself) are both
    /// fine off-main.</para>
    ///
    /// <para><b>Includes mid-game edits.</b> Sliders changed via the in-game Native
    /// Options panel mutate <see cref="LiveSettings"/> directly through
    /// <c>NativeOptionsBridge.Binding.OnChanged</c>, so by postfix time <c>Current</c>
    /// already reflects the live values; the sidecar captures what the player is
    /// actually playing with, not what they picked at world creation.</para>
    /// </remarks>
    [HarmonyPatch(typeof(ManSaveGame), "WriteSaveDataToDisk")]
    internal static class SaveHook
    {
        // Suppress the unused-parameter warning the bound Harmony arg generates.
        private static void Postfix(List<string> data, string filePath)
        {
            _ = data;

            if (string.IsNullOrEmpty(filePath)) return;

            Settings current = SettingsStore.Current;
            if (current == null)
            {
                // Saves before any ModeSwitch has run shouldn't be possible (the save UI is
                // gated on being in a world), but if it happens we'd rather log loudly than
                // write a defaults sidecar that overrides whatever the world was actually
                // generated with.
                KickStart.LogWarning($"SaveHook: WriteSaveDataToDisk for '{filePath}' completed but SettingsStore.Current was null; no sidecar written. World will load with defaults on reload.");
                return;
            }

            try
            {
                if (SettingsStore.SaveFor(filePath, current))
                {
                    KickStart.Log($"SaveHook: sidecar written for '{filePath}' (HM={current.Creation.HeightMultiplier:0.00} density={current.Live.ResourceDensityMultiplier:0.00} yield={current.Live.ResourceYieldMultiplier:0.00})");
                }
                // SaveFor logs its own failure detail inside SaveToFile — no need to repeat here.
            }
            catch (Exception ex)
            {
                KickStart.LogError($"SaveHook: writing sidecar for '{filePath}' threw", ex);
            }
        }
    }
}
