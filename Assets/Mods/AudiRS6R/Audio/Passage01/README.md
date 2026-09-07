# RS6 passage_01 engine audio

User-selected local test audio from the previously prepared `RS6_pitch_loops/passage_01` set. The three WAV files are copied unchanged; extraction windows and provenance are recorded in `extraction.json`.

Source: thierry vigneau Boiserie, "Une RS6 de 700CV ca DRIFT ???", https://www.youtube.com/watch?v=bfumTAM-aNM. No free-reuse license has been verified. Local integration does not establish redistribution rights.

The clips are mono PCM16 at 44.1 kHz, approximately 0.375 seconds each. Low/mid/high describe relative stages of this acceleration; Low is not a measured idle recording. Runtime blends adjacent loops at equal power and applies a modest pitch curve, with no claim of measured RPM calibration.

Run `tools~/BuildPassageAudio.ps1` from this mod to build and validate dedicated Windows and Mac audio bundles in an isolated Unity 2022.3.62f2 project. Then run the repository's `tools/external-build/BuildBigAmbitionsMods.ps1 -ModName AudiRS6R -Install`.

Runtime validation still required: idle, acceleration through gears, throttle release, stopping and exiting, pause/resume, vehicle volume, save reload and mod unload. The per-vehicle `passage_01 engine audio ready` log confirms source initialization, not listening quality.
