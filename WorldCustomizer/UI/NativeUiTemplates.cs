using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace WorldCustomizer.UI
{
    /// <summary>
    /// Locates and caches GameObject "row" templates from the game's own
    /// <c>UIScreenOptions</c> so we can <c>Instantiate</c> native-styled controls
    /// into our settings popup instead of drawing IMGUI ourselves.
    /// </summary>
    /// <remarks>
    /// <para>The options screen's rows are MonoBehaviour wrappers
    /// (<c>UIOptionsBehaviourSlider</c>, <c>UIOptionsBehaviourToggle</c>) attached to
    /// hand-laid-out prefabs. We walk that screen at first use, find the parent of
    /// the first slider/toggle child, and cache the GameObject. That GameObject
    /// becomes our row prefab.</para>
    /// <para>If discovery fails (game UI not loaded yet, hierarchy unexpected), we
    /// log the situation and the caller falls back to the IMGUI screen.</para>
    /// </remarks>
    internal static class NativeUiTemplates
    {
        public static GameObject SliderRowTemplate { get; private set; }
        public static GameObject ToggleRowTemplate { get; private set; }
        public static GameObject ButtonTemplate    { get; private set; }
        public static bool Loaded { get; private set; }

        /// <summary>Run discovery once. Idempotent. Logs a single diagnostic line
        /// summarizing what was found vs missing.</summary>
        public static bool TryLoad()
        {
            if (Loaded) return true;

            UIScreen optionsScreen = null;
            try
            {
                optionsScreen = Singleton.Manager<ManUI>.inst.GetScreen(ManUI.ScreenType.Options);
            }
            catch (Exception ex)
            {
                KickStart.LogWarning($"NativeUiTemplates: GetScreen(Options) threw: {ex.Message}");
                return false;
            }

            if (optionsScreen == null)
            {
                KickStart.LogWarning("NativeUiTemplates: ManUI.GetScreen(Options) returned null");
                return false;
            }

            SliderRowTemplate = FindFirstRow<Slider>(optionsScreen.gameObject);
            ToggleRowTemplate = FindFirstRow<Toggle>(optionsScreen.gameObject);
            ButtonTemplate    = FindFirstStandaloneButton(optionsScreen.gameObject);

            var summary = new StringBuilder("NativeUiTemplates discovery:");
            summary.Append(" slider=").Append(SliderRowTemplate ? Path(SliderRowTemplate.transform) : "<null>");
            summary.Append(" toggle=").Append(ToggleRowTemplate ? Path(ToggleRowTemplate.transform) : "<null>");
            summary.Append(" button=").Append(ButtonTemplate    ? Path(ButtonTemplate.transform)    : "<null>");
            KickStart.Log(summary.ToString());

            Loaded = SliderRowTemplate != null;   // slider is the minimum requirement
            return Loaded;
        }

        /// <summary>
        /// Walk every <typeparamref name="T"/> in the subtree of <paramref name="root"/>,
        /// climb to the nearest ancestor that has BOTH a <typeparamref name="T"/>
        /// somewhere below it AND a Text label somewhere below it, and return that
        /// ancestor — that's the conventional "row" container shape. Widgets that
        /// live inside a Dropdown's internal template are skipped (Unity Dropdowns
        /// use Toggles for their list items, which would otherwise match).
        /// </summary>
        private static GameObject FindFirstRow<T>(GameObject root) where T : Component
        {
            var widgets = root.GetComponentsInChildren<T>(includeInactive: true);
            foreach (var w in widgets)
            {
                if (IsInsideDropdownInternals(w.transform)) continue;

                var t = w.transform.parent;
                int hopsRemaining = 4;
                while (t != null && hopsRemaining-- > 0)
                {
                    if (t.GetComponentInChildren<Text>(includeInactive: true) != null)
                    {
                        return t.gameObject;
                    }
                    t = t.parent;
                }
            }
            return null;
        }

        private static bool IsInsideDropdownInternals(Transform t)
        {
            // A Dropdown has its own Toggle/Slider widgets as part of the list-item
            // template. They live below the Dropdown component in the hierarchy.
            while (t != null)
            {
                if (t.GetComponent<Dropdown>() != null) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>First Button whose parent isn't itself a Slider/Toggle/Dropdown
        /// internal — i.e. a real "action button" we could clone for Confirm/Cancel.</summary>
        private static GameObject FindFirstStandaloneButton(GameObject root)
        {
            var buttons = root.GetComponentsInChildren<Button>(includeInactive: true);
            foreach (var b in buttons)
            {
                // Exclude buttons nested inside scrollbar / dropdown / slider widget chrome
                Transform anc = b.transform.parent;
                bool ok = true;
                int hopsRemaining = 6;
                while (anc != null && hopsRemaining-- > 0)
                {
                    if (anc.GetComponent<Scrollbar>() != null ||
                        anc.GetComponent<Slider>()    != null ||
                        anc.GetComponent<Dropdown>()  != null ||
                        anc.GetComponent<Toggle>()    != null)
                    {
                        ok = false; break;
                    }
                    anc = anc.parent;
                }
                if (ok) return b.gameObject;
            }
            return null;
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder();
            while (t != null)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, t.gameObject.name);
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
