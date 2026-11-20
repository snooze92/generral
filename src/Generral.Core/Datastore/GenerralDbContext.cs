using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Datastore;

public class GenerralDbContext(DbContextOptions<GenerralDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ConfigEntry> Config => Set<ConfigEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.Identifier).IsUnique();
            user.HasIndex(u => u.Username).IsUnique();
            user.Property(u => u.Username).UseCollation("NOCASE");
        });

        modelBuilder.Entity<ConfigEntry>(config =>
        {
            config.ToTable("Config");
            config.HasIndex(c => c.Key).IsUnique();
            config.Property(c => c.Key).UseCollation("NOCASE");
        });
    }
}
