using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorldCustomizer.UI
{
    /// <summary>
    /// Native-styled settings popup that clones the game's own UIScreenOptions row
    /// prefabs (slider, toggle, button) for native look. Falls back to the IMGUI
    /// screen if template discovery fails.
    /// </summary>
    /// <remarks>
    /// Pass 2: full panel with all 14 settings in a scrollable list, cloned native
    /// buttons for Reset/Cancel/Confirm. Labels are rendered via TMP_Text (which
    /// the game uses) — the legacy <c>Text</c> path is kept as a fallback.
    /// </remarks>
    internal class UIScreenWorldCustomizeNative : UIScreen
    {
        private Settings m_Working;
        private Action<Settings> m_OnConfirm;
        private Action m_OnCancel;
        private GameObject m_BackdropRoot;
        private RectTransform m_ContentRT;
        private GameObject m_WarningOverlay;
        private Text m_WarningText;

        public static void CreateAndShow(Settings initial, Action<Settings> onConfirm, Action onCancel)
        {
            if (!NativeUiTemplates.TryLoad())
            {
                KickStart.Log("Native templates unavailable; using IMGUI fallback");
                UIScreenWorldCustomize.CreateAndShow(initial, onConfirm, onCancel);
                return;
            }

            GameObject go = null;
            UIScreenWorldCustomizeNative screen = null;
            try
            {
                go = new GameObject("WorldCustomizer.SettingsScreen.Native");
                UnityEngine.Object.DontDestroyOnLoad(go);

                screen = go.AddComponent<UIScreenWorldCustomizeNative>();
                screen.m_Working = Settings.Clone(initial);
                screen.m_OnConfirm = onConfirm;
                screen.m_OnCancel = onCancel;
                screen.BuildUi();
                screen.ScreenInitialize(ManUI.ScreenType.Null);

                Singleton.Manager<ManUI>.inst.PushScreenAsPopup(screen);
            }
            catch (Exception ex)
            {
                KickStart.LogError("Native popup build threw; falling back to IMGUI", ex);
                // Destroy the partial popup so the orphan backdrop doesn't render
                // underneath the IMGUI fallback we're about to invoke.
                if (screen != null && screen.m_BackdropRoot != null)
                    UnityEngine.Object.Destroy(screen.m_BackdropRoot);
                if (go != null)
                    UnityEngine.Object.Destroy(go);
                UIScreenWorldCustomize.CreateAndShow(initial, onConfirm, onCancel);
            }
        }

        // ------------------------------------------------------------
        // UIScreen plumbing
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

        // Safety net: if the popup is dismissed without going through our buttons
        // (e.g. scene unload, game quit), make sure the backdrop on MainCanvas
        // doesn't outlive us.
        private void OnDestroy()
        {
            if (m_BackdropRoot != null)
            {
                UnityEngine.Object.Destroy(m_BackdropRoot);
                m_BackdropRoot = null;
            }
        }

        // ------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------
        private void BuildUi()
        {
            var mainCanvasGo = GameObject.Find("_GameManager/GUI/MainCanvas");
            if (mainCanvasGo == null)
            {
                throw new InvalidOperationException("MainCanvas missing");
            }

            // Modal backdrop covering the whole canvas
            var backdrop = NewChild(mainCanvasGo, "WC.NativeBackdrop");
            backdrop.transform.SetAsLastSibling();
            var backdropRT = backdrop.GetComponent<RectTransform>();
            StretchToParent(backdropRT);
            var backdropImg = backdrop.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.65f);
            backdropImg.raycastTarget = true;
            m_BackdropRoot = backdrop;

            // Centered panel — sized to give cloned options rows their native dimensions
            const float panelW = 820f;
            const float panelH = 800f;
            const float titleH = 50f;
            const float buttonBarH = 60f;
            const float padding = 16f;

            var panel = NewChild(backdrop, "Panel");
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot     = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(panelW, panelH);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.10f, 0.12f, 0.15f, 0.98f);

            // Title bar
            var title = NewChild(panel, "Title");
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot     = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, titleH);
            titleRT.anchoredPosition = Vector2.zero;
            var titleText = title.AddComponent<Text>();
            titleText.text = "World Customization";
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.fontSize = 26;
            titleText.color = Color.white;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Scroll viewport (middle section)
            float scrollTop = titleH + padding;
            float scrollBottom = buttonBarH + padding;
            var scrollViewGo = NewChild(panel, "ScrollView");
            var scrollViewRT = scrollViewGo.GetComponent<RectTransform>();
            scrollViewRT.anchorMin = new Vector2(0f, 0f);
            scrollViewRT.anchorMax = new Vector2(1f, 1f);
            scrollViewRT.offsetMin = new Vector2(padding, scrollBottom);
            scrollViewRT.offsetMax = new Vector2(-padding, -scrollTop);
            var scrollRect = scrollViewGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            // Viewport with RectMask2D (clips by RectTransform rect; doesn't need
            // an Image, and doesn't have the alpha-threshold issue that Mask + Image
            // had — Mask treats alpha as a stencil threshold, so a near-transparent
            // viewport Image caused EVERYTHING inside to be clipped to invisibility.
            var viewport = NewChild(scrollViewGo, "Viewport");
            var viewportRT = viewport.GetComponent<RectTransform>();
            StretchToParent(viewportRT);
            viewport.AddComponent<RectMask2D>();
            scrollRect.viewport = viewportRT;

            // Content
            var content = NewChild(viewport, "Content");
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, 0f);   // grown by ContentSizeFitter
            contentRT.anchoredPosition = Vector2.zero;
            var vlayout = content.AddComponent<VerticalLayoutGroup>();
            vlayout.padding = new RectOffset(8, 8, 8, 8);
            vlayout.spacing = 6f;
            vlayout.childAlignment = TextAnchor.UpperCenter;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;     // drive row heights from LayoutElement.preferredHeight
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scrollRect.content = contentRT;
            m_ContentRT = contentRT;

            // Populate rows
            PopulateRows();

            // Force a layout rebuild so VLG / ContentSizeFitter recompute right away.
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            // Button bar
            var buttonBar = NewChild(panel, "ButtonBar");
            var buttonBarRT = buttonBar.GetComponent<RectTransform>();
            buttonBarRT.anchorMin = new Vector2(0f, 0f);
            buttonBarRT.anchorMax = new Vector2(1f, 0f);
            buttonBarRT.pivot     = new Vector2(0.5f, 0f);
            buttonBarRT.sizeDelta = new Vector2(-2f * padding, buttonBarH);
            buttonBarRT.anchoredPosition = new Vector2(0f, padding * 0.5f);

            // Reset (left)
            AddBarButton(buttonBar, "Reset", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(140f, 44f), new Vector2(0f, 0f), ResetPressed);
            // Confirm (right)
            AddBarButton(buttonBar, "Confirm", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(160f, 44f), new Vector2(0f, 0f), ConfirmPressed);
            // Cancel (just left of Confirm)
            AddBarButton(buttonBar, "Cancel", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(140f, 44f), new Vector2(-170f, 0f), CancelPressed);

            // Warning overlay (sibling of Panel, hidden by default). Built inline rather than
            // via PopupHelper / PushScreenAsPopup because pushing a second NotificationScreen
            // on top of our already-popup-stacked customize screen does not display — UI
            // stack mechanics quietly drop the second popup and our onDecline (no-op) fires,
            // making the Confirm button look broken from the user's perspective.
            BuildWarningOverlay();
        }

        private void BuildWarningOverlay()
        {
            m_WarningOverlay = NewChild(m_BackdropRoot, "WarningOverlay");
            var overlayRT = m_WarningOverlay.GetComponent<RectTransform>();
            StretchToParent(overlayRT);
            var dim = m_WarningOverlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;   // block clicks to the customize panel beneath

            var warnPanel = NewChild(m_WarningOverlay, "WarnPanel");
            var warnRT = warnPanel.GetComponent<RectTransform>();
            warnRT.anchorMin = new Vector2(0.5f, 0.5f);
            warnRT.anchorMax = new Vector2(0.5f, 0.5f);
            warnRT.pivot     = new Vector2(0.5f, 0.5f);
            warnRT.sizeDelta = new Vector2(620f, 300f);
            var panelImg = warnPanel.AddComponent<Image>();
            panelImg.color = new Color(0.18f, 0.10f, 0.10f, 0.98f);

            // Title
            var title = NewChild(warnPanel, "Title");
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot     = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 44f);
            titleRT.anchoredPosition = Vector2.zero;
            var titleText = title.AddComponent<Text>();
            titleText.text = "Confirm extreme settings";
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(1f, 0.85f, 0.4f, 1f);
            titleText.alignment = TextAnchor.MiddleCenter;

            // Message body
            var msg = NewChild(warnPanel, "Message");
            var msgRT = msg.GetComponent<RectTransform>();
            msgRT.anchorMin = new Vector2(0f, 0f);
            msgRT.anchorMax = new Vector2(1f, 1f);
            msgRT.offsetMin = new Vector2(24f, 80f);
            msgRT.offsetMax = new Vector2(-24f, -52f);
            m_WarningText = msg.AddComponent<Text>();
            m_WarningText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_WarningText.fontSize = 16;
            m_WarningText.color = Color.white;
            m_WarningText.alignment = TextAnchor.MiddleCenter;
            m_WarningText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_WarningText.text = string.Empty;

            // Buttons row (Continue + Back) at the bottom of the panel
            AddBarButton(warnPanel, "Continue",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                size: new Vector2(150f, 44f), offset: new Vector2(-85f, 24f),
                onClick: () => { HideWarning(); DoConfirm(); });
            AddBarButton(warnPanel, "Back",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                size: new Vector2(150f, 44f), offset: new Vector2(85f, 24f),
                onClick: HideWarning);

            m_WarningOverlay.SetActive(false);
        }

        private void ShowWarning(string message)
        {
            if (m_WarningOverlay == null) return;
            if (m_WarningText != null) m_WarningText.text = message;
            m_WarningOverlay.transform.SetAsLastSibling();
            m_WarningOverlay.SetActive(true);
        }

        private void HideWarning()
        {
            if (m_WarningOverlay != null) m_WarningOverlay.SetActive(false);
        }

        private void PopulateRows()
        {
            var c = m_Working.Creation;
            var l = m_Working.Live;

            AddSectionHeader("Geometry — locked after creation");
            AddSliderRow("Biome size", c.ContinentSize, 0.5f, 5.0f, "0.00", v => m_Working.Creation.ContinentSize = v);
            AddSliderRow("Biome chaos", c.BiomeChaos, 0f, 1f, "0.00", v => m_Working.Creation.BiomeChaos = v);
            AddSliderRow("Biome edge sharpness", c.BiomeEdgeSharpness, 0f, 3f, "0.00", v => m_Working.Creation.BiomeEdgeSharpness = v);
            AddToggleRow("Enable regions", c.EnableRegions, v => m_Working.Creation.EnableRegions = v);
            AddSliderRow("Height multiplier", c.HeightMultiplier, 0.1f, 5.0f, "0.00", v => m_Working.Creation.HeightMultiplier = v);

            AddSectionHeader("Gameplay — changeable later");
            AddSliderRow("Resource density",   l.ResourceDensityMultiplier,   0f,  5f,  "0.00", v => m_Working.Live.ResourceDensityMultiplier = v);
            AddSliderRow("Resource yield",     l.ResourceYieldMultiplier,     0f, 20f,  "0.0",  v => m_Working.Live.ResourceYieldMultiplier = v);
            AddSliderRow("Drill speed",        l.DrillSpeedMultiplier,        0f, 50f,  "0.0",  v => m_Working.Live.DrillSpeedMultiplier = v);
            AddSliderRow("AutoMiner speed",    l.AutoMinerSpeedMultiplier,    0f, 50f,  "0.0",  v => m_Working.Live.AutoMinerSpeedMultiplier = v);
            AddSliderRow("Atmosphere density", l.AtmosphereDensityMultiplier, 0f, 10f,  "0.00", v => m_Working.Live.AtmosphereDensityMultiplier = v);

            AddSectionHeader("SCU / Pickup — changeable later");
            AddSliderRow("Pickup range",       l.ScuPickupRangeMultiplier,    0.1f, 10f, "0.00", v => m_Working.Live.ScuPickupRangeMultiplier = v);
            AddSliderRow("Beam pull strength", l.ScuBeamStrengthMultiplier,   0.1f, 10f, "0.00", v => m_Working.Live.ScuBeamStrengthMultiplier = v);
            AddSliderRow("Stack capacity",     l.ScuStackCapacityMultiplier,  0.5f, 10f, "0.00", v => m_Working.Live.ScuStackCapacityMultiplier = v);
            AddSliderRow("Lift height",        l.ScuLiftHeightMultiplier,     0.5f, 5f,  "0.00", v => m_Working.Live.ScuLiftHeightMultiplier = v);
            AddSliderRow("Pickup speed",       l.ScuPickupSpeedMultiplier,    0.1f, 10f, "0.00", v => m_Working.Live.ScuPickupSpeedMultiplier = v);
            AddSliderRow("Items per tick",     l.ScuItemsPerTickMultiplier,   1f,   10f, "0.00", v => m_Working.Live.ScuItemsPerTickMultiplier = v);

            AddSectionHeader("Loose-item budget — changeable later");
            AddSliderRow("Max loose items (cap)", l.MaxLooseItemCount,           500f, 20000f, "0",    v => m_Working.Live.MaxLooseItemCount = Mathf.RoundToInt(v));
            AddSliderRow("Loose item lifetime",   l.LooseItemLifetimeMultiplier, 0.1f, 5f,     "0.00", v => m_Working.Live.LooseItemLifetimeMultiplier = v);

            AddSectionHeader("Combat / Salvage — changeable later");
            AddSliderRow("Detached-block heal",   l.BlockDetachHealAmount,       0f,   1f,     "0.00", v => m_Working.Live.BlockDetachHealAmount = v);
        }

        // ------------------------------------------------------------
        // Row factories
        // ------------------------------------------------------------
        private void AddSectionHeader(string text)
        {
            var go = NewChild(m_ContentRT.gameObject, "Section_" + text);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;
            le.flexibleWidth = 1f;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.color = new Color(0.7f, 0.85f, 1f, 1f);
            t.fontSize = 18;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleLeft;
        }

        private void AddSliderRow(string label, float initial, float min, float max, string format, Action<float> onChange)
        {
            var row = SafeCloneAndStripWrappers(NativeUiTemplates.SliderRowTemplate, m_ContentRT);
            row.name = "Row_" + label;

            // Reset row anchors + size + position so VLG can drive it from scratch.
            // The cloned prefab carries over sizeDelta and anchoredPos from its
            // original (wider) parent context — with stretch anchors that would
            // turn into a row much wider than our panel, offset off to the side.
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot     = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(0f, rrt.sizeDelta.y);
            rrt.anchoredPosition = Vector2.zero;

            EnsureRowHeight(row, 46f);

            var slider = row.GetComponentInChildren<Slider>(includeInactive: true);
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = Mathf.Clamp(initial, min, max);

            // Force the handle visible. The cloned native slider has a Handle child,
            // but its Image color is sometimes near-transparent in the source prefab.
            // Without a visible handle, sliders at low values look like an empty bar
            // (no fill, no thumb) — particularly painful for our 0–50 ranges.
            if (slider.handleRect != null)
            {
                var handleImg = slider.handleRect.GetComponent<Image>();
                if (handleImg != null)
                {
                    handleImg.color = new Color(0.85f, 0.95f, 1f, 1f);
                }
            }

            // Build the label string and bind it to whatever first text component the
            // row has (TMP_Text in the modern game, legacy Text in old prefabs). The
            // value is shown inline in the label so we don't have to guess which
            // child is the value readout.
            string Compose(float v) => $"{label}: {v.ToString(format)}";
            SetFirstText(row, Compose(slider.value));

            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(v =>
            {
                onChange(v);
                SetFirstText(row, Compose(v));
            });
        }

        private void AddToggleRow(string label, bool initial, Action<bool> onChange)
        {
            // If we have a toggle template, clone it. Otherwise fall back to a manually
            // built row with a labeled Toggle.
            GameObject row;
            Toggle toggle;
            if (NativeUiTemplates.ToggleRowTemplate != null)
            {
                row = SafeCloneAndStripWrappers(NativeUiTemplates.ToggleRowTemplate, m_ContentRT);
                row.name = "Row_" + label;
                toggle = row.GetComponentInChildren<Toggle>(includeInactive: true);
            }
            else
            {
                row = NewChild(m_ContentRT.gameObject, "Row_" + label);
                toggle = row.AddComponent<Toggle>();
            }
            EnsureRowHeight(row, 40f);

            if (toggle != null)
            {
                toggle.isOn = initial;
                toggle.onValueChanged.RemoveAllListeners();
                toggle.onValueChanged.AddListener(v =>
                {
                    onChange(v);
                    SetFirstText(row, $"{label}: {(v ? "On" : "Off")}");
                });
            }
            SetFirstText(row, $"{label}: {(initial ? "On" : "Off")}");
        }

        private void AddBarButton(GameObject bar, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 offset, Action onClick)
        {
            GameObject go;
            if (NativeUiTemplates.ButtonTemplate != null)
            {
                go = SafeCloneAndStripWrappers(NativeUiTemplates.ButtonTemplate, bar.transform);
                go.name = "Btn_" + label;
            }
            else
            {
                go = NewChild(bar, "Btn_" + label);
                go.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.25f, 1f);
                go.AddComponent<Button>();
            }
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            SetFirstText(go, label);
            var btn = go.GetComponentInChildren<Button>(includeInactive: true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => onClick());
            }
        }

        private static void EnsureRowHeight(GameObject row, float height)
        {
            // If the clone already has a LayoutElement (the options-row prefab does),
            // respect its preferredHeight — it knows how tall the row needs to be to
            // fit its internal label + slider. Only add one if missing.
            var le = row.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = row.AddComponent<LayoutElement>();
                le.preferredHeight = height;
            }
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
        }

        // ------------------------------------------------------------
        // Text helpers (Text + TMP_Text)
        // ------------------------------------------------------------
        /// <summary>
        /// Set the first label-like text in a subtree. Duck-types via reflection so
        /// it catches Text, TMP_Text, TextMeshProUGUI, TextMeshPro, and any other
        /// component exposing a writable string <c>text</c> property — the game has
        /// several variants and we don't want to enumerate them.
        /// </summary>
        private static void SetFirstText(GameObject root, string text)
        {
            var components = root.GetComponentsInChildren<Component>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                var t = c.GetType();
                // Limit to types whose name says "Text" so we don't accidentally set
                // unrelated string properties (e.g. InputField, Selectable.text on
                // some custom widget).
                if (!t.Name.Contains("Text") && t.Name != "TMP_Text" && !t.Name.Contains("TextMesh")) continue;
                var prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                {
                    prop.SetValue(c, text, null);
                    return;
                }
            }
        }

        // ------------------------------------------------------------
        // Cloning helpers
        // ------------------------------------------------------------
        private static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }

        private static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Instantiate a game-side UI template safely:
        /// 1. Toggle the source inactive so the clone is born inactive — prevents
        ///    OnEnable from firing during cloning on the game's stateful wrapper
        ///    MonoBehaviours that haven't been initialized by the UI pool.
        /// 2. Destroy every UIOptionsBehaviour* on the clone (they NRE without
        ///    OnPool initialisation) and every LocalisedText* (which would overwrite
        ///    our manually-set labels on Awake/OnEnable from locale data).
        /// 3. Reparent and activate.
        /// </summary>
        private static GameObject SafeCloneAndStripWrappers(GameObject template, Transform parent)
        {
            bool prevActive = template.activeSelf;
            template.SetActive(false);
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(template);
            }
            finally
            {
                template.SetActive(prevActive);
            }

            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                string typeName = b.GetType().Name;
                // Strip:
                //   UIOptionsBehaviour*    — game UI wrappers that NRE without OnPool init
                //   *Localis*              — UILocalisedText etc. that would overwrite our labels
                //   StateMachineRunner     — leftover from the wrappers' FSMs
                //   UISfx*                 — sfx hooks that may bind to systems we don't init
                if (typeName.StartsWith("UIOptionsBehaviour") ||
                    typeName.Contains("Localis") ||
                    typeName == "StateMachineRunner" ||
                    typeName.StartsWith("UISfx"))
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }
            }

            clone.transform.SetParent(parent, worldPositionStays: false);
            clone.SetActive(true);
            return clone;
        }

        // ------------------------------------------------------------
        // Buttons
        // ------------------------------------------------------------
        private void ConfirmPressed()
        {
            m_Working.Sanitize();

            if (LiveSettings.ShouldWarnAboutPhysicsLoad(m_Working.Live, out _, out string warning))
            {
                ShowWarning(warning);
                return;
            }

            DoConfirm();
        }

        private void DoConfirm()
        {
            var result = m_Working;
            var cb = m_OnConfirm;
            CloseAndDispose();
            try { cb?.Invoke(result); }
            catch (Exception ex) { KickStart.LogError("Confirm callback threw", ex); }
        }

        private void CancelPressed()
        {
            var cb = m_OnCancel;
            CloseAndDispose();
            try { cb?.Invoke(); }
            catch (Exception ex) { KickStart.LogError("Cancel callback threw", ex); }
        }

        private void ResetPressed()
        {
            m_Working = Settings.CreateDefaults();
            // Rebuild rows in-place
            for (int i = m_ContentRT.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(m_ContentRT.GetChild(i).gameObject);
            }
            PopulateRows();
        }

        private void CloseAndDispose()
        {
            try { Singleton.Manager<ManUI>.inst.PopScreen(showPrev: true); }
            catch (Exception ex) { KickStart.LogError("PopScreen threw on close", ex); }
            if (m_BackdropRoot != null)
            {
                UnityEngine.Object.Destroy(m_BackdropRoot);
                m_BackdropRoot = null;
            }
            UnityEngine.Object.Destroy(gameObject);
        }

    }
}
