# Custom NPC API — Modder Guide

## 1. What the API is for

Use this library when your mod needs a persistent-looking named NPC in the Big Ambitions world and you do not want to reproduce the reflection-heavy interaction code yourself.

The API is intentionally small:

```text
CustomNpcDefinition       -> what the NPC looks like and where it stands
CustomNpcSpawnOptions     -> interaction callback / custom prefab factory / visibility
CustomNpcApi.Spawn(...)   -> creates the NPC and returns a handle
CustomNpcHandle           -> hide/show, disable interaction, despawn
CustomNpcApi.OpenDialog() -> opens a CallDialogType that your mod registered
CustomNpcPhone            -> vanilla phone-contact helper
```

Your mod still decides *why* an NPC is visible, which dialog is registered, which quest is active, what rewards happen, and when the NPC should move.

---

## 2. SDK setup

Place the library beside your mod:

```text
Assets/Mods/
  Custom NPC API/
  My Mod/
```

Add the `CustomNPCAPI` assembly to your mod's `.asmdef` references. When using GUID references, use the GUID from `Custom NPC API/CustomNPCAPI.asmdef.meta` in the same checkout.

At runtime, distribute/enable `Custom NPC API` as a separate dependency. Do not copy its DLL into every consuming mod unless you intentionally want to fork the API.

---

## 3. Basic NPC definition

```csharp
var npc = new CustomNpcDefinition
{
    Id = "mymod:alex",
    DisplayName = "Alex",
    NameKey = "mymod:alex_name",
    PrefabName = "Characters/Homeless",

    Gender = "Male",
    AgeInDays = 35 * 365,
    AppearanceSeed = 104729,

    Position = new Vector3(301.58f, 0.09f, -188.47f),
    Forward = new Vector3(0f, 0f, -1f),

    LocalPosition = Vector3.zero,
    LocalEulerAngles = new Vector3(0f, 90f, 0f),
    LocalScale = Vector3.one,

    Interactable = true,
    CtaTextKey = "mymod:cta_talk",
    CtaTextFallback = "Talk to {npcname}"
};
```

`Id` must be stable and unique inside your mod. Prefixing it with your mod id is recommended.

### Known vanilla character prefab candidates

The developer tool exposes the prefab candidates that were already proven useful while building StreetQuestRPG:

```text
Characters/Homeless
Characters/Pedestrian
Characters/CasinoCustomer
Characters/CinemaTheaterCustomer
Characters/FullServiceCustomer
Characters/GymCustomer
Characters/HairdresserCustomer
Characters/NightclubCustomer
Characters/NightclubOutsidePedestrian
Characters/SelfServiceCustomer
Characters/StreetPerformer
Characters/CarnivalPedestrian
Characters/WaterPedestrian
Characters/DummyHuman
Characters/DummyAi
Characters/HumanDefinitionLow
```

Not every prefab has identical scripts/poses. Test the exact prefab and appearance you intend to ship.

---

## 4. Placement and facing

`Position` is a world-space position. For reliable placement, stand where the NPC should appear and copy the current player coordinates from a debug tool.

`Forward` should normally be a flat world-space direction (`y = 0`). The API preserves StreetQuestRPG's proven humanoid orientation handling internally, so you can feed it the direction you want to record rather than manually building a Quaternion.

Typical example:

```csharp
Position = new Vector3(301.58f, 0.09f, -188.47f),
Forward  = new Vector3(0f, 0f, -1f)
```

For many humanoid prefabs, this visual correction is a useful starting point:

```csharp
LocalEulerAngles = new Vector3(0f, 90f, 0f)
```

If a specific prefab looks sideways, adjust `LocalEulerAngles.y`, not the world position.

### Scale

Use uniform scale unless you intentionally want distortion:

```csharp
LocalScale = Vector3.one;           // normal adult
LocalScale = Vector3.one * 0.72f;   // smaller/child-sized visual
```

StreetQuest's Julia is an example of a smaller `DummyHuman` appearance driven through scale rather than a separate child model.

---

## 5. Deterministic appearance

For compatible humanoid prefabs, the API calls the game's internal appearance setter with:

```text
Gender + AgeInDays + AppearanceSeed
```

Keeping those three values stable gives the named NPC the same generated appearance after reloads.

```csharp
Gender = "Female",
AgeInDays = 30 * 365,
AppearanceSeed = 32119
```

If the prefab does not expose the expected overload, the API falls back to its normal `SetAppearance()` path.

### Hide unwanted prefab props

Some vanilla character prefabs include child props such as donation buckets or boxes. Remove them by name:

```csharp
HiddenChildObjectNames = new[]
{
    "DonationBucket",
    "DissambledBox"
};
```

---

## 6. Physical interaction

The physical interaction is intentionally callback-based:

```csharp
var handle = CustomNpcApi.Spawn(
    "MyModId",
    definition,
    new CustomNpcSpawnOptions
    {
        OnInteract = context =>
        {
            Debug.Log("Clicked " + context.Definition.Id);
        }
    });
```

The API uses the stable StreetQuest pattern:

1. visible humanoid prefab is only the visual
2. an invisible tiny proxy renderer is used for hover/selection bookkeeping
3. a runtime `SellerStandController` provides the physical Big Ambitions interaction host
4. one shared custom CTA behavior routes the click back to the correct `CustomNpcHandle`

