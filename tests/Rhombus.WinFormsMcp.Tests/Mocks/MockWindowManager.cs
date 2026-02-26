using System.Collections.Generic;
using C5T8fBtWY.WinFormsMcp.Server.Abstractions;
using C5T8fBtWY.WinFormsMcp.Server.Models;

namespace C5T8fBtWY.WinFormsMcp.Tests.Mocks;

/// <summary>
/// Mock implementation of IWindowManager for unit testing handlers.
/// </summary>
public class MockWindowManager : IWindowManager
{
    private readonly List<WindowInfo> _windows = new();

    /// <summary>
    /// Configure mock windows for testing.
    /// </summary>
    public void SetWindows(IEnumerable<WindowInfo> windows)
    {
        _windows.Clear();
        _windows.AddRange(windows);
    }

    /// <summary>
    /// Add a single mock window.
    /// </summary>
    public void AddWindow(WindowInfo window)
    {
        _windows.Add(window);
    }

    /// <summary>
    /// Add a simple mock window with just title and handle.
    /// </summary>
    public void AddWindow(string title, string handle, int pid = 1234)
    {
        _windows.Add(new WindowInfo
        {
            Title = title,
            Handle = handle,
            ProcessId = pid
        });
    }

    public List<WindowInfo> GetAllWindows()
    {
        return new List<WindowInfo>(_windows);
    }

    public List<WindowInfo> GetWindowsByPids(IReadOnlySet<int> pids)
    {
        return _windows.Where(w => pids.Contains(w.ProcessId)).ToList();
    }

    public List<WindowInfo> GetWindowsByPid(int pid)
    {
        return _windows.Where(w => w.ProcessId == pid).ToList();
    }

    public WindowInfo? FindWindow(string? windowHandle, string? windowTitle)
    {
        if (!string.IsNullOrEmpty(windowHandle))
        {
            return _windows.FirstOrDefault(w => w.Handle == windowHandle);
        }

        if (!string.IsNullOrEmpty(windowTitle))
        {
            return _windows.FirstOrDefault(w =>
                w.Title?.Contains(windowTitle, StringComparison.OrdinalIgnoreCase) == true);
        }

        return null;
    }

    public WindowInfo? FindWindowByTitle(string titleSubstring)
    {
        return _windows.FirstOrDefault(w =>
            w.Title?.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase) == true);
    }

    public (int screenX, int screenY) TranslateCoordinates(WindowInfo window, int windowX, int windowY)
    {
        // For testing, just return coordinates as-is (no translation)
        return (windowX, windowY);
    }

    /// <summary>
    /// Reset all mock state.
    /// </summary>
    public void Reset()
    {
        _windows.Clear();
    }
}
