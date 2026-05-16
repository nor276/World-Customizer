using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using WorldCustomizer.Patches.Generation;

namespace WorldCustomizer.Integration
{
    /// <summary>
    /// Soft-detect bridge to the optional "Native Options" mod
    /// (workshop id 2685130411, assembly <c>0Nuterra.NativeOptions</c>).
    ///
    /// When Native Options is installed, this class registers every Tier-2 (live-tunable)
    /// setting as a slider under a "World Customizer" page in the game's pause/options
    /// menu, so players can retune multipliers mid-game without quitting back to the
    /// new-game screen.
    ///
    /// All references to Nuterra types are reflection-only — the World Customizer DLL has
    /// no compile-time or load-time dependency on the Native Options assembly. If the mod
    /// is not present, <see cref="Register"/> logs and returns; nothing else in the project
    /// changes behavior.
    /// </summary>
    internal static class NativeOptionsBridge
    {
        private const string AsmName        = "0Nuterra.NativeOptions";
        private const string OptionRangeFqn = "Nuterra.NativeOptions.OptionRange";
        private const string ModNameLabel   = "World Customizer";

        private static bool s_Detected;
        private static bool s_OptionsBuilt;
        private static bool s_PushingFromCurrent;
        private static ConstructorInfo s_Ctor;
        private static PropertyInfo s_ValueProp;
        private static PropertyInfo s_OnChangedProp;
        private static readonly List<Binding> s_Bindings = new List<Binding>();

        /// <summary>True iff Native Options is loaded and our sliders are constructed.</summary>
        public static bool IsActive => s_OptionsBuilt;

        /// <summary>
        /// Detect Native Options and cache reflection metadata. Does NOT construct any
        /// OptionRange instances — that's deferred to <see cref="EnsureOptionsBuilt"/>
        /// because constructing one triggers the <c>UIElements</c> static ctor inside
        /// Native Options, which calls <c>Resources.FindObjectsOfTypeAll&lt;Font&gt;().First(...)</c>
        /// and throws if the game's UI fonts/prefabs aren't loaded yet. At mod-Init time
        /// they typically aren't.
        /// </summary>
        public static void Register()
        {
            if (s_Detected) return;

            Assembly asm = FindLoadedAssembly(AsmName);
            if (asm == null)
            {
                KickStart.Log("NativeOptionsBridge: Native Options ('0Nuterra.NativeOptions') not loaded — skipping options-menu integration.");
                return;
            }

            Type optionRangeType = asm.GetType(OptionRangeFqn);
            if (optionRangeType == null)
            {
                KickStart.LogWarning($"NativeOptionsBridge: '{OptionRangeFqn}' not found in loaded Native Options assembly; aborting.");
                return;
            }

            ConstructorInfo ctor = optionRangeType.GetConstructor(new[]
            {
                typeof(string), typeof(string), typeof(float), typeof(float), typeof(float), typeof(float)
            });
            if (ctor == null)
            {
                KickStart.LogWarning("NativeOptionsBridge: OptionRange ctor signature changed in this version of Native Options; aborting.");
                return;
            }

            PropertyInfo valueProp = optionRangeType.GetProperty("Value");
            PropertyInfo onChangedProp = optionRangeType.GetProperty("onValueChanged");
            if (valueProp == null || onChangedProp == null)
            {
                KickStart.LogWarning("NativeOptionsBridge: Value/onValueChanged missing on OptionRange; aborting.");
                return;
            }

            s_Ctor = ctor;
            s_ValueProp = valueProp;
            s_OnChangedProp = onChangedProp;
            s_Detected = true;
            KickStart.Log("NativeOptionsBridge: Native Options detected. Sliders will be built when the Options screen first awakens.");
        }

