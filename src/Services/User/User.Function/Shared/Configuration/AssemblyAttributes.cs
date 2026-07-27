using Amazon.Lambda.Annotations;
using Amazon.Lambda.Serialization.SystemTextJson;

// Registers the JSON serializer used by the Lambda runtime to (de)serialize
// input/output events. Required by the Amazon.Lambda.Annotations source generator (AWSLambda0108).
[assembly: Amazon.Lambda.Core.LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

