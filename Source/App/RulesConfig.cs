using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
        public string SourceHash { get; set; }
        public bool IsModified { get; set; }
        public string ValidationWarning { get; set; }

        public static RulesConfig Default()
        {
            return new RulesConfig
            {
                Version = "2.1.0",
                DiagnosticRuleVersion = "multi-evidence-2.1",
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
                SensorReadTimeoutSeconds = 5,
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
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            if (!File.Exists(path))
            {
                fallback.SourceHash = HashText(serializer.Serialize(fallback));
                fallback.ValidationWarning = "外部规则文件不存在，已使用内置默认规则。";
                return fallback;
            }
            try
            {
                string text = File.ReadAllText(path);
                RulesConfig loaded = serializer.Deserialize<RulesConfig>(text);
                if (loaded == null) return fallback;
                string beforeValidation = Fingerprint(loaded);
                ValidateAndFill(loaded, fallback);
                string afterValidation = Fingerprint(loaded);
                loaded.SourceHash = HashBytes(File.ReadAllBytes(path));
                loaded.IsModified = !string.Equals(afterValidation, Fingerprint(fallback), StringComparison.Ordinal);
                if (beforeValidation != afterValidation)
                    loaded.ValidationWarning = "规则文件含越界或缺失字段，相关字段已恢复为安全默认值。";
                else if (loaded.IsModified)
                    loaded.ValidationWarning = "当前使用的规则参数与程序默认值不同；报告已记录规则哈希。";
                return loaded;
            }
            catch (Exception ex)
            {
                fallback.SourceHash = HashText(serializer.Serialize(fallback));
                fallback.ValidationWarning = "规则文件解析失败，已使用内置默认规则：" + ex.Message;
                return fallback;
            }
        }

        private static void ValidateAndFill(RulesConfig value, RulesConfig fallback)
        {
            if (string.IsNullOrWhiteSpace(value.Version)) value.Version = fallback.Version;
            if (value.SamplingIntervalSeconds < 0.5 || value.SamplingIntervalSeconds > 2) value.SamplingIntervalSeconds = fallback.SamplingIntervalSeconds;
            if (value.IdleSeconds < 10 || value.IdleSeconds > 1800) value.IdleSeconds = fallback.IdleSeconds;
            if (value.HotspotProbeSeconds < 10 || value.HotspotProbeSeconds > 600) value.HotspotProbeSeconds = fallback.HotspotProbeSeconds;
            if (value.PrimarySeconds < 60 || value.PrimarySeconds > 1800) value.PrimarySeconds = fallback.PrimarySeconds;
            if (value.QuickIdleSeconds < 10 || value.QuickIdleSeconds > 300) value.QuickIdleSeconds = fallback.QuickIdleSeconds;
            if (value.QuickHotspotProbeSeconds < 10 || value.QuickHotspotProbeSeconds > 300) value.QuickHotspotProbeSeconds = fallback.QuickHotspotProbeSeconds;
            if (value.QuickPrimarySeconds < 30 || value.QuickPrimarySeconds > 600) value.QuickPrimarySeconds = fallback.QuickPrimarySeconds;
            if (value.AnalysisWindowSeconds < 30 || value.AnalysisWindowSeconds > value.PrimarySeconds) value.AnalysisWindowSeconds = fallback.AnalysisWindowSeconds;
            if (value.WarningCoreDeltaC < 5 || value.WarningCoreDeltaC > 30) value.WarningCoreDeltaC = fallback.WarningCoreDeltaC;
            if (value.ConfirmCoreDeltaC <= value.WarningCoreDeltaC || value.ConfirmCoreDeltaC > 40) value.ConfirmCoreDeltaC = fallback.ConfirmCoreDeltaC;
            if (value.SustainedSeconds < 10 || value.SustainedSeconds > 600) value.SustainedSeconds = fallback.SustainedSeconds;
            if (value.CpuSafetyTemperatureC < 90 || value.CpuSafetyTemperatureC > 110) value.CpuSafetyTemperatureC = fallback.CpuSafetyTemperatureC;
            if (value.GpuSafetyTemperatureC < 75 || value.GpuSafetyTemperatureC > 100) value.GpuSafetyTemperatureC = fallback.GpuSafetyTemperatureC;
            if (value.SafetyDurationSeconds < 1 || value.SafetyDurationSeconds > 10) value.SafetyDurationSeconds = fallback.SafetyDurationSeconds;
            if (value.MinimumValidSampleRatio < 0.8 || value.MinimumValidSampleRatio > 1) value.MinimumValidSampleRatio = fallback.MinimumValidSampleRatio;
            if (value.GpuLoadEstablishedPct < 50 || value.GpuLoadEstablishedPct > 100) value.GpuLoadEstablishedPct = fallback.GpuLoadEstablishedPct;
            if (value.GpuPowerEstablishedW < 10 || value.GpuPowerEstablishedW > 200) value.GpuPowerEstablishedW = fallback.GpuPowerEstablishedW;
            if (value.GpuLoadDroppedPct < 0 || value.GpuLoadDroppedPct > 80) value.GpuLoadDroppedPct = fallback.GpuLoadDroppedPct;
            if (value.GpuEstablishTimeoutSeconds < 5 || value.GpuEstablishTimeoutSeconds > 60) value.GpuEstablishTimeoutSeconds = fallback.GpuEstablishTimeoutSeconds;
            if (value.GpuDropTimeoutSeconds < 5 || value.GpuDropTimeoutSeconds > 60) value.GpuDropTimeoutSeconds = fallback.GpuDropTimeoutSeconds;
            if (value.SensorReadTimeoutSeconds < 2 || value.SensorReadTimeoutSeconds > 15) value.SensorReadTimeoutSeconds = fallback.SensorReadTimeoutSeconds;
            if (string.IsNullOrWhiteSpace(value.DiagnosticRuleVersion)) value.DiagnosticRuleVersion = fallback.DiagnosticRuleVersion;
            if (value.MinimumFullAnalysisSeconds < 60 || value.MinimumFullAnalysisSeconds > value.PrimarySeconds) value.MinimumFullAnalysisSeconds = fallback.MinimumFullAnalysisSeconds;
            if (value.MinimumCpuLoadPct < 50 || value.MinimumCpuLoadPct > 100) value.MinimumCpuLoadPct = fallback.MinimumCpuLoadPct;
            if (value.MaximumLoadCoefficientOfVariation < 0.05 || value.MaximumLoadCoefficientOfVariation > 0.8) value.MaximumLoadCoefficientOfVariation = fallback.MaximumLoadCoefficientOfVariation;
            if (value.MaximumPowerCoefficientOfVariation < 0.05 || value.MaximumPowerCoefficientOfVariation > 0.8) value.MaximumPowerCoefficientOfVariation = fallback.MaximumPowerCoefficientOfVariation;
            if (value.DominantHotspotShareWarning <= 0 || value.DominantHotspotShareWarning > 1) value.DominantHotspotShareWarning = fallback.DominantHotspotShareWarning;
            if (value.PowerRetentionWarningRatio <= 0 || value.PowerRetentionWarningRatio > 1) value.PowerRetentionWarningRatio = fallback.PowerRetentionWarningRatio;
            if (value.PowerRetentionCriticalRatio <= 0 || value.PowerRetentionCriticalRatio >= value.PowerRetentionWarningRatio) value.PowerRetentionCriticalRatio = fallback.PowerRetentionCriticalRatio;
            if (value.ClockRetentionWarningRatio <= 0 || value.ClockRetentionWarningRatio > 1) value.ClockRetentionWarningRatio = fallback.ClockRetentionWarningRatio;
            if (value.ClockRetentionCriticalRatio <= 0 || value.ClockRetentionCriticalRatio >= value.ClockRetentionWarningRatio) value.ClockRetentionCriticalRatio = fallback.ClockRetentionCriticalRatio;
            if (value.CpuNearLimitTemperatureC < 85 || value.CpuNearLimitTemperatureC > 110) value.CpuNearLimitTemperatureC = fallback.CpuNearLimitTemperatureC;
            if (value.GpuHotspotDeltaWarningC < 5 || value.GpuHotspotDeltaWarningC > 40) value.GpuHotspotDeltaWarningC = fallback.GpuHotspotDeltaWarningC;
            if (value.GpuHotspotDeltaCriticalC <= value.GpuHotspotDeltaWarningC || value.GpuHotspotDeltaCriticalC > 60) value.GpuHotspotDeltaCriticalC = fallback.GpuHotspotDeltaCriticalC;
            if (value.EvidenceWatchScore < 1 || value.EvidenceWatchScore > 100) value.EvidenceWatchScore = fallback.EvidenceWatchScore;
            if (value.EvidenceSuspectScore <= value.EvidenceWatchScore || value.EvidenceSuspectScore > 100) value.EvidenceSuspectScore = fallback.EvidenceSuspectScore;
            if (value.EvidenceStrongScore <= value.EvidenceSuspectScore || value.EvidenceStrongScore > 100) value.EvidenceStrongScore = fallback.EvidenceStrongScore;
            if (value.MinimumDecisionConfidence <= 0 || value.MinimumDecisionConfidence > 100) value.MinimumDecisionConfidence = fallback.MinimumDecisionConfidence;
            if (value.TemperatureSlopeUnstableCPerMinute < 0.2 || value.TemperatureSlopeUnstableCPerMinute > 10) value.TemperatureSlopeUnstableCPerMinute = fallback.TemperatureSlopeUnstableCPerMinute;
            if (value.IdleCpuWarningTemperatureC < 40 || value.IdleCpuWarningTemperatureC > 100) value.IdleCpuWarningTemperatureC = fallback.IdleCpuWarningTemperatureC;
            if (value.MaximumIdleCpuLoadPct < 5 || value.MaximumIdleCpuLoadPct > 50) value.MaximumIdleCpuLoadPct = fallback.MaximumIdleCpuLoadPct;
        }

        private static string HashText(string value) { return HashBytes(Encoding.UTF8.GetBytes(value ?? string.Empty)); }
        private static string Fingerprint(RulesConfig value)
        {
            List<string> parts = new List<string>();
            foreach (PropertyInfo property in typeof(RulesConfig).GetProperties().OrderBy(p => p.Name))
            {
                if (property.Name == "SourceHash" || property.Name == "IsModified" || property.Name == "ValidationWarning") continue;
                object raw = property.GetValue(value, null);
                string text = raw == null ? "<null>" : Convert.ToString(raw, CultureInfo.InvariantCulture);
                parts.Add(property.Name + "=" + text);
            }
            return string.Join("|", parts.ToArray());
        }
        private static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
