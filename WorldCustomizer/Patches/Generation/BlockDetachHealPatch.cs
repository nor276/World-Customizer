using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Heals a block back toward full HP at the moment it detaches from a tech, and
    /// aborts any pending engine-scheduled <c>SelfDestruct</c>. Intended fix for the
    /// "weapons and structural blocks vaporize during a tech-death cab explosion"
    /// loss-of-loot scenario: the vanilla engine calls
    /// <c>TankBlock.CheckLooseDestruction</c> on every detaching block, which both
    /// rolls <c>Globals.m_BlockSurvivalChance</c> and, when the dying tech has its
    /// <c>ShouldExplodeDetachingBlocks</c> flag set, unconditionally arms a
    /// SelfDestruct fuse. We postfix that method, so vanilla still runs first; we
    /// then revert the destruction decision and patch the block's HP so leftover
    /// AoE damage from neighbor explosions doesn't kill it on the way down.
    /// </summary>
    /// <remarks>
    /// <para><b>Setting.</b> <see cref="LiveSettings.BlockDetachHealAmount"/> ranges
    /// 0..1. 0 is a strict no-op — the postfix early-returns before touching
    /// anything, so vanilla detach behavior is preserved exactly. 1 restores the
    /// block to <c>MaxHealth</c>; intermediate values restore up to that fraction
    /// (we never lower current HP). Read per-call from <c>SettingsStore.Current</c>
    /// so changes via the live UI take effect on the next detach.</para>
    ///
    /// <para><b>Scope.</b> Per the user's design choice the rescue applies to every
    /// tech regardless of team — enemy, neutral, and player. Player techs benefit
    /// from the same buffer when their cab is destroyed, which can change combat
    /// feel; the no-op default keeps that opt-in.</para>
    ///
    /// <para><b>Why a postfix and not a prefix.</b> <c>CheckLooseDestruction</c>
    /// also performs the engine's "first-detach grace" check
    /// (<c>m_PreventSelfDestructOnFirstDetach</c>) and a license-state lookup. A
    /// postfix lets all that vanilla logic run and observe its side effects, then
    /// we override the *outcome* on blocks that would have been marked for
    /// destruction — without having to reimplement any of the vanilla branches.</para>
    ///
    /// <para><b>SelfDestruct path.</b> <c>TankBlock.damage</c> is a
    /// <c>ModuleDamage</c>, not a <c>Damageable</c>; SelfDestruct/AbortSelfDestruct
    /// live there. The HP itself is on <c>block.visible.damageable</c>
    /// (<c>Damageable.Repair</c> additively heals, clamped to MaxHealth).</para>
    /// </remarks>
    [HarmonyPatch(typeof(TankBlock), nameof(TankBlock.CheckLooseDestruction))]
    internal static class BlockDetachHealPatch
    {
        private static bool s_LoggedFirstFire;

        private static void Postfix(TankBlock __instance, Tank prevTech)
        {
            if (__instance == null || prevTech == null) return;

            float setting = SettingsStore.Current?.Live?.BlockDetachHealAmount ?? 0f;
            if (setting <= 0f) return;

            Damageable d = __instance.visible?.damageable;
            if (d == null || d.Invulnerable) return;

            // Cancel the engine's pending self-destruct (set a few lines above us
            // in CheckLooseDestruction when either survival-chance failed or the
            // tech is in "explode detaching blocks" mode).
            ModuleDamage md = __instance.damage;
            if (md != null) md.AbortSelfDestruct();

            // Top HP up to (setting * MaxHealth). Repair is additive and clamps to
            // MaxHealth internally, so a negative delta is the only thing to avoid.
            float target = d.MaxHealth * setting;
            float current = d.Health;
            if (current < target)
            {
                d.Repair(target - current, sendEvent: false);
            }

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"BlockDetachHealPatch.Postfix first fire: block={__instance.name}, prevTech={prevTech.name}, setting={setting:0.00}, healed to {d.Health:0.0}/{d.MaxHealth:0.0}");
            }
        }

        public static void ClearCache() => s_LoggedFirstFire = false;
    }
}
