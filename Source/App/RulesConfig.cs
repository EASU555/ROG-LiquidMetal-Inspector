using System;
using System.IO;
using System.Web.Script.Serialization;

namespace RogLiquidMetalInspector
{
    public sealed class RulesConfig
    {
        public string Version { get; set; }
        public double SamplingIntervalSeconds { get; set; }
        public int IdleSeconds { get; set; }
        public int HotspotProbeSeconds { get; set; }
        public int PrimarySeconds { get; set; }
        public int QuickIdleSeconds { get; set; }
        public int QuickHotspotProbeSeconds { get; set; }
        public int QuickPrimarySeconds { get; set; }
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

        public static RulesConfig Default()
        {
            return new RulesConfig
            {
                Version = "2.0.0",
                DiagnosticRuleVersion = "multi-evidence-2.0",
                SamplingIntervalSeconds = 1,
                IdleSeconds = 120,
                HotspotProbeSeconds = 60,
                PrimarySeconds = 600,
                QuickIdleSeconds = 30,
                QuickHotspotProbeSeconds = 30,
                QuickPrimarySeconds = 120,
                AnalysisWindowSeconds = 300,
                WarningCoreDeltaC = 15,
                ConfirmCoreDeltaC = 20,
                SustainedSeconds = 60,
                CpuSafetyTemperatureC = 100,
                GpuSafetyTemperatureC = 90,
                SafetyDurationSeconds = 3,
                MinimumValidSampleRatio = 0.90,
                GpuLoadEstablishedPct = 85,
                GpuPowerEstablishedW = 50,
                GpuLoadDroppedPct = 40,
                GpuEstablishTimeoutSeconds = 20,
                GpuDropTimeoutSeconds = 10,
                MinimumFullAnalysisSeconds = 240,
                MinimumCpuLoadPct = 85,
                MaximumLoadCoefficientOfVariation = 0.15,
                MaximumPowerCoefficientOfVariation = 0.25,
                DominantHotspotShareWarning = 0.70,
                PowerRetentionWarningRatio = 0.80,
                PowerRetentionCriticalRatio = 0.65,
                ClockRetentionWarningRatio = 0.85,
                ClockRetentionCriticalRatio = 0.70,
                CpuNearLimitTemperatureC = 100,
                GpuHotspotDeltaWarningC = 20,
                GpuHotspotDeltaCriticalC = 30,
                EvidenceWatchScore = 25,
                EvidenceSuspectScore = 50,
                EvidenceStrongScore = 70,
                MinimumDecisionConfidence = 65,
                TemperatureSlopeUnstableCPerMinute = 2.0,
                IdleCpuWarningTemperatureC = 80,
                MaximumIdleCpuLoadPct = 20
            };
        }

        public static RulesConfig Load(string root)
        {
            RulesConfig fallback = Default();
            string path = Path.Combine(root, "规则默认配置.json");
            if (!File.Exists(path)) return fallback;
            try
            {
                RulesConfig loaded = new JavaScriptSerializer().Deserialize<RulesConfig>(File.ReadAllText(path));
                if (loaded == null) return fallback;
                ValidateAndFill(loaded, fallback);
                return loaded;
            }
            catch { return fallback; }
        }

