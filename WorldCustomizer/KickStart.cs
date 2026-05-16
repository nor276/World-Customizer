using System;
using HarmonyLib;
using UnityEngine;
using WorldCustomizer.Integration;
using WorldCustomizer.Patches.Generation;

namespace WorldCustomizer
{
    /// <summary>
    /// Mod entry point. Invoked by the ModExt loader when the assembly is loaded.
    /// </summary>
    public static class KickStart
    {
        public const string ModID = "rbigg.WorldCustomizer";
        public const string ModName = "World Customizer";
        public const string Version = "0.1.0";

        private static bool s_Initialized;
        private static Harmony s_Harmony;

        /// <summary>
        /// Loader-invoked entry. Must be idempotent — the loader may call multiple times in
        /// some failure modes.
        /// </summary>
        public static void Initiate()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            Log("init begin");

            try
            {
                SettingsSelfTest.Run();
            }
            catch (Exception ex)
            {
                LogError("Settings self-test threw", ex);
            }

            try
            {
                s_Harmony = new Harmony(ModID);
                s_Harmony.PatchAll();
                int patchCount = 0;
                foreach (var method in s_Harmony.GetPatchedMethods())
                {
                    Log($"  Harmony patched: {method.DeclaringType?.FullName}.{method.Name}");
                    patchCount++;
                }
                Log($"Harmony patches applied: {patchCount} methods");
            }
            catch (Exception ex)
            {
                LogError("Harmony.PatchAll threw", ex);
            }

            try
            {
                ManGameMode mgm = Singleton.Manager<ManGameMode>.inst;
                if (mgm != null)
                {
                    mgm.ModeSwitchEvent.Subscribe(ApplyOnModeSwitch.OnModeSwitch);
                    Log("Subscribed to ModeSwitchEvent");
                }
                else
                {
                    LogWarning("ManGameMode.inst is null at Initiate; ModeSwitchEvent subscribe deferred (mod will not apply until subscription succeeds)");
                }
            }
            catch (Exception ex)
            {
                LogError("ModeSwitchEvent subscribe threw", ex);
            }

            try
            {
                NativeOptionsBridge.Register();
            }
            catch (Exception ex)
            {
                LogError("NativeOptionsBridge.Register threw", ex);
            }

            Log("init complete");
        }

        /// <summary>
        /// Loader-invoked teardown. Reverses Initiate() so the mod can be reloaded cleanly.
        /// </summary>
        public static void DeInit()
        {
            if (!s_Initialized) return;
            s_Initialized = false;

            Log("deinit");

            try
            {
                ManGameMode mgm = Singleton.Manager<ManGameMode>.inst;
                if (mgm != null)
                    mgm.ModeSwitchEvent.Unsubscribe(ApplyOnModeSwitch.OnModeSwitch);
            }
            catch (Exception ex)
            {
                LogError("ModeSwitchEvent unsubscribe threw", ex);
            }

            try
            {
                AtmospherePatch.Restore();
            }
            catch (Exception ex)
            {
                LogError("AtmospherePatch.Restore threw", ex);
            }

            try
            {
                LooseItemPatches.Restore();
            }
            catch (Exception ex)
            {
                LogError("LooseItemPatches.Restore threw", ex);
            }

            try
            {
                s_Harmony?.UnpatchAll(ModID);
                s_Harmony = null;
            }
            catch (Exception ex)
            {
                LogError("Harmony.UnpatchAll threw", ex);
            }

            // INTENTIONALLY do NOT call SettingsStore.Clear().
            //
            // The mod loader recycles us (deinit + reinit) on every mode-session change,
            // including immediately after the world starts. If we clear the SettingsStore
            // state on deinit, the next ModeSwitchEvent fires with Pending=null and our
            // handler applies defaults — overwriting the user's choices seconds after they
            // were applied. Static state survives the mod recycle (the assembly stays
            // loaded), so leaving Current/Pending untouched is correct.
        }

        internal static void Log(string message)
        {
            Debug.Log($"[{ModName} {Version}] {message}");
        }

        internal static void LogWarning(string message)
        {
            Debug.LogWarning($"[{ModName} {Version}] {message}");
        }

        internal static void LogError(string message, Exception ex = null)
        {
            if (ex != null)
                Debug.LogError($"[{ModName} {Version}] {message}\n{ex}");
            else
                Debug.LogError($"[{ModName} {Version}] {message}");
        }
    }
}
