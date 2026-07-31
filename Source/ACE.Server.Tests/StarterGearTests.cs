using System;
using System.IO;

using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Common;
using ACE.Server.Entity;

namespace ACE.Server.Tests
{
    [TestClass]
    public class StarterGearTests
    {
        [TestMethod]
        public void CanParseStarterGearJson()
        {
            var starterGearPath = Path.Combine(TestPaths.FindServerDirectory(), "starterGear.json");
            string contents = File.ReadAllText(starterGearPath);

            StarterGearConfiguration config = JsonSerializer.Deserialize<StarterGearConfiguration>(contents, ConfigManager.SerializerOptions);
        }
    }
}
