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

        public static RulesConfig Default()
        {
            return new RulesConfig
            {
                Version = "1.1.0",
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
                GpuDropTimeoutSeconds = 10
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
        }
    }
}
