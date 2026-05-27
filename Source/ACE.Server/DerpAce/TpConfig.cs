namespace ACE.Server.DerpAce
{
    /// <summary>
    /// Thin static holder so PlayerCommands.cs can read TP tunables
    /// without a direct dependency on DerpAceConfigManager.
    /// Values are pushed here by DerpAceConfigManager.Apply().
    /// </summary>
    public static class TpConfig
    {
        public static double CostPerMeter { get; set; } = 2.0;
        public static int    MinCost      { get; set; } = 50;
        public static double RequestTtl   { get; set; } = 30.0;
    }
}
