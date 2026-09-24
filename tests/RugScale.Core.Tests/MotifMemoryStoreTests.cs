using RugScale.Core.Services;

namespace RugScale.Core.Tests;

public sealed class MotifMemoryStoreTests
{
    [Fact]
    public void EmbeddedSeed_LoadsAtLeastOneMotifFamily()
    {
        Assert.NotEmpty(
            MotifMemoryStore.Memory.Families);
    }
}
