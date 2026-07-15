using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RogLiquidMetalInspector
{
    internal static class Program
    {
        internal static string VersionText
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }
        [STAThread]
        private static void Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                RunSelfTest(root);
                return;
            }
            Application app = new Application();
            app.Run(new MainWindow(root));
        }

        private static void RunSelfTest(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "Reports"));
            using (SensorReader reader = new SensorReader(root))
            {
                MachineInfo info = SensorReader.GetMachineInfo();
                Sample sample = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Stopwatch readClock = Stopwatch.StartNew();
                    sample = reader.Read("Self test");
                    readClock.Stop();
                    sample.SensorReadDurationMilliseconds = readClock.Elapsed.TotalMilliseconds;
                    if (!string.IsNullOrWhiteSpace(sample.GpuPciBusId)) break;
                    Thread.Sleep(250);
                }
                string report = string.Join(Environment.NewLine, new string[] {
                    "ROG 液金检测工具自检", "Time=" + DateTime.Now.ToString("o"), "Model=" + info.Model, "CPU=" + info.Cpu,
                    "Administrator=" + info.IsAdministrator, "SensorSource=" + reader.Source, "SensorReadMs=" + sample.SensorReadDurationMilliseconds.ToString("F0"), "PackageTemperature=" + sample.PackageTemperature.ToString("F1"),
                    "CoreCount=" + sample.CoreTemperatures.Count, "PackagePower=" + sample.PackagePower.ToString("F1"),
                    "CpuLoad=" + sample.CpuLoad.ToString("F1"), "CpuLoadAvailable=" + sample.CpuLoadAvailable,
                    "GPU=" + sample.GpuName, "GpuDriver=" + info.GpuDriver, "GpuPciBusId=" + sample.GpuPciBusId, "GpuTemperature=" + sample.GpuTemperature.ToString("F1"), "GpuPower=" + sample.GpuPower.ToString("F1"),
                    "GpuLoad=" + sample.GpuLoad.ToString("F1"), "GpuLoadAvailable=" + sample.GpuLoadAvailable, "GpuClock=" + sample.GpuCoreClock.ToString("F0"), "GpuMemoryTemperature=" + sample.GpuMemoryTemperature.ToString("F1"),
                    "GpuClockEventReasons=" + sample.GpuClockEventReasons, "GpuThermalLimited=" + sample.GpuThermalLimited, "GpuPowerLimited=" + sample.GpuPowerLimited,
                    "CpuFan=" + sample.FanRpm.ToString("F0"), "SystemFan=" + sample.SystemFanRpm.ToString("F0"), "GpuFan=" + sample.GpuFanRpm.ToString("F0"),
                    "Error=" + reader.LastError
                });
                File.WriteAllText(Path.Combine(root, "Reports", "self-test.txt"), report);
            }
        }
    }

    public sealed class MainWindow : Window
    {
        private readonly string _root;
        private readonly MachineInfo _machine;
        private readonly MachineProfile _profile;
        private readonly RulesConfig _rules;
        private readonly List<Sample> _samples = new List<Sample>();
        private readonly ObservableCollection<Sample> _sampleView = new ObservableCollection<Sample>();
        private readonly ObservableCollection<string> _logs = new ObservableCollection<string>();
        private ISensorProvider _sensor;
        private CancellationTokenSource _cancellation;
        private TextBox _roomTemperature;
        private ComboBox _mode;
        private ComboBox _testMode;
        private TextBlock _phase;
        private TextBlock _remaining;
        private TextBlock _packageTemp;
        private TextBlock _power;
        private TextBlock _coreDelta;
        private TextBlock _gpuTemp;
        private TextBlock _gpuPower;
        private TextBlock _gpuMemoryTemp;
        private TextBlock _gpuLoad;
        private TextBlock _gpuDetails;
        private TextBlock _sensorState;
        private TextBlock _verdict;
        private TextBlock _reason;
        private TextBlock _profileInfo;
        private Button _fullButton;
        private Button _quickButton;
        private Button _stopButton;
        private Button _reportButton;
        private string _lastReportFolder;
        private GpuStress _activeGpuStress;
        private string _runStatus;
        private string _stopReason;
        private Stopwatch _runClock;
        private DateTime _runStartedAt;

        public MainWindow(string root)
        {
            _root = root;
            _machine = SensorReader.GetMachineInfo();
            _profile = ProfileLoader.LoadForMachine(root, _machine);
            _rules = RulesConfig.Load(root);
            Title = "ROG 液金检测工具 v" + Program.VersionText + " — CPU / GPU / 双烤";
            Width = Math.Min(1040, SystemParameters.WorkArea.Width);
            Height = Math.Min(700, SystemParameters.WorkArea.Height);
            MinWidth = 760;
            MinHeight = 480;
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable");
            Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
            Content = BuildLayout();
            AddLog("已就绪。规则版本 " + _rules.Version + "；完整检测约 " + FormatMinutes(10 + _rules.IdleSeconds + _rules.HotspotProbeSeconds + _rules.PrimarySeconds) + "，快速筛查约 " + FormatMinutes(10 + _rules.QuickIdleSeconds + _rules.QuickHotspotProbeSeconds + _rules.QuickPrimarySeconds) + "。");
        }

        private UIElement BuildLayout()
        {
            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260) });
            UIElement header = BuildHeader();
            Grid.SetRow(header, 0); root.Children.Add(header);
            TabControl tabs = BuildTabs();
            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);
            Grid main = new Grid { Margin = new Thickness(0, 14, 0, 12) };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            UIElement preparation = BuildPreparationPanel();
            UIElement monitor = BuildMonitorPanel();
            Grid.SetColumn(preparation, 0); Grid.SetColumn(monitor, 1);
            main.Children.Add(preparation);
            main.Children.Add(monitor);
            Grid.SetRow(main, 1);
            root.Children.Add(main);
            return root;
        }

        private UIElement BuildHeader()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(new TextBlock { Text = "ROG 液金检测工具", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brush("#143A5E") });
            title.Children.Add(new TextBlock { Text = "v" + Program.VersionText + " · OpenCL 独显压力 · CPU 单烤 · GPU 单烤 · CPU+GPU 双烤", FontSize = 14, Foreground = Brush("#52616B"), Margin = new Thickness(0, 3, 0, 0) });
            grid.Children.Add(title);
            TextBlock safe = new TextBlock { Text = "只读检测  ·  不修改风扇/功耗/电压", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#0F6B4D"), FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(safe, 1); grid.Children.Add(safe);
            return grid;
        }

        private UIElement BuildPreparationPanel()
        {
            StackPanel panel = Panel("检测准备");
            Grid selectors = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel workloadBox = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
            workloadBox.Children.Add(new TextBlock { Text = "测试模块", FontWeight = FontWeights.SemiBold, Foreground = Brush("#143A5E") });
            _testMode = new ComboBox { Margin = new Thickness(0, 4, 0, 0), SelectedIndex = 0 };
            _testMode.Items.Add("CPU 单烤"); _testMode.Items.Add("GPU 单烤"); _testMode.Items.Add("CPU + GPU 双烤");
            workloadBox.Children.Add(_testMode);
            StackPanel performanceBox = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
            performanceBox.Children.Add(new TextBlock { Text = "性能档位", FontWeight = FontWeights.SemiBold, Foreground = Brush("#143A5E") });
            _mode = new ComboBox { Margin = new Thickness(0, 4, 0, 0), SelectedIndex = 2 };
            _mode.Items.Add("静音 / Silent"); _mode.Items.Add("性能 / Performance"); _mode.Items.Add("增强 / Turbo"); _mode.Items.Add("手动 / Manual");
            performanceBox.Children.Add(_mode);
            Grid.SetColumn(performanceBox, 1); selectors.Children.Add(workloadBox); selectors.Children.Add(performanceBox);
            panel.Children.Add(selectors);
            Grid commands = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            commands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            commands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            commands.RowDefinitions.Add(new RowDefinition()); commands.RowDefinitions.Add(new RowDefinition());
            _fullButton = Button("完整检测", "#0F6CBD", StartFull);
            _quickButton = Button("快速筛查", "#255C88", StartQuick);
            _stopButton = Button("安全停止", "#9E2F2F", StopRun); _stopButton.IsEnabled = false;
            _reportButton = Button("打开报告", "#4F6675", OpenReport); _reportButton.IsEnabled = false;
            Grid.SetColumn(_quickButton, 1); Grid.SetRow(_stopButton, 1); Grid.SetColumn(_reportButton, 1); Grid.SetRow(_reportButton, 1);
            commands.Children.Add(_fullButton); commands.Children.Add(_quickButton); commands.Children.Add(_stopButton); commands.Children.Add(_reportButton);
            panel.Children.Add(commands);
            AddPair(panel, "机型", _machine.Model);
            AddPair(panel, "CPU", _machine.Cpu);
            AddPair(panel, "独立显卡", _machine.Gpu);
            AddPair(panel, "BIOS", _machine.Bios);
            AddPair(panel, "管理员权限", _machine.IsAdministrator ? "已获取（传感器直接读取）" : "未获取：请重新启动并允许 UAC");
            panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "室温（°C）", FontWeight = FontWeights.SemiBold });
            _roomTemperature = new TextBox { Text = "25", Margin = new Thickness(0, 5, 0, 10), Width = 110 };
            panel.Children.Add(_roomTemperature);
            _profileInfo = new TextBlock
            {
                Text = ProfileLoader.Summary(_profile, _mode.SelectedItem.ToString(), _testMode.SelectedItem.ToString()),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#52616B"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Action refreshProfile = delegate { if (_profileInfo != null && _mode.SelectedItem != null && _testMode.SelectedItem != null) _profileInfo.Text = ProfileLoader.Summary(_profile, _mode.SelectedItem.ToString(), _testMode.SelectedItem.ToString()); };
            _mode.SelectionChanged += delegate { refreshProfile(); };
            _testMode.SelectionChanged += delegate { refreshProfile(); };
            panel.Children.Add(_profileInfo);
            return Wrap(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 0) });
        }

        private UIElement BuildLivePanel()
        {
            StackPanel panel = Panel("实时检测");
            Grid metrics = new Grid();
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement phase = Metric("当前阶段", "等待开始", out _phase);
            UIElement temperature = Metric("CPU 温度", "—", out _packageTemp);
            UIElement power = Metric("Package Power", "—", out _power);
            Grid.SetColumn(phase, 0); Grid.SetColumn(temperature, 1); Grid.SetColumn(power, 2);
            metrics.Children.Add(phase); metrics.Children.Add(temperature); metrics.Children.Add(power);
            panel.Children.Add(metrics);
            Grid detail = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement remaining = Metric("剩余时间", "—", out _remaining); UIElement delta = Metric("核心温差", "—", out _coreDelta);
            Grid.SetColumn(delta, 1); detail.Children.Add(remaining); detail.Children.Add(delta); panel.Children.Add(detail);
            _sensorState = new TextBlock { Text = "传感器：未初始化", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#52616B"), Margin = new Thickness(0, 0, 0, 0) };
            panel.Children.Add(_sensorState);
            return Wrap(panel);
        }

        private UIElement BuildEvidencePanel()
        {
            StackPanel panel = Panel("判定与安全边界");
            _verdict = new TextBlock { Text = "尚未运行", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#475467"), TextWrapping = TextWrapping.Wrap };
            _reason = new TextBlock { Text = "完整检测会按：预检 → 空闲基线 → 热点探测 → CPU 主测记录原始数据。", TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 9, 0, 12), Foreground = Brush("#334155") };
            panel.Children.Add(_verdict); panel.Children.Add(_reason);
            panel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 10) });
            AddBullet(panel, "单一核心温差、高温或低功耗只形成一条线索，不直接判液金偏移。");
            AddBullet(panel, "至少两类独立证据，并通过负载、时长和样本覆盖率置信度校验后，才输出“疑似”。");
            AddBullet(panel, "缺少关键传感器、负载不稳或分析时间不足：输出“无法判断/建议复测”。");
            AddBullet(panel, "温度持续贴近 100°C：自动停止并保留证据。");
            panel.Children.Add(new TextBlock { Text = "注意：程序不能在不拆机的情况下确认液金的物理偏移或泄漏。", Foreground = Brush("#8A4B0F"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            return Wrap(panel);
        }

        private UIElement BuildMonitorPanel()
        {
            StackPanel panel = Panel("检测状态与结论");
            Grid metrics = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement phase = Metric("当前阶段", "等待开始", out _phase);
            UIElement remaining = Metric("剩余时间", "—", out _remaining);
            UIElement temperature = Metric("CPU 温度", "—", out _packageTemp);
            UIElement power = Metric("Package Power", "—", out _power);
            Grid.SetColumn(remaining, 1); Grid.SetColumn(temperature, 2); Grid.SetColumn(power, 3);
            metrics.Children.Add(phase); metrics.Children.Add(remaining); metrics.Children.Add(temperature); metrics.Children.Add(power);
            panel.Children.Add(metrics);

            Grid detail = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            for (int i = 0; i < 5; i++) detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement delta = Metric("核心温差", "—", out _coreDelta);
            UIElement gpuTemp = Metric("GPU 温度", "—", out _gpuTemp);
            UIElement gpuMemoryTemp = Metric("显存温度", "—", out _gpuMemoryTemp);
            UIElement gpuPower = Metric("GPU Power", "—", out _gpuPower);
            UIElement gpuLoad = Metric("GPU Load", "—", out _gpuLoad);
            Grid.SetColumn(gpuTemp, 1); Grid.SetColumn(gpuMemoryTemp, 2); Grid.SetColumn(gpuPower, 3); Grid.SetColumn(gpuLoad, 4);
            detail.Children.Add(delta); detail.Children.Add(gpuTemp); detail.Children.Add(gpuMemoryTemp); detail.Children.Add(gpuPower); detail.Children.Add(gpuLoad);
            panel.Children.Add(detail);
            _gpuDetails = new TextBlock { Text = "独显详情：等待传感器初始化", Foreground = Brush("#344054"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 3) };
            panel.Children.Add(_gpuDetails);
            _sensorState = new TextBlock { Text = "传感器：未初始化", Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(_sensorState);
            panel.Children.Add(new Separator { Margin = new Thickness(0, 3, 0, 8) });

            panel.Children.Add(new TextBlock { Text = "判定与安全边界", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#143A5E"), Margin = new Thickness(0, 0, 0, 4) });
            _verdict = new TextBlock { Text = "尚未运行", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#475467"), TextWrapping = TextWrapping.Wrap };
            _reason = new TextBlock { Text = "完整检测会按：预检 → 空闲基线 → 热点探测 → CPU 主测记录原始数据。", TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 4, 0, 6), Foreground = Brush("#334155") };
            panel.Children.Add(_verdict); panel.Children.Add(_reason);
            AddBullet(panel, "综合空间温差、固定热点、高温低功耗、功耗/频率保持率和时间趋势；单一高温不计异常分。");
            AddBullet(panel, "至少两类独立证据且置信度达标才输出“疑似”；快速筛查与不完整测试不形成最终结论。");
            AddBullet(panel, "CPU 100°C 或 GPU 90°C 持续达到安全时长会自动停止并保留证据。");
            panel.Children.Add(new TextBlock { Text = "软件只能筛查散热接触异常，不能在不拆机时确认液金的实体偏移或泄漏。", Foreground = Brush("#8A4B0F"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            return Wrap(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 0) });
        }

        private TabControl BuildTabs()
        {
            TabControl tabs = new TabControl();
            DataGrid grid = new DataGrid { ItemsSource = _sampleView, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
            grid.Columns.Add(Column("时间", "Time", 145)); grid.Columns.Add(Column("阶段", "Phase", 105)); grid.Columns.Add(Column("温度 °C", "PackageTemperature", 80));
            grid.Columns.Add(Column("功耗 W", "PackagePower", 80)); grid.Columns.Add(Column("P核温差 °C", "PCoreDelta", 92)); grid.Columns.Add(Column("E核温差 °C", "ECoreDelta", 92)); grid.Columns.Add(Column("热点", "HottestCore", 120));
            grid.Columns.Add(Column("GPU °C", "GpuTemperature", 80)); grid.Columns.Add(Column("GPU 热点 °C", "GpuHotSpotTemperature", 100)); grid.Columns.Add(Column("GPU W", "GpuPower", 80));
            grid.Columns.Add(Column("GPU %", "GpuLoad", 75)); grid.Columns.Add(Column("GPU MHz", "GpuCoreClock", 90)); grid.Columns.Add(Column("显存温度 °C", "GpuMemoryTemperature", 105)); grid.Columns.Add(Column("来源", "SensorSource", 150));
            tabs.Items.Add(new TabItem { Header = "实时采样", Content = grid });
            ListBox log = new ListBox { ItemsSource = _logs, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            tabs.Items.Add(new TabItem { Header = "运行日志", Content = log });
            return tabs;
        }

        private async void StartFull(object sender, RoutedEventArgs e) { await StartRun(false); }
        private async void StartQuick(object sender, RoutedEventArgs e) { await StartRun(true); }

        private async Task StartRun(bool quick)
        {
            if (_cancellation != null) return;
            double room;
            if (!double.TryParse(_roomTemperature.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out room) || room < 0 || room > 50)
            {
                MessageBox.Show("请输入 0–50°C 之间的室温。", "室温无效", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            RunConfiguration config = RunConfiguration.Create(quick, room, _mode.SelectedItem.ToString(), _testMode.SelectedItem.ToString(), _rules);
            config.ProfileHash = _profile == null ? string.Empty : _profile.SourceHash;
            if (_profile != null && !string.IsNullOrWhiteSpace(_profile.RequiredConditions))
            {
                MessageBoxResult confirmation = MessageBox.Show("请确认本次测试满足以下条件：\n\n" + _profile.RequiredConditions +
                    "\n\n当前模块：" + config.TestMode + "\n当前档位：" + config.PerformanceMode + "\n\n满足请选择“是”；不满足将取消测试。",
                    "确认测试条件", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmation != MessageBoxResult.Yes) return;
                config.ConditionsConfirmed = true;
            }
            if (_rules.IsModified || !string.IsNullOrWhiteSpace(_rules.ValidationWarning))
            {
                MessageBoxResult ruleConfirmation = MessageBox.Show((_rules.ValidationWarning ?? "规则参数与内置默认值不同。") +
                    "\n\n规则 SHA-256：" + (_rules.SourceHash ?? "未记录") + "\n\n继续测试吗？",
                    "规则文件提示", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ruleConfirmation != MessageBoxResult.Yes) return;
            }
            HistoryStore.Prepare(_root, _machine, config);
            _samples.Clear(); _sampleView.Clear(); _lastReportFolder = null; _reportButton.IsEnabled = false;
            _runStatus = "Running"; _stopReason = string.Empty;
            _cancellation = new CancellationTokenSource(); SetRunning(true);
            _runStartedAt = DateTime.Now;
            _runClock = Stopwatch.StartNew();
            GpuModeReference gpuReference = _profile == null ? null : _profile.FindGpuMode(config.PerformanceMode);
            if (gpuReference != null && config.TestMode != "CPU 单烤")
            {
                config.GpuLoadEstablishedPct = Math.Max(config.GpuLoadEstablishedPct, gpuReference.MinimumLoadPct);
            }
            if (_profile != null) AddLog("已匹配机型档案：" + _profile.ProfileName + "。" + ProfileLoader.Summary(_profile, config.PerformanceMode, config.TestMode));
            _sensor = new SensorReader(_root);
            AddLog("传感器来源：" + _sensor.Source + "。" + _sensor.LastError);
            if (!_sensor.IsReady)
            {
                MessageBox.Show("传感器未初始化，检测没有开始。\n" + _sensor.LastError, "无法启动检测", MessageBoxButton.OK, MessageBoxImage.Warning);
                _sensor.Dispose(); _sensor = null;
                if (_runClock != null) _runClock.Stop();
                _cancellation.Dispose(); _cancellation = null; SetRunning(false);
                return;
            }
            if (!_machine.IsAdministrator) AddLog("提示：当前非管理员运行，部分 CPU 传感器可能无法读取。");
            string sensorError = string.Empty;
            try
            {
                await RunPhase("预检", 10, 0, false, config, _cancellation.Token);
                string validationError = ValidatePreflight(config);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    _runStatus = "SensorFailed"; _stopReason = validationError;
                    throw new InvalidOperationException(validationError);
                }
                await RunPhase("空闲基线", config.IdleSeconds, 0, false, config, _cancellation.Token);
                bool cpuLoad = config.TestMode != "GPU 单烤";
                bool gpuLoad = config.TestMode != "CPU 单烤";
                await RunPhase("热点探测", config.ProbeSeconds, cpuLoad ? 1 : 0, gpuLoad, config, _cancellation.Token);
                string mainPhase = config.TestMode == "CPU 单烤" ? "CPU 主测" : config.TestMode == "GPU 单烤" ? "GPU 主测" : "双烤主测";
                await RunPhase(mainPhase, config.PrimarySeconds, cpuLoad ? Math.Max(1, Environment.ProcessorCount) : 0, gpuLoad, config, _cancellation.Token);
                _runStatus = "Completed";
            }
            catch (OperationCanceledException)
            {
                if (_runStatus == "Running") { _runStatus = "UserStopped"; _stopReason = "用户请求安全停止。"; }
            }
            catch (Exception ex)
            {
                try { _cancellation.Cancel(); } catch { }
                if (_runStatus == "Running") _runStatus = "Error";
                if (string.IsNullOrWhiteSpace(_stopReason)) _stopReason = ex.Message;
                AddLog("运行失败：" + ex.Message);
            }
            finally
            {
                try { _cancellation.Cancel(); } catch { }
                if (_runClock != null) _runClock.Stop();
                sensorError = _sensor == null ? string.Empty : _sensor.LastError;
                if (_sensor != null)
                {
                    ISensorProvider closing = _sensor; _sensor = null;
                    try { Task.Run(() => closing.Dispose()).Wait(3000); } catch { }
                }
                _cancellation.Dispose(); _cancellation = null; SetRunning(false);
            }
            FinishRun(config, _runStatus, _stopReason, sensorError);
        }

        private async Task RunPhase(string name, int seconds, int workers, bool gpuLoad, RunConfiguration config, CancellationToken token)
        {
            CpuStress stress = null;
            GpuStress gpu = null;
            bool cpuLoad = workers > 0;
            try
            {
                if (cpuLoad) { stress = new CpuStress(); stress.Start(workers, token); }
                if (gpuLoad)
                {
                    gpu = new GpuStress(); _activeGpuStress = gpu; gpu.Start(token);
                    bool initialized = await Task.Run(() => gpu.WaitUntilInitialized(15000), token);
                    if (!initialized) { _runStatus = "StressFailed"; throw new InvalidOperationException("GPU 压力源未能启动。" + gpu.LastError); }
                    config.StressGpuDeviceName = gpu.DeviceName;
                    AddLog(gpu.Status + "。程序将在 " + _rules.GpuEstablishTimeoutSeconds + " 秒内校验实际 GPU Load 与 Power。");
                }
                AddLog("进入阶段：" + name + "（按真实时间 " + seconds + " 秒）。");
                Stopwatch phaseClock = Stopwatch.StartNew();
                double nextSampleAt = 0;
                bool gpuLoadEstablished = !gpuLoad;
                bool cpuLoadEstablished = workers < 2;
                DateTime? gpuLowSince = null, cpuLowSince = null, cpuHotSince = null, gpuHotSince = null;
                DateTime dispatchChangedAt = DateTime.Now;
                long lastDispatch = gpu == null ? 0 : gpu.DispatchCount;
                while (phaseClock.Elapsed.TotalSeconds < seconds)
                {
                    token.ThrowIfCancellationRequested();
                    Stopwatch sensorReadClock = Stopwatch.StartNew();
                    Task<Sample> readTask = Task.Run(() => _sensor.Read(name));
                    Task completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(config.SensorReadTimeoutSeconds), token));
                    if (completed != readTask)
                    {
                        token.ThrowIfCancellationRequested();
                        _runStatus = "SensorFailed";
                        throw new TimeoutException("传感器读取超过 " + config.SensorReadTimeoutSeconds + " 秒；压力负载已停止，当前测试无效。");
                    }
                    Sample sample = await readTask;
                    sensorReadClock.Stop();
                    sample.SensorReadDurationMilliseconds = sensorReadClock.Elapsed.TotalMilliseconds;
                    if (_runClock != null)
                    {
                        sample.ElapsedSeconds = _runClock.Elapsed.TotalSeconds;
                        sample.Time = _runStartedAt.AddSeconds(sample.ElapsedSeconds);
                    }
                    if (gpuLoad && !string.IsNullOrWhiteSpace(gpu.DeviceName) && !string.IsNullOrWhiteSpace(sample.GpuName) &&
                        !NvidiaSmiTelemetry.NamesMatch(gpu.DeviceName, sample.GpuName))
                    {
                        _runStatus = "StressFailed";
                        throw new InvalidOperationException("GPU 设备不一致：压力运行在“" + gpu.DeviceName + "”，传感器读取“" + sample.GpuName + "”。");
                    }
                    AddSample(sample);
                    int left = Math.Max(0, (int)Math.Ceiling(seconds - phaseClock.Elapsed.TotalSeconds));
                    _phase.Text = name; _remaining.Text = FormatSeconds(left); UpdateReadings(sample);
                    DateTime now = DateTime.Now;

                    if (workers >= 2 && (sample.CpuLoad >= 70 || sample.PackagePower >= 40)) cpuLoadEstablished = true;
                    if (workers >= 2 && !cpuLoadEstablished && phaseClock.Elapsed.TotalSeconds >= 20)
                    {
                        _runStatus = "StressFailed";
                        throw new InvalidOperationException("CPU 压力未建立：20 秒后 CPU Load/Package Power 仍不足。检测已中止。");
                    }
                    if (workers >= 2 && cpuLoadEstablished && sample.CpuLoadAvailable && sample.CpuLoad < 50 && sample.PackagePower < 30)
                        cpuLowSince = cpuLowSince ?? now;
                    else cpuLowSince = null;
                    if (cpuLowSince.HasValue && (now - cpuLowSince.Value).TotalSeconds >= config.GpuDropTimeoutSeconds)
                    {
                        _runStatus = "StressFailed";
                        throw new InvalidOperationException("CPU 压力持续掉载，检测已中止；当前数据不会生成正常结论。");
                    }
                    if (gpuLoad)
                    {
                        if (gpu.IsFailed) { _runStatus = "StressFailed"; throw new InvalidOperationException(gpu.Status); }
                        if (gpu.DispatchCount != lastDispatch) { lastDispatch = gpu.DispatchCount; dispatchChangedAt = now; }
                        else if ((now - dispatchChangedAt).TotalSeconds >= 5) { _runStatus = "StressFailed"; throw new InvalidOperationException("OpenCL 压力调度已停止增长，检测中止。"); }
                        bool loaded = sample.GpuLoad >= config.GpuLoadEstablishedPct && sample.GpuPower >= config.GpuPowerEstablishedW;
                        if (loaded)
                        {
                            if (!gpuLoadEstablished) AddLog("GPU 负载校验通过：" + sample.GpuLoad.ToString("F0") + "% / " + sample.GpuPower.ToString("F1") + "W。");
                            gpuLoadEstablished = true;
                            gpuLowSince = null;
                        }
                        else if (gpuLoadEstablished && sample.GpuLoad < config.GpuLoadDroppedPct) gpuLowSince = gpuLowSince ?? now;
                        else gpuLowSince = null;

                        if (!gpuLoadEstablished && phaseClock.Elapsed.TotalSeconds >= config.GpuEstablishTimeoutSeconds)
                        { _runStatus = "StressFailed"; throw new InvalidOperationException("GPU 压力未建立：仍只有 " + sample.GpuLoad.ToString("F0") + "% / " + sample.GpuPower.ToString("F1") + "W。检测已中止，不生成液金判断。"); }
                        if (gpuLowSince.HasValue && (now - gpuLowSince.Value).TotalSeconds >= config.GpuDropTimeoutSeconds)
                        { _runStatus = "StressFailed"; throw new InvalidOperationException("GPU 压力持续掉载，检测已中止。请确认性能模式、电源与独显状态。"); }
                    }

                    cpuHotSince = cpuLoad && sample.PackageTemperature >= config.CpuSafetyTemperatureC ? (cpuHotSince ?? now) : (DateTime?)null;
                    gpuHotSince = gpuLoad && sample.GpuTemperature >= config.GpuSafetyTemperatureC ? (gpuHotSince ?? now) : (DateTime?)null;
                    if ((cpuHotSince.HasValue && (now - cpuHotSince.Value).TotalSeconds >= config.SafetyDurationSeconds) ||
                        (gpuHotSince.HasValue && (now - gpuHotSince.Value).TotalSeconds >= config.SafetyDurationSeconds))
                    {
                        _runStatus = "SafetyStopped";
                        _stopReason = "安全中止：CPU ≥" + config.CpuSafetyTemperatureC + "°C 或 GPU ≥" + config.GpuSafetyTemperatureC + "°C 持续达到安全时长。";
                        AddLog(_stopReason);
                        _cancellation.Cancel(); token.ThrowIfCancellationRequested();
                    }
                    nextSampleAt += config.SamplingIntervalSeconds;
                    double delaySeconds = nextSampleAt - phaseClock.Elapsed.TotalSeconds;
                    if (delaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                    else nextSampleAt = phaseClock.Elapsed.TotalSeconds;
                }
            }
            finally
            {
                if (stress != null) stress.Dispose();
                if (gpu != null) gpu.Dispose();
                if (object.ReferenceEquals(_activeGpuStress, gpu)) _activeGpuStress = null;
            }
        }

        private void FinishRun(RunConfiguration config, string runStatus, string runError, string sensorError)
        {
            AnalysisResult result = AnalysisEngine.Analyze(_samples, config, _profile);
            AnalysisEngine.ApplyRunStatus(result, runStatus, runError);
            HistoryStore.ApplyAndSave(_root, _machine, config, result);
            _verdict.Text = result.Verdict; _verdict.Foreground = SeverityBrush(result.Severity); _reason.Text = result.Reason;
            try
            {
                _lastReportFolder = ReportWriter.Write(_root, _machine, config, _samples, result, sensorError);
                _reportButton.IsEnabled = true;
                AddLog("报告已生成：" + _lastReportFolder);
            }
            catch (Exception ex) { AddLog("报告生成失败：" + ex.Message); _reason.Text += " 报告生成失败：" + ex.Message; }
            _phase.Text = runStatus == "Completed" ? "已完成" : "已中止"; _remaining.Text = "—";
        }

        private string ValidatePreflight(RunConfiguration config)
        {
            List<Sample> preflight = _samples.Where(s => s.Phase == "预检").ToList();
            if (preflight.Count < 3) return "预检样本不足。";
            double required = Math.Min(0.80, config.MinimumValidSampleRatio);
            bool needCpu = config.TestMode != "GPU 单烤";
            bool needGpu = config.TestMode != "CPU 单烤";
            bool requireBothCoreGroups = _profile != null && _profile.RequirePCoreAndECore;
            double cpuRatio = preflight.Count(s => s.PackageTemperature > 0 && s.PackagePower > 0 && (requireBothCoreGroups ? s.PCoreCount >= 2 && s.ECoreCount >= 2 : s.PCoreCount >= 2 || s.ECoreCount >= 2)) / (double)preflight.Count;
            double gpuRatio = preflight.Count(s => s.GpuTemperature > 0 && !string.IsNullOrWhiteSpace(s.GpuName)) / (double)preflight.Count;
            if (needCpu && cpuRatio < required) return "CPU 传感器预检失败：有效率 " + (cpuRatio * 100).ToString("F0") + "% ，需要 Package 温度、功耗" + (requireBothCoreGroups ? "以及 P-Core/E-Core 两组核心温度。" : "和同类型核心温度。") ;
            if (needGpu && gpuRatio < required) return "GPU 传感器预检失败：有效率 " + (gpuRatio * 100).ToString("F0") + "% ，需要独显名称和温度；负载与功耗将在压力启动后校验。";
            AddLog("预检通过：CPU 有效率 " + (cpuRatio * 100).ToString("F0") + "% / GPU 有效率 " + (gpuRatio * 100).ToString("F0") + "% 。");
            return string.Empty;
        }

        private void StopRun(object sender, RoutedEventArgs e)
        {
            if (_cancellation != null) { _runStatus = "UserStopped"; _stopReason = "用户请求安全停止。"; AddLog(_stopReason + "正在结束当前阶段。"); _cancellation.Cancel(); }
        }

        private void OpenReport(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_lastReportFolder) && Directory.Exists(_lastReportFolder)) Process.Start(_lastReportFolder);
        }


        private void AddSample(Sample sample)
        {
            _samples.Add(sample); _sampleView.Insert(0, sample); if (_sampleView.Count > 120) _sampleView.RemoveAt(_sampleView.Count - 1);
        }

        private void UpdateReadings(Sample sample)
        {
            _packageTemp.Text = sample.PackageTemperature > 0 ? sample.PackageTemperature.ToString("F1") + " °C" : "—";
            _power.Text = sample.PackagePower > 0 ? sample.PackagePower.ToString("F1") + " W" : "—";
            _coreDelta.Text = CoreDeltaText(sample);
            _gpuTemp.Text = sample.GpuTemperature > 0 ? sample.GpuTemperature.ToString("F1") + " °C" : "—";
            _gpuPower.Text = sample.GpuPower > 0 ? sample.GpuPower.ToString("F1") + " W" : "—";
            _gpuMemoryTemp.Text = sample.GpuMemoryTemperature > 0 ? sample.GpuMemoryTemperature.ToString("F1") + " °C" : "—";
            _gpuLoad.Text = sample.GpuLoadAvailable || sample.GpuLoad > 0 ? sample.GpuLoad.ToString("F0") + " %" : "—";
            string gpuName = string.IsNullOrWhiteSpace(sample.GpuName) ? _machine.Gpu : sample.GpuName;
            string vram = sample.GpuMemoryTotal > 0 ? (sample.GpuMemoryUsed / 1024.0).ToString("F1") + "/" + (sample.GpuMemoryTotal / 1024.0).ToString("F1") + " GB" : "未读取";
            string gpuFan = sample.GpuFanRpm > 0 ? sample.GpuFanRpm.ToString("F0") + " RPM" : "未报告/停转";
            string stressState = _activeGpuStress == null ? "未启用" : _activeGpuStress.Status + "，调度 " + _activeGpuStress.DispatchCount;
            _gpuDetails.Text = "独显：" + gpuName + "  · 核心 " + Value(sample.GpuCoreClock, " MHz") + "  · 显存 " + Value(sample.GpuMemoryClock, " MHz") +
                "  · VRAM " + vram + "  · 热点 " + Value(sample.GpuHotSpotTemperature, " °C") + "  · 风扇 " + gpuFan + "  · PCI " + (string.IsNullOrWhiteSpace(sample.GpuPciBusId) ? "未读取" : sample.GpuPciBusId) +
                "  · 限制原因 " + (string.IsNullOrWhiteSpace(sample.GpuClockEventReasons) ? "未读取" : sample.GpuClockEventReasons) + "  · 压力源：" + stressState;
            string sensorError = _sensor == null ? string.Empty : _sensor.LastError;
            _sensorState.Text = "传感器：" + sample.SensorSource + "  · 本次读取 " + sample.SensorReadDurationMilliseconds.ToString("F0") + " ms  · 核温字段：P " + sample.PCoreCount + " / E " + sample.ECoreCount + "（共 " + sample.CoreTemperatures.Count + "） · CPU/系统风扇：" +
                (sample.FanRpm > 0 ? sample.FanRpm.ToString("F0") : "—") + "/" + (sample.SystemFanRpm > 0 ? sample.SystemFanRpm.ToString("F0") : "—") + " RPM" +
                (string.IsNullOrWhiteSpace(sensorError) ? string.Empty : "  · 错误：" + sensorError);
        }

        private void SetRunning(bool running)
        {
            _fullButton.IsEnabled = !running; _quickButton.IsEnabled = !running; _stopButton.IsEnabled = running; _roomTemperature.IsEnabled = !running; _mode.IsEnabled = !running; _testMode.IsEnabled = !running;
        }

        private void AddLog(string text) { _logs.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + text); }
        private static string FormatSeconds(int seconds) { return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
        private static string FormatMinutes(int seconds) { return Math.Ceiling(seconds / 60.0).ToString("F0") + " 分钟"; }
        private static string Value(double value, string unit) { return value > 0 ? value.ToString("F0") + unit : "—"; }
        private static string CoreDeltaText(Sample sample)
        {
            string p = sample.PCoreCount >= 2 ? "P " + sample.PCoreDelta.ToString("F1") : "P —";
            string e = sample.ECoreCount >= 2 ? "E " + sample.ECoreDelta.ToString("F1") : "E —";
            return p + " / " + e + " °C";
        }
        private static SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static SolidColorBrush SeverityBrush(string severity) { return severity == "Red" ? Brush("#B42318") : severity == "Orange" ? Brush("#B54708") : severity == "Green" ? Brush("#027A48") : Brush("#475467"); }
        private static StackPanel Panel(string title) { StackPanel p = new StackPanel(); p.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#143A5E"), Margin = new Thickness(0, 0, 0, 12) }); return p; }
        private static Border Wrap(UIElement element) { return new Border { Background = Brushes.White, BorderBrush = Brush("#D8E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 8), Child = element }; }
        private static void AddPair(Panel panel, string key, string value) { Grid g = new Grid { Margin = new Thickness(0, 0, 0, 4) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.Children.Add(new TextBlock { Text = key, Foreground = Brush("#667085") }); TextBlock val = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold }; Grid.SetColumn(val, 1); g.Children.Add(val); panel.Children.Add(g); }
        private static StackPanel Metric(string label, string value, out TextBlock valueBlock) { StackPanel p = new StackPanel { Margin = new Thickness(0, 0, 0, 2) }; p.Children.Add(new TextBlock { Text = label, Foreground = Brush("#667085"), TextWrapping = TextWrapping.Wrap }); valueBlock = new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("#172B4D"), TextWrapping = TextWrapping.Wrap }; p.Children.Add(valueBlock); return p; }
        private static Button Button(string text, string color, RoutedEventHandler handler)
        {
            SolidColorBrush enabled = Brush(color);
            Button b = new Button { Content = text, Background = enabled, Foreground = Brushes.White, BorderBrush = enabled, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
            b.IsEnabledChanged += delegate
            {
                if (b.IsEnabled) { b.Background = enabled; b.BorderBrush = enabled; b.Foreground = Brushes.White; }
                else { b.Background = Brush("#EAECF0"); b.BorderBrush = Brush("#98A2B3"); b.Foreground = Brush("#475467"); }
            };
            b.Click += handler;
            return b;
        }
        private static void AddBullet(Panel panel, string text) { panel.Children.Add(new TextBlock { Text = "•  " + text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 6) }); }
        private static DataGridTextColumn Column(string header, string property, double width) { return new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(property) { StringFormat = property == "Time" ? "HH:mm:ss" : "F1" }, Width = width }; }
    }

    internal sealed class CpuStress : IDisposable
    {
        private readonly List<Task> _workers = new List<Task>();
        private volatile bool _disposed;
        private CancellationTokenSource _stop;
        public void Start(int workerCount, CancellationToken token)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            for (int i = 0; i < workerCount; i++)
            {
                _workers.Add(Task.Factory.StartNew(() =>
                {
                    double value = 0.1234;
                    while (!_stop.IsCancellationRequested && !_disposed)
                    {
                        for (int n = 0; n < 50000; n++) value = Math.Sqrt(value * value + 1.000001) + Math.Sin(value);
                    }
                    GC.KeepAlive(value);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
        }
        public void Dispose()
        {
            _disposed = true;
            try { if (_stop != null) _stop.Cancel(); } catch { }
            try { Task.WaitAll(_workers.ToArray(), 5000); } catch { }
            if (_stop != null) _stop.Dispose();
        }
    }

}
