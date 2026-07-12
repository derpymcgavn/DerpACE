using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static partial class GlobalKillQuestManager
    {
        private const double WorldUnitsPerClick = 240.0;
        private const double MaximumFootSampleWorldUnits = 30.0;
        private static readonly ConcurrentDictionary<string, TrekMovementSample> _trekSamples = new ConcurrentDictionary<string, TrekMovementSample>();

        public static void OnPlayerOverworldMoved(Player player, Position position, bool grounded)
        {
            if (player == null || position == null)
                return;

            var rawMovement = player.CurrentMoveToState?.RawMotionState;
            var movingByFoot = rawMovement != null &&
                (rawMovement.ForwardCommand != MotionCommand.Invalid || rawMovement.SidestepCommand != MotionCommand.Invalid);

            List<(PersistentGlobalQuest quest, int rewardPercent)> completions = null;
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow).Where(q => q.Kind == GlobalQuestKind.CardinalTrek).ToList())
                {
                    var sampleKey = MakePersistentKey(player.Guid.Full, quest.Epoch) + ":trek";
                    if (!grounded || player.Teleporting || position.Indoors || position.InstanceId != 0 || !movingByFoot || IsNonRepeatPersistentQuestCompleted(player, quest))
                    {
                        _trekSamples.TryRemove(sampleKey, out _);
                        continue;
                    }

                    var worldX = position.LandblockId.LandblockX * Position.BlockLength + position.PositionX;
                    var worldY = position.LandblockId.LandblockY * Position.BlockLength + position.PositionY;
                    if (!_trekSamples.TryGetValue(sampleKey, out var previous))
                    {
                        _trekSamples[sampleKey] = new TrekMovementSample(worldX, worldY);
                        continue;
                    }

                    _trekSamples[sampleKey] = new TrekMovementSample(worldX, worldY);
                    var dx = worldX - previous.WorldX;
                    var dy = worldY - previous.WorldY;
                    var sampleDistance = Math.Sqrt(dx * dx + dy * dy);
                    if (sampleDistance <= 0 || sampleDistance > MaximumFootSampleWorldUnits)
                        continue;

                    var directionalUnits = quest.Direction switch
                    {
                        "North" => Math.Max(0, dy),
                        "East" => Math.Max(0, dx),
                        "South" => Math.Max(0, -dy),
                        "West" => Math.Max(0, -dx),
                        _ => 0,
                    };
                    if (directionalUnits <= 0)
                        continue;

                    var progressKey = MakePersistentKey(player.Guid.Full, quest.Epoch);
                    var oldWholeClicks = 0;
                    var progress = _persistentProgress.AddOrUpdate(progressKey,
                        _ => new PersistentGlobalQuestProgress { Distance = directionalUnits / WorldUnitsPerClick },
                        (_, old) =>
                        {
                            oldWholeClicks = (int)Math.Floor(old.Distance);
                            old.Distance += directionalUnits / WorldUnitsPerClick;
                            return old;
                        });
                    MarkPersistentStateDirtyUnsafe();

                    var wholeClicks = (int)Math.Floor(progress.Distance);
                    if (progress.Distance >= quest.Required)
                    {
                        if (TryFinishPersistentQuest(player, quest))
                        {
                            completions ??= new List<(PersistentGlobalQuest, int)>();
                            completions.Add((quest, quest.RewardPercent));
                        }
                    }
                    else if (wholeClicks > oldWholeClicks && (wholeClicks == 1 || wholeClicks % 5 == 0))
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {wholeClicks}/{quest.Required} clicks traveled {quest.Direction} on foot.", ChatMessageType.Broadcast);
                }

                if (completions != null)
                {
                    foreach (var completion in completions)
                    {
                        BroadcastPersistentWrapUp(completion.quest);
                        RollPersistentQuest(completion.quest.Lane, true, DateTime.UtcNow);
                    }
                    SavePersistentStateNowUnsafe();
                }
                else
                    SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            if (completions == null)
                return;

            foreach (var completion in completions)
            {
                var levelXp = player.GetXPToNextLevel(player.Level ?? 1);
                var bonus = Math.Max(1, (long)Math.Round(levelXp * (completion.rewardPercent / 100.0)));
                player.EarnXP(bonus, XpType.Quest);
                player.SendMessage($"[Global Quest Complete:{GetLaneLabel(completion.quest.Lane)}] You were first to travel {completion.quest.Required} clicks {completion.quest.Direction} on foot and earned {bonus:N0} XP ({completion.rewardPercent}% of level XP)!", ChatMessageType.Broadcast);
                BroadcastPersistentCompletion(player, completion.quest, $"{player.Name} won the {GetLaneLabel(completion.quest.Lane)} cardinal trek by traveling {completion.quest.Required} clicks {completion.quest.Direction} on foot!");
            }
        }

        private sealed class TrekMovementSample
        {
            public double WorldX { get; }
            public double WorldY { get; }

            public TrekMovementSample(double worldX, double worldY)
            {
                WorldX = worldX;
                WorldY = worldY;
            }
        }
    }
}