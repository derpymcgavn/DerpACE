using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Common.Extensions;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Factories;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class WorldObject
    {
        public List<WorldObject> GetCreateListForSlumLord(DestinationType type)
        {
            var items = new List<WorldObject>();

            foreach (var item in Biota.PropertiesCreateList.Where(x => x.DestinationType == type))
            {
                var wo = WorldObjectFactory.CreateNewWorldObject(item.WeenieClassId);

                if (item.Palette > 0)
                    wo.PaletteTemplate = item.Palette;

                if (item.Shade > 0)
                    wo.Shade = item.Shade;

                if (item.StackSize > 0)
                {
                    if (wo is Stackable)
                        wo.SetStackSize(item.StackSize);
                    else
                        wo.StackSize = item.StackSize;  // item isn't a stackable object, but we want multiples of it while not displaying multiple single items in the profile. Munge stacksize to get us there.
                }

                items.Add(wo);
            }
            return items;
        }

        public static List<WorldObject> GenerateWieldedTreasureSets(List<TreasureWielded> items)
        {
            var curIdx = 0;
            List<WorldObject> results = null;
            GenerateWieldedTreasureSets(items, ref results, ref curIdx);
            return results;
        }

        private static void GenerateWieldedTreasureSets(List<TreasureWielded> items, ref List<WorldObject> results, ref int curIdx, bool skip = false)
        {
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
            var probability = 0.0f;
            var rolled = false;
            var continued = false;

            for ( ; curIdx < items.Count; curIdx++)
            {
                var item = items[curIdx];

                if (item.ContinuesPreviousSet)
                {
                    if (!continued)
                    {
                        curIdx--;
                        return;
                    }
                    else
                        continued = false;
                }

                var skipNext = true;

                if (!skip)
                {
                    if (item.SetStart || probability >= 1.0f)
                    {
                        rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                        probability = 0.0f;
                        rolled = false;
                    }

                    probability += item.Probability;

                    if (rng < probability && !rolled)
                    {
                        rolled = true;
                        skipNext = false;

                        // item roll successful, add to generated list
                        var wo = CreateWieldedTreasure(item);

                        if (wo != null)
                        {
                            if (results == null)
                                results = new List<WorldObject>();

                            results.Add(wo);
                        }
                    }
                }

                if (item.HasSubSet)
                {
                    curIdx++;
                    GenerateWieldedTreasureSets(items, ref results, ref curIdx, skipNext);
                    continued = true;
                }
            }
        }

        /*public static List<WorldObject> GenerateWieldedTreasureSets(TreasureWieldedTable table)
        {
            var wieldedTreasure = new List<WorldObject>();

            foreach (var set in table.Sets)
                wieldedTreasure.AddRange(GenerateWieldedTreasureSet(set));

            return wieldedTreasure;
        }

        public static List<WorldObject> GenerateWieldedTreasureSet(TreasureWieldedSet set)
        {
            var wieldedTreasure = new List<WorldObject>();

            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
            var probability = 0.0f;
            var rolled = false;

            foreach (var item in set.Items)
            {
                if (item.Item.SetStart || probability >= 1.0f)
                {
                    rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                    probability = 0.0f;
                    rolled = false;
                }
                probability += item.Item.Probability;

                if (rng >= probability || rolled) continue;

                rolled = true;

                // item roll successful, spawn item in creature inventory
                var wo = CreateWieldedTreasure(item.Item);
                if (wo == null) continue;

                wieldedTreasure.Add(wo);

                // traverse into possible subsets
                if (item.Subset != null)
                    wieldedTreasure.AddRange(GenerateWieldedTreasureSet(item.Subset));
            }

            return wieldedTreasure;
        }*/

        public static WorldObject CreateWieldedTreasure(TreasureWielded item)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(item.WeenieClassId);
            if (wo == null) return null;

            if (item.PaletteId > 0)
                wo.PaletteTemplate = (int)item.PaletteId;

            if (item.Shade > 0)
                wo.Shade = item.Shade;

            if (item.StackSize > 0)
            {
                var stackSize = item.StackSize;

                var hasVariance = item.StackSizeVariance > 0;
                if (hasVariance)
                {
                    var minStack = Math.Max(1, (item.StackSize * (1.0f - item.StackSizeVariance)).Round());
                    var maxStack = item.StackSize;
                    stackSize = ThreadSafeRandom.Next(minStack, maxStack);
                }
                wo.SetStackSize(stackSize);
            }
            return wo;
        }

        public virtual void OnWield(Creature creature)
        {
            EmoteManager.OnWield(creature);

            // Thief's Dagger: enter stealth.
            // Translucency is set synchronously so that if the tracking system recreates the
            // player during the particle delay it already carries the correct value.
            // We can't send GameMessageCreateObject to self (teleport-re-init / duplicates),
            // so for the wielder we push a GameMessagePublicUpdatePropertyFloat directly to
            // their session — this updates the client's local Translucency without rebuilding
            // the object. For nearby players we still do the Delete+Create dance.
            if (GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger) == true)
            {
                const float stealthTranslucency = 0.75f;

                var player = creature as Player;
                creature.Translucency = stealthTranslucency;   // set immediately so all future CreateObject packets include it

                // Self-sync: push the translucency property to the wielder so their own client
                // immediately renders them translucent (no re-create, no teleport flicker).
                if (player?.Session != null)
                {
                    player.Session.Network.EnqueueSend(new GameMessagePublicUpdatePropertyFloat(
                        creature, ACE.Entity.Enum.Properties.PropertyFloat.Translucency, stealthTranslucency));
                }

                var chain = new ActionChain();
                // 1. play black particle (visible to self + others), then delete others' stale copy
                chain.AddAction(creature, () =>
                {
                    creature.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SkillDownBlack);
                    creature.EnqueueBroadcast(false, new GameMessageDeleteObject(creature));
                });
                chain.AddDelaySeconds(0.5);
                // 2. recreate for others — translucency is already set on the object
                chain.AddAction(creature, () =>
                {
                    creature.EnqueueBroadcast(false, new GameMessageCreateObject(creature));
                    player?.SendMessage("You slip into the shadows.", ACE.Entity.Enum.ChatMessageType.Magic);
                });
                chain.EnqueueChain();
            }
        }

        public virtual void OnUnWield(Creature creature)
        {
            EmoteManager.OnUnwield(creature);

            // Thief's Dagger: only exit stealth when the last Thief's Dagger is removed
            if (GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger) == true)
            {
                var stillHasThievesDagger = creature.EquippedObjects.Values
                    .Any(w => w.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger) == true);

                if (!stillHasThievesDagger)
                {
                    var player = creature as Player;
                    creature.Translucency = null;   // clear immediately so tracking-system recreates are opaque

                    // Self-sync: push the cleared translucency back to the wielder's client.
                    // PublicUpdatePropertyFloat with value 0.0 is the standard "back to opaque" signal.
                    if (player?.Session != null)
                    {
                        player.Session.Network.EnqueueSend(new GameMessagePublicUpdatePropertyFloat(
                            creature, ACE.Entity.Enum.Properties.PropertyFloat.Translucency, 0.0));
                    }

                    var chain = new ActionChain();
                    // 1. delete others' translucent copy
                    chain.AddAction(creature, () =>
                    {
                        creature.EnqueueBroadcast(false, new GameMessageDeleteObject(creature));
                    });
                    chain.AddDelaySeconds(0.3);
                    // 2. recreate for others at full opacity
                    chain.AddAction(creature, () =>
                    {
                        creature.EnqueueBroadcast(false, new GameMessageCreateObject(creature));
                        creature.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.UnHide);
                        player?.SendMessage("You step out of the shadows.", ACE.Entity.Enum.ChatMessageType.Magic);
                    });
                    chain.EnqueueChain();
                }
            }
        }
    }
}
