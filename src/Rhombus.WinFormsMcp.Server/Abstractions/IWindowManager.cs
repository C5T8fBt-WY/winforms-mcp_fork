using System.Collections.Generic;
using Rhombus.WinFormsMcp.Server.Models;

namespace Rhombus.WinFormsMcp.Server.Abstractions;

/// <summary>
/// Interface for window management, enabling testability of handlers.
/// </summary>
public interface IWindowManager
{
    List<WindowInfo> GetAllWindows();
    List<WindowInfo> GetWindowsByPids(IReadOnlySet<int> pids);
    List<WindowInfo> GetWindowsByPid(int pid);
    WindowInfo? FindWindow(string? windowHandle, string? windowTitle);
    WindowInfo? FindWindowByTitle(string titleSubstring);
    (int screenX, int screenY) TranslateCoordinates(WindowInfo window, int windowX, int windowY);
}
