using System;

using ACE.Server.Factories;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests
{
    [TestClass]
    public class MutatorBalanceSimulatorTests
    {
        [TestMethod]
        public void MutatorBalanceReportIncludesCoreWeaponFamilies()
        {
            var report = MutatorBalanceSimulator.Run(
                attacks: 100000,
                baseDamage: 100.0,
                attacksPerSecond: 1.0,
                hitRate: 0.75,
                evadeRate: 0.15,
                armorStopped: 35.0);

            Console.WriteLine(report);

            Assert.IsFalse(string.IsNullOrWhiteSpace(report));
            StringAssert.Contains(report, "Thief dagger");
            StringAssert.Contains(report, "Pugilist flurry");
            StringAssert.Contains(report, "Stonehand hammer");
            StringAssert.Contains(report, "Opportunist");
            StringAssert.Contains(report, "Executioner");
            StringAssert.Contains(report, "Archmagi caster");
            StringAssert.Contains(report, "Recommendations:");
        }
    }
}