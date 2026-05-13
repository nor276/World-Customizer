using System;
using UnityEngine;

namespace WorldCustomizer.UI
{
    /// <summary>
    /// IMGUI fallback for the settings popup. Used when
    /// <see cref="UIScreenWorldCustomizeNative"/> can't discover the game's row
    /// prefabs (e.g. <c>ManUI.GetScreen(Options)</c> returns null because the
    /// options screen hasn't been spawned into the canvas yet). Less polished
    /// than the native variant but always works.
    /// </summary>
    /// <remarks>
    /// The screen is built programmatically: a single GameObject with this component plus
    /// no children. Rendering happens in <see cref="OnGUI"/>. Modality is provided by the
    /// game's screen stack via <c>ManUI.PushScreenAsPopup</c>.
    /// </remarks>
    internal class UIScreenWorldCustomize : UIScreen
    {
        private Settings m_Working;
        private Action<Settings> m_OnConfirm;
        private Action m_OnCancel;

        // Window layout (centered on screen, sized in pixels)
        private const int WindowWidth = 540;
        private const int WindowHeight = 720;
        private const int WindowID = 0x57435A4D;   // arbitrary unique ID for IMGUI

        private Vector2 m_ScrollPos;
        private Rect m_WindowRect;

        // Inline warning state (rendered as a second GUI.Window on top of m_WindowRect when set).
        // We don't use PopupHelper from inside an already-pushed popup — see the comment in
        // UIScreenWorldCustomizeNative.BuildWarningOverlay for why.
        private bool m_WarningVisible;
        private string m_WarningMessage;
        private Rect m_WarningRect;
        private const int WarningWindowID = 0x57435A57;

        // ------------------------------------------------------------
        // Factory + show
        // ------------------------------------------------------------
        public static void CreateAndShow(Settings initial, Action<Settings> onConfirm, Action onCancel)
        {
            var go = new GameObject("WorldCustomizer.SettingsScreen");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var screen = go.AddComponent<UIScreenWorldCustomize>();
            screen.m_Working = Settings.Clone(initial);
            screen.m_OnConfirm = onConfirm;
            screen.m_OnCancel = onCancel;
            screen.ScreenInitialize(ManUI.ScreenType.Null);

            // Center the window
            screen.m_WindowRect = new Rect(
                (Screen.width  - WindowWidth)  * 0.5f,
                (Screen.height - WindowHeight) * 0.5f,
                WindowWidth, WindowHeight);

            try
            {
                Singleton.Manager<ManUI>.inst.PushScreenAsPopup(screen);
            }
            catch (Exception ex)
            {
                KickStart.LogError("PushScreenAsPopup threw", ex);
                // Fallback: render anyway by activating the GameObject directly
                go.SetActive(true);
            }
        }

        // ------------------------------------------------------------
        // UIScreen overrides
        // ------------------------------------------------------------
        public override void Show(bool fromStackPop)
        {
            base.Show(fromStackPop);
            BlockScreenExit(exitBlocked: true);
        }

        public override void Hide()
        {
            base.Hide();
            BlockScreenExit(exitBlocked: false);
        }

        public override bool GoBack()
        {
            CancelPressed();
            return false;
        }

        // ------------------------------------------------------------
        // IMGUI rendering
        // ------------------------------------------------------------
        private void OnGUI()
        {
            if (state != State.Show) return;
            m_WindowRect = GUI.Window(WindowID, m_WindowRect, DrawWindow, "World Customization");

            if (m_WarningVisible)
            {
                if (m_WarningRect.width <= 0f)
                {
                    const int w = 520, h = 280;
                    m_WarningRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                }
                // ModalWindow blocks input to the underlying window while shown.
                m_WarningRect = GUI.ModalWindow(WarningWindowID, m_WarningRect, DrawWarning, "Confirm extreme settings");
            }
        }

