using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        private const double DanceBootWarmupSeconds = 10.0;
        private double _danceBootStartedTime;
        private double _danceBootNextPulseTime;
        private uint _danceBootActiveGuid;
        private bool _danceBootWarmupMessageSent;
        private const float DanceBootFellowshipRange = 30.0f;

        private void DanceBootsTick(double currentUnixTime)
        {
            if (IsDead || CombatMode != CombatMode.NonCombat || !IsDanceMotionActive())
            {
                ResetDanceBootState();
                return;
            }

            var boots = GetEquippedDanceBoots();
            if (boots == null)
            {
                ResetDanceBootState();
                return;
            }

            if (_danceBootActiveGuid != boots.Guid.Full)
            {
                _danceBootActiveGuid = boots.Guid.Full;
                _danceBootStartedTime = currentUnixTime;
                _danceBootNextPulseTime = currentUnixTime + DanceBootWarmupSeconds;
                _danceBootWarmupMessageSent = false;
                return;
            }

            if (currentUnixTime - _danceBootStartedTime < DanceBootWarmupSeconds)
                return;

            if (!_danceBootWarmupMessageSent)
            {
                Session?.Network.EnqueueSend(new GameMessageSystemChat($"Your {boots.Name} find the rhythm.", ChatMessageType.Magic));
                _danceBootWarmupMessageSent = true;
            }

            if (currentUnixTime < _danceBootNextPulseTime)
                return;

            var interval = Math.Clamp(boots.GetProperty(PropertyFloat.DanceBootPulseIntervalSeconds) ?? 5.0, 1.0, 30.0);
            _danceBootNextPulseTime = currentUnixTime + interval;

            var amount = Math.Clamp((int)Math.Round(boots.GetProperty(PropertyFloat.DanceBootRestoreAmount) ?? 1.0), 1, 10);
            GetDanceBootVital(this, boots, out var label, out var playScript);

            var targets = GetDanceBootTargets();
            var restoredTotal = 0;
            var restoredCount = 0;

            foreach (var target in targets)
            {
                var vital = GetDanceBootVital(target, boots, out _, out _);
                if (vital == null || vital.Current >= vital.MaxValue)
                    continue;

                var restored = target.UpdateVitalDelta(vital, amount);
                if (restored <= 0)
                    continue;

                restoredTotal += restored;
                restoredCount++;
                target.EnqueueBroadcast(new GameMessageScript(target.Guid, playScript, 0.5f));

                if (target != this)
                    target.Session?.Network.EnqueueSend(new GameMessageSystemChat($"{Name}'s {GetDanceBootAbilityName(boots)} restores {restored} points of your {label}.", ChatMessageType.Magic));
                else
                    Session?.Network.EnqueueSend(new GameMessageSystemChat($"{GetDanceBootAbilityName(boots)} restores {restored} points of your {label}.", ChatMessageType.Magic));
            }

            if (restoredTotal <= 0)
                return;

            if (targets.Count > 0 && targets[0] != this)
            {
                var fellowText = restoredCount == 1 ? "nearby fellow" : "nearby fellows";
                Session?.Network.EnqueueSend(new GameMessageSystemChat($"{GetDanceBootAbilityName(boots)} restores {restoredTotal} total {label} across {restoredCount} {fellowText}.", ChatMessageType.Magic));
            }
        }

        private bool IsDanceMotionActive()
        {
            var command = CurrentMotionState?.MotionState?.ForwardCommand ?? MotionCommand.Invalid;
            return command == MotionCommand.DrudgeDance || command == MotionCommand.DrudgeDanceState;
        }

        private WorldObject GetEquippedDanceBoots()
        {
            if (!EquippedObjectsLoaded)
                return null;

            return EquippedObjects.Values.FirstOrDefault(item =>
                item.CurrentWieldedLocation is EquipMask loc
                && loc.HasFlag(EquipMask.FootWear)
                && (item.GetProperty(PropertyBool.IsHealingDanceBoots) == true
                    || item.GetProperty(PropertyBool.IsRejuvenatingDanceBoots) == true
                    || item.GetProperty(PropertyBool.IsReplenishingDanceBoots) == true));
        }

        private List<Player> GetDanceBootTargets()
        {
            var fellowTargets = GetNearbyDanceBootFellows();
            return fellowTargets.Count > 0 ? fellowTargets : new List<Player> { this };
        }

        private List<Player> GetNearbyDanceBootFellows()
        {
            var targets = new List<Player>();
            if (Fellowship == null || Location == null || CurrentLandblock == null)
                return targets;

            var rangeSq = DanceBootFellowshipRange * DanceBootFellowshipRange;
            foreach (var weakRef in Fellowship.FellowshipMembers.Values)
            {
                if (!weakRef.TryGetTarget(out var fellow) || fellow == null || fellow == this)
                    continue;

                if (!fellow.IsAlive || fellow.Location == null || fellow.CurrentLandblock != CurrentLandblock)
                    continue;

                if (Location.Distance2DSquared(fellow.Location) > rangeSq)
                    continue;

                targets.Add(fellow);
            }

            return targets;
        }

        private static CreatureVital GetDanceBootVital(Player player, WorldObject boots, out string label, out PlayScript playScript)
        {
            if (boots.GetProperty(PropertyBool.IsHealingDanceBoots) == true)
            {
                label = "Health";
                playScript = PlayScript.HealthUpRed;
                return player.Health;
            }

            if (boots.GetProperty(PropertyBool.IsReplenishingDanceBoots) == true)
            {
                label = "Mana";
                playScript = PlayScript.RegenUpBlue;
                return player.Mana;
            }

            label = "Stamina";
            playScript = PlayScript.RegenUpYellow;
            return player.Stamina;
        }

        private static string GetDanceBootAbilityName(WorldObject boots)
        {
            if (boots.GetProperty(PropertyBool.IsHealingDanceBoots) == true)
                return "Healing Dance";

            if (boots.GetProperty(PropertyBool.IsReplenishingDanceBoots) == true)
                return "Replenishing Dance";

            return "Rejuvenating Dance";
        }

        private void ResetDanceBootState()
        {
            _danceBootStartedTime = 0;
            _danceBootNextPulseTime = 0;
            _danceBootActiveGuid = 0;
            _danceBootWarmupMessageSent = false;
        }
    }
}
