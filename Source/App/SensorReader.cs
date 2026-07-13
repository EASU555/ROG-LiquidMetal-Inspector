using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;

namespace RogLiquidMetalInspector
{
    public sealed class SensorReader : ISensorProvider
    {
        private object _computer;
        private bool _lhmReady;
        private string _source;
        private string _lastError;
        private ResolveEventHandler _assemblyResolver;

        public string Source { get { return _source; } }
        public string LastError { get { return _lastError; } }
        public bool IsReady { get { return _lhmReady; } }

        public SensorReader(string executableDirectory)
        {
            _source = "WMI 回退";
            _lastError = string.Empty;
            TryStartLibreHardwareMonitor(executableDirectory);
        }

        public Sample Read(string phase)
        {
            Sample sample = new Sample();
            sample.Time = DateTime.Now;
            sample.Phase = phase;
            sample.SensorSource = _source;
            if (_lhmReady)
            {
                try
                {
                    ReadLhm(sample);
                    return sample;
                }
                catch (Exception ex)
                {
                    _lastError = "LibreHardwareMonitor 读取失败：" + ex.Message;
                    _lhmReady = false;
                    _source = "WMI 回退";
                    sample.SensorSource = _source;
                }
            }
            ReadWmiFallback(sample);
            return sample;
        }

        private void TryStartLibreHardwareMonitor(string executableDirectory)
        {
            string dll = Path.Combine(executableDirectory, "lib", "LibreHardwareMonitorLib.dll");
            if (!File.Exists(dll))
            {
                _lastError = "未找到 LibreHardwareMonitorLib.dll：" + dll;
                return;
            }
            try
            {
                string libraryDirectory = Path.GetDirectoryName(dll);
                _assemblyResolver = delegate(object sender, ResolveEventArgs args)
                {
                    try
                    {
                        string dependency = Path.Combine(libraryDirectory, new AssemblyName(args.Name).Name + ".dll");
                        return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
                    }
                    catch { return null; }
                };
                AppDomain.CurrentDomain.AssemblyResolve += _assemblyResolver;
                Assembly assembly = Assembly.LoadFrom(dll);
                Type computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer", true);
                _computer = Activator.CreateInstance(computerType);
                SetPropertyIfPresent(_computer, "IsCpuEnabled", true);
                SetPropertyIfPresent(_computer, "IsGpuEnabled", true);
                SetPropertyIfPresent(_computer, "IsMotherboardEnabled", true);
                SetPropertyIfPresent(_computer, "IsControllerEnabled", true);
                MethodInfo open = _computer.GetType().GetMethod("Open", Type.EmptyTypes);
                open.Invoke(_computer, null);
                _lhmReady = true;
                _source = "LibreHardwareMonitor";
            }
            catch (Exception ex)
            {
                _lastError = "LibreHardwareMonitor 初始化失败：" + ex.GetType().Name + " - " + ex.Message;
                _lhmReady = false;
            }
        }

        private void ReadLhm(Sample sample)
        {
            List<object> hardware = GetEnumerableProperty(_computer, "Hardware").Cast<object>().ToList();
            object selectedGpu = hardware.Where(IsGpuHardware).OrderByDescending(GpuHardwareScore).FirstOrDefault();
            double cpuClockSum = 0;
            int cpuClockCount = 0;
            foreach (object item in hardware)
            {
                UpdateHardware(item);
                string hardwareType = GetStringProperty(item, "HardwareType");
                if (!IsGpuHardware(item) || object.ReferenceEquals(item, selectedGpu))
                    ReadHardwareSensors(item, sample, hardwareType, GetStringProperty(item, "Name"), ref cpuClockSum, ref cpuClockCount);
                foreach (object sub in GetEnumerableProperty(item, "SubHardware"))
                {
                    UpdateHardware(sub);
                    string subType = GetStringProperty(sub, "HardwareType");
                    if (subType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) < 0 || object.ReferenceEquals(item, selectedGpu))
                        ReadHardwareSensors(sub, sample, subType, GetStringProperty(sub, "Name"), ref cpuClockSum, ref cpuClockCount);
                }
            }
            sample.CoreTemperatures = sample.CoreTemperatures.OrderBy(c => c.Name).ToList();
            sample.AverageClock = cpuClockCount > 0 ? cpuClockSum / cpuClockCount : 0;
            if (sample.CoreTemperatures.Count > 0)
            {
                CoreTemperature hot = sample.CoreTemperatures.OrderByDescending(c => c.Temperature).First();
                sample.HottestCore = hot.Name;
                SetCoreGroupMetrics(sample, sample.CoreTemperatures.Where(c => c.Name.StartsWith("P-Core", StringComparison.OrdinalIgnoreCase)).ToList(), true);
                SetCoreGroupMetrics(sample, sample.CoreTemperatures.Where(c => c.Name.StartsWith("E-Core", StringComparison.OrdinalIgnoreCase)).ToList(), false);
                if (sample.PCoreCount >= 2 || sample.ECoreCount >= 2) sample.CoreDelta = Math.Max(sample.PCoreDelta, sample.ECoreDelta);
                else
                {
                    double cool = sample.CoreTemperatures.Min(c => c.Temperature);
                    sample.CoreDelta = hot.Temperature - cool;
                }
            }
        }

