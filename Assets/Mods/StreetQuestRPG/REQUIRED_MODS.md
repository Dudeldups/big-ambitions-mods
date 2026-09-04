# StreetQuestRPG Required Mods

StreetQuestRPG now requires **Custom NPC API**.

Required technical assembly:

```text
CustomNPCAPI.dll
```

For SDK development, keep both folders in the same checkout:

```text
Assets/Mods/Custom NPC API/
Assets/Mods/StreetQuestRPG/
```

`StreetQuestRPG.asmdef` references the `CustomNPCAPI` assembly by GUID.

For a code-only external build, use the helper shipped with the library:

```powershell
cd "Assets\Mods\Custom NPC API"
.\tools\BuildLibraryAndStreetQuest.ps1
```

For Workshop distribution, publish/enable **Custom NPC API** as a required dependency rather than bundling a private copy of `CustomNPCAPI.dll` inside StreetQuestRPG.
