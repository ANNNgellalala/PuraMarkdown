using PuraMarkdown.Syntax;

var markdown = """
# 账单概览

本月支出 **1,280.00** 元。

| 类别 | 金额 |
| --- | ---: |
| 餐饮 | 320 |
| 交通 | 180 |
""";

var syntax = new PuraSyntaxBuilder()
    .UseCommonMark()
    .UseGfm()
    .Build();

var document = syntax.ParseDocument(markdown, version: 1);

Console.WriteLine($"version={document.Version}");
Console.WriteLine($"blocks={document.Markdown.Count}");
foreach (var heading in document.Outline.Headings)
{
    Console.WriteLine($"h{heading.Level} {heading.Text} #{heading.Anchor}");
}

foreach (var block in document.Markdown)
{
    Console.WriteLine(block.GetType().Name);
}

foreach (var diagnostic in document.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}

Console.WriteLine("Arc.Avalonia 可在此 MarkdownDocument 上构建自定义控件，无需引用 Avalonia 渲染包。");
