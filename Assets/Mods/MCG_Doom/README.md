# MCG_Doom

DOOM as a game for the **More Computer Games** framework in Big Ambitions.

The mod is structured like the existing MCG games: a small `ComputerGameMod<TGame>` entry point registers the title, while `DoomGame` owns a camera and delegates the actual game to a small runtime adapter. The original Managed Doom desktop frontend is not used.

## Current implementation

The first implementation targets a deliberately small, testable slice:

- original DOOM shareware game data (`doom1.wad`)
- Managed Doom software renderer
- 320x200 point-filtered Unity framebuffer
- MCG-owned camera output
- fixed 35 Hz DOOM game ticks
- keyboard and mouse controls
- native DOOM menu/automap remapped away from MCG-reserved keys
- short startup controls hint for the remapped DOOM / MCG keys
- sound and music disabled for the first runtime test

## Folder layout

```text
MCG_Doom/
├─ Config/
│  └─ Doom/
│     └─ doom1.wad                 # added by PrepareThirdParty.ps1
├─ Locales/
├─ Scripts/
│  ├─ Core/                        # lifecycle/path handling
│  ├─ Input/                       # Unity -> DOOM input bridge
│  ├─ Rendering/                   # DOOM framebuffer -> Unity
│  └─ ThirdParty/
│     └─ ManagedDoom/              # vendored engine source
├─ ThirdParty/
│  └─ Licenses/
├─ tools/
│  └─ PrepareThirdParty.ps1
├─ MCG_Doom.asmdef
└─ ModManifest.asset
```

## Prepare the third-party source and game data

Run once from PowerShell:

```powershell
cd Assets\Mods\MCG_Doom
.\tools\PrepareThirdParty.ps1
```

The script:

1. resolves the requested Managed Doom Git ref to a concrete commit,
2. downloads that source revision,
3. copies `ManagedDoom/src` into `Scripts/ThirdParty/ManagedDoom`,
4. removes the Silk.NET desktop frontend,
5. copies Managed Doom's GPLv2 license notice,
6. downloads Debian's unmodified DOOM 1.9 shareware source archive,
7. verifies the known archive MD5,
8. extracts `doom1.wad` into `Config/Doom`, and
9. writes a local preparation record with hashes.

The resulting mod folder therefore contains everything the player needs. The player is **not** expected to provide a WAD.

To refresh the payload deliberately:

```powershell
.\tools\PrepareThirdParty.ps1 -Force
```

To pin a specific Managed Doom revision:

```powershell
.\tools\PrepareThirdParty.ps1 -ManagedDoomRef <commit-sha> -Force
```

After the first known-good in-game build, pin that commit rather than tracking `master` for releases.

## Controls

MCG reserves `Tab`, `Backspace`, and `Escape`, so DOOM cannot use those keys directly.

| Action | Keys |
| --- | --- |
| Move | `WASD` |
| Turn | Left / Right arrow or mouse |
| Fire | Left Ctrl or left mouse button |
| Use / Open | Space or right mouse button |
| Run | Shift |
| Weapons | `1` - `7` |
| Automap | `M` |
| DOOM menu | `P` |
| Leave computer | `Tab` (MCG) |
| MCG launcher/back | `Backspace` (MCG) |
| Big Ambitions pause | `Escape` (host) |

The normal DOOM arrow-key movement is also retained where it does not conflict with the modern WASD mapping.

When a DOOM session starts, a small controls hint is shown at the bottom of the game image for about 7.5 seconds. It calls out `P = DOOM MENU`, `M = AUTOMAP`, `Backspace = game selection`, and `Tab = leave PC`, then hides automatically.

A copy-ready Steam Workshop BBCode section is included in `WORKSHOP_CONTROLS.bbcode.txt`.

## Build

This mod is intended to live at:

```text
Assets/Mods/MCG_Doom
```

After `PrepareThirdParty.ps1` has been run, use the repository's normal external build path:

```powershell
.\tools\external-build\BuildBigAmbitionsMods.ps1 -ModName MCG_Doom -Install
```

The existing external build copies `Config/`, so `Config/Doom/doom1.wad` is installed beside the mod DLL automatically.

## More Computer Games dependency

Do **not** bundle the MCG library into this mod. It is a dependency of the game mod, just like Tetrix uses the library rather than embedding it.

The asmdef references the same MCG Unity assembly GUID used by the Tetrix example.

## Licensing

The adapter/mod source in this folder is prepared to be distributed under GPLv2-or-later together with the GPLv2-or-later Managed Doom source used by it. See `LICENSE` and `THIRD_PARTY_NOTICES.md`.

`doom1.wad` is the original DOOM shareware IWAD and remains copyright id Software. It is kept unmodified and separate from the code. See `DOOM_SHAREWARE_NOTICE.md`.

## First test checklist

Before adding audio, verify these in-game:

1. DOOM appears in the MCG launcher.
2. The title screen is visible and has correct orientation/aspect ratio.
3. `P` opens/closes the DOOM menu without triggering the Big Ambitions pause menu.
4. A new game can reach E1M1.
5. WASD, turning, fire and use work.
6. `M` toggles the automap.
7. Leaving the PC and reopening it does not leak input or leave the MCG camera active.
8. Repeated game sessions clean up the Unity texture/material/camera correctly.

Once those pass, sound effects and music should be added as separate `ISound` and `IMusic` adapters.


## Local build

After `PrepareThirdParty.ps1`, build/install with:

```powershell
.\tools\BuildAndInstall.ps1
```

This supplies the separately installed `LIB_BaComputerGames.dll` as a compile-only reference for the existing SDK external builder. It is removed immediately after the build and is never bundled into MCG_Doom.

## .NET Framework 4.7.2 compatibility

The current Big Ambitions external build may compile this mod as `net472`. Current Managed Doom uses a handful of newer BCL APIs. `tools/BuildAndInstall.ps1` therefore runs `tools/ApplyManagedDoomCompatibility.ps1` before every build. The patch is idempotent and backports only the APIs needed by the vendored engine (`Math.Clamp`, `MathF.Round`, `Dictionary.TryAdd`, `Stream.ReadExactly`, newer `String.Split` overloads, `ExceptionDispatchInfo.Throw(Exception)`, and the `Span`/`MemoryMarshal` framebuffer write).

Do not manually edit all vendored files for these compatibility changes. Re-running the build wrapper is sufficient.
