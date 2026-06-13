using System;
using System.Linq;
using log4net;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using ACE.Server.Physics;

namespace ACE.Server.WorldObjects
{
    public class Food : Stackable
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// A new biota be created taking all of its values from weenie.
        /// </summary>
        public Food(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        /// <summary>
        /// Restore a WorldObject from the database.
        /// </summary>
        public Food(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            ObjectDescriptionFlags |= ObjectDescriptionFlag.Food;
        }

        /// <summary>
        /// This is raised by Player.HandleActionUseItem.<para />
        /// The item should be in the players possession.
        /// </summary>
        public override void ActOnUse(WorldObject activator)
        {
            if (!(activator is Player player))
                return;

            if (player.IsBusy || player.Teleporting || player.suicideInProgress)
            {
                player.SendWeenieError(WeenieError.YoureTooBusy);
                return;
            }

            if (player.IsJumping)
            {
                player.SendWeenieError(WeenieError.YouCantDoThatWhileInTheAir);
                return;
            }

            var motionCommand = GetUseSound() == Sound.Eat1 ? MotionCommand.Eat : MotionCommand.Drink;

            player.ApplyConsumable(motionCommand, () => ApplyConsumable(player));
        }

        /// <summary>
        /// Applies the boost from the consumable, broadcasts the sound,
        /// sends message to player, and consumes from inventory
        /// </summary>
        public void ApplyConsumable(Player player)
        {
            if (player.IsDead) return;

            // verify item is still valid
            if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null)
            {
                //player.SendWeenieError(WeenieError.ObjectGone);   // results in 'Unable to move object!' transient error
                player.SendTransientError($"Cannot find the {Name}");   // custom message
                return;
            }

            // trying to use a dispel potion while pk timer is active
            // send error message and cancel - do not consume item
            if (SpellDID != null)
            {
                var spell = new Spell(SpellDID.Value);

                if (spell.MetaSpellType == SpellType.Dispel && !VerifyDispelPKStatus(this, player))
                    return;
            }

            if (BoosterEnum != PropertyAttribute2nd.Undef)
            {
                BoostVital(player);
            }

            if (SpellDID != null)
            {
                CastSpell(player);
            }

            var soundEvent = new GameMessageSound(player.Guid, GetUseSound(), 1.0f);
            player.EnqueueBroadcast(soundEvent);

            if (!UnlimitedUse)
                player.TryConsumeFromInventoryWithNetworking(this, 1);

            // Easter egg: eat 3 cheese wheels → involuntary consequences.
            TryCheeseWheelEasterEgg(player);
            TryCookingGlovesWellFed(player);
        }

        private const int WellFedRequiredMeals = 10;
        internal const double WellFedDurationSeconds = 7200.0;
        internal const int WellFedCooldownId = 2058;
        // Active spell display is client-DAT driven. Use a client-known all-attribute
        // spell so the +5 Well Fed effect appears in the retail spell panel.
        private const uint WellFedSpell = (uint)SpellId.SetSocietyAttributeAll1;

