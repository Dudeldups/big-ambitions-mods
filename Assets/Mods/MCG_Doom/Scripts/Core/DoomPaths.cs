using System;
using System.IO;
using System.Reflection;

namespace MCG_Doom.Core
{
    internal static class DoomPaths
    {
        private static readonly string[] RelativeCandidates =
        {
            Path.Combine("Config", "Doom", "doom1.wad"),
            Path.Combine("Config", "Doom", "DOOM1.WAD")
        };

        public static string FindBundledSharewareWad()
        {
            var assemblyDirectory = GetAssemblyDirectory();

            foreach (var relativePath in RelativeCandidates)
            {
                var candidate = Path.Combine(assemblyDirectory, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                "Bundled DOOM shareware IWAD was not found under Config/Doom. " +
                "Run tools/PrepareThirdParty.ps1 before building the release package.");
        }

        private static string GetAssemblyDirectory()
        {
            var location = typeof(DoomPaths).Assembly.Location;
            if (string.IsNullOrWhiteSpace(location))
            {
                location = Assembly.GetExecutingAssembly().Location;
            }

            return Path.GetDirectoryName(location) ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
