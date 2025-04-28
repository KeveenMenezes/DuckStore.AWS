using Marten.Linq;

namespace BuildingBlocks.UnitTests;

public static class MartenMockExtensions
{
    public static Mock<IMartenQueryable<T>> CreateMartenQueryableMock<T>(IEnumerable<T> data) where T : class
    {
        var mockMartenQueryable = new Mock<IMartenQueryable<T>>(data.AsQueryable());

        return mockMartenQueryable;
    }
}
