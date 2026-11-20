using Generral.Core.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Generral.Core.Instrumentation;

public static class Logging
{
    private const string Template = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u4}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>Shared level switch so the log level can change at runtime (Settings → General).</summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Information);

    public static LogEventLevel ToLevel(string logLevel)
    {
        return logLevel switch
        {
            "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            _ => LogEventLevel.Information,
        };
    }

    public static LoggerConfiguration Configure(LoggerConfiguration logger, IAppFolders folders, HostSettings settings)
    {
        LevelSwitch.MinimumLevel = ToLevel(settings.LogLevel);

        return logger
            .MinimumLevel.ControlledBy(LevelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
            .MinimumLevel.Override("Generral.Http.Authentication", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(new CleansingFormatter(new MessageTemplateTextFormatter(Template)))
            .WriteTo.File(
                new CleansingFormatter(new MessageTemplateTextFormatter(Template)),
                Path.Combine(folders.LogsFolder, "generral.txt"),
                fileSizeLimitBytes: 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 50,
                shared: true);
    }

    /// <summary>Renders an event, then strips secrets from the result (Sonarr's CleanseLogMessage rules).</summary>
    private sealed class CleansingFormatter(ITextFormatter inner) : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var buffer = new StringWriter();
            inner.Format(logEvent, buffer);
            output.Write(CleanseLogMessage.Cleanse(buffer.ToString()));
        }
    }
}
