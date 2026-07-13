using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RogLiquidMetalInspector
{
    public static class ReportWriter
    {
        public static string Write(string root, MachineInfo machine, RunConfiguration config, List<Sample> samples, AnalysisResult result, string sensorError)
        {
            string folder = Path.Combine(root, "Reports", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            WriteCsv(Path.Combine(folder, "samples.csv"), samples);
            WriteJson(Path.Combine(folder, "result.json"), machine, config, result, sensorError);
            WriteHtml(Path.Combine(folder, "summary.html"), machine, config, result, sensorError);
            return folder;
        }

        private static void WriteCsv(string path, List<Sample> samples)
        {
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("timestamp,phase,package_temperature_c,package_power_w,cpu_load_pct,average_clock_mhz,p_core_count,p_core_delta_c,hottest_p_core,e_core_count,e_core_delta_c,hottest_e_core,core_delta_c,hottest_core,gpu_name,gpu_temperature_c,gpu_hotspot_c,gpu_memory_temperature_c,gpu_power_w,gpu_load_pct,gpu_core_clock_mhz,gpu_memory_clock_mhz,gpu_memory_used_mb,gpu_memory_total_mb,gpu_fan_rpm,cpu_fan_rpm,sensor_source,core_temperatures");
                foreach (Sample sample in samples)
                {
                    List<string> cores = new List<string>();
                    foreach (CoreTemperature core in sample.CoreTemperatures)
                        cores.Add(core.Name + ":" + core.Temperature.ToString("F1", CultureInfo.InvariantCulture));
                    writer.WriteLine(string.Join(",", new string[] {
                        Csv(sample.Time.ToString("o")), Csv(sample.Phase), Num(sample.PackageTemperature), Num(sample.PackagePower), Num(sample.CpuLoad), Num(sample.AverageClock),
                        sample.PCoreCount.ToString(CultureInfo.InvariantCulture), Num(sample.PCoreDelta), Csv(sample.HottestPCore),
                        sample.ECoreCount.ToString(CultureInfo.InvariantCulture), Num(sample.ECoreDelta), Csv(sample.HottestECore), Num(sample.CoreDelta), Csv(sample.HottestCore),
                        Csv(sample.GpuName), Num(sample.GpuTemperature), Num(sample.GpuHotSpotTemperature), Num(sample.GpuMemoryTemperature), Num(sample.GpuPower), Num(sample.GpuLoad),
                        Num(sample.GpuCoreClock), Num(sample.GpuMemoryClock), Num(sample.GpuMemoryUsed), Num(sample.GpuMemoryTotal), Num(sample.GpuFanRpm), Num(sample.FanRpm),
                        Csv(sample.SensorSource), Csv(string.Join(";", cores))
                    }));
                }
            }
        }

        private static void WriteJson(string path, MachineInfo machine, RunConfiguration config, AnalysisResult result, string sensorError)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["reportVersion"] = Program.VersionText;
            root["generatedAt"] = DateTime.Now.ToString("o");
            root["machine"] = machine;
            root["configuration"] = config;
            root["result"] = result;
            root["sensorError"] = sensorError ?? string.Empty;
            root["disclaimer"] = "软件只能筛查散热接触异常，不能在不拆机的情况下确认液金物理偏移或泄漏。";
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(path, serializer.Serialize(root), new UTF8Encoding(true));
        }

        private static void WriteHtml(string path, MachineInfo machine, RunConfiguration config, AnalysisResult result, string sensorError)
        {
            string severity = result.Severity == "Red" ? "#b42318" : result.Severity == "Orange" ? "#b54708" : result.Severity == "Green" ? "#027a48" : result.Severity == "Blue" ? "#175cd3" : "#475467";
            bool cpuTest = config.TestMode != "GPU 单烤";
            bool gpuTest = config.TestMode != "CPU 单烤";
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html lang='zh-CN'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ROG 液金检测摘要</title>");
            html.Append("<style>body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;max-width:980px;margin:32px auto;padding:0 18px;color:#172b3a;background:#f7f9fc}main{background:#fff;border:1px solid #d8e1e8;border-radius:12px;padding:26px}h1{margin:0 0 4px}.meta,.note{color:#52616b;line-height:1.65}.verdict{border-left:5px solid ");
            html.Append(severity);
            html.Append(";padding:14px 18px;background:#f8fafc;font-size:20px;font-weight:650;margin:20px 0 8px}table{border-collapse:collapse;width:100%;margin:14px 0 24px}td,th{border:1px solid #d0d7de;padding:9px;text-align:left;vertical-align:top}th{background:#edf4f8}td:first-child{width:28%}.warning{color:#8a4b0f}.score{display:inline-block;padding:3px 8px;border-radius:999px;background:#edf4f8;font-weight:600}.trigger{background:#fff4e5}.critical{background:#fff0f0}</style></head><body><main>");
            html.Append("<h1>ROG 笔记本散热接触异常筛查报告</h1><div class='meta'>工具 v" + Escape(Program.VersionText) + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
            html.Append("<div class='verdict'>" + Escape(result.Verdict) + "</div><p class='note'>" + Escape(result.Reason) + "</p>");

            Section(html, "运行状态");
            Row(html, "完成状态", result.IsComplete ? "完整完成" : "未完整完成");
            Row(html, "内部状态", StatusText(result.RunStatus));
            Row(html, "判定规则", result.DiagnosticRuleVersion);
            Row(html, "综合可疑度", result.SuspicionScore.ToString("F0") + " / 100");
            Row(html, "CPU / GPU 可疑度", result.CpuSuspicionScore.ToString("F0") + " / " + result.GpuSuspicionScore.ToString("F0"));
            Row(html, "证据置信度", result.ConfidenceScore.ToString("F0") + " / 100（" + result.ConfidenceLevel + "）");
            Row(html, "独立证据类别", result.IndependentEvidenceCount.ToString(CultureInfo.InvariantCulture));
            Row(html, "分析时长", result.AnalysisDurationSeconds.ToString("F1") + " 秒");
            Row(html, "分析样本", result.SampleCount.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(result.RunError)) Row(html, "中止/错误原因", result.RunError);
            EndSection(html);

            Section(html, "设备与条件");
            Row(html, "机型", machine.Model); Row(html, "CPU", machine.Cpu); Row(html, "GPU", machine.Gpu); Row(html, "BIOS", machine.Bios); Row(html, "Windows", machine.Windows);
            Row(html, "测试模块", config.TestMode); Row(html, "性能模式", config.PerformanceMode); Row(html, "室温", config.RoomTemperature.ToString("F1") + " °C");
            Row(html, "采样间隔", config.SamplingIntervalSeconds.ToString("F2") + " 秒");
            EndSection(html);

            if (cpuTest)
            {
                Section(html, "CPU 检测");
                Row(html, "CPU 有效样本率", Percent(result.CpuValidSampleRatio));
                Row(html, "空闲 CPU 温度 / 负载", Temperature(result.IdleCpuTemperature) + " / " + PercentageMetric(result.IdleCpuLoad));
                Row(html, "P-Core 字段有效率", Percent(result.PCoreValidSampleRatio));
                Row(html, "E-Core 字段有效率", Percent(result.ECoreValidSampleRatio));
                Row(html, "P-Core 温差 P95", Delta(result.PCoreDeltaP95, result.PCoreValidSampleRatio > 0));
                Row(html, "E-Core 温差 P95", Delta(result.ECoreDeltaP95, result.ECoreValidSampleRatio > 0));
                Row(html, "P-Core 异常最长持续", result.SustainedPCoreHotspotSeconds + " 秒");
                Row(html, "E-Core 异常最长持续", result.SustainedECoreHotspotSeconds + " 秒");
                Row(html, "稳态 CPU 温度", Temperature(result.SteadyPackageTemperature));
                Row(html, "稳态 CPU Package Power", Power(result.SteadyPackagePower));
                Row(html, "稳态平均核心频率", Frequency(result.SteadyClock));
                Row(html, "最常见热点", Missing(result.DominantHotspot));
                Row(html, "固定热点占比", Percent(result.DominantHotspotShare));
                Row(html, "高温低功耗最长持续", result.SustainedPowerCollapseSeconds + " 秒");
                Row(html, "平均 CPU 负载", PercentageMetric(result.CpuAverageLoad));
                Row(html, "负载/功耗波动 CV", result.CpuLoadCoefficientOfVariation.ToString("F3") + " / " + result.CpuPowerCoefficientOfVariation.ToString("F3"));
                Row(html, "功耗/频率保持率", Percent(result.CpuPowerRetention) + " / " + Percent(result.CpuClockRetention));
                Row(html, "温度/功耗趋势", result.CpuTemperatureSlopeCPerMinute.ToString("F2") + " °C/min / " + result.CpuPowerSlopeWPerMinute.ToString("F2") + " W/min");
                Row(html, "接近 CPU 温度上限", result.NearCpuLimitSeconds + " 秒");
                EndSection(html);
            }

            if (gpuTest)
            {
                Section(html, "GPU 检测");
                Row(html, "GPU 有效样本率", Percent(result.GpuValidSampleRatio));
                Row(html, "空闲 GPU 温度 / 负载", Temperature(result.IdleGpuTemperature) + " / " + PercentageMetric(result.IdleGpuLoad));
                Row(html, "GPU 设备", Missing(result.GpuDeviceName));
                Row(html, "GPU 稳态负载", PercentageMetric(result.SteadyGpuLoad));
                Row(html, "GPU 稳态功耗", Power(result.SteadyGpuPower));
                Row(html, "GPU 稳态温度", Temperature(result.SteadyGpuTemperature));
                Row(html, "GPU 热点温度", Temperature(result.GpuHotSpotTemperature));
                Row(html, "GPU 热点差 P95", Delta(result.GpuHotspotDeltaP95, result.GpuHotSpotTemperature > 0));
                Row(html, "热点差异常最长持续", result.SustainedGpuHotspotDeltaSeconds + " 秒");
                Row(html, "GPU 显存温度", Temperature(result.GpuMemoryTemperature));
                Row(html, "GPU 稳态核心频率", Frequency(result.SteadyGpuCoreClock));
                Row(html, "高温低功耗最长持续", result.SustainedGpuHotLowPowerSeconds + " 秒");
                Row(html, "负载/功耗波动 CV", result.GpuLoadCoefficientOfVariation.ToString("F3") + " / " + result.GpuPowerCoefficientOfVariation.ToString("F3"));
                Row(html, "功耗/频率保持率", Percent(result.GpuPowerRetention) + " / " + Percent(result.GpuClockRetention));
                Row(html, "GPU 温度趋势", result.GpuTemperatureSlopeCPerMinute.ToString("F2") + " °C/min");
                EndSection(html);
            }

            if (cpuTest && gpuTest)
            {
                Section(html, "双烤功耗关系");
                Row(html, "CPU+GPU 稳态芯片功耗", Power(result.TotalSteadyPower));
                Row(html, "总功耗保持率", Percent(result.TotalPowerRetention));
                EndSection(html);
            }

            if (result.Evidence != null && result.Evidence.Count > 0)
            {
                html.Append("<h2>逐项证据</h2><table><tr><th>级别 / 分值</th><th>证据</th><th>观测与边界</th><th>依据等级</th></tr>");
                foreach (DiagnosticEvidence evidence in result.Evidence.OrderByDescending(e => e.Triggered).ThenByDescending(e => e.Score))
                {
                    string rowClass = evidence.Level == "Critical" ? " class='critical'" : evidence.Triggered ? " class='trigger'" : string.Empty;
                    html.Append("<tr" + rowClass + "><td><span class='score'>" + Escape(EvidenceLevel(evidence.Level)) + " · " + evidence.Score.ToString("F0") + "</span></td><td><strong>" + Escape(evidence.Title) + "</strong><br><span class='note'>" + Escape(evidence.Description) + "</span></td><td>" + Escape(evidence.Observed) + "<br><span class='note'>边界：" + Escape(evidence.Threshold) + "</span></td><td>" + Escape(evidence.SourceTier) + "</td></tr>");
                }
                html.Append("</table>");
            }

            if (result.ProfileMatched)
            {
                Section(html, "机型档案参考");
                Row(html, "匹配档案", result.ProfileName); Row(html, "档案参考", result.ProfileReference); Row(html, "筛查证据", result.ProfileEvidence);
                EndSection(html);
            }

            if (!string.IsNullOrWhiteSpace(sensorError)) html.Append("<h2>传感器诊断</h2><p class='note'>" + Escape(sensorError) + "</p>");
            html.Append("<h2>结论边界</h2><p class='note warning'>15/20°C 核心温差、20/30°C GPU Hot Spot 差和功耗保持率均为工程筛查线，不是 ASUS 官方确诊标准。异常结果建议在相同室温、档位和摆放条件下完成三次测试，并至少复现两次。本程序不修改风扇、功耗或电压，也不能在不拆机的情况下确认液金实体偏移、泄漏或具体界面材料故障。</p>");
            html.Append("</main></body></html>");
            File.WriteAllText(path, html.ToString(), new UTF8Encoding(true));
        }

        private static void Section(StringBuilder html, string title) { html.Append("<h2>" + Escape(title) + "</h2><table><tr><th>字段</th><th>值</th></tr>"); }
        private static void EndSection(StringBuilder html) { html.Append("</table>"); }
        private static void Row(StringBuilder html, string name, string value) { html.Append("<tr><td>" + Escape(name) + "</td><td>" + Escape(value) + "</td></tr>"); }
        private static string Escape(string text) { return (text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }
        private static string StatusText(string status)
        {
            return status == "Completed" ? "已完成" : status == "UserStopped" ? "用户停止" : status == "SafetyStopped" ? "安全保护停止" :
                status == "SensorFailed" ? "传感器失败" : status == "StressFailed" ? "压力负载失败" : status == "Error" ? "程序错误" : Missing(status);
        }
        private static string EvidenceLevel(string level)
        {
            return level == "Critical" ? "严重" : level == "Warning" ? "警告" : level == "Normal" ? "正常" : level == "Limitation" ? "限制" : "信息";
        }
        private static string Missing(string value) { return string.IsNullOrWhiteSpace(value) ? "未读取" : value; }
        private static string Temperature(double value) { return value > 0 ? value.ToString("F1") + " °C" : "未读取"; }
        private static string Delta(double value, bool available) { return available ? value.ToString("F1") + " °C" : "未读取"; }
        private static string Power(double value) { return value > 0 ? value.ToString("F1") + " W" : "未读取"; }
        private static string Frequency(double value) { return value > 0 ? value.ToString("F0") + " MHz" : "未读取"; }
        private static string PercentageMetric(double value) { return value > 0 ? value.ToString("F1") + " %" : "未读取"; }
        private static string Percent(double value) { return value > 0 ? (value * 100).ToString("F1") + " %" : "0.0 %"; }
        private static string Num(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
        private static string Csv(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\""; }
    }
}