Do **not** replace the proxy with the humanoid renderers. That was the important fix that prevented hover from poisoning normal world clicks.

### CTA text

Preferred:

```csharp
CtaTextKey = "mymod:cta_talk"
```

The localization key can use the replacement `npcname` if your localization path supports it.

Fallback:

```csharp
CtaTextFallback = "Talk to {npcname}"
```

---

## 7. Opening your own dialog

Register your normal Big Ambitions `CallDialogType` in your consuming mod. The API only opens it.

```csharp
var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("mymod_alex_dialog");
CallDialogFactory.RegisterDialog(dialogType, () => new AlexDialog());
```

Then:

```csharp
OnInteract = _ => CustomNpcApi.OpenDialog("mymod_alex_dialog")
```

This same dialog key can also be assigned to the NPC's phone contact, so physical talking and calling reach the same mod-owned conversation logic.

---

## 8. Phone contacts

```csharp
var contact = CustomNpcPhone.EnsureContact(new CustomNpcPhoneDefinition
{
    ContactId = "mymod:alex_contact",
    DescriptionKey = "mymod:contact_group_friends",
    ContactCategory = "General",
    DialogTypeKey = "mymod_alex_dialog"
});
```

This uses the vanilla contact system and assigns `callDialogTypeOverride` from the stable hashed dialog key.

To keep visible phone history:

```csharp
CustomNpcPhone.AppendNpcMessage(contact, "mymod:alex_message_1");
CustomNpcPhone.AppendPlayerMessage(contact, "mymod:alex_reply_1");
```

The helper marks the save as changed after contact/dialog/history mutations.

Important: the API does not decide when the player earns a contact. Your quest/business/story code should call `EnsureContact(...)` when appropriate.

---

## 9. Custom AssetBundle prefabs

`PrefabName` alone uses the game's `PrefabHelper` and is meant for vanilla character prefab names.

For a prefab owned by your mod's AssetBundle, supply `VisualFactory`:

```csharp
var options = new CustomNpcSpawnOptions
{
    VisualFactory = parent =>
    {
        return AssetService.Spawn(
            context.ModId,
            "AssetBundles/mymod.unity3d",
            "Assets/Mods/My Mod/Prefabs/Alex.prefab",
            Vector3.zero,
            Quaternion.identity,
            parent,
            false,
            true,
            true);
    },
    OnInteract = _ => CustomNpcApi.OpenDialog("mymod_alex_dialog")
};

var handle = CustomNpcApi.Spawn(context.ModId, definition, options);
```

This keeps AssetBundle ownership in the consuming mod. Custom NPC API never assumes another mod's bundle key or source path.

---

## 10. Visibility and despawning

For a simple NPC:

```csharp
handle.SetVisible(false);
handle.SetVisible(true);
```

To disable only talking/clicking while leaving the visual under your own movement/presentation control:

```csharp
handle.SetInteractionEnabled(false);
```

This split is useful for schedule handoffs or walk-away animations.

When done:

```csharp
handle.Dispose();
```

Or clean every NPC created by your mod:

```csharp
CustomNpcApi.DespawnAll(context.ModId);
```

---

## 11. Developer preview interface

The library contains a small runtime developer window, but it is intentionally disabled by default so normal players do not get a modding UI.

Enable it from a development build/mod entry:

```csharp
CustomNpcApi.DeveloperToolsEnabled = true;
```

Then press:

```text
F7
```

The window provides:

- currently active Custom NPC API NPCs, grouped by owner mod id
- a `Go` button to move the player to an active NPC
- live player position and forward direction
- `Capture player transform` so you can mark the exact NPC location, walk away, and preview it without standing inside the model
- `Copy player JSON` for quick coordinate/forward capture
- vanilla character prefab selection
- gender, age-in-days, appearance seed, uniform scale and local yaw controls
- `Spawn / Refresh` at the captured placement (or 2.5m in front of the player when no placement is captured)
- `Despawn`
- `Copy NPC JSON` for a paste-ready block containing prefab, appearance, position, forward, local rotation and scale

Recommended workflow:

1. enable developer tools
2. stand exactly where the NPC should be placed
3. press F7 and choose `Capture player transform`
4. walk a few meters away so you can see the target spot
5. choose a prefab and tune gender / age / seed / scale / local yaw
6. use `Spawn / Refresh` until the NPC looks right
7. use `Copy NPC JSON`
8. paste the values into your own config/C# definition
9. disable `DeveloperToolsEnabled` before release

The developer window is a convenience tool, not part of save data and not required for NPCs at runtime.

---

## 12. What remains outside the API

These StreetQuest systems are deliberately **not** part of Custom NPC API:

- quest catalogs and objective types
- story flags / favor
- character `states`
- daily/building schedules
- walk-in / walk-away waypoint state machines
- ambient speech bubbles
- city-map People markers and filters
- shared-shell apartment routing
- custom quest inventory

A consuming mod can implement any of those systems and simply feed the resolved final position/appearance into `CustomNpcApi.Spawn(...)`.

That boundary is intentional: it keeps the reusable dependency small and makes updates to fragile Big Ambitions interaction internals happen in one place.
