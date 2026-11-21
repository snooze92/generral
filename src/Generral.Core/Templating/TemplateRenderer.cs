using Scriban;
using Scriban.Runtime;

namespace Generral.Core.Templating;

public sealed record TemplateLimits
{
    public int LoopLimit { get; init; } = 1000;

    public int RecursiveLimit { get; init; } = 50;

    /// <summary>Maximum length of a converted string and of the rendered output, in characters.</summary>
    public int LimitToString { get; init; } = 100_000;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>Registers extra functions on the shared builtin objects (<c>regex</c>, <c>string</c>, …).</summary>
public interface ITemplateHelpers
{
    void Register(ScriptObject builtins);
}

/// <summary>
/// Compiles and runs sandboxed Scriban templates: no includes, loop/recursion/output limits and a time limit.
/// Model members keep their names as-is (<c>event.fields.strVenue</c>), missing members evaluate to null.
/// </summary>
public sealed class TemplateRenderer
{
    private static readonly string[] RemovedBuiltins = ["include", "include_join"];

    private readonly ScriptObject _builtins;

    public TemplateRenderer(TemplateLimits? limits = null, IEnumerable<ITemplateHelpers>? helpers = null)
    {
        Limits = limits ?? new TemplateLimits();
        _builtins = TemplateContext.GetDefaultBuiltinObject();
        foreach (var name in RemovedBuiltins)
        {
            _builtins.Remove(name);
        }

        new GenerralTemplateHelpers().Register(_builtins);
        foreach (var helper in helpers ?? [])
        {
            helper.Register(_builtins);
        }

        foreach (var value in _builtins.Values)
        {
            if (value is ScriptObject scriptObject)
            {
                scriptObject.IsReadOnly = true;
            }
        }

        _builtins.IsReadOnly = true;
    }

    public TemplateLimits Limits { get; }

    /// <exception cref="TemplateException">The template has syntax errors.</exception>
    public CompiledTemplate Compile(string text)
    {
        var template = Template.Parse(text);
        if (template.HasErrors)
        {
            throw new TemplateException(string.Join("; ", template.Messages.Select(m => m.Message)));
        }

        return new CompiledTemplate(this, text, template);
    }

    public string Render(string text, IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken = default)
        => Compile(text).Render(model, cancellationToken);

    /// <summary>Imports the public static methods of <paramref name="functions"/> into the builtin object <paramref name="objectName"/> (snake_case names).</summary>
    public static void Import(ScriptObject builtins, string objectName, Type functions)
    {
        if (builtins[objectName] is not ScriptObject target)
        {
            target = new ScriptObject();
            builtins.SetValue(objectName, target, true);
        }

        var imported = new ScriptObject();
        imported.Import(functions);
        foreach (var (name, function) in imported)
        {
            target.Remove(name);
            target.SetValue(name, function, true);
        }
    }

    internal TemplateContext CreateContext(IReadOnlyDictionary<string, object?> model, CancellationToken cancellationToken)
    {
        var context = new TemplateContext(_builtins)
        {
            LoopLimit = Limits.LoopLimit,
            LoopLimitQueryable = Limits.LoopLimit,
            RecursiveLimit = Limits.RecursiveLimit,
            ObjectRecursionLimit = Limits.RecursiveLimit,
            LimitToString = Limits.LimitToString,
            OutputLimit = Limits.LimitToString,
            OnOutputLimit = ScriptLimitBehavior.Throw,
            RegexTimeOut = Limits.RegexTimeout,
            EnableRelaxedTargetAccess = true,
            TemplateLoader = null,
            CancellationToken = cancellationToken,
        };
        context.PushGlobal(ScriptValues.ToScriptObject(model));
        return context;
    }
}
