using Microsoft.EntityFrameworkCore;

namespace ACE.Database.Models.Shard;

public partial class ShardDbContext
{
    public virtual DbSet<BossMechanicProfile> BossMechanicProfile { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BossMechanicProfile>(entity =>
        {
            entity.HasKey(e => e.ProfileName);
            entity.ToTable("boss_mechanic_profile");
            entity.HasIndex(e => e.WeenieClassId).IsUnique();
            entity.Property(e => e.ProfileName).HasMaxLength(64).HasColumnName("profile_Name");
            entity.Property(e => e.WeenieClassId).HasColumnName("weenie_Class_Id");
            entity.Property(e => e.DraftRevision).HasColumnName("draft_Revision");
            entity.Property(e => e.DraftJson).HasColumnType("longtext").HasColumnName("draft_Json");
            entity.Property(e => e.PublishedRevision).HasColumnName("published_Revision");
            entity.Property(e => e.PublishedJson).HasColumnType("longtext").HasColumnName("published_Json");
            entity.Property(e => e.PreviousRevision).HasColumnName("previous_Revision");
            entity.Property(e => e.PreviousJson).HasColumnType("longtext").HasColumnName("previous_Json");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.ModifiedBy).HasMaxLength(64).HasColumnName("modified_By");
            entity.Property(e => e.ModifiedAt).HasColumnName("modified_At");
        });
    }
}