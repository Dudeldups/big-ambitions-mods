# Audi audio test: recorded passages and event-driven pops

Revision 11 preserves the original Car idle at pitch 1.0 and volume 0.24. Its asset, processing and idle/driving fade thresholds are unchanged. The new local audition path uses the two user-supplied RS6_pitch_loops passage sets directly, without the synthesized growl or further filtering. Passage 1 is selected on configuration. Left Ctrl + Left Alt + A switches passages while driving, with a short crossfade; idle is unaffected.

Each passage contains separate EngineLow/Mid/High excerpts. The supplied notes identify extraction windows 182.12–183.90 seconds for passage 1 and 460.42–461.54 seconds for passage 2 of https://www.youtube.com/watch?v=bfumTAM-aNM (thierry vigneau Boiserie). Original engine RPM is unknown. Harmonic-comb estimates are 25.3/32.5/50.9 Hz for passage 1 and 52.7/55.7/45.9 Hz for passage 2. The latter is not a clean rising sequence, so passage 1 is the initial comparison. These estimates are calibration aids, not proven fundamentals or physical RPM. Playback targets rise with native RPM over 26–65 Hz and 45–85 Hz respectively. Source RMS is approximately 0.16; a 0.75 gain adjustment retains the existing 0.12 reference level. Short excerpts may retain rev motion or background sounds, so listening remains necessary.

The supplied provenance notes do not establish redistribution permission. The six new recordings and their README/extraction notes are copied only to the user's ModsLocal/AudiRS6R/Config/Audio/Recorded folder. They are not in Git or the Workshop package. After building/installing the mod, run tools~/Install-RecordedAudio.ps1 -SourceRoot E:/Downloads/RS6_pitch_loops for this local audition. The script verifies copy hashes. A complete bank is required; if it is absent the code logs a warning and uses the existing packaged driving samples. Invalid audio restores native Car playback and logs an error. Runtime-created clips are owned and cleaned up by the controller; the original Car clip remains borrowed.

## Pop events

Pops now use driver throttle (physics.input.Throttle), which excludes automatic engine throttle cuts during upshifts. There is no continuous randomized overrun timer.

- Throttle at 45% or above for at least 0.12 seconds arms a lift event. Crossing down through 20% consumes that arm. A 0.22-second history measures drop size and release speed; slow easing produces little or no probability. Rearming requires meaningful throttle again, even if the previous lift was silent.
- RPM probability rises smoothly from zero near 1400 RPM to its maximum at 5500 RPM. Gears 1/2/3 use factors 1/0.95/0.8; fourth uses 0.35. Higher gears use 0.06, rising toward 0.36 only at exceptionally high RPM (5700–7000).
- A forward downshift records pre-shift RPM and observes the next 0.2 seconds for an RPM rise. Its chance/intensity increases smoothly with a 200–1600 RPM jump, engine RPM and vehicle speed. An upshift or a downshift without an RPM rise cannot qualify. A high-RPM kickdown may qualify while the accelerator remains pressed.
- A successful lift schedules 1–3 pops; a downshift schedules 1–2. Strength influences count and volume. First-pop delay is 35–100 ms; subsequent delays are 85–205 ms. A 1.0–1.35 second cooldown prevents overlapping events. No new burst comes from merely continuing to coast.
- Idle, reverse, engine-off, exit and pause clear pending events. A stale sampling gap also resets detection. Reapplying throttle cancels a remaining lift tail. Pop volume retains the quieter 0.48 base multiplied by event intensity and a random 0.8–1.1 variation; pitch varies by 6%. One-shot playback allows individual tails to finish.

These are responsive sound-design rules, not a physical combustion simulation. Logs report revision 11, passage selection, input and engine throttle, band playback, burst decision/probability, RPM jump, burst size and individual playback. Decision logs are limited to two per second; individual pop logs to one every two seconds. Five-second state samples include total pop count.

## Verification

Run tools~/Test-Audio.ps1 -RecordingsRoot E:/Downloads/RS6_pitch_loops in a fresh PowerShell process. It checks idle calibration, packaged and recorded pitch alignment, all fifteen decoded WAVs, and deterministic scenario tests for abrupt/slow lifts, idle, high-gear rarity, downshift RPM rise (including delayed rise and kickdown), no-rise shifts, speed, burst limits, zero-throttle retriggering, cooldown, rearming, pause/exit/reverse and frame-rate consistency. The Unity clip stub tests decoding only; it does not simulate DSP/mixer behaviour.

The packaged fallback generator and its existing source provenance remain documented in generate_engine_audio.py and Config/Audio/generation.json. Local audition clips are copied byte-for-byte from the supplied folder. Build/install, automated checks and hash verification do not establish actual in-game sound quality.
