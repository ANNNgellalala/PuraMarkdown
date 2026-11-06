# ADR-0005：目标框架采用 net8.0 单目标

- 状态：**被 ADR-0013 取代**（2026-11-05：升级为 net10.0，按用户要求使用最新 .NET）
- 日期：2026-11-05
- 关联：ADR-0008（Avalonia 12 优先）

## 背景

需要决定各包的目标框架（TFM）。候选：

1. `net8.0` 单目标；
2. `net8.0` + `netstandard2.0` 多目标（兼容 .NET Framework/WPF 宿主、Unity 等）；
3. 更高版本（net9.0/net10.0）。

## 决策

**全部包统一 `net8.0` 单目标**（含 `PuraMarkdown.Abstractions`）。

## 理由

- 平台已定为 Avalonia 12 桌面优先（ADR-0008），其最低要求即 .NET 8；1.0 不承诺 WPF/.NET Framework 宿主。
- Markdig 1.x 提供 `net8.0` 资产，可直接引用，无需 `System.Memory` 等兼容包。
- 单目标避免 `#if` 分支与多目标测试矩阵，降低维护成本；可用现代 BCL（`System.Text.Json`、`ImmutableArray`、`Span`）。
- `Abstractions` 若为 `netstandard2.0`，会限制其使用 record/required/JSON 源生成等能力，收益低。

## 后果

- 宿主必须是 .NET 8+（Avalonia 12 / ASP.NET Core 8+ / 控制台）。
- 若未来需要 WPF 或 .NET Framework 宿主：新增 `netstandard2.0` 目标，代价是 `Abstractions` 与 `Core` 的多目标适配（预计 5–8 人日），届时另立 ADR。
- AOT/裁剪验证以 `net8.0` 为唯一基线。

## 备选与回退

若 1.0 发布前明确出现 WPF 宿主需求，则改为 `net8.0;netstandard2.0` 多目标，并优先保证 `Abstractions` 兼容。
