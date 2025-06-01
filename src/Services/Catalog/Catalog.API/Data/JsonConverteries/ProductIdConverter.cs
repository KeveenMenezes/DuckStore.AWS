namespace Catalog.API.Data.JsonConverteries;

public class ProductIdConverter : JsonConverter<ProductId>
{
    public override ProductId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var guid = reader.GetGuid();
        return new ProductId(guid);
    }

    public override void Write(Utf8JsonWriter writer, ProductId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
