using Rhombus.WinFormsMcp.Server.Services;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for TreeExpansionService.
/// </summary>
public class TreeExpansionServiceTests
{
    [Test]
    public void Mark_AddsElement()
    {
        var service = new TreeExpansionService();

        service.Mark("Button1");

        Assert.That(service.IsMarked("Button1"), Is.True);
        Assert.That(service.Count, Is.EqualTo(1));
    }

    [Test]
    public void Mark_IgnoresNullOrEmpty()
    {
        var service = new TreeExpansionService();

        service.Mark(null!);
        service.Mark("");

        Assert.That(service.Count, Is.EqualTo(0));
    }

    [Test]
    public void Mark_NoDuplicates()
    {
        var service = new TreeExpansionService();

        service.Mark("Button1");
        service.Mark("Button1");

        Assert.That(service.Count, Is.EqualTo(1));
    }

    [Test]
    public void IsMarked_ReturnsFalse_WhenNotMarked()
    {
        var service = new TreeExpansionService();

        Assert.That(service.IsMarked("Button1"), Is.False);
    }

    [Test]
    public void IsMarked_ReturnsFalse_ForNullOrEmpty()
    {
        var service = new TreeExpansionService();
        service.Mark("Button1");

        Assert.That(service.IsMarked(null!), Is.False);
        Assert.That(service.IsMarked(""), Is.False);
    }

    [Test]
    public void GetAll_ReturnsAllMarkedElements()
    {
        var service = new TreeExpansionService();
        service.Mark("Button1");
        service.Mark("Button2");
        service.Mark("TextBox1");

        var all = service.GetAll();

        Assert.That(all, Has.Count.EqualTo(3));
        Assert.That(all, Does.Contain("Button1"));
        Assert.That(all, Does.Contain("Button2"));
        Assert.That(all, Does.Contain("TextBox1"));
    }

    [Test]
    public void GetAll_ReturnsEmpty_WhenNoneMarked()
    {
        var service = new TreeExpansionService();

        var all = service.GetAll();

        Assert.That(all, Is.Empty);
    }

    [Test]
    public void Clear_RemovesElement()
    {
        var service = new TreeExpansionService();
        service.Mark("Button1");
        service.Mark("Button2");

        service.Clear("Button1");

        Assert.That(service.IsMarked("Button1"), Is.False);
        Assert.That(service.IsMarked("Button2"), Is.True);
        Assert.That(service.Count, Is.EqualTo(1));
    }

    [Test]
    public void Clear_DoesNotThrow_WhenNotMarked()
    {
        var service = new TreeExpansionService();

        Assert.DoesNotThrow(() => service.Clear("Button1"));
    }

    [Test]
    public void ClearAll_RemovesAllElements()
    {
        var service = new TreeExpansionService();
        service.Mark("Button1");
        service.Mark("Button2");
        service.Mark("TextBox1");

        service.ClearAll();

        Assert.That(service.Count, Is.EqualTo(0));
        Assert.That(service.IsMarked("Button1"), Is.False);
        Assert.That(service.IsMarked("Button2"), Is.False);
        Assert.That(service.IsMarked("TextBox1"), Is.False);
    }

    [Test]
    public void Count_ReturnsZero_WhenEmpty()
    {
        var service = new TreeExpansionService();

        Assert.That(service.Count, Is.EqualTo(0));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var service = new TreeExpansionService();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                service.Mark($"Element{index}");
                service.IsMarked($"Element{index}");
                service.GetAll();
                service.Clear($"Element{index}");
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }
}
