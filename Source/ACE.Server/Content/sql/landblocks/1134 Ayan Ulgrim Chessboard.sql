-- Persistent chessboard for Ulgrim's NPC chess match in Ayan Baqur.
INSERT INTO landblock_instance
    (guid, weenie_Class_Id, obj_Cell_Id, origin_X, origin_Y, origin_Z,
     angles_W, angles_X, angles_Y, angles_Z, is_Link_Child)
VALUES
    (1897087115, 14341, 288620558, 39.386238, 136.769104, 41.955501,
     1.000000, 0.000000, 0.000000, 0.000000, 0)
ON DUPLICATE KEY UPDATE
    weenie_Class_Id = VALUES(weenie_Class_Id),
    obj_Cell_Id = VALUES(obj_Cell_Id),
    origin_X = VALUES(origin_X),
    origin_Y = VALUES(origin_Y),
    origin_Z = VALUES(origin_Z),
    angles_W = VALUES(angles_W),
    angles_X = VALUES(angles_X),
    angles_Y = VALUES(angles_Y),
    angles_Z = VALUES(angles_Z),
    is_Link_Child = VALUES(is_Link_Child);