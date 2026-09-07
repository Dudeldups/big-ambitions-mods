# Three-band Audi engine and exhaust pops

Revision 10 preserves the original bundled `Car` idle at pitch 1.0 and volume 0.24, independent of throttle. Three held layers generated from the user's `AudiRevving.wav` provide driving audio only. Driving layers fade in over idle + 4% to idle + 22% of limiter RPM (1180–2440 RPM with the current 900/7000 RPM settings). Original Car also remains the native fallback if configuration fails.

The three engine clips derive from different sections of the same recording: 0.32–0.62, 1.50–2.08, and 3.82–4.35 seconds. Spectral averaging and harmonic/noise resynthesis discard the original rise/fall and slow loudness motion. This is a sound-design approximation, not a reconstruction of an authentic RS6-R at measured RPM. It is more processed than the earlier pitch-stabilized excerpt and needs an in-game listening test.

Each two-second clip has a held acoustic reference of 96, 176, or 320 Hz. Playback maps native idle-to-limiter RPM onto a lower shared 80–180 Hz target and divides that target by each clip's reference to calculate pitch. Low/mid/high crossfade with sine/cosine gains. RPM and throttle follow a 0.1-second smoothing time; throttle changes volume. Native mixer/spatial routing is preserved, added engine distortion is disabled, and game pauses stop all loops before restarting them together on resume.

Each driving band now has a separate Load variant with stronger lower partials and soft saturation, rendered offline. These add growl under acceleration while retaining the same playback pitch range and average clip level. Throttle between 15% and 80% blends linearly from the existing coast sample into its Load variant. The original coast clips and Car idle are unchanged. All six driving sources advance together; loaded variants also remain silent at idle. This is designed sound, not measured RS6-R exhaust behaviour.

The original implementation takes inspiration from the inspected Ninja's use of calibrated bands. No Ninja, BMW, or Mercedes code, recordings, or binaries are included. The source input is the user-provided AudiRevving recording; its SHA-256 and processing measurements are in `Config/Audio/generation.json`. The original video URL was not supplied. Exhaust pops are generated from deterministic noise and a decaying low-frequency pulse and do not contain third-party samples.

Pops require a player-controlled, running engine in a forward gear. Load at 2500 RPM or above and 40% throttle marks recent acceleration. While throttle is at most 22% and RPM at least 1800, random overrun pops can occur for up to 2.4 seconds after that load. Each next interval is 0.16 seconds plus an exponential random delay; rate parameters are 2.8/2.2/1.3/0.65/0.35 per second in gears 1/2/3/4/5+. These are sampling parameters, not guaranteed counts. A shift or throttle edge does not directly trigger a pop. The same gear can produce multiple irregular pops, especially in lower gears, and brief shift cuts often produce none. Idle, reverse, sustained acceleration, prolonged coasting, pause, engine-off and exit do not trigger pops. Pausing or leaving the eligible state discards the pending event. Source gain is 0.48 times a random 0.8–1.1 multiplier and game master volume (0.384–0.528, versus 0.80 previously); pitch varies by at most 6%. One-shot playback allows a short tail to finish when another pop occurs. These are test calibrations.

To regenerate (Python with NumPy):

```powershell
python 'Assets/Mods/AudiRS6R/tools~/generate_engine_audio.py' 'E:/Downloads/AudiRevving.wav'
```

The generator writes nine PCM WAVs and the measurement manifest. The engine loops are periodic by construction without a seam fade. Generation rejects clipping and more than 1 dB of 100 ms loudness variation. Coast loops measure approximately 0.24–0.34 dB variation; Load loops measure 0.13–0.37 dB. Unity metadata is tracked separately and must be retained when regenerating.

Run `tools~/Test-Audio.ps1` in a fresh PowerShell process for idle/load/RPM/crossfade invariants, randomized same-gear overrun timing, lower-gear bias, frame-rate sensitivity, state resets, and WAV decoding validation. Its Unity AudioClip stub only checks managed decoding; it does not simulate mixer or DSP behaviour. Runtime C# must also pass the repository's external build and local install command.

Runtime diagnostics identify revision 10, all clip names and reference frequencies. Five-second samples report the original Car idle, idle/driving blend, coast/load blend, six driving-source pitches/volumes, raw/smoothed RPM, throttle, gear, mixer gains, overrun state, and pop count. Pop messages include trigger reason, clip, playback/mute status, spatial range, and engine mixer gain, limited to one message per two seconds. Configuration errors restore native audio and log the failure.

Listening test: restart the game, enter the Audi, idle, accelerate through gears, hold a steady speed, then release the throttle after accelerating above 2500 RPM. Listen for a stronger loaded tone and quieter, irregular pops over the next couple of seconds, particularly in lower gears. Verify pause/resume and exit stop sounds appropriately. Automated checks do not verify subjective tone or actual runtime playback.
