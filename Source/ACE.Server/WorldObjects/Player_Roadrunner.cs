using System;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        private const double RoadrunnerSettingsRefreshInterval = 5.0;
        private double nextRoadrunnerCheckTime;
        private double nextRoadrunnerSettingsRefreshTime;
        private double roadrunnerCheckInterval = 0.25;
        private bool roadrunnerEnabled;
        private void RoadrunnerTick(double currentUnixTime)
        {
            if (currentUnixTime < nextRoadrunnerCheckTime)
                return;

            if (currentUnixTime >= nextRoadrunnerSettingsRefreshTime)
            {
                roadrunnerEnabled = PropertyManager.GetBool("roadrunner_enabled").Item;
                roadrunnerCheckInterval = Math.Max(0.10, PropertyManager.GetDouble("roadrunner_check_interval").Item);
                nextRoadrunnerSettingsRefreshTime = currentUnixTime + RoadrunnerSettingsRefreshInterval;
            }

            nextRoadrunnerCheckTime = currentUnixTime + roadrunnerCheckInterval;
            var shouldHaveRoadrunner = roadrunnerEnabled && IsOnOutdoorRoad();
            var enchantment = EnchantmentManager.GetEnchantment(CustomSpellManager.RoadrunnerSpellId);
            if (!shouldHaveRoadrunner)
            {
                if (enchantment != null)
                    RemoveRoadrunner(enchantment);
                return;
            }
            if (enchantment != null)
                return;
            var spell = new Spell(CustomSpellManager.RoadrunnerSpellId);
            if (spell.NotFound && (!CustomSpellManager.EnsureRoadrunnerSpellLoaded() || (spell = new Spell(CustomSpellManager.RoadrunnerSpellId)).NotFound))
                return;
            var addResult = EnchantmentManager.Add(spell, this, null);
            if (addResult.Enchantment != null)
            {
                Session?.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(Session, new Enchantment(this, addResult.Enchantment)));
                RefreshRoadrunnerRunSpeed();
            }
        }

        private void RemoveRoadrunner(PropertiesEnchantmentRegistry enchantment)
        {
            EnchantmentManager.Remove(enchantment, false);
            RefreshRoadrunnerRunSpeed();
        }

        private void RefreshRoadrunnerRunSpeed()
        {
            Session?.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, GetCreatureSkill(ACE.Entity.Enum.Skill.Run)));
            HandleRunRateUpdate();
        }
        private bool IsOnOutdoorRoad()
        {
            if (!IsAlive || Teleporting || Location == null || CurrentLandblock == null || CurrentLandblock.IsDungeon)
                return false;
            var physicsLandblock = CurrentLandblock.PhysicsLandblock;
            return physicsLandblock != null && physicsLandblock.OnRoad(Location.Pos);
        }
    }
}
