using System;
using System.Collections.Generic;
using System.IO;
using RogLiquidMetalInspector;

namespace RogLiquidMetalInspector
{
    internal static class Program
    {
        internal static string VersionText { get { return "test"; } }
    }
}

internal static class AnalysisEngineTests
{
    private static int _failures;

    private static void Main()
    {
        TestHybridCoreGroupsDoNotCrossCompare();
        TestSustainedDurationUsesTimestamps();
        TestSingleHighTemperatureIsNotLiquidMetalEvidence();
        TestHotIdleBaselineIsOnlySupportingEvidence();
        TestTwoIndependentCpuSignalsCanReachSuspect();
        TestInterruptedRunCannotRemainGreen();
        TestGpuLowLoadCannotRemainGreen();
        TestCpuLowLoadCannotRemainGreen();
        TestInvalidCoverageCannotJudge();
        TestSparseTimelineCannotLookComplete();
        TestZeroLoadsAreNotDiscarded();
        TestTemperatureRampBlocksFormalVerdict();
        TestMissingProfileModeBlocksGreen();
        TestDualPowerShiftDoesNotBecomeFalseRed();
        TestRepetitionGateRequiresTwoRuns();
        TestRuleBoundsRejectHostileValues();
        TestEmptyProfileCannotMatchEveryMachine();
        TestShippedDefaultRulesDoNotRaiseWarning();
        TestReportContainsStatusAndHybridCoreFields();
        if (_failures > 0) throw new Exception(_failures + " tests failed.");
        Console.WriteLine("ROG detector tests: 19/19 passed");
    }

