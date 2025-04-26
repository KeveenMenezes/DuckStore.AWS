namespace Discount.Grpc.Data;

public class DiscountContext(DbContextOptions<DiscountContext> options)
    : DbContext(options)
{
    public DbSet<Coupon> Coupons { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coupon>().HasData(
            new Coupon
            {
                Id = 1,
                ProductName = "IPhone X",
                Description = "IPhone Description XI",
                Amount = 1
            },
            new Coupon
            {
                Id = 2,
                ProductName = "IPhone XI",
                Description = "IPhone Description XI",
                Amount = 2
            }
        );
    }
}
