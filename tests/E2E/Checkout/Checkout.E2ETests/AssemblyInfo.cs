// Every test signs in as the same customer and works on that customer's one cart: running them in
// parallel would have them overwrite each other's cart.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
