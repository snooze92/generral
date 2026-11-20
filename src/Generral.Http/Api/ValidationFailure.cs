namespace Generral.Http.Api;

/// <summary>Sonarr's validation error shape, so the vendored UI forms can show errors next to fields.</summary>
public sealed record ValidationFailure(string PropertyName, string ErrorMessage)
{
    public string Severity => "error";

    public bool IsWarning => false;

    public static IResult BadRequest(params ValidationFailure[] failures)
    {
        return Results.BadRequest(failures);
    }
}
