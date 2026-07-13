using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RogLiquidMetalInspector
{
    internal sealed class GpuStress : IDisposable
    {
        private const ulong ClDeviceTypeGpu = 1UL << 2;
        private const ulong ClMemWriteOnly = 1UL << 1;
        private const uint ClDeviceName = 0x102B;
        private const uint ClDeviceVendor = 0x102C;
        private const uint ClDeviceGlobalMemSize = 0x101F;

        private readonly ManualResetEvent _initialized = new ManualResetEvent(false);
        private volatile bool _disposed;
        private volatile bool _ready;
        private volatile bool _failed;
        private string _deviceName = string.Empty;
        private string _lastError = string.Empty;
        private long _dispatchCount;
        private Task _worker;
        private CancellationTokenSource _stop;
        private int _started;

        public bool IsReady { get { return _ready; } }
        public bool IsFailed { get { return _failed; } }
        public string DeviceName { get { return _deviceName; } }
        public string LastError { get { return _lastError; } }
        public long DispatchCount { get { return Interlocked.Read(ref _dispatchCount); } }
        public string Status
        {
            get
            {
                if (_failed) return "OpenCL 启动失败：" + _lastError;
                if (_ready) return "OpenCL 独显负载已启动（" + _deviceName + "）";
                return "正在初始化 OpenCL 独显负载";
            }
        }

        public void Start(CancellationToken token)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("GPU 压力源不能重复启动。");
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _worker = Task.Factory.StartNew(() => Run(_stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public bool WaitUntilInitialized(int timeoutMilliseconds)
        {
            return _initialized.WaitOne(timeoutMilliseconds) && _ready;
        }

        private void Run(CancellationToken token)
        {
            IntPtr context = IntPtr.Zero;
            IntPtr queue = IntPtr.Zero;
            IntPtr program = IntPtr.Zero;
            IntPtr kernel = IntPtr.Zero;
            IntPtr output = IntPtr.Zero;
            try
            {
                IntPtr device = SelectDiscreteGpu();
                _deviceName = GetDeviceString(device, ClDeviceName);
                int error;
                IntPtr[] devices = new[] { device };
                context = clCreateContext(IntPtr.Zero, 1, devices, IntPtr.Zero, IntPtr.Zero, out error);
                Check(error, "创建 OpenCL Context");
                queue = clCreateCommandQueue(context, device, 0, out error);
                Check(error, "创建 OpenCL CommandQueue");

                string source = @"
                    __kernel void rog_stress(__global float4* output, uint seed)
                    {
                        uint id = (uint)get_global_id(0);
                        float s = (float)(seed & 4095U) * 0.00001f;
                        float4 v = (float4)(0.113f + id * 0.000001f + s,
                                           0.271f + id * 0.000002f + s,
                                           0.419f + id * 0.000003f + s,
                                           0.733f + id * 0.000004f + s);
                        for (uint i = 0; i < 768; ++i)
                        {
                            v = native_sin(mad(v, (float4)(1.000031f, 1.000037f, 1.000039f, 1.000061f),
                                               (float4)(0.00017f, 0.00019f, 0.00023f, 0.00029f)));
                            v = mad(v, v + (float4)(0.101f, 0.103f, 0.107f, 0.109f),
                                    (float4)(0.211f, 0.223f, 0.227f, 0.229f));
                        }
                        output[id] = v;
                    }";
                program = clCreateProgramWithSource(context, 1, new[] { source }, IntPtr.Zero, out error);
                Check(error, "创建 OpenCL Program");
                error = clBuildProgram(program, 1, devices, "-cl-fast-relaxed-math", IntPtr.Zero, IntPtr.Zero);
                if (error != 0) throw new InvalidOperationException("编译 OpenCL 压力内核失败（错误 " + error + "）：" + GetBuildLog(program, device));
                kernel = clCreateKernel(program, "rog_stress", out error);
                Check(error, "创建 OpenCL Kernel");

                const int workItems = 262144;
                output = clCreateBuffer(context, ClMemWriteOnly, new UIntPtr((ulong)workItems * 16UL), IntPtr.Zero, out error);
                Check(error, "分配 OpenCL 显存");
                error = clSetKernelArgMemory(kernel, 0, new UIntPtr((uint)IntPtr.Size), ref output);
                Check(error, "设置 OpenCL 输出参数");

                _ready = true;
                _initialized.Set();
                uint seed = 1;
                UIntPtr[] global = { new UIntPtr(workItems) };
                UIntPtr[] local = { new UIntPtr(256) };
                while (!token.IsCancellationRequested && !_disposed)
                {
                    error = clSetKernelArgUInt(kernel, 1, new UIntPtr(sizeof(uint)), ref seed);
                    Check(error, "设置 OpenCL 动态参数");
                    error = clEnqueueNDRangeKernel(queue, kernel, 1, null, global, local, 0, IntPtr.Zero, IntPtr.Zero);
                    Check(error, "提交 OpenCL 压力任务");
                    error = clFinish(queue);
                    Check(error, "等待 OpenCL 压力任务");
                    Interlocked.Increment(ref _dispatchCount);
                    seed++;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _failed = true;
                _ready = false;
                _initialized.Set();
            }
            finally
            {
                _ready = false;
                if (output != IntPtr.Zero) clReleaseMemObject(output);
                if (kernel != IntPtr.Zero) clReleaseKernel(kernel);
                if (program != IntPtr.Zero) clReleaseProgram(program);
                if (queue != IntPtr.Zero) clReleaseCommandQueue(queue);
                if (context != IntPtr.Zero) clReleaseContext(context);
            }
        }

        private static IntPtr SelectDiscreteGpu()
        {
            uint platformCount;
            Check(clGetPlatformIDs(0, null, out platformCount), "枚举 OpenCL 平台");
            if (platformCount == 0) throw new InvalidOperationException("系统没有可用的 OpenCL 平台。请重新安装 NVIDIA 显卡驱动。");
            IntPtr[] platforms = new IntPtr[platformCount];
            Check(clGetPlatformIDs(platformCount, platforms, out platformCount), "读取 OpenCL 平台");
            List<IntPtr> devices = new List<IntPtr>();
            foreach (IntPtr platform in platforms)
            {
                uint count;
                int error = clGetDeviceIDs(platform, ClDeviceTypeGpu, 0, null, out count);
                if (error != 0 || count == 0) continue;
                IntPtr[] found = new IntPtr[count];
                if (clGetDeviceIDs(platform, ClDeviceTypeGpu, count, found, out count) == 0) devices.AddRange(found);
            }
            if (devices.Count == 0) throw new InvalidOperationException("OpenCL 没有发现 GPU 设备。请确认独显已启用。");
            IntPtr best = IntPtr.Zero;
            long bestScore = long.MinValue;
            foreach (IntPtr device in devices)
            {
                string name = GetDeviceString(device, ClDeviceName);
                string vendor = GetDeviceString(device, ClDeviceVendor);
                ulong memory = GetDeviceUInt64(device, ClDeviceGlobalMemSize);
                long score = (long)Math.Min(memory / (1024UL * 1024UL), 100000UL);
                if (vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) score += 1000000;
                else if (vendor.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) score += 500000;
                if (name.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) score += 100000;
                if (vendor.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) score -= 500000;
                if (score > bestScore) { bestScore = score; best = device; }
            }
            if (best == IntPtr.Zero) throw new InvalidOperationException("没有找到可用于压力测试的独立 GPU。");
            return best;
        }

        private static string GetDeviceString(IntPtr device, uint parameter)
        {
            UIntPtr size;
            if (clGetDeviceInfo(device, parameter, UIntPtr.Zero, null, out size) != 0 || size == UIntPtr.Zero) return string.Empty;
            byte[] data = new byte[(int)size.ToUInt64()];
            if (clGetDeviceInfo(device, parameter, size, data, out size) != 0) return string.Empty;
            return Encoding.UTF8.GetString(data).TrimEnd('\0', ' ', '\r', '\n');
        }

        private static ulong GetDeviceUInt64(IntPtr device, uint parameter)
        {
            byte[] data = new byte[8];
            UIntPtr size;
            if (clGetDeviceInfo(device, parameter, new UIntPtr(8), data, out size) != 0) return 0;
            return BitConverter.ToUInt64(data, 0);
        }

        private static string GetBuildLog(IntPtr program, IntPtr device)
        {
            const uint ClProgramBuildLog = 0x1183;
            UIntPtr size;
            if (clGetProgramBuildInfo(program, device, ClProgramBuildLog, UIntPtr.Zero, null, out size) != 0 || size == UIntPtr.Zero) return "无编译日志";
            byte[] data = new byte[(int)size.ToUInt64()];
            clGetProgramBuildInfo(program, device, ClProgramBuildLog, size, data, out size);
            return Encoding.UTF8.GetString(data).TrimEnd('\0', ' ', '\r', '\n');
        }

        private static void Check(int error, string action)
        {
            if (error != 0) throw new InvalidOperationException(action + "失败（OpenCL 错误 " + error + "）。");
        }

        public void Dispose()
        {
            _disposed = true;
            try { if (_stop != null) _stop.Cancel(); } catch { }
            bool completed = false;
            try { completed = _worker == null || _worker.Wait(5000); } catch { completed = true; }
            if (completed)
            {
                _initialized.Dispose();
                if (_stop != null) _stop.Dispose();
            }
        }

        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clGetPlatformIDs(uint numEntries, [Out] IntPtr[] platforms, out uint numPlatforms);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clGetDeviceIDs(IntPtr platform, ulong deviceType, uint numEntries, [Out] IntPtr[] devices, out uint numDevices);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clGetDeviceInfo(IntPtr device, uint parameter, UIntPtr valueSize, [Out] byte[] value, out UIntPtr valueSizeReturned);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern IntPtr clCreateContext(IntPtr properties, uint numDevices, [In] IntPtr[] devices, IntPtr notify, IntPtr userData, out int error);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong properties, out int error);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)] private static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, [In] string[] source, IntPtr lengths, out int error);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)] private static extern int clBuildProgram(IntPtr program, uint numDevices, [In] IntPtr[] devices, string options, IntPtr notify, IntPtr userData);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint parameter, UIntPtr valueSize, [Out] byte[] value, out UIntPtr valueSizeReturned);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)] private static extern IntPtr clCreateKernel(IntPtr program, string kernelName, out int error);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, UIntPtr size, IntPtr hostPointer, out int error);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgMemory(IntPtr kernel, uint index, UIntPtr size, ref IntPtr value);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi, EntryPoint = "clSetKernelArg")] private static extern int clSetKernelArgUInt(IntPtr kernel, uint index, UIntPtr size, ref uint value);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint dimensions, [In] UIntPtr[] offset, [In] UIntPtr[] globalSize, [In] UIntPtr[] localSize, uint waitCount, IntPtr waitList, IntPtr newEvent);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clFinish(IntPtr queue);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clReleaseMemObject(IntPtr memory);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clReleaseKernel(IntPtr kernel);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clReleaseProgram(IntPtr program);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clReleaseCommandQueue(IntPtr queue);
        [DllImport("OpenCL.dll", CallingConvention = CallingConvention.Winapi)] private static extern int clReleaseContext(IntPtr context);
    }
}
