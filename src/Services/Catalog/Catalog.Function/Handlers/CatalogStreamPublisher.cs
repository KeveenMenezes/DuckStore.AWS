namespace Catalog.Function.Handlers;

// Relay to satisfy AWS Lambda's 128-char handler-string limit — the full namespace of
// CatalogStreamEventPublisherFunction exceeds it by 4 characters.
public class CatalogStreamPublisher
    : Catalog.Function.Modules.Products.EventsIntegration.Publisher.CatalogStreamEventPublisherFunction { }
