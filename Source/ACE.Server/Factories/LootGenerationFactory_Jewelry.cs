using ACE.Common;
using ACE.Database.Models.World;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        /// <summary>
        /// This is only called by /testlootgen command
        /// The actual lootgen system doesn't use this.
        /// </summary>
        private static WorldObject CreateJewelry(TreasureDeath profile, bool isMagical)
        {
            var treasureRoll = new TreasureRoll(TreasureItemType.Jewelry);
            treasureRoll.Wcid = JewelryWcids.Roll(profile.Tier);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);

            MutateJewelry(wo, profile, isMagical, treasureRoll);

            return wo;
        }

        private static void MutateJewelry(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
            // material type
            var materialType = GetMaterialType(wo, profile.Tier);
            if (materialType > 0)
                wo.MaterialType = materialType;

            // item color
            MutateColor(wo);

            // gem count / gem material
            if (wo.GemCode != null)
                wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
            else
                wo.GemCount = ThreadSafeRandom.Next(1, 5);

            wo.GemType = RollGemType(profile.Tier);

            // workmanship
            wo.ItemWorkmanship = WorkmanshipChance.Roll(profile.Tier);

            // wield level requirement for t7+
            if (profile.Tier > 6)
                RollWieldLevelReq_T7_T8(wo, profile);

            // assign magic
            if (isMagical)
                AssignMagic(wo, profile, roll);
            else
            {
                wo.ItemManaCost = null;
                wo.ItemMaxMana = null;
                wo.ItemCurMana = null;
                wo.ItemSpellcraft = null;
                wo.ItemDifficulty = null;
                wo.ManaRate = null;
            }

            // gear rating (t8)
            if (profile.Tier == 8)
                TryMutateGearRating(wo, profile, roll);

            // Vampiric Jewelry affix: on-hit chance to restore vitals with diminishing returns across stacked pieces.
            // Rolls one of three flavors: Health (vampiric), Stamina (leech), Mana (siphon). See @lootconfig.
            var rolledVampiric = false;
            var vampPts = 0;
            var vampVitalRoll = 0;
            string vampVitalLabel = null;
            if (profile.Tier >= ACE.Server.Managers.DerpACEConfig.VampiricJewelryMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.VampiricJewelryDropChance)
            {
                vampPts = ThreadSafeRandom.Next(
                    ACE.Server.Managers.DerpACEConfig.VampiricJewelryPointsMin,
                    ACE.Server.Managers.DerpACEConfig.VampiricJewelryPointsMax);
                if (vampPts < 1) vampPts = 1;

                // 0 = Health (vampire), 1 = Stamina (leech), 2 = Mana (siphon)
                vampVitalRoll = ThreadSafeRandom.Next(0, 2);

                string suffix;
                ACE.Entity.Enum.UiEffects uiEffect;
                switch (vampVitalRoll)
                {
                    case 1:
                        vampVitalLabel = "stamina";
                        suffix = " of the Leech";
                        uiEffect = ACE.Entity.Enum.UiEffects.BoostStamina;
                        break;
                    case 2:
                        vampVitalLabel = "mana";
                        suffix = " of the Siphon";
                        uiEffect = ACE.Entity.Enum.UiEffects.BoostMana;
                        break;
                    default:
                        vampVitalLabel = "health";
                        suffix = " of the Vampire";
                        uiEffect = ACE.Entity.Enum.UiEffects.BoostHealth;
                        break;
                }

                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsVampiricJewelry, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.VampiricJewelryPoints, vampPts);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.VampiricJewelryVital, vampVitalRoll);
                wo.UiEffects = uiEffect;
                wo.Name = wo.Name + suffix;
                rolledVampiric = true;
            }

            // item value
            //  if (wo.HasMutateFilter(MutateFilter.Value))     // fixme: data
                MutateValue(wo, profile.Tier, roll);

            wo.LongDesc = GetLongDesc(wo);

            // Append the Vampiric jewelry description AFTER GetLongDesc so it survives the spell-name overwrite.
            if (rolledVampiric)
            {
                var procChancePct = (int)System.Math.Round(ACE.Server.Managers.DerpACEConfig.VampiricJewelryOnHitProcChance * 100.0);
                var burst = (int)System.Math.Round(vampPts * ACE.Server.Managers.DerpACEConfig.VampiricJewelryOnHitMultiplier);
                if (burst < 1) burst = 1;

                // Build a per-piece-count diminishing returns breakdown.
                var dr = ACE.Server.Managers.DerpACEConfig.VampiricJewelryDiminishingReturns;
                string drNote;
                if (dr == null || dr.Length <= 1)
                {
                    drNote = $"Stacking multiple pieces does not reduce the per-piece proc.";
                }
                else
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (var i = 1; i < dr.Length; i++)
                    {
                        var scaled = (int)System.Math.Round(burst * dr[i]);
                        if (scaled < 1) scaled = 1;
                        parts.Add($"{i}pc: {scaled}");
                    }
                    drNote = "Diminishing returns when stacking " + string.Join(", ", parts) + " " + vampVitalLabel + " per proc.";
                }

                wo.LongDesc = (wo.LongDesc ?? wo.Name)
                    + $"\n\nVampiric ({vampVitalLabel}): Each strike has a {procChancePct}% chance to drain {burst} {vampVitalLabel} from your target. "
                    + drNote;

                wo.Inscription = $"On hit: {procChancePct}% chance to drain {burst} {vampVitalLabel}.\n" + drNote;
                wo.ScribeName = "M.S.";
            }
        }
    }
}
