/* DerpACE: add Ironman and Nomad oath chests beside each starter Pathwarden chest. */

DELETE FROM `landblock_instance`
WHERE `weenie_Class_Id` IN (3238931, 2000615);

INSERT INTO `landblock_instance`
    (`weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`)
SELECT
    3238931,
    `obj_Cell_Id`,
    `origin_X` + 0.75,
    `origin_Y` + 0.75,
    `origin_Z`,
    `angles_W`,
    `angles_X`,
    `angles_Y`,
    `angles_Z`,
    b'0',
    NOW()
FROM `landblock_instance`
WHERE `weenie_Class_Id` IN (33609, 33610, 33611, 33612);

INSERT INTO `landblock_instance`
    (`weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`)
SELECT
    2000615,
    `obj_Cell_Id`,
    `origin_X` - 0.75,
    `origin_Y` + 0.75,
    `origin_Z`,
    `angles_W`,
    `angles_X`,
    `angles_Y`,
    `angles_Z`,
    b'0',
    NOW()
FROM `landblock_instance`
WHERE `weenie_Class_Id` IN (33609, 33610, 33611, 33612);