#nullable enable
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxUiDebugLogger
    {
        public static void Log(string message)
        {
            // Big Ambitions 1.0 strips System.IO.File.AppendAllText from its player profile.
            // Unity's logger remains available and is safe while this diagnostic is enabled.
            Debug.Log("[BigHax UI] " + message);
        }
    }
}
