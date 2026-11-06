using System.Runtime.CompilerServices;

namespace PuraMarkdown.Testing;

/// <summary>
/// 文件化 golden 基线。默认路径为调用方测试文件同目录下的 <c>golden/&lt;name&gt;.txt</c>。
/// 设置环境变量 <c>PURA_UPDATE_GOLDEN=1</c> 可更新基线。
/// </summary>
public static class GoldenFile
{
    /// <summary>AssertMatches。</summary>
    /// <param name="name">name 参数。</param>
    /// <param name="actual">actual 参数。</param>
    /// <param name="callerFile">callerFile 参数。</param>
    public static void AssertMatches(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "golden");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}.txt");
        var normalized = actual.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (Environment.GetEnvironmentVariable("PURA_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, normalized);
            return;
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"缺少 golden 文件：{path}。设置 PURA_UPDATE_GOLDEN=1 后重新运行以生成。");
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!string.Equals(expected, normalized, StringComparison.Ordinal))
        {
            throw new GoldenMismatchException(path, expected, normalized);
        }
    }
}

/// <summary>GoldenMismatchException。</summary>
public sealed class GoldenMismatchException(string path, string expected, string actual)
    : Exception($"golden 不匹配：{path}\n--- expected ---\n{expected}\n--- actual ---\n{actual}")
{
    /// <summary>Path。</summary>
    public string Path { get; } = path;

    /// <summary>Expected。</summary>
    public string Expected { get; } = expected;

    /// <summary>Actual。</summary>
    public string Actual { get; } = actual;
}
