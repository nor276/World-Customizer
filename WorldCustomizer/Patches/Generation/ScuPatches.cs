using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldCustomizer.Patches.Generation
{
    /// <summary>
    /// Three patches on the SCU/conveyor pickup pipeline:
    /// <list type="bullet">
    /// <item><see cref="PickupRangePatch"/> scales <c>ModuleItemPickup.m_PickupRange</c> — how far an SCU sees chunks.</item>
    /// <item><see cref="BeamStrengthPatch"/> scales <c>ModuleItemHolderBeam.m_BeamStrength</c> — how hard the beam pulls grabbed chunks.</item>
    /// <item><see cref="StackCapacityPatch"/> scales <c>ModuleItemHolder.m_CapacityPerStack</c> via the public <c>OverrideStackCapacity</c> method.</item>
    /// </list>
    /// All three follow the same pattern as <see cref="ResourceYieldPatch"/> / <see cref="AutoMinerSpeedPatch"/>:
    /// postfix on <c>OnSpawn</c> + <c>ApplyToAllLoaded</c> walking <c>FindObjectsOfType</c> at mode-switch
    /// time. Originals cached per <c>GetInstanceID</c> for idempotent re-apply.
    /// </summary>
    /// <remarks>
    /// Scope is "all pickup-bearing blocks", not SCU-only. <c>ModuleItemPickup</c> is on every
    /// conveyor with pickup. Multiplying scales each block by the same factor, so vanilla relative
    /// ranges are preserved (long SCU stays long-range, short pickup conveyor stays short-range).
    /// </remarks>
    [HarmonyPatch(typeof(ModuleItemPickup), "OnSpawn")]
    internal static class PickupRangePatch
    {
        private static readonly Dictionary<int, float> s_OriginalRange = new Dictionary<int, float>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ModuleItemPickup __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"PickupRangePatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ScuPickupRangeMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ModuleItemPickup[] all = Object.FindObjectsOfType<ModuleItemPickup>();
            for (int i = 0; i < all.Length; i++)
                ApplyToInstance(all[i], multiplier);
        }

        private static void ApplyToInstance(ModuleItemPickup pickup, float? overrideMultiplier = null)
        {
            if (pickup == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ScuPickupRangeMultiplier
                ?? 1f;

            int id = pickup.GetInstanceID();
            if (!s_OriginalRange.TryGetValue(id, out float original))
            {
                original = Reflect.GetField<float>(pickup, "m_PickupRange");
                s_OriginalRange[id] = original;
            }

            float scaled = Mathf.Max(0.1f, original * multiplier);
            Reflect.SetField(pickup, "m_PickupRange", scaled);
        }

        public static void ClearCache() => s_OriginalRange.Clear();
    }

    [HarmonyPatch(typeof(ModuleItemHolderBeam), "OnSpawn")]
    internal static class BeamStrengthPatch
    {
        private static readonly Dictionary<int, float> s_OriginalStrength = new Dictionary<int, float>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ModuleItemHolderBeam __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"BeamStrengthPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ScuBeamStrengthMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ModuleItemHolderBeam[] all = Object.FindObjectsOfType<ModuleItemHolderBeam>();
            for (int i = 0; i < all.Length; i++)
                ApplyToInstance(all[i], multiplier);
        }

        private static void ApplyToInstance(ModuleItemHolderBeam beam, float? overrideMultiplier = null)
        {
            if (beam == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ScuBeamStrengthMultiplier
                ?? 1f;

            int id = beam.GetInstanceID();
            if (!s_OriginalStrength.TryGetValue(id, out float original))
            {
                original = Reflect.GetField<float>(beam, "m_BeamStrength");
                s_OriginalStrength[id] = original;
            }

            // Engine clamps the final force vector at 2000 inside UpdateFloat, so multipliers above
            // that ceiling will silently saturate on already-strong beams. We still write the raw
            // scaled value — lets weaker beams benefit from the multiplier proportionally.
            float scaled = Mathf.Max(1f, original * multiplier);
            Reflect.SetField(beam, "m_BeamStrength", scaled);
        }

        public static void ClearCache() => s_OriginalStrength.Clear();
    }

    /// <summary>
    /// Biases the in-flight chunk trajectory upward via per-instance
    /// <c>OverrideHeightCorrectionLiftFactor</c>. Multiplier scales the engine's vanilla
    /// <c>holdBeamFloatParams.heightCorrectionLiftFactor</c> baseline — higher = chunks
    /// rise more before approaching the stack horizontally, clearing obstacles on the way.
    /// </summary>
    /// <remarks>
    /// <para>Earlier revisions also scaled <c>m_BeamBaseHeight</c> to raise where chunks
    /// settle on the stack. That broke absorption: the pre-pickup distance check in
    /// <c>UpdateItemMovement</c> ([line 322 of ModuleItemHolderBeam.cs]) measures the chunk's
    /// distance to the elevated stack target. Raising the target by N meters added N to
    /// the effective distance, exceeding <c>PickupRange</c> for ground-level chunks → engine
    /// drops them on grab → SCU appears not to suck items in. So we now leave the target
    /// alone and only reshape the trajectory.</para>
    /// <para>The override field <c>m_OverrideHeightCorrectionLiftFactor</c> is read in
    /// <c>UpdateFloat</c> at line 603 with a guard: <c>>= 0f</c> uses the override,
    /// otherwise the global. We always write the override at OnSpawn so the multiplier
    /// applies cleanly to this beam without touching anyone else's tuning.</para>
    /// </remarks>
    [HarmonyPatch(typeof(ModuleItemHolderBeam), "OnSpawn")]
    internal static class LiftHeightPatch
    {
        private static bool s_LoggedFirstFire;

        private static void Postfix(ModuleItemHolderBeam __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"LiftHeightPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ScuLiftHeightMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ModuleItemHolderBeam[] all = Object.FindObjectsOfType<ModuleItemHolderBeam>();
            for (int i = 0; i < all.Length; i++)
                ApplyToInstance(all[i], multiplier);
        }

        private static void ApplyToInstance(ModuleItemHolderBeam beam, float? overrideMultiplier = null)
        {
            if (beam == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ScuLiftHeightMultiplier
                ?? 1f;

            // Read the runtime global baseline so mod-adjusted defaults are honored.
            float baseline = 0.5f;   // fallback to TT's vanilla initializer
            Globals g = Globals.inst;
            if (g != null) baseline = g.holdBeamFloatParams.heightCorrectionLiftFactor;

            // Public API on the beam — engine's UpdateFloat reads m_OverrideHeightCorrectionLiftFactor
            // and prefers it over the global when >= 0.
            beam.OverrideHeightCorrectionLiftFactor(Mathf.Max(0.01f, baseline * multiplier));
        }

        public static void ClearCache() => s_LoggedFirstFire = false;
    }

    /// <summary>
    /// Inverse-scales <c>ModuleItemPickup.m_VisionRefreshInterval</c> per-instance. Lower
    /// interval = bucket refills more often = more pickups per second under sustained load.
    /// </summary>
    [HarmonyPatch(typeof(ModuleItemPickup), "OnSpawn")]
    internal static class PickupSpeedPatch
    {
        private static readonly Dictionary<int, float> s_OriginalInterval = new Dictionary<int, float>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ModuleItemPickup __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"PickupSpeedPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ScuPickupSpeedMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ModuleItemPickup[] all = Object.FindObjectsOfType<ModuleItemPickup>();
            for (int i = 0; i < all.Length; i++)
                ApplyToInstance(all[i], multiplier);
        }

        private static void ApplyToInstance(ModuleItemPickup pickup, float? overrideMultiplier = null)
        {
            if (pickup == null) return;

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ScuPickupSpeedMultiplier
                ?? 1f;
            if (multiplier <= 0f) multiplier = 1f;

            int id = pickup.GetInstanceID();
            if (!s_OriginalInterval.TryGetValue(id, out float original))
            {
                original = Reflect.GetField<float>(pickup, "m_VisionRefreshInterval");
                s_OriginalInterval[id] = original;
            }

            // Floor at 50ms so we don't refresh every frame (would thrash the bucket sort).
            float scaled = Mathf.Max(0.05f, original / multiplier);
            Reflect.SetField(pickup, "m_VisionRefreshInterval", scaled);
        }

        public static void ClearCache() => s_OriginalInterval.Clear();
    }

    /// <summary>
    /// Postfix on <c>ModuleItemPickup.TryPickupItems</c>. Vanilla picks one item per Update
    /// tick (TakeOneItem breaks after a successful take when no filter callback is set). We
    /// invoke TakeOneItem extra times per tick, draining more of the in-range bucket per
    /// frame — effective "N items at once" pickup.
    /// </summary>
    /// <remarks>
    /// Stack selection is replicated from the original (non-full stack with fewest items,
    /// filtered by IsPickupStack), since the same stack may not still be optimal after the
    /// first take. <c>TakeOneItem</c> and <c>IsPickupStack</c> are private — accessed via
    /// reflection, MethodInfo cached on first fire.
    /// </remarks>
    [HarmonyPatch(typeof(ModuleItemPickup), "TryPickupItems")]
    internal static class MultiPickPatch
    {
        private static MethodInfo s_TakeOneItem;
        private static MethodInfo s_IsPickupStack;
        private static bool s_LoggedFirstFire;
        private static bool s_ReflectionFailed;

        private static void Postfix(ModuleItemPickup __instance)
        {
            if (__instance == null || s_ReflectionFailed) return;

            LiveSettings l = SettingsStore.Current?.Live;
            if (l == null) return;

            int extraPicks = Mathf.RoundToInt(l.ScuItemsPerTickMultiplier) - 1;
            if (extraPicks <= 0) return;

            if (s_TakeOneItem == null)
            {
                s_TakeOneItem   = AccessTools.Method(typeof(ModuleItemPickup), "TakeOneItem");
                s_IsPickupStack = AccessTools.Method(typeof(ModuleItemPickup), "IsPickupStack");
                if (s_TakeOneItem == null || s_IsPickupStack == null)
                {
                    KickStart.LogWarning("MultiPickPatch: TakeOneItem or IsPickupStack not found via reflection — disabling");
                    s_ReflectionFailed = true;
                    return;
                }
            }

            ModuleItemHolder holder = __instance.GetComponent<ModuleItemHolder>();
            if (holder == null) return;

            // block.centreOfMassWorld may NRE if the block has been detached mid-tick.
            // Cheap null-guard; caller already handles a quiet return.
            TankBlock block = __instance.block;
            if (block == null) return;
            Vector3 centre = block.centreOfMassWorld;

            object[] isPickupArgs = new object[1];
            object[] takeArgs = new object[] { centre, null };

            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"MultiPickPatch.Postfix first fire: instance={__instance.name}, extraPicks={extraPicks}");
            }

            for (int i = 0; i < extraPicks; i++)
            {
                // Re-pick the best stack each iteration: state changed (one item was added).
                ModuleItemHolder.Stack chosen = null;
                ModuleItemHolder.StackIterator.Enumerator stacks = holder.Stacks.GetEnumerator();
                while (stacks.MoveNext())
                {
                    ModuleItemHolder.Stack current = stacks.Current;
                    if (current.IsFull) continue;
                    if (chosen != null && current.NumItems >= chosen.NumItems) continue;
                    isPickupArgs[0] = current;
                    bool isPickup = (bool)s_IsPickupStack.Invoke(__instance, isPickupArgs);
                    if (isPickup) chosen = current;
                }
                if (chosen == null) break;

                takeArgs[1] = chosen;
                object picked = s_TakeOneItem.Invoke(__instance, takeArgs);
                if (picked == null) break;   // bucket empty
            }
        }

        public static void ClearCache() { s_LoggedFirstFire = false; }
    }

    [HarmonyPatch(typeof(ModuleItemHolder), "OnSpawn")]
    internal static class StackCapacityPatch
    {
        private static readonly Dictionary<int, int> s_OriginalCapacity = new Dictionary<int, int>();
        private static bool s_LoggedFirstFire;

        private static void Postfix(ModuleItemHolder __instance)
        {
            if (!s_LoggedFirstFire)
            {
                s_LoggedFirstFire = true;
                KickStart.Log($"StackCapacityPatch.Postfix first fire: instance={__instance?.name}, mult={SettingsStore.Current?.Live?.ScuStackCapacityMultiplier}");
            }
            ApplyToInstance(__instance);
        }

        public static void ApplyToAllLoaded(float multiplier)
        {
            ModuleItemHolder[] all = Object.FindObjectsOfType<ModuleItemHolder>();
            for (int i = 0; i < all.Length; i++)
                ApplyToInstance(all[i], multiplier);
        }

        private static void ApplyToInstance(ModuleItemHolder holder, float? overrideMultiplier = null)
        {
            if (holder == null) return;

            int id = holder.GetInstanceID();

            // Conveyors use ModuleItemHolder as a fixed-size transit buffer; their flow
            // logic indexes into the slot array with assumptions baked at construction.
            // Resizing the capacity at runtime makes items skip, duplicate, or stick.
            // Detect conveyor blocks via the colocated ModuleItemConveyor and leave them
            // at their vanilla capacity. If we previously patched this instance under
            // an older mod version (cached original ≠ current), restore.
            if (holder.GetComponent<ModuleItemConveyor>() != null)
            {
                if (s_OriginalCapacity.TryGetValue(id, out int originalConveyor))
                {
                    holder.OverrideStackCapacity(originalConveyor);
                    s_OriginalCapacity.Remove(id);
                }
                return;
            }

            float multiplier = overrideMultiplier
                ?? SettingsStore.Current?.Live?.ScuStackCapacityMultiplier
                ?? 1f;

            if (!s_OriginalCapacity.TryGetValue(id, out int original))
            {
                original = Reflect.GetField<int>(holder, "m_CapacityPerStack");
                s_OriginalCapacity[id] = original;
            }

            int scaled = Mathf.Max(1, Mathf.RoundToInt(original * multiplier));
            // Public method on ModuleItemHolder — no reflection needed for the write path.
            holder.OverrideStackCapacity(scaled);
        }

        public static void ClearCache() => s_OriginalCapacity.Clear();
    }
}
