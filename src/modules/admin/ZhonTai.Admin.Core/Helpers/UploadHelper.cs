using Microsoft.AspNetCore.Http;
using ZhonTai.Admin.Core.Attributes;

namespace ZhonTai.Admin.Core.Helpers;

/// <summary>
/// 文件上传帮助类
/// </summary>
[InjectSingleton]
public class UploadHelper
{
    /// <summary>将本地上传限制在配置的上传目录内，目录参数保持相对 WebRoot 的现有约定。</summary>
    public static string ResolveLocalFilePath(string webRoot, string uploadDirectory, string fileDirectory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webRoot);
        static bool IsDeviceName(string value)
        {
            var stem = value.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]));
        }
        static string RelativeDirectory(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var normalized = value.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.StartsWith('/') || normalized.Contains(':'))
                throw new ArgumentException("上传目录必须是相对目录。");
            foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                if (part == ".." || part.EndsWith('.') || part.EndsWith(' ') || IsDeviceName(part) ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                    throw new ArgumentException("上传目录包含无效路径段。");
            }
            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }
        var configured = RelativeDirectory(uploadDirectory);
        var requested = RelativeDirectory(fileDirectory);
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || fileName.EndsWith('.') || fileName.EndsWith(' ') || IsDeviceName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new ArgumentException("上传文件名无效。");

        var webRootPath = Path.GetFullPath(webRoot);
        var root = Path.GetFullPath(Path.Combine(webRootPath, configured));
        var filePath = Path.GetFullPath(Path.Combine(webRootPath, requested, fileName));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!filePath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("上传文件必须位于配置的上传目录内。");
        filePath = Path.Combine(root, Path.GetRelativePath(root, filePath));

        // 不跟随上传目录、子目录或目标文件上的符号链接/Windows 目录联接。
        for (var current = filePath; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("上传路径不能包含符号链接或目录联接。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (string.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(webRootPath), comparison)) break;
        }
        return filePath;
    }

    /// <summary>
    /// 保存文件
    /// </summary>
    /// <param name="file"></param>
    /// <param name="filePath"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task SaveAsync(IFormFile file, string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.Create(filePath);
        await file.CopyToAsync(stream, cancellationToken);
    }
}
