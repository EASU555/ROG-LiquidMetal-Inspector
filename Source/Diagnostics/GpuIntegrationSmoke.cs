using System;
using System.Threading;

namespace RogLiquidMetalInspector
{
    internal static class GpuIntegrationSmoke
    {
        private static int Main(string[] args)
        {
            string root = args[0];
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (SensorReader sensor = new SensorReader(root))
            using (GpuStress stress = new GpuStress())
            {
                stress.Start(cancellation.Token);
                if (!stress.WaitUntilInitialized(15000)) { Console.Error.WriteLine(stress.Status); return 2; }
                Console.WriteLine(stress.Status);
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(1000);
                    Sample s = sensor.Read("GPU 集成自检");
                    Console.WriteLine("{0}|Temp={1:F1}|MemoryTemp={2:F1}|Power={3:F1}|Load={4:F1}|Core={5:F0}|Memory={6:F0}|VRAM={7:F0}/{8:F0}|Fan={9:F0}|Dispatch={10}",
                        s.GpuName, s.GpuTemperature, s.GpuMemoryTemperature, s.GpuPower, s.GpuLoad, s.GpuCoreClock,
                        s.GpuMemoryClock, s.GpuMemoryUsed, s.GpuMemoryTotal, s.GpuFanRpm, stress.DispatchCount);
                }
                cancellation.Cancel();
            }
            return 0;
        }
    }
}
