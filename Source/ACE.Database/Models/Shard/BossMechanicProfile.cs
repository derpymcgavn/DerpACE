using System;

namespace ACE.Database.Models.Shard;

public class BossMechanicProfile
{
    public string ProfileName { get; set; }
    public uint WeenieClassId { get; set; }
    public int DraftRevision { get; set; }
    public string DraftJson { get; set; }
    public int PublishedRevision { get; set; }
    public string PublishedJson { get; set; }
    public int PreviousRevision { get; set; }
    public string PreviousJson { get; set; }
    public bool Enabled { get; set; }
    public string ModifiedBy { get; set; }
    public DateTime ModifiedAt { get; set; }
}