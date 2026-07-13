using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace RogLiquidMetalInspector
{
    public sealed class MachineProfile
    {
        public string ProfileId { get; set; }
        public string ProfileName { get; set; }
        public string Version { get; set; }
        public string ModelContains { get; set; }
        public string CpuContains { get; set; }
        public string RequiredConditions { get; set; }
        public string WorkloadNotice { get; set; }
        public bool RequirePCoreAndECore { get; set; }
        public List<ModeReference> Modes { get; set; }
        public List<GpuModeReference> GpuModes { get; set; }
        public List<ProfileSource> Sources { get; set; }

        public ModeReference FindMode(string name)
        {
            return Modes == null ? null : Modes.FirstOrDefault(m => string.Equals(m.ModeName, name, StringComparison.OrdinalIgnoreCase));
        }

        public GpuModeReference FindGpuMode(string name)
        {
            return GpuModes == null ? null : GpuModes.FirstOrDefault(m => string.Equals(m.ModeName, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool Matches(MachineInfo machine)
        {
            return machine != null && !string.IsNullOrWhiteSpace(machine.Model) && !string.IsNullOrWhiteSpace(machine.Cpu) &&
                machine.Model.IndexOf(ModelContains ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0 &&
                machine.Cpu.IndexOf(CpuContains ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class GpuModeReference
    {
        public string ModeName { get; set; }
        public double MinimumLoadPct { get; set; }
        public double SingleExpectedPowerMin { get; set; }
        public double SingleExpectedPowerMax { get; set; }
        public double DualExpectedPowerMin { get; set; }
        public double DualExpectedPowerMax { get; set; }
        public double HotTemperatureC { get; set; }
        public double LowPowerAtHotMax { get; set; }
        public int DurationSeconds { get; set; }
        public string NormalText { get; set; }
        public string SuspectText { get; set; }
    }

    public sealed class ModeReference
    {
        public string ModeName { get; set; }
        public string TestName { get; set; }
        public int SteadyStartSecond { get; set; }
        public int SteadyEndSecond { get; set; }
        public double ExpectedPowerMin { get; set; }
        public double ExpectedPowerMax { get; set; }
        public double ExpectedTemperatureMin { get; set; }
        public double ExpectedTemperatureMax { get; set; }
        public double ExpectedR23MultiMin { get; set; }
        public double HotTemperatureC { get; set; }
        public double LowPowerAtHotMax { get; set; }
        public int DurationSeconds { get; set; }
        public string NormalText { get; set; }
        public string SuspectText { get; set; }
    }

    public sealed class ProfileSource
    {
        public string Tier { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }

    public static class ProfileLoader
    {
        public static MachineProfile LoadForMachine(string root, MachineInfo machine)
        {
            string directory = Path.Combine(root, "Profiles");
            if (!Directory.Exists(directory)) return null;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    MachineProfile profile = serializer.Deserialize<MachineProfile>(File.ReadAllText(file));
                    if (profile != null && profile.Matches(machine)) return profile;
                }
                catch { }
            }
            return null;
        }

        public static string Summary(MachineProfile profile, string mode, string testMode)
        {
            if (profile == null) return "未匹配机型档案：使用通用核心温差规则。";
            if (!string.Equals(testMode, "CPU 单烤", StringComparison.OrdinalIgnoreCase))
            {
                GpuModeReference gpu = profile.FindGpuMode(mode);
                if (gpu == null) return "已匹配 " + profile.ProfileName + "；当前模式没有 GPU 对照档案。";
                double min = string.Equals(testMode, "GPU 单烤", StringComparison.OrdinalIgnoreCase) ? gpu.SingleExpectedPowerMin : gpu.DualExpectedPowerMin;
                double max = string.Equals(testMode, "GPU 单烤", StringComparison.OrdinalIgnoreCase) ? gpu.SingleExpectedPowerMax : gpu.DualExpectedPowerMax;
                return "已匹配 G815LR GPU 档案：" + mode + "，有效负载 ≥" + gpu.MinimumLoadPct + "% ，参考功耗 " + min + "–" + max + "W。";
            }
            ModeReference reference = profile.FindMode(mode);
            if (reference == null) return "已匹配 " + profile.ProfileName + "；当前模式没有可比较的社区档案。";
            return "已匹配 G815LR 档案：" + reference.ModeName + "，" + reference.SteadyStartSecond + "–" + reference.SteadyEndSecond +
                " 秒参考 " + reference.ExpectedPowerMin + "–" + reference.ExpectedPowerMax + "W / " + reference.ExpectedTemperatureMin + "–" + reference.ExpectedTemperatureMax + "°C。";
        }
    }
}
