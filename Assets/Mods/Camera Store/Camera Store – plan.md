# Camera Store

## Goal

Create a standalone **Camera Store** business-type mod for Big Ambitions.

The mod should behave like a normal vanilla-style retail business and be comparable in scope to the existing Gun Store mod:

- new business type
- up to 8 products
- suitable selling furniture
- normal importer/wholesale integration
- normal storage, shelf, customer and checkout behavior
- mid-tier profitability
- localization
- proper save/reload and mod unload behavior

## Important Architecture Rule

**Camera Store must be completely standalone.**

It must not:

- reference Freelance Photographer
- check whether Freelance Photographer is installed
- contain photography-contract logic
- contain photography progression
- expose addon-specific UI
- require the addon in order to function

The dependency direction is strictly:

```text
Freelance Photographer
        ↓
    Camera Store
```

Never the other way around.

The only thing Camera Store needs to provide for future integrations is a set of stable item IDs.

---

# Mod Identity

## Player-facing

Mod name:

```text
Camera Store
```

Business type:

```text
Camera Store
```

## Suggested technical names

```text
Folder:       Camera Store
Namespace:    CameraStore
Assembly:     CameraStore-BusinessType
ModsLocal:    Camera Store
```

---

# Stable IDs

Choose stable IDs before release and do not casually rename them later.

Suggested IDs:

```text
camerastore:business_camera_store

camerastore:item_compact_camera
camerastore:item_dslr_camera
camerastore:item_professional_camera
camerastore:item_action_camera
camerastore:item_camera_lens
camerastore:item_tripod
camerastore:item_camera_flash
camerastore:item_camera_bag
```

Furniture IDs should follow the same namespace.

Example:

```text
camerastore:item_camera_display
camerastore:item_camera_accessories_shelf
camerastore:item_glass_camera_counter
```

---

# Product Catalog

V1 should contain a maximum of **8 products**.

The goal is a varied but manageable catalog rather than dozens of nearly identical products.

## 1. Compact Camera

Role:

- inexpensive entry-level camera
- relatively high customer demand
- low-to-medium transaction value
- common sale

Visual:

- small point-and-shoot camera
- fictional branding only

Economic role:

```text
Purchase price: low
Retail price:   low-medium
Demand:         high
```

---

## 2. DSLR Camera

Role:

- core mid-range camera
- one of the main revenue products

Visual:

- interchangeable-lens camera body
- visibly larger and more premium than Compact Camera

Economic role:

```text
Purchase price: medium
Retail price:   medium-high
Demand:         medium
```

---

## 3. Professional Camera

Role:

- premium high-ticket product
- occasional large sale
- lower demand

The Professional Camera should improve revenue per customer but must not turn the business into an overpowered money printer.

Economic role:

```text
Purchase price: high
Retail price:   high
Demand:         low-medium
```

---

## 4. Action Camera

Role:

- second compact camera category
- adds variety to the catalog
- medium price and demand

Visual:

- small rugged action-camera design
- no real-world branding

Economic role:

```text
Purchase price: medium
Retail price:   medium
Demand:         medium
```

---

## 5. Camera Lens

Role:

- important accessory
- relatively valuable
- visually different from camera bodies

Economic role:

```text
Purchase price: medium
Retail price:   medium-high
Demand:         medium
```

---

## 6. Tripod

Role:

- larger accessory
- gives shelves more visual variety

Prefer a folded or packaged tripod for retail display rather than a fully deployed tripod.

Economic role:

```text
Purchase price: low-medium
Retail price:   medium
Demand:         medium
```

---

## 7. Camera Flash

Role:

- cheap specialist accessory
- common basket filler

Economic role:

```text
Purchase price: low
Retail price:   low-medium
Demand:         medium-high
```

---

## 8. Camera Bag

Role:

- inexpensive everyday accessory
- relatively common purchase

Economic role:

```text
Purchase price: low
Retail price:   low-medium
Demand:         high
```

---

# Product Roles

The products should not all have identical economic behavior.

## High-frequency products

```text
Compact Camera
Camera Bag
Camera Flash
```

## Core products

```text
DSLR Camera
Action Camera
Camera Lens
Tripod
```

## High-ticket product

```text
Professional Camera
```

This should create customer baskets with meaningful variation.

Examples:

```text
Compact Camera + Camera Bag

DSLR Camera + Lens + Camera Bag

Professional Camera + Lens

Action Camera + Camera Bag
```

---

# Business Balance

## Target

Camera Store should land around the **middle to upper-middle tier** of vanilla retail businesses.

