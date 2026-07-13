# ROG 液金检测工具（Windows 原生 WPF）

目录划分：

```text
Source/       可编辑源代码、默认规则、构建脚本和第三方库下载源
Docs/         使用说明、界面决策、第三方组件说明
Release_v*/   按版本区分的可运行 EXE、依赖库与本地报告目录
Package/      构建后生成的 ZIP 发布包
Reports/      项目级自检/开发报告（发布版报告位于 Release/Reports）
```

构建：在 PowerShell 中执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Source\Scripts\Build.ps1 -Package
```

开发环境不需要 .NET SDK；脚本使用 Windows 内置的 .NET Framework 4.8 C# 编译器。

当前版本为 v1.0.12。构建脚本会先运行判定与报告自动化测试，再生成 `Release_v1.0.12` 和对应 ZIP。

首次构建时，如果本地没有第三方运行库，脚本会从 LibreHardwareMonitor 官方 GitHub Release 下载 v0.9.6。下载内容保存在 `Source/.cache/`，不会提交到仓库。

## 隐私与发布范围

仓库不包含本机检测报告、历史发布目录或临时诊断二进制文件。`Reports/` 中可能含有机型、BIOS、温度和时间戳信息，因此默认被 Git 忽略。
