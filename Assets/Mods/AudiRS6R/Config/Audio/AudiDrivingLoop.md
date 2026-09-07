# Driving loop test

`AudiDrivingLoop.wav` is the prepared loop from the user's `AudiRevving.wav`, section 3.82–4.35 seconds. It is not the audition preview, which deliberately contains silence between the original excerpt and the loop demonstration.

Preparation: mono downmix, relative pitch stabilization against a prominent harmonic (approximately 246.14 Hz), DC removal, 25 ms overlapping loop join, and RMS normalization to approximately -18.42 dBFS. This is relative harmonic analysis, not measured engine RPM. The result contains 22,244 mono samples at 44,100 Hz (approximately 0.504 seconds), exported as 16-bit PCM. Runtime loads it without another seam edit or pitch-flattening pass.

The Car idle asset and its pitch of 1.0 and volume of 0.24 are retained. The driving loop's playback pitch follows normalized native RPM from 0.65 to 2.1. Throttle controls driving volume. This remains a listening-test calibration; a short loop can retain audible texture repetition.

The source recording and the rejected Audi2/Audi3 originals remain in the user's Downloads folder. They are not replaced by this asset.
