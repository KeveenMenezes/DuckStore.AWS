using Amazon.Lambda.Annotations;
[assembly: Amazon.Lambda.Core.LambdaSerializer(
    typeof(SourceGeneratorLambdaJsonSerializer<Ordering.Function.Shared.Configuration.OrderingSerializerContext>))]

// Makes the source generator emit a Program.Main that dispatches to the right
// [LambdaFunction] based on the ANNOTATIONS_HANDLER environment variable, instead of
// exposing one handler class per function. That is what lets a single ZIP on the
// provided.al2023 custom runtime back every Lambda in this service (ADR-0042).
[assembly: LambdaGlobalProperties(GenerateMain = true)]
