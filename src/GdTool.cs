// GdTool.cs — Geometry Dash save/memory editor.
// Портано с gd_currency.ps1 в одно exe. Компилится csc.exe из .NET Framework (C# 5.0).
// Формат сейва 2.2:  файл = XOR 0x0B ( base64url( gzip( plist ) ) )
// Статы: GS_value/<цифра>, значения строками.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("gd_currency")]
[assembly: AssemblyProduct("gd_currency — накрутка валюты Geometry Dash (save + память)")]
[assembly: AssemblyDescription("Парсер CCGameManager.dat (XOR+base64url+gzip+plist) и сканер памяти процесса. Учебный инструмент.")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace GdSave
{
    [StructLayout(LayoutKind.Sequential)]
    struct MBI
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, IntPtr len);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        public static int ReadInt32(IntPtr h, long addr)
        {
            byte[] b = new byte[4]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)addr, b, 4, out r)) return int.MinValue;
            return BitConverter.ToInt32(b, 0);
        }

        public static long ReadInt64(IntPtr h, long addr)
        {
            byte[] b = new byte[8]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)addr, b, 8, out r)) return long.MinValue;
            return BitConverter.ToInt64(b, 0);
        }

        // ---- чтение из процесса для разбора структуры статов ----
        public static long ReadPtr(IntPtr h, long a)
        {
            byte[] b = new byte[8]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)a, b, 8, out r)) return 0;
            return BitConverter.ToInt64(b, 0);
        }
        public static uint ReadU32(IntPtr h, long a)
        {
            byte[] b = new byte[4]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)a, b, 4, out r)) return 0;
            return BitConverter.ToUInt32(b, 0);
        }
        public static int ReadI32(IntPtr h, long a)
        {
            byte[] b = new byte[4]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)a, b, 4, out r)) return 0;
            return BitConverter.ToInt32(b, 0);
        }
        public static ulong ReadU64(IntPtr h, long a)
        {
            byte[] b = new byte[8]; IntPtr r;
            if (!ReadProcessMemory(h, (IntPtr)a, b, 8, out r)) return 0;
            return BitConverter.ToUInt64(b, 0);
        }
        public static bool WriteU32(IntPtr h, long a, uint v)
        {
            byte[] b = BitConverter.GetBytes(v); IntPtr w;
            return WriteProcessMemory(h, (IntPtr)a, b, 4, out w) && w.ToInt64() == 4;
        }

        public static bool IsWritable(IntPtr h, long addr, int size)
        {
            MBI mbi;
            if (VirtualQueryEx(h, (IntPtr)addr, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) return false;
            if (mbi.State != 0x1000) return false;
            if ((mbi.Protect & 0x100) != 0) return false;
            long b = mbi.BaseAddress.ToInt64();
            if (addr < b || addr + size > b + mbi.RegionSize.ToInt64()) return false;
            uint p = mbi.Protect & 0xFF;
            return p == 0x04 || p == 0x40 || p == 0x08 || p == 0x80;
        }

        public static long[] Scan(IntPtr h, byte[] needle, int maxResults, bool writableOnly)
        {
            return Scan(h, needle, null, maxResults, writableOnly, 1);
        }

        // wild[i] == true -> байт любой. Нужен для поиска [ID][указатель] без знания значения.
        public static long[] Scan(IntPtr h, byte[] needle, bool[] wild, int maxResults, bool writableOnly, int align)
        {
            List<long> hits = new List<long>();
            const int CHUNK = 1 << 20;
            byte[] buf = new byte[CHUNK];
            long addr = 0x10000;
            long limit = IntPtr.Size == 8 ? 0x7FFFFFFEFFFFL : 0x7FFFFFFFL;
            MBI mbi;
            bool anyWild = wild != null;

            while (addr < limit && hits.Count < maxResults)
            {
                if (VirtualQueryEx(h, (IntPtr)addr, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
                long baseAddr = mbi.BaseAddress.ToInt64();
                long size = mbi.RegionSize.ToInt64();
                if (size <= 0) { addr = baseAddr + 0x1000; continue; }

                uint prot = mbi.Protect;
                uint bp = prot & 0xFF;
                bool writable = bp == 0x04 || bp == 0x40 || bp == 0x08 || bp == 0x80;
                bool readable = mbi.State == 0x1000 && (prot & 0x100) == 0 && (bp == 0x02 || writable || bp == 0x20);
                if (writableOnly) readable = readable && writable;

                if (readable)
                {
                    long off = 0;
                    int skip = 0;
                    while (off < size && hits.Count < maxResults)
                    {
                        int chunk = (int)Math.Min((long)CHUNK, size - off);
                        if (chunk < needle.Length) break;
                        long cur = baseAddr + off;
                        IntPtr read;
                        if (ReadProcessMemory(h, (IntPtr)cur, buf, chunk, out read) && read.ToInt64() > 0)
                        {
                            int n = (int)read.ToInt64();
                            for (int i = skip; i + needle.Length <= n; i++)
                            {
                                bool ok = true;
                                for (int j = 0; j < needle.Length; j++)
                                {
                                    if (anyWild && wild[j]) continue;
                                    if (buf[i + j] != needle[j]) { ok = false; break; }
                                }
                                if (!ok) continue;
                                if (align > 1 && (cur + i) % align != 0) continue;
                                hits.Add(cur + i);
                                if (hits.Count >= maxResults) break;
                            }
                            skip = Math.Min(needle.Length - 1, n);
                        }
                        off += chunk - skip;
                        if (off <= 0) break;
                    }
                }
                addr = baseAddr + size;
            }
            return hits.ToArray();
        }
    }

    class GNode
    {
        public char Kind;                   // d (словарь) s i r t f k
        public string Tag = "d";            // d | dict — важно: у <d> и <dict> первый символ одинаковый
        public string Str;                  // значение скаляра
        public string Key;                  // значение узла <k>
        public string Raw;                  // исходный текст (для побайтового round-trip)
        public OrderedDictionary Dict;      // содержимое словаря
    }

    class SaveFile
    {
        public byte[] Blob;
        public byte[] Key;
        public string Xml;
        public string Comp;                 // gzip | zlib
        public bool B64;
        public string Alpha;                // url | std
    }

    class Parsed
    {
        public string Prefix;
        public GNode Root;
        public string Suffix;
    }

    static class Program
    {
        static readonly string[] B64Chars = BuildB64();
        static string[] BuildB64()
        {
            bool[] ok = new bool[256];
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=-_")
                ok[(byte)c] = true;
            string[] r = new string[256];
            for (int i = 0; i < 256; i++) r[i] = ok[i] ? "y" : "n";
            return r;
        }
        static bool IsB64(byte c) { return B64Chars[c] == "y"; }

        // ------------------------------------------------------------------
        static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte x in b) { if (sb.Length > 0) sb.Append(' '); sb.Append(x.ToString("X2")); }
            return sb.ToString();
        }

        static byte[] Xor(byte[] data, byte[] key)
        {
            byte[] outp = new byte[data.Length];
            if (key.Length == 1)
            {
                byte[] map = new byte[256];
                for (int i = 0; i < 256; i++) map[i] = (byte)(i ^ key[0]);
                for (int i = 0; i < data.Length; i++) outp[i] = map[data[i]];
                return outp;
            }
            for (int i = 0; i < data.Length; i++) outp[i] = (byte)(data[i] ^ key[i % key.Length]);
            return outp;
        }

        // период ключа подбирается по тому, что после XOR всё попадает в base64-алфавит
        static byte[] SolveKey(byte[] data, int maxLen)
        {
            for (int L = 1; L <= maxLen; L++)
            {
                byte[] key = new byte[L];
                bool good = true;
                for (int r = 0; r < L && good; r++)
                {
                    int found = -1;
                    for (int k = 0; k < 256; k++)
                    {
                        bool pass = true;
                        int samples = 0;
                        for (int i = r; i < data.Length && samples < 3; i += L, samples++)
                            if (!IsB64((byte)(data[i] ^ k))) { pass = false; break; }
                        if (pass)
                        {
                            if (found >= 0) { found = -2; break; }
                            found = k;
                        }
                    }
                    if (found < 0) { good = false; break; }
                    key[r] = (byte)found;
                }
                if (good) return key;
            }
            return null;
        }

        static byte[] Inflate(byte[] raw, out string comp)
        {
            comp = null;
            if (raw.Length < 8) return null;
            try
            {
                MemoryStream o;
                if (raw[0] == 0x1F && raw[1] == 0x8B)
                {
                    using (MemoryStream ms = new MemoryStream(raw))
                    using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
                    using (MemoryStream outp = new MemoryStream())
                    {
                        gz.CopyTo(outp);
                        o = new MemoryStream(outp.ToArray());
                    }
                    comp = "gzip";
                }
                else if (raw[0] == 0x78)
                {
                    byte[] body = new byte[raw.Length - 6];
                    Array.Copy(raw, 2, body, 0, body.Length);
                    using (MemoryStream ms = new MemoryStream(body))
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (MemoryStream outp = new MemoryStream())
                    {
                        ds.CopyTo(outp);
                        o = new MemoryStream(outp.ToArray());
                    }
                    comp = "zlib";
                }
                else return null;

                using (o) return o.ToArray();
            }
            catch { return null; }
        }

        static uint Adler32(byte[] d)
        {
            const uint MOD = 65521;
            uint a = 1, b = 0;
            int i = 0;
            while (i < d.Length)
            {
                int n = Math.Min(5552, d.Length - i);
                for (int j = 0; j < n; j++, i++) { a += d[i]; b += a; }
                a %= MOD; b %= MOD;
            }
            return (b << 16) | a;
        }

        static byte[] Deflate(byte[] data, string comp)
        {
            if (comp == "gzip")
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (GZipStream gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                        gz.Write(data, 0, data.Length);
                    return ms.ToArray();
                }
            }
            // zlib = 2 байта заголовка + СЫРОЙ deflate + 4 байта adler32 (DeflateStream даёт только середину)
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
                    ds.Write(data, 0, data.Length);
                byte[] raw = ms.ToArray();
                byte[] outp = new byte[raw.Length + 6];
                outp[0] = 0x78; outp[1] = 0x9C;
                Array.Copy(raw, 0, outp, 2, raw.Length);
                uint ad = Adler32(data);
                outp[outp.Length - 4] = (byte)(ad >> 24);
                outp[outp.Length - 3] = (byte)(ad >> 16);
                outp[outp.Length - 2] = (byte)(ad >> 8);
                outp[outp.Length - 1] = (byte)ad;
                return outp;
            }
        }

        // base64url в .NET есть только начиная с .NET 9, поэтому руками
        static byte[] FromB64(string s)
        {
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        static string ToB64(byte[] raw, string alpha)
        {
            string s = Convert.ToBase64String(raw);
            if (alpha == "url") s = s.Replace('+', '-').Replace('/', '_');
            return s;
        }

        static SaveFile TestPayload(byte[] pt)
        {
            if (pt.Length < 16) return null;
            int head = Math.Min(128, pt.Length);
            bool ok = true;
            for (int i = 0; i < head; i++)
            {
                byte c = pt[i];
                if (!IsB64(c) && c != 0x0A && c != 0x0D) { ok = false; break; }
            }
            if (ok && pt.Length % 4 == 0)
            {
                try
                {
                    string txt = Encoding.ASCII.GetString(pt);
                    string comp;
                    byte[] bytes = Inflate(FromB64(txt), out comp);
                    if (bytes != null)
                    {
                        string xml = Encoding.UTF8.GetString(bytes);
                        if (xml.TrimStart().StartsWith("<"))
                        {
                            bool url = txt.IndexOf('-') >= 0 || txt.IndexOf('_') >= 0;
                            return new SaveFile { Xml = xml, Comp = comp, B64 = true, Alpha = url ? "url" : "std" };
                        }
                    }
                }
                catch { }
            }
            string comp2;
            byte[] raw2 = Inflate(pt, out comp2);
            if (raw2 != null)
            {
                string xml = Encoding.UTF8.GetString(raw2);
                if (xml.TrimStart().StartsWith("<"))
                    return new SaveFile { Xml = xml, Comp = comp2, B64 = false, Alpha = "std" };
            }
            return null;
        }

        static SaveFile FindKey(byte[] blob)
        {
            int[] first = { 0x48, 0x65, 0x6E, 0x78 };   // 'H'(gzip b64) 'e'/'n'(zlib b64) 0x78 (сырой)
            foreach (int c in first)
            {
                byte k = (byte)(blob[0] ^ c);
                SaveFile r = TestPayload(Xor(blob, new byte[] { k }));
                if (r != null) { r.Key = new byte[] { k }; r.Blob = blob; return r; }
            }
            byte[] solved = SolveKey(blob, 64);
            if (solved != null)
            {
                SaveFile r = TestPayload(Xor(blob, solved));
                if (r != null) { r.Key = solved; r.Blob = blob; return r; }
            }
            return null;
        }

        static SaveFile ReadSave(string path)
        {
            byte[] blob = File.ReadAllBytes(path);
            SaveFile s = FindKey(blob);
            if (s == null) throw new Exception("формат не распознан (gzip/zlib/base64url + XOR не подошли). Смотри mem-scan.");
            s.Blob = blob;
            return s;
        }

        static byte[] EncodeSave(string xml, SaveFile s)
        {
            byte[] raw = Deflate(Encoding.UTF8.GetBytes(xml), s.Comp);
            byte[] payload = s.B64 ? Encoding.ASCII.GetBytes(ToB64(raw, s.Alpha)) : raw;
            return Xor(payload, s.Key);
        }

        // ------------------------------------------------------------------
        // plist: <plist><dict>...</dict></plist>, внутри <d>, самозакрывающиеся <d />, булевы <t />
        static bool At(string t, int p, string lit)
        {
            if (p < 0 || p + lit.Length > t.Length) return false;
            for (int i = 0; i < lit.Length; i++) if (t[p + i] != lit[i]) return false;
            return true;
        }

        static GNode ReadNode(string t, ref int pos)
        {
            int p0 = pos;
            if (At(t, p0, "<k>"))
            {
                int e = t.IndexOf("</k>", p0 + 3, StringComparison.Ordinal);
                if (e < 0) throw new Exception("битый <k> @" + p0);
                pos = e + 4;
                return new GNode { Kind = 'k', Key = t.Substring(p0 + 3, e - p0 - 3), Raw = "" };
            }
            if (At(t, p0, "<d />")) { pos = p0 + 5; return new GNode { Kind = 'd', Dict = new OrderedDictionary(), Raw = "<d />" }; }
            if (At(t, p0, "<t />")) { pos = p0 + 5; return new GNode { Kind = 't', Str = "1", Raw = "<t />" }; }
            if (At(t, p0, "<f />")) { pos = p0 + 5; return new GNode { Kind = 'f', Str = "0", Raw = "<f />" }; }
            if (At(t, p0, "<t></t>")) { pos = p0 + 7; return new GNode { Kind = 'f', Str = "0", Raw = "<t></t>" }; }
            if (At(t, p0, "<f></f>")) { pos = p0 + 7; return new GNode { Kind = 'f', Str = "0", Raw = "<f></f>" }; }

            string[] tags = { "s", "i", "r", "t", "f" };
            foreach (string tag in tags)
            {
                string open = "<" + tag + ">";
                if (!At(t, p0, open)) continue;
                string close = "</" + tag + ">";
                int e = t.IndexOf(close, p0 + 3, StringComparison.Ordinal);
                if (e < 0) throw new Exception("битый <" + tag + "> @" + p0);
                string v = t.Substring(p0 + 3, e - p0 - 3);
                pos = e + 4;
                char k = tag[0];
                if (k == 't') v = "1";
                if (k == 'f') v = "0";
                return new GNode { Kind = k, Str = v, Raw = t.Substring(p0, pos - p0) };
            }

            string[] dicts = { "d", "dict" };
            foreach (string dt in dicts)
            {
                string open = "<" + dt + ">";
                if (!At(t, p0, open)) continue;
                string close = "</" + dt + ">";
                pos = p0 + open.Length;
                OrderedDictionary d = new OrderedDictionary();
                while (!At(t, pos, close))
                {
                    if (pos >= t.Length) throw new Exception("не закрыт <" + dt + "> @" + p0);
                    GNode k = ReadNode(t, ref pos);
                    GNode v = ReadNode(t, ref pos);
                    d[k.Key] = v;
                }
                pos += close.Length;
                return new GNode { Kind = 'd', Tag = dt, Dict = d, Raw = "" };
            }
            string frag = t.Substring(p0, Math.Min(30, t.Length - p0));
            throw new Exception("неизвестный тег @" + p0 + " : '" + frag + "'");
        }

        static Parsed ParseSave(string xml)
        {
            int start = xml.IndexOf("<dict>", StringComparison.Ordinal);
            if (start < 0) start = xml.IndexOf("<d>", StringComparison.Ordinal);
            if (start < 0) throw new Exception("нет корневого <dict>/<d>");
            int pos = start;
            GNode root = ReadNode(xml, ref pos);
            if (xml.IndexOf("<k>", pos, StringComparison.Ordinal) >= 0)
                throw new Exception("документ распарсен не до конца (позиция " + pos + " из " + xml.Length + ")");
            return new Parsed { Prefix = xml.Substring(0, start), Root = root, Suffix = xml.Substring(pos) };
        }

        static void WriteNode(GNode n, StringBuilder sb)
        {
            if (IsDict(n))
            {
                string tag = string.IsNullOrEmpty(n.Tag) ? "d" : n.Tag;
                if (n.Dict.Count == 0) { sb.Append("<" + tag + " />"); return; }
                sb.Append('<').Append(tag).Append('>');
                foreach (object key in n.Dict.Keys)
                {
                    sb.Append("<k>").Append((string)key).Append("</k>");
                    WriteNode((GNode)n.Dict[key], sb);
                }
                sb.Append("</").Append(tag).Append('>');
                return;
            }
            if (n.Raw != null && n.Raw.Length > 0) { sb.Append(n.Raw); return; }
            if (n.Kind == 't') { sb.Append("<t />"); return; }
            if (n.Kind == 'f') { sb.Append("<f />"); return; }
            sb.Append('<').Append(n.Kind).Append('>').Append(n.Str).Append("</").Append(n.Kind).Append('>');
        }

        static string Serialize(Parsed p)
        {
            StringBuilder sb = new StringBuilder(p.Xml_Estimate());
            sb.Append(p.Prefix);
            WriteNode(p.Root, sb);
            sb.Append(p.Suffix);
            return sb.ToString();
        }

        static bool IsDict(GNode n) { return n.Kind == 'd'; }

        static GNode GetNode(GNode root, string path)
        {
            GNode n = root;
            if (string.IsNullOrEmpty(path)) return n;
            foreach (string seg in path.Split('/'))
            {
                if (!IsDict(n)) throw new Exception("путь '" + path + "': '" + seg + "' уже не словарь");
                if (!n.Dict.Contains(seg)) throw new Exception("нет ключа '" + seg + "' в '" + path + "'");
                n = (GNode)n.Dict[seg];
            }
            return n;
        }

        static void WriteFlat(GNode node, string prefix, OrderedDictionary acc)
        {
            if (IsDict(node))
            {
                foreach (object key in node.Dict.Keys)
                {
                    string p = string.IsNullOrEmpty(prefix) ? (string)key : prefix + "/" + (string)key;
                    WriteFlat((GNode)node.Dict[key], p, acc);
                }
            }
            else if (node.Kind == 't') acc[prefix] = "bool:true";
            else if (node.Kind == 'f') acc[prefix] = "bool:false";
            else acc[prefix] = node.Kind + ":" + node.Str;
        }

        // ------------------------------------------------------------------
        static string SaveDir()
        {
            string[] dirs = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeometryDash"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeometryDash")
            };
            string[] names = { "CCGameManager.dat", "CCGameManager2.dat" };
            foreach (string d in dirs)
                foreach (string f in names)
                {
                    string p = Path.Combine(d, f);
                    if (File.Exists(p)) return p;
                }
            return null;
        }

        static string ResolveFile(string opt)
        {
            if (!string.IsNullOrEmpty(opt))
            {
                if (!File.Exists(opt)) throw new Exception("нет файла: " + opt);
                return Path.GetFullPath(opt);
            }
            string p = SaveDir();
            if (p == null)
                throw new Exception("не нашёл CCGameManager.dat. Укажи -f путь (обычно %LOCALAPPDATA%\\GeometryDash\\)");
            return p;
        }

        static void AssertGameClosed(string target)
        {
            Process[] ps = Process.GetProcessesByName("GeometryDash");
            if (ps.Length == 0) return;
            string dir = Path.GetDirectoryName(Path.GetFullPath(target));
            string l = Path.GetDirectoryName(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeometryDash"));
            string r = Path.GetDirectoryName(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeometryDash"));
            if (string.Equals(dir, l, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dir, r, StringComparison.OrdinalIgnoreCase))
                throw new Exception("GeometryDash запущен (PID " + string.Join(",", Array.ConvertAll(ps, p => p.Id.ToString()))
                    + "). Закрой игру — иначе она перезапишет сейв из памяти.");
            Console.WriteLine("[i] игра запущена, но цель не в её каталоге (" + dir + ") — пишу");
        }

        static void Backup(string path)
        {
            if (_noBackup) return;
            string dst = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, dst, true);
            Console.WriteLine("[backup] " + dst);
        }

        static bool _noBackup;
        static bool _debug;

        // ------------------------------------------------------------------
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }
            try { return Run(args); }
            catch (Exception e) { Console.WriteLine("[!] " + e.Message); if (_debug) Console.WriteLine(e.StackTrace); return 1; }
        }

        static int Run(string[] args)
        {
            Dictionary<string, string> opt = new Dictionary<string, string>();
            HashSet<string> known = new HashSet<string>(new string[] {
                "f","file","o","out","path","type","proc","max","seconds","writable","nobackup","bytes","align","force","current","watch","want","win","min","maxchanges","exe","base","scan","csv","debug","h","help" });
            List<string> pos = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string key = a.TrimStart('-');
                if (a.StartsWith("-") && known.Contains(key.ToLowerInvariant()))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) opt[key.ToLowerInvariant()] = args[++i];
                    else opt[key.ToLowerInvariant()] = "true";
                }
                else pos.Add(a);
            }
            _noBackup = opt.ContainsKey("nobackup");
            _debug = opt.ContainsKey("debug");
            string ex; optExe = opt.TryGetValue("exe", out ex) ? ex : null;
            string bo; if (opt.TryGetValue("base", out bo) && bo.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) STAT_PTR_OFF = long.Parse(bo.Substring(2), NumberStyles.HexNumber);
            if (pos.Count == 0 || opt.ContainsKey("h") || opt.ContainsKey("help")) { Help(); return pos.Count == 0 ? 1 : 0; }

            string act = pos[0].ToLowerInvariant();
            string path = pos.Count > 1 ? pos[1] : null;
            string value = pos.Count > 2 ? pos[2] : null;
            // find / mem-* принимают единственный позиционный аргумент — это и есть значение
            if (value == null && (act == "find" || act.StartsWith("mem-"))) value = path;
            string stateFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gd_scan.json");

            switch (act)
            {
                // ---------------- SAVE ----------------
                case "info":
                    {
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        Parsed p = ParseSave(s.Xml);
                        Console.WriteLine("файл   : " + f);
                        Console.WriteLine("размер : " + s.Blob.Length + " байт");
                        Console.WriteLine("XOR    : " + Hex(s.Key) + "  (" + s.Key.Length + " байт)");
                        Console.WriteLine("конверт: " + (s.B64 ? "base64" + (s.Alpha == "url" ? "-url" : "") + " -> " : "") + s.Comp + (s.B64 ? "" : " (без base64)"));
                        Console.WriteLine("xml    : " + s.Xml.Length + " символов");
                        Console.WriteLine("верхних ключей: " + (IsDict(p.Root) ? p.Root.Dict.Count.ToString() : "0"));
                        break;
                    }

                case "dump":
                    {
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        string target = Opt(opt, "o", "out");
                        if (string.IsNullOrEmpty(target)) target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gd_save_dump.xml");
                        File.WriteAllText(target, s.Xml, new UTF8Encoding(false));
                        Console.WriteLine("xml -> " + target + " (" + s.Xml.Length + " симв.)");
                        break;
                    }

                case "ls":
                    {
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        GNode node = GetNode(ParseSave(s.Xml).Root, path);
                        OrderedDictionary acc = new OrderedDictionary();
                        WriteFlat(node, path, acc);
                        int max = int.Parse(OptDef(opt, "max", "200"), CultureInfo.InvariantCulture);
                        int i = 0;
                        foreach (object k in acc.Keys)
                        {
                            if (i >= max) { Console.WriteLine("... (ещё " + (acc.Count - i) + ")"); break; }
                            Console.WriteLine("{0,-40} {1}", k, acc[k]);
                            i++;
                        }
                        Console.WriteLine("[всего " + acc.Count + "]");
                        break;
                    }

                case "get":
                    {
                        if (path == null) throw new Exception("укажи путь: get GS_value/14");
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        GNode node = GetNode(ParseSave(s.Xml).Root, path);
                        if (IsDict(node)) Console.WriteLine(path + " -> словарь (" + node.Dict.Count + " ключей)");
                        else Console.WriteLine(path + " = " + node.Str + "  [<" + node.Kind + ">]");
                        break;
                    }

                case "find":
                    {
                        if (value == null) throw new Exception("нужно: find <число|строка> [-path префикс]");
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        OrderedDictionary acc = new OrderedDictionary();
                        WriteFlat(ParseSave(s.Xml).Root, "", acc);
                        string pref = OptDef(opt, "path", "");
                        long want = 0;
                        bool isNum = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out want);
                        int hits = 0;
                        foreach (object key in acc.Keys)
                        {
                            string k = (string)key;
                            if (!string.IsNullOrEmpty(pref) && !k.StartsWith(pref, StringComparison.Ordinal)) continue;
                            string v = (string)acc[k];
                            int c = v.IndexOf(':');
                            string raw = c >= 0 ? v.Substring(c + 1) : v;
                            bool match;
                            if (isNum)
                            {
                                long n;
                                match = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n == want;
                            }
                            else match = raw.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (match) { Console.WriteLine("{0,-40} {1}", k, v); hits++; }
                        }
                        Console.WriteLine("[совпадений: " + hits + "]");
                        break;
                    }

                case "stats":
                    {
                        Dictionary<string, string> map = new Dictionary<string, string>();
                        map["2"] = "практика, %";
                        map["6"] = "звёзды";
                        map["8"] = "секретные монеты";
                        map["13"] = "алмазы ИЛИ осколки (не разобрано)";
                        map["14"] = "орбы";
                        map["29"] = "осколки ИЛИ алмазы (не разобрано)";
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        GNode node = GetNode(ParseSave(s.Xml).Root, "GS_value");
                        Console.WriteLine("{0,-6} {1,-20} {2}", "ID", "значение", "что это");
                        foreach (object key in node.Dict.Keys)
                        {
                            GNode v = (GNode)node.Dict[key];
                            if (v.Kind != 's' && v.Kind != 'i') continue;
                            string id = (string)key;
                            string label;
                            if (map.ContainsKey(id)) label = map[id];
                            else label = v.Str == "0" ? "? (ноль)" : "? ";
                            Console.WriteLine("{0,-6} {1,-20} {2}", id, v.Str, label);
                        }
                        Console.WriteLine();
                        Console.WriteLine("[i] чтобы подписать остальные: snapshot -> поиграй -> diff");
                        break;
                    }

                case "verify":
                    {
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        Stopwatch sw = Stopwatch.StartNew();
                        SaveFile s = ReadSave(f);
                        long tDec = sw.ElapsedMilliseconds;
                        Parsed p = ParseSave(s.Xml);
                        long tParse = sw.ElapsedMilliseconds - tDec;
                        OrderedDictionary acc = new OrderedDictionary();
                        WriteFlat(p.Root, "", acc);
                        long tFlat = sw.ElapsedMilliseconds - tDec - tParse;
                        string re = Serialize(p);
                        long tSer = sw.ElapsedMilliseconds - tDec - tParse - tFlat;
                        byte[] bytes = EncodeSave(re, s);
                        sw.Stop();
                        bool same = string.Equals(re, s.Xml, StringComparison.Ordinal);
                        // ПОБАЙТОВОЕ сравнение: отличается ли наш файл от файла, который писала игра?
                        int firstDiff = -1;
                        int maxN = Math.Min(bytes.Length, s.Blob.Length);
                        for (int i = 0; i < maxN; i++) if (bytes[i] != s.Blob[i]) { firstDiff = i; break; }
                        bool byteSame = firstDiff < 0 && bytes.Length == s.Blob.Length;
                        Console.WriteLine("узлов значений : " + acc.Count);
                        Console.WriteLine("тайминги       : декод=" + tDec + "ms парс=" + tParse + "ms флэт=" + tFlat
                            + "ms сериал=" + tSer + "ms всего=" + sw.ElapsedMilliseconds + "ms");
                        Console.WriteLine("xml -> сериализация: " + (same ? "идентично, байт в байт" : "РАСХОЖДЕНИЕ")
                            + "  (оригинал " + s.Xml.Length + " / наш " + re.Length + " симв.)");
                        Console.WriteLine("конверт        : " + (s.B64 ? "base64" + (s.Alpha == "url" ? "-url" : "") + " -> " : "") + s.Comp
                            + ", XOR-ключ " + Hex(s.Key));
                        Console.WriteLine("файл побайтово : " + (byteSame ? "ВОСПРОИЗВОДИТСЯ БАЙТ В БАЙТ"
                            : "отличается, наш " + bytes.Length + " байт против " + s.Blob.Length + " оригинала"));
                        if (!byteSame)
                        {
                            int n = Math.Min(24, maxN);
                            StringBuilder oh = new StringBuilder(), nh = new StringBuilder();
                            for (int i = 0; i < n; i++) { oh.Append(s.Blob[i].ToString("X2")).Append(' '); nh.Append(bytes[i].ToString("X2")).Append(' '); }
                            Console.WriteLine("  начало оригинала: " + oh.ToString().Trim());
                            Console.WriteLine("  начало нашего   : " + nh.ToString().Trim());
                            if (firstDiff >= 0)
                                Console.WriteLine("  первое расхождение: смещение " + firstDiff
                                    + ", оригинал 0x" + s.Blob[firstDiff].ToString("X2")
                                    + ", наш 0x" + bytes[firstDiff].ToString("X2"));
                        }
                        if (!same)
                        {
                            int n = Math.Min(re.Length, s.Xml.Length);
                            for (int i = 0; i < n; i++)
                            {
                                if (re[i] != s.Xml[i])
                                {
                                    Console.WriteLine("первое расхождение на символе " + i);
                                    int st = Math.Max(0, i - 60);
                                    Console.WriteLine("  оригинал: ..." + s.Xml.Substring(st, Math.Min(120, s.Xml.Length - st)) + "...");
                                    Console.WriteLine("  наш     : ..." + re.Substring(st, Math.Min(120, re.Length - st)) + "...");
                                    break;
                                }
                            }
                        }
                        string tmp = Path.Combine(Path.GetTempPath(), "gd_verify_tmp.dat");
                        File.WriteAllBytes(tmp, bytes);
                        try
                        {
                            SaveFile chk = ReadSave(tmp);
                            Console.WriteLine("обратное чтение : " + (string.Equals(chk.Xml, re, StringComparison.Ordinal) ? "ок" : "НЕ ОК"));
                        }
                        finally { try { File.Delete(tmp); } catch { } }
                        return same ? 0 : 2;
                    }

                case "set":
                    {
                        if (path == null || value == null) throw new Exception("нужно: set <путь> <значение>");
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        AssertGameClosed(f);
                        SaveFile s = ReadSave(f);
                        Parsed p = ParseSave(s.Xml);
                        GNode node = GetNode(p.Root, path);
                        if (IsDict(node)) throw new Exception("'" + path + "' — словарь, не значение");
                        if (node.Kind != 's' && node.Kind != 'i' && node.Kind != 'r')
                            throw new Exception("'" + path + "' — тип <" + node.Kind + ">, править нельзя");
                        string old = node.Str;
                        if (node.Kind == 'i')
                        {
                            long tmpNum;
                            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmpNum))
                                throw new Exception("для <i> нужно целое");
                        }
                        if (node.Kind == 'r')
                        {
                            double tmpNum;
                            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out tmpNum))
                                throw new Exception("для <r> нужно число (запятая как разделитель)");
                        }
                        node.Str = value;
                        node.Raw = "";                       // помечаем изменённым
                        string newXml = Serialize(p);

                        // сначала во временный файл: оригинал не трогаем, пока не прошла проверка
                        byte[] bytes = EncodeSave(newXml, s);
                        string tmp = Path.Combine(Path.GetTempPath(), "gd_set_" + Guid.NewGuid().ToString("N") + ".dat");
                        File.WriteAllBytes(tmp, bytes);
                        try
                        {
                            SaveFile chk = ReadSave(tmp);
                            if (!string.Equals(chk.Xml, newXml, StringComparison.Ordinal)) throw new Exception("верификация не прошла, оригинал не тронут");
                            if (chk.Comp != s.Comp || chk.B64 != s.B64 || chk.Alpha != s.Alpha) throw new Exception("конверт разъехался, оригинал не тронут");
                        }
                        catch { try { File.Delete(tmp); } catch { } throw; }

                        Backup(f);
                        File.WriteAllBytes(f, bytes);
                        File.Delete(tmp);
                        Console.WriteLine("[ok] " + path + " : " + old + " -> " + value);
                        break;
                    }

                case "snapshot":
                    {
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        OrderedDictionary acc = new OrderedDictionary();
                        WriteFlat(ParseSave(s.Xml).Root, "", acc);
                        OrderedDictionary root = new OrderedDictionary();
                        root["file"] = f;
                        root["key"] = Hex(s.Key);
                        root["comp"] = s.Comp;
                        root["b64"] = s.B64;
                        root["alpha"] = s.Alpha;
                        root["at"] = DateTime.Now.ToString("s");
                        root["vals"] = acc;
                        string target = Opt(opt, "o", "out");
                        if (string.IsNullOrEmpty(target)) target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gd_snapshot.json");
                        File.WriteAllText(target, NewJson().Serialize(root), new UTF8Encoding(false));
                        Console.WriteLine("снимок -> " + target + " (" + acc.Count + " значений)");
                        break;
                    }

                case "diff":
                    {
                        if (path == null) throw new Exception("укажи json снимка: diff before.json");
                        Dictionary<string, object> old =
                            NewJson().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                        Dictionary<string, object> oldVals = old["vals"] as Dictionary<string, object>;
                        string f = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(f);
                        OrderedDictionary acc = new OrderedDictionary();
                        WriteFlat(ParseSave(s.Xml).Root, "", acc);
                        int changed = 0;
                        foreach (object key in acc.Keys)
                        {
                            string k = (string)key;
                            string cur = (string)acc[k];
                            if (!oldVals.ContainsKey(k)) { Console.WriteLine("[+] {0,-40} {1}", k, cur); changed++; }
                            else if ((string)oldVals[k] != cur) { Console.WriteLine("[~] {0,-40} {1} -> {2}", k, oldVals[k], cur); changed++; }
                        }
                        foreach (object key in oldVals.Keys)
                            if (!acc.Contains(key)) { Console.WriteLine("[-] {0,-40} {1}", key, oldVals[(string)key]); changed++; }
                        Console.WriteLine("[изменений: " + changed + " из " + acc.Count + "]");
                        break;
                    }

                // ---------------- MEM ----------------
                case "mem-dump":
                    {
                        string aStr = path;
                        if (aStr == null) throw new Exception("нужно: mem-dump <адрес> [длина]");
                        if (aStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) aStr = aStr.Substring(2);
                        long a = Convert.ToInt64(aStr, 16);
                        int len = value == null ? 256 : int.Parse(value);
                        int p = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, p);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ");
                        try
                        {
                            byte[] buf = new byte[len];
                            IntPtr r;
                            if (!Native.ReadProcessMemory(h, (IntPtr)a, buf, len, out r))
                                throw new Exception("не читается по 0x" + a.ToString("X"));
                            int n = (int)r.ToInt64();
                            Console.WriteLine("дамп 0x" + a.ToString("X") + " (" + n + " байт):");
                            for (int i = 0; i + 16 <= n; i += 16)
                            {
                                StringBuilder hex = new StringBuilder(); StringBuilder asc = new StringBuilder();
                                for (int j = 0; j < 16; j++)
                                {
                                    hex.Append(buf[i + j].ToString("X2")).Append(' ');
                                    byte c = buf[i + j];
                                    asc.Append(c >= 32 && c < 127 ? (char)c : '.');
                                }
                                Console.WriteLine("  {0:X8}  {1} {2}", a + i, hex, asc);
                            }
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "find-stats":
                    {
                        // Ищем узлы [ptr][stat id][value] и сверяем value с сейвом.
                        // Совпавшие ID подтверждают раскладку, дальше находим любой ID по той же схеме.
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        Dictionary<int, string> fromSave = new Dictionary<int, string>();
                        try
                        {
                            string sf = ResolveFile(Opt(opt, "f", "file"));
                            SaveFile ss = ReadSave(sf);
                            GNode gs = GetNode(ParseSave(ss.Xml).Root, "GS_value");
                            foreach (object k in gs.Dict.Keys)
                            {
                                GNode v = (GNode)gs.Dict[k];
                                if (v.Kind == 's' || v.Kind == 'i') fromSave[int.Parse((string)k)] = v.Str;
                            }
                        }
                        catch { }
                        Console.WriteLine("[*] сканирую узлы [указатель][ID][значение]...");
                        Dictionary<int, List<string[]>> found = new Dictionary<int, List<string[]>>();
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ");
                        try
                        {
                            List<long> rA = new List<long>(); List<int> rS = new List<int>();
                            long a = 0x10000, lim = 0x7FFFFFFEFFFFL; MBI mbi;
                            while (a < lim)
                            {
                                if (Native.VirtualQueryEx(h, (IntPtr)a, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
                                long ba = mbi.BaseAddress.ToInt64(), sz = mbi.RegionSize.ToInt64();
                                if (sz <= 0) { a = ba + 0x1000; continue; }
                                uint pr = mbi.Protect, bp = pr & 0xFF;
                                if (mbi.State == 0x1000 && (pr & 0x100) == 0 && (bp == 0x04 || bp == 0x40 || bp == 0x08 || bp == 0x80) && sz < 0x2000000)
                                { rA.Add(ba); rS.Add((int)sz); }
                                a = ba + sz;
                            }
                            byte[] buf = new byte[1 << 20];
                            for (int ri = 0; ri < rA.Count; ri++)
                            {
                                long off = 0;
                                while (off < rS[ri])
                                {
                                    int want = (int)Math.Min((long)buf.Length, rS[ri] - off);
                                    IntPtr r;
                                    if (!Native.ReadProcessMemory(h, (IntPtr)(rA[ri] + off), buf, want, out r)) break;
                                    int got = (int)r.ToInt64();
                                    if (got < 24) break;
                                    for (int j = 0; j + 32 <= got; j += 8)
                                    {
                                        // узел: [end ptr][next ptr][stat int][value uint32]
                                        long endPtr = BitConverter.ToInt64(buf, j);
                                        long nextPtr = BitConverter.ToInt64(buf, j + 8);
                                        if (!LooksLikePtr(endPtr) || !LooksLikePtr(nextPtr)) continue;
                                        int id = BitConverter.ToInt32(buf, j + 16);
                                        if (id < 1 || id > 40) continue;
                                        long v = BitConverter.ToUInt32(buf, j + 20);
                                        if (!found.ContainsKey(id)) found[id] = new List<string[]>();
                                        List<string[]> lst = found[id];
                                        if (lst.Count < 200)
                                            lst.Add(new string[] { (rA[ri] + off + j).ToString(), v.ToString(), idS(id) });
                                    }
                                    off += (got - 32) > 0 ? (got - 32) : 8;   // гарантия прогресса
                                }
                            }
                        }
                        finally { Native.CloseHandle(h); }

                        Console.WriteLine();
                        Console.WriteLine("{0,-4} {1,-26} {2,-10} {3,-8} {4}", "ID", "стат", "в сейве", "узлов", "совпавшие с сейвом");
                        List<int> ids = new List<int>(found.Keys); ids.Sort();
                        foreach (int id in ids)
                        {
                            string sv; fromSave.TryGetValue(id, out sv);
                            List<string[]> l = found[id];
                            List<string> ok = new List<string>();
                            foreach (string[] e in l)
                                if (sv != null && sv == e[1]) ok.Add("0x" + long.Parse(e[0]).ToString("X"));
                            string okS = ok.Count > 0 ? (ok.Count + " шт: " + string.Join(" ", ok.GetRange(0, Math.Min(4, ok.Count)).ToArray()))
                                                       : "-";
                            Console.WriteLine("{0,-4} {1,-26} {2,-10} {3,-8} {4}", id, StatName(id), sv ?? "-", l.Count, okS);
                        }
                        Console.WriteLine();
                        Console.WriteLine("[*] совпадение значения с сейвом = настоящий узел статистики");
                        break;
                    }

                case "wallet":
                    {
                        // Живая таблица GameStatsManager: показывает current/delta и итог "на экране"
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            long si = GetStatsInstance(h, pid);
                            if (si == 0)
                            {
                                Console.WriteLine("[!] не нашёл таблицу статов.");
                                Console.WriteLine("    Если игра только запустилась - зайди в главное меню и подожди, пока прогрузится сейв.");
                                return 6;
                            }
                            bool csv = opt.ContainsKey("csv");
                            if (csv)
                            {
                                Console.WriteLine("id;stat;screen;current;delta");
                                for (int id = 1; id <= 44; id++)
                                {
                                    long v0 = FindStatValue(h, si + STAT_INFO_OFF, id);
                                    if (v0 == 0) continue;
                                    long d0 = FindStatValue(h, si + STAT_DELTA_OFF, id);
                                    uint c0 = Native.ReadU32(h, v0);
                                    uint l0 = d0 != 0 ? Native.ReadU32(h, d0) : 0;
                                    Console.WriteLine(id + ";" + StatName(id) + ";" + unchecked((int)(c0 - l0)) + ";" + c0 + ";" + l0);
                                }
                                break;
                            }
                            Console.WriteLine("{0,-4} {1,-26} {2,-12} {3,-10} {4}", "ID", "стат", "экран", "current", "delta");
                            Console.WriteLine(new string('-', 66));
                            for (int id = 1; id <= 44; id++)
                            {
                                long v = FindStatValue(h, si + STAT_INFO_OFF, id);
                                if (v == 0) continue;
                                long d = FindStatValue(h, si + STAT_DELTA_OFF, id);
                                uint cur = Native.ReadU32(h, v);
                                uint del = d != 0 ? Native.ReadU32(h, d) : 0;
                                int shown = unchecked((int)(cur - del));
                                Console.WriteLine("{0,-4} {1,-26} {2,-12} {3,-10} {4}", id, StatName(id), shown, cur, del);
                            }
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "bulk":
                    {
                        // gd_currency bulk orbs=500000 diamonds=999 usercoins=999 ...
                        // Один скан таблицы - правит все указанные статы сразу.
                        if (pos.Count < 2) throw new Exception("нужно: bulk <стат>=<число> [<стат>=<число> ...]");
                        List<int> order = new List<int>();
                        Dictionary<int, long> want = new Dictionary<int, long>();
                        for (int i = 1; i < pos.Count; i++)
                        {
                            string[] kv = pos[i].Split('=');
                            if (kv.Length != 2) throw new Exception("не понял '" + pos[i] + "', формат: orbs=500000");
                            int sid; ResolveStat(kv[0].Trim(), out sid);
                            long sv = (long)ToNum(kv[1].Trim());
                            if (!want.ContainsKey(sid)) order.Add(sid);
                            want[sid] = sv;
                        }
                        if (want.Count == 0) throw new Exception("не указано ни одного стата");

                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr hb = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (hb == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
                        try
                        {
                            long si2 = GetStatsInstance(hb, pid);
                            if (si2 == 0)
                            {
                                Console.WriteLine("[!] не нашёл таблицу статов.");
                                Console.WriteLine("    Игра запущена? Сейв прогрузился? Зайди в главное меню и повтори.");
                                return 6;
                            }
                            Console.WriteLine("ID   стат                       было          стало");
                            Console.WriteLine(new string('-', 60));
                            int okc = 0;
                            int sumDelta = 0;
                            foreach (int sid in order)
                            {
                                long v = FindStatValue(hb, si2 + STAT_INFO_OFF, sid);
                                if (v == 0) { Console.WriteLine("{0,-4} {1,-26} {2}", sid, StatName(sid), "нет в таблице"); continue; }
                                long d = FindStatValue(hb, si2 + STAT_DELTA_OFF, sid);
                                uint delta = d != 0 ? Native.ReadU32(hb, d) : 0;
                                uint was = Native.ReadU32(hb, v);
                                uint tgt = unchecked((uint)(want[sid] + delta));
                                Native.WriteU32(hb, v, tgt);
                                uint now = Native.ReadU32(hb, v);
                                int wasShown = unchecked((int)(was - delta));
                                int nowShown = unchecked((int)(now - delta));
                                if (now == tgt) { okc++; sumDelta += (nowShown - wasShown); }
                                Console.WriteLine("{0,-4} {1,-26} {2,-13} {3}", sid, StatName(sid), wasShown, nowShown);
                            }
                            Console.WriteLine(new string('-', 60));
                            Console.WriteLine("[ok] записано " + okc + " из " + order.Count + " статов");
                            if (sumDelta != 0) Console.WriteLine("[i] суммарно прибавлено: " + sumDelta);

                            // Дубль в plist-записи, чтобы значения уехали в сейв при выходе.
                            long sc; 
                            List<StatRecord> recs = ScanStatRecords(hb, pid, out sc);
                            int strOk = 0;
                            foreach (StatRecord r in recs)
                                if (want.ContainsKey(r.Id) && WriteStatString(hb, r.Addr, want[r.Id].ToString(CultureInfo.InvariantCulture))) strOk++;
                            Console.WriteLine("[i] в plist-записи ушло " + strOk + " - при выходе из игры сохранится в сейв");
                        }
                        finally { Native.CloseHandle(hb); }
                        break;
                    }

                case "nodedump":
                    {
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            int id = 14;
                            if (path != null) { int tmp; ResolveStat(path, out tmp); id = tmp; }
                            List<string> lg = new List<string>();
                            long si = GetStatsInstance(h, pid);
                            lg.Add("stats_instance = 0x" + si.ToString("X"));
                            for (int which = 0; which < 2; which++)
                            {
                                long info = si + (which == 0 ? STAT_INFO_OFF : STAT_DELTA_OFF);
                                lg.Add("");
                                lg.Add("=== StatInfo #" + (which == 0 ? "ТЕКУЩИЕ" : "ДЕЛЬТЫ") + " = 0x" + info.ToString("X") + " ===");
                                lg.Add("    +0x08 list_end   = 0x" + Native.ReadPtr(h, info + 0x08).ToString("X"));
                                lg.Add("    +0x18 table      = 0x" + Native.ReadPtr(h, info + 0x18).ToString("X"));
                                lg.Add("    +0x30 hash_mask  = 0x" + Native.ReadU64(h, info + 0x30).ToString("X"));
                                long node = FindStatNode(h, info, id);
                                lg.Add("    узел стата " + id + " = " + (node == 0 ? "НЕ НАЙДЕН" : "0x" + node.ToString("X")));
                                if (node == 0) continue;
                                byte[] nb = new byte[0x40]; IntPtr rr;
                                Native.ReadProcessMemory(h, (IntPtr)node, nb, 0x40, out rr);
                                for (int off = 0; off < 0x40; off += 8)
                                {
                                    string hexs = "", txt = "";
                                    for (int k = 0; k < 8; k++)
                                    {
                                        hexs += nb[off + k].ToString("X2") + " ";
                                        txt += (nb[off + k] >= 32 && nb[off + k] < 127) ? (char)nb[off + k] : '.';
                                    }
                                    lg.Add("    +0x" + off.ToString("X2") + "  " + hexs + " |" + txt + "|"
                                        + "   u32=" + BitConverter.ToUInt32(nb, off)
                                        + "  u64=0x" + BitConverter.ToUInt64(nb, off).ToString("X"));
                                }
                            }
                            foreach (string s in lg) Console.WriteLine(s);
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "statmap":
                    {
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            long scanned;
                            Console.WriteLine("[*] сканирую записи статов...");
                            List<StatRecord> recs = ScanStatRecords(h, pid, out scanned);
                            Console.WriteLine("[*] просмотрено " + (scanned / (1024 * 1024)) + " МБ, найдено записей: " + recs.Count);
                            recs.Sort((x, y) => x.Addr.CompareTo(y.Addr));
                            Console.WriteLine();
                            Console.WriteLine("{0,-4} {1,-28} {2,-12} {3}", "ID", "стат", "значение", "адрес");
                            foreach (StatRecord r in recs)
                                Console.WriteLine("{0,-4} {1,-28} {2,-12} 0x{3:X}", r.Id, StatName(r.Id), r.Value, r.Addr);
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "live-scan":
                    {
                        // Правка стата в ЖИВОЙ игре через структуру таблицы (не сканированием)
                        if (path == null || value == null) throw new Exception("нужно: live <стат> <значение>");
                        int id; ResolveStat(path, out id);
                        long want = (long)ToNum(value);
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
                        try
                        {
                            long si = GetStatsInstance(h, pid);
                            if (si == 0)
                            {
                                Console.WriteLine("[!] не нашёл таблицу статов по оффсету base+" + STAT_PTR_OFF.ToString("X"));
                                Console.WriteLine("    Похоже, другая версия игры. Сделай: statmap - чтобы проверить,");
                                Console.WriteLine("    либо откатись на сканирование по значению (live -scan).");
                                return 6;
                            }
                            long vAddr = FindStatValue(h, si + STAT_INFO_OFF, id);
                            if (vAddr == 0) { Console.WriteLine("[!] стат " + id + " (" + StatName(id) + ") не найден в таблице"); return 6; }
                            long dAddr = FindStatValue(h, si + STAT_DELTA_OFF, id);
                            uint delta = dAddr != 0 ? Native.ReadU32(h, dAddr) : 0;
                            uint cur = Native.ReadU32(h, vAddr);
                            uint target = unchecked((uint)(want + delta));
                            Native.WriteU32(h, vAddr, target);
                            uint after = Native.ReadU32(h, vAddr);
                            Console.WriteLine("[ok] " + StatName(id) + " (ID " + id + ")");
                            Console.WriteLine("     было " + cur + "  стало " + after + "  (дельта " + delta + ", твоё значение " + want + ")");
                            Console.WriteLine("     адрес 0x" + vAddr.ToString("X") + " в модуле " +
                                (vAddr - Process.GetProcessById(pid).MainModule.BaseAddress.ToInt64()).ToString("X"));
                            if (after != target) Console.WriteLine("[!] запись не подтвердилась");
                            else Console.WriteLine("[i] значение изменилось в игре. После выхода GD сама сохранит его.");
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "live":
                    {
                        if (path == null || value == null) throw new Exception("нужно: live <стат> <значение> [-current N] [-force] [-seconds N]");
                        int id; string alias = ResolveStat(path, out id);
                        long want = (long)ToNum(value);
                        bool legacy = opt.ContainsKey("scan");

                        string file = ResolveFile(Opt(opt, "f", "file"));
                        SaveFile s = ReadSave(file);
                        GNode node = GetNode(ParseSave(s.Xml).Root, "GS_value/" + id);
                        long inFile = long.Parse(node.Str, CultureInfo.InvariantCulture);

                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));

                        if (!legacy)
                        {
                            // РАБОЧИЙ ПУТЬ: хеш-таблица GameStatsManager - именно её читает UI.
                            // На экране показывается current - delta, поэтому пишем current = цель + delta.
                            IntPtr hh = Native.OpenProcess(0x1F0FFF, false, pid);
                            if (hh == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
                            try
                            {
                                long sc;
                                ScanStatRecords(hh, pid, out sc);
                                long si = GetStatsInstance(hh, pid);
                                if (si == 0)
                                {
                                    Console.WriteLine("[!] не нашёл таблицу статов (статистика)");
                                    Console.WriteLine("    Попробуй старую ветку: live " + path + " " + value + " -scan");
                                    return 6;
                                }
                                long vAddr = FindStatValue(hh, si + STAT_INFO_OFF, id);
                                if (vAddr == 0) { Console.WriteLine("[!] стат " + id + " (" + StatName(id) + ") не найден в таблице"); return 6; }
                                long dAddr = FindStatValue(hh, si + STAT_DELTA_OFF, id);
                                uint delta = dAddr != 0 ? Native.ReadU32(hh, dAddr) : 0;
                                uint curWas = Native.ReadU32(hh, vAddr);
                                uint target = unchecked((uint)(want + delta));
                                Native.WriteU32(hh, vAddr, target);
                                uint after = Native.ReadU32(hh, vAddr);

                                Console.WriteLine("[*] " + alias + " (ID " + id + ") = " + StatName(id));
                                Console.WriteLine("    в таблице:  current=" + curWas + "  delta=" + delta + "  -> на экране было " + (curWas - delta));
                                Console.WriteLine("    записано:   current=" + after + "  delta=" + delta + "  -> на экране будет " + (after - delta));
                                Console.WriteLine("    адрес 0x" + vAddr.ToString("X"));
                                if (after != target) Console.WriteLine("[!] запись не подтвердилась");
                                else Console.WriteLine("[ok] " + StatName(id) + " = " + (after - delta) + " (твоё значение " + want + ")");

                                // Дополнительно пишем в plist-запись, чтобы значение уехало в сейв при выходе из игры.
                                long sc2;
                                List<StatRecord> recs = ScanStatRecords(hh, pid, out sc2);
                                int strOk = 0;
                                foreach (StatRecord r in recs)
                                    if (r.Id == id && WriteStatString(hh, r.Addr, value.Trim())) strOk++;
                                if (strOk > 0)
                                    Console.WriteLine("[i] продублировано в plist-запись (" + strOk + ") - при выходе уйдёт в сейв");
                            }
                            finally { Native.CloseHandle(hh); }
                            break;
                        }

                        string curStr = OptDef(opt, "current", null);
                        long cur = curStr != null ? (long)ToNum(curStr) : inFile;

                        int seconds = int.Parse(OptDef(opt, "seconds", "0"), CultureInfo.InvariantCulture);
                        bool force = opt.ContainsKey("force");

                        List<string> addrs = FindStatAddr(pid, cur, 256);
                        if (addrs.Count == 0)
                        {
                            Console.WriteLine("[!] в памяти игры нет числа " + cur + " (в сейве " + alias + " = " + inFile + ")");
                            Console.WriteLine("    Значение в сейве могло устареть - игра же не сохранялась.");
                            Console.WriteLine("    Посмотри число на экране профиля и повтори:  live " + path + " " + value + " -current <число>");
                            return 3;
                        }
                        Console.WriteLine("[*] " + alias + " (ID " + id + "): в сейве " + inFile + ", ищу в памяти " + cur
                            + " -> кандидатов " + addrs.Count);

                        int watch = int.Parse(OptDef(opt, "watch", "0"), CultureInfo.InvariantCulture);
                        if (watch > 0) return WatchAndWrite(pid, addrs, cur, want, watch, path);

                        if (addrs.Count > 1 && !force)
                        {
                            foreach (string a in addrs) Console.WriteLine("    [" + a + "]");
                            OrderedDictionary st = new OrderedDictionary();
                            st["pid"] = pid; st["stat"] = path; st["id"] = id; st["type"] = "int32";
                            st["cur"] = cur; st["want"] = want; st["addrs"] = addrs;
                            File.WriteAllText(stateFile, NewJson().Serialize(st), new UTF8Encoding(false));
                            Console.WriteLine();
                            Console.WriteLine("[i] Теперь измени значение В ИГРЕ (например собери/потрать " + path + ") и выполни:");
                            Console.WriteLine("      gd_currency live-narrow <новое число>");
                            Console.WriteLine("    После сужения тул сам запишет " + want + " во все оставшиеся адреса.");
                            return 4;
                        }
                        int wrote = WriteLive(pid, addrs, want, seconds);
                        Console.WriteLine("[ok] записано в " + wrote + " адресов: " + alias + " = " + want);
                        Console.WriteLine("[i] закрой игру через штатное меню - GD сам запишет это в сейв");
                        break;
                    }

                case "live-narrow":
                    {
                        if (path == null) throw new Exception("нужно: live-narrow <новое число>");
                        Dictionary<string, object> st = LoadState(stateFile);
                        if (st["stat"] == null) throw new Exception("в " + stateFile + " нет данных live. Сначала: live <стат> <значение>");
                        int pid = Convert.ToInt32(st["pid"]);
                        long want = Convert.ToInt64(st["want"]);
                        long cur = (long)ToNum(path);
                        List<string> keep = new List<string>();
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            foreach (object o in ((System.Collections.IEnumerable)st["addrs"]))
                            {
                                string a = (string)o;
                                long ad = long.Parse(a.Substring(2), NumberStyles.HexNumber);
                                if (Native.ReadInt32(h, ad) == (int)cur) keep.Add(a);
                            }
                        }
                        finally { Native.CloseHandle(h); }
                        Console.WriteLine("[*] осталось кандидатов: " + keep.Count + " (ищем значение " + cur + ")");
                        foreach (string a in keep) Console.WriteLine("    [" + a + "]");
                        if (keep.Count >= 1)
                        {
                            // после сужения все оставшиеся адреса содержат одно и то же значение -
                            // это копии одной переменной (модель + стек + кэш). Пишем во все, как freeze в CE.
                            int w = WriteLive(pid, keep, want, 0);
                            Console.WriteLine("[ok] кандидатов " + keep.Count + ", записано " + w
                                + ": " + st["stat"] + " = " + want);
                            Console.WriteLine("[i] закрой игру через штатное меню - GD сам запишет это в сейв");
                            foreach (string a in keep) Remember(st["stat"].ToString(), a, pid);
                        }
                        else
                        {
                            Console.WriteLine("[!] ни один кандидат не содержит " + cur + ". Возможно ты не изменил это значение в игре.");
                        }
                        break;
                    }

                case "live-probe":
                    {
                        // Ищет стат БЕЗ знания текущего значения: снимает снимки RW-памяти,
                        // находит ячейки, которые меняются, и группирует их по значению.
                        // Настоящий стат узнаётся по копиям: 2-3 адреса держат одно число и меняются синхронно.
                        int seconds = int.Parse(OptDef(opt, "seconds", "30"), CultureInfo.InvariantCulture);
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        int topN = int.Parse(OptDef(opt, "max", "20"), CultureInfo.InvariantCulture);
                        LiveProbe(pid, seconds, topN, stateFile, OptDef(opt, "want", null),
                            int.Parse(OptDef(opt, "min", "0"), CultureInfo.InvariantCulture),
                            int.Parse(OptDef(opt, "maxchanges", "10"), CultureInfo.InvariantCulture),
                            int.Parse(OptDef(opt, "win", "0"), CultureInfo.InvariantCulture));
                        break;
                    }

                case "live-write":
                    {
                        if (path == null || value == null) throw new Exception("нужно: live-write <адрес|адреса через запятую> <значение>");
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        List<string> addrs = new List<string>();
                        foreach (string s in path.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string t = s.Trim();
                            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                            addrs.Add("0x" + long.Parse(t, NumberStyles.HexNumber).ToString("X"));
                        }
                        long want = (long)ToNum(value);
                        int w = WriteLive(pid, addrs, want, int.Parse(OptDef(opt, "seconds", "0"), CultureInfo.InvariantCulture));
                        Console.WriteLine("[ok] записано " + w + " из " + addrs.Count + ": " + want);
                        foreach (string a in addrs) Remember("ручной", a, pid);
                        break;
                    }

                case "setrun":
                    {
                        if (path == null || value == null) throw new Exception("нужно: setrun <стат> <значение>");
                        int id; ResolveStat(path, out id);
                        string exe = FindGameExe();
                        if (exe == null) throw new Exception("не нашёл GeometryDash.exe - запусти игру хоть раз или укажи -exe <путь>");
                        string target = "GS_value/" + id;
                        string file = ResolveFile(Opt(opt, "f", "file"));
                        // ВАЛИДАЦИЯ ДО УБИЙСТВА: если сейв или ключ не годятся - игру не трогаем
                        SaveFile dry = ReadSave(file);
                        GNode dryNode = GetNode(ParseSave(dry.Xml).Root, target);
                        if (IsDict(dryNode)) throw new Exception("'" + target + "' — словарь, не значение");

                        Process[] ps = Process.GetProcessesByName("GeometryDash");
                        if (ps.Length > 0)
                        {
                            Console.WriteLine("[*] закрываю игру (" + ps.Length + " шт), она успеет записать сейв...");
                            foreach (Process p in ps) { try { p.CloseMainWindow(); } catch { } }
                            DateTime dl = DateTime.Now.AddSeconds(8);
                            while (DateTime.Now < dl && Process.GetProcessesByName("GeometryDash").Length > 0)
                                System.Threading.Thread.Sleep(200);
                            foreach (Process p in Process.GetProcessesByName("GeometryDash"))
                            { try { p.Kill(); } catch { } }
                            System.Threading.Thread.Sleep(1200);
                        }
                        // сейв читаем заново: штатный выход игры его перезаписал
                        string changed = PatchSaveValue(file, target, value);
                        Console.WriteLine("[ok] " + target + " : " + changed);
                        PatchCloudToo(file, target, value);
                        Console.WriteLine("[*] запускаю игру");
                        StartGame(exe);
                        Console.WriteLine("[i] AutoCloud Steam подменяет сейв сам, галочки в настройках нет.");
                        Console.WriteLine("    Поэтому патчатся обе копии; после выхода игры проверь орбы ещё раз.");

                        // Орбы/алмазы/осколки в 2.2 живут только в памяти и из файла не грузятся.
                        // Дописываем их сразу после старта свежего процесса.
                        if (IsMemoryOnlyStat(id) && long.Parse(value) > 0)
                        {
                            Console.WriteLine("[*] " + StatName(id) + " в 2.2 не грузится из файла - пишу в память...");
                            for (int attempt = 1; attempt <= 12; attempt++)
                            {
                                System.Threading.Thread.Sleep(5000);
                                Process[] pr = Process.GetProcessesByName("GeometryDash");
                                if (pr.Length == 0) { Console.WriteLine("[!] игра не запустилась"); break; }
                                try
                                {
                                    List<string> lg = new List<string>();
                                    int n = ApplyLiveStat(pr[0].Id, id, value.Trim(), lg);
                                    foreach (string s in lg) Console.WriteLine("    " + s);
                                    if (n > 0) { Console.WriteLine("[ok] " + StatName(id) + " прописан в памяти (" + n + ")"); break; }
                                    Console.WriteLine("    записей пока нет, пробую ещё раз (" + attempt + "/12)");
                                }
                                catch (Exception e) { Console.WriteLine("    " + e.Message); }
                            }
                        }
                        break;
                    }

                case "mem-scan":
                    {
                        string bytesHex = OptDef(opt, "bytes", "");
                        int pidScan = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        if (!string.IsNullOrEmpty(bytesHex)) { ScanBytes(opt, pidScan, bytesHex); break; }
                        if (value == null) throw new Exception("нужно: mem-scan <число> [-type int32|int64|float|double] | -bytes <hex>");
                        int pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        string type = OptDef(opt, "type", "int32");
                        int max = int.Parse(OptDef(opt, "max", "200"), CultureInfo.InvariantCulture);
                        bool writableOnly = opt.ContainsKey("writable");
                        int align = int.Parse(OptDef(opt, "align", (type == "int32" || type == "float") ? "4" : "8"), CultureInfo.InvariantCulture);
                        byte[] needle = Needle(value, type);
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
                        try
                        {
                            Console.WriteLine("[*] скан PID " + pid + " : " + value + " как " + type
                                + (writableOnly ? ", только RW-страницы" : "") + ", выравнивание " + align + "...");
                            Stopwatch sw = Stopwatch.StartNew();
                            long[] hits = Native.Scan(h, needle, null, max, writableOnly, align);
                            sw.Stop();
                            Console.WriteLine("[+] найдено: " + hits.Length + "  (" + sw.ElapsedMilliseconds + " мс)");
                            List<string> list = new List<string>();
                            foreach (long a in hits) list.Add("0x" + a.ToString("X"));
                            OrderedDictionary st = new OrderedDictionary();
                            st["pid"] = pid; st["type"] = type; st["value"] = value; st["align"] = align; st["addrs"] = list;
                            File.WriteAllText(stateFile, NewJson().Serialize(st), new UTF8Encoding(false));
                            for (int i = 0; i < Math.Min(10, hits.Length); i++)
                                Console.WriteLine("    [0x" + hits[i].ToString("X") + "]");
                            if (hits.Length > 10) Console.WriteLine("    ...");
                            Console.WriteLine("[i] поменяй значение в игре и запусти mem-narrow " + value);
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "mem-narrow":
                    {
                        if (value == null) throw new Exception("нужно: mem-narrow <число>");
                        Dictionary<string, object> st = LoadState(stateFile);
                        int pid = Convert.ToInt32(st["pid"]);
                        string type = (string)st["type"];
                        long want = (long)ToNum(value);
                        List<string> keep = new List<string>();
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            foreach (object o in ((System.Collections.IEnumerable)st["addrs"]))
                            {
                                string a = (string)o;
                                long addr = long.Parse(a.Substring(2), NumberStyles.HexNumber);
                                bool eq;
                                if (type == "int32") eq = Native.ReadInt32(h, addr) == (int)want;
                                else if (type == "int64") eq = Native.ReadInt64(h, addr) == want;
                                else
                                {
                                    byte[] b = new byte[type == "float" ? 4 : 8];
                                    IntPtr r;
                                    if (!Native.ReadProcessMemory(h, (IntPtr)addr, b, b.Length, out r)) continue;
                                    double cur = type == "float" ? BitConverter.ToSingle(b, 0) : BitConverter.ToDouble(b, 0);
                                    eq = cur == (double)want;
                                }
                                if (eq) keep.Add(a);
                            }
                            OrderedDictionary ns = new OrderedDictionary();
                            ns["pid"] = pid; ns["type"] = type; ns["value"] = value; ns["align"] = st.ContainsKey("align") ? st["align"] : 4; ns["addrs"] = keep;
                            File.WriteAllText(stateFile, NewJson().Serialize(ns), new UTF8Encoding(false));
                            Console.WriteLine("[+] осталось: " + keep.Count);
                            foreach (string a in keep) Console.WriteLine("    [" + a + "]");
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                case "mem-write":
                    {
                        if (value == null) throw new Exception("нужно: mem-write <число> [-seconds N] [-path 0xAAA,0xBBB]");
                        Dictionary<string, object> st = null;
                        List<string> addrs = new List<string>();
                        string type = OptDef(opt, "type", "int32");
                        string pth = OptDef(opt, "path", "");
                        int pid;
                        if (!string.IsNullOrEmpty(pth))
                        {
                            foreach (string s in pth.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                                addrs.Add(s.Trim());
                            pid = TargetPid(OptDef(opt, "proc", "GeometryDash"));
                        }
                        else
                        {
                            st = LoadState(stateFile);
                            pid = Convert.ToInt32(st["pid"]);
                            type = (string)st["type"];
                            foreach (object o in ((System.Collections.IEnumerable)st["addrs"])) addrs.Add((string)o);
                        }
                        if (addrs.Count == 0) throw new Exception("нет адресов");

                        int seconds = int.Parse(OptDef(opt, "seconds", "0"), CultureInfo.InvariantCulture);
                        byte[] buf = Needle(value, type);
                        IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
                        if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
                        try
                        {
                            if (seconds > 0) Console.WriteLine("[*] freeze " + seconds + " сек: пишу " + value + " в " + addrs.Count + " адресов");
                            int did = 0, skipped = 0;
                            DateTime end = DateTime.Now.AddSeconds(seconds);
                            do
                            {
                                did = 0;
                                foreach (string a in addrs)
                                {
                                    long addr = long.Parse(a.Substring(2), NumberStyles.HexNumber);
                                    if (!Native.IsWritable(h, addr, buf.Length)) { skipped++; continue; }
                                    IntPtr w;
                                    if (Native.WriteProcessMemory(h, (IntPtr)addr, buf, buf.Length, out w) && w.ToInt64() == buf.Length) did++;
                                    else skipped++;
                                }
                                if (seconds > 0)
                                {
                                    if (DateTime.Now < end) System.Threading.Thread.Sleep(50);
                                    else break;
                                }
                            } while (seconds > 0);
                            Console.WriteLine("[ok] записано в " + did + " из " + addrs.Count + " адресов"
                                + (skipped > 0 ? ", пропущено read-only/неудачных: " + skipped : ""));
                        }
                        finally { Native.CloseHandle(h); }
                        break;
                    }

                default:
                    Console.WriteLine("неизвестная команда: " + act);
                    Help();
                    return 1;
            }
            return 0;
        }

        // Следит за кандидатами, пока значение меняется в игре. Настоящий стат - единственный,
        // у кого значение меняется ОДНОВРЕМЕННО во всех копиях; мусор так себя не ведёт.
        static int WatchAndWrite(int pid, List<string> addrs, long cur, long want, int seconds, string statName)
        {
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
            try
            {
                Dictionary<string, int> prev = new Dictionary<string, int>();
                foreach (string a in addrs) prev[a] = Native.ReadInt32(h, Addr(a));
                DateTime end = DateTime.Now.AddSeconds(seconds);
                int tick = 0;
                List<string> seen = null;          // прошлый синхронно изменившийся набор
                int seenVal = 0;
                Console.WriteLine("[*] слежу за " + addrs.Count + " кандидатами до " + seconds + " сек."
                    + " Играй/собирай " + statName + " - значение должно измениться.");
                while (DateTime.Now < end)
                {
                    System.Threading.Thread.Sleep(700);
                    tick++;
                    List<string> changed = new List<string>();
                    Dictionary<string, int> now = new Dictionary<string, int>();
                    foreach (string a in addrs)
                    {
                        int v = Native.ReadInt32(h, Addr(a));
                        now[a] = v;
                        if (v != prev[a]) changed.Add(a);
                    }
                    if (changed.Count == 0)
                    {
                        if (tick % 10 == 0) Console.WriteLine("    [" + (int)(end - DateTime.Now).TotalSeconds + "s] пока без изменений...");
                        continue;
                    }
                    // изменившиеся с одинаковым значением = копии одного стата
                    Dictionary<int, List<string>> groups = new Dictionary<int, List<string>>();
                    foreach (string a in changed)
                    {
                        if (!groups.ContainsKey(now[a])) groups[now[a]] = new List<string>();
                        groups[now[a]].Add(a);
                    }
                    int best = 0, bestCount = 0;
                    foreach (int k in groups.Keys) if (groups[k].Count > bestCount) { bestCount = groups[k].Count; best = k; }
                    List<string> live = groups[best];
                    Console.WriteLine("    [" + (int)(end - DateTime.Now).TotalSeconds + "s] изменилось " + changed.Count
                        + ", одинаковое значение " + best + " у " + live.Count + " шт.");
                    // подтверждаем два тика подряд: набор и значение те же -> это точно наш стат
                    if (seen != null && SameSet(seen, live) && best != seenVal)
                    {
                        int w = WriteLive(pid, live, want, 0);
                        Console.WriteLine("[ok] живой стат " + statName + " подтверждён (" + live.Count + " копий), записано: " + want);
                        foreach (string a in live) Console.WriteLine("    -> " + a);
                        Console.WriteLine("[i] закрой игру через штатное меню - GD сама запишет это в сейв");
                        foreach (string a in live) Remember(statName, a, pid);
                        return 0;
                    }
                    seen = live; seenVal = best; prev = now;
                }
                Console.WriteLine("[!] за отведённое время не удалось подтвердить адрес. Кандидатов осталось " + addrs.Count);
                return 5;
            }
            finally { Native.CloseHandle(h); }
        }

        class ProbeCell
        {
            public int Val;
            public int Changes;
            public double SumDelta;
            public double SumAbs;
            public double LastDelta;
            public bool Hit;          // ячейка хоть раз держала искомое число
        }

        // Следит за ВСЕЙ RW-памятью и ловит ячейки, которые меняются. Знать число не нужно.
        // Стат отличается тем, что у него 2-3 копии с одинаковым значением, меняющиеся синхронно.
        static void LiveProbe(int pid, int seconds, int topN, string stateFile, string want, int minVal, int maxChanges, int win)
        {
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
            try
            {
                List<long> rAddr = new List<long>();
                List<int> rSize = new List<int>();
                List<long> rOff = new List<long>();
                long addr = 0x10000, limit = 0x7FFFFFFEFFFFL, total = 0;
                MBI mbi;
                while (addr < limit && total < 1400000000L)
                {
                    if (Native.VirtualQueryEx(h, (IntPtr)addr, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
                    long ba = mbi.BaseAddress.ToInt64(), sz = mbi.RegionSize.ToInt64();
                    if (sz <= 0) { addr = ba + 0x1000; continue; }
                    uint prot = mbi.Protect, bp = prot & 0xFF;
                    bool wr = bp == 0x04 || bp == 0x40 || bp == 0x08 || bp == 0x80;
                    if (mbi.State == 0x1000 && (prot & 0x100) == 0 && wr && sz < 0x2000000)
                    {
                        rAddr.Add(ba); rSize.Add((int)sz); rOff.Add(total); total += sz;
                    }
                    addr = ba + sz;
                }
                if (total == 0) throw new Exception("не найдено читаемых RW-регионов");
                Console.WriteLine("[*] под наблюдением " + (total / (1024 * 1024)) + " МБ RW-памяти в " + rAddr.Count + " регионах");

                byte[] snap = new byte[total];
                byte[] buf = new byte[1 << 20];
                // первый снимок
                for (int i = 0; i < rAddr.Count; i++)
                {
                    long off = 0;
                    while (off < rSize[i])
                    {
                        int got = ReadChunk(h, rAddr[i] + off, (int)Math.Min((long)buf.Length, rSize[i] - off), buf);
                        if (got <= 0) break;
                        Array.Copy(buf, 0, snap, (int)(rOff[i] + off), got);
                        off += got;
                    }
                }
                Console.WriteLine("[*] первый снимок снят. Слежу " + seconds + " сек - ИГРАЙ и собирай валюту!");
                Console.WriteLine("[*] (на смену значения хватит 5-10 секунд)");

                int wantVal = 0;
                if (want != null) wantVal = unchecked((int)ToNum(want));
                // окно значений вокруг искомого: отсекает тонну мусора на этапе СБОРА,
                // иначе словарь переполняется шумом раньше, чем дойдёт до стата
                int lo = wantVal - win, hi = wantVal + win;
                Dictionary<long, ProbeCell> cells = new Dictionary<long, ProbeCell>();
                int skipped = 0;
                DateTime end = DateTime.Now.AddSeconds(seconds);
                int tick = 0;
                while (DateTime.Now < end)
                {
                    System.Threading.Thread.Sleep(250);
                    for (int i = 0; i < rAddr.Count; i++)
                    {
                        long off = 0;
                        while (off < rSize[i])
                        {
                            int wantRead = (int)Math.Min((long)buf.Length, rSize[i] - off);
                            int got = ReadChunk(h, rAddr[i] + off, wantRead, buf);
                            if (got < 4) break;
                            int baseIdx = (int)(rOff[i] + off);
                            for (int j = 0; j + 4 <= got; j += 4)
                            {
                                int idx = baseIdx + j;
                                if (snap[idx] == buf[j] && snap[idx + 1] == buf[j + 1] &&
                                    snap[idx + 2] == buf[j + 2] && snap[idx + 3] == buf[j + 3]) continue;
                                int old = snap[idx] | (snap[idx + 1] << 8) | (snap[idx + 2] << 16) | (snap[idx + 3] << 24);
                                int nw = buf[j] | (buf[j + 1] << 8) | (buf[j + 2] << 16) | (buf[j + 3] << 24);
                                if (win > 0 && !InWin(old, lo, hi) && !InWin(nw, lo, hi)) continue;
                                long a = rAddr[i] + off + j;
                                ProbeCell c;
                                if (!cells.TryGetValue(a, out c))
                                {
                                    // шум (куча пересоздаётся каждый кадр) не копим, иначе память взорвётся
                                    if (cells.Count > 2500000) { skipped++; continue; }
                                    c = new ProbeCell { Val = old };
                                    cells[a] = c;
                                }
                                double d = nw - c.Val;
                                c.SumDelta += d; c.SumAbs += Math.Abs(d); c.LastDelta = d; c.Changes++; c.Val = nw;
                                if (wantVal != 0 && nw == wantVal) c.Hit = true;   // поймали число в любой момент окна
                            }
                            Array.Copy(buf, 0, snap, baseIdx, got);
                            off += got;
                        }
                    }
                    tick++;
                    if (tick % 8 == 0) Console.WriteLine("    [" + (int)(end - DateTime.Now).TotalSeconds + "s] изменяющихся ячеек: " + cells.Count);
                }

                // группировка по значению: копии одного стата держат одно число
                Dictionary<int, List<long>> byVal = new Dictionary<int, List<long>>();
                foreach (long a in cells.Keys)
                {
                    int v = cells[a].Val;
                    if (v <= minVal || v > 100000000) continue;   // -min отсекает мелкие счётчики анимаций
                    if (!byVal.ContainsKey(v)) byVal[v] = new List<long>();
                    byVal[v].Add(a);
                }
                List<long> best = new List<long>();
                SortedDictionary<int, long> score = new SortedDictionary<int, long>();
                foreach (int v in byVal.Keys)
                {
                    List<long> g = byVal[v];
                    if (g.Count < 2) continue;                        // копий нет - не похоже на стат
                    long ch = 0; foreach (long a in g) ch += cells[a].Changes;
                    score[v] = ch;
                }
                List<int> order = new List<int>(score.Keys);
                // Признак настоящего стата: копий НЕМНОГО (2-8) и значение меняется на мелочь.
                // Отсекаем мусор вроде 115 ячеек с одинаковой константой.
                // если число задано - ищем ячейки, которые хоть раз его держали (тайминг не важен)
                if (wantVal != 0)
                {
                    List<string> hits = new List<string>();
                    foreach (long a in cells.Keys) if (cells[a].Hit) hits.Add("0x" + a.ToString("X"));
                    hits.Sort();
                    Console.WriteLine();
                    Console.WriteLine("всего изменяющихся ячеек: " + cells.Count);
                    if (hits.Count == 0)
                    {
                        Console.WriteLine("[!] ни одна ячейка не держала число " + wantVal + " за это время.");
                        Console.WriteLine("    Возможно оно не менялось (не играл) или на экране было другое.");
                        Console.WriteLine("    Пробуй запустить пробу и ПОИГРАТЬ, либо посмотри таблицу ниже.");
                    }
                    else
                    {
                        Console.WriteLine("[+] ячеек, державших " + wantVal + ": " + hits.Count);
                        foreach (string a in hits) Console.WriteLine("    " + a);
                        Console.WriteLine();
                        Console.WriteLine("[i] записать своё значение:");
                        Console.WriteLine("      gd_currency live-write " + string.Join(",", hits.ToArray()) + " 500000");
                        return;
                    }
                }

                Dictionary<int, double> avgDelta = new Dictionary<int, double>();
                List<int> keep = new List<int>();
                foreach (int v in order)
                {
                    List<long> g = byVal[v];
                    if (g.Count < 2 || g.Count > 8) continue;
                    // стат меняется редко: за сессию 2-5 раз. Мусор меняется сотни раз - отсекаем.
                    long ch = 0; double sumAbs = 0;
                    foreach (long a in g)
                    {
                        if (cells[a].Changes > maxChanges) { ch = int.MaxValue; break; }
                        ch += cells[a].Changes; sumAbs += cells[a].SumAbs;
                    }
                    if (ch == int.MaxValue || ch == 0) continue;
                    avgDelta[v] = sumAbs / ch;
                    keep.Add(v);
                }
                keep.Sort((x, y) =>
                {
                    int c = byVal[y].Count.CompareTo(byVal[x].Count);
                    if (c != 0) return c;
                    return avgDelta[x].CompareTo(avgDelta[y]);
                });

                Console.WriteLine();
                Console.WriteLine("всего изменяющихся ячеек: " + cells.Count + (skipped > 0 ? " (переполнено, пропущено " + skipped + ")" : ""));
                Console.WriteLine("=== ячейки с 2-8 копиями, редко меняющиеся, с мелкой дельтой ===");
                Console.WriteLine("{0,-14} {1,-12} {2,-8} {3,-10} {4}", "адрес", "значение", "копий", "изменений", "ср.дельта");
                int shown = 0;
                foreach (int v in keep)
                {
                    if (shown++ >= topN) break;
                    List<string> g = new List<string>();
                    long ch = 0;
                    foreach (long a in byVal[v]) { g.Add("0x" + a.ToString("X")); ch += cells[a].Changes; }
                    Console.WriteLine("{0,-14} {1,-12} {2,-8} {3,-10} {4}", g[0], v, g.Count, ch,
                        string.Format("{0:0.##}", avgDelta[v]));
                    for (int k = 1; k < g.Count && k < 5; k++) Console.WriteLine("    ещё: " + g[k]);
                }
                if (wantVal != 0) return;   // уже отработала ветка поиска по числу выше
                {
                    Console.WriteLine();
                    Console.WriteLine("[i] Найди в таблице значение, которое видишь на экране профиля, и запиши:");
                    Console.WriteLine("      gd_currency live-write <адрес1,адрес2,...> 500000");
                }
            }
            finally { Native.CloseHandle(h); }
        }

        static int ReadChunk(IntPtr h, long addr, int size, byte[] buf)
        {
            Array.Clear(buf, 0, size);      // при неудаче нельзя читать хвост прошлого региона
            IntPtr r;
            if (!Native.ReadProcessMemory(h, (IntPtr)addr, buf, size, out r)) return 0;
            int n = (int)r.ToInt64();
            return n <= 0 ? 0 : (n > size ? size : n);
        }

        static bool InWin(int v, int lo, int hi) { return v >= lo && v <= hi; }

        static bool SameSet(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            List<string> x = new List<string>(a); List<string> y = new List<string>(b);
            x.Sort(); y.Sort();
            for (int i = 0; i < x.Count; i++) if (x[i] != y[i]) return false;
            return true;
        }

        static long Addr(string a) { return long.Parse(a.Substring(2), NumberStyles.HexNumber); }

        static Dictionary<string, string> StatAliases()
        {
            Dictionary<string, string> a = new Dictionary<string, string>();
            // валюта
            a["orbs"] = "14";
            a["diamonds"] = "13";
            a["double"] = "29"; a["doublecoins"] = "29"; a["diamonds2"] = "29";
            a["diamonds2nd"] = "29"; a["d2"] = "29"; a["shards"] = "29"; a["gem2"] = "29";
            a["usercoins"] = "12"; a["uc"] = "12"; a["coins2"] = "12";
            a["moons"] = "28"; a["moon"] = "28";
            // обычные статы
            a["stars"] = "6";
            a["coins"] = "8"; a["secretcoins"] = "8"; a["secret"] = "8";
            a["practice"] = "2"; a["attempts"] = "2";
            a["jumps"] = "1";
            a["levels"] = "3"; a["completed"] = "3";
            a["online"] = "4";
            a["demons"] = "5";
            a["packs"] = "7";
            a["destroyed"] = "9";
            a["rated"] = "11";
            a["keys"] = "21";
            a["totalorbs"] = "22"; a["orbscollected"] = "22";
            // осколки
            a["shadow"] = "16"; a["poison"] = "17"; a["fire"] = "18"; a["ice"] = "19";
            a["lava"] = "20"; a["basementkeys"] = "21";
            a["earth"] = "23"; a["blood"] = "24"; a["light"] = "25"; a["metal"] = "26"; a["soul"] = "27";
            return a;
        }

        static string ResolveStat(string name, out int id)
        {
            string s = name.Trim().ToLowerInvariant();
            Dictionary<string, string> a = StatAliases();
            if (a.ContainsKey(s)) { id = int.Parse(a[s]); return "стат " + id + " (" + name + ")"; }
            if (int.TryParse(s, out id)) return "стат " + id;
            List<string> ks = new List<string>(a.Keys); ks.Sort();
            throw new Exception("не знаю стата '" + name + "'.\n    Известные: " + string.Join(", ", ks.ToArray()) + "\n    или просто номер ID");
        }

        // ищет в памяти процесса значение стата; по умолчанию int32 с выравниванием 4
        static List<string> FindStatAddr(int pid, long value, int max)
        {
            List<string> res = new List<string>();
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
            try
            {
                byte[] n32 = BitConverter.GetBytes(checked((int)value));
                foreach (long a in Native.Scan(h, n32, null, max, true, 4)) res.Add("0x" + a.ToString("X"));
                if (res.Count == 0)
                {
                    byte[] n64 = BitConverter.GetBytes(value);
                    foreach (long a in Native.Scan(h, n64, null, max, true, 8)) res.Add("0x" + a.ToString("X"));
                }
            }
            finally { Native.CloseHandle(h); }
            return res;
        }

        static int WriteLive(int pid, List<string> addrs, long want, int seconds)
        {
            byte[] buf = BitConverter.GetBytes(checked((int)want));
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (PID=" + pid + ")");
            int did = 0, skip = 0;
            try
            {
                DateTime end = DateTime.Now.AddSeconds(seconds);
                do
                {
                    did = 0;
                    foreach (string a in addrs)
                    {
                        long ad = long.Parse(a.Substring(2), NumberStyles.HexNumber);
                        if (!Native.IsWritable(h, ad, 4)) { skip++; continue; }
                        IntPtr w;
                        if (Native.WriteProcessMemory(h, (IntPtr)ad, buf, 4, out w) && w.ToInt64() == 4) did++;
                        else skip++;
                    }
                    if (seconds > 0)
                    {
                        if (DateTime.Now < end) System.Threading.Thread.Sleep(50);
                        else break;
                    }
                } while (seconds > 0);
                // проверка: читаем обратно
                foreach (string a in addrs)
                {
                    long ad = long.Parse(a.Substring(2), NumberStyles.HexNumber);
                    if (Native.ReadInt32(h, ad) == (int)want) did = Math.Max(did, 1);
                }
            }
            finally { Native.CloseHandle(h); }
            if (skip > 0) Console.WriteLine("[i] пропущено непригодных адресов: " + skip);
            return did;
        }

        // запоминаем адрес как смещение от базы модуля, чтобы пережить ASLR
        static void Remember(string stat, string addr, int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                long baseAddr = p.MainModule.BaseAddress.ToInt64();
                long off = long.Parse(addr.Substring(2), NumberStyles.HexNumber) - baseAddr;
                OrderedDictionary st = new OrderedDictionary();
                st["stat"] = stat; st["addr"] = addr; st["off"] = off; st["module"] = p.ProcessName; st["at"] = DateTime.Now.ToString("s");
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gd_stats.json");
                List<object> items = new List<object>();
                if (File.Exists(f))
                {
                    try
                    {
                        System.Collections.ArrayList old = NewJson().Deserialize<System.Collections.ArrayList>(File.ReadAllText(f));
                        foreach (object o in old) items.Add(o);
                    }
                    catch { }
                }
                items.Add(st);
                File.WriteAllText(f, NewJson().Serialize(items), new UTF8Encoding(false));
                Console.WriteLine("[i] адрес запомнен: " + stat + " = " + addr + " (смещение 0x" + off.ToString("X") + " от базы модуля)");
            }
            catch (Exception e) { Console.WriteLine("[i] не смог запомнить адрес: " + e.Message); }
        }

        // AutoCloud Steam (папка "ac" в userdata) синхронизирует %LOCALAPPDATA% сам,
        // без галочки в настройках. Если не патчить и его - Steam вернёт старую версию.
        static List<string> CloudCopies(string fileName)
        {
            List<string> res = new List<string>();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam\\userdata");
            if (!Directory.Exists(root)) return res;
            foreach (string ud in Directory.GetDirectories(root))
            {
                string p = Path.Combine(ud, "322170", "ac", "WinAppDataLocal", "GeometryDash", fileName);
                if (File.Exists(p)) res.Add(p);
            }
            return res;
        }

        static void PatchCloudToo(string localFile, string statPath, string value)
        {
            List<string> clouds = CloudCopies(Path.GetFileName(localFile));
            if (clouds.Count == 0)
            {
                Console.WriteLine("[i] AutoCloud-копия сейчас отсутствует - Steam зальёт нашу при следующей синхронизации");
                return;
            }
            foreach (string c in clouds)
            {
                try
                {
                    string r = PatchSaveValue(c, statPath, value);
                    Console.WriteLine("[ok] AutoCloud тоже пропатчен: " + c + " (" + r + ")");
                }
                catch (Exception e) { Console.WriteLine("[!] AutoCloud " + c + ": " + e.Message); }
            }
        }

        // ================================================================
        //  СТРУКТУРА СТАТОВ GD 2.2 (x64)
        //  по данным публичного редактора Nico-Posada/Geometry-Dash-2.2-Stat-Editor
        //
        //  base+0x6A4E78 -> stats_instance
        //  StatInfo @ stats_instance+0x248 (текущие), +0x288 (дельты)
        //     +0x08 list_end      +0x18 stat_table      +0x30 hash_mask
        //  StatLinkedList (узел хеш-таблицы):
        //     +0x08 next    +0x10 stat (int)    +0x14 value (uint32)
        //  бакет = 2 * (FNV1a64(4 байта ID) & hash_mask)
        // ================================================================
        static long STAT_PTR_OFF = 0x6A4E78;   // переопределяется -base
        const long STAT_INFO_OFF = 0x248;
        const long STAT_DELTA_OFF = 0x288;

        static readonly Dictionary<int, string> StatNames = new Dictionary<int, string>
        {
            {1,"ВСЕГО ПРЫЖКОВ"},{2,"ВСЕГО ПОПЫТОК"},{3,"ПРОЙДЕНО УРОВНЕЙ"},{4,"ПРОЙДЕНО ОНЛАЙН-УРОВНЕЙ"},
            {5,"ДЕМОНОВ"},{6,"ЗВЁЗДЫ"},{7,"ПРОЙДЕНО НАБОРОВ"},{8,"МОНЕТЫ"},{9,"ИГРОКОВ УНИЧТОЖЕНО"},
            {10,"ЛАЙК/ДИЗЛАЙК УРОВНЕЙ"},{11,"ОЦЕНЕНО УРОВНЕЙ"},{12,"USER COINS"},{13,"АЛМАЗЫ"},{14,"ОРБЫ"},
            {15,"ПРОЙДЕНО ЕЖЕДНЕВНЫХ"},{16,"ТЕНЕВЫЕ ОСКОЛКИ"},{17,"ЯДОВЫЕ ОСКОЛКИ"},{18,"ОГНЕННЫЕ ОСКОЛКИ"},
            {19,"ЛЕДЯНЫЕ ОСКОЛКИ"},{20,"ЛАВОВЫЕ ОСКОЛКИ"},{21,"КЛЮЧИ ПОДВАЛА"},{22,"ВСЕГО ОРБ СОБРАНО"},
            {23,"ЗЕМЛЯНЫЕ ОСКОЛКИ"},{24,"КРОВЯНЫЕ ОСКОЛКИ"},{25,"СВЕТОВЫЕ ОСКОЛКИ"},{26,"МЕТАЛЛИЧЕСКИЕ ОСКОЛКИ"},
            {27,"ДУШЕВНЫЕ ОСКОЛКИ"},{28,"ЛУНЫ"},{29,"ДВОЙНЫЕ АЛМАЗЫ"},{30,"ПРОЙДЕНО ГАУНТЛЕТОВ"},
            {31,"СОБРАНО НАГРАД СПИСКА"}
        };

        // В 2.2 эти статы не восстанавливаются из файла (известный баг RobTop) - живут только в памяти
        static bool IsMemoryOnlyStat(int id)
        {
            return id == 13 || id == 14 || id == 16 || id == 17 || id == 18 || id == 19 || id == 20 || id == 21 ||
                   (id >= 23 && id <= 29) || id == 31;
        }

        static string idS(int id) { return id.ToString(); }

        static string StatName(int id)
        {
            string n;
            return StatNames.TryGetValue(id, out n) ? n : ("стат " + id);
        }

        // адрес узла стата по хеш-таблице; 0 если не найден
        static long FindStatNode(IntPtr h, long statInfo, int statId)
        {
            long table = Native.ReadPtr(h, statInfo + 0x18);
            long listEnd = Native.ReadPtr(h, statInfo + 0x08);
            ulong mask = Native.ReadU64(h, statInfo + 0x30);
            if (table == 0 || mask == 0) return 0;

            ulong hash = 0xCBF29CE484222325UL;
            const ulong prime = 0x100000001B3UL;
            byte[] num = BitConverter.GetBytes(statId);
            for (int i = 0; i < 4; i++) { hash ^= num[i]; hash *= prime; }
            ulong idx = 2 * (hash & mask);

            long cur = Native.ReadPtr(h, table + (long)(idx + 1) * 8);
            if (cur == 0 || cur == listEnd) return 0;
            long start = Native.ReadPtr(h, table + (long)idx * 8);

            if (statId != Native.ReadI32(h, cur + 0x10))
            {
                int guard = 0;
                while (cur != start && guard++ < 8192)
                {
                    cur = Native.ReadPtr(h, cur + 0x08);
                    if (cur == 0) return 0;
                    if (statId == Native.ReadI32(h, cur + 0x10)) break;
                }
            }
            if (statId != Native.ReadI32(h, cur + 0x10)) return 0;
            return cur;
        }

        static long FindStatValue(IntPtr h, long statInfo, int statId)
        {
            long n = FindStatNode(h, statInfo, statId);
            return n == 0 ? 0 : n + 0x14;
        }

        static bool LooksLikePtr(long v)
        {
            // пользовательские указатели x64: 0x0000000000001000 .. 0x00007FFFFFFFFFFF
            return v > 0x10000 && v < 0x00007FFFFFFFFFFFL && (v & 7) == 0;
        }

        // Версионно-независимый поиск: ищем сам StatInfo по признакам структуры.
        // hash_mask = 2^k-1 (k=5..12) по смещению +0x30, рядом валидные указатели.
        static long FindStatsInstanceScan(IntPtr h, int pid, List<string> log)
        {
            List<long> rAddr = new List<long>();
            List<int> rSize = new List<int>();
            long addr = 0x10000, limit = 0x7FFFFFFEFFFFL;
            MBI mbi;
            while (addr < limit)
            {
                if (Native.VirtualQueryEx(h, (IntPtr)addr, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
                long ba = mbi.BaseAddress.ToInt64(), sz = mbi.RegionSize.ToInt64();
                if (sz <= 0) { addr = ba + 0x1000; continue; }
                uint prot = mbi.Protect, bp = prot & 0xFF;
                bool wr = bp == 0x04 || bp == 0x40 || bp == 0x08 || bp == 0x80;
                if (mbi.State == 0x1000 && (prot & 0x100) == 0 && wr && sz < 0x2000000)
                { rAddr.Add(ba); rSize.Add((int)sz); }
                addr = ba + sz;
            }
            byte[] buf = new byte[1 << 20];
            int tested = 0;
            for (int ri = 0; ri < rAddr.Count; ri++)
            {
                long off = 0;
                while (off < rSize[ri])
                {
                    int want = (int)Math.Min((long)buf.Length, rSize[ri] - off);
                    IntPtr r;
                    if (!Native.ReadProcessMemory(h, (IntPtr)(rAddr[ri] + off), buf, want, out r)) break;
                    int got = (int)r.ToInt64();
                    if (got < 0x40) break;
                    for (int j = 0; j + 0x40 <= got; j += 8)
                    {
                        ulong mask = BitConverter.ToUInt64(buf, j + 0x30);
                        // быстрый отсев: маска хеш-таблицы = 2^k-1
                        if (mask < 0x1F || mask > 0xFFF || (mask & (mask + 1)) != 0) continue;
                        long x = rAddr[ri] + off + j;          // адрес StatInfo
                        long table = BitConverter.ToInt64(buf, j + 0x18);
                        long listEnd = BitConverter.ToInt64(buf, j + 0x08);
                        if (!LooksLikePtr(table) || !LooksLikePtr(listEnd)) continue;
                        tested++;
                        // проверка: узел стата STARS(6) обязан найтись и содержать stat=6
                        long node = FindStatNode(h, x, 6);
                        if (node == 0) continue;
                        if (Native.ReadI32(h, node + 0x10) != 6) continue;
                        // таблица должна быть НАПОЛНЕНА: почти все ID 1..31 обязаны разрешаться.
                        // Иначе это пустышка (игра ещё не загрузила сейв) - такие отбраковываем.
                        int found = 0;
                        for (int t = 1; t <= 31; t++) if (FindStatNode(h, x, t) != 0) found++;
                        if (found < 20) continue;
                        long si = x - STAT_INFO_OFF;
                        if (log != null)
                            log.Add("StatInfo=0x" + x.ToString("X") + " table=0x" + table.ToString("X")
                                + " mask=0x" + mask.ToString("X") + " -> stats_instance=0x" + si.ToString("X"));
                        return si;
                    }
                    off += (got - 0x40) > 0 ? (got - 0x40) : 8;
                }
            }
            if (log != null) log.Add("проверено кандидатов по маске: " + tested);
            return 0;
        }

        static long GetStatsInstance(IntPtr h, int pid)
        {
            try
            {
                long b = Process.GetProcessById(pid).MainModule.BaseAddress.ToInt64();
                long si = Native.ReadPtr(h, b + STAT_PTR_OFF);
                if (si != 0)
                {
                    // быстрая проверка, что оффсет вообще тот
                    if (FindStatNode(h, si + STAT_INFO_OFF, 6) != 0) return si;
                }
            }
            catch { }
            return FindStatsInstanceScan(h, pid, null);
        }

        // ================================================================
        //  ТАБЛИЦА СТАТОВ В ПАМЯТИ GD 2.2 (найдена эмпирически на живой игре)
        //  запись 0x60 байт:
        //     +0x00 u64 = 0x0F
        //     +0x08 ptr (куча)
        //     +0x18 ptr (модуль)
        //     +0x20 u32 = 0x35?? , младший байт = ID стата
        //     +0x48 char[16] = значение СТРОКОЙ ("2701", "9999", "25"...)
        // ================================================================
        const int REC_SIZE = 0x60;
        const int REC_ID_OFF = 0x18;
        const int REC_VAL_OFF = 0x48;
        const int REC_VAL_LEN = 16;

        class StatRecord
        {
            public long Addr;      // начало записи
            public int Id;
            public string Value;
        }

        static string ReadStatString(IntPtr h, long rec)
        {
            byte[] b = new byte[REC_VAL_LEN];
            IntPtr r;
            if (!Native.ReadProcessMemory(h, (IntPtr)(rec + REC_VAL_OFF), b, REC_VAL_LEN, out r)) return null;
            int n = 0;
            while (n < REC_VAL_LEN && b[n] != 0) n++;
            if (n == 0) return "";
            string s = Encoding.ASCII.GetString(b, 0, n);
            foreach (char c in s) if (!char.IsDigit(c) && c != '-') return null;   // только цифры
            return s;
        }

        static bool WriteStatString(IntPtr h, long rec, string val)
        {
            if (val.Length >= REC_VAL_LEN) return false;
            byte[] b = new byte[REC_VAL_LEN];
            Encoding.ASCII.GetBytes(val, 0, val.Length, b, 0);
            b[val.Length] = 0;
            IntPtr w;
            return Native.WriteProcessMemory(h, (IntPtr)(rec + REC_VAL_OFF), b, REC_VAL_LEN, out w) && w.ToInt64() == REC_VAL_LEN;
        }

        // Один проход по RW-памяти: ищем подпись записи стата и собираем все записи
        static List<StatRecord> ScanStatRecords(IntPtr h, int pid, out long scanned)
        {
            List<StatRecord> res = new List<StatRecord>();
            scanned = 0;
            List<long> rA = new List<long>(); List<int> rS = new List<int>();
            long a = 0x10000, lim = 0x7FFFFFFEFFFFL; MBI mbi;
            while (a < lim)
            {
                if (Native.VirtualQueryEx(h, (IntPtr)a, out mbi, (IntPtr)Marshal.SizeOf(typeof(MBI))) == IntPtr.Zero) break;
                long ba = mbi.BaseAddress.ToInt64(), sz = mbi.RegionSize.ToInt64();
                if (sz <= 0) { a = ba + 0x1000; continue; }
                uint pr = mbi.Protect, bp = pr & 0xFF;
                if (mbi.State == 0x1000 && (pr & 0x100) == 0 && (bp == 0x04 || bp == 0x40 || bp == 0x08 || bp == 0x80) && sz < 0x2000000)
                { rA.Add(ba); rS.Add((int)sz); }
                a = ba + sz;
            }
            byte[] buf = new byte[1 << 20];
            for (int ri = 0; ri < rA.Count; ri++)
            {
                long off = 0;
                while (off < rS[ri])
                {
                    int want = (int)Math.Min((long)buf.Length, rS[ri] - off);
                    IntPtr r;
                    if (!Native.ReadProcessMemory(h, (IntPtr)(rA[ri] + off), buf, want, out r)) break;
                    int got = (int)r.ToInt64();
                    if (got < REC_SIZE) break;
                    scanned += got;
                    for (int j = 0; j + REC_SIZE <= got; j += 8)
                    {
                        // подпись записи: 0F 00 00 00 00 00 00 00 ... 0x35?? в +0x20
                        if (buf[j] != 0x0F || buf[j + 1] != 0x00 || buf[j + 2] != 0x00 || buf[j + 3] != 0x00) continue;
                        if (buf[j + 4] != 0x00 || buf[j + 5] != 0x00 || buf[j + 6] != 0x00 || buf[j + 7] != 0x00) continue;
                        // u32 = 0x000035xx -> младший байт = ID стата, следом идёт 0x35
                        int id = buf[j + REC_ID_OFF];
                        if (buf[j + REC_ID_OFF + 1] != 0x35 || id < 1 || id > 44) continue;
                        if (buf[j + REC_ID_OFF + 2] != 0 || buf[j + REC_ID_OFF + 3] != 0) continue;
                        long rec = rA[ri] + off + j;
                        string val = ReadStatString(h, rec);
                        if (val == null) continue;
                        res.Add(new StatRecord { Addr = rec, Id = id, Value = val });
                    }
                    off += (got - REC_SIZE) > 0 ? (got - REC_SIZE) : 8;
                }
            }
            return res;
        }

        // Запись значения стата в живую память игры (по таблице записей)
        static int ApplyLiveStat(int pid, int id, string val, List<string> log)
        {
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ");
            try
            {
                long sc;
                List<StatRecord> recs = ScanStatRecords(h, pid, out sc);
                List<StatRecord> mine = new List<StatRecord>();
                foreach (StatRecord r in recs) if (r.Id == id) mine.Add(r);
                if (log != null) log.Add("записей в памяти: " + mine.Count + " из " + recs.Count);
                int ok = 0;
                foreach (StatRecord r in mine)
                {
                    if (log != null) log.Add("  0x" + r.Addr.ToString("X") + " : " + r.Value + " -> " + val);
                    if (WriteStatString(h, r.Addr, val)) ok++;
                }
                return ok;
            }
            finally { Native.CloseHandle(h); }
        }

        // Запуск через Steam -applaunch: прямой запуск exe ненадёжен (GD не стартует или цепляется к Steam)
        static void StartGame(string exe)
        {
            string steam = @"C:\Program Files (x86)\Steam\steam.exe";
            if (File.Exists(steam))
            {
                Console.WriteLine("[*] запуск через Steam: -applaunch 322170");
                Process.Start(new ProcessStartInfo(steam) { Arguments = "-applaunch 322170", UseShellExecute = true });
                return;
            }
            Console.WriteLine("[*] запуск напрямую: " + exe);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
        }

        // Правка значения сейва с проверкой через временный файл (оригинал не трогаем до успеха).
        static string PatchSaveValue(string file, string path, string value)
        {
            SaveFile s = ReadSave(file);
            Parsed p = ParseSave(s.Xml);
            GNode node = GetNode(p.Root, path);
            if (IsDict(node)) throw new Exception("'" + path + "' — словарь, не значение");
            if (node.Kind != 's' && node.Kind != 'i' && node.Kind != 'r')
                throw new Exception("'" + path + "' — тип <" + node.Kind + ">, править нельзя");
            string old = node.Str;
            if (node.Kind == 'i')
            {
                long tmpNum;
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmpNum))
                    throw new Exception("для <i> нужно целое");
            }
            if (node.Kind == 'r')
            {
                double tmpNum;
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out tmpNum))
                    throw new Exception("для <r> нужно число (запятая как разделитель)");
            }
            node.Str = value;
            node.Raw = "";
            string newXml = Serialize(p);

            byte[] bytes = EncodeSave(newXml, s);
            string tmp = Path.Combine(Path.GetTempPath(), "gd_set_" + Guid.NewGuid().ToString("N") + ".dat");
            File.WriteAllBytes(tmp, bytes);
            try
            {
                SaveFile chk = ReadSave(tmp);
                if (!string.Equals(chk.Xml, newXml, StringComparison.Ordinal)) throw new Exception("верификация не прошла, оригинал не тронут");
                if (chk.Comp != s.Comp || chk.B64 != s.B64 || chk.Alpha != s.Alpha) throw new Exception("конверт разъехался, оригинал не тронут");
            }
            catch { try { File.Delete(tmp); } catch { } throw; }

            Backup(file);
            File.WriteAllBytes(file, bytes);
            File.Delete(tmp);
            return old + " -> " + value;
        }

        static string FindGameExe()
        {
            string opt2 = optExe;
            if (!string.IsNullOrEmpty(opt2) && File.Exists(opt2)) return opt2;
            Process[] ps = Process.GetProcessesByName("GeometryDash");
            if (ps.Length > 0)
            {
                try { return ps[0].MainModule.FileName; } catch { }
            }
            string[] cands = {
                @"C:\Program Files (x86)\Steam\steamapps\common\Geometry Dash\GeometryDash.exe",
                @"C:\Program Files\Steam\steamapps\common\Geometry Dash\GeometryDash.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"..\Programs\Geometry Dash\GeometryDash.exe")
            };
            foreach (string c in cands) if (File.Exists(c)) return c;
            return null;
        }

        static string optExe;

        static JavaScriptSerializer NewJson()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;   // сейв GD легко переваливает за дефолтные 2 МБ
            s.RecursionLimit = 256;
            return s;
        }

        static string Opt(Dictionary<string, string> opt, string a, string b)
        {
            string v;
            if (opt.TryGetValue(a, out v)) return v;
            if (opt.TryGetValue(b, out v)) return v;
            return null;
        }

        // AOB-скан: ищем байтовую сигнатуру, а не число. Нужен для привязки к полям структур.
        static void ScanBytes(Dictionary<string, string> opt, int pid, string hex)
        {
            byte[] needle; bool[] wild;
            ParseHex(hex, out needle, out wild);
            int max = int.Parse(OptDef(opt, "max", "200"), CultureInfo.InvariantCulture);
            bool writableOnly = opt.ContainsKey("writable");
            IntPtr h = Native.OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero) throw new Exception("OpenProcess не дал доступ (нужны права админа? PID=" + pid + ")");
            try
            {
                Console.WriteLine("[*] AOB-скан PID " + pid + " : " + hex + " (" + needle.Length + " байт)"
                    + (writableOnly ? ", только RW" : "") + "...");
                Stopwatch sw = Stopwatch.StartNew();
                long[] hits = Native.Scan(h, needle, wild, max, writableOnly, 1);
                sw.Stop();
                Console.WriteLine("[+] найдено: " + hits.Length + "  (" + sw.ElapsedMilliseconds + " мс)");
                for (int i = 0; i < Math.Min(20, hits.Length); i++)
                    Console.WriteLine("    [0x" + hits[i].ToString("X") + "]");
            }
            finally { Native.CloseHandle(h); }
        }

        static void ParseHex(string s, out byte[] needle, out bool[] wild)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (!char.IsWhiteSpace(c) && c != ',') sb.Append(c);
            if (sb.Length % 2 != 0) throw new Exception("hex должен быть чётной длины");
            needle = new byte[sb.Length / 2];
            wild = new bool[needle.Length];
            for (int i = 0; i < needle.Length; i++)
            {
                string b = sb.ToString(i * 2, 2);
                if (b[0] == '?' || b[0] == '*') { wild[i] = true; needle[i] = 0; }
                else needle[i] = byte.Parse(b, NumberStyles.HexNumber);
            }
        }

        static byte[] ParseHex(string s)
        {
            byte[] n; bool[] w;
            ParseHex(s, out n, out w);
            return n;
        }

        static string OptDef(Dictionary<string, string> opt, string a, string def)
        {
            string v;
            return opt.TryGetValue(a, out v) ? v : def;
        }

        static Dictionary<string, object> LoadState(string file)
        {
            if (!File.Exists(file)) throw new Exception("нет " + file + " — сначала сделай mem-scan");
            return NewJson().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
        }

        static int TargetPid(string spec)
        {
            if (string.IsNullOrEmpty(spec) || spec == "true") spec = "GeometryDash";
            int n;
            if (int.TryParse(spec, out n)) return n;
            Process[] ps = Process.GetProcessesByName(spec);
            if (ps.Length == 0) throw new Exception("процесс '" + spec + "' не найден");
            if (ps.Length > 1) Console.WriteLine("[!] найдено " + ps.Length + " процессов, беру PID " + ps[0].Id);
            return ps[0].Id;
        }

        static double ToNum(string v)
        {
            string t = v.Trim();
            bool neg = false;
            if (t.StartsWith("-0x", StringComparison.OrdinalIgnoreCase)) { neg = true; t = t.Substring(1); }
            else if (t.StartsWith("+0x", StringComparison.OrdinalIgnoreCase)) { t = t.Substring(1); }
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // hex разбираем целым: NumberStyles.HexNumber неприменим к double
                ulong u = ulong.Parse(t.Substring(2), NumberStyles.HexNumber);
                double d = u;
                return neg ? -d : d;
            }
            double r;
            double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
            return r;
        }

        static byte[] Needle(string val, string type)
        {
            double n = ToNum(val);
            switch (type)
            {
                case "int32": return BitConverter.GetBytes(checked((int)n));
                case "int64": return BitConverter.GetBytes(checked((long)n));
                case "float": return BitConverter.GetBytes((float)n);
                case "double": return BitConverter.GetBytes(n);
            }
            throw new Exception("тип должен быть int32|int64|float|double");
        }

        static void Help()
        {
            Console.WriteLine(@"gd_currency — инструмент для Geometry Dash (save + память)

  SAVE:
    gd_currency info                                   разбор сейва
    gd_currency stats                                  ID статов + что это
    gd_currency ls [путь] [-max N]                     плоский список значений
    gd_currency get <путь>                             одно значение, напр. GS_value/14
    gd_currency set <путь> <значение>                  накрутить (игра должна быть ЗАКРЫТА)
    gd_currency setrun <стат> <значение>               ЗАКРЫТЬ игру -> накрутить -> запустить заново
                                                      (самый надёжный способ, всё в одной команде)
    gd_currency find <число|строка> [-path префикс]     поиск по значению
    gd_currency verify                                 round-trip, побайтовая сверка
    gd_currency dump [-o файл.xml]                     выгрузить plist в xml
    gd_currency snapshot [-o b.json]                   снимок всех значений
    gd_currency diff <снимок.json>                     что изменилось в игре

  MEM (живой процесс, как в Cheat Engine):
    gd_currency live <стат> <значение>        ПРАВИТ СТАТ В ЗАПУЩЕННОЙ ИГРЕ (в т.ч. орбы)
    gd_currency bulk <стат>=<число> [...]      НАКРУТИТЬ ВСЮ ВАЛЮТУ ЗА ОДИН ПРОХОД
    gd_currency wallet [-csv]                 вся живая статистика: что реально на экране
    gd_gui.exe                                визуальный интерфейс (двойной клик)
    gd_currency nodedump <стат>               сырой дамп узла таблицы (отладка)
    gd_currency statmap                      plist-записи статистики в памяти
    gd_currency live-scan <стат> <значение> [-current N] [-watch N]
        старый метод: поиск по значению (когда live не справился)
    gd_currency live-narrow <новое число>
        сузить кандидатов после того, как значение изменилось в игре
    gd_currency live-probe -seconds N [-min N] [-want <число>] [-maxchanges N]
        НЕ знает числа: следит за всей памятью и ловит ячейки, которые меняются,
        группирует их по значению. Настоящий стат = 2-8 копий одного числа,
        меняющихся редко и на мелочь.
        ИГРАЙ, пока идёт наблюдение.
        -min N        отсечь значения меньше N (мусор анимаций)
        -want N       поймать ячейки, которые хоть раз держали N (число надо ПРОЙТИ,
                      счётчик орбов только растёт -> бери текущее + сколько соберёшь)
        -win N        окно значений вокруг -want (по умолчанию 0=выключено, рекомендую 400-800).
                      Без него шум переполняет сбор и настоящий стат теряется.
        -maxchanges N отсечь ячейки, меняющиеся чаще N раз (шум кучи, по умолчанию 10)
    gd_currency live-write <адрес1,адрес2,...> <значение>
        записать значение по конкретным адресам
    gd_currency mem-scan   <число> [-type int32|int64|float|double] [-writable] [-align N]
    gd_currency mem-scan   -bytes <hex>  (можно ? вместо байта)   AOB-скан
    gd_currency mem-narrow <число>                     сузить после изменения в игре
    gd_currency mem-write  <число> [-seconds N]        записать / держать (freeze)
    gd_currency mem-dump   <адрес> [длина]            hex-дамп памяти

  Общее:
    -f файл       путь к .dat (по умолчанию %LOCALAPPDATA%\GeometryDash\CCGameManager.dat)
    -proc имя/PID процесс-цель для mem-*, по умолчанию GeometryDash

ФОРМАТ СЕЙВА 2.2:
    файл = XOR 0x0B ( base64url( gzip( plist ) ) )
    статы = GS_value/<цифра>, значения строками <s>
    6 = звёзды, 8 = секретные монеты, 14 = орбы, 2 = практика %

АЛИАСЫ ДЛЯ live:
    orbs=14 stars=6 coins=8 diamonds=13 shards=29 practice=2
    (13 и 29 - алмазы и осколки, какой где - смотри snapshot/diff)");
        }
    }

    static class ParsedExt
    {
        public static int Xml_Estimate(this Parsed p)
        {
            int n = (p.Root != null && p.Root.Kind == 'd') ? p.Root.Dict.Count * 64 : 1024;
            return Math.Max(n, 4096);
        }
    }
}
