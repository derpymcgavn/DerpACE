namespace ACE.Common
{
    public class DatabaseConfiguration
    {
        /// <summary>
        /// Hours between full orphan-property maintenance sweeps. Zero runs every startup;
        /// a negative value disables the full sweep. Per-biota insert protection is unaffected.
        /// </summary>
        public int StartupOrphanSweepIntervalHours { get; set; } = 168;
        public MySqlConfiguration Authentication { get; set; } = new MySqlConfiguration()
        {
            Host     = "127.0.0.1",
            Port     = 3306,
            Database = "ace_auth",
            Username = "root",
            Password = ""
        };

        public MySqlConfiguration Shard { get; set; } = new MySqlConfiguration()
        {
            Host = "127.0.0.1",
            Port = 3306,
            Database = "ace_shard",
            Username = "root",
            Password = ""
        };

        public MySqlConfiguration World { get; set; } = new MySqlConfiguration()
        {
            Host = "127.0.0.1",
            Port = 3306,
            Database = "ace_world",
            Username = "root",
            Password = ""
        };
    }
}
