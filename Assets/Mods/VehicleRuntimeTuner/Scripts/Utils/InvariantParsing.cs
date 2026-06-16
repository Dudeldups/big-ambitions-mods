#nullable enable
using System.Globalization;
using VehicleRuntimeTuner.Profiles;

namespace VehicleRuntimeTuner.Utils
{
    public static class InvariantParsing
    {
        public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static readonly CultureInfo CurrentCulture = CultureInfo.CurrentCulture;

        public static bool TryParseFloat(string? text, out float value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = text.Trim().Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, Culture, out value))
                return true;

            return float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CurrentCulture, out value);
        }

        public static string FormatOptional(OptionalFloat value)
        {
            return value.hasValue ? value.value.ToString(Culture) : string.Empty;
        }

        public static void TryApplyOptionalFloat(string? text, OptionalFloat target)
        {
            if (TryParseFloat(text, out var value))
            {
                target.hasValue = true;
                target.value = value;
            }
            else
            {
                target.hasValue = false;
            }
        }
    }
}
