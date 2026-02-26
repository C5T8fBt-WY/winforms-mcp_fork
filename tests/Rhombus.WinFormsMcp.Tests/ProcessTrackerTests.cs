using C5T8fBtWY.WinFormsMcp.Server.Services;
using NUnit.Framework;

namespace C5T8fBtWY.WinFormsMcp.Tests;

[TestFixture]
public class ProcessTrackerTests
{
    private ProcessTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new ProcessTracker();
    }

    [Test]
    public void Track_AddsPid()
    {
        _tracker.Track(1234);

        Assert.That(_tracker.IsTracked(1234), Is.True);
        Assert.That(_tracker.Count, Is.EqualTo(1));
    }

    [Test]
    public void Track_MultiplePids_TracksAll()
    {
        _tracker.Track(1234);
        _tracker.Track(5678);
        _tracker.Track(9012);

        Assert.That(_tracker.Count, Is.EqualTo(3));
        Assert.That(_tracker.IsTracked(1234), Is.True);
        Assert.That(_tracker.IsTracked(5678), Is.True);
        Assert.That(_tracker.IsTracked(9012), Is.True);
    }

    [Test]
    public void Track_SamePidTwice_CountsOnce()
    {
        _tracker.Track(1234);
        _tracker.Track(1234);

        Assert.That(_tracker.Count, Is.EqualTo(1));
    }

    [Test]
    public void Untrack_RemovesPid()
    {
        _tracker.Track(1234);
        _tracker.Track(5678);

        _tracker.Untrack(1234);

        Assert.That(_tracker.IsTracked(1234), Is.False);
        Assert.That(_tracker.IsTracked(5678), Is.True);
        Assert.That(_tracker.Count, Is.EqualTo(1));
    }

    [Test]
    public void Untrack_NonExistentPid_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _tracker.Untrack(9999));
    }

    [Test]
    public void IsTracked_ReturnsFalseForUnknownPid()
    {
        Assert.That(_tracker.IsTracked(9999), Is.False);
    }

    [Test]
    public void GetTrackedPids_ReturnsAllTrackedPids()
    {
        _tracker.Track(100);
        _tracker.Track(200);
        _tracker.Track(300);

        var pids = _tracker.GetTrackedPids();

        Assert.That(pids.Count, Is.EqualTo(3));
        Assert.That(pids.Contains(100), Is.True);
        Assert.That(pids.Contains(200), Is.True);
        Assert.That(pids.Contains(300), Is.True);
    }

    [Test]
    public void GetTrackedPids_ReturnsEmptySetWhenEmpty()
    {
        var pids = _tracker.GetTrackedPids();

        Assert.That(pids.Count, Is.EqualTo(0));
    }

    [Test]
    public void GetTrackedPids_ReturnsCopy_ModificationsDoNotAffectTracker()
    {
        _tracker.Track(100);

        var pids = _tracker.GetTrackedPids();

        // The returned set should be a copy, not a live view
        // Tracking a new PID should not affect the previously returned set
        _tracker.Track(200);

        Assert.That(pids.Count, Is.EqualTo(1));
        Assert.That(_tracker.Count, Is.EqualTo(2));
    }

    [Test]
    public void Clear_RemovesAllPids()
    {
        _tracker.Track(100);
        _tracker.Track(200);
        _tracker.Track(300);

        _tracker.Clear();

        Assert.That(_tracker.Count, Is.EqualTo(0));
        Assert.That(_tracker.IsTracked(100), Is.False);
        Assert.That(_tracker.IsTracked(200), Is.False);
        Assert.That(_tracker.IsTracked(300), Is.False);
    }

    [Test]
    public void Count_ReturnsZeroWhenEmpty()
    {
        Assert.That(_tracker.Count, Is.EqualTo(0));
    }

    [Test]
    public void Count_ReflectsTrackAndUntrackOperations()
    {
        Assert.That(_tracker.Count, Is.EqualTo(0));

        _tracker.Track(100);
        Assert.That(_tracker.Count, Is.EqualTo(1));

        _tracker.Track(200);
        Assert.That(_tracker.Count, Is.EqualTo(2));

        _tracker.Untrack(100);
        Assert.That(_tracker.Count, Is.EqualTo(1));

        _tracker.Untrack(200);
        Assert.That(_tracker.Count, Is.EqualTo(0));
    }
}
