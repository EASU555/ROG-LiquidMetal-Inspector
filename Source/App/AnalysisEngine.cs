using System;
using System.Collections.Generic;
using System.Linq;

namespace RogLiquidMetalInspector
{
    public static class AnalysisEngine
    {
        public static AnalysisResult Analyze(List<Sample> samples, RunConfiguration config, MachineProfile profile)
        {
            AnalysisResult result = new AnalysisResult { IsQuickScreen = config.QuickScreen };
            ApplyProfile(result, profile, config);
            List<Sample> primary = samples.Where(s => s.Phase == "CPU 主测" || s.Phase == "GPU 主测" || s.Phase == "双烤主测").OrderBy(s => s.Time).ToList();
            if (primary.Count == 0)
            {
                result.Verdict = "无法判断"; result.Severity = "Gray"; result.Reason = "未获得主测数据。"; result.CanJudge = false;
                return result;
            }

            List<Sample> steady = SelectSteadyWindow(primary, config, profile);
            result.SampleCount = steady.Count;
            result.AnalysisDurationSeconds = DurationSeconds(steady, config.SamplingIntervalSeconds);
            return config.TestMode == "CPU 单烤" ? AnalyzeCpu(steady, config, profile, result) : AnalyzeGpu(steady, config, profile, result);
        }

        public static void ApplyRunStatus(AnalysisResult result, string runStatus, string runError)
        {
            string status = string.IsNullOrWhiteSpace(runStatus) ? "Completed" : runStatus;
            result.RunStatus = status;
            result.RunError = runError ?? string.Empty;
            result.IsComplete = status == "Completed";
            result.PreliminaryVerdict = result.Verdict;
            if (result.IsComplete) return;

            result.CanJudge = false;
            result.Severity = status == "SafetyStopped" ? "Red" : "Orange";
            result.Verdict = status == "UserStopped" ? "用户中止：结果无效" :
                status == "SafetyStopped" ? "安全中止：需要复核" :
                status == "SensorFailed" ? "传感器失败：无法判断" :
                status == "StressFailed" ? "压力负载失败：无法判断" : "测试异常中止：无法判断";
            result.Reason = (string.IsNullOrWhiteSpace(runError) ? "测试没有完整完成。" : runError) + " 当前数据只作为故障排查证据，不得作为绿色正常结论。";
        }

