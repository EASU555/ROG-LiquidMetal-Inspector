using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RogLiquidMetalInspector
{
    public static class AnalysisEngine
    {
        public static AnalysisResult Analyze(List<Sample> samples, RunConfiguration config, MachineProfile profile)
        {
            AnalysisResult result = new AnalysisResult
            {
                IsQuickScreen = config.QuickScreen,
                DiagnosticRuleVersion = config.DiagnosticRuleVersion
            };
            ApplyProfile(result, profile, config);
            List<Sample> primary = samples.Where(IsPrimary).OrderBy(s => s.Time).ToList();
            if (primary.Count == 0)
            {
                SetUnable(result, "未获得主测数据。", "DATA_NO_PRIMARY", "没有 CPU/GPU 主测阶段样本。");
                return result;
            }

            List<Sample> steady = SelectSteadyWindow(primary, config, profile);
            result.SampleCount = steady.Count;
            result.AnalysisDurationSeconds = DurationSeconds(steady, config.SamplingIntervalSeconds);
            FillIdleMetrics(samples, result);

            bool cpuTest = config.TestMode != "GPU 单烤";
            bool gpuTest = config.TestMode != "CPU 单烤";
            bool cpuValid = true;
            bool gpuValid = true;
            if (cpuTest) AnalyzeIdleSignals(config, result);
            if (cpuTest) cpuValid = AnalyzeCpuSignals(steady, config, profile, result, gpuTest);
            if (gpuTest) gpuValid = AnalyzeGpuSignals(steady, config, profile, result, cpuTest);
            if (cpuTest && gpuTest) AnalyzeDualSignals(steady, config, result);

            CalculateScores(result, cpuTest, gpuTest);
            CalculateConfidence(result, config, cpuTest, gpuTest);
            FinalizeDecision(result, config, cpuTest, gpuTest, cpuValid && gpuValid);
            return result;
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
            result.ConfidenceScore = Math.Min(result.ConfidenceScore, 20);
            result.ConfidenceLevel = "不足";
            result.Severity = status == "SafetyStopped" ? "Red" : "Orange";
            result.Verdict = status == "UserStopped" ? "用户中止：结果无效" :
                status == "SafetyStopped" ? "安全中止：需要人工复核" :
                status == "SensorFailed" ? "传感器失败：无法判断" :
                status == "StressFailed" ? "压力负载失败：无法判断" : "测试异常中止：无法判断";
            AddEvidence(result, "RUN_INCOMPLETE", "System", "DataQuality", "Limitation", "测试未完整完成",
                string.IsNullOrWhiteSpace(runError) ? "测试流程没有完整完成。" : runError,
                status, "完整完成全部阶段", "程序运行状态", 0, false);
            result.Reason = (string.IsNullOrWhiteSpace(runError) ? "测试没有完整完成。" : runError) +
                " 当前评分仅作为排障线索，不得作为绿色正常或液金异常结论。";
        }

        private static bool AnalyzeCpuSignals(List<Sample> steady, RunConfiguration config, MachineProfile profile, AnalysisResult result, bool dual)
        {
            bool requireBoth = profile != null && profile.RequirePCoreAndECore;
            List<Sample> valid = steady.Where(s => IsCpuValid(s, requireBoth)).ToList();
            result.CpuValidSampleRatio = Ratio(valid.Count, steady.Count);
            result.PCoreValidSampleRatio = Ratio(steady.Count(s => s.PCoreCount >= 2), steady.Count);
            result.ECoreValidSampleRatio = Ratio(steady.Count(s => s.ECoreCount >= 2), steady.Count);
            if (result.CpuValidSampleRatio < config.MinimumValidSampleRatio)
            {
                AddEvidence(result, "CPU_DATA_COVERAGE", "CPU", "DataQuality", "Limitation", "CPU 有效样本不足",
                    "缺少 Package 温度、功耗或同类型核心温度。", Percent(result.CpuValidSampleRatio),
                    ">=" + Percent(config.MinimumValidSampleRatio), "程序数据质量规则", 0, false);
                return false;
            }

            result.PCoreDeltaP95 = Percentile(valid.Where(s => s.PCoreCount >= 2).Select(s => s.PCoreDelta).ToList(), 0.95);
            result.ECoreDeltaP95 = Percentile(valid.Where(s => s.ECoreCount >= 2).Select(s => s.ECoreDelta).ToList(), 0.95);
            result.CoreDeltaP95 = Math.Max(result.PCoreDeltaP95, result.ECoreDeltaP95);
            result.SteadyPackageTemperature = AveragePositive(valid.Select(s => s.PackageTemperature));
            result.SteadyPackagePower = AveragePositive(valid.Select(s => s.PackagePower));
            result.SteadyClock = AveragePositive(valid.Select(s => s.AverageClock));
            result.CpuAverageLoad = AveragePositive(valid.Select(s => s.CpuLoad));
            result.CpuLoadCoefficientOfVariation = CoefficientOfVariation(valid.Select(s => s.CpuLoad));
            result.CpuPowerCoefficientOfVariation = CoefficientOfVariation(valid.Select(s => s.PackagePower));
            result.CpuPowerRetention = Retention(valid, s => s.PackagePower);
            result.CpuClockRetention = Retention(valid, s => s.AverageClock);
            result.CpuTemperatureSlopeCPerMinute = SlopePerMinute(valid, s => s.PackageTemperature);
            result.CpuPowerSlopeWPerMinute = SlopePerMinute(valid, s => s.PackagePower);
            result.TemperatureRise = result.SteadyPackageTemperature - config.RoomTemperature;
            result.ThermalEfficiency = result.SteadyPackagePower > 0.1 ? result.TemperatureRise / result.SteadyPackagePower : 0;
            double dominantShare;
            result.DominantHotspot = DominantHotspot(valid, config.WarningCoreDeltaC, out dominantShare);
            result.DominantHotspotShare = dominantShare;
            result.SustainedPCoreHotspotSeconds = LongestDurationSeconds(valid, s => s.PCoreCount >= 2 && s.PCoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedECoreHotspotSeconds = LongestDurationSeconds(valid, s => s.ECoreCount >= 2 && s.ECoreDelta >= config.ConfirmCoreDeltaC, config.SamplingIntervalSeconds);
            result.SustainedHotspotSeconds = Math.Max(result.SustainedPCoreHotspotSeconds, result.SustainedECoreHotspotSeconds);
            result.NearCpuLimitSeconds = LongestDurationSeconds(valid, s => s.PackageTemperature >= config.CpuNearLimitTemperatureC, config.SamplingIntervalSeconds);

            if (result.CpuAverageLoad < config.MinimumCpuLoadPct)
            {
                AddEvidence(result, "CPU_LOAD_INSUFFICIENT", "CPU", "DataQuality", "Limitation", "CPU 压力负载不足",
                    "低负载下的温度、功耗和核心温差不能用于散热接触判断。", F1(result.CpuAverageLoad) + "%",
                    ">=" + F1(config.MinimumCpuLoadPct) + "%", "压力有效性规则", 0, false);
                return false;
            }

            bool severeSpatial = result.CoreDeltaP95 >= config.ConfirmCoreDeltaC && result.SustainedHotspotSeconds >= config.SustainedSeconds;
            if (severeSpatial)
            {
                AddEvidence(result, "CPU_CORE_DELTA_SUSTAINED", "CPU", "Spatial", "Critical", "同类型核心温差持续异常",
                    "P-Core 与 E-Core 分组后，仍有一组达到高温差并持续存在。",
                    "P95 " + F1(result.CoreDeltaP95) + "°C / " + result.SustainedHotspotSeconds + " 秒",
                    ">=" + F1(config.ConfirmCoreDeltaC) + "°C 且 >=" + config.SustainedSeconds + " 秒", "社区复测阈值 + 程序时间证据", 35, true);
            }
            else if (result.CoreDeltaP95 >= config.WarningCoreDeltaC)
            {
                AddEvidence(result, "CPU_CORE_DELTA_BORDERLINE", "CPU", "Spatial", "Warning", "同类型核心温差处于边界",
                    "温差可能来自负载调度、核心体质或接触不均，单独出现只建议复测。", F1(result.CoreDeltaP95) + "°C",
                    F1(config.WarningCoreDeltaC) + "–" + F1(config.ConfirmCoreDeltaC) + "°C", "社区经验阈值（低证据等级）", 15, true);
            }
            else
            {
                AddEvidence(result, "CPU_CORE_DELTA_NORMAL", "CPU", "Spatial", "Normal", "同类型核心温差未触发",
                    "P-Core 与 E-Core 已分别计算。", "P " + F1(result.PCoreDeltaP95) + "°C / E " + F1(result.ECoreDeltaP95) + "°C",
                    "<" + F1(config.WarningCoreDeltaC) + "°C", "程序分组统计", 0, false);
            }

            if (result.CoreDeltaP95 >= config.WarningCoreDeltaC && result.DominantHotspotShare >= config.DominantHotspotShareWarning)
            {
                AddEvidence(result, "CPU_FIXED_HOTSPOT", "CPU", "Spatial", "Warning", "固定核心热点重复出现",
                    "同一个热点核心在异常温差样本中反复占主导，比偶发调度尖峰更值得复测。",
                    result.DominantHotspot + " / " + Percent(result.DominantHotspotShare),
                    ">=" + Percent(config.DominantHotspotShareWarning), "程序重复性证据", 10, true);
            }

            ModeReference reference = profile == null ? null : profile.FindMode(config.PerformanceMode);
            double hotThreshold = reference == null ? config.CpuNearLimitTemperatureC - 5 : reference.HotTemperatureC;
            if (reference != null)
            {
                result.SustainedPowerCollapseSeconds = LongestDurationSeconds(valid,
                    s => s.CpuLoad >= config.MinimumCpuLoadPct && s.PackageTemperature >= reference.HotTemperatureC && s.PackagePower > 0.1 && s.PackagePower <= reference.LowPowerAtHotMax,
                    config.SamplingIntervalSeconds);
                result.IsPowerCollapseAtHighTemperature = result.SustainedPowerCollapseSeconds >= reference.DurationSeconds;
                if (result.IsPowerCollapseAtHighTemperature)
                {
                    AddEvidence(result, "CPU_HOT_LOW_POWER", "CPU", "ThermalResistance", dual ? "Warning" : "Critical", "高温低功耗持续存在",
                        dual ? "双烤受 Dynamic Boost 功耗分配影响，因此降低该证据权重，必须与其他类别证据合并。" : "高温下持续功耗明显低于同机型档案，符合热阻上升后功耗被温控压低的表现。",
                        ">=" + F1(reference.HotTemperatureC) + "°C / <=" + F1(reference.LowPowerAtHotMax) + "W / " + result.SustainedPowerCollapseSeconds + " 秒",
                        ">=" + reference.DurationSeconds + " 秒", "同代评测 + 同机型异常前后案例", dual ? 20 : 30, true);
                }
                else
                {
                    AddEvidence(result, "CPU_PROFILE_POWER_OK", "CPU", "ThermalResistance", "Normal", "未出现持续高温低功耗",
                        "机型档案的高温低功耗组合未达到持续时间。", result.SustainedPowerCollapseSeconds + " 秒",
                        "<" + reference.DurationSeconds + " 秒", "机型档案", 0, false);
                }
            }
            else
            {
                AddEvidence(result, "CPU_PROFILE_MISSING", "CPU", "DataQuality", "Limitation", "缺少机型功耗档案",
                    "未使用通用瓦数硬阈值，避免不同 BIOS、档位和散热规模造成误判。", "未匹配", "需要同机型档案", "判定边界", 0, false);
            }

            bool hotAndLoaded = result.SteadyPackageTemperature >= hotThreshold && result.CpuAverageLoad >= config.MinimumCpuLoadPct;
            AddRetentionEvidence(result, "CPU", "CPU_POWER_RETENTION", "Package 功耗保持率", result.CpuPowerRetention,
                config.PowerRetentionWarningRatio, config.PowerRetentionCriticalRatio, hotAndLoaded, 20, 10);
            if (result.SteadyClock > 0)
                AddRetentionEvidence(result, "CPU", "CPU_CLOCK_RETENTION", "平均核心频率保持率", result.CpuClockRetention,
                    config.ClockRetentionWarningRatio, config.ClockRetentionCriticalRatio, hotAndLoaded, 15, 7);
            else
                AddEvidence(result, "CPU_CLOCK_MISSING", "CPU", "DataQuality", "Limitation", "CPU 频率字段缺失",
                    "无法使用频率保持率交叉验证功耗下降。", "未读取", "需要核心频率", "传感器限制", 0, false);

            if (result.NearCpuLimitSeconds > 0)
            {
                AddEvidence(result, "CPU_NEAR_TJMAX", "CPU", "ThermalLimit", "Info", "CPU 接近温度上限",
                    "Intel 说明接近 Tjunction 上限会触发降频和功耗控制；高温本身不是液金偏移证据。",
                    result.NearCpuLimitSeconds + " 秒 >=" + F1(config.CpuNearLimitTemperatureC) + "°C",
                    "Intel 275HX Max Operating Temperature 105°C", "Intel 官方规格", 0, false);
            }
            return true;
        }

        private static bool AnalyzeGpuSignals(List<Sample> steady, RunConfiguration config, MachineProfile profile, AnalysisResult result, bool dual)
        {
            List<Sample> valid = steady.Where(IsGpuValid).ToList();
            result.GpuValidSampleRatio = Ratio(valid.Count, steady.Count);
            if (result.GpuValidSampleRatio < config.MinimumValidSampleRatio)
            {
                AddEvidence(result, "GPU_DATA_COVERAGE", "GPU", "DataQuality", "Limitation", "GPU 有效样本不足",
                    "缺少独显名称、核心温度或功耗。", Percent(result.GpuValidSampleRatio),
                    ">=" + Percent(config.MinimumValidSampleRatio), "程序数据质量规则", 0, false);
                return false;
            }

            result.SteadyGpuTemperature = AveragePositive(valid.Select(s => s.GpuTemperature));
            result.SteadyGpuPower = AveragePositive(valid.Select(s => s.GpuPower));
            result.GpuHotSpotTemperature = AveragePositive(valid.Select(s => s.GpuHotSpotTemperature));
            result.GpuMemoryTemperature = AveragePositive(valid.Select(s => s.GpuMemoryTemperature));
            result.SteadyGpuLoad = AveragePositive(valid.Select(s => s.GpuLoad));
            result.SteadyGpuCoreClock = AveragePositive(valid.Select(s => s.GpuCoreClock));
            result.GpuLoadCoefficientOfVariation = CoefficientOfVariation(valid.Select(s => s.GpuLoad));
            result.GpuPowerCoefficientOfVariation = CoefficientOfVariation(valid.Select(s => s.GpuPower));
            result.GpuPowerRetention = Retention(valid, s => s.GpuPower);
            result.GpuClockRetention = Retention(valid, s => s.GpuCoreClock);
            result.GpuTemperatureSlopeCPerMinute = SlopePerMinute(valid, s => s.GpuTemperature);
            result.GpuDeviceName = valid.Select(s => s.GpuName).Where(n => !string.IsNullOrWhiteSpace(n)).GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;

            GpuModeReference reference = profile == null ? null : profile.FindGpuMode(config.PerformanceMode);
            double minimumLoad = reference == null ? config.GpuLoadEstablishedPct : reference.MinimumLoadPct;
            double hotTemperature = reference == null ? 85 : reference.HotTemperatureC;
            double lowPower = reference == null ? config.GpuPowerEstablishedW : reference.LowPowerAtHotMax;
            int duration = reference == null ? config.SustainedSeconds : reference.DurationSeconds;
            if (result.SteadyGpuLoad < minimumLoad)
            {
                AddEvidence(result, "GPU_LOAD_INSUFFICIENT", "GPU", "DataQuality", "Limitation", "GPU 压力负载不足",
                    "低负载下的温度和功耗不能用于散热接触判断。", F1(result.SteadyGpuLoad) + "%",
                    ">=" + F1(minimumLoad) + "%", "压力有效性规则", 0, false);
                return false;
            }

            List<Sample> hotspotSamples = valid.Where(s => s.GpuHotSpotTemperature > s.GpuTemperature && s.GpuTemperature > 0).ToList();
            double hotspotCoverage = Ratio(hotspotSamples.Count, valid.Count);
            if (hotspotCoverage >= config.MinimumValidSampleRatio)
            {
                result.GpuHotspotDeltaP95 = Percentile(hotspotSamples.Select(s => s.GpuHotSpotTemperature - s.GpuTemperature).ToList(), 0.95);
                result.SustainedGpuHotspotDeltaSeconds = LongestDurationSeconds(hotspotSamples,
                    s => s.GpuHotSpotTemperature - s.GpuTemperature >= config.GpuHotspotDeltaWarningC, config.SamplingIntervalSeconds);
                if (result.GpuHotspotDeltaP95 >= config.GpuHotspotDeltaCriticalC && result.SustainedGpuHotspotDeltaSeconds >= config.SustainedSeconds)
                {
                    AddEvidence(result, "GPU_HOTSPOT_DELTA_CRITICAL", "GPU", "Spatial", "Critical", "GPU 热点差持续偏大",
                        "热点与平均核心温度差持续偏大，支持裸晶接触/覆盖不均，但仍需同卡复测。",
                        "P95 " + F1(result.GpuHotspotDeltaP95) + "°C / " + result.SustainedGpuHotspotDeltaSeconds + " 秒",
                        ">=" + F1(config.GpuHotspotDeltaCriticalC) + "°C", "社区维修前后案例（低证据等级）", 35, true);
                }
                else if (result.GpuHotspotDeltaP95 >= config.GpuHotspotDeltaWarningC)
                {
                    AddEvidence(result, "GPU_HOTSPOT_DELTA_WARNING", "GPU", "Spatial", "Warning", "GPU 热点差处于边界",
                        "热点差受芯片布局、传感器实现和安装压力影响，单独出现只建议复测。",
                        F1(result.GpuHotspotDeltaP95) + "°C", ">=" + F1(config.GpuHotspotDeltaWarningC) + "°C",
                        "社区经验阈值（低证据等级）", 15, true);
                }
                else
                {
                    AddEvidence(result, "GPU_HOTSPOT_DELTA_NORMAL", "GPU", "Spatial", "Normal", "GPU 热点差未触发",
                        "驱动提供了足够热点样本。", F1(result.GpuHotspotDeltaP95) + "°C",
                        "<" + F1(config.GpuHotspotDeltaWarningC) + "°C", "程序统计", 0, false);
                }
            }
            else
            {
                bool blackwell = result.GpuDeviceName.IndexOf("RTX 50", StringComparison.OrdinalIgnoreCase) >= 0;
                AddEvidence(result, "GPU_HOTSPOT_UNAVAILABLE", "GPU", "DataQuality", "Limitation", "GPU Hot Spot 不可用",
                    blackwell ? "RTX 50 系列驱动通常不向通用监控软件提供 Hot Spot；程序不会用 0 代替，也不会因此伪造空间温差结论。" : "驱动没有提供足够 Hot Spot 样本；程序不会用 0 代替，也不会伪造空间温差结论。",
                    Percent(hotspotCoverage), ">=" + Percent(config.MinimumValidSampleRatio), "HWiNFO 作者说明 / 驱动限制", 0, false);
            }

            if (reference != null)
            {
                result.SustainedGpuHotLowPowerSeconds = LongestDurationSeconds(valid,
                    s => s.GpuLoad >= minimumLoad && s.GpuTemperature >= hotTemperature && s.GpuPower <= lowPower,
                    config.SamplingIntervalSeconds);
                if (result.SustainedGpuHotLowPowerSeconds >= duration)
                {
                    AddEvidence(result, "GPU_HOT_LOW_POWER", "GPU", "ThermalResistance", dual ? "Warning" : "Critical", "GPU 高温低功耗持续存在",
                        dual ? "双烤功耗会被 Dynamic Boost 动态分配，因此该证据在双烤中降权。" : "负载成立时，高温伴随持续低功耗，支持温控压制或散热热阻异常。",
                        ">=" + F1(hotTemperature) + "°C / <=" + F1(lowPower) + "W / " + result.SustainedGpuHotLowPowerSeconds + " 秒",
                        ">=" + duration + " 秒", "机型档案", dual ? 20 : 30, true);
                }
                else
                {
                    AddEvidence(result, "GPU_HOT_LOW_POWER_CLEAR", "GPU", "ThermalResistance", "Normal", "GPU 未出现持续高温低功耗",
                        "有效负载下未达到机型高温低功耗组合。", result.SustainedGpuHotLowPowerSeconds + " 秒",
                        "<" + duration + " 秒", "机型档案", 0, false);
                }
            }
            else
            {
                AddEvidence(result, "GPU_PROFILE_MISSING", "GPU", "DataQuality", "Limitation", "缺少 GPU 机型功耗档案",
                    "不同 Laptop GPU 的 TGP 范围差异很大，程序不会使用通用瓦数阈值判液金异常。",
                    "未匹配", "需要同 GPU/同机型档案", "判定边界", 0, false);
            }

            bool hotAndLoaded = result.SteadyGpuTemperature >= hotTemperature - 2 && result.SteadyGpuLoad >= minimumLoad;
            AddRetentionEvidence(result, "GPU", "GPU_POWER_RETENTION", "GPU 功耗保持率", result.GpuPowerRetention,
                config.PowerRetentionWarningRatio, config.PowerRetentionCriticalRatio, hotAndLoaded, 20, 10);
            if (result.SteadyGpuCoreClock > 0)
                AddRetentionEvidence(result, "GPU", "GPU_CLOCK_RETENTION", "GPU 核心频率保持率", result.GpuClockRetention,
                    config.ClockRetentionWarningRatio, config.ClockRetentionCriticalRatio, hotAndLoaded, 15, 7);
            else
                AddEvidence(result, "GPU_CLOCK_MISSING", "GPU", "DataQuality", "Limitation", "GPU 频率字段缺失",
                    "无法使用频率保持率交叉验证功耗下降。", "未读取", "需要 GPU 核心频率", "传感器限制", 0, false);

            double expectedMin = reference == null ? 0 : dual ? reference.DualExpectedPowerMin : reference.SingleExpectedPowerMin;
            if (expectedMin > 0 && result.SteadyGpuPower < expectedMin && result.SteadyGpuLoad >= minimumLoad && result.SteadyGpuTemperature < hotTemperature)
            {
                AddEvidence(result, "GPU_POWER_BELOW_PROFILE_COOL", "GPU", "Configuration", "Warning", "GPU 功耗低于档案但温度不高",
                    "更像性能档位、驱动、适配器或 Dynamic Boost 配置差异，不作为液金异常强证据。",
                    F1(result.SteadyGpuPower) + "W", ">=" + F1(expectedMin) + "W", "机型档案", 8, true);
            }
            result.NearGpuLimitSeconds = LongestDurationSeconds(valid, s => s.GpuTemperature >= hotTemperature, config.SamplingIntervalSeconds);
            if (result.NearGpuLimitSeconds > 0)
                AddEvidence(result, "GPU_HIGH_TEMPERATURE", "GPU", "ThermalLimit", "Info", "GPU 进入高温区间",
                    "高温只有与低功耗、频率下降或热点差共同出现时才提高液金异常评分。",
                    result.NearGpuLimitSeconds + " 秒 >=" + F1(hotTemperature) + "°C", "组合证据要求", "程序判定边界", 0, false);
            return true;
        }

        private static void AnalyzeDualSignals(List<Sample> steady, RunConfiguration config, AnalysisResult result)
        {
            List<Sample> valid = steady.Where(s => s.PackagePower > 0 && s.GpuPower > 0).ToList();
            result.TotalSteadyPower = AveragePositive(valid.Select(s => s.PackagePower + s.GpuPower));
            result.TotalPowerRetention = Retention(valid, s => s.PackagePower + s.GpuPower);
            AddEvidence(result, "DUAL_DYNAMIC_BOOST", "System", "Configuration", "Limitation", "双烤存在动态功耗分配",
                "NVIDIA Dynamic Boost 会在 CPU、GPU 和显存间动态转移功耗，因此不能要求 CPU 与 GPU 同时达到各自单烤上限。",
                "CPU " + F1(result.SteadyPackagePower) + "W + GPU " + F1(result.SteadyGpuPower) + "W",
                "按总功耗趋势与分组件证据联合解释", "NVIDIA 官方 Dynamic Boost 文档", 0, false);
            if (result.TotalPowerRetention > 0 && result.TotalPowerRetention < 0.75 &&
                result.CpuAverageLoad >= config.MinimumCpuLoadPct && result.SteadyGpuLoad >= config.GpuLoadEstablishedPct &&
                (result.SteadyPackageTemperature >= config.CpuNearLimitTemperatureC - 5 || result.NearGpuLimitSeconds > 0))
            {
                AddEvidence(result, "DUAL_TOTAL_POWER_DECAY", "System", "TemporalDegradation", "Warning", "双烤总芯片功耗明显衰减",
                    "CPU 与 GPU 负载仍成立，但总芯片功耗随热积累下降，支持整机热饱和；不能单独定位液金位置。",
                    Percent(result.TotalPowerRetention), ">=75%", "程序时间序列工程规则", 8, true);
            }
        }

        private static void AddRetentionEvidence(AnalysisResult result, string component, string code, string title, double ratio,
            double warning, double critical, bool thermalContext, double criticalScore, double warningScore)
        {
            if (ratio <= 0)
            {
                AddEvidence(result, code + "_MISSING", component, "DataQuality", "Limitation", title + "不可计算",
                    "首段或末段缺少有效数据。", "未读取", "需要完整首末段", "传感器限制", 0, false);
                return;
            }
            if (thermalContext && ratio < critical)
                AddEvidence(result, code + "_CRITICAL", component, "TemporalDegradation", "Critical", title + "严重下降",
                    "在高温且负载成立时，末段相对首段明显下降，支持随热积累发生性能压制。",
                    Percent(ratio), ">=" + Percent(critical), "程序时间序列工程规则", criticalScore, true);
            else if (thermalContext && ratio < warning)
                AddEvidence(result, code + "_WARNING", component, "TemporalDegradation", "Warning", title + "下降",
                    "在高温且负载成立时，末段相对首段下降；需与空间温差或高温低功耗交叉验证。",
                    Percent(ratio), ">=" + Percent(warning), "程序时间序列工程规则", warningScore, true);
            else
                AddEvidence(result, code + "_OK", component, "TemporalDegradation", "Normal", title + "稳定",
                    thermalContext ? "高温阶段没有达到功耗/频率衰减阈值。" : "当前未同时满足高温与负载条件，不把保持率变化归因于散热。",
                    Percent(ratio), ">=" + Percent(warning), "程序时间序列工程规则", 0, false);
        }

        private static void CalculateScores(AnalysisResult result, bool cpuTest, bool gpuTest)
        {
            result.CpuSuspicionScore = ClampScore(result.Evidence.Where(e => e.Triggered && e.Component == "CPU").Sum(e => e.Score));
            result.GpuSuspicionScore = ClampScore(result.Evidence.Where(e => e.Triggered && e.Component == "GPU").Sum(e => e.Score));
            double systemScore = result.Evidence.Where(e => e.Triggered && e.Component == "System").Sum(e => e.Score);
            string focus = !gpuTest || (cpuTest && result.CpuSuspicionScore >= result.GpuSuspicionScore) ? "CPU" : "GPU";
            result.SuspicionScore = ClampScore(Math.Max(result.CpuSuspicionScore, result.GpuSuspicionScore) + systemScore);
            result.IndependentEvidenceCount = result.Evidence.Where(e => e.Triggered && e.Score > 0 && (e.Component == focus || e.Component == "System"))
                .Select(e => e.Category).Distinct().Count();
        }

        private static void CalculateConfidence(AnalysisResult result, RunConfiguration config, bool cpuTest, bool gpuTest)
        {
            double coverage = cpuTest && gpuTest ? Math.Min(result.CpuValidSampleRatio, result.GpuValidSampleRatio) : cpuTest ? result.CpuValidSampleRatio : result.GpuValidSampleRatio;
            double confidence = Math.Min(1, coverage) * 35;
            confidence += Math.Min(1, result.AnalysisDurationSeconds / Math.Max(1, config.MinimumFullAnalysisSeconds)) * 25;
            int components = (cpuTest ? 1 : 0) + (gpuTest ? 1 : 0);
            double stability = 0;
            if (cpuTest && result.CpuAverageLoad >= config.MinimumCpuLoadPct && result.CpuLoadCoefficientOfVariation <= config.MaximumLoadCoefficientOfVariation) stability += 20.0 / components;
            if (gpuTest && result.SteadyGpuLoad >= config.GpuLoadEstablishedPct && result.GpuLoadCoefficientOfVariation <= config.MaximumLoadCoefficientOfVariation) stability += 20.0 / components;
            confidence += stability;
            if (result.ProfileMatched) confidence += 10;
            if (config.RoomTemperature >= 10 && config.RoomTemperature <= 40) confidence += 5;
            if (cpuTest && result.PCoreValidSampleRatio >= config.MinimumValidSampleRatio && result.ECoreValidSampleRatio >= config.MinimumValidSampleRatio) confidence += 5.0 / components;
            if (gpuTest && result.GpuValidSampleRatio >= config.MinimumValidSampleRatio) confidence += 5.0 / components;

            if (cpuTest && result.CpuPowerCoefficientOfVariation > config.MaximumPowerCoefficientOfVariation) confidence -= 10;
            if (gpuTest && result.GpuPowerCoefficientOfVariation > config.MaximumPowerCoefficientOfVariation) confidence -= 10;
            if (cpuTest && Math.Abs(result.CpuTemperatureSlopeCPerMinute) > config.TemperatureSlopeUnstableCPerMinute) confidence -= 8;
            if (gpuTest && Math.Abs(result.GpuTemperatureSlopeCPerMinute) > config.TemperatureSlopeUnstableCPerMinute) confidence -= 8;
            if (!result.ProfileMatched) confidence = Math.Min(confidence, 80);
            if (config.QuickScreen) confidence = Math.Min(confidence, 55);
            result.ConfidenceScore = ClampScore(confidence);
            result.ConfidenceLevel = result.ConfidenceScore >= 80 ? "高" : result.ConfidenceScore >= 65 ? "中" : result.ConfidenceScore >= 45 ? "低" : "不足";

            if (cpuTest && result.CpuLoadCoefficientOfVariation > config.MaximumLoadCoefficientOfVariation)
                AddEvidence(result, "CPU_LOAD_UNSTABLE", "CPU", "DataQuality", "Limitation", "CPU 负载波动偏大",
                    "负载波动会影响功耗、频率和核心温差的可比性。", F2(result.CpuLoadCoefficientOfVariation),
                    "CV <=" + F2(config.MaximumLoadCoefficientOfVariation), "程序数据质量规则", 0, false);
            if (gpuTest && result.GpuLoadCoefficientOfVariation > config.MaximumLoadCoefficientOfVariation)
                AddEvidence(result, "GPU_LOAD_UNSTABLE", "GPU", "DataQuality", "Limitation", "GPU 负载波动偏大",
                    "负载波动会影响功耗和温度的可比性。", F2(result.GpuLoadCoefficientOfVariation),
                    "CV <=" + F2(config.MaximumLoadCoefficientOfVariation), "程序数据质量规则", 0, false);
        }

        private static void FinalizeDecision(AnalysisResult result, RunConfiguration config, bool cpuTest, bool gpuTest, bool dataValid)
        {
            result.CanJudge = dataValid && !config.QuickScreen && result.ConfidenceScore >= config.MinimumDecisionConfidence;
            string focus = !gpuTest || (cpuTest && result.CpuSuspicionScore >= result.GpuSuspicionScore) ? "CPU" : "GPU";
            string prefix = cpuTest && gpuTest ? "双烤" : focus;
            if (!dataValid)
            {
                result.Verdict = "数据不足：无法综合判断"; result.Severity = "Gray"; result.CanJudge = false;
            }
            else if (config.QuickScreen)
            {
                result.Verdict = result.SuspicionScore >= config.EvidenceWatchScore ? "快速筛查：发现异常线索，建议完整复测" : "快速筛查：未见明显线索";
                result.Severity = result.SuspicionScore >= config.EvidenceWatchScore ? "Orange" : "Blue";
                result.CanJudge = false;
            }
            else if (result.ConfidenceScore < config.MinimumDecisionConfidence)
            {
                result.Verdict = "证据置信度不足：建议复测"; result.Severity = "Orange"; result.CanJudge = false;
            }
            else if (result.SuspicionScore >= config.EvidenceStrongScore && result.IndependentEvidenceCount >= 2)
            {
                result.Verdict = prefix + "：本次高度疑似散热接触异常，需重复验证"; result.Severity = "Red";
            }
            else if (result.SuspicionScore >= config.EvidenceSuspectScore && result.IndependentEvidenceCount >= 2)
            {
                result.Verdict = prefix + "：本次疑似散热接触异常，需重复验证"; result.Severity = "Red";
            }
            else if (result.SuspicionScore >= config.EvidenceWatchScore)
            {
                result.Verdict = prefix + "：异常线索不足以确诊，建议复测"; result.Severity = "Orange";
            }
            else
            {
                result.Verdict = prefix + "：本次未见明显散热接触异常"; result.Severity = "Green";
            }

            List<string> top = result.Evidence.Where(e => e.Triggered && e.Score > 0).OrderByDescending(e => e.Score).Take(3).Select(e => e.Title).ToList();
            string evidenceText = top.Count == 0 ? "没有触发异常评分项" : "主要线索：" + string.Join("、", top.ToArray());
            result.Reason = "综合可疑度 " + F0(result.SuspicionScore) + "/100，置信度 " + F0(result.ConfidenceScore) + "/100（" + result.ConfidenceLevel +
                "），独立证据类别 " + result.IndependentEvidenceCount + "。" + evidenceText + "。单一高温不计为液金偏移证据；异常结论建议同条件三次测试至少复现两次，软件结论不等同拆机确认。";
        }

        private static void AnalyzeIdleSignals(RunConfiguration config, AnalysisResult result)
        {
            if (result.IdleCpuTemperature <= 0 || result.IdleCpuLoad <= 0)
            {
                AddEvidence(result, "CPU_IDLE_BASELINE_MISSING", "CPU", "DataQuality", "Limitation", "CPU 空闲基线不完整",
                    "空闲温度或负载缺失，不能使用异常空闲温度作为辅助证据。", "未读取", "需要空闲温度与负载", "程序数据质量规则", 0, false);
                return;
            }
            if (result.IdleCpuTemperature >= config.IdleCpuWarningTemperatureC && result.IdleCpuLoad <= config.MaximumIdleCpuLoadPct)
            {
                AddEvidence(result, "CPU_IDLE_ABNORMALLY_HOT", "CPU", "Baseline", "Warning", "低负载空闲温度异常偏高",
                    "同机型异常个案出现过低负载空闲高温，但后台任务、风扇静停和进风受阻也会造成相同表现，因此只计辅助证据。",
                    F1(result.IdleCpuTemperature) + "°C / " + F1(result.IdleCpuLoad) + "%",
                    ">=" + F1(config.IdleCpuWarningTemperatureC) + "°C 且负载 <=" + F1(config.MaximumIdleCpuLoadPct) + "%",
                    "同机型异常前后个案（n=1，低证据等级）", 15, true);
            }
            else
            {
                AddEvidence(result, "CPU_IDLE_BASELINE_OK", "CPU", "Baseline", "Normal", "CPU 空闲基线未触发异常",
                    "空闲温度与负载组合未达到辅助筛查线。", F1(result.IdleCpuTemperature) + "°C / " + F1(result.IdleCpuLoad) + "%",
                    "低负载时 <" + F1(config.IdleCpuWarningTemperatureC) + "°C", "程序工程筛查规则", 0, false);
            }
        }

        private static void FillIdleMetrics(List<Sample> samples, AnalysisResult result)
        {
            List<Sample> idle = samples.Where(s => s.Phase == "空闲基线").ToList();
            result.IdleCpuTemperature = AveragePositive(idle.Select(s => s.PackageTemperature));
            result.IdleCpuLoad = AveragePositive(idle.Select(s => s.CpuLoad));
            result.IdleGpuTemperature = AveragePositive(idle.Select(s => s.GpuTemperature));
            result.IdleGpuLoad = AveragePositive(idle.Select(s => s.GpuLoad));
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

        private static bool IsPrimary(Sample sample) { return sample.Phase == "CPU 主测" || sample.Phase == "GPU 主测" || sample.Phase == "双烤主测"; }
        private static bool IsCpuValid(Sample sample, bool requireBoth)
        {
            bool cores = requireBoth ? sample.PCoreCount >= 2 && sample.ECoreCount >= 2 : sample.PCoreCount >= 2 || sample.ECoreCount >= 2;
            return sample.PackageTemperature > 0 && sample.PackagePower > 0 && cores;
        }
        private static bool IsGpuValid(Sample sample) { return sample.GpuTemperature > 0 && sample.GpuPower > 0 && !string.IsNullOrWhiteSpace(sample.GpuName); }

        private static void AddEvidence(AnalysisResult result, string code, string component, string category, string level, string title,
            string description, string observed, string threshold, string sourceTier, double score, bool triggered)
        {
            result.Evidence.Add(new DiagnosticEvidence
            {
                Code = code, Component = component, Category = category, Level = level, Title = title, Description = description,
                Observed = observed, Threshold = threshold, SourceTier = sourceTier, Score = score, Triggered = triggered
            });
        }

        private static void SetUnable(AnalysisResult result, string reason, string code, string description)
        {
            result.Verdict = "无法判断"; result.Severity = "Gray"; result.Reason = reason; result.CanJudge = false;
            result.ConfidenceScore = 0; result.ConfidenceLevel = "不足";
            AddEvidence(result, code, "System", "DataQuality", "Limitation", "主测数据不可用", description, "无", "需要完整主测", "程序数据质量规则", 0, false);
        }

        private static double Retention(List<Sample> samples, Func<Sample, double> selector)
        {
            if (samples.Count < 3) return 0;
            DateTime start = samples.First().Time;
            double duration = Math.Max(1, (samples.Last().Time - start).TotalSeconds);
            DateTime firstEnd = start.AddSeconds(duration / 3.0);
            DateTime lastStart = start.AddSeconds(duration * 2.0 / 3.0);
            double first = AveragePositive(samples.Where(s => s.Time <= firstEnd).Select(selector));
            double last = AveragePositive(samples.Where(s => s.Time >= lastStart).Select(selector));
            return first > 0 ? last / first : 0;
        }

        private static double SlopePerMinute(List<Sample> samples, Func<Sample, double> selector)
        {
            List<Sample> valid = samples.Where(s => selector(s) > 0).OrderBy(s => s.Time).ToList();
            if (valid.Count < 3) return 0;
            DateTime start = valid.First().Time;
            List<double> x = valid.Select(s => (s.Time - start).TotalMinutes).ToList();
            List<double> y = valid.Select(selector).ToList();
            double xMean = x.Average(), yMean = y.Average();
            double denominator = x.Sum(v => (v - xMean) * (v - xMean));
            if (denominator <= 0) return 0;
            return x.Select((v, i) => (v - xMean) * (y[i] - yMean)).Sum() / denominator;
        }

        private static double CoefficientOfVariation(IEnumerable<double> values)
        {
            List<double> data = values.Where(v => v > 0).ToList();
            if (data.Count < 2) return 0;
            double mean = data.Average();
            if (mean <= 0) return 0;
            double variance = data.Sum(v => (v - mean) * (v - mean)) / data.Count;
            return Math.Sqrt(variance) / mean;
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

        private static string DominantHotspot(List<Sample> samples, double warningDelta, out double share)
        {
            List<string> names = new List<string>();
            foreach (Sample s in samples)
            {
                if (s.PCoreDelta < warningDelta && s.ECoreDelta < warningDelta) continue;
                string name = s.PCoreDelta >= s.ECoreDelta ? s.HottestPCore : s.HottestECore;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            if (names.Count == 0) { share = 0; return "未形成固定热点"; }
            IGrouping<string, string> dominant = names.GroupBy(n => n).OrderByDescending(g => g.Count()).First();
            share = dominant.Count() / (double)names.Count;
            return dominant.Key;
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

        private static double AveragePositive(IEnumerable<double> values) { List<double> data = values.Where(v => v > 0).ToList(); return data.Count == 0 ? 0 : data.Average(); }
        private static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            values.Sort(); double position = (values.Count - 1) * p; int lower = (int)Math.Floor(position); int upper = (int)Math.Ceiling(position);
            return lower == upper ? values[lower] : values[lower] + (values[upper] - values[lower]) * (position - lower);
        }
        private static double Ratio(int count, int total) { return total <= 0 ? 0 : count / (double)total; }
        private static double ClampScore(double value) { return Math.Max(0, Math.Min(100, value)); }
        private static string Percent(double value) { return (value * 100).ToString("F1", CultureInfo.InvariantCulture) + "%"; }
        private static string F0(double value) { return value.ToString("F0", CultureInfo.InvariantCulture); }
        private static string F1(double value) { return value.ToString("F1", CultureInfo.InvariantCulture); }
        private static string F2(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
    }
}
