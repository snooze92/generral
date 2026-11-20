namespace Generral.Core.Datastore;

public class User
{
    public int Id { get; set; }

    public Guid Identifier { get; set; }

    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public required string Salt { get; set; }

    public int Iterations { get; set; }
}
