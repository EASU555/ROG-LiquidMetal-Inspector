using System;
using System.Collections.Generic;

namespace RogLiquidMetalInspector
{
    public sealed class Sample
    {
        public DateTime Time { get; set; }
        public string Phase { get; set; }
        public double PackageTemperature { get; set; }
        public double PackagePower { get; set; }
        public double AverageClock { get; set; }
        public double FanRpm { get; set; }
        public double CpuLoad { get; set; }
        public double GpuTemperature { get; set; }
        public double GpuPower { get; set; }
        public double GpuHotSpotTemperature { get; set; }
        public double GpuMemoryTemperature { get; set; }
        public double GpuLoad { get; set; }
        public double GpuCoreClock { get; set; }
        public double GpuMemoryClock { get; set; }
        public double GpuMemoryUsed { get; set; }
        public double GpuMemoryTotal { get; set; }
        public double GpuFanRpm { get; set; }
        public string GpuName { get; set; }
        public double CoreDelta { get; set; }
        public double PCoreDelta { get; set; }
        public double ECoreDelta { get; set; }
        public int PCoreCount { get; set; }
        public int ECoreCount { get; set; }
        public string HottestPCore { get; set; }
        public string HottestECore { get; set; }
        public string HottestCore { get; set; }
        public string SensorSource { get; set; }
        public List<CoreTemperature> CoreTemperatures { get; set; }

        public Sample()
        {
            CoreTemperatures = new List<CoreTemperature>();
            HottestCore = string.Empty;
            HottestPCore = string.Empty;
            HottestECore = string.Empty;
            SensorSource = string.Empty;
            GpuName = string.Empty;
        }
    }

    public sealed class CoreTemperature
    {
        public string Name { get; set; }
        public double Temperature { get; set; }
    }

    public sealed class MachineInfo
    {
        public string Model { get; set; }
        public string Cpu { get; set; }
        public string Bios { get; set; }
        public string Windows { get; set; }
        public string Gpu { get; set; }
        public bool IsAdministrator { get; set; }
    }

    public sealed class AnalysisResult
    {
        public string Verdict { get; set; }
        public string Severity { get; set; }
        public string Reason { get; set; }
        public bool CanJudge { get; set; }
        public double CoreDeltaP95 { get; set; }
        public double PCoreDeltaP95 { get; set; }
        public double ECoreDeltaP95 { get; set; }
        public double SteadyPackageTemperature { get; set; }
        public double SteadyPackagePower { get; set; }
        public double SteadyClock { get; set; }
        public double TemperatureRise { get; set; }
        public double ThermalEfficiency { get; set; }
        public int SustainedHotspotSeconds { get; set; }
        public int SustainedPCoreHotspotSeconds { get; set; }
        public int SustainedECoreHotspotSeconds { get; set; }
        public string DominantHotspot { get; set; }
        public int SampleCount { get; set; }
        public bool IsQuickScreen { get; set; }
        public bool ProfileMatched { get; set; }
        public string ProfileId { get; set; }
        public string ProfileName { get; set; }
        public string ProfileReference { get; set; }
        public string ProfileEvidence { get; set; }
        public int SustainedPowerCollapseSeconds { get; set; }
        public bool IsPowerCollapseAtHighTemperature { get; set; }
        public double SteadyGpuTemperature { get; set; }
        public double SteadyGpuPower { get; set; }
        public double GpuHotSpotTemperature { get; set; }
        public double GpuMemoryTemperature { get; set; }
        public double SteadyGpuLoad { get; set; }
        public double SteadyGpuCoreClock { get; set; }
        public string GpuDeviceName { get; set; }
        public int SustainedGpuHotLowPowerSeconds { get; set; }
        public double CpuValidSampleRatio { get; set; }
        public double PCoreValidSampleRatio { get; set; }
        public double ECoreValidSampleRatio { get; set; }
        public double GpuValidSampleRatio { get; set; }
        public double AnalysisDurationSeconds { get; set; }
        public string RunStatus { get; set; }
        public bool IsComplete { get; set; }
        public string RunError { get; set; }
        public string PreliminaryVerdict { get; set; }
    }

    public sealed class RunConfiguration
    {
        public bool QuickScreen { get; set; }
        public double RoomTemperature { get; set; }
        public string PerformanceMode { get; set; }
        public int IdleSeconds { get; set; }
        public int ProbeSeconds { get; set; }
        public int PrimarySeconds { get; set; }
        public string TestMode { get; set; }
        public double SamplingIntervalSeconds { get; set; }
        public int AnalysisWindowSeconds { get; set; }
        public double WarningCoreDeltaC { get; set; }
        public double ConfirmCoreDeltaC { get; set; }
        public int SustainedSeconds { get; set; }
        public double CpuSafetyTemperatureC { get; set; }
        public double GpuSafetyTemperatureC { get; set; }
        public int SafetyDurationSeconds { get; set; }
        public double MinimumValidSampleRatio { get; set; }
        public double GpuLoadEstablishedPct { get; set; }
        public double GpuPowerEstablishedW { get; set; }
        public double GpuLoadDroppedPct { get; set; }
        public int GpuEstablishTimeoutSeconds { get; set; }
        public int GpuDropTimeoutSeconds { get; set; }

        public static RunConfiguration Create(bool quick, double roomTemperature, string performanceMode, string testMode, RulesConfig rules)
        {
            rules = rules ?? RulesConfig.Default();
            RunConfiguration config = new RunConfiguration();
            config.QuickScreen = quick;
            config.RoomTemperature = roomTemperature;
            config.PerformanceMode = performanceMode;
            config.TestMode = testMode;
            config.IdleSeconds = quick ? rules.QuickIdleSeconds : rules.IdleSeconds;
            config.ProbeSeconds = quick ? rules.QuickHotspotProbeSeconds : rules.HotspotProbeSeconds;
            config.PrimarySeconds = quick ? rules.QuickPrimarySeconds : rules.PrimarySeconds;
            config.SamplingIntervalSeconds = rules.SamplingIntervalSeconds;
            config.AnalysisWindowSeconds = rules.AnalysisWindowSeconds;
            config.WarningCoreDeltaC = rules.WarningCoreDeltaC;
            config.ConfirmCoreDeltaC = rules.ConfirmCoreDeltaC;
            config.SustainedSeconds = rules.SustainedSeconds;
            config.CpuSafetyTemperatureC = rules.CpuSafetyTemperatureC;
            config.GpuSafetyTemperatureC = rules.GpuSafetyTemperatureC;
            config.SafetyDurationSeconds = rules.SafetyDurationSeconds;
            config.MinimumValidSampleRatio = rules.MinimumValidSampleRatio;
            config.GpuLoadEstablishedPct = rules.GpuLoadEstablishedPct;
            config.GpuPowerEstablishedW = rules.GpuPowerEstablishedW;
            config.GpuLoadDroppedPct = rules.GpuLoadDroppedPct;
            config.GpuEstablishTimeoutSeconds = rules.GpuEstablishTimeoutSeconds;
            config.GpuDropTimeoutSeconds = rules.GpuDropTimeoutSeconds;
            return config;
        }
    }
}
