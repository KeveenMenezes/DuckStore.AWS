namespace Catalog.API.Data.JsonConverteries;

public class ProductAttributeConverter : JsonConverter<ProductAttribute>
{
    public override ProductAttribute Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;

        var name = root.GetProperty("Name").GetString()!;
        var unit = root.GetProperty("Unit").GetString()!;
        var type = (AttributeType)root.GetProperty("Type").GetInt32();
        var isRequired = root.GetProperty("IsRequired").GetBoolean();

        return new ProductAttribute(name, unit, type, isRequired);
    }

    public override void Write(Utf8JsonWriter writer, ProductAttribute value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WriteString("Unit", value.Unit);
        writer.WriteNumber("Type", (int)value.Type);
        writer.WriteBoolean("IsRequired", value.IsRequired);
        writer.WriteEndObject();
    }
}
