using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Postfix on <c>ModuleWing.AerofoilState.CalculateForce</c>. Scales the returned lift
    /// force by <see cref="LiveSettings.AtmosphereDensityMultiplier"/>.
    /// </summary>
    /// <remarks>
    /// <para><c>ManWorld.UniversalAtmosphereDensityCurve</c> exists in the source but is dead
    /// code at lift-compute time — TerraTech's actual lift formula is per-wing
    /// (<c>liftCurve.Evaluate(attackAngle) × liftStrength × airVelocity²</c>) with no
    /// density factor. Mutating the curve had no effect because nothing reads it.</para>
    /// <para>This patch applies the user-chosen "atmosphere density" multiplier directly to
    /// the wing force vector. Density 0 ≈ zero lift, density 3 ≈ tripled lift.</para>
    /// <para>Target is on a <b>private nested class</b> (<c>ModuleWing+AerofoilState</c>),
    /// so we use a <c>TargetMethod</c> selector to reach it — the simpler
    /// <c>[HarmonyPatch(typeof(...))]</c> attribute path can't see private nested types.</para>
    /// </remarks>
    [HarmonyPatch]
    internal static class AtmospherePatch
    {
        private static bool s_LoggedFirstFire;

        // Harmony invokes TargetMethod() to resolve the patch target dynamically.
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("ModuleWing+AerofoilState:CalculateForce");
        }

        private static void Postfix(ref Vector3 __result)
        {
            LiveSettings l = SettingsStore.Current?.Live;
            if (l == null) return;
            if (Mathf.Approximately(l.AtmosphereDensityMultiplier, 1f)) return;

            __result *= l.AtmosphereDensityMultiplier;

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"AtmospherePatch.Postfix first fire: AtmosphereDensityMultiplier={l.AtmosphereDensityMultiplier}");
            }
        }

        // No-op stubs. Atmosphere is applied on demand inside the Postfix above (the
        // multiplier is read per-call from SettingsStore.Current), so there's nothing to
        // push at mode-switch time. The empty methods keep ApplyOnModeSwitch.ApplyTier2's
        // call shape symmetric with the other Tier-2 entries.
        public static void Apply(float multiplier) { }
        public static void Restore() { }
        public static void ClearCache() { s_LoggedFirstFire = false; }
    }
}
