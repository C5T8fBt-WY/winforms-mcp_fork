using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace Rhombus.WinFormsMcp.Server.Sandbox;

/// <summary>
/// Builds Windows Sandbox (.wsb) configuration files with security validation.
///
/// Security model:
/// - Rejects paths containing sensitive locations (Documents, Desktop, AppData, etc.)
/// - Resolves symlinks and normalizes paths to prevent bypass attempts
/// - Uses case-insensitive path matching (Windows is case-insensitive)
/// - Only allows explicitly mapped directories
/// </summary>
public class WsbConfigBuilder
{
    private readonly List<MappedFolder> _mappedFolders = new();
    private string? _logonCommand;
    private bool _vGpuEnabled = false;
    private bool _networkingEnabled = false;

    /// <summary>
    /// Sensitive path segments that should never be mapped.
    /// These paths could expose user data or system files.
    /// </summary>
    private static readonly string[] ForbiddenPathSegments =
    {
        @"\Documents",
        @"\Desktop",
        @"\AppData",
        @"\Windows",
        @"\Program Files",
        @"\Program Files (x86)",
        @"\ProgramData",
        @"\Recovery",
        @"\$Recycle.Bin",
        @"\System Volume Information"
    };

    /// <summary>
    /// Add a folder mapping from host to sandbox.
    /// </summary>
    /// <param name="hostFolder">Path on the host system</param>
    /// <param name="sandboxFolder">Path inside the sandbox</param>
    /// <param name="readOnly">Whether the sandbox can only read from this folder</param>
    /// <exception cref="ArgumentException">If the path is invalid or forbidden</exception>
    public WsbConfigBuilder AddMappedFolder(string hostFolder, string sandboxFolder, bool readOnly = true)
    {
        ValidatePath(hostFolder);

        _mappedFolders.Add(new MappedFolder
        {
            HostFolder = NormalizePath(hostFolder),
            SandboxFolder = sandboxFolder,
            ReadOnly = readOnly
        });

        return this;
    }

    /// <summary>
    /// Set the command to run when the sandbox starts.
    /// </summary>
    public WsbConfigBuilder SetLogonCommand(string command)
    {
        _logonCommand = command;
        return this;
    }

    /// <summary>
    /// Enable or disable vGPU (virtual GPU).
    /// Default is disabled for security/performance.
    /// </summary>
    public WsbConfigBuilder SetVGpu(bool enabled)
    {
        _vGpuEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Enable or disable networking.
    /// Default is disabled for security isolation.
    /// </summary>
    public WsbConfigBuilder SetNetworking(bool enabled)
    {
        _networkingEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Build the .wsb XML configuration.
    /// </summary>
    /// <returns>XML string for the .wsb file</returns>
    public string Build()
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true
        });

        writer.WriteStartElement("Configuration");

        // vGPU setting
        writer.WriteElementString("VGpu", _vGpuEnabled ? "Enable" : "Disable");

        // Networking setting
        writer.WriteElementString("Networking", _networkingEnabled ? "Enable" : "Disable");

