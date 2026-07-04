using Amazon.Lambda.Serialization.SystemTextJson;

// Registers the JSON serializer used by the Lambda runtime to (de)serialize
// input/output events (the DynamoDB Streams payload that triggers the publisher).
[assembly: Amazon.Lambda.Core.LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
