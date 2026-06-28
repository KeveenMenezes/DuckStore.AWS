using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: Amazon.Lambda.Core.LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
