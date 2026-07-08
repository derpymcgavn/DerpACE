using System;
using System.IO;

using Newtonsoft.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

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

            StarterGearConfiguration config = JsonConvert.DeserializeObject<StarterGearConfiguration>(contents);
        }
    }
}