It should be profitable and attractive without becoming one of the strongest businesses in the game.

## Strengths

- compact inventory
- medium-to-high item values
- good revenue per transaction
- attractive premium products
- moderate scaling potential

## Weaknesses

- relatively high inventory investment
- premium cameras have lower demand
- stock mistakes tie up meaningful amounts of cash
- customer volume should not be exceptionally high

## Balance Strategy

Do not finalize values only from spreadsheets.

Test:

1. small store
2. medium store
3. larger store
4. average customer spend
5. product turnover
6. gross margin
7. rent and employee costs
8. logistics costs
9. required inventory capital
10. net weekly profit

If the store is too strong, prefer reducing demand on expensive products before destroying reasonable retail margins.

---

# Furniture

Keep custom furniture limited.

Target:

```text
2 required fixtures
1 optional premium fixture
```

## Camera Display

Primary sales display.

Compatible products could include:

```text
Compact Camera
DSLR Camera
Professional Camera
Action Camera
Camera Lens
Camera Flash
```

Visual direction:

- clean electronics-store style
- glass/premium retail presentation
- several product positions
- readable at normal gameplay zoom

---

## Camera Accessories Shelf

Compatible products:

```text
Camera Lens
Tripod
Camera Flash
Camera Bag
```

Purpose:

- separate accessories from cameras
- create more interesting store layouts
- prevent every product from sharing the same fixture

---

## Optional Glass Camera Counter

Only implement if it adds enough value visually.

Possible products:

```text
DSLR Camera
Professional Camera
Camera Lens
```

It remains a completely normal Camera Store selling fixture.

It must not contain Freelance Photographer-specific interactions.

---

# Vanilla Furniture Reuse

Reuse normal Big Ambitions systems wherever possible.

Do not create custom replacements for:

- checkout counters
- storage
- cleaning
- security
- employee management
- general logistics equipment

The mod should feel like another Big Ambitions business type rather than a separate game built inside Big Ambitions.

---

# Runtime Registration

The mod should follow the normal SDK business-type architecture.

## Initialization responsibilities

- load Camera Store AssetBundle
- load BusinessType asset
- register BusinessType
- register all 8 items
- register shelf/display compatibility
- integrate products into importer/wholesale systems
- validate required assets
- log useful errors

Use the existing proven Big Ambitions modding paths such as:

```text
ModdingAPI.RegisterModBusinessType(...)
ItemsGetter.RegisterModItem(...)
ShelfController.RegisterItemToShow(...)
```

where appropriate to the current SDK implementation.

---

# Unload Responsibilities

Clean up mod-owned runtime registrations where supported.

At minimum ensure that reloads do not create:

- duplicate business registrations
- duplicate products
- duplicate importer entries
- duplicate shelf mappings
- stale event subscriptions

---

# Suggested Project Structure

```text
Assets/Mods/Camera Store/
├─ AssetBundles/
├─ Items/
├─ Locales/
├─ Models/
├─ Prefabs/
├─ Scripts/
│  ├─ CameraStoreMod.cs
│  ├─ CameraStoreIds.cs
│  ├─ CameraStoreBusinessRegistration.cs
│  ├─ CameraStoreItemRegistration.cs
│  ├─ CameraStoreImporterIntegration.cs
│  └─ CameraStoreShelfIntegration.cs
├─ CameraStore.asset
└─ thumbnail.png
```

Keep all public/stable IDs centralized in:

```text
CameraStoreIds.cs
```

---

# Item Registration Requirements

For every product verify:

- correct localized name
- wholesale price
- retail price behavior
- demand
- cargo/storage behavior
- item type/category
- shelf compatibility
- customer purchasing
- prefab
- icon
- scale
- orientation
- material rendering

All products must be genuine normal Big Ambitions retail goods.

---

# Import / Wholesale

The normal business loop must be:

```text
Acquire Stock
    ↓
Warehouse / Storage
    ↓
Store
    ↓
Sales Fixture
    ↓
Customer Purchase
```

Choose whichever importer integration best matches existing project conventions.

Options:

- suitable vanilla importer
- existing modded-products wholesale/import integration

Requirements:

- all products available without Freelance Photographer
- idempotent registration
- reasonable wholesale pricing
- no addon-specific product category

---

# Models and Visuals

Each product should have a clearly recognizable model.

Requirements:

- no real camera manufacturer trademarks
- coherent fictional visual style
- correct HDRP-compatible materials
- sensible polygon count
- sensible texture resolution
- correct scale
- correct shelf orientation
- recognizable silhouettes

Avoid relying on tiny package text to distinguish products.

