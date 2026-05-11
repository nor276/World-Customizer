# Workshop assets

Source files that ship alongside the compiled DLL when the mod is uploaded to
the Steam Workshop. The build copies anything listed under `WorkshopPayload`
in `WorldCustomizer.csproj` into both `workshop-staging/` and
`Documents/My Games/TerraTech/Mods/WorldCustomizer/`.

## Files

### WorldCustomizer_bundle

**Required for Workshop upload; not currently committed.** TerraTech's
loader (`ManMods`) registers every mod folder by loading a Unity AssetBundle
named `<mod folder>/<mod name>_bundle` and reading a `Contents.asset` of
type `ModContents` from it. If the file is missing or invalid, the loader
logs `Load AssetBundle failed for mod <id>` and skips the folder — including
the DLL inside it.

For a pure-code mod like this one, the bundle's `ModContents` is empty (no
blocks/corps/skins) — it just satisfies the loader contract. The only known
way to produce it is Unity 2018.4.13f1 + Payload Studios' TerraTech ModTool
unitypackage; opening the Mod Designer, naming the mod `WorldCustomizer`,
and clicking Export produces a `WorldCustomizer_bundle` file in the local
mods folder.

The csproj's `WorkshopPayload` rule is **conditional** on this file
existing, so builds succeed without it — they just produce an unpublishable
staged folder. Add the file here once it's authored.

### preview.png

Steam Workshop preview image. 512×512 minimum, 1:1 aspect ratio, PNG.
Workshop displays this as the mod thumbnail.

Not committed yet — any 512×512 PNG works. Like the bundle, its inclusion in
`WorkshopPayload` is conditional, so build doesn't fail without it.

## Current dev workflow

The development workflow described in [`../README.md`](../README.md) does
**not** use `workshop-assets/WorldCustomizer_bundle`. Instead, the mod runs
out of a subscribed workshop folder whose existing `_bundle` file serves as
the marker. That's why this folder is currently bundle-less and the staged
output isn't Workshop-publishable.

## Excluded from the staged folder

This README is project documentation; the build only copies files explicitly
listed under `WorkshopPayload` in `WorldCustomizer.csproj`.
