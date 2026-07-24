using System;
using ACE.Entity;
using EntityPosition = ACE.Entity.Position;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Sequence;
namespace ACE.Server.WorldObjects
{
    public class FlutterStone : Gem
    {
        public const uint FlutterStoneWeenieClassId = 2000620;
        private const double FlutterDistanceFeet = 20.0;
        internal const int FlutterStoneCooldownId = 2042;
        internal const double DefaultCooldownSeconds = 30.0;
        private const double SpecializedArcaneLoreCooldownSeconds = 20.0;
        private const double InteriorSafetyStepFeet = 2.0;

        private static readonly double[] SafeFlutterDistances =
        {
            FlutterDistanceFeet,
            16.0,
            12.0,
            8.0,
            4.0,
        };

        public FlutterStone(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public FlutterStone(Biota biota) : base(biota)
        {
        }

        public override void OnActivate(WorldObject activator)
        {
            if (activator is Player player)
                ApplyCooldown(player);

            base.OnActivate(activator);
        }

        public override void ActOnUse(WorldObject activator)
        {
            if (activator is not Player player)
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

            if (player.Location == null || player.CurrentLandblock == null || player.PhysicsObj == null)
            {
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The stone cannot find the road ahead."));
                return;
            }

            if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null)
            {
                player.SendTransientError($"Cannot find the {Name}");
                return;
            }

            if (!TryResolveSafeDestination(player, out var destination))
            {
                player.ApplyVisualEffects(PlayScript.Fizzle, 0.7f);
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The Flutter Stone finds no safe place ahead."));
                return;
            }

            player.ApplyVisualEffects(PlayScript.LayingofHands, 1.0f);
            player.Sequences.GetNextSequence(SequenceType.ObjectForcePosition);
            player.UpdatePlayerPosition(destination, true);
            player.Session?.Network.EnqueueSend(new GameMessageSystemChat("The Flutter Stone flutters you forward.", ChatMessageType.Broadcast));

            if ((GetProperty(PropertyBool.UnlimitedUse) ?? false) == false)
                player.TryConsumeFromInventoryWithNetworking(this, 1);
        }

        private void ApplyCooldown(Player player)
        {
            var arcaneLore = player.GetCreatureSkill(Skill.ArcaneLore);
            CooldownId = FlutterStoneCooldownId;
            CooldownDuration = arcaneLore.AdvancementClass >= SkillAdvancementClass.Specialized
                ? SpecializedArcaneLoreCooldownSeconds
                : DefaultCooldownSeconds;
        }

        private static bool TryResolveSafeDestination(Player player, out EntityPosition destination)
        {
            destination = null;

            foreach (var distance in SafeFlutterDistances)
            {
                var candidate = player.Location.InFrontOf(distance);
                candidate.InstanceId = player.Location.InstanceId;

                if (!TryPrepareCandidate(player, candidate))
                    continue;

                if (candidate.LandblockId.Landblock != player.Location.LandblockId.Landblock)
                    continue;

                if (!IsSafeFlutterPath(player, candidate))
                    continue;

                if (!player.ValidateMovement(candidate))
                    continue;

                destination = candidate;
                return true;
            }

            return false;
        }

        public static bool TryResolveSafeDestination(Player player, EntityPosition desired, out EntityPosition destination)
        {
            destination = null;
            if (player?.Location == null || player.CurrentLandblock == null || desired == null)
                return false;

            var dx = desired.PositionX - player.Location.PositionX;
            var dy = desired.PositionY - player.Location.PositionY;
            var dz = desired.PositionZ - player.Location.PositionZ;
            if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < 0.01)
                return false;

            foreach (var scale in new[] { 1.0, 0.8, 0.6, 0.4, 0.2 })
            {
                var candidate = new EntityPosition(player.Location);
                candidate.PositionX += (float)(dx * scale);
                candidate.PositionY += (float)(dy * scale);
                candidate.PositionZ += (float)(dz * scale);
                candidate.InstanceId = player.Location.InstanceId;
                candidate.LandblockId = new LandblockId(candidate.GetCell());
                if (!TryPrepareCandidate(player, candidate)) continue;
                if (candidate.LandblockId.Landblock != player.Location.LandblockId.Landblock) continue;
                if (!IsSafeFlutterPath(player, candidate)) continue;
                if (!player.ValidateMovement(candidate)) continue;
                destination = candidate;
                return true;
            }
            return false;
        }

        private static bool IsSafeFlutterPath(Player player, EntityPosition destination)
        {
            if (player?.Location == null || destination == null)
                return false;

            if (destination.InstanceId != player.Location.InstanceId)
                return false;

            if (destination.LandblockId.Landblock != player.Location.LandblockId.Landblock)
                return false;

            if (player.Location.Indoors || destination.Indoors || player.CurrentLandblock?.IsDungeon == true)
                return IsSafeInteriorFlutterPath(player, destination);

            return true;
        }

        private static bool IsSafeInteriorFlutterPath(Player player, EntityPosition destination)
        {
            if (player.Location == null || destination == null)
                return false;

            if (!player.Location.Indoors || !destination.Indoors)
                return false;

            if ((player.Location.Cell & 0xFFFF0000) != (destination.Cell & 0xFFFF0000))
                return false;

            var fromCell = ACE.Server.Physics.Common.LScape.get_landcell(player.Location.Cell);
            var toCell = ACE.Server.Physics.Common.LScape.get_landcell(destination.Cell);
            if (fromCell == null || toCell == null || !fromCell.IsVisible(toCell))
                return false;

            var dx = destination.PositionX - player.Location.PositionX;
            var dy = destination.PositionY - player.Location.PositionY;
            var dz = destination.PositionZ - player.Location.PositionZ;
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= 0.01)
                return false;

            var samples = Math.Max(1, (int)Math.Ceiling(distance / InteriorSafetyStepFeet));
            uint previousCell = player.Location.Cell;

            for (var i = 1; i <= samples; i++)
            {
                var t = i / (double)samples;
                var sample = new EntityPosition(player.Location);
                sample.PositionX += (float)(dx * t);
                sample.PositionY += (float)(dy * t);
                sample.PositionZ += (float)(dz * t);
                sample.InstanceId = player.Location.InstanceId;
                sample.LandblockId = new LandblockId(sample.GetCell());

                if (!TryPrepareCandidate(player, sample))
                    return false;

                if (!sample.Indoors || (sample.Cell & 0xFFFF0000) != (player.Location.Cell & 0xFFFF0000))
                    return false;

                var previous = ACE.Server.Physics.Common.LScape.get_landcell(previousCell);
                var current = ACE.Server.Physics.Common.LScape.get_landcell(sample.Cell);
                if (previous == null || current == null || !previous.IsVisible(current))
                    return false;

                if (!player.ValidateMovement(sample))
                    return false;

                previousCell = sample.Cell;
            }

            return true;
        }
        private static bool TryPrepareCandidate(Player player, EntityPosition candidate)
        {
            if (player.CurrentLandblock.IsDungeon || candidate.Indoors)
            {
                if (candidate.LandblockId.Landblock != player.Location.LandblockId.Landblock)
                    return false;

                AdjustDungeon(candidate);
                return candidate.LandblockId.Landblock == player.Location.LandblockId.Landblock;
            }

            candidate.AdjustMapCoords();
            return candidate.IsWalkable();
        }
    }
}





