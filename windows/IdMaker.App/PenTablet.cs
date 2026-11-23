// The pen tablet's driver, asked directly.
//
// A tablet driver (XP-Pen's included) installs wintab32.dll, the standard
// "WinTab" interface drawing programs read pen pressure through. This opens
// a WinTab context on a window and keeps the most recent pressure the pen
// reported, so a stroke can be drawn thicker where the pen pressed harder.
//
// It is one of two ways pressure reaches the signing screen:
//
//   Windows Ink   when "Windows Ink" is ticked in the tablet's settings the
//                 pen arrives as a pen, pressure and all, and this file is
//                 not needed (see SignaturePad's stylus handlers).
//   WinTab        when it is not ticked the pen arrives as a mouse, which
//                 has no pressure. The driver still knows it, and this is
//                 how it is asked.
//
// Constants and layouts are from the WinTab 1.4 specification (wintab.h).
// Nothing here is required: with no tablet, or no driver, every call fails
// quietly and the signature is drawn by speed instead of pressure.

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace IdMaker.App;

public sealed class PenTablet : IDisposable
{
    private const int WT_PACKET = 0x7FF0;
    private const int WT_PROXIMITY = 0x7FF5;
    private const uint WTI_DEFSYSCTX = 4;
    private const uint WTI_DEVICES = 100;
    private const uint DVC_NAME = 1;
    private const uint DVC_NPRESSURE = 15;
    private const uint CXO_SYSTEM = 0x0001;
    private const uint CXO_MESSAGES = 0x0004;
    private const uint PK_BUTTONS = 0x0040;
    private const uint PK_X = 0x0080;
    private const uint PK_Y = 0x0100;
    private const uint PK_NORMAL_PRESSURE = 0x0400;
    private const uint PacketData = PK_BUTTONS | PK_X | PK_Y | PK_NORMAL_PRESSURE;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogContext
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string lcName;
        public uint lcOptions, lcStatus, lcLocks, lcMsgBase, lcDevice, lcPktRate;
        public uint lcPktData, lcPktMode, lcMoveMask;
        public uint lcBtnDnMask, lcBtnUpMask;
        public int lcInOrgX, lcInOrgY, lcInOrgZ, lcInExtX, lcInExtY, lcInExtZ;
        public int lcOutOrgX, lcOutOrgY, lcOutOrgZ, lcOutExtX, lcOutExtY, lcOutExtZ;
        public uint lcSensX, lcSensY, lcSensZ;
        public int lcSysMode;
        public int lcSysOrgX, lcSysOrgY, lcSysExtX, lcSysExtY;
        public uint lcSysSensX, lcSysSensY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Axis { public int axMin, axMax; public uint axUnits, axResolution; }

    // The fields PacketData asks for, in the order the specification fixes.
    [StructLayout(LayoutKind.Sequential)]
    private struct Packet { public uint pkButtons; public int pkX, pkY; public uint pkNormalPressure; }

