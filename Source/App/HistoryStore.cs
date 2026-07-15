using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RogLiquidMetalInspector
{
    public static class HistoryStore
    {
        private const int MaximumEntries = 100;

        public static void Prepare(string root, MachineInfo machine, RunConfiguration config)
        {
            List<HistoryEntry> comparable = Comparable(Load(root), machine, config).ToList();
            List<HistoryEntry> healthy = comparable.Where(e => e.Completed && e.DataQualityPassed && e.ConfidenceScore >= 65 && e.RawSeverity == "Green").ToList();
            if (healthy.Count < 2) return;
            config.BaselineCpuThermalEfficiency = Median(healthy.Where(e => e.CpuThermalEfficiency > 0).Select(e => e.CpuThermalEfficiency).ToList());
            config.BaselineGpuThermalEfficiency = Median(healthy.Where(e => e.GpuThermalEfficiency > 0).Select(e => e.GpuThermalEfficiency).ToList());
            config.BaselineAvailable = config.BaselineCpuThermalEfficiency > 0 || config.BaselineGpuThermalEfficiency > 0;
        }

        public static void ApplyAndSave(string root, MachineInfo machine, RunConfiguration config, AnalysisResult result)
        {
            List<HistoryEntry> all = Load(root);
            List<HistoryEntry> prior = Comparable(all, machine, config).Where(e => e.Completed && e.DataQualityPassed && e.ConfidenceScore >= config.MinimumDecisionConfidence)
                .OrderByDescending(e => e.TimestampUtc).Take(2).ToList();
            result.ComparableHistoryRuns = prior.Count;
            string rawSeverity = result.Severity;
            string rawVerdict = result.Verdict;

            if (!config.QuickScreen && result.IsComplete && result.DataQualityPassed && result.ConfidenceScore >= config.MinimumDecisionConfidence)
            {
                if (rawSeverity == "Red")
                {
                    int reproduced = 1 + prior.Count(e => e.RawSeverity == "Red");
                    result.ReproducedRuns = reproduced;
                    result.ReproductionStatus = "最近 " + (prior.Count + 1) + " 次同条件测试中异常复现 " + reproduced + " 次";
                    if (reproduced < 2)
                    {
                        result.PreliminaryVerdict = rawVerdict;
                        result.Verdict = "异常尚未复现：需要同条件再测";
                        result.Severity = "Orange";
                        result.CanJudge = false;
                        result.Reason += " 本次异常只有 1 次，尚未达到三次中至少两次复现的输出门槛。";
                    }
                }
                else if (rawSeverity == "Green")
                {
                    int reproduced = 1 + prior.Count(e => e.RawSeverity == "Green");
                    result.ReproducedRuns = reproduced;
                    result.ReproductionStatus = "最近 " + (prior.Count + 1) + " 次同条件测试中正常复现 " + reproduced + " 次";
                    if (reproduced < 2)
                    {
                        result.PreliminaryVerdict = rawVerdict;
                        result.Verdict = "本次未见异常：等待重复确认";
                        result.Severity = "Blue";
                        result.CanJudge = false;
                        result.Reason += " 首次正常结果只建立候选基线，再完成一次同条件测试后才确认。";
                    }
                }
            }

            if (config.QuickScreen || !result.IsComplete) return;
            all.Add(new HistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Model = machine == null ? string.Empty : machine.Model,
                Cpu = machine == null ? string.Empty : machine.Cpu,
                Gpu = machine == null ? string.Empty : machine.Gpu,
                TestMode = config.TestMode,
                PerformanceMode = config.PerformanceMode,
                RoomTemperature = config.RoomTemperature,
                DiagnosticRuleVersion = config.DiagnosticRuleVersion,
                ProfileHash = config.ProfileHash,
                Completed = result.IsComplete,
                DataQualityPassed = result.DataQualityPassed,
                ConfidenceScore = result.ConfidenceScore,
                RawSeverity = rawSeverity,
                RawVerdict = rawVerdict,
                CpuThermalEfficiency = result.ThermalEfficiency,
                GpuThermalEfficiency = result.GpuThermalEfficiency,
                CpuTemperature = result.SteadyPackageTemperature,
                CpuPower = result.SteadyPackagePower,
                GpuTemperature = result.SteadyGpuTemperature,
                GpuPower = result.SteadyGpuPower
            });
            Save(root, all.OrderByDescending(e => e.TimestampUtc).Take(MaximumEntries).OrderBy(e => e.TimestampUtc).ToList());
        }

        private static IEnumerable<HistoryEntry> Comparable(IEnumerable<HistoryEntry> entries, MachineInfo machine, RunConfiguration config)
        {
            string model = machine == null ? string.Empty : machine.Model;
            string cpu = machine == null ? string.Empty : machine.Cpu;
            string gpu = machine == null ? string.Empty : machine.Gpu;
            return entries.Where(e => Equal(e.Model, model) && Equal(e.Cpu, cpu) && Equal(e.Gpu, gpu) &&
                Equal(e.TestMode, config.TestMode) && Equal(e.PerformanceMode, config.PerformanceMode) &&
                Equal(e.DiagnosticRuleVersion, config.DiagnosticRuleVersion) && Math.Abs(e.RoomTemperature - config.RoomTemperature) <= 2 &&
                (string.IsNullOrWhiteSpace(config.ProfileHash) || Equal(e.ProfileHash, config.ProfileHash)));
        }

        private static List<HistoryEntry> Load(string root)
        {
            string path = Path.Combine(root, "History", "history.json");
            if (!File.Exists(path)) return new List<HistoryEntry>();
            try { return new JavaScriptSerializer().Deserialize<List<HistoryEntry>>(File.ReadAllText(path)) ?? new List<HistoryEntry>(); }
            catch { return new List<HistoryEntry>(); }
        }

        private static void Save(string root, List<HistoryEntry> entries)
        {
            try
            {
                string folder = Path.Combine(root, "History");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "history.json");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(entries), new UTF8Encoding(true));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch { }
        }

        private static bool Equal(string left, string right) { return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase); }
        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2.0 : values[middle];
        }
    }

    public sealed class HistoryEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string Model { get; set; }
        public string Cpu { get; set; }
        public string Gpu { get; set; }
        public string TestMode { get; set; }
        public string PerformanceMode { get; set; }
        public double RoomTemperature { get; set; }
        public string DiagnosticRuleVersion { get; set; }
        public string ProfileHash { get; set; }
        public bool Completed { get; set; }
        public bool DataQualityPassed { get; set; }
        public double ConfidenceScore { get; set; }
        public string RawSeverity { get; set; }
        public string RawVerdict { get; set; }
        public double CpuThermalEfficiency { get; set; }
        public double GpuThermalEfficiency { get; set; }
        public double CpuTemperature { get; set; }
        public double CpuPower { get; set; }
        public double GpuTemperature { get; set; }
        public double GpuPower { get; set; }
    }
}
