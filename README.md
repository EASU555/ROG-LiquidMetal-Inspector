# ROG 液金检测工具（Windows 原生 WPF）

当前版本：v1.1.0。

这是一个只读的 CPU、GPU 与双烤散热接触异常筛查工具。它采集温度、功耗、负载、频率、P-Core/E-Core 组内温差和可用的 GPU Hot Spot 数据，生成 CSV、JSON 与 HTML 报告。程序不会修改风扇、功耗或电压。

v1.1.0 将原来的单阈值判断升级为多证据模型：

- 空间证据：P-Core/E-Core 分组后的温差 P95、持续时间、固定热点重复率、可用时的 GPU Hot Spot 差。
- 热阻证据：高负载下持续高温与异常低功耗同时出现。
- 时间证据：稳定窗口首末段的功耗与频率保持率、温度/功耗趋势。
- 基线证据：低负载空闲高温只作为辅助线索。
- 数据质量：有效样本率、测试时长、负载/功耗波动、传感器缺失与机型档案匹配。
- 双烤解释：按 CPU+GPU 总功耗和分组件证据解释 NVIDIA Dynamic Boost，不要求两者同时达到单烤上限。

任何单一高温、温差或瓦数都不会直接判定液金偏移。异常结论建议在相同室温、档位和摆放条件下测试三次，并至少复现两次。软件只能筛查“散热接触/热路径异常”，不能在不拆机时确认液金实体偏移、泄漏或具体界面材料故障。

## 目录

```text
Source/       源代码、规则、机型档案、构建脚本和第三方库下载源
Docs/         使用说明、判定规则与研究报告
Release_v*/   按版本区分的可运行 EXE、依赖库和本地报告目录
Package/      可直接解压运行的 ZIP 发布包
```

## 构建

在 PowerShell 中执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Source\Scripts\Build.ps1 -Package
```

脚本使用 Windows 自带的 .NET Framework 4.8 C# 编译器，先运行自动化测试，再生成 `Release_v1.1.0` 和 `Package/ROG-LiquidMetal-Inspector_v1.1.0_win-x64.zip`。首次构建时如本地缺少运行库，会从 LibreHardwareMonitor 官方 GitHub Release 下载 v0.9.6。

本机检测报告包含机型、BIOS、温度和时间戳等信息，默认由 Git 忽略，不上传到公开仓库。
