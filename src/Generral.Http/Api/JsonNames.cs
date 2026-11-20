using System.Text.Json;

namespace Generral.Http.Api;

internal static class JsonNames
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    }
}
