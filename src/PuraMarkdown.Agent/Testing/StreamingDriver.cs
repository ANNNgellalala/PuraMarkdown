namespace PuraMarkdown.Agent.Testing;

/// <summary>流式驱动：把文本按固定大小切块追加，直到 Complete。</summary>
public static class StreamingDriver
{
    /// <summary>AppendInChunksAsync。</summary>
    /// <param name="view">view 参数。</param>
    /// <param name="text">text 参数。</param>
    /// <param name="chunkSize">chunkSize 参数。</param>
    public static async Task AppendInChunksAsync(PuraMarkdownView view, string text, int chunkSize = 7)
    {
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            session.Append(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
        }

        await session.CompleteAsync();
    }
}

