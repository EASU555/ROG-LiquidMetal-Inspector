using System;

namespace RogLiquidMetalInspector
{
    internal static class SensorReaderSmoke
    {
        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            using (SensorReader sensor = new SensorReader(root))
            {
                Sample sample = sensor.Read("传感器冒烟测试");
                Console.WriteLine("Ready={0}|Source={1}|CPU={2:F1}C/{3:F1}W/{4:F0}MHz|P={5}/{6:F1}C|E={7}/{8:F1}C|GPU={9}|{10:F1}C/{11:F1}W/{12:F1}%|Error={13}",
                    sensor.IsReady, sensor.Source, sample.PackageTemperature, sample.PackagePower, sample.AverageClock,
                    sample.PCoreCount, sample.PCoreDelta, sample.ECoreCount, sample.ECoreDelta,
                    sample.GpuName, sample.GpuTemperature, sample.GpuPower, sample.GpuLoad, sensor.LastError);
                if (!sensor.IsReady || sample.PackageTemperature <= 0 || sample.PackagePower <= 0 || sample.PCoreCount < 2 || sample.ECoreCount < 2) return 2;
                if (sample.AverageClock <= 300 || sample.AverageClock > 8000) return 3;
                return 0;
            }
        }
    }
}
