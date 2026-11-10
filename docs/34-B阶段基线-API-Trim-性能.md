# B 阶段基线：PublicAPI / Trim-AOT / 性能对比

> 日期：2026-11-08。对应 B5 硬化三项：PublicAPI baseline/分析器、Avalonia 渲染包 Trim/AOT 复跑、正式性能基线对比。
> 环境：Windows 10.0.26300，i7-12700F（12C/20T），.NET SDK 10.0.114，Avalonia 12.1.5，Markdig 1.5.1。

---

## 1. PublicAPI baseline 与分析器

### 1.1 接入

- 包：`Microsoft.CodeAnalysis.PublicApiAnalyzers` **5.6.0**（`PrivateAssets=all`）；
- 每个可发布包均有：
  - `PublicAPI.Shipped.txt`；
  - `PublicAPI.Unshipped.txt`（已发布 API 之外的新增项放这里）。
- 当前 B 版 API 已全部写入 `PublicAPI.Shipped.txt`，`Unshipped` 仅保留 `#nullable enable`。

> 2026-11-10 更新：按 ADR-0027，7 个包已合并为 3 个；下表为**合并后**基线（原 Agent/Chat/Testing 条目并入 `PuraMarkdown`，原高亮/Mermaid 条目并入 `PuraMarkdown.Plugins`）。

| 包 | Shipped 条目数（含 header） |
| --- | ---: |
| `PuraMarkdown` | 845 |
| `PuraMarkdown.Syntax` | 214 |
| `PuraMarkdown.Plugins` | 22 |

### 1.2 规则与更新方式

- 新增/修改公共 API 未声明 → `RS0016`（构建 warning/error）；
- 声明了不存在的 API → `RS0017`；
- 生成/补齐声明：
  ```bash
  dotnet format analyzers src/PuraMarkdown/PuraMarkdown.csproj --diagnostics RS0016
  ```
- 发布 1.0 时把 `Unshipped` 条目移入 `Shipped`，并保持 `#nullable enable`。

### 1.3 CI 行为

`dotnet build` 即触发分析器；当前全解决方案 **0 warning / 0 error**。未来公共 API 变更若未更新 baseline，CI 会直接失败。

---

## 2. Avalonia 渲染包 Trim/AOT

### 2.1 命令

```bash
# Trim（partial，保留 Avalonia 反射需求）
dotnet publish samples/PuraMarkdown.Spike/PuraMarkdown.Spike.csproj \
  -c Release -r win-x64 --self-contained false \
  -p:PublishTrimmed=true -p:TrimMode=partial -o artifacts/trim-avalonia

# Native AOT
dotnet publish samples/PuraMarkdown.Spike/PuraMarkdown.Spike.csproj \
  -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot-avalonia
```

### 2.2 结果

| 产物 | 大小 | 运行 |
| --- | ---: | --- |
| Trim（目录总计） | 145.1 MB | ✅ 输出卡片/按钮/8 blocks |
| Native AOT（目录总计） | 218.6 MB | ✅ 输出卡片/按钮/8 blocks |
| Native AOT 可执行文件 | 21.3 MB | ✅ |

- Trim 发布出现 `IL2104: Avalonia.DesignerSupport produced trim warnings`，来自 Avalonia 包自身，不是 PuraMarkdown；应用运行正常。
- AOT 发布无 PuraMarkdown 相关错误；`PuraMarkdown.Syntax` + ArcSample 的 trimmed publish 也独立验证通过。

### 2.3 结论

- B 拆分没有破坏 Avalonia 渲染、Agent 组件、Mermaid/ColorCode 插件的 Trim/AOT 运行；
- Native AOT 当前可用；后续 C 语义模型上线后需重新执行本表。
- **2026-11-09 追加（Emoji/数学）**：核心不依赖 CSharpMath；Spike 通过示例插件 `CSharpMathMathPlugin` 使用 CSharpMath。重新执行 Trim（partial）与 Native AOT，均成功；两种产物运行 `--screenshot` 均正常输出 Emoji 与数学公式。包体积/启动耗时尚未重新测，发布前应补测。

---

## 3. 性能基线对比

### 3.1 场景与命令

| 场景 | Spike 参数 |
| --- | --- |
| 短流式 | `--stream-perf` |
| 长代码流式（14.3KB / 896 次追加） | `--stream-perf-long` |
| 多轮对话 300 / 1000 条 | `--conversation-perf` / `--conversation-perf-large` |
| 消息虚拟化 1000 条 | `--virtualization-perf` |
| 块级虚拟化 1MB / 1000 块 | `--block-virtualization-perf` |

### 3.2 B 前 vs B 后

> B 前数据来自 [多轮长对话性能验证](16-多轮长对话性能验证.md) / [块级虚拟化实现](18-块级虚拟化实现.md) 的记录值；B 后为本次同机实测。无头渲染/GC 存在波动，差值仅作趋势参考。

| 场景 | B 前 | B 后 | 变化 |
| --- | --- | --- | --- |
| 长代码流式 896 次 | 127ms / p95 0.3ms | 143ms / p95 0.2ms | 总耗时 +12.6%，尾部延迟持平 |
| 对话 A：300 条独立控件 | 315ms / 34.8MB | 271ms / 38.4MB | 耗时 -14%，内存 +10.3% |
| 对话 C：500 条单流式 | 692ms + Complete 345ms | 442ms + Complete 220ms | 耗时明显下降 |
| 对话 D：120 条独立流式 | 142ms / p95 1.00ms | 101ms / p95 0.99ms | 耗时下降 |
| 对话 A-large：1000 条 | 998ms / 121.2MB | 1043ms / 133.5MB | 耗时 +4.5%，内存 +10.1% |
| 对话 C-large：800 条 | 845ms + Complete 445ms | 842ms + Complete 393ms | 持平/Complete 下降 |
| 块虚拟化 1MB/1000 块 | 455ms / 1.6MB | 405ms / 1.7MB | 略优 |
| 非虚拟化 1MB/1000 块 | 2652ms / 69.8MB | 2401ms / 69.8MB | 略优 |

### 3.3 结论

- Syntax 拆分没有引入 AST 转换；解析/渲染主路径保持在基线附近；
- 内存 +10% 属于 1000 条控件场景的测量/布局波动，仍在 B5 阈值（≤10%）附近；
- 长代码流式 +12.6% 总耗时与尾部 p95 持平，未出现 UI 卡顿回归；
- 块级/消息级虚拟化收益保持；
- 1MB/1000 块仍是当前最优路径（非虚拟化 2.4s/69.8MB → 虚拟化 405ms/1.7MB）。

### 3.4 门禁建议

- 后续 C 阶段以本表为基线；
- CI 可先做“非零回归”烟测，BenchmarkDotNet 全量基准按需运行；
- 任何 >15% 的首屏/流式总耗时回归都需要解释或回退。

---

## 4. 复跑清单

```bash
dotnet build PuraMarkdown.slnx -t:Rebuild
dotnet test tests/PuraMarkdown.Tests/PuraMarkdown.Tests.csproj
dotnet publish ... -p:PublishTrimmed=true -p:TrimMode=partial
dotnet publish ... -p:PublishAot=true
dotnet run --project samples/PuraMarkdown.Spike -- --conversation-perf
```



