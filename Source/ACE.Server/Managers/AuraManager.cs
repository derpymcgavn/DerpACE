using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// DerpACE – Persistent Aura System.
    ///
    /// Auras are stored as up to three PlayScript IDs on a player's biota
    /// (PropertyInt.AuraPlayScript1/2/3).  The manager re-fires those scripts
    /// on a configurable heartbeat interval so they look like continuous
    /// looping particle effects to nearby clients.
    ///
    /// Because PlayScript is a one-shot network message the interval must be
    /// short enough that the effect visually loops.  Most enchant/aura scripts
    /// look best at 3-5 seconds.
    ///
    /// Admin commands (see AdminCommands.cs):
    ///   @aura info [playerName]          - list active aura slots
    ///   @aura set  &lt;slot&gt; &lt;scriptId&gt; [playerName]  - assign a PlayScript to a slot
    ///   @aura clear &lt;slot|all&gt; [playerName]         - remove one or all slots
    ///   @aura list                       - print every valid PlayScript id
    /// </summary>
    public static class AuraManager
    {
        private static readonly log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // ---- tuning knobs -----------------------------------------------
        /// <summary>Seconds between aura re-broadcasts per player.</summary>
        public static double AuraTickInterval  = 4.0;

        /// <summary>Speed/scale factor forwarded to GameMessageScript.</summary>
        public static float  AuraPlaybackSpeed = 1.0f;
        // -----------------------------------------------------------------

        /// <summary>The three biota property slots used by the aura system.</summary>
        public static readonly PropertyInt[] AuraSlots = new[]
        {
            PropertyInt.AuraPlayScript1,
            PropertyInt.AuraPlayScript2,
            PropertyInt.AuraPlayScript3,
        };

        private static readonly HashSet<uint> ValidPlayScripts = Enum.GetValues<PlayScript>()
            .Select(value => (uint)value)
            .ToHashSet();

        // Landblock groups heartbeat players concurrently.
        private static readonly ConcurrentDictionary<uint, double> _lastTick = new ConcurrentDictionary<uint, double>();

        // =================================================================
        //  Core tick — call from Player.Heartbeat (~every 5 s)
        // =================================================================

        /// <summary>
        /// Fires stored aura scripts when the per-player cooldown has elapsed.
        /// Designed to be called from <see cref="Player"/>.Heartbeat.
        /// </summary>
        public static void Tick(Player player, double currentUnixTime)
        {
            if (player == null) return;

            if (_lastTick.TryGetValue(player.Guid.Full, out var last) &&
                currentUnixTime - last < AuraTickInterval)
                return;

            _lastTick[player.Guid.Full] = currentUnixTime;
            FireAuras(player);
        }

        /// <summary>
        /// Immediately broadcasts all active aura scripts on <paramref name="player"/>.
        /// Call on enter-world so effects appear the instant the player loads in.
        /// </summary>
        public static void FireAuras(Player player)
        {
            if (player == null) return;

            foreach (var slot in AuraSlots)
            {
                var raw = player.GetProperty(slot);
                if (raw == null || raw.Value == 0) continue;

                if (!ValidPlayScripts.Contains((uint)raw.Value))
                {
                    log.Warn($"AuraManager: {player.Name} slot {slot} has invalid PlayScript id {raw.Value} — clearing.");
                    player.RemoveProperty(slot);
                    continue;
                }

                player.EnqueueBroadcast(new GameMessageScript(
                    player.Guid,
                    (PlayScript)(uint)raw.Value,
                    AuraPlaybackSpeed));
            }
        }

        // =================================================================
        //  Admin helpers (called from AdminCommands HandleAura)
        // =================================================================

        /// <summary>Returns a formatted summary of all active aura slots.</summary>
        public static string GetAuraInfo(Player player)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Auras on {player.Name}:");
            bool any = false;

            for (int i = 0; i < AuraSlots.Length; i++)
            {
                var raw = player.GetProperty(AuraSlots[i]);
                if (raw == null || raw.Value == 0) continue;
                sb.AppendLine($"  Slot {i + 1}: {(PlayScript)(uint)raw.Value}  (id {raw.Value})");
                any = true;
            }

            if (!any) sb.AppendLine("  (none active)");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Sets aura <paramref name="slot"/> (1-3) to <paramref name="scriptId"/>.
        /// Returns an error string on failure, or <c>null</c> on success.
        /// </summary>
        public static string SetAura(Player player, int slot, uint scriptId)
        {
            if (slot < 1 || slot > AuraSlots.Length)
                return $"Slot must be 1-{AuraSlots.Length}.";

            if (!ValidPlayScripts.Contains(scriptId) || scriptId == 0)
                return $"PlayScript id {scriptId} is not valid. Use '@aura list' to see all options.";

            player.SetProperty(AuraSlots[slot - 1], (int)scriptId);
            player.SaveBiotaToDatabase();

            // Show the new effect immediately without waiting for the next tick.
            FireAuras(player);
            return null;
        }

        /// <summary>
        /// Clears aura slot 1-3, or ALL slots when <paramref name="slot"/> == 0.
        /// Returns an error string on failure, or <c>null</c> on success.
        /// </summary>
        public static string ClearAura(Player player, int slot)
        {
            if (slot == 0)
            {
                foreach (var s in AuraSlots)
                    player.RemoveProperty(s);
                player.SaveBiotaToDatabase();
                return null;
            }

            if (slot < 1 || slot > AuraSlots.Length)
                return $"Slot must be 1-{AuraSlots.Length}, or 0 to clear all slots.";

            player.RemoveProperty(AuraSlots[slot - 1]);
            player.SaveBiotaToDatabase();
            return null;
        }

        /// <summary>Returns a full numbered list of every valid PlayScript name and id.</summary>
        public static string GetScriptList()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Valid PlayScript ids (pass the numeric id to '@aura set'):");

            foreach (PlayScript ps in Enum.GetValues(typeof(PlayScript)))
            {
                if (ps == PlayScript.Invalid) continue;
                sb.AppendLine($"  {(uint)ps,4}  {ps}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Removes per-player tick state when a player logs out.</summary>
        public static void OnPlayerLogout(Player player)
        {
            if (player != null)
                _lastTick.TryRemove(player.Guid.Full, out _);
        }
    }
}