        /// <summary>
        /// Construct all OptionRange instances. Called from a Harmony prefix on
        /// <c>UIScreenOptions.Awake</c> so that the game's UI prefabs are guaranteed to
        /// exist (the prefix runs before vanilla Awake, which itself touches those
        /// prefabs to render the screen).
        ///
        /// Idempotent: re-entry is no-op once options are built.
        /// </summary>
        public static void EnsureOptionsBuilt()
        {
            if (!s_Detected || s_OptionsBuilt) return;

            try
            {
                ConstructorInfo ctor = s_Ctor;
                PropertyInfo onChangedProp = s_OnChangedProp;

                AddSlider(ctor, onChangedProp, "Resource density",        0f,     5f,    0.05f,
                    l => l.ResourceDensityMultiplier,
                    (l, v) => l.ResourceDensityMultiplier = v,
                    null /* read on demand by SceneryCellDensityPatch on newly-streamed tiles */);

                AddSlider(ctor, onChangedProp, "Resource yield",          0f,    20f,    0.25f,
                    l => l.ResourceYieldMultiplier,
                    (l, v) => l.ResourceYieldMultiplier = v,
                    l => ResourceYieldPatch.ApplyToAllLoaded(l.ResourceYieldMultiplier));

                AddSlider(ctor, onChangedProp, "Drill speed",             0f,    50f,    0.25f,
                    l => l.DrillSpeedMultiplier,
                    (l, v) => l.DrillSpeedMultiplier = v,
                    null /* DrillSpeedPatch postfix reads Current on every hit */);

                AddSlider(ctor, onChangedProp, "AutoMiner speed",         0f,    50f,    0.25f,
                    l => l.AutoMinerSpeedMultiplier,
                    (l, v) => l.AutoMinerSpeedMultiplier = v,
                    l => AutoMinerSpeedPatch.ApplyToAllLoaded(l.AutoMinerSpeedMultiplier));

                AddSlider(ctor, onChangedProp, "Atmosphere density",      0f,    10f,    0.1f,
                    l => l.AtmosphereDensityMultiplier,
                    (l, v) => l.AtmosphereDensityMultiplier = v,
                    null /* AtmospherePatch postfix on AerofoilState.CalculateForce reads Current each call */);

                AddSlider(ctor, onChangedProp, "Pickup range",            0.1f,  10f,    0.1f,
                    l => l.ScuPickupRangeMultiplier,
                    (l, v) => l.ScuPickupRangeMultiplier = v,
                    l => PickupRangePatch.ApplyToAllLoaded(l.ScuPickupRangeMultiplier));

                AddSlider(ctor, onChangedProp, "Beam pull strength",      0.1f,  10f,    0.1f,
                    l => l.ScuBeamStrengthMultiplier,
                    (l, v) => l.ScuBeamStrengthMultiplier = v,
                    l => BeamStrengthPatch.ApplyToAllLoaded(l.ScuBeamStrengthMultiplier));

                AddSlider(ctor, onChangedProp, "Stack capacity",          0.5f,  10f,    0.1f,
                    l => l.ScuStackCapacityMultiplier,
                    (l, v) => l.ScuStackCapacityMultiplier = v,
                    l => StackCapacityPatch.ApplyToAllLoaded(l.ScuStackCapacityMultiplier));

                AddSlider(ctor, onChangedProp, "Lift height",             0.5f,   5f,    0.1f,
                    l => l.ScuLiftHeightMultiplier,
                    (l, v) => l.ScuLiftHeightMultiplier = v,
                    l => LiftHeightPatch.ApplyToAllLoaded(l.ScuLiftHeightMultiplier));

                AddSlider(ctor, onChangedProp, "Pickup speed",            0.1f,  10f,    0.1f,
                    l => l.ScuPickupSpeedMultiplier,
                    (l, v) => l.ScuPickupSpeedMultiplier = v,
                    l => PickupSpeedPatch.ApplyToAllLoaded(l.ScuPickupSpeedMultiplier));

                AddSlider(ctor, onChangedProp, "Items per tick",          1f,    10f,    1f,
                    l => l.ScuItemsPerTickMultiplier,
                    (l, v) => l.ScuItemsPerTickMultiplier = v,
                    null /* MultiPickPatch reads multiplier each tick */);

                AddSlider(ctor, onChangedProp, "Max loose items",       500f, 20000f,  100f,
                    l => l.MaxLooseItemCount,
                    (l, v) => l.MaxLooseItemCount = Mathf.RoundToInt(v),
                    null /* MaxLooseItemPatch is a getter Postfix reading Current each call */);

                AddSlider(ctor, onChangedProp, "Loose item lifetime",     0.1f,   5f,    0.1f,
                    l => l.LooseItemLifetimeMultiplier,
                    (l, v) => l.LooseItemLifetimeMultiplier = v,
                    l => LooseItemPatches.ApplyLooseItemLifetime(l.LooseItemLifetimeMultiplier));

                AddSlider(ctor, onChangedProp, "Detached-block heal",     0f,     1f,    0.05f,
                    l => l.BlockDetachHealAmount,
                    (l, v) => l.BlockDetachHealAmount = v,
                    null /* BlockDetachHealPatch postfix reads Current on each detach */);

                s_OptionsBuilt = true;
                KickStart.Log($"NativeOptionsBridge: built {s_Bindings.Count} Tier-2 sliders under '{ModNameLabel}'.");

                // If a world is already loaded (e.g. user reopens Options mid-game and
                // the sliders are being built for the first time), push the live values
                // into the freshly-constructed sliders so they reflect the running world.
                SyncFromCurrent();
            }
            catch (Exception ex)
            {
                KickStart.LogError("NativeOptionsBridge.EnsureOptionsBuilt threw", ex);
            }
        }

