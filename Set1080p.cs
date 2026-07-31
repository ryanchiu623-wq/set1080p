using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

// Set1080p (enhanced + self-check)
//  1. 投影模式 -> 同步 (Duplicate / clone)
//  2. 解析度  -> 1920 x 1080
//  3. DPI 縮放 -> 125%
// 靜默執行；解析度/DPI 失敗會自動重試一次，仍失敗才彈出訊息並寫 log 到 D:。
static class Set1080p
{
    // ---------- 1) 解析度：ChangeDisplaySettings ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DM_PELSWIDTH  = 0x00080000;
    const int DM_PELSHEIGHT = 0x00100000;

    static bool SetResolution(int w, int h)
    {
        DEVMODE dm = new DEVMODE();
        dm.dmDeviceName = new string('\0', 32);
        dm.dmFormName   = new string('\0', 32);
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        dm.dmPelsWidth  = w;
        dm.dmPelsHeight = h;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
        return ChangeDisplaySettings(ref dm, 0) == 0; // DISP_CHANGE_SUCCESSFUL
    }

    // ---------- 3) DPI：DisplayConfig 未公開 API ----------
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct POINTL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECTL { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering;
        public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate; public DISPLAYCONFIG_RATIONAL hSyncFreq; public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize; public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard; public uint scanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO { public POINTL PathSourceSize; public RECTL DesktopImageRegion; public RECTL DesktopImageClip; }

    [StructLayout(LayoutKind.Explicit)]
    public struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        [FieldOffset(0)] public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; public DISPLAYCONFIG_MODE_INFO_UNION modeInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
    { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public int minScaleRel; public int curScaleRel; public int maxScaleRel; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_DPI_SCALE_SET
    { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public int scaleRel; }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numMode, [Out] DISPLAYCONFIG_MODE_INFO[] modeArray, IntPtr currentTopologyId);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DPI_SCALE_GET packet);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DPI_SCALE_SET packet);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    const int  DPI_GET = -3;
    const int  DPI_SET = -4;
    // Windows 支援的縮放百分比清單（相對索引用）
    static readonly int[] DpiVals = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static int IndexOfDpi(int pct)
    {
        for (int i = 0; i < DpiVals.Length; i++) if (DpiVals[i] == pct) return i;
        return -1;
    }

    static bool SetDpiForAllSources(int targetPct)
    {
        uint numPath, numMode;
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPath, out numMode) != 0) return false;
        var paths = new DISPLAYCONFIG_PATH_INFO[numPath];
        var modes = new DISPLAYCONFIG_MODE_INFO[numMode];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPath, paths, ref numMode, modes, IntPtr.Zero) != 0) return false;

        int targetIdx = IndexOfDpi(targetPct);
        if (targetIdx < 0) return false;

        bool anyOk = false;
        for (int i = 0; i < numPath; i++)
        {
            LUID adapter = paths[i].sourceInfo.adapterId;
            uint srcId   = paths[i].sourceInfo.id;

            var get = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET();
            get.header.type = DPI_GET;
            get.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_GET));
            get.header.adapterId = adapter;
            get.header.id = srcId;
            if (DisplayConfigGetDeviceInfo(ref get) != 0) continue;

            int cur = get.curScaleRel;
            if (cur < get.minScaleRel) cur = get.minScaleRel;
            if (cur > get.maxScaleRel) cur = get.maxScaleRel;

            int recommendedIdx = Math.Abs(get.minScaleRel); // 建議值在清單中的位置
            int rel = targetIdx - recommendedIdx;           // 要設定的相對索引
            if (rel < get.minScaleRel) rel = get.minScaleRel;
            if (rel > get.maxScaleRel) rel = get.maxScaleRel;

            var set = new DISPLAYCONFIG_SOURCE_DPI_SCALE_SET();
            set.header.type = DPI_SET;
            set.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DPI_SCALE_SET));
            set.header.adapterId = adapter;
            set.header.id = srcId;
            set.scaleRel = rel;
            if (DisplayConfigSetDeviceInfo(ref set) == 0) anyOk = true;
        }
        return anyOk;
    }

    // ---------- 2) 投影模式 -> 同步 (Duplicate) ----------
    static void SetDuplicateProjection()
    {
        try
        {
            var psi = new ProcessStartInfo("DisplaySwitch.exe", "/clone");
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            var p = Process.Start(psi);
            if (p != null) { p.WaitForExit(5000); }
        }
        catch { /* 忽略；下方會嘗試套用其餘設定 */ }
        Thread.Sleep(1500); // 等待拓撲切換完成
    }

    // ---------- log：自動在 D: 建立資料夾並寫檔 ----------
    // 回傳實際寫入的完整路徑；全部失敗回傳 null。
    static string WriteLog(string content)
    {
        string fileName = "Set1080p_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";

        // 優先：D:\Set1080p_Log
        try
        {
            string dir = @"D:\Set1080p_Log";
            Directory.CreateDirectory(dir); // 不存在就建立，已存在則不動作
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, content, new UTF8Encoding(true)); // 含 BOM，記事本可正確顯示中文
            return path;
        }
        catch { /* D: 不存在 / 無寫入權限，改用備援 */ }

        // 備援：使用者暫存資料夾
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "Set1080p_Log");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, content, new UTF8Encoding(true));
            return path;
        }
        catch { return null; }
    }

    [STAThread]
    static void Main()
    {
        var log = new StringBuilder();
        log.AppendLine("========================================");
        log.AppendLine(" Set1080p 執行記錄");
        log.AppendLine(" 時間：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        log.AppendLine(" 電腦：" + Environment.MachineName + " / 使用者：" + Environment.UserName);
        log.AppendLine("========================================");
        log.AppendLine();

        // 前置：切換投影模式 -> 同步 (Duplicate)。此步無可靠回傳值，僅記錄不列入成敗。
        log.AppendLine("[前置] 投影模式 -> 同步 (Duplicate)");
        SetDuplicateProjection();
        log.AppendLine("       已送出 DisplaySwitch /clone 指令");
        log.AppendLine();

        bool resOk = false;
        bool dpiOk = false;

        // 自檢：最多兩次（第一次有失敗就自動重試一次；已成功的項目會略過）
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            log.AppendLine("[第 " + attempt + " 次嘗試]");

            if (!resOk)
            {
                resOk = SetResolution(1920, 1080);
                log.AppendLine("  解析度 1920 x 1080 ... " + (resOk ? "OK" : "失敗"));
            }
            else log.AppendLine("  解析度 1920 x 1080 ... 已成功（略過）");

            if (!dpiOk)
            {
                dpiOk = SetDpiForAllSources(125);
                log.AppendLine("  DPI 縮放 125%      ... " + (dpiOk ? "OK" : "失敗"));
            }
            else log.AppendLine("  DPI 縮放 125%      ... 已成功（略過）");

            log.AppendLine();

            if (resOk && dpiOk) break;

            if (attempt == 1)
            {
                log.AppendLine(">> 有項目未成功，1.5 秒後自動重試一次...");
                log.AppendLine();
                Thread.Sleep(1500);
            }
        }

        bool allOk = resOk && dpiOk;
        log.AppendLine("最終結果：" + (allOk ? "全部成功套用" : "重試後仍有項目失敗"));

        if (!allOk)
        {
            // 組出失敗項目清單
            var problems = new StringBuilder();
            if (!resOk) problems.AppendLine("• 解析度設定為 1920 x 1080 失敗");
            if (!dpiOk) problems.AppendLine("• DPI 縮放設定為 125% 失敗");

            // 寫入 log（自動於 D: 建立資料夾；D: 不可用時退回暫存資料夾）
            string logPath = WriteLog(log.ToString());

            // 跳出錯誤視窗
            const uint MB_OK = 0x0, MB_ICONERROR = 0x10;
            string msg =
                "螢幕解析度自動調整失敗\n\n" +
                problems.ToString() +
                "\n（已自動重試一次仍未成功。DPI 若無變化，請重新登入後再檢查。）\n";
            msg += (logPath != null)
                ? "\n記錄檔已儲存至：\n" + logPath
                : "\n（記錄檔寫入失敗，請確認 D: 磁碟是否存在或有寫入權限）";

            MessageBoxW(IntPtr.Zero, msg, "Set1080p", MB_OK | MB_ICONERROR);
        }
    }
}
