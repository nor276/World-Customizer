using HarmonyLib;
using WorldCustomizer.UI;

namespace WorldCustomizer.Patches
{
    /// <summary>
    /// Prefix on <c>UIScreenNewGame.StartGameMode</c>. Intercepts the click that would
    /// normally start the new world and shows a "Customize?" popup instead. The popup's
    /// button callbacks re-invoke <c>StartGameMode</c> with a re-entry flag set so the
    /// second pass runs the original.
    /// </summary>
    /// <remarks>
    /// <para>Why <c>StartGameMode</c> and not <c>ApplyModeSettingsAndStart</c>:
    /// <c>UIGameModePlayButton.OnPlayClicked</c> calls <c>ApplyModeSettings(mode)</c> and
    /// <c>StartGameMode()</c> as two separate calls; the convenience wrapper
    /// <c>ApplyModeSettingsAndStart</c> exists in the source but is not invoked by the UI.
    /// Patching <c>StartGameMode</c> is the only reliable interception point for the
    /// new-game flow that is actually exercised in-game.</para>
    /// <para>Multiplayer is detected and the popup skipped — host/client settings sync
    /// is out of scope for v1.</para>
    /// </remarks>
    [HarmonyPatch(typeof(UIScreenNewGame), nameof(UIScreenNewGame.StartGameMode))]
    internal static class NewGameHook
    {
        private static bool s_PromptHandled;

        private static bool Prefix(UIScreenNewGame __instance)
        {
            KickStart.Log("NewGameHook: StartGameMode invoked");

            // Re-entry from the popup callback: clear the flag and let the original run.
            if (s_PromptHandled)
            {
                s_PromptHandled = false;
                return true;
            }

            // Multiplayer: never prompt. v1 defers MP support.
            if (IsMultiplayer())
            {
                KickStart.Log("NewGameHook: skipping popup in multiplayer");
                return true;
            }

            UIScreenNewGame screen = __instance;
            PopupHelper.ShowYesNo(
                message: "Customize world generation?",
                acceptLabel: "Customize",
                declineLabel: "Use Defaults",
                onAccept: () =>
                {
                    KickStart.Log("NewGameHook: user picked Customize");
                    Settings initial = SettingsStore.Pending ?? Settings.CreateDefaults();
                    UIScreenWorldCustomizeNative.CreateAndShow(
                        initial: initial,
                        onConfirm: chosen =>
                        {
                            KickStart.Log("NewGameHook: settings confirmed, starting world");
                            SettingsStore.Pending = chosen;
                            ProceedToGameStart(screen);
                        },
                        onCancel: () =>
                        {
                            KickStart.Log("NewGameHook: customize cancelled, returning to new-game screen");
                            // Don't proceed — user is back at the new-game screen.
                        });
                },
                onDecline: () =>
                {
                    KickStart.Log("NewGameHook: user picked Use Defaults");
                    // Set Pending to an explicit defaults instance, NOT null. The Promote()
                    // fallback chain is Pending ?? Current ?? defaults, so leaving Pending
                    // null after the user has already played a customized world this session
                    // would have Promote fall through to that world's Current — silently
                    // applying the prior world's settings to this "default" new game.
                    SettingsStore.Pending = Settings.CreateDefaults();
                    ProceedToGameStart(screen);
                });

            // Skip the original — the popup callback will re-invoke after the user picks.
            return false;
        }

        private static void ProceedToGameStart(UIScreenNewGame screen)
        {
            if (screen == null) return;
            s_PromptHandled = true;
            screen.StartGameMode();
        }

        private static bool IsMultiplayer()
        {
            try
            {
                ManNetwork net = Singleton.Manager<ManNetwork>.inst;
                return net != null && net.IsMultiplayer();
            }
            catch
            {
                return false;
            }
        }
    }
}
