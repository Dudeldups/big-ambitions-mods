# Audi audio test: generated growl and reactive pops

Revision 12 restores the packaged, stationary driving layers used before the two recorded-passage auditions. The original Car idle remains at pitch 1.0 and volume 0.24; its asset, processing and idle/driving fade thresholds are unchanged. The audition loader and Ctrl+Alt+A shortcut have been removed. Any old Config/Audio/Recorded files in ModsLocal are ignored.

## Driving tone

Three coast layers and three matched loaded layers crossfade with native RPM and throttle. Acoustic reference frequencies are 96/176/320 Hz; the playback target remains 80–180 Hz across normalized engine RPM. These are sound calibration values, not measured physical RPM. Loaded variants add stronger lower and odd harmonics plus soft saturation. Their harmonic rolloff is wider and a parallel base component retains upper-mid detail. This changes timbre without lowering the overall pitch further. Coast samples and all three pop samples are byte-identical to revision 11. Idle never passes through the generator.

The generator uses selected sections of the previously supplied AudiRevving.wav, averages their spectrum and reconstructs periodic harmonics plus stationary noise. It does not loop a rising/falling rev sequence. See generate_engine_audio.py and Config/Audio/generation.json for source hash, analysis windows and reproduction details. All six held layers retain RMS 0.12. The new loaded peaks are below 0.20, with less than 0.39 dB variation between 100 ms RMS windows. Spectral checks show increased upper harmonic energy in every loaded layer; perceived growl and brightness still need in-game listening.

## Pop events

Pops use driver throttle (physics.input.Throttle), which excludes automatic engine throttle cuts during upshifts. There is no continuous randomized overrun timer.

- Throttle at 45% or above for at least 0.12 seconds arms a lift event. Crossing down through 20% consumes that arm. A 0.22-second history measures drop size and release speed; slow easing produces little or no probability. Rearming requires meaningful throttle again, even if the previous lift was silent.
- Lift probability rises smoothly from zero near 1400 RPM to its maximum RPM contribution at 5500 RPM. Full abrupt release uses 0.99 times that contribution times the gear factor. Gears 1/2/3 use factors 1/0.98/0.92. Fourth rises smoothly from 0.40 to 0.80 over 4000–6200 RPM. Higher gears retain 0.06 at moderate RPM, rising smoothly to 0.70 over 4200–6500 RPM. At 5500 RPM, an otherwise qualifying sixth-gear lift now has about a 44% chance instead of 6%; at 6500 it has about 69%. A cooldown or an incomplete/slow lift still reduces the observed event rate.
- Downshift calibration is unchanged. A forward downshift records pre-shift RPM and observes the next 0.2 seconds for an RPM rise. Its chance/intensity increases smoothly with a 200–1600 RPM jump, engine RPM and vehicle speed, with a preference for lower gears. An upshift or a downshift without an RPM rise cannot qualify. A high-RPM kickdown may qualify while the accelerator remains pressed.
- A successful lift schedules 1–3 pops; a downshift schedules 1–2. Strength influences count and volume. First-pop delay is 35–100 ms; subsequent delays are 85–205 ms. A 1.0–1.35 second cooldown prevents overlapping events. No new burst comes from merely continuing to coast.
- Idle, reverse, engine-off, exit and pause clear pending events. A stale sampling gap also resets detection. Reapplying throttle cancels a remaining lift tail. Pop volume retains the 0.48 base multiplied by event intensity and a random 0.8–1.1 variation; pitch varies by 6%. One-shot playback allows individual tails to finish.

Logs report revision 12, generated sample selection, input and engine throttle, band playback, burst decision/probability, RPM jump, burst size and individual playback. Decision logs are limited to two per second; individual pop logs to one every two seconds. Five-second state samples include total pop count.

## Verification and listening test

Run tools~/Test-Audio.ps1 in a fresh PowerShell process. It checks idle calibration, generated pitch alignment over 1001 RPM points, all nine packaged WAVs, and deterministic scenarios for abrupt/slow lifts, idle, high-gear rarity at moderate RPM, stronger high-RPM releases, downshift RPM rise (including delayed rise and kickdown), no-rise shifts, speed, burst limits, zero-throttle retriggering, cooldown, rearming, pause/exit/reverse and frame-rate consistency. The Unity clip stub tests decoding only; it does not simulate DSP/mixer behaviour.

For runtime testing, restart the game to load the installed DLL, confirm the original idle, then compare gentle and full-throttle acceleration. Abruptly release above 5500 RPM in lower and higher gears; compare with gradual release and sustained coasting. Check logs for revision=12 and pop event decisions. Build/install and automated checks do not establish actual in-game sound quality.
