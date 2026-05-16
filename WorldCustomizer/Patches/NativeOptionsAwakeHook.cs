using HarmonyLib;
using WorldCustomizer.Integration;

namespace WorldCustomizer.Patches
{
    /// <summary>
    /// Harmony prefix on <c>UIScreenOptions.Awake</c> that triggers
    /// <see cref="NativeOptionsBridge.EnsureOptionsBuilt"/> the first time the in-game
    /// Options screen wakes up. By then the game's UI fonts and prefabs are guaranteed
    /// to be loaded (Awake is about to render the screen using them), so Native
    /// Options' <c>UIElements</c> static ctor — which probes
    /// <c>Resources.FindObjectsOfTypeAll&lt;Font&gt;()</c> for "Exo-SemiBold" et al —
    /// can succeed.
    ///
    /// Running as a prefix matters: Native Options itself postfixes the same method to
    /// build the "MODS" tab and drain its pending-options list. Our prefix ensures our
    /// sliders are in that pending list before the drain happens.
    ///
    /// No-op when Native Options isn't installed.
    /// </summary>
    [HarmonyPatch(typeof(UIScreenOptions), "Awake")]
    internal static class NativeOptionsAwakeHook
    {
        private static void Prefix()
        {
            NativeOptionsBridge.EnsureOptionsBuilt();
        }
    }
}
