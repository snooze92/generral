using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Generral.Core.Datastore;

/// <summary>Used by <c>dotnet ef</c> to create migrations.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GenerralDbContext>
{
    public GenerralDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GenerralDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new GenerralDbContext(options);
    }
}
