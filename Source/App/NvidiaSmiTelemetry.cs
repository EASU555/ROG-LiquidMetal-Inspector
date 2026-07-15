using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace RogLiquidMetalInspector
{
    internal sealed class NvidiaSmiTelemetry : IDisposable
    {
        private readonly string _executable;
        private readonly object _sync = new object();
        private DateTime _lastReadUtc;
        private List<GpuTelemetrySnapshot> _cache = new List<GpuTelemetrySnapshot>();
        private bool _refreshing;
        private bool _disposed;

        public NvidiaSmiTelemetry()
        {
            string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
            string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            _executable = File.Exists(system) ? system : File.Exists(programFiles) ? programFiles : string.Empty;
        }

        public void Apply(Sample sample)
        {
            if (sample == null || string.IsNullOrWhiteSpace(_executable)) return;
            GpuTelemetrySnapshot selected;
            bool startRefresh = false;
            lock (_sync)
            {
                if (_disposed) return;
                selected = Select(_cache, sample.GpuName);
                if (!_refreshing && ((DateTime.UtcNow - _lastReadUtc).TotalSeconds >= 5 || _cache.Count == 0))
                {
                    _refreshing = true;
                    _lastReadUtc = DateTime.UtcNow;
                    startRefresh = true;
                }
            }
            if (startRefresh) Task.Factory.StartNew(RefreshCache, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            if (selected == null) return;
            if (string.IsNullOrWhiteSpace(sample.GpuName)) sample.GpuName = selected.Name;
            sample.GpuPciBusId = selected.PciBusId;
            sample.GpuClockEventReasons = selected.ActiveReasons;
            sample.GpuTelemetrySource = "NVIDIA SMI";
            sample.GpuThermalLimited = selected.ThermalLimited;
            sample.GpuPowerLimited = selected.PowerLimited;
            sample.GpuPowerBrakeLimited = selected.PowerBrakeLimited;
        }

        private void RefreshCache()
        {
            List<GpuTelemetrySnapshot> fresh = ReadAll();
            lock (_sync)
            {
                if (!_disposed && fresh.Count > 0) _cache = fresh;
                _refreshing = false;
            }
        }

        private List<GpuTelemetrySnapshot> ReadAll()
        {
            List<GpuTelemetrySnapshot> output = new List<GpuTelemetrySnapshot>();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(_executable, "-q -x")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(info))
                {
                    Task<string> stdout = Task.Factory.StartNew(() => process.StandardOutput.ReadToEnd());
                    Task<string> stderr = Task.Factory.StartNew(() => process.StandardError.ReadToEnd());
                    if (!process.WaitForExit(2500))
                    {
                        try { process.Kill(); } catch { }
                        return output;
                    }
                    if (!stdout.Wait(1000)) return output;
                    string xml = stdout.Result;
                    if (string.IsNullOrWhiteSpace(xml)) return output;
                    XmlDocument document = new XmlDocument();
                    document.XmlResolver = null;
                    document.LoadXml(xml);
                    foreach (XmlNode gpu in document.SelectNodes("/nvidia_smi_log/gpu"))
                    {
                        List<string> active = new List<string>();
                        XmlNode reasons = gpu.SelectSingleNode("clocks_event_reasons");
                        if (reasons != null)
                            foreach (XmlNode reason in reasons.ChildNodes)
                                if (IsActive(reason.InnerText)) active.Add(ReasonText(reason.Name));
                        output.Add(new GpuTelemetrySnapshot
                        {
                            Name = Text(gpu, "product_name"),
                            PciBusId = Text(gpu, "pci/pci_bus_id"),
                            ActiveReasons = active.Count == 0 ? "无活动限制原因" : string.Join("、", active.ToArray()),
                            ThermalLimited = Active(gpu, "clocks_event_reasons/clocks_event_reason_hw_thermal_slowdown") || Active(gpu, "clocks_event_reasons/clocks_event_reason_sw_thermal_slowdown"),
                            PowerLimited = Active(gpu, "clocks_event_reasons/clocks_event_reason_sw_power_cap"),
                            PowerBrakeLimited = Active(gpu, "clocks_event_reasons/clocks_event_reason_hw_power_brake_slowdown")
                        });
                    }
                }
            }
            catch { }
            return output;
        }

        private static GpuTelemetrySnapshot Select(List<GpuTelemetrySnapshot> values, string sampledName)
        {
            if (values == null || values.Count == 0) return null;
            string wanted = Normalize(sampledName);
            if (wanted.Length == 0) return values[0];
            return values.FirstOrDefault(v => NamesMatch(wanted, Normalize(v.Name))) ?? values[0];
        }

        internal static bool NamesMatch(string left, string right)
        {
            string a = Normalize(left), b = Normalize(right);
            if (a.Length == 0 || b.Length == 0) return false;
            return a == b || a.Contains(b) || b.Contains(a);
        }

        private static string Normalize(string value)
        {
            string text = (value ?? string.Empty).ToLowerInvariant();
            text = text.Replace("nvidia", string.Empty).Replace("corporation", string.Empty).Replace("geforce", string.Empty).Replace("(tm)", string.Empty);
            return Regex.Replace(text, "[^a-z0-9]", string.Empty);
        }

        private static string Text(XmlNode node, string xpath)
        {
            XmlNode value = node == null ? null : node.SelectSingleNode(xpath);
            return value == null ? string.Empty : value.InnerText.Trim();
        }

        private static bool Active(XmlNode node, string xpath) { return IsActive(Text(node, xpath)); }
        private static bool IsActive(string value) { return string.Equals((value ?? string.Empty).Trim(), "Active", StringComparison.OrdinalIgnoreCase); }
        private static string ReasonText(string tag)
        {
            string value = (tag ?? string.Empty).Replace("clocks_event_reason_", string.Empty).Replace('_', ' ');
            return value;
        }

        public void Dispose()
        {
            lock (_sync) { _disposed = true; }
        }

        private sealed class GpuTelemetrySnapshot
        {
            public string Name { get; set; }
            public string PciBusId { get; set; }
            public string ActiveReasons { get; set; }
            public bool ThermalLimited { get; set; }
            public bool PowerLimited { get; set; }
            public bool PowerBrakeLimited { get; set; }
        }
    }
}
