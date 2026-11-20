namespace Generral.Http.Authentication;

public static class AuthSchemes
{
    public const string ApiKey = "ApiKey";
    public const string Forms = "Forms";
    public const string External = "External";

    /// <summary>Policy for the UI and native API: API key, or the configured UI method.</summary>
    public const string UiPolicy = "UI";

    /// <summary>Policy for machine clients (Sonarr shim, iCal feed): API key only.</summary>
    public const string ApiKeyPolicy = "ApiKeyOnly";

    public const string ApiKeyHeader = "X-Api-Key";
    public const string ApiKeyQuery = "apikey";
}