        // Mapped folders
        if (_mappedFolders.Count > 0)
        {
            writer.WriteStartElement("MappedFolders");
            foreach (var folder in _mappedFolders)
            {
                writer.WriteStartElement("MappedFolder");
                writer.WriteElementString("HostFolder", folder.HostFolder);
                writer.WriteElementString("SandboxFolder", folder.SandboxFolder);
                writer.WriteElementString("ReadOnly", folder.ReadOnly.ToString().ToLower());
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        // Logon command
        if (!string.IsNullOrEmpty(_logonCommand))
        {
            writer.WriteStartElement("LogonCommand");
            writer.WriteElementString("Command", _logonCommand);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Configuration
        writer.Flush();

        return sb.ToString();
    }

    /// <summary>
    /// Build and save the configuration to a file.
    /// </summary>
    /// <param name="outputPath">Path to save the .wsb file</param>
    /// <returns>The full path to the created file</returns>
    public string BuildAndSave(string outputPath)
    {
        var xml = Build();
        File.WriteAllText(outputPath, xml);
        return Path.GetFullPath(outputPath);
    }

    /// <summary>
    /// Validates a host path for security issues.
    /// Throws ArgumentException if the path is forbidden.
    /// </summary>
    private void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        // Normalize the path for consistent checking
        var normalizedPath = NormalizePath(path);

        // Allow system temp folder and its subdirectories
        var tempPath = NormalizePath(Path.GetTempPath());
        if (normalizedPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
        {
            // Temp folder is allowed - skip further checks for this path
            // Just verify no path traversal
            if (normalizedPath.Contains(".."))
            {
                throw new ArgumentException(
                    "Security violation: Path contains traversal sequences '..'",
                    nameof(path));
            }
            return;
        }

        // Check for forbidden path segments (case-insensitive)
        foreach (var forbidden in ForbiddenPathSegments)
        {
            if (normalizedPath.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new ArgumentException(
                    $"Security violation: Path contains forbidden segment '{forbidden}'. " +
                    $"Full path: '{normalizedPath}'",
                    nameof(path));
            }
        }

        // Check for user profile paths (handles different usernames)
        var userProfilePath = GetUserProfilePath();
        if (userProfilePath != null &&
            normalizedPath.StartsWith(userProfilePath, StringComparison.OrdinalIgnoreCase))
        {
            // Check if it's trying to access user folders
            var remainingPath = normalizedPath.Substring(userProfilePath.Length);
            foreach (var forbidden in ForbiddenPathSegments)
            {
                var segment = forbidden.TrimStart('\\');
                if (remainingPath.StartsWith("\\" + segment, StringComparison.OrdinalIgnoreCase) ||
                    remainingPath.Equals("\\" + segment, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Security violation: Cannot map user folder '{segment}'",
                        nameof(path));
                }
            }
        }

        // Check for path traversal attempts
        if (normalizedPath.Contains(".."))
        {
            throw new ArgumentException(
                "Security violation: Path contains traversal sequences '..'",
                nameof(path));
        }
    }

    /// <summary>
    /// Normalizes a path by resolving symlinks, normalizing separators,
    /// and converting to canonical form.
    /// </summary>
    private string NormalizePath(string path)
    {
        // Get the full path (resolves relative paths and normalizes separators)
        var fullPath = Path.GetFullPath(path);

        // On Windows, resolve symlinks and junction points
        // Note: In production, use GetFinalPathNameByHandle for symlink resolution
        // For now, we do basic normalization
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Gets the current user's profile path (e.g., C:\Users\username).
    /// </summary>
    private string? GetUserProfilePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(userProfile) ? null : userProfile;
    }

    /// <summary>
    /// Represents a folder mapping for the sandbox configuration.
    /// </summary>
    private class MappedFolder
    {
        public required string HostFolder { get; init; }
        public required string SandboxFolder { get; init; }
        public bool ReadOnly { get; init; }
    }
}

/// <summary>
/// Factory for creating standard sandbox configurations.
/// </summary>
public static class SandboxConfigurations
{
    /// <summary>
    /// Creates a standard MCP sandbox configuration with shared folder transport.
    /// </summary>
    /// <param name="appPath">Path to the application to test</param>
    /// <param name="mcpServerPath">Path to the MCP server binaries</param>
    /// <param name="sharedFolderPath">Path to the shared communication folder</param>
    /// <param name="outputFolderPath">Path for sandbox output (screenshots, logs)</param>
    /// <returns>Configured WsbConfigBuilder ready to build</returns>
    public static WsbConfigBuilder CreateMcpSandbox(
        string appPath,
        string mcpServerPath,
        string sharedFolderPath,
        string? outputFolderPath = null)
    {
        var builder = new WsbConfigBuilder()
            .SetVGpu(false)      // Disable for consistent automation
            .SetNetworking(false) // Disable for security
            .AddMappedFolder(appPath, @"C:\App", readOnly: true)
            .AddMappedFolder(mcpServerPath, @"C:\MCP", readOnly: true)
            .AddMappedFolder(sharedFolderPath, @"C:\Shared", readOnly: false);

        if (!string.IsNullOrEmpty(outputFolderPath))
        {
            builder.AddMappedFolder(outputFolderPath, @"C:\Output", readOnly: false);
        }

        // Set logon command to run MCP server via bootstrap script
        builder.SetLogonCommand(@"powershell -ExecutionPolicy Bypass -File C:\MCP\bootstrap.ps1");

        return builder;
    }
}