        private static void SetCoreGroupMetrics(Sample sample, List<CoreTemperature> cores, bool performanceCores)
        {
            if (performanceCores) sample.PCoreCount = cores.Count; else sample.ECoreCount = cores.Count;
            if (cores.Count == 0) return;
            CoreTemperature hot = cores.OrderByDescending(c => c.Temperature).First();
            double delta = cores.Count >= 2 ? hot.Temperature - cores.Min(c => c.Temperature) : 0;
            if (performanceCores) { sample.PCoreDelta = delta; sample.HottestPCore = hot.Name; }
            else { sample.ECoreDelta = delta; sample.HottestECore = hot.Name; }
        }

        private static bool IsGpuHardware(object hardware)
        {
            return GetStringProperty(hardware, "HardwareType").IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GpuHardwareScore(object hardware)
        {
            string type = GetStringProperty(hardware, "HardwareType");
            string name = GetStringProperty(hardware, "Name");
            int score = type.IndexOf("Nvidia", StringComparison.OrdinalIgnoreCase) >= 0 ? 3000 :
                type.IndexOf("Amd", StringComparison.OrdinalIgnoreCase) >= 0 ? 2000 : 100;
            if (name.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0) score += 1000;
            if (name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) score += 500;
            return score;
        }

        private void ReadHardwareSensors(object hardware, Sample sample, string hardwareType, string hardwareName, ref double cpuClockSum, ref int cpuClockCount)
        {
            bool cpu = string.Equals(hardwareType, "Cpu", StringComparison.OrdinalIgnoreCase);
            bool gpu = hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0;
            if (gpu && string.IsNullOrWhiteSpace(sample.GpuName)) sample.GpuName = hardwareName;
            foreach (object sensor in GetEnumerableProperty(hardware, "Sensors"))
            {
                string type = GetStringProperty(sensor, "SensorType");
                string name = GetStringProperty(sensor, "Name");
                double value = GetNullableDoubleProperty(sensor, "Value");
                if (value <= 0) continue;
                if (cpu && type == "Temperature")
                {
                    if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sample.PackageTemperature = Math.Max(sample.PackageTemperature, value);
                    }
                    else if (IsIndividualCore(name))
                    {
                        sample.CoreTemperatures.Add(new CoreTemperature { Name = name, Temperature = value });
                    }
                }
                if (cpu && type == "Power" &&
                    (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    sample.PackagePower = Math.Max(sample.PackagePower, value);
                }
                if (cpu && type == "Clock" && IsIndividualCore(name))
                {
                    cpuClockSum += value;
                    cpuClockCount++;
                }
                if (cpu && type == "Load" && name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sample.CpuLoad = Math.Max(sample.CpuLoad, value);
                }
                if (gpu && type == "Temperature")
                {
                    if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0)
                        sample.GpuMemoryTemperature = Math.Max(sample.GpuMemoryTemperature, value);
                    else if (name.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0)
                        sample.GpuHotSpotTemperature = Math.Max(sample.GpuHotSpotTemperature, value);
                    else if (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0)
                        sample.GpuTemperature = Math.Max(sample.GpuTemperature, value);
                }
                if (gpu && type == "Power") sample.GpuPower = Math.Max(sample.GpuPower, value);
                if (gpu && type == "Load" && (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("D3D 3D", StringComparison.OrdinalIgnoreCase) >= 0)) sample.GpuLoad = Math.Max(sample.GpuLoad, value);
                if (gpu && type == "Clock")
                {
                    if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0) sample.GpuMemoryClock = Math.Max(sample.GpuMemoryClock, value);
                    else if (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Graphics", StringComparison.OrdinalIgnoreCase) >= 0) sample.GpuCoreClock = Math.Max(sample.GpuCoreClock, value);
                }
                if (gpu && type == "SmallData")
                {
                    if (name.IndexOf("GPU Memory Used", StringComparison.OrdinalIgnoreCase) >= 0) sample.GpuMemoryUsed = Math.Max(sample.GpuMemoryUsed, value);
                    else if (name.IndexOf("GPU Memory Total", StringComparison.OrdinalIgnoreCase) >= 0) sample.GpuMemoryTotal = Math.Max(sample.GpuMemoryTotal, value);
                }
                if (type == "Fan")
                {
                    sample.FanRpm = Math.Max(sample.FanRpm, value);
                    if (gpu) sample.GpuFanRpm = Math.Max(sample.GpuFanRpm, value);
                }
            }
        }

