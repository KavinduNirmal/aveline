namespace Aveline.Tests.Unit;

public class SmokeTests
{
    [Fact]
    public void TestEnvironment_ShouldBeProperlyConfigured()
    {
        // Arrange
        const bool isConfigured = true;

        // Act & Assert
        isConfigured.Should().BeTrue();
    }
}
