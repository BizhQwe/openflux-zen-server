using Microsoft.EntityFrameworkCore;
using OpenFlux.Zen.Server.Models;

namespace OpenFlux.Zen.Server.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Tunnel> Tunnels => Set<Tunnel>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(64).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PasswordSalt).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SecretPath).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ListenHost).HasMaxLength(64).HasDefaultValue("127.0.0.1");
            entity.Property(e => e.PublishMode).HasMaxLength(32).HasDefaultValue("local");
        });

        modelBuilder.Entity<Tunnel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(32).HasDefaultValue("exit");
            entity.Property(e => e.Transport).HasMaxLength(32).HasDefaultValue("yandex");
            entity.Property(e => e.Inbound).HasMaxLength(32);
            entity.Property(e => e.Mode).HasMaxLength(32).HasDefaultValue("l4");
            entity.Property(e => e.Codec).HasMaxLength(32).HasDefaultValue("batched");
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}
