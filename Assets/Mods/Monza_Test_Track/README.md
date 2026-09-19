# Monza Test Track

Large vehicle test circuit prototype for Big Ambitions.

## Runtime behavior

- Loads on `ModEntryOnCityLoad`.
- Prefers a full Unity AssetBundle built from the supplied Monza 1998 GLB.
- Places the facility around `(0, 600, 0)`: X/Z remain inside the normal Big Ambitions traffic grid while altitude separates the test track from the city.
- Uses static non-convex `MeshCollider` road geometry.
- Adds an invisible `GroundSafetyBase` below the circuit.
- Does not modify native city terrain or save-map geometry.

If the full AssetBundle is unavailable, the mod falls back to the earlier diagnostic asphalt-only runtime mesh.

## Full visual AssetBundle

The original GLB is intentionally not committed to this repository. It is CC BY 4.0 and is consumed locally by a deterministic Unity build step.

The helper script:

`tools/BuildMonzaTestTrackAssets.ps1`

does the following:

1. Finds the newest `monza_circuit_1998_layout*.glb` in the current Windows user's Downloads folder, unless `-SourceGlb` is supplied.
2. Copies it locally to `Assets/Mods/Monza_Test_Track/Models/monza_circuit_1998_layout.glb`.
3. Starts Unity 2022.3.62f2 in batch mode.
4. Imports the GLB through the SDK's installed glTFast importer.
5. Builds `Prefabs/MonzaTestTrack.prefab`.
6. Identifies the source asphalt meshes using material `logoansa_95` and the known source mesh/object names.
7. Adds static road MeshColliders and an invisible safety base.
8. Creates a `SpawnPoint` on the road.
9. Builds and verifies:
   `Assets/Mods/Monza_Test_Track/AssetBundles/Windows/monzatesttrack.unity3d`.

The normal external build then copies that bundle to ModsLocal.

## Test control

1. Load a city with the mod enabled.
2. Enter a vehicle.
3. Press **F7** to teleport the current vehicle and driver to Monza.
4. Drive the circuit.
5. Press **F7** again to return to the previous city position.

If the mod unloads while the current vehicle is still at the test site and a return position is known, it attempts to return that vehicle before removing the track.

## Current prototype limits

- no pedestrian NavMesh or physical city access
- no map POI
- no lap timing
- no automatic save/load recovery for vehicles left on the circuit
- leaving the vehicle at the elevated test site is not part of the supported test flow yet
- the first full-visual build uses source GLB materials as imported by glTFast; material tuning can follow after the first in-game visual validation

## Source attribution

See `ATTRIBUTION.md`.
