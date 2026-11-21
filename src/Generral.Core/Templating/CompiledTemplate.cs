using Scriban;
using Scriban.Syntax;

namespace Generral.Core.Templating;

public sealed class CompiledTemplate
{
    private readonly TemplateRenderer _renderer;
    private readonly Template _template;
    private readonly bool _singleExpression;

    internal CompiledTemplate(TemplateRenderer renderer, string text, Template template)
    {
        _renderer = renderer;
        _template = template;
        Text = text;
        var trimmed = text.Trim();
        _singleExpression = trimmed.StartsWith("{{", StringComparison.Ordinal)
            && trimmed.EndsWith("}}", StringComparison.Ordinal)
            && trimmed.IndexOf("{{", 2, StringComparison.Ordinal) < 0;
    }

    public string Text { get; }

    /// <exception cref="TemplateException">Runtime error, sandbox limit or timeout.</exception>
    public string Render(IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken = default)
        => Run(model, cancellationToken, context => _template.Render(context));

    /// <summary>
    /// Returns the raw value of a template made of a single <c>{{ expression }}</c> (e.g. a range or an array),
    /// otherwise the rendered string.
    /// </summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken = default)
        => _singleExpression
            ? Run(model, cancellationToken, context => _template.Evaluate(context))
            : Render(model, cancellationToken);

    private T Run<T>(IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken, Func<TemplateContext, T> run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_renderer.Limits.Timeout);
        var context = _renderer.CreateContext(model, timeout.Token);
        try
        {
            return run(context);
        }
        catch (ScriptAbortException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TemplateException($"Template timed out after {_renderer.Limits.Timeout.TotalSeconds:0.##}s");
        }
        catch (ScriptRuntimeException e)
        {
            throw new TemplateException(e.Message, e);
        }
    }
}

public sealed class TemplateException : Exception
{
    public TemplateException(string message)
        : base(message)
    {
    }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
