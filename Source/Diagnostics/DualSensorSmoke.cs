using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RogLiquidMetalInspector
{
    internal static class DualSensorSmoke
    {
        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dual-sensor-smoke.txt");
            int seconds = args.Length > 2 ? Math.Max(5, int.Parse(args[2])) : 15;
            List<string> lines = new List<string>();
            List<Task> cpuWorkers = new List<Task>();
            int exitCode = 0;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (SensorReader sensor = new SensorReader(root))
            using (GpuStress gpu = new GpuStress())
            {
                for (int worker = 0; worker < Environment.ProcessorCount; worker++)
                {
                    cpuWorkers.Add(Task.Factory.StartNew(() =>
                    {
                        double value = 0.1234;
                        while (!cancellation.IsCancellationRequested)
                            for (int n = 0; n < 50000; n++) value = Math.Sqrt(value * value + 1.000001) + Math.Sin(value);
                        GC.KeepAlive(value);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
                }
                gpu.Start(cancellation.Token);
                if (!gpu.WaitUntilInitialized(15000))
                {
                    lines.Add(gpu.Status);
                    exitCode = 2;
                }
                else
                {
                    lines.Add(gpu.Status);
                    for (int second = 0; second < seconds; second++)
                    {
                        Stopwatch clock = Stopwatch.StartNew();
                        Sample sample = sensor.Read("双烤传感器冒烟测试");
                        clock.Stop();
                        lines.Add(string.Format("{0}|ReadMs={1:F0}|CPU={2:F1}C/{3:F1}W/{4:F1}%|GPU={5:F1}C/{6:F1}W/{7:F1}%|PCI={8}|Reason={9}",
                            second + 1, clock.Elapsed.TotalMilliseconds, sample.PackageTemperature, sample.PackagePower, sample.CpuLoad,
                            sample.GpuTemperature, sample.GpuPower, sample.GpuLoad, sample.GpuPciBusId, sample.GpuClockEventReasons));
                        if (clock.Elapsed.TotalSeconds >= 5) { exitCode = 4; break; }
                        if (sample.PackageTemperature >= 100 || sample.GpuTemperature >= 90) { lines.Add("Safety temperature reached; smoke test stopped."); break; }
                        int delay = Math.Max(0, 1000 - (int)clock.Elapsed.TotalMilliseconds);
                        if (delay > 0) Thread.Sleep(delay);
                    }
                }
                cancellation.Cancel();
            }
            try { Task.WaitAll(cpuWorkers.ToArray(), 5000); } catch { }
            File.WriteAllLines(output, lines.ToArray());
            return exitCode;
        }
    }
}
