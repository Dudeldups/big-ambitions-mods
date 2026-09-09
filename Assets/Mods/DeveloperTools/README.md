# Developer Tools

Developer/testing utility mod for Big Ambitions. The testing window defaults to `F10`; its UI, quick-money, and quick-needs hotkeys can be changed in the normal mod options.

The vanilla and modded vehicle catalogs are shown separately and refresh each time the window opens. The vanilla item catalog is populated lazily and cached. Opening the testing window pauses gameplay and closing it restores the player's previous game-speed state. City-map teleport input only runs while the map is open. Time advancement uses the game's `TimeMachine` at a temporary 6x speed, so normal hourly and daily simulation ticks continue to run.
