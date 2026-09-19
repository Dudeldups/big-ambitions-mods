# Monza Test Track

First runtime prototype for a large vehicle test circuit in Big Ambitions.

## Prototype behavior

- Loads on `ModEntryOnCityLoad`.
- Reconstructs the largest connected driveable asphalt surface extracted from the supplied Monza 1998 GLB.
- Places the circuit away from the normal city at approximately `(5000, 80, 0)`.
- Creates a static non-convex `MeshCollider` for the asphalt.
- Creates a large `GroundSafetyBase` below the circuit.
- Does not modify native city terrain or save-map geometry.

## Test control

1. Load a city with the mod enabled.
2. Enter a vehicle.
3. Press **F7** to teleport the current vehicle and driver to the Monza prototype.
4. Drive the circuit.
5. Press **F7** again while still in that vehicle to return to the previous city position.

If the mod unloads while the current vehicle is still at the test site and a return position is known, it attempts to return that vehicle before removing the track.

## Current prototype limits

This version intentionally validates world placement and high-speed vehicle physics first.

- asphalt only; original scenery, buildings, vegetation, curbs and textures are not reconstructed yet
- no pedestrian NavMesh or physical city access
- no map POI
- no lap timing
- no automatic save/load recovery for vehicles left on the circuit
- leaving the vehicle at the isolated site is not part of the supported test flow yet

The next stage can replace the diagnostic runtime surface with a full visual prefab / AssetBundle once placement and vehicle behavior have been validated in-game.
