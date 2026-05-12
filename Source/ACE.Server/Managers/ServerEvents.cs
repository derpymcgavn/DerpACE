namespace ACE.Server.Managers
{
    /// <summary>
    /// Lightweight server-side event flags that don't require database entries.
    /// Toggle with @start event &lt;name&gt; / @end event &lt;name&gt;
    /// </summary>
    public static class ServerEvents
    {
        /// <summary>
        /// When true, lootgen weapons and shields receive a random ObjScale between 0.25 and 3.25.
        /// </summary>
        public static bool WackyLoot { get; set; } = false;
    }
}
