using System;
using System.IO;

namespace ACE.Server.Tests
{
    internal static class TestPaths
    {
        public static string FindServerDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var sourceServer = Path.Combine(dir.FullName, "Source", "ACE.Server");
                if (Directory.Exists(sourceServer))
                    return sourceServer;

                var siblingServer = Path.Combine(dir.FullName, "ACE.Server");
                if (Directory.Exists(siblingServer))
                    return siblingServer;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate ACE.Server from the test output directory.");
        }
    }
}