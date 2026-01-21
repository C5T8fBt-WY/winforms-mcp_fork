using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles advanced tools: get_capabilities, get_dpi_info, subscribe_to_events, get_pending_events,
/// mark_for_expansion, clear_expansion_marks, relocate_element, check_element_stale, get_cache_stats,
/// invalidate_cache, confirm_action, execute_confirmed_action.
/// </summary>
internal class AdvancedHandlers : HandlerBase
{
    private readonly Func<string, JsonElement, Task<JsonElement>>? _toolDispatcher;

    public AdvancedHandlers(SessionManager session, WindowManager windows, Func<string, JsonElement, Task<JsonElement>>? toolDispatcher = null)
        : base(session, windows)
    {
        _toolDispatcher = toolDispatcher;
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "get_capabilities",
        "get_dpi_info",
        "subscribe_to_events",
        "get_pending_events",
        "mark_for_expansion",
        "clear_expansion_marks",
        "relocate_element",
        "check_element_stale",
        "get_cache_stats",
        "invalidate_cache",
        "confirm_action",
        "execute_confirmed_action"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "get_capabilities" => GetCapabilities(args),
            "get_dpi_info" => GetDpiInfo(args),
            "subscribe_to_events" => SubscribeToEvents(args),
            "get_pending_events" => GetPendingEvents(args),
            "mark_for_expansion" => MarkForExpansion(args),
            "clear_expansion_marks" => ClearExpansionMarks(args),
            "relocate_element" => RelocateElement(args),
            "check_element_stale" => CheckElementStale(args),
            "get_cache_stats" => GetCacheStats(args),
            "invalidate_cache" => InvalidateCache(args),
            "confirm_action" => ConfirmAction(args),
            "execute_confirmed_action" => ExecuteConfirmedAction(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> GetCapabilities(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Check if Windows Sandbox is available
            bool sandboxAvailable = false;
            try
            {
                // Check for Windows Sandbox feature via registry
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages");
                if (key != null)
                {
                    var packageNames = key.GetSubKeyNames();
                    sandboxAvailable = packageNames.Any(p =>
                        p.Contains("Containers-DisposableClientVM", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // If we can't check registry, try running the sandbox manager's check
                try
                {
                    var sandboxManager = Session.GetSandboxManager();
                    sandboxAvailable = sandboxManager.IsSandboxAvailable();
                }
                catch { /* sandbox not available */ }
            }

            // Get OS version
            var osVersion = Environment.OSVersion.ToString();
            var osVersionFriendly = $"Windows {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor} Build {Environment.OSVersion.Version.Build}";

            // Get FlaUI version from assembly
            var flauiVersion = typeof(FlaUI.Core.AutomationBase).Assembly.GetName().Version?.ToString() ?? "Unknown";

            // List all available features (from supported tools - handler doesn't know about all tools)
            // This will be updated by the caller to include all registered tools
            var features = SupportedTools.ToArray();

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("sandbox_available", sandboxAvailable),
                ("os_version", osVersionFriendly),
                ("os_full", osVersion),
                ("flaui_version", flauiVersion),
                ("uia_backend", "UIA2"),
                ("max_depth_supported", 10),
                ("token_budget", Constants.Limits.DefaultTokenBudget),
                ("features", features),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> GetDpiInfo(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var windowTitle = GetStringArg(args, "windowTitle");

            IntPtr windowHandle = IntPtr.Zero;

            // If window title provided, get handle for that window
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var automation = Session.GetAutomation();
                var window = automation.GetWindowByTitle(windowTitle);
                if (window != null)
                {
                    try
                    {
                        windowHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
                    }
                    catch
                    {
                        // Fall back to no window handle if property not available
                    }
                }
            }

            var dpiInfo = DpiHelper.GetDpiInfo(windowHandle != IntPtr.Zero ? windowHandle : null);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("system_dpi", dpiInfo.SystemDpi),
                ("system_scale_factor", dpiInfo.SystemScaleFactor),
                ("window_dpi", dpiInfo.WindowDpi),
                ("window_scale_factor", dpiInfo.WindowScaleFactor),
                ("is_per_monitor_aware", dpiInfo.IsPerMonitorAware),
                ("standard_dpi", dpiInfo.StandardDpi),
                ("window_specified", !string.IsNullOrEmpty(windowTitle)),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> SubscribeToEvents(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Parse event types array
            var eventTypes = new List<string>();
            if (args.TryGetProperty("event_types", out var typesElement) && typesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typesElement.EnumerateArray())
                {
                    var eventType = item.GetString();
                    if (!string.IsNullOrEmpty(eventType))
                    {
                        eventTypes.Add(eventType);
                    }
                }
            }

            if (eventTypes.Count == 0)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("No event types specified", Windows,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            // Validate event types
            var validTypes = new HashSet<string> { "window_opened", "dialog_shown", "structure_changed", "property_changed" };
            var invalidTypes = eventTypes.Where(t => !validTypes.Contains(t.ToLowerInvariant())).ToList();
            if (invalidTypes.Count > 0)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail($"Invalid event types: {string.Join(", ", invalidTypes)}", Windows,
                    ("valid_types", new[] { "window_opened", "dialog_shown", "structure_changed", "property_changed" }),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            // Subscribe
            Session.SubscribeToEvents(eventTypes);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("subscribed_to", eventTypes),
                ("queue_max_size", 10),
                ("message", "Events will be queued. Use get_pending_events to retrieve them."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> GetPendingEvents(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var (events, droppedCount) = Session.DrainEventQueue();

            stopwatch.Stop();

            var eventList = events.Select(e => new
            {
                type = e.Type,
                timestamp = e.Timestamp.ToString("o"),
                window_title = e.WindowTitle,
                process_id = e.ProcessId,
                details = e.Details
            }).ToArray();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("events", eventList),
                ("events_count", events.Count),
                ("events_dropped", droppedCount),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> MarkForExpansion(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var elementKey = GetStringArg(args, "elementKey");
            var elementId = GetStringArg(args, "elementId");

            // Resolve element key from cached element if provided
            if (!string.IsNullOrEmpty(elementId))
            {
                var element = Session.GetElement(elementId);
                if (element != null)
                {
                    // Use AutomationId if available, otherwise Name
                    elementKey = element.AutomationId;
                    if (string.IsNullOrEmpty(elementKey))
                        elementKey = element.Name;
                }
            }

            if (string.IsNullOrEmpty(elementKey))
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail("Either elementKey or valid elementId required", Windows,
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            Session.MarkForExpansion(elementKey);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("element_key", elementKey),
                ("total_marked", Session.GetExpandedElements().Count),
                ("message", "Element marked for expansion. Next get_ui_tree call will expand its children regardless of depth limit."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> ClearExpansionMarks(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var elementKey = GetStringArg(args, "elementKey");
            var clearedCount = 0;

            if (string.IsNullOrEmpty(elementKey))
            {
                // Clear all
                clearedCount = Session.GetExpandedElements().Count;
                Session.ClearAllExpansionMarks();
            }
            else
            {
                // Clear specific element
                if (Session.IsMarkedForExpansion(elementKey))
                {
                    Session.ClearExpansionMark(elementKey);
                    clearedCount = 1;
                }
            }

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("cleared_count", clearedCount),
                ("remaining_marked", Session.GetExpandedElements().Count),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> RelocateElement(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = Session.GetAutomation();

            var elementId = GetStringArg(args, "elementId");
            var automationId = GetStringArg(args, "automationId");
            var name = GetStringArg(args, "name");
            var className = GetStringArg(args, "className");
            var controlType = GetStringArg(args, "controlType");

            // Build search criteria
            var criteria = new ElementSearchCriteria
            {
                AutomationId = automationId,
                Name = name,
                ClassName = className,
                ControlType = controlType,
                OriginalElementId = elementId
            };

            // If we have an element ID, try to extract criteria from cached element
            if (!string.IsNullOrEmpty(elementId))
            {
                var cachedElement = Session.GetElement(elementId);
                if (cachedElement != null)
                {
                    // Get criteria from existing element
                    try
                    {
                        criteria = new ElementSearchCriteria
                        {
                            AutomationId = string.IsNullOrEmpty(automationId) ? cachedElement.AutomationId : automationId,
                            Name = string.IsNullOrEmpty(name) ? cachedElement.Name : name,
                            ClassName = string.IsNullOrEmpty(className) ? cachedElement.ClassName : className,
                            ControlType = string.IsNullOrEmpty(controlType) ? cachedElement.ControlType.ToString() : controlType,
                            OriginalElementId = elementId,
                            LastKnownBounds = new Rectangle(
                                (int)cachedElement.BoundingRectangle.X,
                                (int)cachedElement.BoundingRectangle.Y,
                                (int)cachedElement.BoundingRectangle.Width,
                                (int)cachedElement.BoundingRectangle.Height
                            )
                        };
                    }
                    catch
                    {
                        // Element is stale, use whatever criteria we have
                    }
                }
            }

            // Attempt relocation
            var relocateResult = automation.RelocateElement(criteria);

            stopwatch.Stop();

            if (relocateResult.Success && relocateResult.RelocatedElement != null)
            {
                // Update the cache with the new element reference
                var newElementId = Session.CacheElement(relocateResult.RelocatedElement);

                // If we had an old element ID, remove it
                if (!string.IsNullOrEmpty(elementId))
                {
                    Session.ClearElement(elementId);
                }

                return Task.FromResult(ToolResponse.Ok(Windows,
                    ("relocated", true),
                    ("new_element_id", newElementId),
                    ("old_element_id", elementId),
                    ("matched_by", relocateResult.MatchedBy),
                    ("message", relocateResult.Message),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }
            else
            {
                return Task.FromResult(ToolResponse.Fail(relocateResult.Message ?? "Element not found", Windows,
                    ("relocated", false),
                    ("suggestions", relocateResult.Suggestions),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> CheckElementStale(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var automation = Session.GetAutomation();

            var elementId = GetStringArg(args, "elementId") ?? throw new ArgumentException("elementId is required");

            var element = Session.GetElement(elementId);
            if (element == null)
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Ok(Windows,
                    ("is_stale", true),
                    ("reason", "Element ID not found in cache"),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
            }

            var isStale = automation.IsElementStale(element);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("is_stale", isStale),
                ("element_id", elementId),
                ("reason", isStale ? "Element reference is no longer valid" : "Element is accessible"),
                ("recommendation", isStale ? "Use relocate_element to re-find this element" : null),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> GetCacheStats(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var treeCache = Session.GetTreeCache();
            var stats = treeCache.GetStats();

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("cache_hits", stats.CacheHits),
                ("cache_misses", stats.CacheMisses),
                ("hit_rate", stats.HitRate),
                ("hit_rate_percent", $"{stats.HitRate * 100:F1}%"),
                ("is_dirty", stats.IsDirty),
                ("has_cached_data", stats.HasCachedData),
                ("cache_age_ms", stats.CacheAgeMs),
                ("max_cache_age_ms", stats.MaxCacheAgeMs),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> InvalidateCache(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var treeCache = Session.GetTreeCache();
            var resetStats = GetBoolArg(args, "reset_stats", false);

            treeCache.Clear();

            if (resetStats)
            {
                treeCache.ResetStats();
            }

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("cache_cleared", true),
                ("stats_reset", resetStats),
                ("message", "Tree cache invalidated. Next get_ui_tree will rebuild from scratch."),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private Task<JsonElement> ConfirmAction(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var action = GetStringArg(args, "action") ?? throw new ArgumentException("action is required");
            var description = GetStringArg(args, "description") ?? throw new ArgumentException("description is required");
            var target = GetStringArg(args, "target");

            JsonElement? parameters = null;
            if (args.TryGetProperty("parameters", out var paramsElement) && paramsElement.ValueKind != JsonValueKind.Null)
            {
                parameters = paramsElement;
            }

            // Validate action type
            var validActions = new HashSet<string> { "close_app", "force_close", "send_keys_dangerous", "custom" };
            if (!validActions.Contains(action.ToLowerInvariant()))
            {
                stopwatch.Stop();
                return Task.FromResult(ToolResponse.Fail(
                    $"Invalid action type: {action}",
                    Windows,
                    ("valid_actions", new[] { "close_app", "force_close", "send_keys_dangerous", "custom" }),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)
                ).ToJsonElement());
            }

            var confirmation = Session.CreateConfirmation(action, description, target, parameters);

            stopwatch.Stop();

            return Task.FromResult(ToolResponse.Ok(Windows,
                ("status", "pending_confirmation"),
                ("confirmation_token", confirmation.Token),
                ("action", confirmation.Action),
                ("description", confirmation.Description),
                ("target", confirmation.Target),
                ("expires_at", confirmation.ExpiresAt.ToString("o")),
                ("expires_in_seconds", 60),
                ("message", "Call execute_confirmed_action with this token to proceed"),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds)
            ).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResponse.Fail(ex.Message, Windows).ToJsonElement());
        }
    }

    private async Task<JsonElement> ExecuteConfirmedAction(JsonElement args)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var token = GetStringArg(args, "confirmationToken") ?? throw new ArgumentException("confirmationToken is required");

            var confirmation = Session.ConsumeConfirmation(token);
            if (confirmation == null)
            {
                stopwatch.Stop();
                return ToolResponse.Fail("Invalid or expired confirmation token", Windows,
                    ("error_code", "INVALID_TOKEN"),
                    ("execution_time_ms", stopwatch.ElapsedMilliseconds)).ToJsonElement();
            }

            // Execute the confirmed action
            JsonElement actionResult;
            switch (confirmation.Action.ToLowerInvariant())
            {
                case "close_app":
                    if (_toolDispatcher != null)
                    {
                        actionResult = await _toolDispatcher("close_app", confirmation.Parameters ?? JsonDocument.Parse("{}").RootElement);
                    }
                    else
                    {
                        actionResult = ToolResponse.Fail("Tool dispatcher not available for close_app", Windows).ToJsonElement();
                    }
                    break;

                case "force_close":
                    // Extract PID from parameters and force kill
                    if (confirmation.Parameters?.TryGetProperty("pid", out var pidElement) == true)
                    {
                        try
                        {
                            var pid = pidElement.GetInt32();
                            var process = System.Diagnostics.Process.GetProcessById(pid);
                            process.Kill(entireProcessTree: true);
                            actionResult = ToolResponse.Ok(Windows, ("message", $"Process {pid} force killed")).ToJsonElement();
                        }
                        catch (Exception ex)
                        {
                            actionResult = ToolResponse.Fail($"Force kill failed: {ex.Message}", Windows).ToJsonElement();
                        }
                    }
                    else
                    {
                        actionResult = ToolResponse.Fail("Missing pid parameter for force_close", Windows).ToJsonElement();
                    }
                    break;

                case "send_keys_dangerous":
                    if (_toolDispatcher != null)
                    {
                        actionResult = await _toolDispatcher("send_keys", confirmation.Parameters ?? JsonDocument.Parse("{}").RootElement);
                    }
                    else
                    {
                        actionResult = ToolResponse.Fail("Tool dispatcher not available for send_keys", Windows).ToJsonElement();
                    }
                    break;

                case "custom":
                    // Custom actions just return success - caller handles the action
                    // Parse the parameters to include them properly
                    object? actionParams = null;
                    if (confirmation.Parameters.HasValue)
                    {
                        actionParams = JsonSerializer.Deserialize<object>(confirmation.Parameters.Value.GetRawText());
                    }
                    actionResult = ToolResponse.Ok(Windows,
                        ("message", "Custom action confirmed"),
                        ("action_parameters", actionParams)
                    ).ToJsonElement();
                    break;

                default:
                    actionResult = ToolResponse.Fail($"Unknown action type: {confirmation.Action}", Windows).ToJsonElement();
                    break;
            }

            stopwatch.Stop();

            // Merge action result with execution metadata
            var resultDict = new Dictionary<string, object?>
            {
                ["confirmed_action"] = confirmation.Action,
                ["confirmed_target"] = confirmation.Target,
                ["confirmation_used"] = true,
                ["execution_time_ms"] = stopwatch.ElapsedMilliseconds
            };

            // Add all properties from actionResult (which already has windows)
            foreach (var prop in actionResult.EnumerateObject())
            {
                resultDict[prop.Name] = prop.Value.Clone();
            }

            // Ensure windows are included in case actionResult doesn't have them
            if (!resultDict.ContainsKey("windows"))
            {
                resultDict["windows"] = Windows.GetAllWindows();
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(resultDict)).RootElement;
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, Windows).ToJsonElement();
        }
    }
}
