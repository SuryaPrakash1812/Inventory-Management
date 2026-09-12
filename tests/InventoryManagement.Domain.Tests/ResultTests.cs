using InventoryManagement.Core.Common;
using Xunit;

namespace InventoryManagement.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Success_ProducesSuccessfulResultWithNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_ProducesFailedResultWithError()
    {
        var result = Result.Failure("Something went wrong");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ThrowsWhenValueAccessed()
    {
        var result = Result.Failure<int>("Not found");

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_WithEmptyMessage_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(string.Empty));
    }
}
