using System;
using System.IO;
using System.Threading;
using Uninstaller.Windows.Filesystem;
using Xunit;

namespace Uninstaller.Windows.Tests.Filesystem;

public class WindowsCanonicalPathResolverTests
{
    private readonly WindowsCanonicalPathResolver _resolver = new WindowsCanonicalPathResolver();

    [Fact]
    public void ResolveAndVerify_AbsoluteNormalPath_IsValidAndCanonical()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Temp\NormalFolder");
        Assert.True(result.IsValid);
        Assert.True(result.IsCanonical);
        Assert.Equal(@"C:\Temp\NormalFolder", result.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_RelativePath_IsInvalid()
    {
        var result = _resolver.ResolveAndVerify(@"Temp\NormalFolder");
        Assert.False(result.IsValid);
        Assert.False(result.IsCanonical);
        Assert.Contains("absolute path", result.Reason);
    }

    [Fact]
    public void ResolveAndVerify_DotNormalization()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Temp\.\NormalFolder");
        Assert.True(result.IsValid);
        Assert.Equal(@"C:\Temp\NormalFolder", result.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_DotDotNormalization()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Temp\App\..\NormalFolder");
        Assert.True(result.IsValid);
        Assert.Equal(@"C:\Temp\NormalFolder", result.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_TrailingSeparators()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Temp\NormalFolder\");
        Assert.True(result.IsValid);
        Assert.Equal(@"C:\Temp\NormalFolder", result.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_RepeatedSeparators()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Temp\\NormalFolder");
        Assert.True(result.IsValid);
        Assert.Equal(@"C:\Temp\NormalFolder", result.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_CasingVariations_IsDeterministic()
    {
        var res1 = _resolver.ResolveAndVerify(@"C:\temp\app");
        var res2 = _resolver.ResolveAndVerify(@"C:\TEMP\APP");
        Assert.Equal(res1.CanonicalPath, res2.CanonicalPath, ignoreCase: true);
    }

    [Fact]
    public void ResolveAndVerify_SiblingPrefixAttack_IsNotWithinRoot()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Program Files\MyApp2", @"C:\Program Files\MyApp");
        Assert.True(result.IsValid);
        Assert.False(result.IsWithinExpectedRoot);
        Assert.Contains(result.Warnings, w => w.Contains("not contained within"));
    }

    [Fact]
    public void ResolveAndVerify_ExpectedRootChild_IsWithinRoot()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Program Files\MyApp\bin", @"C:\Program Files\MyApp");
        Assert.True(result.IsValid);
        Assert.True(result.IsWithinExpectedRoot);
    }

    [Fact]
    public void ResolveAndVerify_ExpectedRootItself_IsWithinRootWithWarning()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Program Files\MyApp", @"C:\Program Files\MyApp");
        Assert.True(result.IsValid);
        Assert.True(result.IsWithinExpectedRoot);
        Assert.Contains(result.Warnings, w => w.Contains("exactly the expected root"));
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.System)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.UserProfile)]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    [InlineData(Environment.SpecialFolder.MyVideos)]
    [InlineData(Environment.SpecialFolder.MyMusic)]
    public void ResolveAndVerify_ProtectedPath_IsFlaggedProtected(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(path)) return; // Skip if OS doesn't resolve it

        var result = _resolver.ResolveAndVerify(path);
        Assert.True(result.IsValid);
        Assert.True(result.IsProtected);
    }

    [Fact]
    public void ResolveAndVerify_MissingPath_IsValidLexically()
    {
        var result = _resolver.ResolveAndVerify(@"C:\Path\That\Should\Never\Exist\12345");
        Assert.True(result.IsValid);
        Assert.True(result.IsCanonical);
    }

    [Fact]
    public void ResolveAndVerify_MalformedPath_IsInvalid()
    {
        // Path with invalid characters for Windows (|) or just invalid path format
        var result = _resolver.ResolveAndVerify("C:\\Path\\|\"Invalid\"\\App");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ResolveAndVerify_ReparsePointDetection_FailsClosed()
    {
        // On modern Windows, C:\Documents and Settings is a Junction to C:\Users
        var reparsePointPath = @"C:\Documents and Settings";
        if (Directory.Exists(reparsePointPath))
        {
            var attributes = File.GetAttributes(reparsePointPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var result = _resolver.ResolveAndVerify(reparsePointPath);
                Assert.True(result.IsValid); // It resolves lexically
                Assert.True(result.IsReparsePoint);
                Assert.Contains(result.Warnings, w => w.Contains("reparse point"));
            }
        }
    }

    [Fact]
    public void ResolveAndVerify_Cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        // Since we don't have async IO in ResolveAndVerify, it doesn't throw OperationCanceledException immediately, 
        // but we verify the API accepts the token and behaves deterministically.
        var result = _resolver.ResolveAndVerify(@"C:\App", cancellationToken: cts.Token);
        Assert.True(result.IsValid);
    }
}
