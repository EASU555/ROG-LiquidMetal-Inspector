using System;

namespace RogLiquidMetalInspector
{
    public interface ISensorProvider : IDisposable
    {
        string Source { get; }
        string LastError { get; }
        bool IsReady { get; }
        Sample Read(string phase);
    }
}
