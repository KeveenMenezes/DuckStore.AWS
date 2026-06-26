using Amazon.Lambda.Serialization.SystemTextJson;

// Registra o serializador JSON usado pelo runtime do Lambda para (de)serializar
// os eventos de entrada/saída. Exigido pelo source generator do Amazon.Lambda.Annotations (AWSLambda0108).
[assembly: Amazon.Lambda.Core.LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
