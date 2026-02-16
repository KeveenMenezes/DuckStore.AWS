# Ordering Functional Tests

## Prerequisites

These functional tests require Docker to be running on your machine, as they use .NET Aspire to orchestrate test containers for:
- SQL Server
- RabbitMQ

## Running the Tests

### With Docker Desktop/Engine Running

```bash
dotnet test tests/Services/Ordering/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj
```

The tests will automatically:
1. Start SQL Server container via Aspire
2. Start RabbitMQ container via Aspire
3. Configure the Ordering API to use these test containers
4. Run the functional tests
5. Clean up containers after tests complete

### Without Docker

If Docker is not available, these tests will hang or fail during infrastructure startup. In this case:
- Run only the unit tests instead: `dotnet test tests/Services/Ordering/Ordering.UnitTests/Ordering.UnitTests.csproj`
- Or ensure Docker is installed and running before executing functional tests

## Test Configuration

The test fixture (`OrderingApiFixture`) automatically configures:
- **SQL Server Connection**: Retrieved from Aspire's SQL Server test container
- **RabbitMQ Connection**: Retrieved from Aspire's RabbitMQ test container and formatted as `amqp://host:port`
- **MessageBroker Settings**: Username and password set to default `guest` credentials

## Troubleshooting

### Tests Hang on Startup
- **Cause**: Docker is not running or accessible
- **Solution**: Start Docker Desktop/Engine and retry

### Connection Errors
- **Cause**: Test containers failed to start properly
- **Solution**: Check Docker logs and ensure sufficient resources are available

### Package Version Errors
- **Cause**: FluentValidation package version mismatch
- **Solution**: This has been fixed - FluentValidation is now at version 12.1.1 in `Directory.Packages.props`

## Changes Made to Fix Tests

1. Updated FluentValidation from 12.1.0 to 12.1.1 to resolve dependency conflicts
2. Added RabbitMQ resource configuration in the test fixture
3. Added MessageBroker configuration (Host, UserName, Password) for test environment
4. Fixed connection string name to match infrastructure expectations (`orderingDb` lowercase)
5. Added necessary Aspire using directives for proper type resolution