        private static AnalysisResult AnalyzeCpu(List<Sample> steady, RunConfiguration config, MachineProfile profile, AnalysisResult result)
        {
            bool requireBothCoreGroups = profile != null && profile.RequirePCoreAndECore;
            List<Sample> valid = steady.Where(s => IsCpuValid(s, requireBothCoreGroups)).ToList();
            result.CpuValidSampleRatio = steady.Count == 0 ? 0 : valid.Count / (double)steady.Count;
            result.PCoreValidSampleRatio = steady.Count == 0 ? 0 : steady.Count(s => s.PCoreCount >= 2) / (double)steady.Count;
            result.ECoreValidSampleRatio = steady.Count == 0 ? 0 : steady.Count(s => s.ECoreCount >= 2) / (double)steady.Count;
            if (result.CpuValidSampleRatio < config.MinimumValidSampleRatio)
            {
                result.Verdict = "无法判断"; result.Severity = "Gray"; result.CanJudge = false;
                result.Reason = "CPU 有效样本率仅 " + Percent(result.CpuValidSampleRatio) + "，低于要求的 " + Percent(config.MinimumValidSampleRatio) + "。需要 Package 温度、功耗和同类型核心温度。";
                return result;
            }

            result.CanJudge = !config.QuickScreen;
            result.PCoreDeltaP95 = Percentile(valid.Where(s => s.PCoreCount >= 2).Select(s => s.PCoreDelta).ToList(), 0.95);
            result.ECoreDeltaP95 = Percentile(valid.Where(s => s.ECoreCount >= 2).Select(s => s.ECoreDelta).ToList(), 0.95);
            result.CoreDeltaP95 = Math.Max(result.PCoreDeltaP95, result.ECoreDeltaP95);
            result.SteadyPackageTemperature = AveragePositive(valid.Select(s => s.PackageTemperature));
            result.SteadyPackagePower = AveragePositive(valid.Select(s => s.PackagePower));
            result.SteadyClock = AveragePositive(valid.Select(s => s.AverageClock));
            result.TemperatureRise = result.SteadyPackageTemperature - config.RoomTemperature;
            result.ThermalEfficiency = result.SteadyPackagePower > 0.1 ? result.TemperatureRise / result.SteadyPackagePower : 0;
            result.DominantHotspot = DominantHotspot(valid);
            result.SustainedPCoreHotspotSeconds = LongestDurationSeconds(valid, s => s.PCoreCount >= 2 && s.PCoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedECoreHotspotSeconds = LongestDurationSeconds(valid, s => s.ECoreCount >= 2 && s.ECoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedHotspotSeconds = Math.Max(result.SustainedPCoreHotspotSeconds, result.SustainedECoreHotspotSeconds);

            ModeReference reference = profile == null ? null : profile.FindMode(config.PerformanceMode);
            if (reference != null)
            {
                result.SustainedPowerCollapseSeconds = LongestDurationSeconds(valid,
                    s => s.PackageTemperature >= reference.HotTemperatureC && s.PackagePower > 0.1 && s.PackagePower <= reference.LowPowerAtHotMax,
                    config.SamplingIntervalSeconds);
                result.IsPowerCollapseAtHighTemperature = result.SustainedPowerCollapseSeconds >= reference.DurationSeconds;
            }

            if (config.QuickScreen)
            {
                result.Verdict = result.CoreDeltaP95 >= config.ConfirmCoreDeltaC ? "快速筛查：建议完整复测" : "快速筛查：未见明显异常";
                result.Severity = result.CoreDeltaP95 >= config.ConfirmCoreDeltaC ? "Orange" : "Blue";
                result.Reason = "快速筛查不足以形成最终结论；P-Core P95=" + result.PCoreDeltaP95.ToString("F1") + "°C，E-Core P95=" + result.ECoreDeltaP95.ToString("F1") + "°C。" + ProfileTail(result);
                return result;
            }

            bool coreSuspect = result.CoreDeltaP95 >= config.ConfirmCoreDeltaC && result.SustainedHotspotSeconds >= config.SustainedSeconds;
            if (coreSuspect && result.IsPowerCollapseAtHighTemperature)
            {
                result.Verdict = "疑似散热接触异常（机型档案强匹配）"; result.Severity = "Red";
                result.Reason = "同类型核心温差 P95 ≥" + config.ConfirmCoreDeltaC + "°C 且持续 ≥" + config.SustainedSeconds + " 秒，同时出现高温低功耗。请同条件复测。" + ProfileTail(result);
            }
            else if (coreSuspect)
            {
                result.Verdict = "疑似散热接触异常"; result.Severity = "Red";
                result.Reason = "P-Core/E-Core 分组后仍出现同类型核心温差异常并达到持续时长。请同条件复测。" + ProfileTail(result);
            }
            else if (result.IsPowerCollapseAtHighTemperature)
            {
                result.Verdict = "机型档案异常：建议完整复测"; result.Severity = "Orange";
                result.Reason = "出现高温时持续低功耗，但内置负载不等同 AIDA64 FPU，不能单独确诊。" + ProfileTail(result);
            }
            else if (result.CoreDeltaP95 >= config.WarningCoreDeltaC)
            {
                result.Verdict = "边界：建议复测"; result.Severity = "Orange";
                result.Reason = "同类型核心温差处于 " + config.WarningCoreDeltaC + "–" + config.ConfirmCoreDeltaC + "°C。请检查环境、电源和性能模式。" + ProfileTail(result);
            }
            else
            {
                result.Verdict = "未见明显异常"; result.Severity = "Green";
                result.Reason = "P-Core与E-Core分别计算后均未触发持续温差异常，也未触发高温低功耗筛查。该结果不等同于拆机确认液金状态。" + ProfileTail(result);
            }
            return result;
        }

        private static AnalysisResult AnalyzeGpu(List<Sample> steady, RunConfiguration config, MachineProfile profile, AnalysisResult result)
        {
            List<Sample> validGpu = steady.Where(IsGpuValid).ToList();
            result.GpuValidSampleRatio = steady.Count == 0 ? 0 : validGpu.Count / (double)steady.Count;
            if (result.GpuValidSampleRatio < config.MinimumValidSampleRatio)
            {
                result.Verdict = "无法判断 GPU"; result.Severity = "Gray"; result.CanJudge = false;
                result.Reason = "GPU 有效样本率仅 " + Percent(result.GpuValidSampleRatio) + "，低于要求的 " + Percent(config.MinimumValidSampleRatio) + "。";
                return result;
            }

            if (config.TestMode == "CPU + GPU 双烤")
            {
                bool requireBothCoreGroups = profile != null && profile.RequirePCoreAndECore;
                List<Sample> validCpu = steady.Where(s => IsCpuValid(s, requireBothCoreGroups)).ToList();
                result.CpuValidSampleRatio = steady.Count == 0 ? 0 : validCpu.Count / (double)steady.Count;
                result.PCoreValidSampleRatio = steady.Count == 0 ? 0 : steady.Count(s => s.PCoreCount >= 2) / (double)steady.Count;
                result.ECoreValidSampleRatio = steady.Count == 0 ? 0 : steady.Count(s => s.ECoreCount >= 2) / (double)steady.Count;
                if (result.CpuValidSampleRatio < config.MinimumValidSampleRatio)
                {
                    result.Verdict = "无法判断双烤"; result.Severity = "Gray"; result.CanJudge = false;
                    result.Reason = "GPU数据有效，但CPU有效样本率仅 " + Percent(result.CpuValidSampleRatio) + "；双烤不得只依据GPU数据输出绿色结论。";
                    return result;
                }
                FillCpuMetricsForDual(validCpu, config, result);
            }

            result.CanJudge = !config.QuickScreen;
            result.SteadyGpuTemperature = AveragePositive(validGpu.Select(s => s.GpuTemperature));
            result.SteadyGpuPower = AveragePositive(validGpu.Select(s => s.GpuPower));
            result.GpuHotSpotTemperature = AveragePositive(validGpu.Select(s => s.GpuHotSpotTemperature));
            result.GpuMemoryTemperature = AveragePositive(validGpu.Select(s => s.GpuMemoryTemperature));
            result.SteadyGpuLoad = AveragePositive(validGpu.Select(s => s.GpuLoad));
            result.SteadyGpuCoreClock = AveragePositive(validGpu.Select(s => s.GpuCoreClock));
            result.GpuDeviceName = validGpu.Select(s => s.GpuName).Where(n => !string.IsNullOrWhiteSpace(n)).GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;

            GpuModeReference gpuReference = profile == null ? null : profile.FindGpuMode(config.PerformanceMode);
            double minimumLoad = gpuReference == null ? config.GpuLoadEstablishedPct : gpuReference.MinimumLoadPct;
            double hotTemperature = gpuReference == null ? 85 : gpuReference.HotTemperatureC;
            double lowPower = gpuReference == null ? config.GpuPowerEstablishedW : gpuReference.LowPowerAtHotMax;
            int duration = gpuReference == null ? config.SustainedSeconds : gpuReference.DurationSeconds;
            result.SustainedGpuHotLowPowerSeconds = LongestDurationSeconds(validGpu,
                s => s.GpuLoad >= minimumLoad && s.GpuTemperature >= hotTemperature && s.GpuPower <= lowPower,
                config.SamplingIntervalSeconds);

            if (result.SteadyGpuLoad < minimumLoad)
            {
                result.Verdict = "GPU 压力负载不足：无法判断"; result.Severity = "Orange"; result.CanJudge = false;
                result.Reason = "分析窗口内 GPU 平均负载仅 " + result.SteadyGpuLoad.ToString("F1") + "% ，低于要求的 " + minimumLoad.ToString("F0") + "%；不能据此输出正常结论。";
                return result;
            }

            if (config.QuickScreen)
            {
                result.Verdict = "GPU 快速筛查：建议完整复测"; result.Severity = "Blue";
                result.Reason = "快速GPU负载已验证传感器和压力链路，但不形成最终液金结论。GPU Load=" + result.SteadyGpuLoad.ToString("F1") + "% / Power=" + result.SteadyGpuPower.ToString("F1") + "W。";
                return result;
            }

            bool cpuSuspect = config.TestMode == "CPU + GPU 双烤" && result.CoreDeltaP95 >= config.ConfirmCoreDeltaC && result.SustainedHotspotSeconds >= config.SustainedSeconds;
            if (cpuSuspect)
            {
                result.Verdict = "双烤：疑似 CPU 散热接触异常"; result.Severity = "Red";
                result.Reason = "双烤中按P-Core/E-Core分组后仍出现持续同类型核心温差异常；请用CPU单烤再次确认。";
            }
            else if (result.SustainedGpuHotLowPowerSeconds >= duration)
            {
                result.Verdict = "GPU 散热接触异常：建议复测"; result.Severity = "Orange";
                result.Reason = "在GPU负载持续 ≥" + minimumLoad + "% 时，高温低功耗持续达到 " + duration + " 秒。请用3DMark或游戏压力复测。";
            }
            else
            {
                double expectedMin = gpuReference == null ? 0 : config.TestMode == "GPU 单烤" ? gpuReference.SingleExpectedPowerMin : gpuReference.DualExpectedPowerMin;
                if (expectedMin > 0 && result.SteadyGpuLoad >= minimumLoad && result.SteadyGpuPower < expectedMin)
                {
                    result.Verdict = "GPU 功耗低于机型参考：建议检查档位"; result.Severity = "Orange";
                    result.Reason = "GPU负载有效，但稳态功耗 " + result.SteadyGpuPower.ToString("F1") + "W 低于当前模式参考下限 " + expectedMin.ToString("F0") + "W；这不单独证明液金异常。";
                }
                else
                {
                    result.Verdict = config.TestMode == "GPU 单烤" ? "GPU 未见明显异常" : "双烤：未见明显异常"; result.Severity = "Green";
                    result.Reason = "GPU有效样本率、持续负载和功耗均通过校验，未出现持续高温低功耗。" + ProfileTail(result);
                }
            }
            return result;
        }

        private static void FillCpuMetricsForDual(List<Sample> valid, RunConfiguration config, AnalysisResult result)
        {
            result.PCoreDeltaP95 = Percentile(valid.Where(s => s.PCoreCount >= 2).Select(s => s.PCoreDelta).ToList(), 0.95);
            result.ECoreDeltaP95 = Percentile(valid.Where(s => s.ECoreCount >= 2).Select(s => s.ECoreDelta).ToList(), 0.95);
            result.CoreDeltaP95 = Math.Max(result.PCoreDeltaP95, result.ECoreDeltaP95);
            result.SteadyPackageTemperature = AveragePositive(valid.Select(s => s.PackageTemperature));
            result.SteadyPackagePower = AveragePositive(valid.Select(s => s.PackagePower));
            result.SteadyClock = AveragePositive(valid.Select(s => s.AverageClock));
            result.SustainedPCoreHotspotSeconds = LongestDurationSeconds(valid, s => s.PCoreCount >= 2 && s.PCoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedECoreHotspotSeconds = LongestDurationSeconds(valid, s => s.ECoreCount >= 2 && s.ECoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedHotspotSeconds = Math.Max(result.SustainedPCoreHotspotSeconds, result.SustainedECoreHotspotSeconds);
        }

        private static List<Sample> SelectSteadyWindow(List<Sample> primary, RunConfiguration config, MachineProfile profile)
        {
            DateTime phaseStart = primary.First().Time;
            DateTime windowEnd = primary.Last().Time;
            ModeReference cpuReference = config.TestMode == "CPU 单烤" && profile != null ? profile.FindMode(config.PerformanceMode) : null;
            if (cpuReference != null && cpuReference.SteadyEndSecond > 0)
            {
                DateTime profileEnd = phaseStart.AddSeconds(cpuReference.SteadyEndSecond);
                if (profileEnd < windowEnd) windowEnd = profileEnd;
            }
            DateTime windowStart = windowEnd.AddSeconds(-config.AnalysisWindowSeconds);
            if (windowStart < phaseStart) windowStart = phaseStart;
            if (cpuReference != null)
            {
                DateTime profileStart = phaseStart.AddSeconds(cpuReference.SteadyStartSecond);
                if (profileStart > windowStart) windowStart = profileStart;
            }
            return primary.Where(s => s.Time >= windowStart && s.Time <= windowEnd).ToList();
        }

        private static bool IsCpuValid(Sample sample, bool requireBothCoreGroups)
        {
            bool coresValid = requireBothCoreGroups ? sample.PCoreCount >= 2 && sample.ECoreCount >= 2 : sample.PCoreCount >= 2 || sample.ECoreCount >= 2;
            return sample.PackageTemperature > 0 && sample.PackagePower > 0 && coresValid;
        }

        private static bool IsGpuValid(Sample sample)
        {
            return sample.GpuTemperature > 0 && sample.GpuPower > 0 && sample.GpuLoad >= 0 && !string.IsNullOrWhiteSpace(sample.GpuName);
        }

        private static int LongestDurationSeconds(List<Sample> samples, Func<Sample, bool> predicate, double intervalSeconds)
        {
            DateTime? start = null, previous = null;
            double best = 0;
            double maxGap = Math.Max(intervalSeconds * 2.5, intervalSeconds + 1);
            foreach (Sample sample in samples.OrderBy(s => s.Time))
            {
                if (predicate(sample))
                {
                    if (!start.HasValue || (previous.HasValue && (sample.Time - previous.Value).TotalSeconds > maxGap)) start = sample.Time;
                    previous = sample.Time;
                    best = Math.Max(best, (sample.Time - start.Value).TotalSeconds + intervalSeconds);
                }
                else { start = null; previous = null; }
            }
            return (int)Math.Floor(best);
        }

        private static double DurationSeconds(List<Sample> samples, double intervalSeconds)
        {
            return samples.Count == 0 ? 0 : Math.Max(intervalSeconds, (samples.Last().Time - samples.First().Time).TotalSeconds + intervalSeconds);
        }

        private static void ApplyProfile(AnalysisResult result, MachineProfile profile, RunConfiguration config)
        {
            if (profile == null) return;
            result.ProfileMatched = true; result.ProfileId = profile.ProfileId; result.ProfileName = profile.ProfileName;
            result.ProfileReference = ProfileLoader.Summary(profile, config.PerformanceMode, config.TestMode);
            if (config.TestMode == "CPU 单烤")
            {
                ModeReference reference = profile.FindMode(config.PerformanceMode);
                result.ProfileEvidence = reference == null ? profile.WorkloadNotice : reference.SuspectText + " " + profile.WorkloadNotice;
            }
            else
            {
                GpuModeReference reference = profile.FindGpuMode(config.PerformanceMode);
                result.ProfileEvidence = reference == null ? string.Empty : reference.SuspectText + " " + reference.NormalText;
            }
        }

        private static string ProfileTail(AnalysisResult result) { return string.IsNullOrWhiteSpace(result.ProfileReference) ? string.Empty : " 机型参考：" + result.ProfileReference; }
        private static string Percent(double value) { return (value * 100).ToString("F1") + "%"; }
        private static double AveragePositive(IEnumerable<double> values) { List<double> data = values.Where(v => v > 0).ToList(); return data.Count == 0 ? 0 : data.Average(); }
        private static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            values.Sort(); double position = (values.Count - 1) * p; int lower = (int)Math.Floor(position); int upper = (int)Math.Ceiling(position);
            return lower == upper ? values[lower] : values[lower] + (values[upper] - values[lower]) * (position - lower);
        }
        private static string DominantHotspot(List<Sample> samples)
        {
            return samples.Where(s => !string.IsNullOrWhiteSpace(s.HottestCore)).GroupBy(s => s.HottestCore).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "未知";
        }
    }
}
