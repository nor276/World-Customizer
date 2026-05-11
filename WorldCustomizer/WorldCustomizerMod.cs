namespace WorldCustomizer
{
    /// <summary>
    /// Loader-facing entry. The ModExt loader (<c>ManMods</c>) discovers mods by scanning
    /// every loaded DLL for <see cref="ModBase"/>-derived types and instantiating them.
    /// This class is the bridge between that contract and our static
    /// <see cref="KickStart"/> initialization machinery.
    /// </summary>
    /// <remarks>
    /// Must be:
    /// <list type="bullet">
    /// <item>The only <c>ModBase</c>-derived class in this assembly (the loader logs an
    /// error if multiple are present and picks one arbitrarily).</item>
    /// <item>Public, with a parameterless constructor (the loader instantiates via
    /// <c>Activator.CreateInstance</c>).</item>
    /// </list>
    /// Init/DeInit are intentionally thin delegations so the bulk of init logic stays in
    /// <see cref="KickStart"/> where it can be unit-tested without instantiating ModBase.
    /// </remarks>
    public class WorldCustomizerMod : ModBase
    {
        public override void Init()
        {
            KickStart.Initiate();
        }

        public override void DeInit()
        {
            KickStart.DeInit();
        }
    }
}
