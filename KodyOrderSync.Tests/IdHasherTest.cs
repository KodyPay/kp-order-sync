using Xunit;

namespace KodyOrderSync.Tests;

public class IdHasherTest
{
    [Fact]
    public void HashOrderId_ReturnsNonEmptyString()
    {
        // Arrange
        string orderId = "test-order-123";

        // Act
        string result = IdHasher.HashOrderId(orderId);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void HashOrderId_ReturnsConsistentHash_ForSameInput()
    {
        // Arrange
        string orderId = "order-xyz-456";

        // Act
        string hash1 = IdHasher.HashOrderId(orderId);
        string hash2 = IdHasher.HashOrderId(orderId);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashOrderId_ReturnsDifferentHash_ForDifferentInput()
    {
        // Arrange
        string orderId1 = "order-abc-123";
        string orderId2 = "order-abc-124";

        // Act
        string hash1 = IdHasher.HashOrderId(orderId1);
        string hash2 = IdHasher.HashOrderId(orderId2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashOrderId_ReturnsValidString_ForEmptyInput()
    {
        // Arrange
        string orderId = "";

        // Act
        string result = IdHasher.HashOrderId(orderId);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void HashOrderId_RespectsMaxLength()
    {
        // Arrange
        string veryLongOrderId = new string('a', 1000);

        // Act
        string result = IdHasher.HashOrderId(veryLongOrderId);

        // Assert
        Assert.True(result.Length <= 30);
    }

    [Fact]
    public void HashOrderId_DoesNotContainInvalidSqlCharacters()
    {
        // Arrange
        string[] orderIds = { "test-order+123", "test/order=456", "complex+/=id" };

        foreach (var orderId in orderIds)
        {
            // Act
            string result = IdHasher.HashOrderId(orderId);

            // Assert
            Assert.DoesNotContain("/", result);
            Assert.DoesNotContain("+", result);
            Assert.DoesNotContain("=", result);
        }
    }
}