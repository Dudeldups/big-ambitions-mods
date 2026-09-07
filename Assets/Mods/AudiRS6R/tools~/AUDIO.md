# Audi audio test: generated growl and reactive pops

Revision 14 changes pop timbre and narrows its random pitch range. The engine sound was accepted after revision 12 and pop frequency after revision 13. All six driving WAVs, the original Car idle, engine playback settings and the pop event detector/scheduler are unchanged in this iteration. Idle remains the borrowed Car clip at pitch 1.0 and volume 0.24, with the existing idle/driving fade and no added distortion. Old local Recorded audition files are ignored.

## Pop sound character

The previous generator used nearly unfiltered noise and a sub-millisecond attack. Its samples contained 36-41% spectral energy above 2 kHz and 27-32% of their total energy in the opening 5 ms, consistent with the reported sharp wooden clack.

The new deterministic synthesis combines a filtered pressure/noise impact with noisy gas flow and short damped exhaust modes. Their main body frequencies are 145, 130 and 115 Hz, with additional non-integer overtones. A 4 ms attack softens the opening. Body decay is 34-42 ms, giving the deeper variants a slightly longer resonant tail. Mild saturation adds density; a final 1.6-1.8 kHz low-pass also removes harmonics created by saturation. Filtering happens offline in the pop generator, not on the shared engine mixer. Clips retain their original 0.20/0.225/0.25-second lengths and each prior variant's RMS energy rather than normalizing to its old high transient peak.

Measurements of the encoded WAVs: peaks 0.272-0.344 instead of 0.72, 81-92% energy between 70 and 700 Hz, under 0.002% above 2 kHz even at maximum random playback pitch, and 3-9% of total energy in the first 5 ms. About 0.5-1% remains after 100 ms. Endpoints fade to silence and DC is removed without introducing a boundary step. These measurements establish the intended spectral/envelope change, not subjective in-game realism.

Random clip and volume selection are retained. Pop pitch is now 0.96-1.01x, replacing 0.94-1.06x, to avoid brighter outliers without dramatically lowering every sample. Pop volume remains 0.48 times event intensity and random 0.8-1.1 variation. One-shot playback retains individual tails. No new runtime filter or distortion component is added.

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

Logs identify revision=14, popTone=filteredExhaustBody and popPitch=0.96..1.01. Existing rate-limited diagnostics report event reason, probability, pre-shift RPM/source gear, selected clip, playback volume/pitch and mixer state.

## Verification and listening

Run tools~/Test-Audio.ps1 in a fresh PowerShell process for idle/driving calibration, 1001 RPM crossfades, nine WAV decodes and event scenarios. Run python tools~/test_pop_audio.py (NumPy required) for encoded pop spectral balance at both playback pitch limits, short tail, softened opening, level/headroom, distinct variants and silent boundaries. The tests do not simulate Unity DSP or the in-game mixer.

Restart Big Ambitions after installation. Listen to individual releases and short bursts from loaded low-gear upshifts: the target is BOP/BOFF with a short body instead of CLACK. Compare larger and smaller variants while checking that the accepted frequency, engine and idle remain intact. Build/install and numerical checks do not establish in-game sound quality.
