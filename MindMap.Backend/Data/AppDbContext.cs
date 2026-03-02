using MindMap.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace MindMap.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MindMapDocument> MindMaps => Set<MindMapDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(64);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(64);
            entity.HasIndex(x => x.NormalizedUserName).IsUnique();
        });

        modelBuilder.Entity<MindMapDocument>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(120);
            entity.Property(x => x.ShareCode).HasMaxLength(24);
            entity.HasIndex(x => x.ShareCode).IsUnique();
            entity.HasOne(x => x.Owner)
                .WithMany(u => u.MindMaps)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
