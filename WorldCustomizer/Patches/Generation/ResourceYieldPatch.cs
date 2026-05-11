using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Scales <c>ResourceDispenser.m_TotalChunks</c> by
    /// <see cref="LiveSettings.ResourceYieldMultiplier"/>.
    /// </summary>
    /// <remarks>
    /// Two entry points:
    /// <list type="bullet">
    /// <item>Postfix on <c>OnSpawn</c> handles instances that come online after world load.</item>
    /// <item><see cref="ApplyToAllLoaded"/> walks instances already loaded at apply time
    /// (called from <see cref="ApplyOnModeSwitch"/>).</item>
    /// </list>
    /// Originals are cached per InstanceID so re-application is idempotent. The cache
    /// lives for the session — it leaks at world-exit, but the count is bounded by the number
    /// of unique dispenser components ever instantiated.
    /// </remarks>
    [HarmonyPatch(typeof(ResourceDispenser), "OnSpawn")]
    internal static class ResourceYieldPatch
    {
        private static readonly Dictionary<int, int> s_OriginalTotalChunks = new Dictionary<int, int>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ResourceDispenser __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"ResourceYieldPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ResourceYieldMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ResourceDispenser[] all = Object.FindObjectsOfType<ResourceDispenser>();
            for (int i = 0; i < all.Length; i++)
            {
                ApplyToInstance(all[i], multiplier);
            }
        }

        private static void ApplyToInstance(ResourceDispenser dispenser, float? overrideMultiplier = null)
        {
            if (dispenser == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ResourceYieldMultiplier
                ?? 1f;

            int id = dispenser.GetInstanceID();
            int original;
            if (!s_OriginalTotalChunks.TryGetValue(id, out original))
            {
                original = Reflect.GetField<int>(dispenser, "m_TotalChunks");
                s_OriginalTotalChunks[id] = original;
            }

            int scaled = Mathf.Max(1, Mathf.RoundToInt(original * multiplier));
            Reflect.SetField(dispenser, "m_TotalChunks", scaled);
        }

        /// <summary>For diagnostics. Drops the cached originals — next apply will re-cache.</summary>
        public static void ClearCache()
        {
            s_OriginalTotalChunks.Clear();
        }
    }
}
