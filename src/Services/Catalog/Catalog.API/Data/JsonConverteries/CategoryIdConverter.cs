namespace Catalog.API.Data.JsonConverteries;

public class CategoryIdConverter : JsonConverter<CategoryId>
{
    public override CategoryId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var guid = reader.GetGuid();
        return new CategoryId(guid);
    }

    public override void Write(Utf8JsonWriter writer, CategoryId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