    private static void TestHybridCoreGroupsDoNotCrossCompare()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(120, 1, 5, 4, true);
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(result.Severity == "Green", "P/E core groups must not be cross-compared");
        Assert(result.CoreDeltaP95 < 10, "grouped delta should remain below warning");
    }

    private static void TestSustainedDurationUsesTimestamps()
    {
        RunConfiguration config = CpuConfig();
        config.SamplingIntervalSeconds = 2;
        List<Sample> samples = CpuSamples(35, 2, 22, 5, false);
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(result.SustainedPCoreHotspotSeconds >= 60, "sustained duration must use wall clock, not sample count");
        Assert(result.Severity == "Orange", "one spatial signal alone should request retest, not diagnose");
        Assert(result.IndependentEvidenceCount == 1, "one spatial signal must remain one independent category");
    }

    private static void TestSingleHighTemperatureIsNotLiquidMetalEvidence()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 4, 4, false);
        foreach (Sample sample in samples) sample.PackageTemperature = 99;
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(result.SuspicionScore == 0, "high temperature alone must not add liquid-metal suspicion score");
        Assert(result.Severity == "Green", "high temperature alone must not be diagnosed as contact failure");
    }

    private static void TestHotIdleBaselineIsOnlySupportingEvidence()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 4, 4, false);
        DateTime start = DateTime.Today.AddMinutes(-3);
        for (int i = 0; i < 120; i++) samples.Add(new Sample {
            Time = start.AddSeconds(i), Phase = "空闲基线", PackageTemperature = 90, PackagePower = 10,
            CpuLoad = 8, PCoreCount = 8, ECoreCount = 16, PCoreDelta = 4, ECoreDelta = 4
        });
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, TestProfile());
        Assert(result.Evidence.Exists(e => e.Code == "CPU_IDLE_ABNORMALLY_HOT" && e.Triggered), "hot low-load idle baseline should be recorded");
        Assert(result.Severity != "Red", "idle temperature alone must remain supporting evidence");
    }

    private static void TestTwoIndependentCpuSignalsCanReachSuspect()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 22, 5, false);
        foreach (Sample sample in samples) { sample.PackageTemperature = 98; sample.PackagePower = 80; }
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, TestProfile());
        Assert(result.IndependentEvidenceCount >= 2, "spatial and thermal-resistance signals must be independent");
        Assert(result.SuspicionScore >= config.EvidenceSuspectScore && result.Severity == "Red", "two strong independent signals should reach suspect");
    }

    private static void TestInterruptedRunCannotRemainGreen()
    {
        RunConfiguration config = CpuConfig();
        AnalysisResult result = AnalysisEngine.Analyze(CpuSamples(120, 1, 4, 4, false), config, null);
        AnalysisEngine.ApplyRunStatus(result, "UserStopped", "test stop");
        Assert(!result.CanJudge && !result.IsComplete, "interrupted run must be incomplete");
        Assert(result.Severity != "Green" && result.Verdict.Contains("中止"), "interrupted run must not remain green");
    }

    private static void TestGpuLowLoadCannotRemainGreen()
    {
        RunConfiguration config = RunConfiguration.Create(false, 25, "增强 / Turbo", "GPU 单烤", RulesConfig.Default());
        List<Sample> samples = new List<Sample>();
        DateTime start = DateTime.Today;
        for (int i = 0; i < 120; i++) samples.Add(new Sample {
            Time = start.AddSeconds(i), Phase = "GPU 主测", GpuName = "Test GPU", GpuTemperature = 72,
            GpuPower = 110, GpuLoad = 50, GpuCoreClock = 2200
        });
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(!result.CanJudge && result.Severity != "Green", "low GPU load must not produce green result");
    }

    private static void TestCpuLowLoadCannotRemainGreen()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 4, 4, false);
        foreach (Sample sample in samples) sample.CpuLoad = 20;
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, TestProfile());
        Assert(!result.CanJudge && result.Severity == "Gray", "low CPU load must not produce a green result");
    }

    private static void TestInvalidCoverageCannotJudge()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(100, 1, 4, 4, false);
        for (int i = 0; i < 20; i++) { samples[i].PackagePower = 0; samples[i].PCoreCount = 0; samples[i].ECoreCount = 0; }
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(!result.CanJudge && result.Severity == "Gray", "less than 90% valid samples must be non-judgable");
    }

    private static void TestSparseTimelineCannotLookComplete()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(2, 300, 4, 4, false);
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(!result.CanJudge && result.Severity == "Gray", "two samples far apart must not look like a complete five-minute run");
        Assert(result.SamplingCoverageRatio < 0.1, "sparse timeline coverage must be measured against expected samples");
    }

    private static void TestZeroLoadsAreNotDiscarded()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 4, 4, false);
        for (int i = 0; i < samples.Count; i++) { samples[i].CpuLoadAvailable = true; samples[i].CpuLoad = i % 2 == 0 ? 0 : 99; }
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(result.CpuAverageLoad < 55, "zero load samples must participate in the average");
        Assert(!result.CanJudge && result.Severity != "Green", "alternating zero load must not produce green");
    }

    private static void TestTemperatureRampBlocksFormalVerdict()
    {
        RunConfiguration config = CpuConfig();
        List<Sample> samples = CpuSamples(300, 1, 4, 4, false);
        for (int i = 0; i < samples.Count; i++) samples[i].PackageTemperature = 70 + i * 0.08;
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
        Assert(!result.CanJudge && result.Severity == "Orange", "non-steady temperature must request retest");
        Assert(result.Evidence.Exists(e => e.Code == "CPU_TEMPERATURE_NOT_STEADY"), "temperature ramp blocker must be reported");
    }

    private static void TestMissingProfileModeBlocksGreen()
    {
        RunConfiguration config = RunConfiguration.Create(false, 25, "手动 / Manual", "CPU 单烤", RulesConfig.Default());
        AnalysisResult result = AnalysisEngine.Analyze(CpuSamples(300, 1, 4, 4, false), config, TestProfile());
        Assert(result.ProfileMatched && !result.ProfileModeMatched, "machine match and mode match must be separate");
        Assert(!result.CanJudge && result.Severity == "Orange", "missing current-mode reference must not produce green");
    }

    private static void TestDualPowerShiftDoesNotBecomeFalseRed()
    {
        RunConfiguration config = RunConfiguration.Create(false, 25, "增强 / Turbo", "CPU + GPU 双烤", RulesConfig.Default());
        List<Sample> samples = CpuSamples(300, 1, 5, 4, false);
        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i]; sample.Phase = "双烤主测"; sample.PackageTemperature = 90; sample.PackagePower = i < 100 ? 50 : 90;
            sample.GpuName = "Test GPU"; sample.GpuTemperature = 86; sample.GpuPower = i < 100 ? 120 : 60; sample.GpuLoad = 99;
            sample.GpuCoreClock = i < 100 ? 2400 : 1600;
        }
        AnalysisResult result = AnalysisEngine.Analyze(samples, config, TestProfile());
        Assert(result.Severity != "Red", "one GPU throttling event expressed as power and clock decay must not be double-counted into red");
    }

    private static void TestRepetitionGateRequiresTwoRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), "RogHistoryTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            MachineInfo machine = new MachineInfo { Model = "Test", Cpu = "CPU", Gpu = "GPU" };
            RunConfiguration config = CpuConfig();
            AnalysisResult first = CompletedGreen(); HistoryStore.ApplyAndSave(root, machine, config, first);
            Assert(first.Severity == "Blue" && !first.CanJudge, "first normal run must remain a candidate baseline");
            AnalysisResult second = CompletedGreen(); HistoryStore.ApplyAndSave(root, machine, config, second);
            Assert(second.Severity == "Green" && second.CanJudge && second.ReproducedRuns >= 2, "second matching normal run may satisfy repetition gate");
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    private static AnalysisResult CompletedGreen()
    {
        return new AnalysisResult { Severity = "Green", Verdict = "normal", Reason = "normal", CanJudge = true, IsComplete = true,
            RunStatus = "Completed", DataQualityPassed = true, ConfidenceScore = 80, ThermalEfficiency = 0.5 };
    }

    private static void TestRuleBoundsRejectHostileValues()
    {
        string root = Path.Combine(Path.GetTempPath(), "RogRuleTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "规则默认配置.json"), "{\"samplingIntervalSeconds\":99,\"cpuSafetyTemperatureC\":300,\"gpuSafetyTemperatureC\":300,\"evidenceWatchScore\":999,\"sensorReadTimeoutSeconds\":999}");
            RulesConfig rules = RulesConfig.Load(root);
            Assert(rules.SamplingIntervalSeconds <= 2 && rules.CpuSafetyTemperatureC <= 110 && rules.GpuSafetyTemperatureC <= 100, "hostile safety values must be restored to bounded defaults");
            Assert(rules.EvidenceWatchScore <= 100 && rules.SensorReadTimeoutSeconds <= 15 && !string.IsNullOrWhiteSpace(rules.ValidationWarning), "hostile scoring and timeout values must be bounded and reported");
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    private static void TestEmptyProfileCannotMatchEveryMachine()
    {
        MachineProfile profile = new MachineProfile();
        Assert(!profile.Matches(new MachineInfo { Model = "Any", Cpu = "Any" }), "empty profile match fields must not match all machines");
    }

    private static void TestShippedDefaultRulesDoNotRaiseWarning()
    {
        string configRoot = Path.Combine(Directory.GetCurrentDirectory(), "Source", "Config");
        RulesConfig rules = RulesConfig.Load(configRoot);
        Assert(!rules.IsModified && string.IsNullOrWhiteSpace(rules.ValidationWarning), "shipped default rules must not raise a false recovery warning");
    }

    private static void TestReportContainsStatusAndHybridCoreFields()
    {
        string root = Path.Combine(Path.GetTempPath(), "RogDetectorTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            RunConfiguration config = CpuConfig();
            List<Sample> samples = CpuSamples(2, 1, 4, 3, false);
            AnalysisResult result = AnalysisEngine.Analyze(samples, config, null);
            AnalysisEngine.ApplyRunStatus(result, "UserStopped", "test stop");
            string folder = ReportWriter.Write(root, new MachineInfo { Model = "Test", Cpu = "Test CPU", Gpu = "Test GPU", Windows = "Test OS" }, config, samples, result, string.Empty);
            string csv = File.ReadAllText(Path.Combine(folder, "samples.csv"));
            string html = File.ReadAllText(Path.Combine(folder, "summary.html"));
            string json = File.ReadAllText(Path.Combine(folder, "result.json"));
            Assert(csv.Contains("p_core_count") && csv.Contains("e_core_delta_c") && csv.Contains("sensor_read_ms"), "CSV must expose P/E core fields and sensor timing");
            Assert(html.Contains("未完整完成") && html.Contains("用户停止"), "HTML must expose incomplete status");
            Assert(json.Contains("reportVersion") && json.Contains("UserStopped"), "JSON must expose report and run status");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static RunConfiguration CpuConfig()
    {
        return RunConfiguration.Create(false, 25, "增强 / Turbo", "CPU 单烤", RulesConfig.Default());
    }

    private static MachineProfile TestProfile()
    {
        MachineProfile profile = new MachineProfile { ProfileId = "test", ProfileName = "Test", RequirePCoreAndECore = true, Modes = new List<ModeReference>(), GpuModes = new List<GpuModeReference>() };
        profile.Modes.Add(new ModeReference {
            ModeName = "增强 / Turbo", SteadyStartSecond = 0, SteadyEndSecond = 600, ExpectedPowerMin = 120, ExpectedPowerMax = 140,
            HotTemperatureC = 95, LowPowerAtHotMax = 90, DurationSeconds = 60, SuspectText = "test"
        });
        profile.GpuModes.Add(new GpuModeReference {
            ModeName = "增强 / Turbo", MinimumLoadPct = 85, SingleExpectedPowerMin = 90, SingleExpectedPowerMax = 140,
            DualExpectedPowerMin = 60, DualExpectedPowerMax = 135, HotTemperatureC = 85, LowPowerAtHotMax = 70,
            DurationSeconds = 60, SuspectText = "test", NormalText = "test"
        });
        return profile;
    }

    private static List<Sample> CpuSamples(int count, int intervalSeconds, double pDelta, double eDelta, bool crossGroupExtremes)
    {
        List<Sample> samples = new List<Sample>();
        DateTime start = DateTime.Today;
        for (int i = 0; i < count; i++)
        {
            Sample sample = new Sample {
                Time = start.AddSeconds(i * intervalSeconds), Phase = "CPU 主测", PackageTemperature = 90, PackagePower = 125,
                AverageClock = 4100, CpuLoad = 99, PCoreCount = 8, ECoreCount = 16, PCoreDelta = pDelta, ECoreDelta = eDelta,
                CoreDelta = Math.Max(pDelta, eDelta), HottestPCore = "P-Core #1", HottestECore = "E-Core #1", HottestCore = "P-Core #1"
            };
            if (crossGroupExtremes) { sample.PCoreDelta = 5; sample.ECoreDelta = 4; sample.CoreDelta = 5; }
            samples.Add(sample);
        }
        return samples;
    }

    private static void Assert(bool condition, string message)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine("FAIL: " + message);
    }
}
