using Rhombus.WinFormsMcp.Server.Sandbox;

namespace Rhombus.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for WsbConfigBuilder - validates .wsb generation and security checks.
/// These tests run headless and don't require Windows Sandbox to be installed.
/// </summary>
public class WsbConfigBuilderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WsbConfigTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    #region Basic XML Generation Tests

    [Test]
    public void Build_EmptyConfig_ProducesValidXml()
    {
        var builder = new WsbConfigBuilder();
        var xml = builder.Build();

        Assert.That(xml, Does.Contain("<Configuration>"));
        Assert.That(xml, Does.Contain("</Configuration>"));
        Assert.That(xml, Does.Contain("<VGpu>Disable</VGpu>"));
        Assert.That(xml, Does.Contain("<Networking>Disable</Networking>"));
    }

    [Test]
    public void Build_WithVGpuEnabled_SetsVGpuToEnable()
    {
        var xml = new WsbConfigBuilder()
            .SetVGpu(true)
            .Build();

        Assert.That(xml, Does.Contain("<VGpu>Enable</VGpu>"));
    }

    [Test]
    public void Build_WithNetworkingEnabled_SetsNetworkingToEnable()
    {
        var xml = new WsbConfigBuilder()
            .SetNetworking(true)
            .Build();

        Assert.That(xml, Does.Contain("<Networking>Enable</Networking>"));
    }

    [Test]
    public void Build_WithLogonCommand_IncludesCommand()
    {
        var xml = new WsbConfigBuilder()
            .SetLogonCommand(@"C:\MCP\Server.exe")
            .Build();

        Assert.That(xml, Does.Contain("<LogonCommand>"));
        Assert.That(xml, Does.Contain(@"<Command>C:\MCP\Server.exe</Command>"));
        Assert.That(xml, Does.Contain("</LogonCommand>"));
    }

    [Test]
    public void Build_WithMappedFolder_IncludesFolderMapping()
    {
        // Create a valid test directory
        var testFolder = Path.Combine(_tempDir, "TestApp");
        Directory.CreateDirectory(testFolder);

        var xml = new WsbConfigBuilder()
            .AddMappedFolder(testFolder, @"C:\App", readOnly: true)
            .Build();

        Assert.That(xml, Does.Contain("<MappedFolders>"));
        Assert.That(xml, Does.Contain("<MappedFolder>"));
        Assert.That(xml, Does.Contain($"<HostFolder>{testFolder}</HostFolder>"));
        Assert.That(xml, Does.Contain(@"<SandboxFolder>C:\App</SandboxFolder>"));
        Assert.That(xml, Does.Contain("<ReadOnly>true</ReadOnly>"));
    }

    [Test]
    public void Build_WithReadWriteFolder_SetsReadOnlyFalse()
    {
        var testFolder = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(testFolder);

        var xml = new WsbConfigBuilder()
            .AddMappedFolder(testFolder, @"C:\Output", readOnly: false)
            .Build();

        Assert.That(xml, Does.Contain("<ReadOnly>false</ReadOnly>"));
    }

    [Test]
    public void Build_WithMultipleFolders_IncludesAllMappings()
    {
        var folder1 = Path.Combine(_tempDir, "App");
        var folder2 = Path.Combine(_tempDir, "MCP");
        var folder3 = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);
        Directory.CreateDirectory(folder3);

        var xml = new WsbConfigBuilder()
            .AddMappedFolder(folder1, @"C:\App", readOnly: true)
            .AddMappedFolder(folder2, @"C:\MCP", readOnly: true)
            .AddMappedFolder(folder3, @"C:\Output", readOnly: false)
            .Build();

        // Count occurrences of MappedFolder
        var count = CountOccurrences(xml, "<MappedFolder>");
        Assert.That(count, Is.EqualTo(3));
    }

    #endregion

    #region Security Validation Tests - Documents Path

    [Test]
    public void AddMappedFolder_DocumentsPath_ThrowsArgumentException()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var builder = new WsbConfigBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder(documentsPath, @"C:\Docs", readOnly: true));

        Assert.That(ex.Message, Does.Contain("Security violation"));
        Assert.That(ex.Message, Does.Contain("Documents").IgnoreCase);
    }

    [Test]
    public void AddMappedFolder_NestedDocumentsPath_ThrowsArgumentException()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var nestedPath = Path.Combine(documentsPath, "SomeFolder", "Nested");
        var builder = new WsbConfigBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder(nestedPath, @"C:\Docs", readOnly: true));

        Assert.That(ex.Message, Does.Contain("Security violation"));
    }

    #endregion

    #region Security Validation Tests - Desktop Path

    [Test]
    public void AddMappedFolder_DesktopPath_ThrowsArgumentException()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var builder = new WsbConfigBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder(desktopPath, @"C:\Desktop", readOnly: true));

        Assert.That(ex.Message, Does.Contain("Security violation"));
        Assert.That(ex.Message, Does.Contain("Desktop").IgnoreCase);
    }

    #endregion

    #region Security Validation Tests - Case Variations

    [Test]
    public void AddMappedFolder_CaseVariation_Documents_ThrowsArgumentException()
    {
        // Try with different case variations
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new[]
        {
            Path.Combine(userProfile, "documents"),
            Path.Combine(userProfile, "DOCUMENTS"),
            Path.Combine(userProfile, "Documents"),
            Path.Combine(userProfile, "DoCuMeNtS")
        };

        foreach (var path in paths)
        {
            var builder = new WsbConfigBuilder();
            var ex = Assert.Throws<ArgumentException>(() =>
                builder.AddMappedFolder(path, @"C:\Test", readOnly: true),
                $"Should reject path: {path}");

            Assert.That(ex.Message, Does.Contain("Security violation"),
                $"Path {path} should be rejected as security violation");
        }
    }

    [Test]
    public void AddMappedFolder_CaseVariation_Desktop_ThrowsArgumentException()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new[]
        {
            Path.Combine(userProfile, "desktop"),
            Path.Combine(userProfile, "DESKTOP"),
            Path.Combine(userProfile, "DeskTop")
        };

        foreach (var path in paths)
        {
            var builder = new WsbConfigBuilder();
            var ex = Assert.Throws<ArgumentException>(() =>
                builder.AddMappedFolder(path, @"C:\Test", readOnly: true),
                $"Should reject path: {path}");

            Assert.That(ex.Message, Does.Contain("Security violation"),
                $"Path {path} should be rejected as security violation");
        }
    }

    #endregion

    #region Security Validation Tests - Path Traversal

    [Test]
    public void AddMappedFolder_PathTraversal_ThrowsArgumentException()
    {
        // Create a valid base folder
        var baseFolder = _tempDir;

        // Try path traversal
        var traversalPath = Path.Combine(baseFolder, "..", "..", "Windows");
        var builder = new WsbConfigBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder(traversalPath, @"C:\Bad", readOnly: true));

        // Should be caught either by traversal check or Windows forbidden segment
        Assert.That(ex.Message, Does.Contain("Security violation").Or.Contain(".."));
    }

    #endregion

    #region Security Validation Tests - System Paths

    [Test]
    [TestCase(@"C:\Windows")]
    [TestCase(@"C:\Windows\System32")]
    [TestCase(@"C:\Program Files")]
    [TestCase(@"C:\Program Files (x86)")]
    [TestCase(@"C:\ProgramData")]
    public void AddMappedFolder_SystemPath_ThrowsArgumentException(string systemPath)
    {
        // Skip if path doesn't exist (e.g., Program Files (x86) on ARM)
        if (!Directory.Exists(systemPath))
        {
            Assert.Ignore($"Path {systemPath} does not exist on this system");
            return;
        }

        var builder = new WsbConfigBuilder();

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder(systemPath, @"C:\System", readOnly: true));

        Assert.That(ex.Message, Does.Contain("Security violation"));
    }

    #endregion

    #region Valid Path Tests

    [Test]
    public void AddMappedFolder_ValidTempPath_Succeeds()
    {
        var testFolder = Path.Combine(_tempDir, "ValidTest");
        Directory.CreateDirectory(testFolder);

        var builder = new WsbConfigBuilder();

        Assert.DoesNotThrow(() =>
            builder.AddMappedFolder(testFolder, @"C:\Test", readOnly: true));
    }

    [Test]
    public void AddMappedFolder_ValidCustomPath_Succeeds()
    {
        // Create a folder in temp (which is valid)
        var customFolder = Path.Combine(_tempDir, "CustomApp");
        Directory.CreateDirectory(customFolder);

        var builder = new WsbConfigBuilder()
            .AddMappedFolder(customFolder, @"C:\App", readOnly: true);

        var xml = builder.Build();
        Assert.That(xml, Does.Contain(customFolder));
    }

    #endregion

    #region File Save Tests

    [Test]
    public void BuildAndSave_CreatesFile()
    {
        var testFolder = Path.Combine(_tempDir, "App");
        Directory.CreateDirectory(testFolder);

        var outputPath = Path.Combine(_tempDir, "test.wsb");

        var builder = new WsbConfigBuilder()
            .AddMappedFolder(testFolder, @"C:\App", readOnly: true)
            .SetLogonCommand(@"C:\App\test.exe");

        var savedPath = builder.BuildAndSave(outputPath);

        Assert.That(File.Exists(savedPath), Is.True);

        var content = File.ReadAllText(savedPath);
        Assert.That(content, Does.Contain("<Configuration>"));
        Assert.That(content, Does.Contain("<MappedFolder>"));
    }

    #endregion

    #region Factory Tests

    [Test]
    public void CreateMcpSandbox_ProducesValidConfig()
    {
        var appPath = Path.Combine(_tempDir, "App");
        var mcpPath = Path.Combine(_tempDir, "MCP");
        var sharedPath = Path.Combine(_tempDir, "Shared");
        var outputPath = Path.Combine(_tempDir, "Output");

        Directory.CreateDirectory(appPath);
        Directory.CreateDirectory(mcpPath);
        Directory.CreateDirectory(sharedPath);
        Directory.CreateDirectory(outputPath);

        var builder = SandboxConfigurations.CreateMcpSandbox(
            appPath, mcpPath, sharedPath, outputPath);

        var xml = builder.Build();

        // Verify all expected elements
        Assert.That(xml, Does.Contain("<VGpu>Disable</VGpu>"));
        Assert.That(xml, Does.Contain("<Networking>Disable</Networking>"));
        Assert.That(xml, Does.Contain(@"<SandboxFolder>C:\App</SandboxFolder>"));
        Assert.That(xml, Does.Contain(@"<SandboxFolder>C:\MCP</SandboxFolder>"));
        Assert.That(xml, Does.Contain(@"<SandboxFolder>C:\Shared</SandboxFolder>"));
        Assert.That(xml, Does.Contain(@"<SandboxFolder>C:\Output</SandboxFolder>"));
        Assert.That(xml, Does.Contain("bootstrap.ps1"));

        // Count folder mappings
        var mappingCount = CountOccurrences(xml, "<MappedFolder>");
        Assert.That(mappingCount, Is.EqualTo(4));
    }

    [Test]
    public void CreateMcpSandbox_WithoutOutput_HasThreeMappings()
    {
        var appPath = Path.Combine(_tempDir, "App");
        var mcpPath = Path.Combine(_tempDir, "MCP");
        var sharedPath = Path.Combine(_tempDir, "Shared");

        Directory.CreateDirectory(appPath);
        Directory.CreateDirectory(mcpPath);
        Directory.CreateDirectory(sharedPath);

        var builder = SandboxConfigurations.CreateMcpSandbox(
            appPath, mcpPath, sharedPath, outputFolderPath: null);

        var xml = builder.Build();

        var mappingCount = CountOccurrences(xml, "<MappedFolder>");
        Assert.That(mappingCount, Is.EqualTo(3));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void AddMappedFolder_EmptyPath_ThrowsArgumentException()
    {
        var builder = new WsbConfigBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder("", @"C:\Empty", readOnly: true));
    }

    [Test]
    public void AddMappedFolder_WhitespacePath_ThrowsArgumentException()
    {
        var builder = new WsbConfigBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.AddMappedFolder("   ", @"C:\Whitespace", readOnly: true));
    }

    [Test]
    public void Build_FluentApi_WorksCorrectly()
    {
        var folder = Path.Combine(_tempDir, "Test");
        Directory.CreateDirectory(folder);

        var xml = new WsbConfigBuilder()
            .SetVGpu(false)
            .SetNetworking(false)
            .AddMappedFolder(folder, @"C:\Test", readOnly: true)
            .SetLogonCommand(@"C:\Test\app.exe")
            .Build();

        Assert.That(xml, Does.Contain("<Configuration>"));
        Assert.That(xml, Does.Contain("<VGpu>Disable</VGpu>"));
        Assert.That(xml, Does.Contain(@"C:\Test\app.exe"));
    }

    #endregion

    #region Helper Methods

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    #endregion
}
