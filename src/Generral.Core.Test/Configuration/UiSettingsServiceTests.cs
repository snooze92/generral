using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Test.Configuration;

public class UiSettingsServiceTests
{
    [Test]
    public void Defaults_then_round_trip()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new GenerralDbContext(new DbContextOptionsBuilder<GenerralDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
        var service = new UiSettingsService(db);

        Assert.That(service.Get(), Is.EqualTo(new UiSettings()));

        service.Save(new UiSettings { Theme = "dark", FirstDayOfWeek = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(service.Get().Theme, Is.EqualTo("dark"));
            Assert.That(service.Get().FirstDayOfWeek, Is.EqualTo(1));
            Assert.That(() => service.Save(new UiSettings { Theme = "purple" }), Throws.ArgumentException);
        });
    }
}