At normal Big Ambitions camera distance the product category should still be obvious.

---

# Localization

All visible strings should be localization-ready.

Suggested keys:

```text
camerastore:business_name

camerastore:item_compact_camera
camerastore:item_dslr_camera
camerastore:item_professional_camera
camerastore:item_action_camera
camerastore:item_camera_lens
camerastore:item_tripod
camerastore:item_camera_flash
camerastore:item_camera_bag

camerastore:furniture_camera_display
camerastore:furniture_camera_accessories_shelf
camerastore:furniture_glass_camera_counter
```

English is required for initial development.

Additional translations can follow once text stabilizes.

---

# Integration Contract for Freelance Photographer

Camera Store must expose no active addon API unless later proven necessary.

The stable item IDs are enough for V1 integration.

Freelance Photographer can internally map:

```text
camerastore:item_compact_camera
→ photography camera tier 1

camerastore:item_dslr_camera
→ photography camera tier 2

camerastore:item_professional_camera
→ photography camera tier 3
```

This mapping belongs entirely inside Freelance Photographer.

Camera Store must not know that these gameplay tiers exist.

---

# Save Data

Prefer vanilla persistence.

Camera Store should ideally need no custom save record in V1.

Vanilla systems should manage:

- business ownership
- inventory
- shelves
- storage
- product stock
- employees
- customer sales

Only introduce `modData` if a genuinely custom persistent mechanic is added later.

---

# Performance

Avoid permanent scene scans.

Preferred pattern:

```text
Load
→ register
→ retry only while dependency/system unavailable
→ stop after success
```

If internal/reflection-based hooks are unavoidable:

- resolve them once
- cache members
- isolate them in helper classes
- fail softly when possible

Do not run broad object scans every frame.

---

# Error Handling

Log clear errors for:

- missing AssetBundle
- missing BusinessType asset
- missing Item asset
- failed product registration
- failed importer integration
- failed shelf mapping
- missing product prefab
- missing localization where detectable

Core failures should fail closed rather than creating a half-working business.

---

# External Build

Code-only iteration can use the existing external mod build workflow.

Unity remains required when changing:

- prefabs
- ScriptableObjects
- product assets
- BusinessType assets
- models
- materials
- AssetBundles

---

# MVP Definition

Camera Store V1 is complete when:

1. Camera Store registers as a player business.
2. All 8 products are available.
3. All products can be imported/acquired normally.
4. All products can be stored.
5. All products can be displayed.
6. Customers purchase all products correctly.
7. At least two dedicated sales fixtures work.
8. Product visuals are presentable.
9. Business profitability is approximately mid-tier.
10. Save/reload works.
11. Mod reload does not duplicate registrations.
12. Localization is prepared.
13. Workshop packaging is ready.
14. Freelance Photographer is completely absent from Camera Store code.

---

# Testing Checklist

## Registration

- [ ] New save
- [ ] Existing save
- [ ] Business appears once
- [ ] Reload does not duplicate business
- [ ] Reload does not duplicate items
- [ ] Unload/reload behaves correctly

## Products

- [ ] Compact Camera works
- [ ] DSLR Camera works
- [ ] Professional Camera works
- [ ] Action Camera works
- [ ] Camera Lens works
- [ ] Tripod works
- [ ] Camera Flash works
- [ ] Camera Bag works

For each product:

- [ ] Import
- [ ] Storage
- [ ] Shelf placement
- [ ] Correct model
- [ ] Correct icon
- [ ] Correct price
- [ ] Customer purchase
- [ ] Save/reload

## Business

- [ ] Checkout works
- [ ] Employees work normally
- [ ] Customer capacity is sensible
- [ ] Customer demand is sensible
- [ ] BizMan displays correctly
- [ ] Small-store profitability tested
- [ ] Medium-store profitability tested
- [ ] Large-store profitability tested

## Compatibility

- [ ] Camera Store works with Freelance Photographer not installed
- [ ] No missing-addon warning appears
- [ ] Camera Store contains no Freelance Photographer assembly reference

---

# Explicitly Out of Scope

Camera Store V1 must not implement:

- photography missions
- photography contracts
- photography skill
- photography reputation
- Photography Mode
- usable camera mechanics
- screenshot evaluation
- photo quality
- photo rewards
- photography employees
- photography agency systems
- Freelance Photographer detection

Those belong entirely to the addon.

---

# Future Camera Store Scope

Reasonable future additions:

- additional camera/accessory products
- additional store fixtures
- improved models
- localization
- balance adjustments
- visual polish

Any feature involving the player actively working as a photographer belongs in Freelance Photographer instead.