        private static bool IsIndividualCore(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.ToLowerInvariant();
            return (n.Contains("core") || n.Contains("ccd")) &&
                !n.Contains("average") && !n.Contains("max") && !n.Contains("distance") && !n.Contains("package");
        }

        private void ReadWmiFallback(Sample sample)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        object raw = item["CurrentTemperature"];
                        if (raw == null) continue;
                        double celsius = Convert.ToDouble(raw) / 10.0 - 273.15;
                        if (celsius > 0 && celsius < 130) sample.PackageTemperature = Math.Max(sample.PackageTemperature, celsius);
                    }
                }
            }
            catch (Exception ex)
            {
                _lastError = "WMI 回退读取失败：" + ex.Message;
            }
        }

        private static void UpdateHardware(object hardware)
        {
            MethodInfo update = hardware.GetType().GetMethod("Update", Type.EmptyTypes);
            if (update != null) update.Invoke(hardware, null);
        }

        private static IEnumerable GetEnumerableProperty(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(property);
            object value = info == null ? null : info.GetValue(target, null);
            return value as IEnumerable ?? new object[0];
        }

        private static string GetStringProperty(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(property);
            object value = info == null ? null : info.GetValue(target, null);
            return value == null ? string.Empty : value.ToString();
        }

        private static double GetNullableDoubleProperty(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(property);
            object value = info == null ? null : info.GetValue(target, null);
            if (value == null) return 0;
            try { return Convert.ToDouble(value); }
            catch { return 0; }
        }

        private static void SetPropertyIfPresent(object target, string property, bool value)
        {
            PropertyInfo info = target.GetType().GetProperty(property);
            if (info != null && info.CanWrite) info.SetValue(target, value, null);
        }

        public void Dispose()
        {
            try
            {
                if (_computer != null)
                {
                    MethodInfo close = _computer.GetType().GetMethod("Close", Type.EmptyTypes);
                    if (close != null) close.Invoke(_computer, null);
                }
            }
            catch { }
            if (_assemblyResolver != null) AppDomain.CurrentDomain.AssemblyResolve -= _assemblyResolver;
        }

        public static MachineInfo GetMachineInfo()
        {
            MachineInfo info = new MachineInfo();
            info.Model = GetWmiText("Win32_ComputerSystem", "Model");
            info.Cpu = GetWmiText("Win32_Processor", "Name");
            info.Bios = GetWmiText("Win32_BIOS", "SMBIOSBIOSVersion");
            info.Windows = GetWindowsVersion();
            info.Gpu = GetPreferredGpuName();
            info.IsAdministrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            return info;
        }

        private static string GetWindowsVersion()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption,Version,BuildNumber FROM Win32_OperatingSystem"))
                    foreach (ManagementObject item in searcher.Get())
                        return (item["Caption"] + " " + item["Version"] + " (Build " + item["BuildNumber"] + ")").Trim();
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string GetPreferredGpuName()
        {
            try
            {
                List<string> names = new List<string>();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementObject item in searcher.Get()) if (item["Name"] != null) names.Add(item["Name"].ToString().Trim());
                string preferred = names.FirstOrDefault(n => n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0);
                return preferred ?? names.FirstOrDefault() ?? "未知";
            }
            catch { return "未知"; }
        }

        private static string GetWmiText(string className, string property)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + className))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        if (item[property] != null) return item[property].ToString().Trim();
                    }
                }
            }
            catch { }
            return "未知";
        }
    }
}