        private void TryCookingGlovesWellFed(Player player)
        {
            var cookingGloves = GetCookingGlovesEquipped(player);
            if (cookingGloves == null)
            {
                player.RemoveProperty(PropertyInt.WellFedFoodCount);
                return;
            }

            cookingGloves.CooldownId = WellFedCooldownId;
            cookingGloves.CooldownDuration = WellFedDurationSeconds;

            var cooldownRemaining = GetWellFedCooldownRemaining(cookingGloves);
            if (cooldownRemaining > 0)
            {
                EnsureWellFedCooldownVisible(player, cookingGloves, cooldownRemaining);
                return;
            }

            var count = player.GetProperty(PropertyInt.WellFedFoodCount) ?? 0;
            count++;

            if (count < WellFedRequiredMeals)
            {
                player.SetProperty(PropertyInt.WellFedFoodCount, count);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"Your cooking gloves warm slightly. {WellFedRequiredMeals - count} more meal{(WellFedRequiredMeals - count == 1 ? "" : "s")} until Well Fed.",
                    ChatMessageType.System));
                return;
            }

            player.RemoveProperty(PropertyInt.WellFedFoodCount);

            var spell = new Spell(WellFedSpell);
            if (spell.NotFound)
            {
                player.SetProperty(PropertyInt.WellFedFoodCount, WellFedRequiredMeals - 1);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"Well Fed display spell {WellFedSpell} is unavailable.",
                    ChatMessageType.System));
                return;
            }

            var addResult = player.EnchantmentManager.Add(spell, player, cookingGloves);
            if (addResult.Enchantment != null)
            {
                addResult.Enchantment.Duration = WellFedDurationSeconds;
                addResult.Enchantment.StartTime = 0;
                player.ChangesDetected = true;
                player.Session.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, addResult.Enchantment)));
                StartWellFedCooldown(player, cookingGloves, WellFedDurationSeconds);
            }

            player.ApplyVisualEffects(PlayScript.EnchantUpYellow);
            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                "You feel properly Well Fed. All primary attributes are increased by 5 for 2 hours.",
                ChatMessageType.System));
        }

        private static WorldObject GetCookingGlovesEquipped(Player player)
        {
            return player.EquippedObjects.Values.FirstOrDefault(item =>
                item?.GetProperty(PropertyBool.IsCookingGloves) == true
                && ((EquipMask)(item.CurrentWieldedLocation ?? 0)).HasFlag(EquipMask.HandWear));
        }

        internal static bool IsCookingGloves(WorldObject item)
        {
            return item?.GetProperty(PropertyBool.IsCookingGloves) == true;
        }

        internal static void RemoveWellFedFromGloves(Player player, WorldObject gloves)
        {
            if (player == null || gloves == null)
                return;

            var enchantment = player.EnchantmentManager.GetEnchantment(WellFedSpell, gloves.Guid.Full);
            if (enchantment != null)
                player.EnchantmentManager.Remove(enchantment);

            player.RemoveProperty(PropertyInt.WellFedFoodCount);
        }

        private static void StartWellFedCooldown(Player player, WorldObject gloves, double duration)
        {
            gloves.CooldownId = WellFedCooldownId;
            gloves.CooldownDuration = duration;
            gloves.UseTimestamp = Time.GetUnixTime() + duration;
            gloves.ChangesDetected = true;
            player.EnchantmentManager.StartCooldown(gloves);
        }

        private static double GetWellFedCooldownRemaining(WorldObject gloves)
        {
            var expires = gloves?.UseTimestamp ?? 0;
            return Math.Max(0, expires - Time.GetUnixTime());
        }

        private static void EnsureWellFedCooldownVisible(Player player, WorldObject gloves, double remaining)
        {
            if (player.EnchantmentManager.GetCooldown(WellFedCooldownId) > 0)
                return;

            gloves.CooldownId = WellFedCooldownId;
            gloves.CooldownDuration = Math.Max(1, remaining);
            player.EnchantmentManager.StartCooldown(gloves);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, int> _cheeseWheelCount
            = new System.Collections.Concurrent.ConcurrentDictionary<uint, int>();

        private void TryCheeseWheelEasterEgg(Player player)
        {
            if (!Name.Equals("Wheel of Cheese", StringComparison.OrdinalIgnoreCase) &&
                !Name.Equals("Cheese Wheel", StringComparison.OrdinalIgnoreCase) &&
                !Name.Contains("Cheese", StringComparison.OrdinalIgnoreCase))
                return;

            var count = _cheeseWheelCount.AddOrUpdate(player.Guid.Full, 1, (_, c) => c + 1);

            if (count >= 3)
            {
                _cheeseWheelCount[player.Guid.Full] = 0;

                var chain = new ACE.Server.Entity.Actions.ActionChain();
                chain.AddDelaySeconds(0.6);
                chain.AddAction(player, () =>
                {
                    player.EnqueueBroadcastMotion(new ACE.Server.Entity.Motion(player, MotionCommand.Flatulence));
                    player.EnqueueBroadcast(new GameMessageSound(player.Guid, Sound.Fizzle, 1.0f));
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                        "You probably shouldn't have eaten that third wheel of cheese...",
                        ACE.Entity.Enum.ChatMessageType.System));
                    player.EnqueueBroadcast(new GameMessageHearSpeech(
                        "*involuntary trumpet*",
                        player.Name, player.Guid.Full, ACE.Entity.Enum.ChatMessageType.Emote));
                });
                chain.EnqueueChain();
            }
        }

        public void BoostVital(Player player)
        {
            var vital = player.GetCreatureVital(BoosterEnum);

            if (vital == null)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{Name} ({Guid}) contains invalid vital {BoosterEnum}", ChatMessageType.Broadcast));
                return;
            }

            // only apply to restoration food?
            var ratingMod = BoostValue > 0 ? player.GetHealingRatingMod() : 1.0f;

            var boostValue = (int)Math.Round(BoostValue * ratingMod);

            var vitalChange = (uint)Math.Abs(player.UpdateVitalDelta(vital, boostValue));

            if (BoosterEnum == PropertyAttribute2nd.Health)
            {
                if (BoostValue >= 0)
                    player.DamageHistory.OnHeal(vitalChange);
                else
                    player.DamageHistory.Add(this, DamageType.Health, vitalChange);
            }

            var verb = BoostValue >= 0 ? "restores" : "takes";

            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The {Name} {verb} {vitalChange} points of your {BoosterEnum}.", ChatMessageType.Broadcast));

            if (player.IsDead)
            {
                player.OnDeath(player.DamageHistory.LastDamager, DamageType.Health, false);
                player.Die();
            }
        }

        public void CastSpell(Player player)
        {
            var spell = new Spell(SpellDID.Value);

            if (spell.NotFound)
            {
                if (spell._spellBase != null)
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System));
                else
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Invalid spell id {SpellDID ?? 0}", ChatMessageType.System));

                return;
            }

            // should be 'You cast', instead of 'Item cast'
            // omitting the item caster here, so player is also used for enchantment registry caster,
            // which could prevent some scenarios with spamming enchantments from multiple food sources to protect against dispels
            player.TryCastSpell(spell, player, this, tryResist: false);
        }

        public Sound GetUseSound()
        {
            var useSound = UseSound;

            if (useSound == Sound.Invalid)
                useSound = Sound.Eat1;

            return useSound;
        }
    }
}
