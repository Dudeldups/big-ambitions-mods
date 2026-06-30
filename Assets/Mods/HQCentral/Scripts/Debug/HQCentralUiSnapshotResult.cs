#nullable enable

namespace HQCentral.Debugging
{
    internal readonly struct HQCentralUiSnapshotResult
    {
        public HQCentralUiSnapshotResult(string logPath, int canvasCount, int elementCount)
        {
            LogPath = logPath;
            CanvasCount = canvasCount;
            ElementCount = elementCount;
        }

        public string LogPath { get; }

        public int CanvasCount { get; }

        public int ElementCount { get; }
    }
}
