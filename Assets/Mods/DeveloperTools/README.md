# Developer Tools

Developer/testing utility mod for Big Ambitions. The testing window defaults to `F10`; its UI, quick-money, and quick-needs hotkeys can be changed in the normal mod options.

The vanilla and modded vehicle catalogs are shown separately and refresh each time the window opens. The vanilla item catalog is populated lazily and cached. Opening the testing window pauses gameplay and closing it restores the player's previous game-speed state. City-map teleport input only runs while the map is open. Time advancement uses the game's `TimeMachine` at a temporary 6x speed, so normal hourly and daily simulation ticks continue to run.

## Vehicle diagnostics quick start

Vehicle mods do not need diagnostic code or a Developer Tools reference. The recorder reads the active game's standard vehicle, NWH physics, wheel, and damage components.

1. Enter the vehicle to test, open Developer Tools, and select **Start Vehicle Diagnostics** in the fixed bottom bar.
2. Close the window and drive. For a clean acceleration result, stop below 2 km/h, release the throttle briefly, then hold full throttle. The recorder automatically captures speed milestones, RPM, shifts, rev-limiter activity, wheel slip/load, handling forces, fuel, distance, and collisions.
3. Exit the vehicle to finish its segment. The session remains armed, so another vehicle can be entered and tested in the same report.
4. Select **Stop Vehicle Diagnostics** manually when finished.

Reports are written as CSV files to:

`%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\DeveloperTools\VehicleDiagnostics`

Every row includes the vehicle type, localized name, instance ID, segment number, and session time. Useful event rows include `vehicle_started` for the configured vehicle/engine/transmission values, `wheel_configuration`, `acceleration_milestone`, `acceleration_summary`, `gear_changed`, `rev_limiter`, `collision_contact`, `collision_result`, and `vehicle_summary`. A collision result groups related physics contacts into one incident and reports whether saved damage, physics damage, or deformation was actually applied.

For comparisons, repeat the same route and launch procedure for each vehicle in one session, then filter or group the CSV by `vehicle_type` and `segment`. Continuous samples are recorded at 5 Hz only while the player controls a vehicle; recording pauses after exit and stops only when manually disabled.
