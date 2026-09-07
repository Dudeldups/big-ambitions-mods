# RS6 passage_01 engine audio

User-selected local test audio from the previously prepared `RS6_pitch_loops/passage_01` set. Original low/mid/high WAV files are retained unchanged as preparation inputs; extraction windows and provenance are recorded in `extraction.json`.

Source: thierry vigneau Boiserie, "Une RS6 de 700CV ca DRIFT ???", https://www.youtube.com/watch?v=bfumTAM-aNM. No free-reuse license has been verified. Local integration does not establish redistribution rights.

The first runtime version crossfaded independent low/mid/high recordings. The user reported an unpleasant overlapping-engine sound in game. The revised runtime plays exactly one `EngineBody.wav` voice derived from EngineHigh: periodic EQ removes subsonic rumble, emphasizes exhaust body around 190 Hz and attenuates frequencies above 1400 Hz. A quiet seam is chosen without mixing in a second recording. The output is mono PCM16 at 44.1 kHz, approximately 0.375 seconds. `body-preparation.json` records hashes and numerical measurements.

Runtime uses one continuous playback clock with pitch 0.65–1.85 driven by smoothed engine RPM, plus smoothed throttle volume. Original engine/fan/transmission whine sources are muted only on this vehicle, and native added engine distortion is disabled. Their prior state is restored on disable/unload. Tire, horn and collision audio remain untouched. This is artistic sound design, not measured RPM or cylinder-count calibration.

Run `python tools~/prepare-body-audio.py` (numpy and soundfile required) to regenerate the body loop. Run `tools~/BuildPassageAudio.ps1` from this mod to build and validate dedicated Windows and Mac audio bundles in an isolated Unity 2022.3.62f2 project. The builder checks that the Windows bundle contains exactly one AudioClip and validates 1001 samples of the production pitch curve. Then run the repository's `tools/external-build/BuildBigAmbitionsMods.ps1 -ModName AudiRS6R -Install`.

Runtime validation still required: idle, acceleration through gears, throttle release, stopping and exiting, pause/resume, vehicle volume, save reload and mod unload. The per-vehicle `passage_01 body-v2 audio ready` log with `voices=1` confirms the revised source initialization, not listening quality.
