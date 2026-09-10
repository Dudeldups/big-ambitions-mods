#nullable enable

internal static class CadillacEscaladeAudioModel
{
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;

    // Keep the native engine recording in the relaxed full-size-SUV range.
    // The donor's wide pitch sweep and distortion made the upper rev range
    // sound synthetic; these values retain load response without that effect.
    internal const float NativeBaseVolume = .12f;
    internal const float NativeMaxDistortion = .07f;
    internal const float NativePitchOffset = .34f;
    internal const float NativePitchRange = .90f;
    internal const float NativeVolumeRange = .05f;
}
