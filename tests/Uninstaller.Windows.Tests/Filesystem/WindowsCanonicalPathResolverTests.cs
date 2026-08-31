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

    [Theory]
    [InlineData(@"C:\Users\test\AppData\Roaming\Telegram Desktop")]
    [InlineData(@"C:\Users\test\AppData\Local\Programs\Telegram Desktop")]
    [InlineData(@"C:\Users\test\AppData\LocalLow\Telegram Desktop")]
    [InlineData(@"C:\Program Files\7-Zip")]
    [InlineData(@"C:\Program Files (x86)\7-Zip")]
    [InlineData(@"C:\ProgramData\Telegram Desktop")]
    public void ResolveAndVerify_ApplicationOwnedLocations_AreNotProtected(string appPath)
    {
        var result = _resolver.ResolveAndVerify(appPath);
        Assert.True(result.IsValid);
        Assert.False(result.IsProtected, $"Path '{appPath}' should not be protected.");
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Windows\SysWOW64\kernel32.dll")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\file.tmp")]
    [InlineData(@"C:\System Volume Information\tracking.log")]
    [InlineData(@"C:\Recovery\Agent\agent.exe")]
    public void ResolveAndVerify_ProtectedSystemSubtrees_AreFlaggedProtected(string systemPath)
    {
        var result = _resolver.ResolveAndVerify(systemPath);
        Assert.True(result.IsValid);
        Assert.True(result.IsProtected, $"System path '{systemPath}' must be protected.");
    }

    [Theory]
    [InlineData("Documents\\MyReport.docx")]
    [InlineData("Downloads\\Installer.exe")]
    [InlineData("Pictures\\Vacation.png")]
    [InlineData("Videos\\Recording.mp4")]
    [InlineData("Music\\Song.mp3")]
    [InlineData("OneDrive\\Secret.docx")]
    [InlineData("Dropbox\\Project.zip")]
    [InlineData("Google Drive\\Work.pdf")]
    [InlineData("iCloudDrive\\Notes.txt")]
    public void ResolveAndVerify_ProtectedUserDataSubtrees_AreFlaggedProtected(string userSubPath)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var actualPath = Path.Combine(userProfile, userSubPath);

        var result = _resolver.ResolveAndVerify(actualPath);
        Assert.True(result.IsProtected, $"User data path '{actualPath}' must be protected.");
    }

    [Fact]
    public void ResolveAndVerify_ExactContainerRoots_AreFlaggedProtected()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.True(_resolver.ResolveAndVerify(@"C:\").IsProtected);
        Assert.True(_resolver.ResolveAndVerify(@"C:\Program Files").IsProtected);
        Assert.True(_resolver.ResolveAndVerify(@"C:\Program Files (x86)").IsProtected);
        Assert.True(_resolver.ResolveAndVerify(@"C:\ProgramData").IsProtected);
        Assert.True(_resolver.ResolveAndVerify(@"C:\Users").IsProtected);
        
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            Assert.True(_resolver.ResolveAndVerify(userProfile).IsProtected);
            Assert.True(_resolver.ResolveAndVerify(Path.Combine(userProfile, "AppData")).IsProtected);
            Assert.True(_resolver.ResolveAndVerify(Path.Combine(userProfile, "AppData", "Roaming")).IsProtected);
            Assert.True(_resolver.ResolveAndVerify(Path.Combine(userProfile, "AppData", "Local")).IsProtected);
        }
    }

    [Fact]
    public void ResolveAndVerify_DesktopShortcuts_AreAllowed_WhileDesktopFilesAreProtected()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktopShortcut = Path.Combine(userProfile, "Desktop", "Telegram.lnk");
        var desktopFolder = Path.Combine(userProfile, "Desktop", "PersonalData");
        var desktopDoc = Path.Combine(userProfile, "Desktop", "Budget.xlsx");

        var resShortcut = _resolver.ResolveAndVerify(desktopShortcut);
        Assert.False(resShortcut.IsProtected, "Desktop .lnk shortcuts must not be flagged protected at path resolver level.");

        var resFolder = _resolver.ResolveAndVerify(desktopFolder);
        Assert.True(resFolder.IsProtected, "Non-shortcut desktop folders must be protected.");

        var resDoc = _resolver.ResolveAndVerify(desktopDoc);
        Assert.True(resDoc.IsProtected, "Non-shortcut desktop files must be protected.");
    }

    [Fact]
    public void ResolveAndVerify_CanonicalEquivalence_ProducesIdenticalDecision()
    {
        var path1 = @"C:\Users\test\AppData\Roaming\Telegram Desktop\..\Telegram Desktop";
        var path2 = @"C:\Users\test\AppData\Roaming\Telegram Desktop\";
        var path3 = @"c:\users\test\appdata\roaming\telegram desktop";

        var res1 = _resolver.ResolveAndVerify(path1);
        var res2 = _resolver.ResolveAndVerify(path2);
        var res3 = _resolver.ResolveAndVerify(path3);

        Assert.Equal(res1.IsProtected, res2.IsProtected);
        Assert.Equal(res2.IsProtected, res3.IsProtected);
        Assert.False(res1.IsProtected);
    }
}
