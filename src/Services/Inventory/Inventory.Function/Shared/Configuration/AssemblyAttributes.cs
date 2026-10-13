using Amazon.Lambda.Annotations;
using Amazon.Lambda.Serialization.SystemTextJson;

// Registers the JSON serializer used by the Lambda runtime to (de)serialize input/output events.
[assembly: Amazon.Lambda.Core.LambdaSerializer(
    typeof(SourceGeneratorLambdaJsonSerializer<Inventory.Function.Shared.Configuration.InventorySerializerContext>))]

// One generated Program.Main dispatching on ANNOTATIONS_HANDLER, so a single ZIP on provided.al2023
// backs every Lambda in this service (ADR-0042).
[assembly: LambdaGlobalProperties(GenerateMain = true)]
