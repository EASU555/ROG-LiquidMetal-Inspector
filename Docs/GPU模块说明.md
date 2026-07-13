# GPU 单烤与双烤模块

## v1.0.12 负载校验

- GPU 压力源由旧版 WinSAT D3D 改为程序内置 OpenCL 计算内核。
- OpenCL 会枚举 GPU，并优先选择 NVIDIA/AMD 独立显卡；本机锁定为 NVIDIA GeForce RTX 5070 Ti Laptop GPU。
- GPU 阶段启动后会同时校验真实传感器数据：GPU Load 达到 85%，并且 GPU Power 达到 50W，才视为压力链路已经建立。50W 仅用于排除“压力根本没有跑起来”，不是机型正常功耗标准。
- 20 秒仍未建立负载，或已经建立后 GPU Load 连续 10 秒低于 40%，检测立即中止，不生成液金判断。
- 分析窗口内平均 GPU Load 仍需达到机型档案要求；低负载不允许输出绿色结论。
- 高负载但功耗偏低不会被误判成“压力没运行”，而会进入机型功耗与高温低功耗分析。
- LibreHardwareMonitor 只读取选中的独显，避免 Intel 核显和 NVIDIA 独显字段混合。

## 检测状态显示

首屏显示 GPU 温度、显存温度、GPU Power 和 GPU Load。独显详情行显示：

- 独显名称
- 核心频率与显存频率
- VRAM 已用/总量
- GPU 热点（驱动提供时）
- GPU 风扇（固件/驱动提供时）
- OpenCL 压力源状态与调度计数

本机 NVIDIA 驱动没有向 LibreHardwareMonitor 提供 GPU Hot Spot 和独立 GPU 风扇转速，因此这两个字段会显示“—”或“未报告/停转”；这属于驱动未暴露字段，不会再用 0 冒充有效读数。GPU 核心温度和 GPU Memory Junction（显存温度）可正常读取。

## 本机集成验证

2026-07-13 在 G815LR / RTX 5070 Ti Laptop GPU 上运行 12 秒压力与传感器集成测试：

- GPU Load：约 96–98%
- GPU Power：稳定后约 126–129W
- GPU 核心频率：约 2.74GHz
- 显存频率：约 14.1GHz
- GPU 温度：测试末约 74°C
- 显存温度：约 60–64°C
- VRAM 总量：12227MB

以上是压力源有效性的程序验证数据，不是液金正常与否的判定样本。完整结论仍需按完整检测时长采集稳态数据。

## 机型参考边界

ASUS 官方规格列出 G815LR 的 RTX 5070 Ti Laptop GPU 为 115W + 25W Dynamic Boost、最高 140W。140W 是最高动态规格，不代表 CPU+GPU 双烤时 GPU 必须始终保持 140W；双烤会受到 280W 适配器、整机功耗预算、温度和 Dynamic Boost 分配影响。

来源：

- [ASUS ROG Strix G18 (2025) G815 官方规格](https://rog.asus.com/gr-en/laptops/rog-strix/rog-strix-g18-2025/spec/?config=90NR0LC1-M004F0)
- [LibreHardwareMonitor 开源项目](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

## 安全边界

GPU 温度达到 90°C 并持续 3 秒时，程序会安全停止并保存已采集数据。双烤期间 CPU 达到安全阈值也会停止；中止报告明确标记为不完整，不能显示绿色结论。程序只施加计算负载并读取传感器，不修改风扇曲线、功耗限制、电压或显卡驱动设置。
