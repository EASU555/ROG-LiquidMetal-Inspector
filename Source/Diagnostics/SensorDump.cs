using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace RogLiquidMetalInspector.Diagnostics
{
    internal static class SensorDump
    {
        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            Assembly assembly = Assembly.LoadFrom(Path.Combine(root, "lib", "LibreHardwareMonitorLib.dll"));
            Type type = assembly.GetType("LibreHardwareMonitor.Hardware.Computer", true);
            object computer = Activator.CreateInstance(type);
            type.GetProperty("IsGpuEnabled").SetValue(computer, true, null);
            type.GetMethod("Open").Invoke(computer, null);
            foreach (object hardware in (IEnumerable)type.GetProperty("Hardware").GetValue(computer, null))
            {
                string hardwareType = hardware.GetType().GetProperty("HardwareType").GetValue(hardware, null).ToString();
                if (hardwareType.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) < 0) continue;
                hardware.GetType().GetMethod("Update").Invoke(hardware, null);
                Console.WriteLine("HARDWARE\t{0}\t{1}", hardwareType, hardware.GetType().GetProperty("Name").GetValue(hardware, null));
                foreach (object sensor in (IEnumerable)hardware.GetType().GetProperty("Sensors").GetValue(hardware, null))
                {
                    Console.WriteLine("SENSOR\t{0}\t{1}\t{2}",
                        sensor.GetType().GetProperty("SensorType").GetValue(sensor, null),
                        sensor.GetType().GetProperty("Name").GetValue(sensor, null),
                        sensor.GetType().GetProperty("Value").GetValue(sensor, null));
                }
            }
            type.GetMethod("Close").Invoke(computer, null);
            return 0;
        }
    }
}
