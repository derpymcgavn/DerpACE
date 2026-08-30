DELETE FROM `weenie` WHERE `class_Id` = 7000021;

INSERT INTO `weenie` (`class_Id`, `class_Name`, `type`, `last_Modified`)
VALUES (7000021, 'derprallybannerdeployed', 1, '2026-08-30 00:00:00') /* Generic */;

INSERT INTO `weenie_properties_int` (`object_Id`, `type`, `value`)
VALUES (7000021,   1,          128) /* ItemType - Misc */
     , (7000021,   3,           61) /* PaletteTemplate */
     , (7000021,   5,            0) /* EncumbranceVal */
     , (7000021,   8,            0) /* Mass */
     , (7000021,  16,            1) /* ItemUseable - No */
     , (7000021,  19,            0) /* Value */
     , (7000021,  93,           36) /* PhysicsState - Static, Ethereal, IgnoreCollisions */;

INSERT INTO `weenie_properties_bool` (`object_Id`, `type`, `value`)
VALUES (7000021,  11, True ) /* IgnoreCollisions */
     , (7000021,  13, True ) /* Ethereal */
     , (7000021,  14, False) /* GravityStatus */
     , (7000021,  19, False) /* Attackable */
     , (7000021,  69, False) /* IsSellable */;

INSERT INTO `weenie_properties_float` (`object_Id`, `type`, `value`)
VALUES (7000021,  12, 0) /* Shade */;

INSERT INTO `weenie_properties_string` (`object_Id`, `type`, `value`)
VALUES (7000021,   1, 'Rally Banner') /* Name */
     , (7000021,  15, 'A planted rally banner.') /* ShortDesc */
     , (7000021,  16, 'A temporary planted banner that rallies nearby fellowship members.') /* LongDesc */;

INSERT INTO `weenie_properties_d_i_d` (`object_Id`, `type`, `value`)
VALUES (7000021,   1, 0x02000CDB) /* Setup */
     , (7000021,   3, 0x20000014) /* SoundTable */
     , (7000021,   6, 0x04001379) /* PaletteBase */
     , (7000021,   7, 0x100003A7) /* ClothingBase */
     , (7000021,   8, 0x060023A8) /* Icon */
     , (7000021,  22, 0x3400002B) /* PhysicsEffectTable */;