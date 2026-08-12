using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Appizza.Modules.Catalog;

public static class PublishedMenuContract
{
    public const int SchemaVersion = 1;
    public const int DefaultPracticalFlavorLimit = 4;
    public const string ConfigurationHashAlgorithm = "appizza-config-v1";

    public static string MenuETag(long catalogVersion, long availabilityVersion) =>
        $"\"catalog-{catalogVersion}-availability-{availabilityVersion}-schema-{SchemaVersion}\"";

    public static string AvailabilityETag(long availabilityVersion) =>
        $"\"availability-{availabilityVersion}-schema-{SchemaVersion}\"";
}

public static class SemanticConfigurationHash
{
    public static string Compute(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteCanonical(writer, element);
        }

        return $"{PublishedMenuContract.ConfigurationHashAlgorithm}:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .Where(property => !IsTechnical(property.Name))
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var decimalValue)) writer.WriteRawValue(decimalValue.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                else writer.WriteRawValue(element.GetRawText());
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsTechnical(string name) => name.Equals("createdAt", StringComparison.OrdinalIgnoreCase)
        || name.Equals("updatedAt", StringComparison.OrdinalIgnoreCase)
        || name.Equals("createdBy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("updatedBy", StringComparison.OrdinalIgnoreCase)
        || name.Equals("version", StringComparison.OrdinalIgnoreCase);
}

public sealed record PublishedMenuHeader(Guid CatalogRevisionId, long CatalogVersion, long AvailabilityVersion, int SchemaVersion, DateTimeOffset PublishedAt);
public sealed record PublishedMediaManifestItem(Guid AssetId, string MimeType, long FileSize, string ChecksumSha256, string CacheKey);
public sealed record PublishedAvailabilityItem(Guid ResourceId, bool ExplicitAvailable, bool EffectiveAvailable, string? ReasonCode);
public sealed record PublishedAvailability(long CatalogVersion, long AvailabilityVersion, int SchemaVersion, IReadOnlyList<PublishedAvailabilityItem> Ingredients, IReadOnlyList<PublishedAvailabilityItem> Products, IReadOnlyList<PublishedAvailabilityItem> Variants);
