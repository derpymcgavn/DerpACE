using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity
{
    /// <summary>
    /// DerpACE: Unarmed combat combo system for punches and kicks.
    /// Tracks attack sequences and applies bonus effects when combos are completed.
    /// Features a discovery system where each player must unlock their own random combo assignments.
    /// </summary>
    public class UnarmedComboSystem
    {
        private readonly Player _player;
        private readonly List<AttackType> _comboChain = new List<AttackType>();
        private DateTime _lastAttackTime = DateTime.MinValue;
        private const double ComboWindowSeconds = 3.0; // Time window to continue combo
        private uint _lastTargetGuid = 0;

        // Player's discovered combos - stored as PropertyInt flags
        private const int MaxCombos = 14; // Total number of combo types

        public UnarmedComboSystem(Player player)
        {
            _player = player;
        }

        /// <summary>
        /// Returns a dictionary of all possible combos and their patterns.
        /// This is the master list that gets randomized per player.
        /// </summary>
        private static Dictionary<ComboType, ComboDefinition> GetMasterComboList()
        {
            return new Dictionary<ComboType, ComboDefinition>
            {
                // 3-hit combos
                { ComboType.OneTwoKick, new ComboDefinition
                    {
                        Name = "One-Two Kick",
                        Pattern = new[] { AttackType.Punch, AttackType.Punch, AttackType.Kick },
                        DamageMultiplier = 1.5f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "A classic boxing combination - two quick jabs followed by a powerful kick!"
                    }
                },
                { ComboType.RisingDragon, new ComboDefinition
                    {
                        Name = "Rising Dragon",
                        Pattern = new[] { AttackType.Kick, AttackType.Punch, AttackType.Kick },
                        DamageMultiplier = 1.5f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "An ancient technique that flows like water, alternating between high and low strikes."
                    }
                },
                { ComboType.TriplePunch, new ComboDefinition
                    {
                        Name = "Triple Punch",
                        Pattern = new[] { AttackType.Punch, AttackType.Punch, AttackType.Punch },
                        DamageMultiplier = 1.4f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "Speed over strength - a rapid flurry of three successive punches."
                    }
                },
                { ComboType.TripleKick, new ComboDefinition
                    {
                        Name = "Triple Kick",
                        Pattern = new[] { AttackType.Kick, AttackType.Kick, AttackType.Kick },
                        DamageMultiplier = 1.5f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "Pure power unleashed - three devastating kicks in succession."
                    }
                },
                { ComboType.SweepingStrikes, new ComboDefinition
                    {
                        Name = "Sweeping Strikes",
                        Pattern = new[] { AttackType.Kick, AttackType.Kick, AttackType.Punch },
                        DamageMultiplier = 1.45f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "Sweep low, strike high - a balanced approach to overwhelming your foe."
                    }
                },
                { ComboType.BatteringRam, new ComboDefinition
                    {
                        Name = "Battering Ram",
                        Pattern = new[] { AttackType.Punch, AttackType.Kick, AttackType.Punch },
                        DamageMultiplier = 1.45f,
                        BonusEffect = ComboEffect.None,
                        Tier = 1,
                        FlavorText = "Relentless pressure - never give your enemy a moment to breathe."
                    }
                },

                // 4-hit combos
                { ComboType.Crusher, new ComboDefinition
                    {
                        Name = "Crusher",
                        Pattern = new[] { AttackType.Punch, AttackType.Punch, AttackType.Kick, AttackType.Kick },
                        DamageMultiplier = 1.8f,
                        BonusEffect = ComboEffect.Knockback,
                        Tier = 2,
                        FlavorText = "Build momentum with precision strikes, then unleash crushing force!"
                    }
                },
                { ComboType.ViperStrike, new ComboDefinition
                    {
                        Name = "Viper Strike",
                        Pattern = new[] { AttackType.Kick, AttackType.Punch, AttackType.Punch, AttackType.Kick },
                        DamageMultiplier = 1.75f,
                        BonusEffect = ComboEffect.ArmorPierce,
                        Tier = 2,
                        FlavorText = "Like a serpent finding weakness in its prey, these strikes pierce through defenses."
                    }
                },
                { ComboType.ThunderKick, new ComboDefinition
                    {
                        Name = "Thunder Kick",
                        Pattern = new[] { AttackType.Kick, AttackType.Kick, AttackType.Kick, AttackType.Kick },
                        DamageMultiplier = 2.0f,
                        BonusEffect = ComboEffect.CriticalBoost,
                        Tier = 2,
                        FlavorText = "Four kicks that echo like thunder - each more devastating than the last!"
                    }
                },
                { ComboType.RapidFists, new ComboDefinition
                    {
                        Name = "Rapid Fists",
                        Pattern = new[] { AttackType.Punch, AttackType.Punch, AttackType.Punch, AttackType.Punch },
                        DamageMultiplier = 1.6f,
                        BonusEffect = ComboEffect.AttackSpeedBoost,
                        Tier = 2,
                        FlavorText = "An unstoppable barrage - your fists become a blur of motion!"
                    }
                },

                // 5-hit combos
                { ComboType.Demolisher, new ComboDefinition
                    {
                        Name = "DEMOLISHER",
                        Pattern = new[] { AttackType.Punch, AttackType.Punch, AttackType.Punch, AttackType.Kick, AttackType.Kick },
                        DamageMultiplier = 2.5f,
                        BonusEffect = ComboEffect.Stun,
                        Tier = 3,
                        FlavorText = "The ultimate finishing move! Three punches soften them up, two kicks END them!"
                    }
                },
                { ComboType.DragonsFury, new ComboDefinition
                    {
                        Name = "DRAGON'S FURY",
                        Pattern = new[] { AttackType.Kick, AttackType.Punch, AttackType.Kick, AttackType.Punch, AttackType.Kick },
                        DamageMultiplier = 2.2f,
                        BonusEffect = ComboEffect.ElementalSurge,
                        Tier = 3,
                        FlavorText = "Channel the rage of ancient dragons! Elemental fury flows through every strike!"
                    }
                },
                { ComboType.Whirlwind, new ComboDefinition
                    {
                        Name = "WHIRLWIND",
                        Pattern = new[] { AttackType.Kick, AttackType.Kick, AttackType.Punch, AttackType.Punch, AttackType.Kick },
                        DamageMultiplier = 2.0f,
                        BonusEffect = ComboEffect.Cleave,
                        Tier = 3,
                        FlavorText = "Become a spinning tempest of destruction, striking all who dare stand near!"
                    }
                }
            };
        }

        /// <summary>
        /// Gets or initializes the player's unique combo pattern mapping.
        /// Each player gets a randomized assignment of patterns to combo types.
        /// Stored as a compact PropertyInt.
        /// </summary>
        private Dictionary<string, ComboType> GetPlayerComboMapping()
        {
            var mapping = new Dictionary<string, ComboType>();
            var masterList = GetMasterComboList();

            // Check if player already has a mapping saved
            var savedMapping = _player.GetProperty(PropertyInt.UnarmedComboSeed);

            if (savedMapping == null)
            {
                // Generate new random mapping using player GUID as seed for consistency
                var random = new Random((int)_player.Guid.Full);
                var availableTypes = masterList.Keys.ToList();

                // Shuffle the combo types
                for (int i = availableTypes.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    var temp = availableTypes[i];
                    availableTypes[i] = availableTypes[j];
                    availableTypes[j] = temp;
                }

                // Assign shuffled types to patterns
                int index = 0;
                foreach (var combo in masterList.OrderBy(c => c.Value.Tier).ThenBy(c => c.Key))
                {
                    var pattern = string.Join("", combo.Value.Pattern.Select(a => a == AttackType.Punch ? "P" : "K"));
                    mapping[pattern] = availableTypes[index];
                    index++;
                }

                // Save the seed so it remains consistent
                _player.SetProperty(PropertyInt.UnarmedComboSeed, (int)_player.Guid.Full);
            }
            else
            {
                // Regenerate the same mapping from the saved seed
                var random = new Random(savedMapping.Value);
                var availableTypes = masterList.Keys.ToList();

                for (int i = availableTypes.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    var temp = availableTypes[i];
                    availableTypes[i] = availableTypes[j];
                    availableTypes[j] = temp;
                }

                int index = 0;
                foreach (var combo in masterList.OrderBy(c => c.Value.Tier).ThenBy(c => c.Key))
                {
                    var pattern = string.Join("", combo.Value.Pattern.Select(a => a == AttackType.Punch ? "P" : "K"));
                    mapping[pattern] = availableTypes[index];
                    index++;
                }
            }

            return mapping;
        }

        /// <summary>
        /// Checks if the player has discovered a specific combo.
        /// Uses PropertyInt64 as a bitfield for discovered combos.
        /// </summary>
        private bool HasDiscoveredCombo(ComboType comboType)
        {
            var discovered = _player.GetProperty(PropertyInt64.UnarmedCombosDiscovered) ?? 0;
            int bit = (int)comboType;
            return (discovered & (1L << bit)) != 0;
        }

        /// <summary>
        /// Marks a combo as discovered for the player.
        /// </summary>
        private void DiscoverCombo(ComboType comboType)
        {
            var discovered = _player.GetProperty(PropertyInt64.UnarmedCombosDiscovered) ?? 0;
            int bit = (int)comboType;
            discovered |= (1L << bit);
            _player.SetProperty(PropertyInt64.UnarmedCombosDiscovered, discovered);
        }

        /// <summary>
        /// Records an attack and checks for combo completion.
        /// Returns the combo bonus multiplier (1.0 for no combo).
        /// </summary>
        public ComboResult RecordAttack(AttackType attackType, uint targetGuid)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastAttack = (now - _lastAttackTime).TotalSeconds;

            // Reset combo if too much time passed or target changed
            if (timeSinceLastAttack > ComboWindowSeconds || targetGuid != _lastTargetGuid)
            {
                ResetCombo();
            }

            _lastAttackTime = now;
            _lastTargetGuid = targetGuid;

            // Only track punch and kick attacks for combos
            if (attackType != AttackType.Punch && attackType != AttackType.Kick)
            {
                ResetCombo();
                return new ComboResult { DamageMultiplier = 1.0f };
            }

            _comboChain.Add(attackType);

            // Check for completed combos (max 5 hit combo)
            if (_comboChain.Count >= 5)
            {
                var result = CheckForCombo();
                if (result.ComboType != ComboType.None)
                {
                    ResetCombo();
                    return result;
                }
                // Keep only last 4 hits if no combo detected
                _comboChain.RemoveAt(0);
            }
            else if (_comboChain.Count >= 4)
            {
                // Check for 4-hit combos
                var result = CheckForCombo();
                if (result.ComboType != ComboType.None)
                {
                    ResetCombo();
                    return result;
                }
            }
            else if (_comboChain.Count >= 3)
            {
                // Check for 3-hit combos
                var result = CheckForCombo();
                if (result.ComboType != ComboType.None)
                {
                    ResetCombo();
                    return result;
                }
            }

            return new ComboResult { DamageMultiplier = 1.0f, HitCount = _comboChain.Count };
        }

        /// <summary>
        /// Checks the current combo chain for recognized patterns.
        /// </summary>
        private ComboResult CheckForCombo()
        {
            var chain = _comboChain;
            var count = chain.Count;
            var masterList = GetMasterComboList();
            var playerMapping = GetPlayerComboMapping();

            // Try to match patterns starting from the longest
            for (int length = Math.Min(5, count); length >= 3; length--)
            {
                var startIndex = count - length;
                var pattern = string.Join("", chain.Skip(startIndex).Select(a => a == AttackType.Punch ? "P" : "K"));

                if (playerMapping.TryGetValue(pattern, out var comboType))
                {
                    var comboDef = masterList[comboType];
                    bool isNewDiscovery = !HasDiscoveredCombo(comboType);

                    if (isNewDiscovery)
                    {
                        DiscoverCombo(comboType);
                    }

                    return new ComboResult
                    {
                        ComboType = comboType,
                        DamageMultiplier = comboDef.DamageMultiplier,
                        Message = GetComboMessage(comboDef, isNewDiscovery),
                        BonusEffect = comboDef.BonusEffect,
                        IsNewDiscovery = isNewDiscovery,
                        FlavorText = comboDef.FlavorText,
                        Tier = comboDef.Tier
                    };
                }
            }

            return new ComboResult { DamageMultiplier = 1.0f };
        }

        /// <summary>
        /// Generates a verbose combo message based on tier and discovery status.
        /// </summary>
        private string GetComboMessage(ComboDefinition combo, bool isNewDiscovery)
        {
            if (isNewDiscovery)
            {
                return combo.Tier switch
                {
                    3 => $"═══════════════════════════\n★★★ LEGENDARY COMBO DISCOVERED! ★★★\n『 {combo.Name.ToUpper()} 』\n═══════════════════════════",
                    2 => $"━━━━━━━━━━━━━━━━━━━━━━\n⚡⚡ ADVANCED COMBO UNLOCKED! ⚡⚡\n『 {combo.Name} 』\n━━━━━━━━━━━━━━━━━━━━━━",
                    _ => $"✦✦✦ NEW COMBO LEARNED! ✦✦✦\n『 {combo.Name} 』"
                };
            }
            else
            {
                return combo.Tier switch
                {
                    3 => $"★★★ {combo.Name.ToUpper()} ★★★",
                    2 => $"⚡⚡ {combo.Name.ToUpper()} ⚡⚡",
                    _ => $"✦ {combo.Name} ✦"
                };
            }
        }

        /// <summary>
        /// Helper to check if the last N attacks match a pattern.
        /// </summary>
        private bool IsPattern(List<AttackType> chain, int startIndex, params AttackType[] pattern)
        {
            if (startIndex < 0 || startIndex + pattern.Length > chain.Count)
                return false;

            for (int i = 0; i < pattern.Length; i++)
            {
                if (chain[startIndex + i] != pattern[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Resets the combo chain.
        /// </summary>
        public void ResetCombo()
        {
            _comboChain.Clear();
            _lastTargetGuid = 0;
        }

        /// <summary>
        /// Gets the current combo count.
        /// </summary>
        public int GetComboCount() => _comboChain.Count;

        /// <summary>
        /// Gets the current combo chain as a string for display.
        /// </summary>
        public string GetComboChainDisplay()
        {
            if (_comboChain.Count == 0)
                return "";

            var display = "";
            foreach (var attack in _comboChain)
            {
                display += attack == AttackType.Punch ? "P" : "K";
            }
            return $"[{display}]";
        }

        /// <summary>
        /// Gets a list of all discovered combos for the player.
        /// </summary>
        public List<string> GetDiscoveredCombos()
        {
            var discovered = new List<string>();
            var masterList = GetMasterComboList();

            foreach (var combo in masterList)
            {
                if (HasDiscoveredCombo(combo.Key))
                {
                    var pattern = string.Join("-", combo.Value.Pattern.Select(a => a == AttackType.Punch ? "P" : "K"));
                    discovered.Add($"{combo.Value.Name} ({pattern}): x{combo.Value.DamageMultiplier:F1} damage");
                }
            }

            return discovered;
        }

        /// <summary>
        /// Gets total number of discovered combos.
        /// </summary>
        public int GetDiscoveredComboCount()
        {
            var discovered = _player.GetProperty(PropertyInt64.UnarmedCombosDiscovered) ?? 0;
            int count = 0;
            for (int i = 0; i < MaxCombos; i++)
            {
                if ((discovered & (1L << i)) != 0)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Definition of a combo with all its properties.
    /// </summary>
    public class ComboDefinition
    {
        public string Name { get; set; }
        public AttackType[] Pattern { get; set; }
        public float DamageMultiplier { get; set; }
        public ComboEffect BonusEffect { get; set; }
        public int Tier { get; set; } // 1=Basic, 2=Advanced, 3=Master
        public string FlavorText { get; set; }
    }

    /// <summary>
    /// Result of a combo check.
    /// </summary>
    public class ComboResult
    {
        public ComboType ComboType { get; set; } = ComboType.None;
        public float DamageMultiplier { get; set; } = 1.0f;
        public string Message { get; set; } = "";
        public ComboEffect BonusEffect { get; set; } = ComboEffect.None;
        public int HitCount { get; set; } = 0;
        public bool IsNewDiscovery { get; set; } = false;
        public string FlavorText { get; set; } = "";
        public int Tier { get; set; } = 0;
    }

    /// <summary>
    /// Types of combos.
    /// </summary>
    public enum ComboType
    {
        None = 0,
        // 3-hit combos
        OneTwoKick = 1,
        RisingDragon = 2,
        TriplePunch = 3,
        TripleKick = 4,
        SweepingStrikes = 5,
        BatteringRam = 6,
        // 4-hit combos
        Crusher = 7,
        ViperStrike = 8,
        ThunderKick = 9,
        RapidFists = 10,
        // 5-hit combos
        Demolisher = 11,
        DragonsFury = 12,
        Whirlwind = 13
    }

    /// <summary>
    /// Special effects that can be applied by combos.
    /// </summary>
    public enum ComboEffect
    {
        None,
        Stun,
        ElementalSurge,
        Cleave,
        Knockback,
        ArmorPierce,
        CriticalBoost,
        AttackSpeedBoost
    }
}
