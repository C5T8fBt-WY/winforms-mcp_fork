using C5T8fBtWY.WinFormsMcp.Server.Services;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for ProcessContext service.
/// </summary>
public class ProcessContextTests
{
    [Test]
    public void TrackLaunchedApp_FirstTime_ReturnsNull()
    {
        var context = new ProcessContext();

        var result = context.TrackLaunchedApp("/path/to/app.exe", 1234);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TrackLaunchedApp_SecondTime_ReturnsPreviousPid()
    {
        var context = new ProcessContext();
        context.TrackLaunchedApp("/path/to/app.exe", 1234);

        var result = context.TrackLaunchedApp("/path/to/app.exe", 5678);

        Assert.That(result, Is.EqualTo(1234));
    }

    [Test]
    public void TrackLaunchedApp_NormalizesPath()
    {
        var context = new ProcessContext();
        context.TrackLaunchedApp("/Path/To/App.exe", 1234);

        // Same path with different casing should return previous PID
        var result = context.TrackLaunchedApp("/path/to/app.exe", 5678);

        Assert.That(result, Is.EqualTo(1234));
    }

    [Test]
    public void GetPreviousLaunchedPid_ReturnsTrackedPid()
    {
        var context = new ProcessContext();
        context.TrackLaunchedApp("/path/to/app.exe", 1234);

        var result = context.GetPreviousLaunchedPid("/path/to/app.exe");

        Assert.That(result, Is.EqualTo(1234));
    }

    [Test]
    public void GetPreviousLaunchedPid_ReturnsNull_WhenNotTracked()
    {
        var context = new ProcessContext();

        var result = context.GetPreviousLaunchedPid("/path/to/app.exe");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void UntrackLaunchedApp_RemovesTracking()
    {
        var context = new ProcessContext();
        context.TrackLaunchedApp("/path/to/app.exe", 1234);

        context.UntrackLaunchedApp("/path/to/app.exe");

        Assert.That(context.GetPreviousLaunchedPid("/path/to/app.exe"), Is.Null);
        Assert.That(context.Count, Is.EqualTo(0));
    }

    [Test]
    public void UntrackLaunchedApp_DoesNotThrow_WhenNotTracked()
    {
        var context = new ProcessContext();

        Assert.DoesNotThrow(() => context.UntrackLaunchedApp("/path/to/nonexistent.exe"));
    }

    [Test]
    public void GetTrackedPids_ReturnsAllPids()
    {
        var context = new ProcessContext();
        context.TrackLaunchedApp("/path/to/app1.exe", 1234);
        context.TrackLaunchedApp("/path/to/app2.exe", 5678);
        context.TrackLaunchedApp("/path/to/app3.exe", 9012);

        var pids = context.GetTrackedPids();

        Assert.That(pids, Has.Count.EqualTo(3));
        Assert.That(pids, Does.Contain(1234));
        Assert.That(pids, Does.Contain(5678));
        Assert.That(pids, Does.Contain(9012));
    }

    [Test]
    public void GetTrackedPids_ReturnsEmpty_WhenNoProcessesTracked()
    {
        var context = new ProcessContext();

        var pids = context.GetTrackedPids();

        Assert.That(pids, Is.Empty);
    }

    [Test]
    public void Count_ReturnsNumberOfTrackedProcesses()
    {
        var context = new ProcessContext();
        Assert.That(context.Count, Is.EqualTo(0));

        context.TrackLaunchedApp("/path/to/app1.exe", 1234);
        Assert.That(context.Count, Is.EqualTo(1));

        context.TrackLaunchedApp("/path/to/app2.exe", 5678);
        Assert.That(context.Count, Is.EqualTo(2));
    }

    [Test]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var context = new ProcessContext();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                context.TrackLaunchedApp($"/path/to/app{index}.exe", index);
                context.GetPreviousLaunchedPid($"/path/to/app{index}.exe");
                context.GetTrackedPids();
                context.UntrackLaunchedApp($"/path/to/app{index}.exe");
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
    }
}
