namespace Generral.Core.Configuration;

public enum AuthenticationType
{
    /// <summary>Login page + cookie, checked against the single user stored in the database.</summary>
    Forms,

    /// <summary>Authentication is delegated to a reverse proxy (SSO). Generral trusts every UI request.</summary>
    External
}

public enum AuthenticationRequiredType
{
    Enabled,
    DisabledForLocalAddresses
}

public enum DatabaseType
{
    Sqlite
}
