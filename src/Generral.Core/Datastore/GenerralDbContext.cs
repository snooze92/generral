using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Tags;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Datastore;

public class GenerralDbContext(DbContextOptions<GenerralDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ConfigEntry> Config => Set<ConfigEntry>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventRevision> EventRevisions => Set<EventRevision>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Episode> Episodes => Set<Episode>();

    public DbSet<Mapping> Mappings => Set<Mapping>();

    public DbSet<MappingVersion> MappingVersions => Set<MappingVersion>();

    public DbSet<EpisodeLink> EpisodeLinks => Set<EpisodeLink>();

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

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

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.HasIndex(t => t.Label).IsUnique();
            tag.Property(t => t.Label).UseCollation("NOCASE");
        });

        modelBuilder.Entity<Source>(source =>
        {
            source.HasIndex(s => s.Name).IsUnique();
            source.Property(s => s.Name).UseCollation("NOCASE");
            source.Property(s => s.Settings).HasJsonConversion();
            source.Property(s => s.Tags).HasJsonConversion();
        });

        modelBuilder.Entity<Event>(ev =>
        {
            ev.HasIndex(e => new { e.SourceId, e.Key }).IsUnique();
            ev.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.SourceId).OnDelete(DeleteBehavior.Cascade);
            ev.Property(e => e.Fields).HasJsonConversion();
            ev.Property(e => e.Images).HasJsonConversion();
        });

        modelBuilder.Entity<EventRevision>(revision =>
        {
            revision.HasIndex(r => new { r.EventId, r.Created });
            revision.HasOne(r => r.Event).WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
            revision.Property(r => r.Fields).HasJsonConversion();
            revision.Property(r => r.Images).HasJsonConversion();
        });

        modelBuilder.Entity<Series>(series =>
        {
            series.HasIndex(s => s.Guid).IsUnique();
            series.HasIndex(s => s.TitleSlug).IsUnique();
            series.Property(s => s.Genres).HasJsonConversion();
            series.Property(s => s.Images).HasJsonConversion();
            series.Property(s => s.Tags).HasJsonConversion();
            series.Property(s => s.SearchSettings).HasJsonConversion();
        });

        modelBuilder.Entity<Season>(season =>
        {
            season.HasIndex(s => new { s.SeriesId, s.SeasonNumber }).IsUnique();
            season.HasOne(s => s.Series).WithMany().HasForeignKey(s => s.SeriesId).OnDelete(DeleteBehavior.Cascade);
            season.Property(s => s.Images).HasJsonConversion();
        });

        modelBuilder.Entity<Episode>(episode =>
        {
            episode.HasIndex(e => new { e.SeriesId, e.SeasonNumber, e.EpisodeNumber }).IsUnique();
            episode.HasIndex(e => e.AirDateUtc);
            episode.HasOne(e => e.Series).WithMany().HasForeignKey(e => e.SeriesId).OnDelete(DeleteBehavior.Cascade);
            episode.Property(e => e.Images).HasJsonConversion();
            episode.Property(e => e.SearchTerms).HasJsonConversion();
        });

        modelBuilder.Entity<Mapping>(mapping =>
        {
            mapping.HasOne(m => m.Series).WithMany().HasForeignKey(m => m.SeriesId).OnDelete(DeleteBehavior.Cascade);
            mapping.HasOne(m => m.Source).WithMany().HasForeignKey(m => m.SourceId).OnDelete(DeleteBehavior.Restrict);
            mapping.Property(m => m.Definition).HasJsonConversion();
        });

        modelBuilder.Entity<MappingVersion>(version =>
        {
            version.HasIndex(v => new { v.MappingId, v.Version }).IsUnique();
            version.HasOne(v => v.Mapping).WithMany().HasForeignKey(v => v.MappingId).OnDelete(DeleteBehavior.Cascade);
            version.Property(v => v.Definition).HasJsonConversion();
        });

        modelBuilder.Entity<EpisodeLink>(link =>
        {
            link.HasIndex(l => new { l.MappingId, l.EventId, l.PartKey }).IsUnique();
            link.HasOne(l => l.Mapping).WithMany().HasForeignKey(l => l.MappingId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(l => l.Event).WithMany().HasForeignKey(l => l.EventId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(l => l.Episode).WithMany().HasForeignKey(l => l.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
