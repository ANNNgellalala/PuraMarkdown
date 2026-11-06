namespace PuraMarkdown;

/// <summary>
/// 稳定边界扫描：找出"可以提交、后续追加不会改变其解析结果"的前缀位置。
/// 保守策略：仅在顶层空行处提交，且前一块不是列表/引用/表格/缩进代码等可续接结构。
/// </summary>
public static class StreamingBlockScanner
{
    /// <summary>返回从 <paramref name="from"/> 起可安全提交的前缀结束偏移（不含未闭合围栏内容）。</summary>
    public static int FindStableBoundary(string text, int from)
    {
        if (string.IsNullOrEmpty(text) || from >= text.Length)
        {
            return Math.Clamp(from, 0, text.Length);
        }

        var boundary = from;
        var lineStart = from;
        var inFence = false;
        var fenceChar = '\0';
        var fenceCount = 0;
        var lastNonBlankLine = string.Empty;

        while (lineStart < text.Length)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline >= 0 ? newline : text.Length;
            var line = text.AsSpan(lineStart, lineEnd - lineStart).TrimEnd('\r');
            var nextLineStart = newline >= 0 ? newline + 1 : text.Length;

            if (!inFence && TryReadFence(line, out var openChar, out var openCount))
            {
                inFence = true;
                fenceChar = openChar;
                fenceCount = openCount;
                lastNonBlankLine = line.ToString();
                lineStart = nextLineStart;
                continue;
            }

            if (inFence)
            {
                if (IsClosingFence(line, fenceChar, fenceCount))
                {
                    inFence = false;
                }

                lastNonBlankLine = line.ToString();
                lineStart = nextLineStart;
                continue;
            }

            if (IsBlank(line))
            {
                // 顶层空行：若前一块可能是"可续接结构"，则继续留在尾部窗口。
                if (!IsOpenEnded(lastNonBlankLine))
                {
                    boundary = nextLineStart;
                }
            }
            else
            {
                lastNonBlankLine = line.ToString();
            }

            lineStart = nextLineStart;
        }

        return boundary;
    }

    private static bool TryReadFence(ReadOnlySpan<char> line, out char fenceChar, out int fenceCount)
    {
        fenceChar = '\0';
        fenceCount = 0;

        var i = 0;
        while (i < line.Length && i < 3 && line[i] == ' ')
        {
            i++;
        }

        if (i >= line.Length || line[i] is not ('`' or '~'))
        {
            return false;
        }

        fenceChar = line[i];
        while (i < line.Length && line[i] == fenceChar)
        {
            fenceCount++;
            i++;
        }

        return fenceCount >= 3;
    }

    private static bool IsClosingFence(ReadOnlySpan<char> line, char fenceChar, int fenceCount)
    {
        var i = 0;
        while (i < line.Length && i < 3 && line[i] == ' ')
        {
            i++;
        }

        var closing = 0;
        while (i < line.Length && line[i] == fenceChar)
        {
            closing++;
            i++;
        }

        if (closing < fenceCount)
        {
            return false;
        }

        while (i < line.Length)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool IsBlank(ReadOnlySpan<char> line)
    {
        foreach (var ch in line)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>判断上一行是否属于"空行后仍可能续接"的结构。</summary>
    private static bool IsOpenEnded(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // 列表项（含任务列表）
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return true;
        }

        // 有序列表 1. / 1)
        var digits = 0;
        while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits + 1 < trimmed.Length
            && (trimmed[digits] == '.' || trimmed[digits] == ')')
            && trimmed[digits + 1] == ' ')
        {
            return true;
        }

        // 引用
        if (trimmed.StartsWith('>'))
        {
            return true;
        }

        // 表格（任意行）与分隔行
        if (trimmed.StartsWith('|') || trimmed.Contains("---", StringComparison.Ordinal))
        {
            return true;
        }

        // 缩进代码：原始行有 ≥4 前导空格
        var leading = 0;
        while (leading < line.Length && line[leading] == ' ')
        {
            leading++;
        }

        return leading >= 4;
    }
}
