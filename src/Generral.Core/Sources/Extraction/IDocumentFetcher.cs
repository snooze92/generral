namespace Generral.Core.Sources.Extraction;

/// <summary>Fetches the raw documents an extraction reads. HTTP, caching and request delays live behind it.</summary>
public interface IDocumentFetcher
{
    Task<FetchedDocument> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record FetchedDocument(Uri Url, string Content);
