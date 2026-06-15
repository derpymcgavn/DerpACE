using System;
using System.Linq;

using ACE.Common;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        private static readonly SpellId[] AlchemicalInstabilityDebuffs =
        {
            SpellId.ImperilOther7,
            SpellId.VulnerabilityOther7,
            SpellId.Brittlemail7,
            SpellId.FesterOther7,
            SpellId.ExhaustionOther7,
            SpellId.ManaDepletionOther7,
            SpellId.WeaknessOther7,
            SpellId.FrailtyOther7,
            SpellId.ClumsinessOther7,
            SpellId.SlownessOther7,
            SpellId.MagicYieldOther7,
            SpellId.WarMagicIneptitudeOther7,
            SpellId.MissileWeaponsIneptitudeOther7,
            SpellId.LightWeaponsIneptitudeOther7,
            SpellId.HeavyWeaponsIneptitudeOther7,
            SpellId.FinesseWeaponsIneptitudeOther7,
            SpellId.VoidMagicIneptitudeOther7,
            SpellId.ShieldIneptitudeOther7,
        };

        /// <summary>
        /// This is set by HandleActionUseItem / TryUseItem
        /// </summary>
        public ObjectGuid LastOpenedContainerId { get; set; }

        /// <summary>
        /// This is set by Hook.ActOnUse
        /// </summary>
        public ObjectGuid LasUsedHookId { get; set; }

        /// <summary>
        /// Handles the 'GameAction 0x35 - UseWithTarget' network message
        /// when player double clicks an inventory item resulting in a target indicator
        /// and then clicks another item
        /// </summary>
        public void HandleActionUseWithTarget(uint sourceObjectGuid, uint targetObjectGuid)
        {
            if (PKLogout)
            {
                SendUseDoneEvent(WeenieError.YouHaveBeenInPKBattleTooRecently);
                return;
            }

            StopExistingMoveToChains();

            // source item is always in our possession
            var sourceItem = FindObject(sourceObjectGuid, SearchLocations.MyInventory | SearchLocations.MyEquippedItems, out _, out _, out var sourceItemIsEquipped);

            if (sourceItem == null)
            {
                log.Warn($"{Name}.HandleActionUseWithTarget({sourceObjectGuid:X8}, {targetObjectGuid:X8}): couldn't find {sourceObjectGuid:X8}");
                SendUseDoneEvent();
                return;
            }

            // Resolve the guid to an object that is either in our possession or on the Landblock
            var target = FindObject(targetObjectGuid, SearchLocations.MyInventory | SearchLocations.MyEquippedItems | SearchLocations.Landblock);

            if (target == null)
            {
                log.Warn($"{Name}.HandleActionUseWithTarget({sourceObjectGuid:X8}, {targetObjectGuid:X8}): couldn't find {targetObjectGuid:X8}");
                SendUseDoneEvent();
                return;
            }

            // handle objects with built-in spells
            if (sourceItem.SpellDID != null)
            {
                if (!RecipeManager.VerifyUse(this, sourceItem, target))
                {
                    //var spell = new Spell((int)sourceItem.SpellDID);
                    //if (spell != null)
                    //    Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"{spell.Name} cannot be cast on {target.Name}."));
                    var usable = sourceItem.ItemUseable ?? Usable.Undef;
                    var action = "";
                    if (usable.HasFlag(Usable.Wielded))
                        action = "wield";
                    else if (usable.HasFlag(Usable.Contained))
                        action = "contain";
                    Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"You must {action} the {sourceItem.Name} to use it."));
                    SendUseDoneEvent();
                    return;
                }
                // check activation requirements
                var result = sourceItem.CheckUseRequirements(this);
                if (!result.Success)
                {
                    if (result.Message != null)
                        Session.Network.EnqueueSend(result.Message);

                    SendUseDoneEvent();
                    return;
                }
                else
                {
                    HandleActionCastTargetedSpell(targetObjectGuid, sourceItem.SpellDID ?? 0, sourceItem);
                    TryAlchemistPhialSplash(sourceItem, target);
                    return;
                }
            }

            // handle casters with built-in spells
            //if (sourceItemIsEquipped)
            //{
            //    if (sourceItem.SpellDID != null)
            //    {
            //        // check activation requirements
            //        var result = sourceItem.CheckUseRequirements(this);
            //        if (!result.Success)
            //        {
            //            if (result.Message != null)
            //                Session.Network.EnqueueSend(result.Message);

            //            SendUseDoneEvent();
            //        }
            //        else
            //        {
            //            HandleActionCastTargetedSpell(targetObjectGuid, sourceItem.SpellDID ?? 0, true);
            //            return;
            //        }
            //    }
            //    else
            //    {
            //        SendUseDoneEvent();
            //    }

            //    return;
            //}

            if (IsTrading)
            {
                if (sourceItem.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
                {
                    SendUseDoneEvent(WeenieError.TradeItemBeingTraded);
                    //SendWeenieError(WeenieError.TradeItemBeingTraded);
                    return;
                }
                if (target.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
                {
                    SendUseDoneEvent(WeenieError.TradeItemBeingTraded);
                    //SendWeenieError(WeenieError.TradeItemBeingTraded);
                    return;
                }
            }

            if (sourceItem is Healer healerItem)
                target = healerItem.GetValidHealingTarget(this, target);

            // re-verify client checks
            if (((sourceItem.TargetType ?? ItemType.None) & target.ItemType) == ItemType.None)
            {
                // ItemHolder::TargetCompatibleWithObject
                SendTransientError($"Cannot use the {sourceItem.Name} with the {target.Name}");
                SendUseDoneEvent();
                return;
            }

            if (target.CurrentLandblock != null && target != this)
            {
                // todo: verify target can be used remotely
                // move RecipeManager.VerifyUse logic into base Player_Use
                // this was avoided because i didn't want to deal with the ramifications of random items missing the correct ItemUseable flags,
                // and because there are still some ItemUseable flags with missing logic we haven't quite figured out yet

                if (IsBusy)
                {
                    SendUseDoneEvent(WeenieError.YoureTooBusy);
                    return;
                }

                CreateMoveToChain(target, (success) =>
                {
                    if (success)
                        sourceItem.HandleActionUseOnTarget(this, target);
                    else
                        SendUseDoneEvent();
                });
            }
            else
                sourceItem.HandleActionUseOnTarget(this, target);
        }

        private void TryAlchemistPhialSplash(WorldObject sourceItem, WorldObject primaryTarget)
        {
            if (sourceItem == null || primaryTarget == null || sourceItem.SpellDID == null)
                return;

            if (!IsAlchemyPhial(sourceItem))
                return;

            if (!(primaryTarget is Creature primaryCreature) || primaryCreature is Player || !primaryCreature.IsAlive)
                return;

            var gloves = Food.GetAlchemistGlovesEquipped(this);
            if (gloves == null)
                return;

            var spell = new Spell(sourceItem.SpellDID.Value);
            if (spell.NotFound || !spell.IsHarmful)
                return;

            TryAlchemicalInstability(gloves, sourceItem, primaryCreature, 0.5);

            var chance = Math.Clamp(gloves.GetProperty(PropertyFloat.AlchemistSplashProcChance) ?? 0.10, 0.0, 0.50);
            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= chance)
                return;

            var maxTargets = Math.Clamp((int)Math.Round(gloves.GetProperty(PropertyFloat.AlchemistSplashTargetCount) ?? 1.0), 1, 3);
            const float splashRadius = 10.0f;

            var splashTargets = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature()
                .Where(creature => creature != null
                    && creature != this
                    && creature != primaryCreature
                    && creature is not Player
                    && creature.IsAlive
                    && creature.Location != null
                    && primaryCreature.Location != null
                    && creature.Location.Distance2D(primaryCreature.Location) <= splashRadius)
                .OrderBy(_ => ThreadSafeRandom.Next(0.0f, 1.0f))
                .Take(maxTargets)
                .ToList();

            if (splashTargets.Count == 0)
                return;

            ApplyVisualEffects(PlayScript.BreatheAcid);

            foreach (var splashTarget in splashTargets)
            {
                splashTarget.ApplyVisualEffects(PlayScript.BreatheAcid);
                TryCastSpell(spell, splashTarget, sourceItem, sourceItem, false, true, true);
            }

            Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Your alchemist gloves scatter the {sourceItem.Name}'s contents onto {splashTargets.Count} nearby target{(splashTargets.Count == 1 ? "" : "s")}.",
                ChatMessageType.Magic));
        }

        internal void TryAlchemicalInstabilityFromPotion(WorldObject sourceItem)
        {
            if (sourceItem == null)
                return;

            var gloves = Food.GetAlchemistGlovesEquipped(this);
            if (gloves?.GetProperty(PropertyBool.IsAlchemicalInstabilityGloves) != true)
                return;

            var chance = Math.Clamp(gloves.GetProperty(PropertyFloat.AlchemicalInstabilityProcChance) ?? 0.05, 0.0, 0.20);
            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= chance)
                return;

            ApplyPotionInstability(sourceItem);
        }

        private void TryAlchemicalInstability(WorldObject gloves, WorldObject sourceItem, Creature target, double chanceMultiplier)
        {
            if (gloves?.GetProperty(PropertyBool.IsAlchemicalInstabilityGloves) != true || sourceItem == null || target == null || !target.IsAlive)
                return;

            var chance = Math.Clamp((gloves.GetProperty(PropertyFloat.AlchemicalInstabilityProcChance) ?? 0.05) * chanceMultiplier, 0.0, 0.20);
            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= chance)
                return;

            var spellId = AlchemicalInstabilityDebuffs[ThreadSafeRandom.Next(0, AlchemicalInstabilityDebuffs.Length - 1)];
            var spell = new Spell((uint)spellId);
            if (spell.NotFound || !spell.IsHarmful)
                return;

            target.ApplyVisualEffects(PlayScript.AetheriaSurgeAffliction);
            TryCastSpell(spell, target, sourceItem, sourceItem, false, true, true);

            Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Alchemical Instability twists the {sourceItem.Name}, applying {spell.Name} to {target.Name}.",
                ChatMessageType.Magic));
        }

        private void ApplyPotionInstability(WorldObject sourceItem)
        {
            ApplyVisualEffects(PlayScript.AetheriaSurgeAffliction);

            var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
            if (roll < 0.45f)
            {
                ApplyInstabilitySelfDebuff(sourceItem);
            }
            else if (roll < 0.70f)
            {
                ApplyTumerokPaletteInstability(sourceItem, changeHair: true, changeSkin: false);
            }
            else if (roll < 0.90f)
            {
                ApplyTumerokPaletteInstability(sourceItem, changeHair: false, changeSkin: true);
            }
            else
            {
                ApplyTumerokPaletteInstability(sourceItem, changeHair: true, changeSkin: true);
            }
        }

        private void ApplyInstabilitySelfDebuff(WorldObject sourceItem)
        {
            var spellId = AlchemicalInstabilityDebuffs[ThreadSafeRandom.Next(0, AlchemicalInstabilityDebuffs.Length - 1)];
            var spell = new Spell((uint)spellId);
            if (spell.NotFound || !spell.IsHarmful)
                return;

            TryCastSpell(spell, this, sourceItem, sourceItem, false, true, true);
            Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Alchemical Instability curdles the {sourceItem.Name}, afflicting you with {spell.Name}.",
                ChatMessageType.Magic));
        }

        private void ApplyTumerokPaletteInstability(WorldObject sourceItem, bool changeHair, bool changeSkin)
        {
            if (!TryGetTumerokSexData(out var tumerokSex))
            {
                ApplyInstabilitySelfDebuff(sourceItem);
                return;
            }

            var changed = false;

            if (changeHair && TryGetRandomTumerokHairPalette(tumerokSex, out var hairPalette))
            {
                HairPaletteDID = hairPalette;
                changed = true;
            }

            if (changeSkin && TryGetRandomTumerokSkinPalette(tumerokSex, out var skinPalette))
            {
                SkinPaletteDID = skinPalette;
                changed = true;
            }

            if (!changed)
            {
                ApplyInstabilitySelfDebuff(sourceItem);
                return;
            }

            EnqueueBroadcast(new GameMessageObjDescEvent(this));

            var changedPart = changeHair && changeSkin ? "hair and skin" : changeHair ? "hair" : "skin";
            Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Alchemical Instability stains your {changedPart} with impossible Tumerok color.",
                ChatMessageType.Magic));
        }

        private bool TryGetTumerokSexData(out ACE.DatLoader.Entity.SexCG sex)
        {
            sex = null;

            if (!Gender.HasValue)
                return false;

            if (!DatManager.PortalDat.CharGen.HeritageGroups.TryGetValue((uint)HeritageGroup.Tumerok, out var tumerok))
                return false;

            return tumerok.Genders.TryGetValue(Gender.Value, out sex);
        }

        private static bool TryGetRandomTumerokSkinPalette(ACE.DatLoader.Entity.SexCG sex, out uint palette)
        {
            palette = 0;
            var skinPalSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(sex.SkinPalSet);
            if (skinPalSet == null || skinPalSet.PaletteList.Count == 0)
                return false;

            palette = skinPalSet.GetPaletteID(ThreadSafeRandom.Next(0.0f, 1.0f));
            return palette != 0;
        }

        private static bool TryGetRandomTumerokHairPalette(ACE.DatLoader.Entity.SexCG sex, out uint palette)
        {
            palette = 0;
            if (sex.HairColorList.Count == 0)
                return false;

            var palSetId = sex.HairColorList[ThreadSafeRandom.Next(0, sex.HairColorList.Count - 1)];
            var hairPalSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(palSetId);
            if (hairPalSet == null || hairPalSet.PaletteList.Count == 0)
                return false;

            palette = hairPalSet.GetPaletteID(ThreadSafeRandom.Next(0.0f, 1.0f));
            return palette != 0;
        }

        private static bool IsAlchemyPhial(WorldObject item)
        {
            var name = item?.Name ?? string.Empty;
            return name.Contains("Phial", StringComparison.OrdinalIgnoreCase)
                || item.ItemType == ItemType.CraftAlchemyBase
                || item.ItemType == ItemType.CraftAlchemyIntermediate;
        }

        /// <summary>
        /// Handles the 'GameAction 0x36 - UseItem' network message
        /// when player double clicks an item
        /// </summary>
        public void HandleActionUseItem(uint itemGuid)
        {
            if (PKLogout)
            {
                SendUseDoneEvent(WeenieError.YouHaveBeenInPKBattleTooRecently);
                return;
            }

            StopExistingMoveToChains();

            var item = FindObject(itemGuid, SearchLocations.MyInventory | SearchLocations.MyEquippedItems | SearchLocations.Landblock);

            if (IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
            {
                SendUseDoneEvent(WeenieError.TradeItemBeingTraded);
                //SendWeenieError(WeenieError.TradeItemBeingTraded);
                return;
            }

            if (item != null)
            {
                if (item.CurrentLandblock != null && !item.Visibility && item.Guid != LastOpenedContainerId)
                {
                    if (IsBusy)
                    {
                        SendUseDoneEvent(WeenieError.YoureTooBusy);
                        return;
                    }

                    CreateMoveToChain(item, (success) => TryUseItem(item, success));
                }
                else
                    TryUseItem(item);
            }
            else
            {
                log.DebugFormat("{0}.HandleActionUseItem({1:X8}): couldn't find object", Name, itemGuid);
                SendUseDoneEvent();
            }
        }

        public DateTime NextUseTime { get; set; }
        public float LastUseTime { get; set; }

        /// <summary>
        /// Attempts to use an item - checks activation requirements
        /// </summary>
        public void TryUseItem(WorldObject item, bool success = true)
        {
            //Console.WriteLine($"{Name}.TryUseItem({item.Name}, {success})");
            LastUseTime = 0.0f;

            if (success)
                item.OnActivate(this);

            // manually managed
            if (LastUseTime == float.MinValue)
                return;

            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(LastUseTime);
            actionChain.AddAction(this, () => SendUseDoneEvent());
            actionChain.EnqueueChain();

            NextUseTime = DateTime.UtcNow + TimeSpan.FromSeconds(LastUseTime);
        }

        /// <summary>
        /// Sends the GameEventUseDone network message for a player
        /// </summary>
        /// <param name="errorType">An optional error message</param>
        public void SendUseDoneEvent(WeenieError errorType = WeenieError.None)
        {
            Session.Network.EnqueueSend(new GameEventUseDone(Session, errorType));
        }


        /// <summary>
        /// This method processes the Game Action (F7B1) No Longer Viewing Contents (0x0195)
        /// This is raised when we:
        /// - have a container open and open up a second container without closing the first container.
        /// </summary>
        public void HandleActionNoLongerViewingContents(uint objectGuid)
        {
            var container = CurrentLandblock?.GetObject(objectGuid) as Container;

            if (container != null && container.Viewer == Guid.Full)
                container.Close(this);
        }

        public Pet CurrentActivePet { get; set; }

        public void ApplyConsumable(MotionCommand useMotion, Action action, float animMod = 1.0f)
        {
            if (PropertyManager.GetBool("allow_fast_chug").Item && FastTick)
            {
                ApplyConsumableWithAnimationCallbacks(useMotion, action);
                return;
            }
            IsBusy = true;

            var actionChain = new ActionChain();

            // if something other that NonCombat.Ready,
            // manually send this swap
            var prevStance = CurrentMotionState.Stance;

            var animTime = 0.0f;

            if (prevStance != MotionStance.NonCombat)
                animTime = EnqueueMotion_Force(actionChain, MotionStance.NonCombat, MotionCommand.Ready, (MotionCommand)prevStance);

            // start the eat/drink motion
            var useAnimTime = EnqueueMotion_Force(actionChain, MotionStance.NonCombat, useMotion, null, 1.0f, animMod);
            animTime += useAnimTime;

            // apply consumable
            actionChain.AddAction(this, action);

            if (animMod == 1.0f)
            {
                // return to ready stance
                animTime += EnqueueMotion_Force(actionChain, MotionStance.NonCombat, MotionCommand.Ready, useMotion);
            }
            else
                actionChain.AddDelaySeconds(useAnimTime * (1.0f - animMod));

            if (prevStance != MotionStance.NonCombat)
                animTime += EnqueueMotion_Force(actionChain, prevStance, MotionCommand.Ready, MotionCommand.NonCombat);

            actionChain.AddAction(this, () => { IsBusy = false; });

            actionChain.EnqueueChain();

            LastUseTime = animTime;
        }

        /// <summary>
        /// Fast chugging state variable
        /// </summary>
        public FoodState FoodState { get; set; }

        public void ApplyConsumableWithAnimationCallbacks(MotionCommand useMotion, Action action)
        {
            IsBusy = true;

            var actionChain = new ActionChain();

            // if combat mode, temporarily drop to non-combat
            var prevStance = CurrentMotionState.Stance;

            var animTime = 0.0f;

            if (prevStance != MotionStance.NonCombat)
                animTime = EnqueueMotion_Force(actionChain, MotionStance.NonCombat, MotionCommand.Ready, (MotionCommand)prevStance);

            // start the eat/drink motion
            var useAnimTime = EnqueueMotion_Force(actionChain, MotionStance.NonCombat, useMotion);

            animTime += useAnimTime;

            // the rest is based on animation callback now
            FoodState.StartChugging(useMotion, action, useAnimTime, prevStance);

            actionChain.EnqueueChain();

            // manually managed
            LastUseTime = float.MinValue;
        }

        public void HandleMotionDone_UseConsumable(uint motionID, bool success)
        {
            //Console.WriteLine($"HandleMotionDone_UseConsumable({(MotionCommand)motionID}, {success})");

            if (!FastTick || !FoodState.IsChugging) return;

            if (motionID != (uint)FoodState.UseMotion)
                return;

            // restore state vars
            var animTime = 0.0f;
            var actionChain = new ActionChain();
            var useMotion = FoodState.UseMotion;
            var useAnimTime = FoodState.UseAnimTime;
            var prevStance = FoodState.PrevStance;

            if (motionID != (uint)MotionCommand.Ready)
            {
                if (FoodState.Callback != null)
                {
                    FoodState.Callback();
                    FoodState.Callback = null;
                }

                FoodState.UseMotion = MotionCommand.Ready;

                animTime += EnqueueMotion_Force(actionChain, MotionStance.NonCombat, MotionCommand.Ready, useMotion);
            }
            else
            {
                FoodState.FinishChugging();

                if (prevStance != MotionStance.NonCombat)
                    animTime += EnqueueMotion_Force(actionChain, prevStance, MotionCommand.Ready, MotionCommand.NonCombat);

                actionChain.AddAction(this, () =>
                {
                    SendUseDoneEvent();
                    IsBusy = false;
                });
            }

            actionChain.EnqueueChain();
        }
    }
}
