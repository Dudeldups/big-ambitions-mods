using System;

namespace MCG_Doom.Compatibility
{
    internal static class ManagedDoomNet472Compat
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        public static float Round(float value)
        {
            return (float)Math.Round(value);
        }
    }
}

namespace System.Collections.Generic
{
    internal static class ManagedDoomDictionaryCompatExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }
    }
}

namespace System.IO
{
    internal static class ManagedDoomStreamCompatExtensions
    {
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }
    }
}
