# Three-band Audi engine and exhaust pops

Revision 8 uses three held engine layers generated from the user's `AudiRevving.wav`, plus three independently generated exhaust-pop variants. The original bundled `Car` clip remains available as native fallback if configuration fails. The held low layer replaces the original idle during normal player driving.

The three engine clips derive from different sections of the same recording: 0.32–0.62, 1.50–2.08, and 3.82–4.35 seconds. Spectral averaging and harmonic/noise resynthesis discard the original rise/fall and slow loudness motion. This is a sound-design approximation, not a reconstruction of an authentic RS6-R at measured RPM. It is more processed than the earlier pitch-stabilized excerpt and needs an in-game listening test.

Each two-second clip has a held acoustic reference of 96, 176, or 320 Hz. Playback maps native idle-to-limiter RPM onto a shared 96–320 Hz target and divides that target by each clip's reference to calculate pitch. Low/mid/high crossfade with sine/cosine gains. RPM and throttle follow a 0.1-second smoothing time; throttle changes volume. Native mixer/spatial routing is preserved, added engine distortion is disabled, and game pauses stop all loops before restarting them together on resume.

The original implementation takes inspiration from the inspected Ninja's use of calibrated bands. No Ninja, BMW, or Mercedes code, recordings, or binaries are included. The source input is the user-provided AudiRevving recording; its SHA-256 and processing measurements are in `Config/Audio/generation.json`. The original video URL was not supplied. Exhaust pops are generated from deterministic noise and a decaying low-frequency pulse and do not contain third-party samples.

Pops require a player-controlled, running engine in a forward gear. Load above 3000 RPM and 55% throttle arms the gate for 0.65 seconds. A throttle release to 18% or below, or a forward upshift, triggers one pop while RPM remains at least 2200. Cooldown is 0.85 seconds. Idle, reverse, steady throttle, pause, engine-off and exit do not trigger pops. A pause or inactive state clears the gate and discards any playing transient. Pop pitch varies by at most 6%; source gain is 0.38 times game master volume. These are test calibrations.

To regenerate (Python with NumPy):

```powershell
python 'Assets/Mods/AudiRS6R/tools~/generate_engine_audio.py' 'E:/Downloads/AudiRevving.wav'
```

The generator writes six PCM WAVs and the measurement manifest. The engine loops are periodic by construction without a seam fade. Generation rejects clipping and more than 1 dB of 100 ms loudness variation. Current loops measure approximately 0.24–0.34 dB variation. Unity metadata is tracked separately and must be retained when regenerating.

Run `tools~/Test-Audio.ps1` in a fresh PowerShell process for RPM/crossfade invariants, pop state transitions and WAV decoding validation. Its Unity AudioClip stub only checks managed decoding; it does not simulate mixer or DSP behaviour. Runtime C# must also pass the repository's external build and local install command.

Runtime diagnostics identify revision 8, all clip names and reference frequencies. Five-second samples report low/mid/high pitch/volume, raw/smoothed RPM, throttle, gear, mixer gains and pop count. Pop messages include trigger reason and clip, limited to one message per two seconds. Configuration errors restore native audio and log the failure.

Listening test: restart the game, enter the Audi, idle, accelerate through gears, hold a steady speed, then release the throttle above 3000 RPM. Listen for transitions, a held tone without a repeating rev swell, and short pops on lift/upshift. Verify pause/resume and exit stop sounds appropriately. Automated checks do not verify subjective tone or actual runtime playback.
