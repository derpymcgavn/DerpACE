CREATE TABLE IF NOT EXISTS `boss_mechanic_profile` (
  `profile_Name` varchar(64) NOT NULL,
  `weenie_Class_Id` int unsigned NOT NULL,
  `draft_Revision` int NOT NULL DEFAULT 1,
  `draft_Json` longtext NOT NULL,
  `published_Revision` int NOT NULL DEFAULT 0,
  `published_Json` longtext NULL,
  `previous_Revision` int NOT NULL DEFAULT 0,
  `previous_Json` longtext NULL,
  `enabled` bit(1) NOT NULL DEFAULT b'0',
  `modified_By` varchar(64) NOT NULL,
  `modified_At` datetime(6) NOT NULL,
  PRIMARY KEY (`profile_Name`),
  UNIQUE KEY `ux_boss_mechanic_profile_weenie` (`weenie_Class_Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;