        private static void ValidateAndFill(RulesConfig value, RulesConfig fallback)
        {
            if (value.SamplingIntervalSeconds < 0.25 || value.SamplingIntervalSeconds > 5) value.SamplingIntervalSeconds = fallback.SamplingIntervalSeconds;
            if (value.IdleSeconds <= 0) value.IdleSeconds = fallback.IdleSeconds;
            if (value.HotspotProbeSeconds <= 0) value.HotspotProbeSeconds = fallback.HotspotProbeSeconds;
            if (value.PrimarySeconds <= 0) value.PrimarySeconds = fallback.PrimarySeconds;
            if (value.QuickIdleSeconds <= 0) value.QuickIdleSeconds = fallback.QuickIdleSeconds;
            if (value.QuickHotspotProbeSeconds <= 0) value.QuickHotspotProbeSeconds = fallback.QuickHotspotProbeSeconds;
            if (value.QuickPrimarySeconds <= 0) value.QuickPrimarySeconds = fallback.QuickPrimarySeconds;
            if (value.AnalysisWindowSeconds <= 0) value.AnalysisWindowSeconds = fallback.AnalysisWindowSeconds;
            if (value.WarningCoreDeltaC <= 0) value.WarningCoreDeltaC = fallback.WarningCoreDeltaC;
            if (value.ConfirmCoreDeltaC <= value.WarningCoreDeltaC) value.ConfirmCoreDeltaC = fallback.ConfirmCoreDeltaC;
            if (value.SustainedSeconds <= 0) value.SustainedSeconds = fallback.SustainedSeconds;
            if (value.CpuSafetyTemperatureC <= 0) value.CpuSafetyTemperatureC = fallback.CpuSafetyTemperatureC;
            if (value.GpuSafetyTemperatureC <= 0) value.GpuSafetyTemperatureC = fallback.GpuSafetyTemperatureC;
            if (value.SafetyDurationSeconds <= 0) value.SafetyDurationSeconds = fallback.SafetyDurationSeconds;
            if (value.MinimumValidSampleRatio < 0.5 || value.MinimumValidSampleRatio > 1) value.MinimumValidSampleRatio = fallback.MinimumValidSampleRatio;
            if (value.GpuLoadEstablishedPct <= 0) value.GpuLoadEstablishedPct = fallback.GpuLoadEstablishedPct;
            if (value.GpuPowerEstablishedW <= 0) value.GpuPowerEstablishedW = fallback.GpuPowerEstablishedW;
            if (value.GpuLoadDroppedPct <= 0) value.GpuLoadDroppedPct = fallback.GpuLoadDroppedPct;
            if (value.GpuEstablishTimeoutSeconds <= 0) value.GpuEstablishTimeoutSeconds = fallback.GpuEstablishTimeoutSeconds;
            if (value.GpuDropTimeoutSeconds <= 0) value.GpuDropTimeoutSeconds = fallback.GpuDropTimeoutSeconds;
            if (string.IsNullOrWhiteSpace(value.DiagnosticRuleVersion)) value.DiagnosticRuleVersion = fallback.DiagnosticRuleVersion;
            if (value.MinimumFullAnalysisSeconds <= 0) value.MinimumFullAnalysisSeconds = fallback.MinimumFullAnalysisSeconds;
            if (value.MinimumCpuLoadPct <= 0 || value.MinimumCpuLoadPct > 100) value.MinimumCpuLoadPct = fallback.MinimumCpuLoadPct;
            if (value.MaximumLoadCoefficientOfVariation <= 0 || value.MaximumLoadCoefficientOfVariation > 1) value.MaximumLoadCoefficientOfVariation = fallback.MaximumLoadCoefficientOfVariation;
            if (value.MaximumPowerCoefficientOfVariation <= 0 || value.MaximumPowerCoefficientOfVariation > 1) value.MaximumPowerCoefficientOfVariation = fallback.MaximumPowerCoefficientOfVariation;
            if (value.DominantHotspotShareWarning <= 0 || value.DominantHotspotShareWarning > 1) value.DominantHotspotShareWarning = fallback.DominantHotspotShareWarning;
            if (value.PowerRetentionWarningRatio <= 0 || value.PowerRetentionWarningRatio > 1) value.PowerRetentionWarningRatio = fallback.PowerRetentionWarningRatio;
            if (value.PowerRetentionCriticalRatio <= 0 || value.PowerRetentionCriticalRatio >= value.PowerRetentionWarningRatio) value.PowerRetentionCriticalRatio = fallback.PowerRetentionCriticalRatio;
            if (value.ClockRetentionWarningRatio <= 0 || value.ClockRetentionWarningRatio > 1) value.ClockRetentionWarningRatio = fallback.ClockRetentionWarningRatio;
            if (value.ClockRetentionCriticalRatio <= 0 || value.ClockRetentionCriticalRatio >= value.ClockRetentionWarningRatio) value.ClockRetentionCriticalRatio = fallback.ClockRetentionCriticalRatio;
            if (value.CpuNearLimitTemperatureC <= 0) value.CpuNearLimitTemperatureC = fallback.CpuNearLimitTemperatureC;
            if (value.GpuHotspotDeltaWarningC <= 0) value.GpuHotspotDeltaWarningC = fallback.GpuHotspotDeltaWarningC;
            if (value.GpuHotspotDeltaCriticalC <= value.GpuHotspotDeltaWarningC) value.GpuHotspotDeltaCriticalC = fallback.GpuHotspotDeltaCriticalC;
            if (value.EvidenceWatchScore <= 0) value.EvidenceWatchScore = fallback.EvidenceWatchScore;
            if (value.EvidenceSuspectScore <= value.EvidenceWatchScore) value.EvidenceSuspectScore = fallback.EvidenceSuspectScore;
            if (value.EvidenceStrongScore <= value.EvidenceSuspectScore) value.EvidenceStrongScore = fallback.EvidenceStrongScore;
            if (value.MinimumDecisionConfidence <= 0 || value.MinimumDecisionConfidence > 100) value.MinimumDecisionConfidence = fallback.MinimumDecisionConfidence;
            if (value.TemperatureSlopeUnstableCPerMinute <= 0) value.TemperatureSlopeUnstableCPerMinute = fallback.TemperatureSlopeUnstableCPerMinute;
            if (value.IdleCpuWarningTemperatureC <= 0) value.IdleCpuWarningTemperatureC = fallback.IdleCpuWarningTemperatureC;
            if (value.MaximumIdleCpuLoadPct <= 0 || value.MaximumIdleCpuLoadPct > 100) value.MaximumIdleCpuLoadPct = fallback.MaximumIdleCpuLoadPct;
        }
    }
}
