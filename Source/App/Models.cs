using System;
using System.Collections.Generic;

namespace RogLiquidMetalInspector
{
    public sealed class Sample
    {
        public DateTime Time { get; set; }
        public double ElapsedSeconds { get; set; }
        public double SensorReadDurationMilliseconds { get; set; }
        public string Phase { get; set; }
        public double PackageTemperature { get; set; }
        public double PackagePower { get; set; }
        public double AverageClock { get; set; }
        public double FanRpm { get; set; }
        public double SystemFanRpm { get; set; }
        public double CpuLoad { get; set; }
        public bool CpuLoadAvailable { get; set; }
        public double GpuTemperature { get; set; }
        public double GpuPower { get; set; }
        public double GpuHotSpotTemperature { get; set; }
        public double GpuMemoryTemperature { get; set; }
        public double GpuLoad { get; set; }
        public bool GpuLoadAvailable { get; set; }
        public double GpuCoreClock { get; set; }
        public double GpuMemoryClock { get; set; }
        public double GpuMemoryUsed { get; set; }
        public double GpuMemoryTotal { get; set; }
        public double GpuFanRpm { get; set; }
        public string GpuName { get; set; }
        public string GpuPciBusId { get; set; }
        public string GpuClockEventReasons { get; set; }
        public string GpuTelemetrySource { get; set; }
        public bool GpuThermalLimited { get; set; }
        public bool GpuPowerLimited { get; set; }
        public bool GpuPowerBrakeLimited { get; set; }
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
            GpuPciBusId = string.Empty;
            GpuClockEventReasons = string.Empty;
            GpuTelemetrySource = string.Empty;
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
        public string GpuDriver { get; set; }
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
        public bool ProfileModeMatched { get; set; }
        public bool ConditionsConfirmed { get; set; }
        public string ProfileId { get; set; }
        public string ProfileName { get; set; }
        public string ProfileReference { get; set; }
        public string ProfileEvidence { get; set; }
        public string ProfileHash { get; set; }
        public string ProfileSourcePath { get; set; }
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
        public double CpuLoadValidSampleRatio { get; set; }
        public double GpuLoadValidSampleRatio { get; set; }
        public double SamplingCoverageRatio { get; set; }
        public double MaximumSampleGapSeconds { get; set; }
        public double AverageSensorReadDurationMilliseconds { get; set; }
        public double MaximumSensorReadDurationMilliseconds { get; set; }
        public bool DataQualityPassed { get; set; }
        public string DecisionBlockReason { get; set; }
        public double AnalysisDurationSeconds { get; set; }
        public string RunStatus { get; set; }
        public bool IsComplete { get; set; }
        public string RunError { get; set; }
        public string PreliminaryVerdict { get; set; }
        public string DiagnosticRuleVersion { get; set; }
        public double SuspicionScore { get; set; }
        public double CpuSuspicionScore { get; set; }
        public double GpuSuspicionScore { get; set; }
        public double ConfidenceScore { get; set; }
        public string ConfidenceLevel { get; set; }
        public int IndependentEvidenceCount { get; set; }
        public double DominantHotspotShare { get; set; }
        public double CpuAverageLoad { get; set; }
        public double CpuLoadCoefficientOfVariation { get; set; }
        public double CpuPowerCoefficientOfVariation { get; set; }
        public double CpuPowerRetention { get; set; }
        public double CpuClockRetention { get; set; }
        public double CpuTemperatureSlopeCPerMinute { get; set; }
        public double CpuPowerSlopeWPerMinute { get; set; }
        public double IdleCpuTemperature { get; set; }
        public double IdleCpuLoad { get; set; }
        public double GpuLoadCoefficientOfVariation { get; set; }
        public double GpuPowerCoefficientOfVariation { get; set; }
        public double GpuPowerRetention { get; set; }
        public double GpuClockRetention { get; set; }
        public double GpuTemperatureSlopeCPerMinute { get; set; }
        public double GpuHotspotDeltaP95 { get; set; }
        public int SustainedGpuHotspotDeltaSeconds { get; set; }
        public double IdleGpuTemperature { get; set; }
        public double IdleGpuLoad { get; set; }
        public double TotalSteadyPower { get; set; }
        public double TotalPowerRetention { get; set; }
        public double GpuThermalEfficiency { get; set; }
        public double SteadyCpuFanRpm { get; set; }
        public double SteadySystemFanRpm { get; set; }
        public double SteadyGpuFanRpm { get; set; }
        public double GpuThermalLimitSampleRatio { get; set; }
        public double GpuPowerLimitSampleRatio { get; set; }
        public string GpuClockEventReasons { get; set; }
        public string StressGpuDeviceName { get; set; }
        public string SampledGpuPciBusId { get; set; }
        public bool GpuDeviceIdentityMatched { get; set; }
        public bool HistoryBaselineAvailable { get; set; }
        public int ComparableHistoryRuns { get; set; }
        public int ReproducedRuns { get; set; }
        public string ReproductionStatus { get; set; }
        public double BaselineCpuThermalEfficiency { get; set; }
        public double BaselineGpuThermalEfficiency { get; set; }
        public int NearCpuLimitSeconds { get; set; }
        public int NearGpuLimitSeconds { get; set; }
        public List<DiagnosticEvidence> Evidence { get; set; }

