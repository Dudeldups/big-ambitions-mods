# Audi audio: finalized growl and reactive pops

Revision 15 sound and pop frequency were accepted in game. Final cleanup removes temporary audio diagnostics without changing playback, samples or event scheduling. The engine sound was accepted after revision 12 and pop frequency after revision 13. All six driving WAVs, the original Car idle, engine playback settings and the pop event detector/scheduler are unchanged in this iteration. Idle remains the borrowed Car clip at pitch 1.0 and volume 0.24, with the existing idle/driving fade and no added distortion. Old local Recorded audition files are ignored.

## Pop sound character

Revision 13 had a sharp noisy attack; revision 14 filtered almost all of its upper spectrum and added damped resonant modes. The user found that result too dull, like knocking on glass. Revision 15 restores the revision 13 noise/attack waveform and removes the added resonant modes.

The new mix is 80% of the original waveform plus 20% of a low-pass copy of that same waveform, each normalized to unit RMS before mixing. Only the quiet parallel copy is filtered at 1100 Hz. Its filter delay is compensated before mixing to keep shared low frequencies in phase. This reinforces the original body while retaining the main waveform's unfiltered attack and crackle. Final normalization preserves each variant's previous RMS energy and duration; endpoint windows prevent boundary clicks.

Encoded samples now retain about 25-29% spectral energy above 2 kHz, compared with 36-41% in revision 13 and almost none in revision 14. Roughly 66-68% remains between 70 and 700 Hz. Opening 5 ms energy is about 25-31%, and peaks are 0.66-0.70. These measurements confirm a return toward the original attack and brightness, while leaving some extra body. The user confirmed this character in game.

Random clip and volume selection are retained. Pop pitch retains revision 14's 0.96-1.01x range, to avoid brighter outliers without dramatically lowering every sample. Pop volume remains 0.48 times event intensity and random 0.8-1.1 variation. One-shot playback retains individual tails. No new runtime filter or distortion component is added.

## Driving tone

Three coast layers and three loaded layers crossfade with native RPM and throttle. Acoustic references are 96/176/320 Hz; the playback target remains 80-180 Hz across normalized RPM. These are calibration values, not measured physical RPM. Loaded variants add lower and odd harmonics, soft saturation and parallel upper-mid detail. All six held layers retain RMS 0.12.

The engine generator analyzes selected sections of the supplied AudiRevving.wav and reconstructs periodic harmonics plus stationary noise. It does not loop an entire rising/falling rev sequence. The pops are separately synthesized, not extracted from that recording. See generate_engine_audio.py and Config/Audio/generation.json for reproduction details and measurements. Existing WAV asset GUIDs are preserved.

## Pop events (unchanged from accepted revision 13)

- Driver throttle at 45% or above for 0.12 seconds arms a release. Crossing down through 20% consumes that arm; a 0.22-second history measures drop size and release speed. Slow easing has little or no chance. Meaningful throttle is required to rearm, even after a silent release.
- Lift probability rises smoothly over 1400-5000 RPM. Gear factors favor 1-3; fourth rises from 0.45 to 0.85 over 3800-6000 RPM, and higher gears from 0.06 to 0.78 over 4200-6400 RPM. Burst intensity retains its separate 1400-5500 RPM calibration.
- Loaded 1-to-2 and 2-to-3 upshifts can trigger 1-2 pops based on pre-shift driver load/RPM and speed. Peak chance is 65% and 55% respectively. Gentle, stationary and higher-gear upshifts remain quiet. A simultaneous throttle release takes priority.
- Downshifts observe the next 0.2 seconds for a measured RPM rise. Chance/intensity depends on a 200-1600 RPM jump, engine RPM, gear and speed. A downshift without an RPM rise cannot qualify.
- Lifts schedule 1-3 pops; shifts schedule 1-2. First delay is 35-100 ms; subsequent delays are 85-205 ms. Lift/upshift cooldown is 0.8-1.05 seconds; downshifts retain 1.0-1.35 seconds. Pending bursts cannot overlap. Holding zero throttle cannot repeatedly trigger releases.
- Idle, reverse, engine-off, exit, pause or a stale sampling gap clear pending events. Reapplying throttle cancels an unfinished lift burst.

Temporary source dumps, driver/pause traces, periodic RPM/mixer samples and per-pop/decision logs have been removed. A single successful initialization message and warnings for initialization failure, missing vehicle physics or playback failure remain for support.

## Verification

Run tools~/Test-Audio.ps1 in a fresh PowerShell process for idle/driving calibration, 1001 RPM crossfades, nine WAV decodes and event scenarios. Run python tools~/test_pop_audio.py (NumPy required) for encoded pop spectral balance at both playback pitch limits, short tail, restored attack without excessive upper clack, level/headroom, distinct variants and silent boundaries. The tests do not simulate Unity DSP or the in-game mixer.

The user confirmed the engine tone, pop frequency and revision 15 pop character in game. Final cleanup changes logging only. Automated audio/event tests and the required external build/install verify the cleanup; they do not independently simulate Unity DSP.

## Horn

The original Audi prefab has an empty native horn clip and a base volume of zero, so the game's H input reaches the vehicle but cannot produce audio. The runtime controller now reads the existing `physics.input.Horn` value and plays a dedicated seamless `Config/Audio/Horn.wav` loop. This respects the game's horn binding rather than checking H directly.

The horn is a one-second, dual-tone 405/510 Hz sound with restrained harmonics and a small periodic diaphragm wobble. Its exact one-second periodic construction and near-zero-slope boundary avoid clicks while held. Playback fades to or from 0.65 times the vehicle master volume over 0.1 seconds. This is about 2.3 dB louder than the initial 0.5 calibration after in-game testing confirmed the tone and behavior but found the horn slightly quiet. It remains available with the engine stopped, but only while the Audi is player-controlled and gameplay is unpaused. The source copies the vehicle's native "other" source spatial/mixer settings when available, with the engine source as a safe fallback.

Initialization logs the selected horn mixer once, and the first recognized horn input per Audi logs that playback started. Failures continue through the existing audio warning/fallback. Run `python tools~/test_horn_audio.py` to validate PCM format, duration, RMS/headroom, dual tones, high-frequency limit and loop boundary. In-game testing must confirm H press/hold/release, position, loudness and mixer behavior.

## Exhaust pop option

The native mod options screen includes "Exhaust pop sounds" under "Audi RS6-R audio", enabled by default. The preference uses the game's native per-mod option key and is loaded when the mod starts, before any settings screen is opened. Native Reset to Defaults restores enabled.

Disabling the option immediately stops existing pop tails and clears queued bursts on every attached Audi controller, including while paused. Disabled playback cannot arm or schedule pop events. Re-enabling starts with fresh event history; it does not replay a previous burst. Engine/idle sources, samples and enabled-state pop calibration are unchanged.

Only option registration and actual value changes are logged; the per-frame and per-pop tuning diagnostics remain removed. Run tools~/Test-Options.ps1 in a fresh PowerShell process for default, persistence/reload, immediate cancellation, disabled suppression, clean re-enable, defaults reset and unload checks. These use API/preferences stubs with the actual option class and event detector; the native menu and audible stop/resume still require an in-game check.
