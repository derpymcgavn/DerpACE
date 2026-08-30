DELETE FROM `weenie` WHERE `class_Id` = 7000020;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (7000020, 'derprallybanner', 1, '2026-08-29 00:00:00') /* Generic */;

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (7000020,   1,          128) /* ItemType - Misc */
     , (7000020,   3,           61) /* PaletteTemplate */
     , (7000020,   5,           50) /* EncumbranceVal */
     , (7000020,   8,           50) /* Mass */
     , (7000020,  16,            8) /* ItemUseable - Contained */
     , (7000020,  19,          500) /* Value */
     , (7000020,  93,         1044) /* PhysicsState - Ethereal, IgnoreCollisions, Gravity */
     , (7000020, 280,         2060) /* SharedCooldown */
     , (7000020, 366,           35) /* UseRequiresSkill - Leadership */
     , (7000020, 369,          180) /* UseRequiresLevel */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (7000020,  11, True ) /* IgnoreCollisions */
     , (7000020,  13, True ) /* Ethereal */
     , (7000020,  14, True ) /* GravityStatus */
     , (7000020,  19, True ) /* Attackable */
     , (7000020,  69, False) /* IsSellable */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (7000020,  12, 0) /* Shade */
     , (7000020, 167, 900) /* CooldownDuration */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (7000020,   1, 'Rally Banner') /* Name */
     , (7000020,  14, 'Plants a temporary rally banner that strengthens nearby fellowship members.') /* Use */
     , (7000020,  15, 'A portable banner for rallying a fellowship. Requires level 180+ and trained Leadership.') /* ShortDesc */
     , (7000020,  16, 'Requires level 180+ and trained Leadership. Plants a temporary flag using the old Dereth banner style. Nearby fellowship members gain a small Damage Rating and Damage Resist Rating aura while they remain near it.') /* LongDesc */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (7000020,   1, 0x02000CDB) /* Setup */
     , (7000020,   3, 0x20000014) /* SoundTable */
     , (7000020,   6, 0x04001379) /* PaletteBase */
     , (7000020,   7, 0x100003A7) /* ClothingBase */
     , (7000020,   8, 0x060023A8) /* Icon */
     , (7000020,  22, 0x3400002B) /* PhysicsEffectTable */;