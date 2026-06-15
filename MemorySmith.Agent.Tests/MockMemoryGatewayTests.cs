using Agent.Core;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class MockMemoryGatewayTests
{
    [Test]
    public async Task SearchAsync_ExactQuery_ReturnsMappedResults()
    {
        var gateway = new MockMemoryGateway();
        gateway.AddSearchResult("gothic", new SearchResult("page-1", 0.95, "Gothic Cathedral"));

        var results = await gateway.SearchAsync("gothic");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].PageId, Is.EqualTo("page-1"));
        Assert.That(results[0].Score, Is.EqualTo(0.95).Within(0.001));
    }

    [Test]
    public async Task SearchAsync_CaseInsensitive_Matches()
    {
        var gateway = new MockMemoryGateway();
        gateway.AddSearchResult("Gothic", new SearchResult("p1", 0.9));

        var results = await gateway.SearchAsync("gothic");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var gateway = new MockMemoryGateway();
        var results = await gateway.SearchAsync("unknown query");
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task CreatePage_ThenGet_ReturnsContent()
    {
        var gateway = new MockMemoryGateway();
        var id = await gateway.CreatePageAsync("Blueprint", "# GothicCathedral", "blueprint");

        Assert.That(id, Is.Not.Null.Or.Empty);
        var content = await gateway.GetPageAsync(id);
        Assert.That(content, Is.EqualTo("# GothicCathedral"));
    }

    [Test]
    public async Task UpdatePage_OverwritesContent()
    {
        var gateway = new MockMemoryGateway();
        gateway.AddPage("p1", "original");
        await gateway.UpdatePageAsync("p1", "updated");

        Assert.That(await gateway.GetPageAsync("p1"), Is.EqualTo("updated"));
    }

    [Test]
    public async Task GetPage_MissingId_ReturnsNull()
    {
        var gateway = new MockMemoryGateway();
        Assert.That(await gateway.GetPageAsync("does-not-exist"), Is.Null);
    }

    [Test]
    public async Task CreatedPageIds_TracksAllNewPages()
    {
        var gateway = new MockMemoryGateway();
        await gateway.CreatePageAsync("A", "a", "wiki");
        await gateway.CreatePageAsync("B", "b", "wiki");

        Assert.That(gateway.CreatedPageIds, Has.Count.EqualTo(2));
    }
}
