namespace Catalog.API.Data.JsonConverteries;

public class BrandIdConverter : JsonConverter<BrandId>
{
    public override BrandId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var guid = reader.GetGuid();
        return new BrandId(guid);
    }

    public override void Write(Utf8JsonWriter writer, BrandId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