        public AnalysisResult()
        {
            Evidence = new List<DiagnosticEvidence>();
            ConfidenceLevel = string.Empty;
            DiagnosticRuleVersion = string.Empty;
            DecisionBlockReason = string.Empty;
            GpuClockEventReasons = string.Empty;
            ReproductionStatus = string.Empty;
        }
    }

    public sealed class DiagnosticEvidence
    {
        public string Code { get; set; }
        public string Component { get; set; }
        public string Category { get; set; }
        public string Level { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Observed { get; set; }
        public string Threshold { get; set; }
        public string SourceTier { get; set; }
        public double Score { get; set; }
        public bool Triggered { get; set; }
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
        public int SensorReadTimeoutSeconds { get; set; }
        public string DiagnosticRuleVersion { get; set; }
        public int MinimumFullAnalysisSeconds { get; set; }
        public double MinimumCpuLoadPct { get; set; }
        public double MaximumLoadCoefficientOfVariation { get; set; }
        public double MaximumPowerCoefficientOfVariation { get; set; }
        public double DominantHotspotShareWarning { get; set; }
        public double PowerRetentionWarningRatio { get; set; }
        public double PowerRetentionCriticalRatio { get; set; }
        public double ClockRetentionWarningRatio { get; set; }
        public double ClockRetentionCriticalRatio { get; set; }
        public double CpuNearLimitTemperatureC { get; set; }
        public double GpuHotspotDeltaWarningC { get; set; }
        public double GpuHotspotDeltaCriticalC { get; set; }
        public double EvidenceWatchScore { get; set; }
        public double EvidenceSuspectScore { get; set; }
        public double EvidenceStrongScore { get; set; }
        public double MinimumDecisionConfidence { get; set; }
        public double TemperatureSlopeUnstableCPerMinute { get; set; }
        public double IdleCpuWarningTemperatureC { get; set; }
        public double MaximumIdleCpuLoadPct { get; set; }
        public string RulesHash { get; set; }
        public bool RulesModified { get; set; }
        public string RulesValidationWarning { get; set; }
        public bool ConditionsConfirmed { get; set; }
        public string StressGpuDeviceName { get; set; }
        public string ProfileHash { get; set; }
        public bool BaselineAvailable { get; set; }
        public double BaselineCpuThermalEfficiency { get; set; }
        public double BaselineGpuThermalEfficiency { get; set; }

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
            config.SensorReadTimeoutSeconds = rules.SensorReadTimeoutSeconds;
            config.DiagnosticRuleVersion = rules.DiagnosticRuleVersion;
            config.MinimumFullAnalysisSeconds = rules.MinimumFullAnalysisSeconds;
            config.MinimumCpuLoadPct = rules.MinimumCpuLoadPct;
            config.MaximumLoadCoefficientOfVariation = rules.MaximumLoadCoefficientOfVariation;
            config.MaximumPowerCoefficientOfVariation = rules.MaximumPowerCoefficientOfVariation;
            config.DominantHotspotShareWarning = rules.DominantHotspotShareWarning;
            config.PowerRetentionWarningRatio = rules.PowerRetentionWarningRatio;
            config.PowerRetentionCriticalRatio = rules.PowerRetentionCriticalRatio;
            config.ClockRetentionWarningRatio = rules.ClockRetentionWarningRatio;
            config.ClockRetentionCriticalRatio = rules.ClockRetentionCriticalRatio;
            config.CpuNearLimitTemperatureC = rules.CpuNearLimitTemperatureC;
            config.GpuHotspotDeltaWarningC = rules.GpuHotspotDeltaWarningC;
            config.GpuHotspotDeltaCriticalC = rules.GpuHotspotDeltaCriticalC;
            config.EvidenceWatchScore = rules.EvidenceWatchScore;
            config.EvidenceSuspectScore = rules.EvidenceSuspectScore;
            config.EvidenceStrongScore = rules.EvidenceStrongScore;
            config.MinimumDecisionConfidence = rules.MinimumDecisionConfidence;
            config.TemperatureSlopeUnstableCPerMinute = rules.TemperatureSlopeUnstableCPerMinute;
            config.IdleCpuWarningTemperatureC = rules.IdleCpuWarningTemperatureC;
            config.MaximumIdleCpuLoadPct = rules.MaximumIdleCpuLoadPct;
            config.RulesHash = rules.SourceHash;
            config.RulesModified = rules.IsModified;
            config.RulesValidationWarning = rules.ValidationWarning;
            return config;
        }
    }
}
