using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Pathfinding;
using ACE.Server.Pathfinding.Geometry;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        private const float TownAmbientSocialRadius = 18.0f;
        private const float TownAmbientJogRadius = 14.0f;
        private const int TownAmbientMemoryLimit = 8;

        private readonly Dictionary<uint, int> townAmbientFamiliarity = new Dictionary<uint, int>();
        private readonly Queue<string> townAmbientKnownVisitors = new Queue<string>();
        private readonly Queue<string> townAmbientRecentLines = new Queue<string>();
        private double nextTownAmbientActionTime;
        private string pendingTownAmbientReply;
        private bool townAmbientAwayFromHome;
        private double nextUlgrimAyanActionTime;
        private bool ulgrimAyanAtBar;
        private int ulgrimAyanRoundsRemaining;
        private Queue<Position> ulgrimAyanRoute;
        private bool ulgrimAyanRouteToBar;

        private bool IsAyanUlgrimAmbientMovementOwned => ulgrimAyanRoute != null || ulgrimAyanAtBar;

        private const float UlgrimAyanInitialTripDelayMin = 120.0f;
        private const float UlgrimAyanInitialTripDelayMax = 300.0f;
        private const float UlgrimAyanBarRoundDelayMin = 45.0f;
        private const float UlgrimAyanBarRoundDelayMax = 90.0f;
        private const float UlgrimAyanHomeDelayMin = 600.0f;
        private const float UlgrimAyanHomeDelayMax = 1200.0f;

        private const uint UlgrimTheUnpleasantWcid = 6873;
        private const uint AyanBarkeeperWcid = 6856;
        private const uint GenericKegWcid = 157;
        private const uint BeerKegWcid = 8377;

        private static readonly (uint Cell, float X, float Y, float Z, float RotationW, float RotationZ)[] UlgrimAyanBarRoute =
        {
            (0x1134001E, 78.883987f, 141.738052f, 42.005501f, -0.029212f, -0.999573f),
            (0x1134001E, 83.818565f, 141.598587f, 42.005501f,  0.997190f,  0.074917f),
            (0x11340138, 83.818764f, 151.815582f, 42.005501f,  1.000000f, -0.000013f),
            (0x11340138, 84.540955f, 154.150040f, 42.005501f,  0.988769f, -0.149451f),
            (0x11340138, 84.268410f, 155.953644f, 42.005501f,  0.997190f,  0.074917f),
        };

        private static readonly MotionCommand[] TownAmbientMotions =
        {
            MotionCommand.Wave,
            MotionCommand.Nod,
            MotionCommand.Shrug,
            MotionCommand.BowDeep,
            MotionCommand.ClapHands,
        };

        private static readonly string[] TownAmbientLeadIns =
        {
            "Mm.",
            "Funny thing, %n...",
            "Between us, %n...",
            "You notice it too, %n?",
            "I keep thinking...",
            "Best not say too much, but...",
            "Another day in %t.",
            "Some things never change, %n.",
        };

        private static readonly string[] TownAmbientObservations =
        {
            "the road has felt busier than usual.",
            "travelers are carrying fewer answers and more questions.",
            "someone has been asking after old ruins again.",
            "trade is good, if you do not ask where it came from.",
            "the guards have been watching the horizon.",
            "there was strange weather beyond the hills.",
            "the regulars have gone quiet about something.",
            "a courier passed through in an awful hurry.",
            "prices rise whenever rumors arrive before caravans.",
            "I heard a name I had not heard in years.",
            "the lamps burned late last night.",
            "folk keep glancing toward the road, then pretending they were not.",
        };

        private static readonly string[] TownAmbientFamiliarObservations =
        {
            "it is the same business we spoke of before.",
            "your hunch may have been right after all.",
            "I asked around. Nobody admits knowing anything.",
            "the story changed twice before noon.",
            "keep that earlier warning between us.",
            "I have learned enough to stop asking loudly.",
            "we should compare notes when fewer ears are nearby.",
            "someone remembered our last conversation.",
        };
        private static readonly string[] UlgrimAyanLines =
        {
            "This is medicinal. The disease is sobriety.",
            "Ayan has two kinds of people: those buying, and those buying my next drink.",
            "I was going to save Dereth today, but then this mug needed supervision.",
            "The barkeep says I have a tab. I say I have a legacy.",
            "Never trust a Virindi that refuses a drink. Or one that accepts it.",
            "I am not hiding in the tavern. I am conducting liquid research.",
        };
        private static readonly string[] TownAmbientReplies =
        {
            "So it goes.",
            "I had noticed the same.",
            "You may be right about that.",
            "We will see before sundown.",
            "That is worth remembering.",
        };

        private void TownAmbientTick(double currentUnixTime)
        {
            if (!IsTownAmbientEligible(out var townName))
                return;

            var aiMode = (TownNpcAiMode)(GetProperty(PropertyInt.TownNpcAiOverride) ?? (int)TownNpcAiMode.Auto);
            if (!Enum.IsDefined(typeof(TownNpcAiMode), aiMode))
                aiMode = TownNpcAiMode.Auto;

            if (!PropertyManager.GetBool("town_ambient_npcs_enabled").Item || aiMode == TownNpcAiMode.Disabled)
            {
                if ((townAmbientAwayFromHome || ulgrimAyanAtBar) && !IsMoving && !IsTurning)
                {
                    ReturnTownAmbientHome();
                    townAmbientAwayFromHome = false;
                    ulgrimAyanAtBar = false;
                }
                return;
            }

            if (IsAyanUlgrim(townName))
            {
                AyanUlgrimTick(currentUnixTime);
                return;
            }

            if (aiMode == TownNpcAiMode.AuthoredOnly)
                return;

            // Stagger the first action so a freshly loaded town does not speak in unison.
            if (nextTownAmbientActionTime <= 0)
            {
                ScheduleNextTownAmbientAction(currentUnixTime);
                return;
            }

            if (currentUnixTime < nextTownAmbientActionTime || IsBusy || EmoteManager.IsBusy || IsMoving || IsTurning)
                return;

            var nearbyObjects = CurrentLandblock.GetWorldObjectsForLocalQuery();
            RememberNearbyVisitor(nearbyObjects);

            if (!string.IsNullOrEmpty(pendingTownAmbientReply))
            {
                if (aiMode == TownNpcAiMode.Social && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.30f)
                    SpeakTownAmbient(pendingTownAmbientReply);
                pendingTownAmbientReply = null;
                PlayTownAmbientMotion(MotionCommand.Nod);
                ScheduleNextTownAmbientAction(currentUnixTime);
                return;
            }

            if (townAmbientAwayFromHome)
            {
                ReturnTownAmbientHome();
                townAmbientAwayFromHome = false;
                ScheduleNextTownAmbientAction(currentUnixTime);
                return;
            }

            var nearbyNpc = FindNearbyTownNpc(nearbyObjects);
            var actionRoll = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (nearbyNpc != null && actionRoll < 0.55f)
                ConverseWithTownNpc(nearbyNpc, townName, aiMode, currentUnixTime);
            else if (CanTownAmbientJog(aiMode) && actionRoll < 0.72f)
                StartTownAmbientJog(currentUnixTime);
            else if (actionRoll < 0.90f)
            {
                PlayTownAmbientMotion();
                ScheduleNextTownAmbientAction(currentUnixTime);
            }
            else
            {
                GreetRememberedVisitor(townName, aiMode);
                ScheduleNextTownAmbientAction(currentUnixTime);
            }
        }

        private bool IsAyanUlgrim(string townName)
        {
            return WeenieClassId == UlgrimTheUnpleasantWcid
                && townName.Contains("Ayan", StringComparison.OrdinalIgnoreCase);
        }

        private void AyanUlgrimTick(double currentUnixTime)
        {
            if (AdvanceUlgrimAyanRoute(currentUnixTime))
                return;

            if (nextUlgrimAyanActionTime <= 0)
            {
                nextUlgrimAyanActionTime = currentUnixTime + ThreadSafeRandom.Next(UlgrimAyanInitialTripDelayMin, UlgrimAyanInitialTripDelayMax);
                return;
            }

            if (currentUnixTime < nextUlgrimAyanActionTime || IsBusy || EmoteManager.IsBusy || IsMoving || IsTurning)
                return;

            if (!ulgrimAyanAtBar)
            {
                if (TryMoveUlgrimToAyanBar())
                    return;

                // If the tavern is not loaded or reachable, Ulgrim remains at his proper spawn.
                PerformUlgrimDrinkingRound();
                nextUlgrimAyanActionTime = currentUnixTime + ThreadSafeRandom.Next(45.0f, 90.0f);
                return;
            }

            if (ulgrimAyanRoundsRemaining-- > 0)
            {
                PerformUlgrimDrinkingRound();
                nextUlgrimAyanActionTime = currentUnixTime + ThreadSafeRandom.Next(UlgrimAyanBarRoundDelayMin, UlgrimAyanBarRoundDelayMax);
                return;
            }

            StartUlgrimAyanRoute(false);
        }

        private bool TryMoveUlgrimToAyanBar()
        {
            var objects = CurrentLandblock.GetWorldObjectsForLocalQuery();
            var barkeeper = objects
                .OfType<Creature>()
                .FirstOrDefault(candidate => candidate.WeenieClassId == AyanBarkeeperWcid && candidate.Location != null);

            // The tavern keg is the preferred anchor. The barkeeper remains a reliable fallback.
            var keg = objects
                .Where(candidate => candidate.Location != null
                    && (candidate.WeenieClassId == GenericKegWcid
                        || candidate.WeenieClassId == BeerKegWcid
                        || (candidate.Name?.Contains("keg", StringComparison.OrdinalIgnoreCase) ?? false)))
                .OrderBy(candidate => barkeeper?.Location == null ? 0 : candidate.Location.Distance2DSquared(barkeeper.Location))
                .FirstOrDefault();
            var barAnchor = keg ?? (WorldObject)barkeeper;

            if (barAnchor?.Location == null || barAnchor.Location.Landblock != Location.Landblock)
                return false;

            StartUlgrimAyanRoute(true);
            return true;
        }

        private void StartUlgrimAyanRoute(bool toBar)
        {
            // Ambient travel owns movement for the entire trip. Clear any generic
            // home/path action first so it cannot force-home Ulgrim mid-route.
            if (HasPendingMovement)
                CancelMoveTo(WeenieError.ActionCancelled);
            if (IsAwake || MonsterState != State.Idle || IsRouting || IsWandering || IsMovingToHome)
                Sleep();

            ulgrimAyanRoute = new Queue<Position>(UlgrimAyanBarRoute.Length);
            ulgrimAyanRouteToBar = toBar;

            if (toBar)
            {
                foreach (var waypoint in UlgrimAyanBarRoute)
                    ulgrimAyanRoute.Enqueue(CreateUlgrimAyanWaypoint(waypoint));
            }
            else
            {
                // Follow the recorded route back through the doorway, then finish at the
                // live home position. This avoids handing off to a second direct MoveTo
                // that can cut across the tavern wall or visibly snap Ulgrim home.
                for (var i = UlgrimAyanBarRoute.Length - 1; i > 0; i--)
                    ulgrimAyanRoute.Enqueue(CreateUlgrimAyanWaypoint(UlgrimAyanBarRoute[i]));

                var home = GetPosition(PositionType.Home);
                ulgrimAyanRoute.Enqueue(home != null
                    ? new Position(home)
                    : CreateUlgrimAyanWaypoint(UlgrimAyanBarRoute[0]));
            }
        }

        private bool AdvanceUlgrimAyanRoute(double currentUnixTime)
        {
            if (ulgrimAyanRoute == null)
                return false;

            if (IsBusy || EmoteManager.IsBusy || HasPendingMovement)
                return true;

            if (ulgrimAyanRoute.Count > 0)
            {
                var destination = ulgrimAyanRoute.Dequeue();
                var useRecordedFacing = ulgrimAyanRouteToBar && ulgrimAyanRoute.Count == 0;
                MoveTo(destination, GetRunRate(), useRecordedFacing, 0.5f);
                IsMoving = true;
                return true;
            }

            ulgrimAyanRoute = null;
            if (ulgrimAyanRouteToBar)
            {
                ulgrimAyanAtBar = true;
                ulgrimAyanRoundsRemaining = ThreadSafeRandom.Next(6, 11);
                nextUlgrimAyanActionTime = currentUnixTime + ThreadSafeRandom.Next(UlgrimAyanBarRoundDelayMin, UlgrimAyanBarRoundDelayMax);
            }
            else
            {
                ulgrimAyanAtBar = false;
                nextUlgrimAyanActionTime = currentUnixTime + ThreadSafeRandom.Next(UlgrimAyanHomeDelayMin, UlgrimAyanHomeDelayMax);
            }
            return true;
        }

        private static Position CreateUlgrimAyanWaypoint((uint Cell, float X, float Y, float Z, float RotationW, float RotationZ) waypoint)
        {
            return new Position(waypoint.Cell, waypoint.X, waypoint.Y, waypoint.Z, 0, 0, waypoint.RotationZ, waypoint.RotationW);
        }

        private void PerformUlgrimDrinkingRound()
        {
            // Use the soul emote drink motion; the consumable Drink motion can leave mug-wielding NPCs held in the raised pose.
            var motion = ThreadSafeRandom.Next(0.0f, 1.0f) < 0.8f ? MotionCommand.MimeDrink : MotionCommand.Slouch;
            PlayTownAmbientMotion(motion);

            if (TownAmbientSpeechEnabled && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.22f)
                SpeakTownAmbient(UlgrimAyanLines[ThreadSafeRandom.Next(0, UlgrimAyanLines.Length - 1)]);
        }
        private bool IsTownAmbientEligible(out string townName)
        {
            townName = null;
            if (!IsNPC || IsDead || Location == null || CurrentLandblock == null || CurrentLandblock.IsDungeon || PhysicsObj == null)
                return false;

            townName = VendorTownTier.GetTownName((int)Location.LandblockX, (int)Location.LandblockY);
            return !string.IsNullOrWhiteSpace(townName);
        }

        private Creature FindNearbyTownNpc(IEnumerable<WorldObject> nearbyObjects)
        {
            var radiusSq = TownAmbientSocialRadius * TownAmbientSocialRadius;
            Creature nearest = null;
            var nearestDistanceSq = radiusSq;

            foreach (var candidate in nearbyObjects)
            {
                if (candidate is not Creature creature || creature == this || !creature.IsNPC || creature.IsDead || creature.Location == null)
                    continue;

                var distanceSq = Location.Distance2DSquared(creature.Location);
                if (distanceSq > nearestDistanceSq)
                    continue;

                nearest = creature;
                nearestDistanceSq = distanceSq;
            }

            return nearest;
        }

        private void ConverseWithTownNpc(Creature other, string townName, TownNpcAiMode aiMode, double currentUnixTime)
        {
            townAmbientFamiliarity.TryGetValue(other.Guid.Full, out var familiarity);
            familiarity = Math.Min(100, familiarity + 1);
            townAmbientFamiliarity[other.Guid.Full] = familiarity;

            // Most conversations are only suggested by body language. Social overrides speak more often.
            var speechChance = TownAmbientSpeechEnabled ? aiMode switch
            {
                TownNpcAiMode.Quiet => 0.0f,
                TownNpcAiMode.Social => 0.22f,
                _ => 0.08f,
            } : 0.0f;

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < speechChance)
            {
                SpeakTownAmbient(BuildTownAmbientDialogue(other, townName, familiarity));

                if (aiMode == TownNpcAiMode.Social && string.IsNullOrEmpty(other.pendingTownAmbientReply)
                    && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.25f)
                {
                    other.pendingTownAmbientReply = TownAmbientReplies[ThreadSafeRandom.Next(0, TownAmbientReplies.Length - 1)];
                    other.nextTownAmbientActionTime = currentUnixTime + ThreadSafeRandom.Next(8.0f, 16.0f);
                }
            }

            PlayTownAmbientMotion(familiarity >= 3 ? MotionCommand.Nod : MotionCommand.Wave);
            if (!other.IsMoving && !other.IsTurning && !other.IsBusy && !other.EmoteManager.IsBusy)
                other.PlayTownAmbientMotion(ThreadSafeRandom.Next(0.0f, 1.0f) < 0.75f ? MotionCommand.Nod : MotionCommand.Shrug);

            other.townAmbientFamiliarity.TryGetValue(Guid.Full, out var reciprocalFamiliarity);
            other.townAmbientFamiliarity[Guid.Full] = Math.Min(100, reciprocalFamiliarity + 1);
            ScheduleNextTownAmbientAction(currentUnixTime);
        }

        private string BuildTownAmbientDialogue(Creature other, string townName, int familiarity)
        {
            var observations = familiarity >= 3 ? TownAmbientFamiliarObservations : TownAmbientObservations;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var lead = TownAmbientLeadIns[ThreadSafeRandom.Next(0, TownAmbientLeadIns.Length - 1)]
                    .Replace("%n", other.Name)
                    .Replace("%t", townName);
                var observation = observations[ThreadSafeRandom.Next(0, observations.Length - 1)];
                var line = $"{lead} {observation}";
                if (townAmbientRecentLines.Contains(line, StringComparer.Ordinal))
                    continue;

                while (townAmbientRecentLines.Count >= 4)
                    townAmbientRecentLines.Dequeue();
                townAmbientRecentLines.Enqueue(line);
                return line;
            }

            return "Mm. Best leave it there.";
        }
        private void RememberNearbyVisitor(IEnumerable<WorldObject> nearbyObjects)
        {
            var radiusSq = TownAmbientSocialRadius * TownAmbientSocialRadius;
            Player visitor = null;
            var nearestDistanceSq = radiusSq;

            foreach (var candidate in nearbyObjects)
            {
                if (candidate is not Player player || player.Location == null)
                    continue;

                var distanceSq = Location.Distance2DSquared(player.Location);
                if (distanceSq > nearestDistanceSq)
                    continue;

                visitor = player;
                nearestDistanceSq = distanceSq;
            }

            if (visitor == null || townAmbientKnownVisitors.Contains(visitor.Name, StringComparer.OrdinalIgnoreCase))
                return;

            while (townAmbientKnownVisitors.Count >= TownAmbientMemoryLimit)
                townAmbientKnownVisitors.Dequeue();
            townAmbientKnownVisitors.Enqueue(visitor.Name);
        }

        private void GreetRememberedVisitor(string townName, TownNpcAiMode aiMode)
        {
            if (townAmbientKnownVisitors.Count == 0)
            {
                PlayTownAmbientMotion();
                return;
            }

            var visitor = townAmbientKnownVisitors.ElementAt(ThreadSafeRandom.Next(0, townAmbientKnownVisitors.Count - 1));
            if (TownAmbientSpeechEnabled && aiMode == TownNpcAiMode.Social && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.20f)
                SpeakTownAmbient($"Haven't seen {visitor} lately. Hope the road treated them kindly.");
            PlayTownAmbientMotion(MotionCommand.Wave);
        }

        private bool CanTownAmbientJog(TownNpcAiMode aiMode)
        {
            if (this is Vendor || aiMode == TownNpcAiMode.Quiet || aiMode == TownNpcAiMode.Social || aiMode == TownNpcAiMode.Stationary)
                return false;

            return GetPosition(PositionType.Home) != null
                && (aiMode == TownNpcAiMode.Runner || Guid.Full % 10 == 0);
        }

        private void StartTownAmbientJog(double currentUnixTime)
        {
            var home = GetPosition(PositionType.Home);
            var width = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
            var destination = Pathfinder.GetRandomPointWithinCircle(home, TownAmbientJogRadius, width);
            if (destination == null || destination.Landblock != home.Landblock)
            {
                ScheduleNextTownAmbientAction(currentUnixTime);
                return;
            }

            MoveTo(destination, GetRunRate(), true, 0.0f);
            townAmbientAwayFromHome = true;
            nextTownAmbientActionTime = currentUnixTime + ThreadSafeRandom.Next(18.0f, 32.0f);
        }

        private void ReturnTownAmbientHome()
        {
            var home = GetPosition(PositionType.Home);
            if (home != null && Location.Landblock == home.Landblock)
                MoveTo(home, GetRunRate(), true, 0.0f);
        }

        private void PlayTownAmbientMotion(MotionCommand motion = MotionCommand.Invalid)
        {
            if (motion == MotionCommand.Invalid)
                motion = TownAmbientMotions[ThreadSafeRandom.Next(0, TownAmbientMotions.Length - 1)];
            EnqueueBroadcastMotion(new Motion(this, motion));
        }

        private bool TownAmbientSpeechEnabled => PropertyManager.GetBool("town_ambient_npc_speech_enabled").Item;

        private void SpeakTownAmbient(string text)
        {
            if (!TownAmbientSpeechEnabled)
                return;

            EnqueueBroadcast(new GameMessageHearSpeech(text, Name, Guid.Full, ChatMessageType.Speech), LocalBroadcastRange);
        }

        private void ScheduleNextTownAmbientAction(double currentUnixTime)
        {
            var minimum = Math.Max(20.0, PropertyManager.GetDouble("town_ambient_npc_interval_min").Item);
            var maximum = Math.Max(minimum, PropertyManager.GetDouble("town_ambient_npc_interval_max").Item);
            nextTownAmbientActionTime = currentUnixTime + ThreadSafeRandom.Next((float)minimum, (float)maximum);
        }
    }
}