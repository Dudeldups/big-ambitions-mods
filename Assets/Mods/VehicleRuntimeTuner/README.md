# Vehicle Runtime Tuner

Runtime tuning/debug mod for Big Ambitions vehicles.

This tool edits live vehicle instances and related runtime objects while the game is running.
It also includes a Unity editor apply tool that can write a saved tuner profile back into the prefab and vehicle asset.

## Hotkeys

- `F10`: toggle overlay
- `F9`: apply current profile
- `F8`: dump runtime data for the active vehicle
- `F7`: save profile JSON
- `F6`: load profile JSON for the active vehicle
- Overlay debug buttons: respawn a test copy, snap the active vehicle to ground, reset rigidbody velocity

## Output folders

Files are written under:

`Application.persistentDataPath/VehicleRuntimeTuner/`

Subfolders:

- `Profiles/`
- `Dumps/`
- `Exports/`
- `vehicle-runtime-tuner.log`

## Intended workflow

1. Start the game with the tuner enabled.
2. Enter a vehicle.
3. Open the overlay with `F10`.
4. Adjust values live.
5. Save the profile JSON.
6. In Unity open `Big Ambitions/Vehicle Runtime Tuner/Apply Saved Profile`.
7. Apply the saved profile back to the prefab and vehicle asset.

## Known limitations

- Runtime changes are not written back automatically at the exact moment you edit them in-game.
- Use the included Unity editor apply window to push the saved JSON profile back into the prefab and vehicle `.asset`.
- Some values only affect live handling after respawn/reinitialization.
- Prefab wiring problems still need Unity-side fixes.
- Material/body mesh renderer assignments are not solved by this tool.
- Reflection mappings may need updates after game updates.
