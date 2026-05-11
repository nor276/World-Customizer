using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Scales <c>ResourceReservoir.m_ExtractionSpeedMultiplier</c> by
    /// <see cref="LiveSettings.AutoMinerSpeedMultiplier"/>. Reservoirs are the deep pool
    /// AutoMiners read from — distinct from the surface chunks <see cref="ResourceYieldPatch"/>
    /// scales.
    /// </summary>
    /// <remarks>
    /// Originals are cached per InstanceID so re-application is idempotent.
    /// </remarks>
    [HarmonyPatch(typeof(ResourceReservoir), "OnSpawn")]
    internal static class AutoMinerSpeedPatch
    {
        private static readonly Dictionary<int, float> s_OriginalSpeedMultiplier = new Dictionary<int, float>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ResourceReservoir __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"AutoMinerSpeedPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.AutoMinerSpeedMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ResourceReservoir[] all = Object.FindObjectsOfType<ResourceReservoir>();
            for (int i = 0; i < all.Length; i++)
            {
                ApplyToInstance(all[i], multiplier);
            }
        }

        private static void ApplyToInstance(ResourceReservoir reservoir, float? overrideMultiplier = null)
        {
            if (reservoir == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.AutoMinerSpeedMultiplier
                ?? 1f;

            int id = reservoir.GetInstanceID();
            float original;
            if (!s_OriginalSpeedMultiplier.TryGetValue(id, out original))
            {
                original = Reflect.GetField<float>(reservoir, "m_ExtractionSpeedMultiplier");
                s_OriginalSpeedMultiplier[id] = original;
            }

            float scaled = Mathf.Max(0.01f, original * multiplier);
            Reflect.SetField(reservoir, "m_ExtractionSpeedMultiplier", scaled);
        }

        public static void ClearCache()
        {
            s_OriginalSpeedMultiplier.Clear();
        }
    }
}
