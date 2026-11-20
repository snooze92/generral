using Generral.Core.Authentication;
using Generral.Core.Datastore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Test.Authentication;

public class UserServiceTests
{
    private SqliteConnection _connection = null!;
    private GenerralDbContext _db = null!;
    private UserService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new GenerralDbContext(new DbContextOptionsBuilder<GenerralDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _service = new UserService(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public void Upsert_creates_a_single_user_with_a_salted_hash()
    {
        var user = _service.Upsert("Admin", "correct horse");

        Assert.Multiple(() =>
        {
            Assert.That(user.Username, Is.EqualTo("admin"));
            Assert.That(user.PasswordHash, Is.Not.EqualTo("correct horse"));
            Assert.That(user.Iterations, Is.EqualTo(UserService.Iterations));
            Assert.That(_db.Users.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void FindUser_checks_the_password_and_ignores_username_case()
    {
        _service.Upsert("admin", "correct horse");

        Assert.Multiple(() =>
        {
            Assert.That(_service.FindUser("ADMIN", "correct horse"), Is.Not.Null);
            Assert.That(_service.FindUser("admin", "wrong"), Is.Null);
            Assert.That(_service.FindUser("other", "correct horse"), Is.Null);
            Assert.That(_service.FindUser("admin", ""), Is.Null);
        });
    }

    [Test]
    public void Changing_the_password_rotates_the_identifier_to_end_sessions()
    {
        var before = _service.Upsert("admin", "one").Identifier;
        var renamed = _service.Upsert("admin2", null).Identifier;
        var changed = _service.Upsert("admin2", "two").Identifier;

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.EqualTo(before));
            Assert.That(changed, Is.Not.EqualTo(before));
            Assert.That(_service.FindUser("admin2", "two"), Is.Not.Null);
            Assert.That(_service.FindUser("admin2", "one"), Is.Null);
            Assert.That(_db.Users.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Creating_the_first_user_requires_a_password()
    {
        Assert.That(() => _service.Upsert("admin", null), Throws.ArgumentException);
    }
}
