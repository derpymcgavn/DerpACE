using System;
using System.Collections.Concurrent;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Manages Whack Armor event: cycles armor through random palette colors on a heartbeat.
    /// </summary>
    public static class WhackArmorManager
    {
        private static readonly ConcurrentDictionary<uint, WorldObject> _trackedArmor = new ConcurrentDictionary<uint, WorldObject>();
        private static readonly Random _rng = new Random();
        private static int _heartbeatCounter = 0;

        // Palette templates to cycle through (common palette variations)
        private static readonly int[] PaletteTemplates = new[]
        {
            0x0400A0A6, // Red
            0x0400A0A7, // Blue
            0x0400A0A8, // Yellow
            0x0400A0A9, // Green
            0x0400A0AA, // Purple
            0x0400A0AB, // Orange
            0x0400A0AC, // White
            0x0400A0AD, // Black
        };

        /// <summary>
        /// Register an armor piece to cycle palettes.
        /// Called during loot generation when WhackArmor event is active.
        /// </summary>
        public static void RegisterArmorPiece(WorldObject armor)
        {
            if (armor == null) return;
            _trackedArmor.TryAdd(armor.Guid.Full, armor);
        }

        /// <summary>
        /// Unregister when armor is deleted or event ends.
        /// </summary>
        public static void UnregisterArmorPiece(WorldObject armor)
        {
            if (armor == null) return;
            _trackedArmor.TryRemove(armor.Guid.Full, out _);
        }

        /// <summary>
        /// Called from WorldManager on each game world heartbeat.
        /// Cycles palettes approximately every 0.5 seconds (based on heartbeat frequency).
        /// </summary>
        public static void Tick()
        {
            _heartbeatCounter++;

            // Only update every ~10 heartbeats (~0.5 seconds at 20 Hz) to reduce network traffic
            if (_heartbeatCounter % 10 != 0)
                return;

            // If event is off, clear tracking
            if (!ServerEvents.WhackArmor)
            {
                _trackedArmor.Clear();
                return;
            }

            // Cycle through tracked armor pieces
            var toRemove = new System.Collections.Generic.List<uint>();

            foreach (var kvp in _trackedArmor)
            {
                var armor = kvp.Value;

                // Skip if armor was destroyed or is null
                if (armor == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Pick a random palette
                var newPalette = PaletteTemplates[_rng.Next(PaletteTemplates.Length)];

                // Only update if palette actually changed
                if (armor.PaletteTemplate != newPalette)
                {
                    armor.PaletteTemplate = newPalette;

                    // Notify nearby players
                    var owner = armor.Container as Player;

                    if (owner != null)
                        owner.EnqueueBroadcast(new GameMessageUpdateObject(armor));
                    else
                        armor.EnqueueBroadcast(new GameMessageUpdateObject(armor));
                }
            }

            // Clean up dead references
            foreach (var guid in toRemove)
                _trackedArmor.TryRemove(guid, out _);
        }

        /// <summary>
        /// Clear all tracked armor (called when event ends).
        /// </summary>
        public static void Clear()
        {
            _trackedArmor.Clear();
        }
    }
}
