using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: Universal Infinite Ammunition (WCID 2000600).
    ///
    /// One ammo item that works with every missile weapon — bow, crossbow, or atlatl.
    /// Damage scales to match the launcher type at fire time, always landing 1 point
    /// below the corresponding Deadly Prismatic variant:
    ///
    ///   * Bow    -> 13 (vs. Deadly Prismatic Arrow 14)
    ///   * Crossbow -> 14 (vs. Deadly Prismatic Quarrel 15)
    ///   * Atlatl -> 13 (vs. Deadly Prismatic Atlatl Dart 14)
    ///
    /// Variance matches Deadly Prismatic (0.20). DamageType is Base, so it inherits
    /// the launcher's damage type (same trick the real prismatic arrows use).
    ///
    /// AmmoType is left unset while the item sits in inventory so it does not appraise as
    /// "for use with bows". SyncToLauncher() stamps the matching AmmoType only once the ammo
    /// is equipped to a launcher, so the AC client accepts it with any launcher family.
    /// UnlimitedUse is forced true so the stack never depletes.
    /// </summary>
    public class UniversalAmmunition : Ammunition
    {
        public const uint UniversalWeenieClassId = 2000600;

        // Tuned 1 below Deadly Prismatic for each launcher family.
        private const int BowMaxDamage      = 13;
        private const int CrossbowMaxDamage = 14;
        private const int AtlatlMaxDamage   = 13;
        private const float PrismaticVariance = 0.20f;

        // Fallback when GetBaseDamage is called on the item itself (no launcher context).
        private const int FallbackMaxDamage = 13;

        public UniversalAmmunition(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            ApplyUniversalDefaults();
        }

        public UniversalAmmunition(Biota biota) : base(biota)
        {
            ApplyUniversalDefaults();
        }

        private void ApplyUniversalDefaults()
        {
            // Leave AmmoType unset while the item is in inventory. A concrete value here
            // (e.g. Arrow) makes the AC client appraise the item as "for use with bows",
            // which is wrong — this ammo works with every launcher. The REAL compatibility
            // trick happens in SyncToLauncher(), which stamps the matching AmmoType only
            // once the ammo is actually equipped to a launcher (the client auto-unequips
            // ammo whose AmmoType != launcher.AmmoType, so the value must match at equip time).
            RemoveProperty(PropertyInt.AmmoType);

            // Never consumed.
            SetProperty(PropertyBool.UnlimitedUse, true);

            // Inherit damage type from the launcher at hit-time (DamageEvent.GetBaseDamage
            // promotes DamageType.Base to the launcher's W_DamageType).
            SetProperty(PropertyInt.DamageType, (int)DamageType.Base);

            // Default base damage / variance — overridden per-launcher in GetBaseDamage()
            // when fired as a projectile, but kept on the item so appraisal looks sensible.
            if ((GetProperty(PropertyInt.Damage) ?? 0) == 0)
                SetProperty(PropertyInt.Damage, FallbackMaxDamage);

            SetProperty(PropertyFloat.DamageVariance, PrismaticVariance);
        }

        /// <summary>
        /// DerpACE: Rewrites this ammo's AmmoType to match the given launcher so the AC client
        /// accepts the pairing and stops auto-unequipping it. Broadcasts the property change so
        /// the change is reflected client-side. Call whenever this ammo or a launcher is equipped.
        /// </summary>
        public void SyncToLauncher(WorldObject launcher)
        {
            if (launcher?.AmmoType == null)
                return;

            var launcherAmmoType = (int)launcher.AmmoType.Value;

            if ((GetProperty(PropertyInt.AmmoType) ?? 0) == launcherAmmoType)
                return;

            SetProperty(PropertyInt.AmmoType, launcherAmmoType);

            // Push the corrected AmmoType to the client so it doesn't reject the ammo.
            var wielder = Wielder as Player;
            wielder?.Session?.Network?.EnqueueSend(
                new Network.GameMessages.Messages.GameMessagePublicUpdatePropertyInt(this, PropertyInt.AmmoType, launcherAmmoType));
        }

        /// <summary>
        /// When this ammo is equipped, immediately conform its AmmoType to the equipped launcher.
        /// </summary>
        public override void OnWield(Creature creature)
        {
            base.OnWield(creature);

            var launcher = creature?.GetEquippedMissileLauncher();
            if (launcher != null)
                SyncToLauncher(launcher);
        }


        /// <summary>
        /// When this object is spawned as a projectile, ProjectileLauncher is set by
        /// Creature_Missile.LaunchProjectile. We use that to scale damage so each
        /// launcher family fires the right "just under Deadly Prismatic" damage.
        /// </summary>
        public override BaseDamage GetBaseDamage()
        {
            var launcher = ProjectileLauncher;
            if (launcher == null)
                return base.GetBaseDamage();

            var max = GetMaxDamageForLauncher(launcher);
            return new BaseDamage(max, PrismaticVariance);
        }

        private static int GetMaxDamageForLauncher(WorldObject launcher)
        {
            var ammoType = launcher.AmmoType ?? global::ACE.Entity.Enum.AmmoType.None;

            // Crossbow family (Bolts)
            if ((ammoType & (global::ACE.Entity.Enum.AmmoType.Bolt | global::ACE.Entity.Enum.AmmoType.BoltCrystal | global::ACE.Entity.Enum.AmmoType.BoltChorizite)) != 0)
                return CrossbowMaxDamage;

            // Atlatl family (Darts)
            if ((ammoType & (global::ACE.Entity.Enum.AmmoType.Atlatl | global::ACE.Entity.Enum.AmmoType.AtlatlCrystal | global::ACE.Entity.Enum.AmmoType.AtlatlChorizite)) != 0)
                return AtlatlMaxDamage;

            // Bow family (Arrows) — default
            return BowMaxDamage;
        }
    }
}