        /// <summary>
        /// Push the values from <see cref="SettingsStore.Current"/> into the registered
        /// sliders. Called from the mode-switch handler so the panel reflects the world's
        /// active settings when the player opens the options menu in-game.
        /// </summary>
        public static void SyncFromCurrent()
        {
            if (!s_OptionsBuilt) return;
            Settings cur = SettingsStore.Current;
            if (cur?.Live == null) return;

            s_PushingFromCurrent = true;
            try
            {
                foreach (Binding b in s_Bindings)
                {
                    try
                    {
                        float v = b.Getter(cur.Live);
                        s_ValueProp.SetValue(b.Option, v, null);
                    }
                    catch (Exception ex)
                    {
                        KickStart.LogError($"NativeOptionsBridge.SyncFromCurrent('{b.Name}') failed", ex);
                    }
                }
            }
            finally
            {
                s_PushingFromCurrent = false;
            }
        }

        private static void AddSlider(
            ConstructorInfo ctor,
            PropertyInfo onChangedProp,
            string name,
            float min,
            float max,
            float roundTo,
            Func<LiveSettings, float> getter,
            Action<LiveSettings, float> setter,
            Action<LiveSettings> applyHook)
        {
            float defaultValue = getter(new LiveSettings());
            object instance = ctor.Invoke(new object[] { name, ModNameLabel, defaultValue, min, max, roundTo });

            Binding binding = new Binding
            {
                Name      = name,
                Option    = instance,
                Getter    = getter,
                Setter    = setter,
                ApplyHook = applyHook,
            };
            s_Bindings.Add(binding);

            // onValueChanged is a UnityEvent<float>; we need a UnityAction<float> delegate.
            object evt = onChangedProp.GetValue(instance, null);
            Type unityActionT = typeof(UnityAction<float>);
            MethodInfo addListener = evt.GetType().GetMethod("AddListener", new[] { unityActionT });
            if (addListener == null)
            {
                KickStart.LogWarning($"NativeOptionsBridge: AddListener not found on '{evt.GetType().FullName}' for '{name}' — slider will not respond.");
                return;
            }
            Delegate handler = Delegate.CreateDelegate(unityActionT, binding, nameof(Binding.OnChanged));
            addListener.Invoke(evt, new object[] { handler });
        }

        private static Assembly FindLoadedAssembly(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == name) return a;
            }
            return null;
        }

        /// <summary>
        /// Wires one Native Options slider to one <see cref="LiveSettings"/> field. The
        /// instance is held in <see cref="s_Bindings"/> for the lifetime of the
        /// AppDomain so the delegate remains rooted.
        /// </summary>
        private sealed class Binding
        {
            public string Name;
            public object Option;
            public Func<LiveSettings, float> Getter;
            public Action<LiveSettings, float> Setter;
            public Action<LiveSettings> ApplyHook;

            // Public + non-static so Delegate.CreateDelegate(UnityAction<float>, this, ...) works.
            public void OnChanged(float newValue)
            {
                if (s_PushingFromCurrent) return;

                Settings cur = SettingsStore.Current;
                if (cur?.Live == null)
                {
                    // No world loaded — value lives on the slider only; no engine state to push.
                    return;
                }

                try
                {
                    Setter(cur.Live, newValue);
                    cur.Live.Sanitize();
                    ApplyHook?.Invoke(cur.Live);
                }
                catch (Exception ex)
                {
                    KickStart.LogError($"NativeOptionsBridge: '{Name}' change handler threw", ex);
                }
            }
        }
    }
}
