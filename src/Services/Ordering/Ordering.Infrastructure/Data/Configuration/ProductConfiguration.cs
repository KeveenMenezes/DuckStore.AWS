using Ordering.Domain.AggregatesModel.OrderAggregate.Models;
using Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

namespace Ordering.Infrastructure.Data.Configuration;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(c => c.Id).HasConversion(
            productId => productId.Value,
            dbId => ProductId.Of(dbId));

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
    }
}
