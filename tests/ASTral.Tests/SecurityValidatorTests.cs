using System.Text;
using ASTral.Security;

namespace ASTral.Tests;

public class SecurityValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public SecurityValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "astral_sectest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env")]
    [InlineData(".env.local")]
    [InlineData("credentials.json")]
    [InlineData("id_rsa")]
    [InlineData("id_rsa.pub")]
    [InlineData("server.pem")]
    [InlineData("server.key")]
    [InlineData("keystore.jks")]
    [InlineData("my.secrets")]
    [InlineData("app.token")]
    [InlineData(".htpasswd")]
    [InlineData(".netrc")]
    [InlineData("service-account-key.json")]
    public void IsSecretFile_DetectsSecretFiles(string filePath)
    {
        Assert.True(SecurityValidator.IsSecretFile(filePath));
    }

    [Theory]
    [InlineData("src/main.py")]
    [InlineData("README.md")]
    [InlineData("package.json")]
    [InlineData("app.config")]
    [InlineData("src/utils.ts")]
    [InlineData("Makefile")]
    public void IsSecretFile_AllowsNormalFiles(string filePath)
    {
        Assert.False(SecurityValidator.IsSecretFile(filePath));
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("photo.jpg")]
    [InlineData("archive.zip")]
    [InlineData("program.exe")]
    [InlineData("library.dll")]
    [InlineData("binary.so")]
    [InlineData("module.wasm")]
    [InlineData("data.sqlite")]
    [InlineData("font.woff2")]
    [InlineData("doc.pdf")]
    public void IsBinaryExtension_DetectsBinaryExtensions(string filePath)
    {
        Assert.True(SecurityValidator.IsBinaryExtension(filePath));
    }

    [Theory]
    [InlineData("main.py")]
    [InlineData("index.js")]
    [InlineData("app.cs")]
    [InlineData("README.md")]
    [InlineData("config.yaml")]
    public void IsBinaryExtension_AllowsTextFiles(string filePath)
    {
        Assert.False(SecurityValidator.IsBinaryExtension(filePath));
    }

    [Fact]
    public void ValidatePath_RejectsPathTraversal()
    {
        var root = Path.GetTempPath();
        var traversal = Path.Combine(root, "..", "..", "etc", "passwd");

        Assert.False(SecurityValidator.ValidatePath(root, traversal));
    }

    [Fact]
    public void ValidatePath_AcceptsValidSubpath()
    {
        // Use a concrete directory without trailing separator to avoid ambiguity
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var valid = Path.Combine(root, "subdir", "file.txt");

        Assert.True(SecurityValidator.ValidatePath(root, valid));
    }

    [Fact]
    public void ValidatePath_AcceptsRootItself()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        Assert.True(SecurityValidator.ValidatePath(root, root));
    }

    [Fact]
    public void IsBinaryContent_DetectsNullBytes()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x00, 0x6F }; // "Hel\0o"
        Assert.True(SecurityValidator.IsBinaryContent(data));
    }

    [Fact]
    public void IsBinaryContent_AllowsTextContent()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("Hello, world!");
        Assert.False(SecurityValidator.IsBinaryContent(data));
    }

    // --- ValidateSymlinks ---

    [Fact]
    public void ValidateSymlinks_NonSymlink_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "regular.txt");
        File.WriteAllText(file, "hello");

        Assert.True(SecurityValidator.ValidateSymlinks(file, _tempDir));
    }

    [Fact]
    public void ValidateSymlinks_SymlinkWithinRoot_ReturnsTrue()
    {
        var target = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(target, "hello");
        var link = Path.Combine(_tempDir, "link.txt");

        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (IOException)
        {
            // Platform doesn't support symlinks
            return;
        }

        Assert.True(SecurityValidator.ValidateSymlinks(link, _tempDir));
    }

    [Fact]
    public void ValidateSymlinks_SymlinkEscapingRoot_ReturnsFalse()
    {
        var outsideTarget = Path.GetTempFileName();
        var link = Path.Combine(_tempDir, "escape_link.txt");

        try
        {
            File.CreateSymbolicLink(link, outsideTarget);
        }
        catch (IOException)
        {
            return;
        }
        finally
        {
            if (File.Exists(outsideTarget))
                File.Delete(outsideTarget);
        }

        Assert.False(SecurityValidator.ValidateSymlinks(link, _tempDir));
    }

    // --- IsBinaryFile ---

    [Fact]
    public void IsBinaryFile_BinaryExtension_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "image.png");
        File.WriteAllText(file, "not really a png");

        Assert.True(SecurityValidator.IsBinaryFile(file));
    }

    [Fact]
    public void IsBinaryFile_TextFile_ReturnsFalse()
    {
        var file = Path.Combine(_tempDir, "readme.txt");
        File.WriteAllText(file, "Hello, world!");

        Assert.False(SecurityValidator.IsBinaryFile(file));
    }

    [Fact]
    public void IsBinaryFile_TextFileWithNullBytes_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "data.txt");
        File.WriteAllBytes(file, new byte[] { 0x48, 0x65, 0x6C, 0x00, 0x6F });

        Assert.True(SecurityValidator.IsBinaryFile(file));
    }

    [Fact]
    public void IsBinaryFile_UnreadableFile_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "nonexistent", "missing.txt");

        Assert.True(SecurityValidator.IsBinaryFile(file));
    }

    // --- GetMaxIndexFiles ---

    [Fact]
    public void GetMaxIndexFiles_ExplicitValue_ReturnsValue()
    {
        Assert.Equal(500, SecurityValidator.GetMaxIndexFiles(500));
    }

    [Fact]
    public void GetMaxIndexFiles_NullValue_ReturnsDefault()
    {
        Assert.Equal(10000, SecurityValidator.GetMaxIndexFiles(null));
    }

    [Fact]
    public void GetMaxIndexFiles_ZeroThrows()
    {
        Assert.Throws<ArgumentException>(() => SecurityValidator.GetMaxIndexFiles(0));
    }

    [Fact]
    public void GetMaxIndexFiles_NegativeThrows()
    {
        Assert.Throws<ArgumentException>(() => SecurityValidator.GetMaxIndexFiles(-1));
    }

    // --- ShouldExcludeFile ---

    [Fact]
    public void ShouldExcludeFile_NormalFile_ReturnsNull()
    {
        var file = Path.Combine(_tempDir, "normal.txt");
        File.WriteAllText(file, "small content");

        Assert.Null(SecurityValidator.ShouldExcludeFile(file, _tempDir));
    }

    [Fact]
    public void ShouldExcludeFile_SecretFile_ReturnsSecretFile()
    {
        var file = Path.Combine(_tempDir, ".env");
        File.WriteAllText(file, "SECRET=value");

        Assert.Equal("secret_file", SecurityValidator.ShouldExcludeFile(file, _tempDir));
    }

    [Fact]
    public void ShouldExcludeFile_LargeFile_ReturnsFileTooLarge()
    {
        var file = Path.Combine(_tempDir, "large.txt");
        File.WriteAllBytes(file, new byte[1024]);

        Assert.Equal("file_too_large", SecurityValidator.ShouldExcludeFile(file, _tempDir, maxFileSize: 512, checkSecrets: false));
    }

    [Fact]
    public void ShouldExcludeFile_BinaryExtension_ReturnsBinaryExtension()
    {
        var file = Path.Combine(_tempDir, "image.png");
        File.WriteAllText(file, "tiny");

        Assert.Equal("binary_extension", SecurityValidator.ShouldExcludeFile(file, _tempDir, checkSecrets: false));
    }

    [Fact]
    public void ShouldExcludeFile_PathTraversal_ReturnsPathTraversal()
    {
        // A file outside of root
        var outsideFile = Path.GetTempFileName();
        try
        {
            Assert.Equal("path_traversal", SecurityValidator.ShouldExcludeFile(outsideFile, _tempDir, checkSymlinks: false));
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }

    // --- SafeDecode ---

    [Fact]
    public void SafeDecode_ValidUtf8_ReturnsString()
    {
        var data = Encoding.UTF8.GetBytes("Hello, world!");
        Assert.Equal("Hello, world!", SecurityValidator.SafeDecode(data));
    }

    [Fact]
    public void SafeDecode_EmptyBytes_ReturnsEmpty()
    {
        Assert.Equal("", SecurityValidator.SafeDecode([]));
    }
}
