namespace TagBites.Pipes.Tests;

public class ConnectionBagTests
{
    [Fact]
    public void BagKeepsEveryConcurrentWrite()
    {
        const int count = 1000;
        var bag = new NamedPipeConnectionBag();

        Parallel.For(0, count, i => bag[i.ToString()] = i);

        for (var i = 0; i < count; i++)
            Assert.Equal(i, bag[i.ToString()]);
    }

    [Fact]
    public void BagRemovesEntryAssignedNull()
    {
        var bag = new NamedPipeConnectionBag();
        bag["a"] = 1;

        bag["a"] = null;

        Assert.Null(bag["a"]);
    }

    [Fact]
    public void BagReturnsNullForUnknownName() => Assert.Null(new NamedPipeConnectionBag()["missing"]);

    [Fact]
    public void BagRejectsNullName()
    {
        var bag = new NamedPipeConnectionBag();

        Assert.Throws<ArgumentNullException>(() => _ = bag[null!]);
        Assert.Throws<ArgumentNullException>(() => bag[null!] = 1);
    }
}
