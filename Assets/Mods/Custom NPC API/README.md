# Custom NPC API

`Custom NPC API` is a small runtime library for Big Ambitions mods that need named, configurable NPCs without copying the physical-NPC implementation out of StreetQuestRPG.

The library owns the fragile game-facing pieces:

- spawn a vanilla humanoid prefab such as `Characters/Homeless` or `Characters/DummyHuman`
- apply deterministic `gender` / `ageInDays` / `appearanceSeed`
- position, rotate and scale the NPC
- add a stable click/hover host using the working `SellerStandController` + invisible proxy-renderer pattern
- expose a simple `OnInteract` callback instead of hard-coding a quest system
- open a registered Big Ambitions call dialog from a physical NPC
- create/update vanilla phone contacts and route their Call button to the same dialog type
- provide an optional in-game developer preview window for appearance and placement work

It deliberately does **not** own quests, story flags, schedules, map markers, apartments or relationship systems. Those remain responsibilities of the consuming mod.

## Dependency

A mod that references this library needs `CustomNPCAPI.dll` available as a separate enabled Big Ambitions mod. In the SDK, add the `CustomNPCAPI` assembly definition as a reference to the consuming mod's `.asmdef`.

StreetQuestRPG in this source package is an example consumer: it resolves its quest/state-specific character definition first and then hands the final physical NPC definition to `CustomNpcApi.Spawn(...)`.

## Minimal example

```csharp
using CustomNPCAPI;
using UnityEngine;

private CustomNpcHandle _npc;

private void SpawnNpc()
{
    var definition = new CustomNpcDefinition
    {
        Id = "example:alex",
        DisplayName = "Alex",
        PrefabName = "Characters/Homeless",
        Gender = "Male",
        AgeInDays = 35 * 365,
        AppearanceSeed = 104729,
        Position = new Vector3(301.58f, 0.09f, -188.47f),
        Forward = new Vector3(0f, 0f, -1f),
        LocalEulerAngles = new Vector3(0f, 90f, 0f),
        LocalScale = Vector3.one,
        CtaTextKey = "mymod:cta_talk_alex"
    };

    _npc = CustomNpcApi.Spawn(
        "MyModId",
        definition,
        new CustomNpcSpawnOptions
        {
            OnInteract = _ => CustomNpcApi.OpenDialog("mymod_alex_dialog")
        });
}
```

Dispose the returned handle when your NPC is no longer needed:

```csharp
_npc?.Dispose();
_npc = null;
```

See [docs/MODDER_GUIDE.md](docs/MODDER_GUIDE.md) for placement, appearance, interaction, custom prefabs, phone contacts and the developer preview UI.


## External code-only build

Use the repository's shared external builder. It resolves the declared StreetQuest dependency, builds Custom NPC API first, and references the freshly built API assembly when compiling StreetQuest:

```powershell
.\tools\external-build\BuildBigAmbitionsMods.ps1 -ModName StreetQuestRPG -Install
```

To build only the library, pass `-ModName "Custom NPC API"` instead.
