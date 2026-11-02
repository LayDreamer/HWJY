using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit;
using ZhonTai.Admin.Core.Helpers;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class LocalUploadPathTests
{
    [Theory]
    [InlineData("upload")]
    [InlineData("upload/2026/11/02")]
    [InlineData("upload\\images")]
    [InlineData("./upload/资料")]
    public async Task ValidSubdirectoriesSaveWithinConfiguredRoot(string directory)
    {
        using var fixture = new UploadDirectory();
        var path = UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", directory, "example.txt");
        Assert.StartsWith(Path.Combine(fixture.WebRoot, "upload") + Path.DirectorySeparatorChar, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("safe-upload"));
        await new UploadHelper().SaveAsync(new FormFile(content, 0, content.Length, "file", "example.txt"), path);
        Assert.Equal("safe-upload", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("upload/../../outside")]
    [InlineData("upload\\..\\outside")]
    [InlineData("/outside")]
    [InlineData("\\outside")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\outside")]
    [InlineData("C:outside")]
    [InlineData("upload-other")]
    [InlineData("upload/.. /outside")]
    [InlineData("upload/stream:ads")]
    [InlineData("upload/NUL")]
    public void UnsafeDirectoriesCannotCreateAnOutsideFile(string directory)
    {
        using var fixture = new UploadDirectory();
        Assert.Throws<ArgumentException>(() => UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", directory, "probe.txt"));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../probe.txt")]
    [InlineData("..\\probe.txt")]
    [InlineData("probe.txt:stream")]
    [InlineData("probe.txt.")]
    [InlineData("NUL.txt")]
    [InlineData("COM1.log")]
    public void UnsafeFileNamesAreRejected(string name)
    {
        using var fixture = new UploadDirectory();
        Assert.Throws<ArgumentException>(() => UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", "upload", name));
    }

    [Fact]
    public void RootCasingCannotSelectADifferentDirectory()
    {
        using var fixture = new UploadDirectory();
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(fixture.WebRoot, "upload", "probe.txt"), UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", "UPLOAD", "probe.txt"));
        else
            Assert.Throws<ArgumentException>(() => UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", "UPLOAD", "probe.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReparsePointCannotRedirectTheUploadRootOrASubdirectory(bool rootLink)
    {
        using var fixture = new UploadDirectory();
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        var link = rootLink ? Path.Combine(fixture.WebRoot, "upload") : Path.Combine(fixture.WebRoot, "upload", "linked");
        Directory.CreateDirectory(Path.GetDirectoryName(link));
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(fixture.Root, "junction.ps1");
            File.WriteAllText(script, "param($LinkPath,$TargetPath) $ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null");
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-File", script, link, outside }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            Assert.True(process.WaitForExit(15000));
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<ArgumentException>(() => UploadHelper.ResolveLocalFilePath(fixture.WebRoot, "upload", rootLink ? "upload" : "upload/linked", "probe.txt"));
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class UploadDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HWJY-upload-tests-" + Guid.NewGuid().ToString("N"));
        public string WebRoot => Path.Combine(Root, "wwwroot");
        public UploadDirectory() => Directory.CreateDirectory(WebRoot);
        public void Dispose()
        {
            var owned = Path.GetFullPath(Root);
            if (!owned.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(owned).StartsWith("HWJY-upload-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(owned, true);
        }
    }
}
