using System;
using System.Threading;

namespace RogLiquidMetalInspector
{
    internal static class GpuStressSmoke
    {
        private static int Main(string[] args)
        {
            int seconds = args.Length > 0 ? Math.Max(5, int.Parse(args[0])) : 15;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (GpuStress stress = new GpuStress())
            {
                stress.Start(cancellation.Token);
                if (!stress.WaitUntilInitialized(15000))
                {
                    Console.Error.WriteLine(stress.Status);
                    return 2;
                }
                Console.WriteLine(stress.Status);
                for (int i = 0; i < seconds; i++)
                {
                    Thread.Sleep(1000);
                    Console.WriteLine("Second={0} Dispatches={1}", i + 1, stress.DispatchCount);
                    if (stress.IsFailed) { Console.Error.WriteLine(stress.Status); return 3; }
                }
                cancellation.Cancel();
            }
            return 0;
        }
    }
}
