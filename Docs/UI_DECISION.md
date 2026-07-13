# 界面决策：ROG 液金异常筛查工具

## UI decision brief

- Surface type：Windows 监测工具
- Platform idiom：原生 Windows / WPF；保留系统标题栏与窗口吸附行为
- Product thesis：在固定负载中把“温差、功耗、频率、风扇与环境”转换为可复核的散热接触异常证据
- Desktop archetype：Monitoring cockpit（监测驾驶舱）
- Main object：一次检测运行（run）及其原始采样记录
- Composition archetype：Inspection bay（检测台）
- Distinctive anchor：持续更新的“当前阶段 / 关键温差 / 判定状态”仪表区
- Layout sketch：

```text
[本机与传感器状态] [一键完整检测 / 快速筛查 / 停止]
[检测阶段与实时读数] [判定证据与安全中止条件]
[采样表格] [运行日志与报告路径]
```

- Density：operational；优先显示可判断性和当前风险，不用装饰性卡片替代数据
- Hierarchy：开始检测是主操作；停止和打开报告为次操作；日志是可复核证据
- Typography：Segoe UI Variable；等宽字段仅用于传感器和文件路径
- Color/materials：系统浅色背景、蓝色信息、橙色复测、红色安全中止；不依赖高饱和装饰
- Motion budget：subtle；仅阶段进度和日志追加，不用持续动画
- State coverage：传感器缺失、非管理员、测试中止、快速筛查、完成、无法判断
- Bans：不使用“侧栏 + 装饰卡片 + 表格”的通用后台壳；不伪造 Mica、不自绘标题栏、不自动改风扇/功耗

## 原创性约束

- Subject：ROG 笔记本散热接触异常筛查
- Product world：安静的硬件检测台，而不是游戏控制中心
- Repeated motif：每个阶段都留下带时间戳的证据条目
- One deliberate move：将“是否可判断”置于温度数字之前；避免把高温直接等同于液金偏移
- Restraints：Windows 原生控件为主，颜色只用于语义状态，所有结论可追溯到原始 CSV
