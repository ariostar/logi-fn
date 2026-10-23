// LogiFn - 极轻量罗技 HID++ 工具（K780 / Unifying 接收器 Fn 反转）
// 用法:
//   logifn.exe list                  列出罗技 HID 接口
//   logifn.exe probe                 找能应答 HID++ 的接口，并查 Fn 反转功能号
//   logifn.exe get   [idx]           读取 Fn 反转状态 (idx 默认 FF)
//   logifn.exe set N [idx]           写入 Fn 反转状态 (N=0/1, idx 默认 FF)
//   logifn.exe watch [秒]            常驻：立即写入并周期性重写(默认 60 秒)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class LogiFn
{
    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1;
    const uint FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const int DIGCF_PRESENT = 0x02;
    const int DIGCF_DEVICEINTERFACE = 0x10;
    const int ERROR_IO_PENDING = 997;
    const byte SWID = 0x0A;
    static int READ_TIMEOUT = 500;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct OVERLAPPED { public IntPtr Internal; public IntPtr InternalHigh; public int Offset; public int OffsetHigh; public IntPtr hEvent; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS c);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr d, IntPtr i, ref Guid g, int n, ref SP_DEVICE_INTERFACE_DATA s);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr d, ref SP_DEVICE_INTERFACE_DATA s, IntPtr detail, int size, out int req, IntPtr i);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr d);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sec, uint disp, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, IntPtr b, int n, out int w, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, IntPtr b, int n, out int r, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEvent(IntPtr sec, bool manualReset, bool initState, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ResetEvent(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, out int transferred, bool wait);
    [DllImport("kernel32.dll")] static extern bool CancelIo(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    class Dev
    {
        public string Path; public ushort Vid, Pid, Usage, UsagePage;
        public int InLen, OutLen; public IntPtr Handle; public IntPtr Event;
    }

    static List<Dev> Enumerate()
    {
        var list = new List<Dev>();
        Guid hid; HidD_GetHidGuid(out hid);
        IntPtr devs = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devs == (IntPtr)(-1)) return list;
        var did = new SP_DEVICE_INTERFACE_DATA();
        did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
        for (int i = 0; ; i++)
        {
            if (!SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref hid, i, ref did)) break;
            int req;
            SetupDiGetDeviceInterfaceDetail(devs, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
            if (req <= 0) continue;
            IntPtr buf = Marshal.AllocHGlobal(req);
            try
            {
                Marshal.WriteInt32(buf, (IntPtr.Size == 8) ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(devs, ref did, buf, req, out req, IntPtr.Zero)) continue;
                string path = Marshal.PtrToStringUni((IntPtr)(buf.ToInt64() + 4));
                if (path == null) continue;
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                      IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == (IntPtr)(-1)) continue;
                var at = new HIDD_ATTRIBUTES();
                at.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (!HidD_GetAttributes(h, ref at)) { CloseHandle(h); continue; }
                var caps = new HIDP_CAPS();
                caps.Reserved = new ushort[17];
                IntPtr pp;
                if (HidD_GetPreparsedData(h, out pp)) { HidP_GetCaps(pp, ref caps); HidD_FreePreparsedData(pp); }
                list.Add(new Dev
                {
                    Path = path, Vid = at.VendorID, Pid = at.ProductID,
                    Usage = caps.Usage, UsagePage = caps.UsagePage,
                    InLen = caps.InputReportByteLength, OutLen = caps.OutputReportByteLength,
                    Handle = h, Event = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero)
                });
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        SetupDiDestroyDeviceInfoList(devs);
        return list;
    }

    static bool VERBOSE = false;

    static bool WriteRep(Dev d, byte[] data, int len)
    {
        IntPtr mem = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.Copy(data, 0, mem, len);
            ResetEvent(d.Event);
            var ov = new OVERLAPPED(); ov.hEvent = d.Event;
            int done;
            if (!WriteFile(d.Handle, mem, len, out done, ref ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING)
                {
                    if (VERBOSE) Console.WriteLine("    [write 失败 err={0}]", err);
                    return false;
                }
                if (WaitForSingleObject(d.Event, (uint)READ_TIMEOUT) != 0) { CancelIo(d.Handle); if (VERBOSE) Console.WriteLine("    [write 超时]"); return false; }
                if (!GetOverlappedResult(d.Handle, ref ov, out done, false)) { if (VERBOSE) Console.WriteLine("    [write 结果失败 err={0}]", Marshal.GetLastWin32Error()); return false; }
            }
            if (VERBOSE) Console.WriteLine("    [write ok {0} 字节]", done);
            return true;
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    static byte[] ReadRep(Dev d, int len)
    {
        IntPtr mem = Marshal.AllocHGlobal(len);
        try
        {
            ResetEvent(d.Event);
            var ov = new OVERLAPPED(); ov.hEvent = d.Event;
            int done;
            if (!ReadFile(d.Handle, mem, len, out done, ref ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING)
                {
                    if (VERBOSE) Console.WriteLine("    [read 失败 err={0}]", err);
                    return null;
                }
                if (WaitForSingleObject(d.Event, (uint)READ_TIMEOUT) != 0) { CancelIo(d.Handle); if (VERBOSE) Console.WriteLine("    [read 超时]"); return null; }
                if (!GetOverlappedResult(d.Handle, ref ov, out done, false)) { if (VERBOSE) Console.WriteLine("    [read 结果失败 err={0}]", Marshal.GetLastWin32Error()); return null; }
            }
            if (done <= 0) return null;
            byte[] r = new byte[done];
            Marshal.Copy(mem, r, 0, done);
            if (VERBOSE) Console.WriteLine("    [read ok {0} 字节: {1}]", done, Hex(r));
            return r;
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    static byte[] Xfer(Dev d, byte devIndex, byte featIdx, byte funcSwId, byte[] parms)
    {
        bool lng = d.OutLen >= 20;
        int wlen = lng ? 20 : 7;
        if (d.OutLen < wlen) wlen = d.OutLen;
        byte[] w = new byte[wlen];
        w[0] = (byte)(lng ? 0x11 : 0x10);
        w[1] = devIndex; w[2] = featIdx; w[3] = funcSwId;
        if (parms != null)
            for (int i = 0; i < parms.Length && 4 + i < wlen; i++) w[4 + i] = parms[i];
        if (!WriteRep(d, w, wlen)) return null;
        int rlen = d.InLen > 0 ? d.InLen : wlen;
        for (int t = 0; t < 8; t++)
        {
            byte[] r = ReadRep(d, rlen);
            if (r == null) return null;
            if (r.Length < 4) continue;
            if (r[0] != 0x10 && r[0] != 0x11) continue;
            if (r[1] != devIndex) continue;
            if (r[2] == 0xFF) return r;                                   // HID++ 错误应答
            if (r[2] == featIdx && (r[3] & 0x0F) == (funcSwId & 0x0F)) return r;
        }
        return null;
    }

    static int GetFeature(Dev d, byte devIndex, ushort featureId, out byte[] raw)
    {
        raw = Xfer(d, devIndex, 0x00, (byte)(0x00 | SWID), new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF), 0 });
        if (raw == null || raw.Length < 5) return -1;
        return raw[4];
    }

    static string Hex(byte[] b)
    {
        if (b == null) return "(无应答)";
        var sb = new StringBuilder();
        for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2"));
        return sb.ToString();
    }

    static Dev Pick(Dev wantLong)
    {
        Dev fb = null;
        foreach (var d in Enumerate())
        {
            if (d.Vid != 0x046D || d.UsagePage != 0xFF00) continue;
            if (d.Usage == 0x0002 && d.OutLen >= 20) return d;
            if (d.Usage == 0x0001 && d.OutLen >= 7 && fb == null) fb = d;
            if (wantLong != null && wantLong.Path == d.Path) return d;
        }
        return fb;
    }

    static void Drain(List<Dev> cols)
    {
        foreach (var c in cols)
            while (true)
            {
                byte[] r = ReadRep(c, c.InLen);
                if (r == null) break;
                Console.WriteLine("   [清空] usage=0x{0:X4} 残留: {1}", c.Usage, Hex(r));
            }
    }

    static void CmdMatrix3(byte devIndex)
    {
        READ_TIMEOUT = 200;
        var cols = new List<Dev>();
        foreach (var d in Enumerate())
            if (d.Vid == 0x046D && d.UsagePage == 0xFF00) cols.Add(d);

        // getFeature(0x0001) 载荷
        byte[] payload = new byte[] { devIndex, 0x00, 0x0A, 0x00, 0x01, 0x00, 0x00 };

        foreach (var w in cols)
        {
            for (int rid = 0x10; rid <= 0x14; rid++)
            {
                int len = w.OutLen;
                if (len < 7) continue;
                byte[] buf = new byte[len];
                buf[0] = (byte)rid;
                for (int i = 0; i < payload.Length && 1 + i < len; i++) buf[1 + i] = payload[i];
                Console.WriteLine("== 集合 usage=0x{0:X4} rid=0x{1:X2} len={2}", w.Usage, rid, len);
                Drain(cols);
                if (!WriteRep(w, buf, len)) { Console.WriteLine("   写入失败"); continue; }
                for (int round = 0; round < 3; round++)
                    foreach (var r in cols)
                    {
                        byte[] resp = ReadRep(r, r.InLen);
                        if (resp != null) Console.WriteLine("   <- usage=0x{0:X4}: {1}", r.Usage, Hex(resp));
                    }
            }
        }
    }

    static void CmdMatrix2(byte devIndex)
    {
        READ_TIMEOUT = 250;
        var cols = new List<Dev>();
        foreach (var d in Enumerate())
            if (d.Vid == 0x046D && d.UsagePage == 0xFF00) cols.Add(d);

        // getFeature(0x0001) 的两种写法
        var variants = new List<KeyValuePair<string, byte[]>>();
        variants.Add(new KeyValuePair<string, byte[]>("短报文 7B", new byte[] { 0x10, devIndex, 0x00, 0x0A, 0x00, 0x01, 0x00 }));
        byte[] lng = new byte[20];
        lng[0] = 0x11; lng[1] = devIndex; lng[2] = 0x00; lng[3] = 0x0A; lng[4] = 0x00; lng[5] = 0x01;
        variants.Add(new KeyValuePair<string, byte[]>("长报文 20B", lng));

        foreach (var v in variants)
        {
            foreach (var w in cols)
            {
                if (v.Value.Length > w.OutLen) continue;
                Console.WriteLine("== 写入 usage=0x{0:X4} ({1}) : {2}", w.Usage, v.Key, Hex(v.Value));
                Drain(cols);
                if (!WriteRep(w, v.Value, v.Value.Length)) { Console.WriteLine("   写入失败"); continue; }
                for (int round = 0; round < 6; round++)
                    foreach (var r in cols)
                    {
                        byte[] resp = ReadRep(r, r.InLen);
                        if (resp != null)
                            Console.WriteLine("   <- 读到(第{0}轮) usage=0x{1:X4}: {2}", round, r.Usage, Hex(resp));
                    }
            }
        }
    }

    static byte[] ParseHex(string s)
    {
        s = s.Replace(" ", "");
        byte[] b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    static void CmdTry(byte devIndex)
    {
        READ_TIMEOUT = 250;
        var cols = new List<Dev>();
        foreach (var d in Enumerate())
            if (d.Vid == 0x046D && d.UsagePage == 0xFF00) cols.Add(d);
        Dev colShort = null, colLong = null;
        foreach (var c in cols) { if (c.Usage == 0x0001) colShort = c; if (c.Usage == 0x0002) colLong = c; }

        string[][] reqs = new string[][] {
            new string[]{"① 2.0 getFeature(0x0001) devIndex=FF", "10FF000A000100"},
            new string[]{"② 2.0 getFeature(0x0000) devIndex=FF", "10FF000A000000"},
            new string[]{"③ 2.0 getFeature(0x40A2) devIndex=FF", "10FF000A40A200"},
            new string[]{"④ 2.0 getFeature(0x0001) devIndex=00", "1000000A000100"},
            new string[]{"⑤ 2.0 getFeature(0x0001) devIndex=01", "1001000A000100"},
            new string[]{"⑥ 2.0 getFeature(0x0001) devIndex=02", "1002000A000100"},
            new string[]{"⑦ 1.0 读寄存器 subID=0x81 addr=0x00",     "10FF81000000"},
            new string[]{"⑧ 1.0 读寄存器 subID=0x81 addr=0x02",     "10FF81020000"},
            new string[]{"⑨ 1.0 getProtocolVersion subID=0x11",     "10FF11000000"},
            new string[]{"⑩ 1.0 subID=0x80 addr=0x00 val=00",       "10FF80000000"},
        };

        foreach (var rq in reqs)
        {
            byte[] req = ParseHex(rq[1]);
            bool shortReq = req.Length <= 7;
            Dev w = shortReq ? colShort : colLong;
            if (w == null) continue;
            if (shortReq && req.Length != w.OutLen) { byte[] t = new byte[w.OutLen]; Array.Copy(req, t, req.Length); req = t; }
            Console.WriteLine("== {0}  写{1} -> {2}", rq[0], shortReq ? "短" : "长", Hex(req));
            Drain(cols);
            if (!WriteRep(w, req, req.Length)) { Console.WriteLine("   写入失败"); continue; }
            for (int round = 0; round < 5; round++)
                foreach (var r in cols)
                {
                    byte[] resp = ReadRep(r, r.InLen);
                    if (resp != null) Console.WriteLine("   <- usage=0x{0:X4}: {1}", r.Usage, Hex(resp));
                }
        }
    }

    static void CmdMatrix(byte devIndex)
    {
        var cols = new List<Dev>();
        foreach (var d in Enumerate())
            if (d.Vid == 0x046D && d.UsagePage == 0xFF00) cols.Add(d);

        foreach (var w in cols)
        {
            foreach (int rid in new int[] { 0x10, 0x11 })
            {
                if (rid == 0x11 && w.OutLen < 20) continue;
                int wlen = w.OutLen;
                if (rid == 0x10 && wlen > 7) wlen = 7;
                if (rid == 0x11 && wlen > 20) wlen = 20;
                byte[] buf = new byte[wlen];
                buf[0] = (byte)rid; buf[1] = devIndex; buf[2] = 0x00; buf[3] = 0x0A;
                if (wlen > 4) buf[4] = 0x00;
                if (wlen > 5) buf[5] = 0x01;
                Console.WriteLine("写入: usage=0x{0:X4} rid=0x{1:X2} len={2}  -> {3}", w.Usage, rid, wlen, Hex(buf));
                if (!WriteRep(w, buf, wlen)) { Console.WriteLine("   写入失败"); continue; }
                foreach (var r in cols)
                {
                    byte[] resp = ReadRep(r, r.InLen);
                    Console.WriteLine("   读 usage=0x{0:X4} (in={1}) -> {2}", r.Usage, r.InLen, Hex(resp));
                }
            }
        }
    }

    static string Ascii(byte[] b)
    {
        if (b == null) return "(无应答)";
        var sb = new StringBuilder();
        for (int i = 4; i < b.Length; i++) sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '.');
        return sb.ToString();
    }

    static void CmdDev(byte devIndex)
    {
        var d = Pick(null);
        if (d == null) { Console.WriteLine("没找到罗技 HID++ 接口"); return; }
        Console.WriteLine("接口 usage=0x{0:X4} in={1} out={2}   devIndex=0x{2:X2}", d.Usage, d.InLen, devIndex);
        byte[] raw;
        int iroot = GetFeature(d, devIndex, 0x0001, out raw);
        Console.WriteLine("  getFeature(0x0001) = {0}   raw={1}", iroot, Hex(raw));
        if (iroot <= 0) return;

        ushort[] fids = new ushort[] { 0x0000, 0x0003, 0x0005, 0x1B04, 0x40A0, 0x40A2, 0x40A3, 0x1814, 0x1815, 0x1D4B, 0x4220, 0x4521, 0x4531 };
        int iName = -1;
        foreach (var fid in fids)
        {
            int fi = GetFeature(d, devIndex, fid, out raw);
            Console.WriteLine("  getFeature(0x{0:X4}) = {1}", fid, fi > 0 ? "0x" + fi.ToString("X2") : "(无)");
            if (fid == 0x0005 && fi > 0) iName = fi;
        }
        if (iName > 0)
        {
            byte[] r = Xfer(d, devIndex, (byte)iName, (byte)((1 << 4) | SWID), new byte[] { 0, 0, 0 });
            Console.WriteLine("  设备名: {0}   (raw={1})", Ascii(r), Hex(r));
        }
    }

    static byte FindSlot(Dev d)
    {
        for (byte ix = 1; ix <= 6; ix++)
        {
            byte[] raw;
            int i1 = GetFeature(d, ix, 0x0001, out raw);
            if (i1 <= 0) continue;
            int iname = GetFeature(d, ix, 0x0005, out raw);
            if (iname <= 0) continue;
            byte[] r = Xfer(d, ix, (byte)iname, (byte)((1 << 4) | SWID), new byte[] { 0, 0, 0 });
            string name = Ascii(r);
            Console.WriteLine("  槽 0x{0:X2}: {1}", ix, name);
            if (name.Contains("K780")) return ix;
        }
        return 0;
    }

    static byte ResolveSlot(Dev d, byte forced)
    {
        if (forced != 0) return forced;
        byte ix = FindSlot(d);
        if (ix == 0) Console.WriteLine("  (没找到 K780，回退到槽 1)");
        return ix == 0 ? (byte)1 : ix;
    }

    static void CmdFn(byte forced)
    {
        var d = Pick(null);
        if (d == null) { Console.WriteLine("没找到罗技 HID++ 接口"); return; }
        byte ix = ResolveSlot(d, forced);
        byte[] raw;
        int fi = GetFeature(d, ix, 0x40A2, out raw);
        Console.WriteLine("槽 0x{0:X2} 功能 0x40A2 -> featureIndex 0x{1:X2}", ix, fi);
        if (fi <= 0) { Console.WriteLine("  该设备没有 Fn 反转功能"); return; }
        byte[] r = Xfer(d, ix, (byte)fi, (byte)((0 << 4) | SWID), new byte[] { 0, 0, 0 });
        Console.WriteLine("  fn0 (get): {0}", Hex(r));
        if (r != null && r.Length >= 6)
            Console.WriteLine("  当前状态 state={0} ({1})   出厂默认 default={2} ({3})",
                r[4], r[4] == 0 ? "F 键为主" : "媒体键为主",
                r[5], r[5] == 0 ? "F 键为主" : "媒体键为主");
    }

    static void CmdFSet(byte forced, int value, int fnTarget)
    {
        var d = Pick(null);
        if (d == null) { Console.WriteLine("没找到罗技 HID++ 接口"); return; }
        byte ix = ResolveSlot(d, forced);
        byte[] raw;
        int fi = GetFeature(d, ix, 0x40A2, out raw);
        Console.WriteLine("槽 0x{0:X2} 功能 0x40A2 -> featureIndex 0x{1:X2}", ix, fi);
        if (fi <= 0) { Console.WriteLine("  该设备没有 Fn 反转功能"); return; }
        byte[] before = Xfer(d, ix, (byte)fi, (byte)((0 << 4) | SWID), new byte[] { 0, 0, 0 });
        Console.WriteLine("  写入前 fn0: {0}", Hex(before));
        byte[] r = Xfer(d, ix, (byte)fi, (byte)((fnTarget << 4) | SWID), new byte[] { (byte)value, 0, 0 });
        Console.WriteLine("  fn{0} <- {1} : {2}", fnTarget, value, Hex(r));
        byte[] after = Xfer(d, ix, (byte)fi, (byte)((0 << 4) | SWID), new byte[] { 0, 0, 0 });
        Console.WriteLine("  写入后 fn0: {0}", Hex(after));
    }

    // 找到带 Fn 反转功能的设备槽（通常是 1）
    static int FindFnSlot(Dev d, out int featureIndex)
    {
        featureIndex = -1;
        for (byte ix = 1; ix <= 6; ix++)
        {
            byte[] raw;
            int fi = GetFeature(d, ix, 0x40A2, out raw);
            if (fi > 0) { featureIndex = fi; return ix; }
        }
        return 0;
    }

    // 应用 Fn 反转：0 = F 键为主；1 = 媒体键为主。返回 1=已改，0=无需改，-1=失败
    static int ApplyFn(int value, bool verbose)
    {
        // 设备空闲休眠时，第二轮起才会被唤醒，因此重试几次
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            var d = Pick(null);
            if (d == null)
            {
                if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 没有罗技 HID++ 接口", DateTime.Now);
                return -1;
            }
            int fi;
            int ix = FindFnSlot(d, out fi);
            if (ix == 0)
            {
                if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 未找到 Fn 反转功能（第 {1} 次，唤醒设备中…）", DateTime.Now, attempt);
                Thread.Sleep(300);
                continue;
            }
            byte[] cur = Xfer(d, (byte)ix, (byte)fi, (byte)((0 << 4) | SWID), new byte[] { 0, 0, 0 });
            if (cur == null || cur.Length < 6)
            {
                if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 读取状态失败（第 {1} 次）", DateTime.Now, attempt);
                Thread.Sleep(300);
                continue;
            }
            if (cur[4] == value)
            {
                if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 槽{1} 状态已是 {2}（{3}），无需写入", DateTime.Now, ix, value, value == 0 ? "F 键为主" : "媒体键为主");
                return 0;
            }
            if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 槽{1} 当前 {2}（{3}） -> 写入 {4}（{5}）", DateTime.Now, ix, cur[4], cur[4] == 0 ? "F 键为主" : "媒体键为主", value, value == 0 ? "F 键为主" : "媒体键为主");
            byte[] r = Xfer(d, (byte)ix, (byte)fi, (byte)((1 << 4) | SWID), new byte[] { (byte)value, 0, 0 });
            if (verbose) Console.WriteLine("   应答: {0}", Hex(r));
            if (r != null) return 1;
            Thread.Sleep(300);
        }
        if (verbose) Console.WriteLine("[{0:HH:mm:ss}] 应用失败", DateTime.Now);
        return -1;
    }

    static void CmdHelp()
    {
        Console.WriteLine(@"logifn - 极轻量 HID++ Fn 反转工具（罗技 K780 等支持 0x40A2 的键盘）

用法: logifn.exe <命令> [参数]

常用命令:
  apply              立即把 F 排切到 F1-F12（开机时执行一次即可）
  fn                 只读当前状态（state=0 F 键为主 / state=1 媒体键为主）
  watch [秒]         常驻守护：长时间空闲后又开始输入时自动校正
                     [秒] 空闲判定阈值，默认 30
                     -log <路径> 指定日志（默认 %LOCALAPPDATA%\logifn\watch.log）
  help               显示本帮助（无参数时默认执行）

诊断命令（排查其它型号时用）:
  list               列出所有 HID 接口
  probe [槽位]       探测哪个接口能应答 HID++（槽位默认 FF）
  dev [槽位]         打印设备的 HID++ feature 表（默认依次试 01-06、FF）
  fset <state> [fn] [槽位]
                     直接写 0x40A2 fn1（state: 0 关 / 1 开；槽位默认 00）
  try / matrix / matrix2 / matrix3
                     早期取证用的报文矩阵测试，日常不需要
  get / set          早期版本走 0xFF 的旧路径，K780 上无效，仅作对照

退出码: 0 成功；1 失败或未找到设备。
");
    }

    static void CmdList()
    {
        foreach (var d in Enumerate())
            Console.WriteLine("usagePage=0x{0:X4} usage=0x{1:X4} in={2,2} out={3,2}  vid={4:X4} pid={5:X4}  {6}",
                d.UsagePage, d.Usage, d.InLen, d.OutLen, d.Vid, d.Pid, d.Path);
    }

    static void CmdProbe(byte devIndex)
    {
        // 先长报文集合(usage 0x0002)，再短报文集合(usage 0x0001)
        var all = Enumerate();
        var order = new List<Dev>();
        foreach (var d in all) if (d.Vid == 0x046D && d.UsagePage == 0xFF00 && d.Usage == 0x0002) order.Add(d);
        foreach (var d in all) if (d.Vid == 0x046D && d.UsagePage == 0xFF00 && d.Usage == 0x0001) order.Add(d);

        foreach (var d in order)
        {
            Console.WriteLine("候选: usage=0x{0:X4} in={1} out={2}", d.Usage, d.InLen, d.OutLen);
            byte[] raw;
            int i1 = GetFeature(d, devIndex, 0x0001, out raw);
            Console.WriteLine("   root.getFeature(0x0001) = {0}  raw={1}", i1, Hex(raw));
            if (i1 <= 0) { Console.WriteLine("   -> 不响应，跳过"); continue; }
            foreach (ushort fid in new ushort[] { 0x40A0, 0x40A2, 0x40A3, 0x40A4 })
            {
                int fi = GetFeature(d, devIndex, fid, out raw);
                Console.WriteLine("   getFeature(0x{0:X4}) = {1}  raw={2}", fid, fi, Hex(raw));
            }
            Console.WriteLine("==> 使用该接口");
            return;
        }
    }

    static int CmdGet(byte devIndex)
    {
        var d = Pick(null);
        if (d == null) { Console.WriteLine("没找到罗技 HID++ 接口"); return 1; }
        Console.WriteLine("接口: usage=0x{0:X4} in={1} out={2}", d.Usage, d.InLen, d.OutLen);
        foreach (ushort fid in new ushort[] { 0x40A2, 0x40A0, 0x40A3, 0x40A4 })
        {
            byte[] raw;
            int fi = GetFeature(d, devIndex, fid, out raw);
            if (fi <= 0) continue;
            Console.WriteLine("功能 0x{0:X4} -> featureIndex 0x{1:X2}", fid, fi);
            for (int fn = 0; fn <= 2; fn++)
            {
                byte[] r = Xfer(d, devIndex, (byte)fi, (byte)((fn << 4) | SWID), new byte[] { 0, 0, 0 });
                Console.WriteLine("  fn{0}: {1}", fn, Hex(r));
            }
            return 0;
        }
        Console.WriteLine("未找到 Fn 反转功能");
        return 1;
    }

    static int CmdSet(byte devIndex, int value)
    {
        var d = Pick(null);
        if (d == null) { Console.WriteLine("没找到罗技 HID++ 接口"); return 1; }
        foreach (ushort fid in new ushort[] { 0x40A2, 0x40A0, 0x40A3, 0x40A4 })
        {
            byte[] raw;
            int fi = GetFeature(d, devIndex, fid, out raw);
            if (fi <= 0) continue;
            Console.WriteLine("功能 0x{0:X4} -> featureIndex 0x{1:X2}，写入 fn1 = {2}", fid, fi, value);
            byte[] r = Xfer(d, devIndex, (byte)fi, (byte)((1 << 4) | SWID), new byte[] { (byte)value, 0, 0 });
            Console.WriteLine("  set 应答: {0}", Hex(r));
            byte[] r2 = Xfer(d, devIndex, (byte)fi, (byte)((0 << 4) | SWID), new byte[] { 0, 0, 0 });
            Console.WriteLine("  回读 fn0: {0}", Hex(r2));
            return 0;
        }
        Console.WriteLine("未找到 Fn 反转功能");
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    static uint LastInputTick()
    {
        var li = new LASTINPUTINFO();
        li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref li)) return 0;
        return li.dwTime;
    }

    class Tee : System.IO.TextWriter
    {
        System.IO.TextWriter a, b;
        public Tee(System.IO.TextWriter a, System.IO.TextWriter b) { this.a = a; this.b = b; }
        public override Encoding Encoding { get { return a.Encoding; } }
        public override void Write(char c) { a.Write(c); b.Write(c); }
        public override void Write(string s) { a.Write(s); b.Write(s); }
        public override void WriteLine(string s) { a.WriteLine(s); b.WriteLine(s); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }

    static int Main(string[] argv)
    {
        var args = new List<string>(argv);
        string mode = args.Count > 0 ? args[0].ToLower() : "help";
        byte devIndex = 0xFF;
        if (mode == "get" && args.Count > 1) devIndex = Convert.ToByte(args[1], 16);
        if (mode == "set" && args.Count > 2) devIndex = Convert.ToByte(args[2], 16);

        if (mode != "watch" && mode != "help")
        {
            int wdm = (mode == "probe" || mode == "get" || mode == "set") ? 15000 : 300000;
            var t = new Thread(delegate () { Thread.Sleep(wdm); Console.WriteLine("[watchdog] 超时退出"); Environment.Exit(3); });
            t.IsBackground = true; t.Start();
        }
        VERBOSE = (mode == "probe") || args.Contains("--v");

        switch (mode)
        {
            case "help": CmdHelp(); return 0;
            case "list": CmdList(); return 0;
            case "probe": CmdProbe(devIndex); return 0;
            case "try": CmdTry(devIndex); return 0;
            case "matrix": CmdMatrix(devIndex); return 0;
            case "matrix3": CmdMatrix3(devIndex); return 0;
            case "fn": CmdFn(devIndex == 0xFF ? (byte)0 : devIndex); return 0;
            case "fset":
                {
                    int v = args.Count > 1 ? int.Parse(args[1]) : 1;
                    int fnT = args.Count > 2 ? int.Parse(args[2]) : 1;
                    byte slot = args.Count > 3 ? Convert.ToByte(args[3], 16) : (byte)0;
                    CmdFSet(slot, v, fnT);
                    return 0;
                }
            case "matrix2": CmdMatrix2(devIndex); return 0;
            case "get": return CmdGet(devIndex);
            case "dev":
                {
                    byte[] idxs = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xFF };
                    if (args.Count > 1) idxs = new byte[] { Convert.ToByte(args[1], 16) };
                    foreach (byte ix in idxs) { Console.WriteLine(); CmdDev(ix); }
                    return 0;
                }
            case "set":
                { int v = args.Count > 1 ? int.Parse(args[1]) : 1; return CmdSet(devIndex, v); }
            case "apply": ApplyFn(0, true); return 0;
            case "watch":
                {
                    int gapSec = args.Count > 1 && args[1] != "-log" ? int.Parse(args[1]) : 30;
                    string logPath = null;
                    int li = args.IndexOf("-log");
                    if (li >= 0 && li + 1 < args.Count) logPath = args[li + 1];
                    if (logPath == null)
                        logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "logifn", "watch.log");
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                        var fi0 = new System.IO.FileInfo(logPath);
                        bool trunc = fi0.Exists && fi0.Length > 100 * 1024;
                        var fs = new System.IO.FileStream(logPath, trunc ? System.IO.FileMode.Create : System.IO.FileMode.Append,
                                                          System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                        Console.SetOut(new Tee(Console.Out, new System.IO.StreamWriter(fs) { AutoFlush = true }));
                    }
                    catch (Exception e) { Console.WriteLine("日志文件打不开: {0}", e.Message); }

                    Console.WriteLine("=== 启动 {0:yyyy-MM-dd HH:mm:ss}（空闲阈值 {1} 秒）===", DateTime.Now, gapSec);
                    Console.WriteLine("只在「长时间无输入后又开始输入」时检查一次；完全空闲时不发任何 HID 包。");
                    try { ApplyFn(0, true); } catch (Exception e) { Console.WriteLine("启动应用失败: {0}", e.Message); }

                    uint prev = LastInputTick();
                    long lastApplied = Environment.TickCount;
                    while (true)
                    {
                        Thread.Sleep(1000);
                        uint t = LastInputTick();
                        if (t == prev) continue;
                        uint gap = t - prev;
                        prev = t;
                        bool need = gap >= (uint)(gapSec * 1000) || (Environment.TickCount - lastApplied) > 300000;
                        if (!need) continue;
                        try { if (ApplyFn(0, true) >= 0) lastApplied = Environment.TickCount; }
                        catch (Exception e) { Console.WriteLine("错误: {0}", e.Message); }
                    }
                }
        }
        Console.WriteLine("未知模式: " + mode);
        return 2;
    }
}
