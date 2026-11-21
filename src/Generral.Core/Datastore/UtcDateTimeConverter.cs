using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Generral.Core.Datastore;

/// <summary>SQLite loses <see cref="DateTimeKind"/>: values are written as UTC and read back as UTC.</summary>
public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
