// Each class signs in as one staff group in its own browser; running them in parallel would start
// several Cognito logins at once and interleave the warehouses each test creates and deactivates.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
