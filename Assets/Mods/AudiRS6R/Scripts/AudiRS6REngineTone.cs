using UnityEngine;

public static class AudiRS6REngineTone
{
    // One continuous sample playback clock, with a low idle and a restrained
    // upper range. Artistic mapping: no measured RPM is available for this clip.
    public static float Pitch(float normalizedRpm)
        => .65f * Mathf.Pow(1.85f / .65f, Mathf.Clamp01(normalizedRpm));
}