        private void DrawWarning(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label(m_WarningMessage ?? string.Empty, GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(36), GUILayout.Width(140)))
            {
                m_WarningVisible = false;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Continue", GUILayout.Height(36), GUILayout.Width(140)))
            {
                m_WarningVisible = false;
                DoConfirm();
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawWindow(int id)
        {
            var c = m_Working.Creation;
            var l = m_Working.Live;

            m_ScrollPos = GUILayout.BeginScrollView(m_ScrollPos);

            GUILayout.Label("Geometry — locked after creation", BoldStyle());
            c.ContinentSize      = LabelSlider("Biome size",                                     c.ContinentSize,      0.5f, 5.0f, "0.00");
            c.BiomeChaos         = LabelSlider("Biome chaos",                                     c.BiomeChaos,         0.0f, 1.0f, "0.00");
            c.BiomeEdgeSharpness = LabelSlider("Biome edge sharpness",                            c.BiomeEdgeSharpness, 0.0f, 3.0f, "0.00");
            c.EnableRegions      = LabelToggle("Enable regions",                                  c.EnableRegions);
            c.TileResolution     = LabelChoice("Tile resolution (engine constant, read-only)",    c.TileResolution,     CreationSettings.ValidTileResolutions);
            c.CellScale          = LabelSlider("World cell scale (engine constant, read-only)",   c.CellScale,          4f,   8f,  "0.0");
            c.HeightmapDetail    = LabelChoice("Heightmap detail",                                c.HeightmapDetail,    CreationSettings.ValidHeightmapDetails);
            c.SplatDetail        = LabelChoice("Splat detail",                                    c.SplatDetail,        CreationSettings.ValidSplatDetails);
            c.HeightMultiplier   = LabelSlider("Height multiplier",                               c.HeightMultiplier,   0.1f, 5.0f, "0.00");

            GUILayout.Space(12);
            GUILayout.Label("Gameplay (changeable later)", BoldStyle());
            l.ResourceDensityMultiplier   = LabelSlider("Resource density",  l.ResourceDensityMultiplier,   0.0f, 5.0f, "0.00");
            l.ResourceYieldMultiplier     = LabelSlider("Resource yield",    l.ResourceYieldMultiplier,     0.0f, 20.0f, "0.0");
            l.DrillSpeedMultiplier        = LabelSlider("Drill speed",       l.DrillSpeedMultiplier,        0.0f, 50.0f, "0.0");
            l.AutoMinerSpeedMultiplier    = LabelSlider("AutoMiner speed",   l.AutoMinerSpeedMultiplier,    0.0f, 50.0f, "0.0");
            l.AtmosphereDensityMultiplier = LabelSlider("Atmosphere density",l.AtmosphereDensityMultiplier, 0.0f, 10.0f, "0.00");

            GUILayout.Space(12);
            GUILayout.Label("SCU / Pickup (changeable later)", BoldStyle());
            l.ScuPickupRangeMultiplier   = LabelSlider("Pickup range",       l.ScuPickupRangeMultiplier,   0.1f, 10.0f, "0.00");
            l.ScuBeamStrengthMultiplier  = LabelSlider("Beam pull strength", l.ScuBeamStrengthMultiplier,  0.1f, 10.0f, "0.00");
            l.ScuStackCapacityMultiplier = LabelSlider("Stack capacity",     l.ScuStackCapacityMultiplier, 0.5f, 10.0f, "0.00");
            l.ScuLiftHeightMultiplier    = LabelSlider("Lift height",        l.ScuLiftHeightMultiplier,    0.5f,  5.0f, "0.00");
            l.ScuPickupSpeedMultiplier   = LabelSlider("Pickup speed",       l.ScuPickupSpeedMultiplier,   0.1f, 10.0f, "0.00");
            l.ScuItemsPerTickMultiplier  = LabelSlider("Items per tick",     l.ScuItemsPerTickMultiplier,  1.0f, 10.0f, "0.00");

            GUILayout.Space(12);
            GUILayout.Label("Loose-item budget (changeable later)", BoldStyle());
            l.MaxLooseItemCount           = Mathf.RoundToInt(LabelSlider("Max loose items (cap)", l.MaxLooseItemCount, 500f, 20000f, "0"));
            l.LooseItemLifetimeMultiplier = LabelSlider("Loose item lifetime", l.LooseItemLifetimeMultiplier, 0.1f, 5.0f, "0.00");

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset", GUILayout.Height(36))) ResetPressed();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel",  GUILayout.Height(36), GUILayout.Width(120))) CancelPressed();
            if (GUILayout.Button("Confirm", GUILayout.Height(36), GUILayout.Width(120))) ConfirmPressed();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, WindowWidth, 24));
        }

        // ------------------------------------------------------------
        // IMGUI helpers
        // ------------------------------------------------------------
        private static GUIStyle s_BoldStyle;
        private static GUIStyle BoldStyle()
        {
            if (s_BoldStyle == null)
            {
                s_BoldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }
            return s_BoldStyle;
        }

        private static float LabelSlider(string label, float value, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString(fmt), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return v;
        }

        private static bool LabelToggle(string label, bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            bool v = GUILayout.Toggle(value, value ? "On" : "Off");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return v;
        }

        private static int LabelChoice(string label, int value, int[] allowed)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            int chosen = value;
            for (int i = 0; i < allowed.Length; i++)
            {
                bool isCurrent = allowed[i] == value;
                if (GUILayout.Toggle(isCurrent, allowed[i].ToString(), "Button", GUILayout.Width(56)) && !isCurrent)
                    chosen = allowed[i];
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return chosen;
        }

        // ------------------------------------------------------------
        // Button actions
        // ------------------------------------------------------------
        private void ConfirmPressed()
        {
            m_Working.Sanitize();

            if (LiveSettings.ShouldWarnAboutPhysicsLoad(m_Working.Live, out _, out string warning))
            {
                m_WarningMessage = warning;
                m_WarningVisible = true;
                return;
            }

            DoConfirm();
        }

        private void DoConfirm()
        {
            Settings result = m_Working;
            Action<Settings> onConfirm = m_OnConfirm;
            CloseAndDispose();
            try { onConfirm?.Invoke(result); }
            catch (Exception ex) { KickStart.LogError("Confirm callback threw", ex); }
        }

        private void CancelPressed()
        {
            Action onCancel = m_OnCancel;
            CloseAndDispose();
            try { onCancel?.Invoke(); }
            catch (Exception ex) { KickStart.LogError("Cancel callback threw", ex); }
        }

        private void ResetPressed()
        {
            m_Working = Settings.CreateDefaults();
        }

        private void CloseAndDispose()
        {
            try { Singleton.Manager<ManUI>.inst.PopScreen(showPrev: true); }
            catch (Exception ex) { KickStart.LogError("PopScreen threw on close", ex); }

            // Schedule destruction next frame so we don't tear down mid-callback
            UnityEngine.Object.Destroy(gameObject);
        }

    }
}
