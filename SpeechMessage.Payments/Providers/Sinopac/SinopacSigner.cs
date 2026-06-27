using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Payments.Providers.Sinopac;

internal static class SinopacSigner
{
    public static string GenerateSign(object value, string aesKey, string nonce)
    {
        var source = GetSigningString(value) + nonce + aesKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToUpperInvariant();
    }

    public static string GetSigningString(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var fields = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var type = value.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<SinopacSignExcludeAttribute>() is not null)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is null || !IsScalarValue(propertyValue))
            {
                continue;
            }

            fields[property.Name] = Convert.ToString(propertyValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Join(
            "&",
            fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{field.Key}={field.Value}"));
    }

    private static bool IsScalarValue(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(Guid);
    }
}
