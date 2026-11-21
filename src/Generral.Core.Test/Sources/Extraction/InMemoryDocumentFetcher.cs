using Generral.Core.Sources.Extraction;

namespace Generral.Core.Test.Sources.Extraction;

internal sealed class InMemoryDocumentFetcher : IDocumentFetcher
{
    private readonly Dictionary<string, string> _documents = [];

    public List<Uri> Requested { get; } = [];

    public InMemoryDocumentFetcher Add(string url, string content)
    {
        _documents[new Uri(url).AbsoluteUri] = content;
        return this;
    }

    public Task<FetchedDocument> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        Requested.Add(url);
        return _documents.TryGetValue(url.AbsoluteUri, out var content)
            ? Task.FromResult(new FetchedDocument(url, content))
            : throw new HttpRequestException($"404 {url}");
    }
}
