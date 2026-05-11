using System;

namespace WorldCustomizer.UI
{
    /// <summary>
    /// Wrapper around <c>UIScreenNotifications</c> for two-button modal dialogs.
    /// </summary>
    /// <remarks>
    /// The base game's notification screen is a singleton instance retrieved via
    /// <c>ManUI.GetScreen(ScreenType.NotificationScreen)</c>. We push it as a popup over
    /// the current screen, configure both buttons, and ensure each button's callback first
    /// pops the notification screen before invoking the user's action.
    /// </remarks>
    internal static class PopupHelper
    {
        /// <summary>Rewired action ID for "menu accept" (matches the game's convention).</summary>
        private const int RewiredAccept = 21;

        /// <summary>Rewired action ID for "menu cancel".</summary>
        private const int RewiredDecline = 22;

        /// <summary>
        /// Show a two-button modal popup. Each button automatically pops the popup before
        /// invoking the supplied callback. If the notification screen cannot be obtained
        /// (e.g. the loader hasn't finished), the decline callback runs synchronously as a
        /// fallback so the caller's flow doesn't deadlock.
        /// </summary>
        public static void ShowYesNo(
            string message,
            string acceptLabel,
            string declineLabel,
            Action onAccept,
            Action onDecline)
        {
            ManUI ui = Singleton.Manager<ManUI>.inst;
            if (ui == null)
            {
                KickStart.LogError("PopupHelper.ShowYesNo: ManUI.inst is null");
                onDecline?.Invoke();
                return;
            }

            UIScreenNotifications notif = ui.GetScreen(ManUI.ScreenType.NotificationScreen) as UIScreenNotifications;
            if (notif == null)
            {
                KickStart.LogError("PopupHelper.ShowYesNo: NotificationScreen not found");
                onDecline?.Invoke();
                return;
            }

            UIButtonData accept = new UIButtonData
            {
                m_Label = acceptLabel,
                m_Callback = () =>
                {
                    try { ui.PopScreen(showPrev: true); }
                    catch (Exception ex) { KickStart.LogError("PopScreen threw on accept", ex); }
                    onAccept?.Invoke();
                },
                m_RewiredAction = RewiredAccept
            };

            UIButtonData decline = new UIButtonData
            {
                m_Label = declineLabel,
                m_Callback = () =>
                {
                    try { ui.PopScreen(showPrev: true); }
                    catch (Exception ex) { KickStart.LogError("PopScreen threw on decline", ex); }
                    onDecline?.Invoke();
                },
                m_RewiredAction = RewiredDecline
            };

            notif.Set(message, accept, decline);
            ui.PushScreenAsPopup(notif);
        }
    }
}
