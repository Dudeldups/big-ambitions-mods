# Volkswagen Amarok

Development branch: `Volkswagen_Amarok/create-vehicle`

This vehicle is generated from the current finished Porsche vehicle architecture while retaining only the reusable Big Ambitions/NWH integrations. Amarok-specific geometry, dimensions, powertrain, suspension, audio, lighting and damage configuration are authored separately.

## Vehicle specification

- Price: $49,900
- Engine: 3.0 V6 TDI, 2967 cm3, 165 kW / 224 PS
- Torque target: 550 Nm with strong low/mid-rpm response
- Drive: permanent 4MOTION AWD
- Transmission: 8-speed automatic
  - 4.714 / 3.143 / 2.106 / 1.667 / 1.285 / 1.000 / 0.839 / 0.667
  - Reverse 3.317
  - Final drive 3.70
- 0-100 km/h target: about 7.9 s
- Top speed: 193 km/h
- Mass: 2078 kg
- Fuel tank: 80 L
- Dimensions: 5.254 x 1.954 x 1.834 m
- Wheelbase: 3.097 m
- Ground clearance reference: 0.192 m
- Tires: 255/60 R18, radius about 0.382 m
- Track: 1.654 m front / 1.658 m rear

## Source assets

Place the supplied source files at:

- `Assets/Mods/Volkswagen_Amarok/Models/2017_volkswagen_amarok_v6.glb`
- `Assets/Mods/Volkswagen_Amarok/Models/VolkswagenAmarokLightOverlays.blend`

The source GLB has separate body, four doors, bonnet, tailgate, front/rear bumpers, glass, V6 TDI engine, interior and four wheel roots. Its tire geometry measures approximately 0.254 m wide and 0.766 m in diameter, which closely matches the specified 255/60 R18 tire.

The Blender light source uses these vertex groups:

- `BHeadlights`
- `BDRL_Indicator_FL`
- `BDRL_Indicator_FR`
- `1RearDrivingLights`
- `1BrakeLights`
- `ThirdBrakeLight`
- `ReverseLights`
- `1IndicatorRL`
- `1IndicatorRR`

`BDRL_Indicator_FL` and `BDRL_Indicator_FR` intentionally feed two functional runtime overlays each: white DRL and amber indicator.

## Generation

From the repository root run:

```powershell
python .\tools\create_volkswagen_amarok.py
```

The script generates the mod source from the current finished vehicle architecture, applies Amarok-specific model/physics/damage integration, creates diesel-specific Low/Mid/High + Load audio and uses Blender to export `AmarokLightOverlays.glb`.

Then open the worktree in Unity and run:

`Big Ambitions Mods > Setup Volkswagen Amarok`

The Unity setup creates the VehicleType/prefab from the known-good Audi donor components, strips donor geometry, normalizes the Amarok GLB, connects the four authored wheels to NWH, configures the 8-speed AWD powertrain, creates the per-instance damage setup and prepares the asset bundle.

## Integrations

The runtime keeps the proven vehicle integrations used by the finished vehicle mods:

- both luxury dealers (`The Hamptons Axis` and `Manhattan Luxury Cars`)
- Private Driver support
- save/load recovery
- repair guard and per-instance visual damage meshes
- repaint-compatible body materials
- runtime light overlays

The Amarok itself is configured as a normal truck rather than a luxury vehicle despite being stocked at the two luxury dealers.

The cargo bed remains visually intact. No arbitrary gameplay cargo capacity is assigned; that is left disabled until a verified vanilla pickup/van cargo path is wired.