    [DllImport("wintab32.dll", EntryPoint = "WTInfoW")] private static extern uint WTInfoContext(uint category, uint index, ref LogContext output);
    [DllImport("wintab32.dll", EntryPoint = "WTInfoW")] private static extern uint WTInfoAxis(uint category, uint index, ref Axis output);
    [DllImport("wintab32.dll", EntryPoint = "WTInfoW")] private static extern uint WTInfoBuffer(uint category, uint index, IntPtr output);
    [DllImport("wintab32.dll", EntryPoint = "WTOpenW")] private static extern IntPtr WTOpen(IntPtr hwnd, ref LogContext context, [MarshalAs(UnmanagedType.Bool)] bool enable);
    [DllImport("wintab32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTClose(IntPtr context);
    [DllImport("wintab32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTPacket(IntPtr context, uint serial, ref Packet packet);
    [DllImport("wintab32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTEnable(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool enable);
    [DllImport("wintab32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTOverlap(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool toTop);

    private IntPtr _context;
    private readonly HwndSource _source;
    private readonly int _maxPressure;
    private double _pressure;
    private long _packetAt = long.MinValue;

    /// <summary>What the driver calls the tablet, such as "XP-Pen Tablet". Empty when it does not say.</summary>
    public string DeviceName { get; }

    private PenTablet(IntPtr context, HwndSource source, int maxPressure, string deviceName)
    {
        _context = context;
        _source = source;
        _maxPressure = maxPressure;
        DeviceName = deviceName;
        _source.AddHook(Hook);
    }

    /// <summary>Is a tablet driver that speaks WinTab installed on this computer at all?</summary>
    public static bool DriverInstalled =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wintab32.dll"));

    /// <summary>
    /// Open the tablet for this window. Null when there is no driver, no
    /// tablet plugged in, or a tablet that reports no pressure.
    /// </summary>
    public static PenTablet? TryOpen(IntPtr hwnd)
    {
        if (!DriverInstalled) return null;
        try
        {
            if (WTInfoBuffer(0, 0, IntPtr.Zero) == 0) return null;          // the driver is there, a tablet is not

            var context = new LogContext();
            if (WTInfoContext(WTI_DEFSYSCTX, 0, ref context) == 0) return null;
            context.lcName = Core.CardConfig.AppName;
            // CXO_SYSTEM leaves the pen moving the pointer as usual; CXO_MESSAGES posts each packet to the window
            context.lcOptions |= CXO_SYSTEM | CXO_MESSAGES;
            context.lcPktData = PacketData;
            context.lcPktMode = 0;                                           // every value absolute
            context.lcMoveMask = PacketData;

            var axis = new Axis();
            if (WTInfoAxis(WTI_DEVICES + context.lcDevice, DVC_NPRESSURE, ref axis) == 0 || axis.axMax <= 0) return null;

            var name = "";
            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                for (var i = 0; i < 1024; i++) Marshal.WriteByte(buffer, i, 0);
                // the driver writes at most the name's own length; names are a few dozen characters
                if (WTInfoBuffer(WTI_DEVICES + context.lcDevice, DVC_NAME, IntPtr.Zero) is > 0 and < 1000
                    && WTInfoBuffer(WTI_DEVICES + context.lcDevice, DVC_NAME, buffer) > 0)
                {
                    name = Marshal.PtrToStringUni(buffer)?.Trim() ?? "";
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }

            var source = HwndSource.FromHwnd(hwnd);
            if (source is null) return null;
            var handle = WTOpen(hwnd, ref context, true);
            return handle == IntPtr.Zero ? null : new PenTablet(handle, source, axis.axMax, name);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                  or SEHException or AccessViolationException or MarshalDirectiveException)
        {
            return null;         // a driver that will not answer is the same as no driver
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_context == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            if (msg == WT_PACKET && lParam == _context)
            {
                var packet = new Packet();
                if (WTPacket(_context, (uint)wParam.ToInt64(), ref packet))
                {
                    _pressure = Math.Clamp((double)packet.pkNormalPressure / _maxPressure, 0, 1);
                    _packetAt = Environment.TickCount64;
                }
            }
            else if (msg == WT_PROXIMITY && (lParam.ToInt64() & 0xFFFF) == 0)
            {
                _packetAt = long.MinValue;                                   // the pen left: its last pressure is no longer news
            }
        }
        catch (Exception e) when (e is SEHException or AccessViolationException or DllNotFoundException)
        {
            Dispose();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// The pressure the pen is reporting right now, 0..1, or -1 when no
    /// packet has arrived lately — which is what a mouse looks like.
    /// </summary>
    public double CurrentPressure => Environment.TickCount64 - _packetAt <= 120 ? _pressure : -1;

    /// <summary>The window came to the front, or went behind: a context only listens while its window is active.</summary>
    public void SetActive(bool active)
    {
        if (_context == IntPtr.Zero) return;
        try
        {
            WTEnable(_context, active);
            if (active) WTOverlap(_context, true);
        }
        catch (Exception e) when (e is SEHException or AccessViolationException or DllNotFoundException) { Dispose(); }
    }

    public void Dispose()
    {
        if (_context == IntPtr.Zero) return;
        var context = _context;
        _context = IntPtr.Zero;
        _source.RemoveHook(Hook);
        try { WTClose(context); }
        catch (Exception e) when (e is SEHException or AccessViolationException or DllNotFoundException) { /* the driver went away first */ }
    }
}
