using Amazon.Lambda.Annotations;
using Amazon.Lambda.Serialization.SystemTextJson;

// Registers the JSON serializer used by the Lambda runtime to (de)serialize
// input/output events. Required by the Amazon.Lambda.Annotations source generator (AWSLambda0108).
[assembly: Amazon.Lambda.Core.LambdaSerializer(
    typeof(SourceGeneratorLambdaJsonSerializer<Catalog.Function.Shared.Configuration.CatalogSerializerContext>))]

// Makes the source generator emit a Program.Main that dispatches to the right
// [LambdaFunction] based on the ANNOTATIONS_HANDLER environment variable, instead of
// exposing one handler class per function. That is what lets a single ZIP on the
// provided.al2023 custom runtime back every Lambda in this service (ADR-0042).
[assembly: LambdaGlobalProperties(GenerateMain = true)]
