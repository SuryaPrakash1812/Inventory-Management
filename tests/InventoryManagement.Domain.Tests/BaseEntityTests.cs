using InventoryManagement.Domain.Common;
using Xunit;

namespace InventoryManagement.Domain.Tests;

public class BaseEntityTests
{
    private sealed class TestEntity : BaseEntity
    {
        public TestEntity()
        {
        }

        public TestEntity(Guid id)
        {
            Id = id;
        }
    }

    private sealed class OtherEntity : BaseEntity
    {
        public OtherEntity(Guid id)
        {
            Id = id;
        }
    }

    [Fact]
    public void NewEntity_GetsNonEmptyId()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    [Fact]
    public void TwoEntities_WithSameIdAndType_AreEqual()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        Assert.Equal(a, b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TwoEntities_WithDifferentIds_AreNotEqual()
    {
        var a = new TestEntity(Guid.NewGuid());
        var b = new TestEntity(Guid.NewGuid());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EntitiesOfDifferentTypes_WithSameId_AreNotEqual()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new OtherEntity(id);

        Assert.NotEqual<object>(a, b);
    }
}
