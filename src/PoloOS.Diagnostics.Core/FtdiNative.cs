using System.Runtime.InteropServices;
namespace PoloOS.Diagnostics.Core;
internal static class FtdiNative
{
    public const uint FT_OK=0, FT_PURGE_RX=1, FT_PURGE_TX=2;
    [DllImport("ftd2xx.dll")] internal static extern uint FT_CreateDeviceInfoList(ref uint numDevs);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_Open(int deviceNumber, ref IntPtr handle);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_Close(IntPtr handle);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_ResetDevice(IntPtr handle);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetBaudRate(IntPtr handle,uint baudRate);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetDataCharacteristics(IntPtr handle,byte wordLength,byte stopBits,byte parity);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetFlowControl(IntPtr handle,ushort flowControl,byte xon,byte xoff);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetTimeouts(IntPtr handle,uint readTimeoutMs,uint writeTimeoutMs);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetLatencyTimer(IntPtr handle,byte latencyMs);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_GetLatencyTimer(IntPtr handle,ref byte latencyMs);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_Purge(IntPtr handle,uint mask);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetBreakOn(IntPtr handle);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_SetBreakOff(IntPtr handle);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_GetQueueStatus(IntPtr handle,ref uint rxBytes);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_Read(IntPtr handle,byte[] buffer,uint bytesToRead,ref uint bytesReturned);
    [DllImport("ftd2xx.dll")] internal static extern uint FT_Write(IntPtr handle,byte[] buffer,uint bytesToWrite,ref uint bytesWritten);
}
