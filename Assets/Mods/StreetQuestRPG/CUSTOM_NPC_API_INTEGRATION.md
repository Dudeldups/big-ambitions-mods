# StreetQuestRPG -> Custom NPC API

StreetQuestRPG now uses the separate `CustomNPCAPI` assembly for the reusable game-facing NPC layer.

Moved out of StreetQuest:

- deterministic humanoid prefab initialization (`gender`, `ageInDays`, `appearanceSeed`)
- visual transform/scale and hidden-child cleanup
- physical interaction collider
- invisible interaction proxy renderer
- runtime `SellerStandController` setup
- shared CTA routing
- manual dialog opening helper
- vanilla phone-contact creation / call-dialog override / message-history helpers
- generic appearance preview implementation

StreetQuest still owns the systems that are specific to StreetQuest content:

- JSON character catalog
- appearances/states and quest/story requirements
- schedules and indoor building gates
- walkers / walk-in / walk-away logic
- speech bubbles
- map markers / People filter
- routed NPC apartments
- quest/favor/inventory/dialog content

At runtime StreetQuest resolves the final active `StreetQuestCharacterDefinition`, converts only its physical representation through `StreetQuestCustomNpcAdapter`, and calls `CustomNpcApi.Spawn(...)`.

`StreetQuestRPG.asmdef` references `CustomNPCAPI.asmdef`, so Custom NPC API must be present/enabled as a runtime dependency.

For the code-only SDK build, use:

```powershell
cd "Assets\Mods\Custom NPC API"
.\tools\BuildLibraryAndStreetQuest.ps1
```

That helper builds `CustomNPCAPI.dll` first and supplies it to the existing external builder as a temporary compile-only reference while StreetQuestRPG is compiled.
