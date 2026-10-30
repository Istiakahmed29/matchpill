// ============================================================================================
//  MatchPill 1.6.0 - a ~320 px desktop card that answers exactly one question:
//  "is any of my teams playing right now, what is the score, and how long has it been going?"
//
//  The free feed carries no match minute and no scorer list, so the game clock is derived from
//  kickoff (and re-anchored on the half-time and kick-off transitions the feed does show), and
//  the goal timeline is built from score changes this process actually witnesses.
//
//  Single file. C# 5 syntax only (the in-box compiler that ships with Windows has no Roslyn),
//  .NET Framework 4.8 / WinForms. No SDK, no NuGet, no Visual Studio, no installer.
//
//  Build:
//    %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:MatchPill.exe MatchPill.cs
//
//  Optional self test (no window, writes selftest.log):
//    MatchPill.exe --selftest
//
//  Scorer coverage probe (no window, writes coverage.log):
//    MatchPill.exe --coverage      runs the real matcher over every match of the day
// ============================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MatchPill
{
    internal static class Program
    {
        internal const string AppName = "MatchPill";
        internal const string AppVersion = "1.6.0";

        // .NET Framework defaults to an older TLS; the provider only speaks 1.2+. Done before any
        // command line tool runs, so --shot --live can talk to the network too.
        internal static void ConfigureNetworking()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 4;
            }
            catch
            {
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Paths.Init();
            ConfigureNetworking();

            if (args != null)
            {
                bool collapsedShot = false;
                bool detailShot = false;
                bool liveShot = false;
                bool allShot = false;
                bool flashShot = false;
                bool emptyShot = false;
                bool demoShot = false;
                bool demoOneShot = false;
                bool nextShot = false;
                bool cupRowsShot = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--collapsed", StringComparison.OrdinalIgnoreCase))
                    {
                        collapsedShot = true;
                    }

                    if (string.Equals(args[i], "--detail", StringComparison.OrdinalIgnoreCase))
                    {
                        detailShot = true;
                    }

                    if (string.Equals(args[i], "--live", StringComparison.OrdinalIgnoreCase))
                    {
                        liveShot = true;
                    }

                    if (string.Equals(args[i], "--all", StringComparison.OrdinalIgnoreCase))
                    {
                        allShot = true;
                    }

                    if (string.Equals(args[i], "--flash", StringComparison.OrdinalIgnoreCase))
                    {
                        flashShot = true;
                    }

                    if (string.Equals(args[i], "--empty", StringComparison.OrdinalIgnoreCase))
                    {
                        emptyShot = true;
                    }

                    if (string.Equals(args[i], "--demo", StringComparison.OrdinalIgnoreCase))
                    {
                        demoShot = true;
                    }

                    if (string.Equals(args[i], "--demo-one", StringComparison.OrdinalIgnoreCase))
                    {
                        demoShot = true;
                        demoOneShot = true;
                    }

                    if (string.Equals(args[i], "--next", StringComparison.OrdinalIgnoreCase))
                    {
                        nextShot = true;
                    }

                    if (string.Equals(args[i], "--cup-rows", StringComparison.OrdinalIgnoreCase))
                    {
                        cupRowsShot = true;
                    }
                }

                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase))
                    {
                        return SelfTest.Run();
                    }

                    if (string.Equals(args[i], "--coverage", StringComparison.OrdinalIgnoreCase))
                    {
                        return CoverageTool.Run();
                    }

                    if (string.Equals(args[i], "--cups", StringComparison.OrdinalIgnoreCase))
                    {
                        return CupProbe.Run();
                    }

                    if (string.Equals(args[i], "--install", StringComparison.OrdinalIgnoreCase))
                    {
                        return AutoStart.Install() ? 0 : 1;
                    }

                    if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        return AutoStart.Uninstall() ? 0 : 1;
                    }

                    // silent, idempotent: start the card only if it is not already up
                    if (string.Equals(args[i], "--ensure", StringComparison.OrdinalIgnoreCase))
                    {
                        return AutoStart.EnsureRunning();
                    }

                    if (string.Equals(args[i], "--screen", StringComparison.OrdinalIgnoreCase))
                    {
                        string screenTarget = "";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            screenTarget = args[i + 1];
                        }

                        return ScreenShot.Run(screenTarget);
                    }

                    if (string.Equals(args[i], "--minimize-probe", StringComparison.OrdinalIgnoreCase))
                    {
                        string probe = Native.MinimizeProbe(Program.AppName);
                        Console.Out.WriteLine(probe);
                        Log.Write("minimize probe: " + probe);
                        return 0;
                    }

                    if (string.Equals(args[i], "--alert-test", StringComparison.OrdinalIgnoreCase))
                    {
                        return AlertProbe.Run(Array.IndexOf(args, "--fire") >= 0);
                    }

                    if (string.Equals(args[i], "--shot", StringComparison.OrdinalIgnoreCase))
                    {
                        string target = "";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            target = args[i + 1];
                        }

                        return ShotTool.Run(target, collapsedShot, detailShot, liveShot, allShot, flashShot,
                            emptyShot, demoShot, demoOneShot, nextShot, cupRowsShot);
                    }

                }
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "MatchPill.SingleInstance.v1", out createdNew))
            {
                if (!createdNew)
                {
                    Native.RaiseExistingInstance(AppName);
                    return 0;
                }

                Native.TryEnablePerMonitorV2();
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 4;

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("UI thread exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("Unhandled exception: " + e.ExceptionObject);
                };

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Log.Trim();
                Log.Write("---- start " + AppVersion + " (dir " + Paths.Dir + ")");

                using (var context = new PillAppContext())
                {
                    Application.Run(context);
                }

                GC.KeepAlive(mutex);
            }

            return 0;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Win32 helpers
    // ----------------------------------------------------------------------------------------
    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

        // Who is sitting on that pixel? "shell_traywnd" means the taskbar, "Progman" the desktop,
        // anything else means a real window is covering the card.
        internal static string ClassAtCentre(Rectangle bounds)
        {
            try
            {
                var point = new POINT(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                IntPtr hit = WindowFromPoint(point);
                if (hit == IntPtr.Zero)
                {
                    return "none";
                }

                var sb = new StringBuilder(256);
                int len = GetClassName(hit, sb, sb.Capacity);
                return len > 0 ? sb.ToString(0, len) : "?";
            }
            catch
            {
                return "?";
            }
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        // "Show desktop" (Win+D) sends SC_MINIMIZE to every top level window. A desktop widget
        // must survive it, so the card ignores that one message; this probe proves it does.
        internal static string MinimizeProbe(string windowTitle)
        {
            IntPtr handle = FindWindow(null, windowTitle);
            if (handle == IntPtr.Zero)
            {
                return "card window not found (is it running?)";
            }

            bool before = IsIconic(handle);
            PostMessage(handle, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
            System.Threading.Thread.Sleep(700);
            bool after = IsIconic(handle);
            bool visible = IsWindowVisible(handle);
            return "handle=" + handle + " iconicBefore=" + before + " iconicAfter=" + after +
                   " visibleAfter=" + visible;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;

            internal POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);

        internal const int SwHide = 0;
        internal const int SwShowNoActivate = 4;

        // Per-monitor v2 is also declared in app.manifest. This call keeps the app crisp even
        // when it is built with the plain one-line csc command that cannot embed a manifest.
        internal static void TryEnablePerMonitorV2()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2))
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        internal static void ShowNoActivate(IntPtr handle)
        {
            try
            {
                ShowWindow(handle, SwShowNoActivate);
            }
            catch
            {
            }
        }

        internal static void HideWindow(IntPtr handle)
        {
            try
            {
                ShowWindow(handle, SwHide);
            }
            catch
            {
            }
        }

        internal static void DestroyIconHandle(IntPtr handle)
        {
            try
            {
                DestroyIcon(handle);
            }
            catch
            {
            }
        }

        // Desktop layer: the card is a normal (non-topmost) window pushed to the bottom of the
        // z-order, so any app the user opens covers it and it never floats over their work.
        internal static void SendToBottom(IntPtr handle)
        {
            const uint SwpNoMove = 0x0002;
            const uint SwpNoSize = 0x0001;
            const uint SwpNoActivate = 0x0010;
            IntPtr hwndBottom = new IntPtr(1);

            try
            {
                SetWindowPos(handle, hwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch
            {
            }
        }

        // When a second instance starts, find the first one and flash it so the user
        // knows it is already running (instead of silently exiting).
        internal static void RaiseExistingInstance(string windowTitle)
        {
            try
            {
                IntPtr handle = FindWindow(null, windowTitle);
                if (handle != IntPtr.Zero && IsWindow(handle))
                {
                    FlashWindow(handle, true);
                    SetForegroundWindow(handle);
                }
            }
            catch
            {
            }
        }

        // Diagnostic: is our window the one that actually paints at its own centre?
        internal static bool IsVisibleAtCentre(IntPtr handle, Rectangle bounds)
        {
            try
            {
                if (!IsWindowVisible(handle))
                {
                    return false;
                }

                var point = new POINT(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                return WindowFromPoint(point) == handle;
            }
            catch
            {
                return false;
            }
        }

        // A WinForms app on .NET Framework keeps ~30 MB resident after its first paint. The card is
        // idle almost all the time, so hand those pages back once and let them fault in on demand.
        internal static void TrimWorkingSet()
        {
            try
            {
                SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch
            {
            }
        }

        // QUNS_NOT_PRESENT = 1, QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        // QUNS_PRESENTATION_MODE = 4, QUNS_ACCEPTS_NOTIFICATIONS = 5, QUNS_QUIET_TIME = 6
        internal static bool IsFullscreenAppActive()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) != 0)
                {
                    return false;
                }

                return state == 2 || state == 3 || state == 4;
            }
            catch
            {
                return false;
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Minimal JSON (no NuGet allowed, so no Newtonsoft)
    // ----------------------------------------------------------------------------------------
    internal sealed class JVal
    {
        internal enum Kind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object
        }

        internal Kind K = Kind.Null;
        internal bool B;
        internal double N;
        internal string S = "";
        internal List<JVal> A;
        internal Dictionary<string, JVal> O;

        internal static JVal Obj()
        {
            var v = new JVal();
            v.K = Kind.Object;
            v.O = new Dictionary<string, JVal>(StringComparer.Ordinal);
            return v;
        }

        internal static JVal Arr()
        {
            var v = new JVal();
            v.K = Kind.Array;
            v.A = new List<JVal>();
            return v;
        }

        internal static JVal Str(string s)
        {
            var v = new JVal();
            v.K = Kind.String;
            v.S = s == null ? "" : s;
            return v;
        }

        internal static JVal Num(double d)
        {
            var v = new JVal();
            v.K = Kind.Number;
            v.N = d;
            return v;
        }

        internal static JVal Bool(bool b)
        {
            var v = new JVal();
            v.K = Kind.Bool;
            v.B = b;
            return v;
        }

        internal static JVal Null()
        {
            return new JVal();
        }

        internal JVal this[string key]
        {
            get
            {
                if (K != Kind.Object || O == null || key == null)
                {
                    return null;
                }

                JVal v;
                return O.TryGetValue(key, out v) ? v : null;
            }
        }

        internal void Set(string key, JVal value)
        {
            if (O == null)
            {
                return;
            }

            O[key] = value;
        }

        internal string GetString(string key, string fallback)
        {
            JVal v = this[key];
            return v != null && v.K == Kind.String ? v.S : fallback;
        }

        internal int GetInt(string key, int fallback)
        {
            JVal v = this[key];
            if (v != null && v.K == Kind.Number)
            {
                return (int)Math.Round(v.N);
            }

            return fallback;
        }

        internal int? GetNullableInt(string key)
        {
            JVal v = this[key];
            if (v != null && v.K == Kind.Number)
            {
                return (int)Math.Round(v.N);
            }

            return null;
        }

        internal bool GetBool(string key, bool fallback)
        {
            JVal v = this[key];
            return v != null && v.K == Kind.Bool ? v.B : fallback;
        }

        internal List<JVal> GetArray(string key)
        {
            JVal v = this[key];
            return v != null && v.K == Kind.Array ? v.A : null;
        }

        internal static JVal Parse(string json)
        {
            var p = new JParser(json);
            JVal v = p.ParseValue();
            return v == null ? Null() : v;
        }

        internal string ToJson()
        {
            var sb = new StringBuilder(256);
            WriteTo(sb);
            return sb.ToString();
        }

        private void WriteTo(StringBuilder sb)
        {
            switch (K)
            {
                case Kind.Null:
                    sb.Append("null");
                    break;
                case Kind.Bool:
                    sb.Append(B ? "true" : "false");
                    break;
                case Kind.Number:
                    sb.Append(N.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case Kind.String:
                    WriteEscaped(sb, S);
                    break;
                case Kind.Array:
                    sb.Append('[');
                    for (int i = 0; i < A.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        A[i].WriteTo(sb);
                    }

                    sb.Append(']');
                    break;
                case Kind.Object:
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JVal> pair in O)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }

                        first = false;
                        WriteEscaped(sb, pair.Key);
                        sb.Append(':');
                        pair.Value.WriteTo(sb);
                    }

                    sb.Append('}');
                    break;
            }
        }

        private static void WriteEscaped(StringBuilder sb, string text)
        {
            sb.Append('"');
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }
            }

            sb.Append('"');
        }
    }

    internal sealed class JParser
    {
        private readonly string _s;
        private int _i;

        internal JParser(string s)
        {
            _s = s == null ? "" : s;
            _i = 0;
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                _i++;
            }
        }

        internal JVal ParseValue()
        {
            SkipWs();
            if (_i >= _s.Length)
            {
                return JVal.Null();
            }

            char c = _s[_i];
            if (c == '{')
            {
                return ParseObject();
            }

            if (c == '[')
            {
                return ParseArray();
            }

            if (c == '"')
            {
                return JVal.Str(ParseString());
            }

            if (c == 't' || c == 'T')
            {
                Expect("true");
                return JVal.Bool(true);
            }

            if (c == 'f' || c == 'F')
            {
                Expect("false");
                return JVal.Bool(false);
            }

            if (c == 'n' || c == 'N')
            {
                Expect("null");
                return JVal.Null();
            }

            return ParseNumber();
        }

        private void Expect(string literal)
        {
            if (_i + literal.Length <= _s.Length &&
                string.Compare(_s, _i, literal, 0, literal.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _i += literal.Length;
                return;
            }

            throw new FormatException("Unexpected token at " + _i.ToString(CultureInfo.InvariantCulture));
        }

        private JVal ParseObject()
        {
            var o = JVal.Obj();
            _i++;                                   // '{'
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length)
                {
                    return o;
                }

                if (_s[_i] == '}')
                {
                    _i++;
                    return o;
                }

                if (_s[_i] == ',')
                {
                    _i++;
                    continue;
                }

                string key = ParseString();
                SkipWs();
                if (_i < _s.Length && _s[_i] == ':')
                {
                    _i++;
                }

                JVal value = ParseValue();
                if (key != null)
                {
                    o.Set(key, value);
                }
            }
        }

        private JVal ParseArray()
        {
            var a = JVal.Arr();
            _i++;                                   // '['
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length)
                {
                    return a;
                }

                if (_s[_i] == ']')
                {
                    _i++;
                    return a;
                }

                if (_s[_i] == ',')
                {
                    _i++;
                    continue;
                }

                a.A.Add(ParseValue());
            }
        }

        private string ParseString()
        {
            SkipWs();
            if (_i >= _s.Length || _s[_i] != '"')
            {
                return "";
            }

            _i++;                                   // opening quote
            var sb = new StringBuilder(32);
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '"')
                {
                    _i++;
                    break;
                }

                if (c == '\\' && _i + 1 < _s.Length)
                {
                    _i++;
                    char e = _s[_i];
                    switch (e)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            if (_i + 4 < _s.Length)
                            {
                                int code;
                                if (int.TryParse(_s.Substring(_i + 1, 4), NumberStyles.HexNumber,
                                        CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    _i += 4;
                                }
                            }

                            break;
                        default:
                            sb.Append(e);
                            break;
                    }

                    _i++;
                    continue;
                }

                sb.Append(c);
                _i++;
            }

            return sb.ToString();
        }

        private JVal ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    _i++;
                    continue;
                }

                break;
            }

            if (_i == start)
            {
                _i++;                               // skip the offending character, never loop forever
                return JVal.Null();
            }

            double d;
            if (double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out d))
            {
                return JVal.Num(d);
            }

            return JVal.Null();
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Portable paths + logs
    // ----------------------------------------------------------------------------------------
    internal static class Paths
    {
        internal static string Dir = "";

        internal static string ConfigFile
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        internal static string CacheFile
        {
            get { return Path.Combine(Dir, "cache.json"); }
        }

        internal static string AppLogFile
        {
            get { return Path.Combine(Dir, "matchpill.log"); }
        }

        internal static string RequestLogFile
        {
            get { return Path.Combine(Dir, "requestlog.txt"); }
        }

        internal static string NextFile
        {
            get { return Path.Combine(Dir, "next.json"); }
        }

        internal static string AlertsFile
        {
            get { return Path.Combine(Dir, "alerts.json"); }
        }

        // Written when the user picks Exit, so the watchdog can tell "stopped on purpose"
        // from "died". Deleted the moment the app starts again.
        internal static string StopFlagFile
        {
            get { return Path.Combine(Dir, "stopped.flag"); }
        }

        internal static void Init()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = exeDir;

            if (!IsWritable(exeDir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MatchPill");
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
            }

            Dir = dir;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".matchpill-write-probe.tmp");
                File.WriteAllText(probe, "probe", Encoding.UTF8);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();

        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(Paths.AppLogFile,
                        string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff}  {1}{2}",
                            DateTime.Now, message, Environment.NewLine),
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        internal static void Trim()
        {
            try
            {
                if (!File.Exists(Paths.AppLogFile))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(Paths.AppLogFile, Encoding.UTF8);
                if (lines.Length <= 400)
                {
                    return;
                }

                var keep = new List<string>();
                for (int i = lines.Length - 200; i < lines.Length; i++)
                {
                    keep.Add(lines[i]);
                }

                File.WriteAllLines(Paths.AppLogFile, keep.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    internal static class RequestLog
    {
        private const int MaxLines = 200;
        private static readonly object Sync = new object();
        private static readonly List<string> Lines = new List<string>();

        internal static void Add(string purpose, string url, int status, long elapsedMs, int bytes)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}Z  {1,-9}  HTTP {2,-3}  {3,6} ms  {4,7} B  {5}",
                DateTime.UtcNow, purpose, status, elapsedMs, bytes, url);

            lock (Sync)
            {
                Lines.Add(line);
                while (Lines.Count > MaxLines)
                {
                    Lines.RemoveAt(0);
                }
            }

            try
            {
                File.AppendAllText(Paths.RequestLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        internal static string[] Snapshot()
        {
            lock (Sync)
            {
                return Lines.ToArray();
            }
        }

        internal static void TrimFile()
        {
            try
            {
                if (!File.Exists(Paths.RequestLogFile))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(Paths.RequestLogFile, Encoding.UTF8);
                if (lines.Length <= MaxLines)
                {
                    return;
                }

                var keep = new List<string>();
                for (int i = lines.Length - MaxLines; i < lines.Length; i++)
                {
                    keep.Add(lines[i]);
                }

                File.WriteAllLines(Paths.RequestLogFile, keep.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Domain model
    // ----------------------------------------------------------------------------------------
    internal enum MatchState
    {
        Scheduled = 0,
        Live = 1,
        Paused = 2,
        Finished = 3,
        Postponed = 4,
        Cancelled = 5,
        Suspended = 6,
        Unknown = 7
    }

    internal sealed class GoalEvent
    {
        internal int Minute;                       // -1 when the goal happened before we started watching
        internal string MinuteLabel = "";          // provider wording such as "45+2'", wins over Minute
        internal string Player = "";               // empty unless a scorer source answered
        internal int HomeAfter;
        internal int AwayAfter;
        internal bool HomeScored;

        internal JVal ToJson()
        {
            JVal o = JVal.Obj();
            o.Set("m", JVal.Num(Minute));
            o.Set("ml", JVal.Str(MinuteLabel));
            o.Set("p", JVal.Str(Player));
            o.Set("h", JVal.Num(HomeAfter));
            o.Set("a", JVal.Num(AwayAfter));
            o.Set("hs", JVal.Bool(HomeScored));
            return o;
        }

        internal static GoalEvent FromJson(JVal o)
        {
            if (o == null || o.K != JVal.Kind.Object)
            {
                return null;
            }

            var e = new GoalEvent();
            e.Minute = o.GetInt("m", -1);
            e.MinuteLabel = o.GetString("ml", "");
            e.Player = o.GetString("p", "");
            e.HomeAfter = o.GetInt("h", 0);
            e.AwayAfter = o.GetInt("a", 0);
            e.HomeScored = o.GetBool("hs", false);
            return e;
        }
    }

    internal sealed class Fixture
    {
        internal string Id = "";
        internal string CompetitionCode = "";
        internal string CompetitionName = "";
        internal string HomeId = "";
        internal string HomeName = "";
        internal string HomeTla = "";
        internal string AwayId = "";
        internal string AwayName = "";
        internal string AwayTla = "";
        internal string HomeCrest = "";              // crest URL, drawn by the card, cached on disk
        internal string AwayCrest = "";
        internal string Venue = "";
        internal string Referee = "";
        internal string Note = "";                  // "Pens 4–3" for a cup tie decided on penalties
        internal int Matchday;
        internal int? HomeScore;
        internal int? AwayScore;
        internal DateTime KickoffUtc = DateTime.MinValue;
        internal MatchState State = MatchState.Unknown;
        internal string Minute = "";                // provider supplied, never computed locally
        internal int MinuteValue;                   // provider minute as a number, 0 when absent
        internal int? HalfTimeHome;
        internal int? HalfTimeAway;
        internal List<GoalEvent> Goals = new List<GoalEvent>();
        internal List<GoalEvent> Scorers = new List<GoalEvent>();   // real names, when a source answered
        internal DateTime ScorersFetchedUtc = DateTime.MinValue;
        internal DateTime GoalFlashUtc = DateTime.MinValue;
        internal DateTime LiveStartUtc = DateTime.MinValue;      // first moment we saw it in play
        internal DateTime SecondHalfStartUtc = DateTime.MinValue; // first moment we saw it back from HT
        internal string RawStatus = "";
        internal string PageUrl = "";               // empty when the provider has no fixture page

        // Trap #5: a feed that never flips to Finished must not stay "live" forever.
        internal bool IsLiveNow(DateTime nowUtc)
        {
            if (State != MatchState.Live && State != MatchState.Paused)
            {
                return false;
            }

            if (KickoffUtc == DateTime.MinValue)
            {
                return false;
            }

            return nowUtc >= KickoffUtc.AddMinutes(-10) && nowUtc <= KickoffUtc.AddHours(4);
        }

        internal bool HasScore
        {
            get { return HomeScore.HasValue && AwayScore.HasValue; }
        }

        internal string ScoreText
        {
            get
            {
                if (!HasScore)
                {
                    return "-";
                }

                return HomeScore.Value.ToString(CultureInfo.InvariantCulture) + "-" +
                       AwayScore.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal int TotalGoals
        {
            get
            {
                int home = HomeScore.HasValue ? HomeScore.Value : 0;
                int away = AwayScore.HasValue ? AwayScore.Value : 0;
                return home + away;
            }
        }

        // Trap #6: a goal is a change in the score pair, not a change in status.
        internal string Signature()
        {
            return Id + "|" + ((int)State).ToString(CultureInfo.InvariantCulture) + "|" +
                   (HomeScore.HasValue ? HomeScore.Value.ToString(CultureInfo.InvariantCulture) : "x") + "|" +
                   (AwayScore.HasValue ? AwayScore.Value.ToString(CultureInfo.InvariantCulture) : "x");
        }

        internal Fixture Clone()
        {
            var f = new Fixture();
            f.Id = Id;
            f.CompetitionCode = CompetitionCode;
            f.CompetitionName = CompetitionName;
            f.HomeId = HomeId;
            f.HomeName = HomeName;
            f.HomeTla = HomeTla;
            f.AwayId = AwayId;
            f.AwayName = AwayName;
            f.AwayTla = AwayTla;
            f.HomeCrest = HomeCrest;
            f.AwayCrest = AwayCrest;
            f.Venue = Venue;
            f.Referee = Referee;
            f.Note = Note;
            f.Matchday = Matchday;
            f.HomeScore = HomeScore;
            f.AwayScore = AwayScore;
            f.KickoffUtc = KickoffUtc;
            f.State = State;
            f.Minute = Minute;
            f.MinuteValue = MinuteValue;
            f.HalfTimeHome = HalfTimeHome;
            f.HalfTimeAway = HalfTimeAway;
            f.RawStatus = RawStatus;
            f.PageUrl = PageUrl;
            f.GoalFlashUtc = GoalFlashUtc;
            f.LiveStartUtc = LiveStartUtc;
            f.SecondHalfStartUtc = SecondHalfStartUtc;
            f.Goals = new List<GoalEvent>();
            for (int i = 0; i < Goals.Count; i++)
            {
                f.Goals.Add(Goals[i]);
            }

            f.Scorers = new List<GoalEvent>();
            for (int i = 0; i < Scorers.Count; i++)
            {
                f.Scorers.Add(Scorers[i]);
            }

            f.ScorersFetchedUtc = ScorersFetchedUtc;
            return f;
        }

        internal JVal ToJson()
        {
            JVal o = JVal.Obj();
            o.Set("id", JVal.Str(Id));
            o.Set("comp", JVal.Str(CompetitionCode));
            o.Set("compName", JVal.Str(CompetitionName));
            o.Set("homeId", JVal.Str(HomeId));
            o.Set("home", JVal.Str(HomeName));
            o.Set("homeTla", JVal.Str(HomeTla));
            o.Set("awayId", JVal.Str(AwayId));
            o.Set("away", JVal.Str(AwayName));
            o.Set("awayTla", JVal.Str(AwayTla));
            o.Set("hc", JVal.Str(HomeCrest));
            o.Set("ac", JVal.Str(AwayCrest));
            o.Set("venue", JVal.Str(Venue));
            o.Set("ref", JVal.Str(Referee));
            o.Set("note", JVal.Str(Note));
            o.Set("md", JVal.Num(Matchday));
            o.Set("hs", HomeScore.HasValue ? JVal.Num(HomeScore.Value) : JVal.Null());
            o.Set("as", AwayScore.HasValue ? JVal.Num(AwayScore.Value) : JVal.Null());
            o.Set("ko", JVal.Str(KickoffUtc.ToString("o", CultureInfo.InvariantCulture)));
            o.Set("st", JVal.Num((int)State));
            o.Set("min", JVal.Str(Minute));
            o.Set("minv", JVal.Num(MinuteValue));
            o.Set("hth", HalfTimeHome.HasValue ? JVal.Num(HalfTimeHome.Value) : JVal.Null());
            o.Set("hta", HalfTimeAway.HasValue ? JVal.Num(HalfTimeAway.Value) : JVal.Null());
            o.Set("raw", JVal.Str(RawStatus));
            o.Set("url", JVal.Str(PageUrl));
            o.Set("start", JVal.Str(LiveStartUtc == DateTime.MinValue
                ? ""
                : LiveStartUtc.ToString("o", CultureInfo.InvariantCulture)));
            o.Set("h2", JVal.Str(SecondHalfStartUtc == DateTime.MinValue
                ? ""
                : SecondHalfStartUtc.ToString("o", CultureInfo.InvariantCulture)));
            JVal goals = JVal.Arr();
            for (int i = 0; i < Goals.Count; i++)
            {
                goals.A.Add(Goals[i].ToJson());
            }

            o.Set("goals", goals);
            JVal scorers = JVal.Arr();
            for (int i = 0; i < Scorers.Count; i++)
            {
                scorers.A.Add(Scorers[i].ToJson());
            }

            o.Set("scorers", scorers);
            return o;
        }

        internal static Fixture FromJson(JVal o)
        {
            if (o == null || o.K != JVal.Kind.Object)
            {
                return null;
            }

            var f = new Fixture();
            f.Id = o.GetString("id", "");
            f.CompetitionCode = o.GetString("comp", "");
            f.CompetitionName = o.GetString("compName", f.CompetitionCode);
            f.HomeId = o.GetString("homeId", "");
            f.HomeName = o.GetString("home", "");
            f.HomeTla = o.GetString("homeTla", "");
            f.AwayId = o.GetString("awayId", "");
            f.AwayName = o.GetString("away", "");
            f.AwayTla = o.GetString("awayTla", "");
            f.HomeCrest = o.GetString("hc", "");
            f.AwayCrest = o.GetString("ac", "");
            f.Venue = o.GetString("venue", "");
            f.Referee = o.GetString("ref", "");
            f.Note = o.GetString("note", "");
            f.Matchday = o.GetInt("md", 0);
            f.HomeScore = o.GetNullableInt("hs");
            f.AwayScore = o.GetNullableInt("as");

            string ko = o.GetString("ko", "");
            DateTime parsed;
            if (DateTime.TryParse(ko, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
            {
                f.KickoffUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            }

            int st = o.GetInt("st", (int)MatchState.Unknown);
            f.State = (MatchState)st;
            f.Minute = o.GetString("min", "");
            f.MinuteValue = o.GetInt("minv", 0);
            f.HalfTimeHome = o.GetNullableInt("hth");
            f.HalfTimeAway = o.GetNullableInt("hta");
            f.RawStatus = o.GetString("raw", "");
            f.PageUrl = o.GetString("url", "");
            f.LiveStartUtc = ParseUtc(o.GetString("start", ""));
            f.SecondHalfStartUtc = ParseUtc(o.GetString("h2", ""));

            List<JVal> goals = o.GetArray("goals");
            if (goals != null)
            {
                for (int i = 0; i < goals.Count; i++)
                {
                    GoalEvent e = GoalEvent.FromJson(goals[i]);
                    if (e != null)
                    {
                        f.Goals.Add(e);
                    }
                }
            }

            List<JVal> scorers = o.GetArray("scorers");
            if (scorers != null)
            {
                for (int i = 0; i < scorers.Count; i++)
                {
                    GoalEvent e = GoalEvent.FromJson(scorers[i]);
                    if (e != null)
                    {
                        f.Scorers.Add(e);
                    }
                }
            }

            return f;
        }

        private static DateTime ParseUtc(string text)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            }

            return DateTime.MinValue;
        }
    }

    internal sealed class TeamResult
    {
        internal string Id = "";
        internal string Name = "";
        internal string CompetitionCode = "";

        internal JVal ToJson()
        {
            JVal o = JVal.Obj();
            o.Set("id", JVal.Str(Id));
            o.Set("name", JVal.Str(Name));
            o.Set("comp", JVal.Str(CompetitionCode));
            return o;
        }

        internal static TeamResult FromJson(JVal o)
        {
            if (o == null)
            {
                return null;
            }

            var t = new TeamResult();
            t.Id = o.GetString("id", "");
            t.Name = o.GetString("name", "");
            t.CompetitionCode = o.GetString("comp", "");
            return t;
        }
    }

    internal sealed class FavoriteTeam
    {
        internal string Id = "";
        internal string Name = "";
        internal string CompetitionCode = "";

        internal JVal ToJson()
        {
            JVal o = JVal.Obj();
            o.Set("id", JVal.Str(Id));
            o.Set("name", JVal.Str(Name));
            o.Set("competition", JVal.Str(CompetitionCode));
            return o;
        }

        internal static FavoriteTeam FromJson(JVal o)
        {
            if (o == null)
            {
                return null;
            }

            var t = new FavoriteTeam();
            t.Id = o.GetString("id", "");
            t.Name = o.GetString("name", "");
            t.CompetitionCode = o.GetString("competition", "");
            return t;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Configuration (portable, lives next to the exe)
    // ----------------------------------------------------------------------------------------
    internal sealed class AppConfig
    {
        internal string Provider = "mock";          // "footballdata" | "mock" | "mock-ratelimited"
        internal string ApiKey = "";
        internal string ApiBaseUrl = "https://api.football-data.org/v4/";
        internal List<FavoriteTeam> Favorites = new List<FavoriteTeam>();
        internal List<string> Competitions = new List<string>(DefaultCompetitions);
        internal bool Collapsed = true;             // collapsed pill is the first-run default
        internal int OpacityPercent = 100;
        internal bool WizardDone = false;
        internal bool ListExpanded;                 // "all matches" vs the first three rows
        internal bool LiveScopeAll = true;          // live matches from any followed competition
        internal bool AlwaysOnTop;                  // float over other windows instead of the desktop
        internal bool NextView;                     // look ahead: scheduled fixtures, no results
        internal bool UseEspnScorers = true;        // player names come from ESPN, not from the feed
        internal bool Cups = true;                  // cup rounds the feed does not carry, from ESPN
        internal int RefreshSeconds = 2;            // how often the card asks the provider for news
        internal int ApiCallsPerMinute = 10;        // the provider's published quota; the gate respects it
        internal bool NotifyBeforeMatch = true;     // one balloon, minutes before a favourite kicks off
        internal int NotifyMinutes = 30;            // how early that balloon goes out
        internal string Monitor = "";
        internal Rectangle ExpandedBounds = Rectangle.Empty;
        internal Rectangle CollapsedBounds = Rectangle.Empty;

        internal static readonly string[] DefaultCompetitions = new string[]
        {
            "PL", "PD", "SA", "BL1", "FL1", "CL"
        };

        internal bool HasKey
        {
            get { return !string.IsNullOrEmpty(ApiKey) && ApiKey.Trim().Length > 0; }
        }

        // What the poll actually asks for. A favourite in La Liga or Ligue 1 must never be
        // invisible just because the config still says "CL", so the configured list is unioned
        // with every favourite's league and with the standard set. Competitions are comma-joined
        // into ONE request, so breadth costs no quota - the view still filters to my teams.
        // (PSG is stored as a CL team because it was added from the CL list, yet its league
        //  matches are Ligue 1: this is exactly the hole that breadth closes.)
        internal List<string> EffectiveCompetitions()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Competitions.Count; i++)
            {
                string code = Competitions[i] == null ? "" : Competitions[i].Trim();
                if (code.Length > 0 && seen.Add(code))
                {
                    list.Add(code);
                }
            }

            for (int i = 0; i < Favorites.Count; i++)
            {
                string code = Favorites[i].CompetitionCode == null ? "" : Favorites[i].CompetitionCode.Trim();
                if (code.Length > 0 && seen.Add(code))
                {
                    list.Add(code);
                }
            }

            for (int i = 0; i < DefaultCompetitions.Length; i++)
            {
                if (seen.Add(DefaultCompetitions[i]))
                {
                    list.Add(DefaultCompetitions[i]);
                }
            }

            return list;
        }

        internal static AppConfig Load(string path)
        {
            var cfg = new AppConfig();
            try
            {
                if (!File.Exists(path))
                {
                    return cfg;
                }

                JVal root = JVal.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (root == null || root.K != JVal.Kind.Object)
                {
                    return cfg;
                }

                cfg.Provider = root.GetString("provider", cfg.Provider);
                cfg.ApiKey = root.GetString("apiKey", "");
                string baseUrl = root.GetString("apiBaseUrl", cfg.ApiBaseUrl);
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    cfg.ApiBaseUrl = baseUrl;
                }

                cfg.Collapsed = root.GetBool("collapsed", true);
                cfg.OpacityPercent = root.GetInt("opacity", 100);
                cfg.WizardDone = root.GetBool("wizardDone", false);
                cfg.ListExpanded = root.GetBool("listExpanded", false);
                cfg.UseEspnScorers = root.GetBool("scorers", true);
                cfg.Cups = root.GetBool("cups", true);
                cfg.RefreshSeconds = root.GetInt("refreshSeconds", 2);
                cfg.ApiCallsPerMinute = root.GetInt("apiCallsPerMinute", 10);
                cfg.LiveScopeAll = root.GetBool("liveAll", true);
                cfg.AlwaysOnTop = root.GetBool("onTop", false);
                cfg.NextView = root.GetBool("nextView", false);
                cfg.NotifyBeforeMatch = root.GetBool("notify", true);
                cfg.NotifyMinutes = root.GetInt("notifyMinutes", 30);
                cfg.Monitor = root.GetString("monitor", "");

                cfg.Competitions = new List<string>();
                List<JVal> comps = root.GetArray("competitions");
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i] != null && comps[i].K == JVal.Kind.String && comps[i].S.Length > 0)
                        {
                            cfg.Competitions.Add(comps[i].S);
                        }
                    }
                }

                if (cfg.Competitions.Count == 0)
                {
                    cfg.Competitions = new List<string>(DefaultCompetitions);
                }

                cfg.Favorites = new List<FavoriteTeam>();
                List<JVal> favs = root.GetArray("favorites");
                if (favs != null)
                {
                    for (int i = 0; i < favs.Count; i++)
                    {
                        FavoriteTeam t = FavoriteTeam.FromJson(favs[i]);
                        if (t != null && t.Id.Length > 0)
                        {
                            cfg.Favorites.Add(t);
                        }
                    }
                }

                cfg.ExpandedBounds = ReadRect(root["expanded"]);
                cfg.CollapsedBounds = ReadRect(root["collapsedBounds"]);

                if (cfg.OpacityPercent < 40)
                {
                    cfg.OpacityPercent = 40;
                }

                if (cfg.OpacityPercent > 100)
                {
                    cfg.OpacityPercent = 100;
                }

                if (cfg.RefreshSeconds < 1)
                {
                    cfg.RefreshSeconds = 1;
                }

                if (cfg.RefreshSeconds > 300)
                {
                    cfg.RefreshSeconds = 300;
                }

                if (cfg.ApiCallsPerMinute < 1)
                {
                    cfg.ApiCallsPerMinute = 1;
                }

                if (cfg.ApiCallsPerMinute > 600)
                {
                    cfg.ApiCallsPerMinute = 600;
                }
            }
            catch (Exception ex)
            {
                Log.Write("config load failed: " + ex.Message);
            }

            return cfg;
        }

        private static Rectangle ReadRect(JVal o)
        {
            if (o == null || o.K != JVal.Kind.Object)
            {
                return Rectangle.Empty;
            }

            return new Rectangle(o.GetInt("x", 0), o.GetInt("y", 0), o.GetInt("w", 0), o.GetInt("h", 0));
        }

        private static JVal WriteRect(Rectangle r)
        {
            JVal o = JVal.Obj();
            o.Set("x", JVal.Num(r.X));
            o.Set("y", JVal.Num(r.Y));
            o.Set("w", JVal.Num(r.Width));
            o.Set("h", JVal.Num(r.Height));
            return o;
        }

        internal void Save(string path)
        {
            try
            {
                JVal root = JVal.Obj();
                root.Set("version", JVal.Num(1));
                root.Set("provider", JVal.Str(Provider));
                root.Set("apiKey", JVal.Str(ApiKey));
                root.Set("apiBaseUrl", JVal.Str(ApiBaseUrl));

                JVal comps = JVal.Arr();
                for (int i = 0; i < Competitions.Count; i++)
                {
                    comps.A.Add(JVal.Str(Competitions[i]));
                }

                root.Set("competitions", comps);

                JVal favs = JVal.Arr();
                for (int i = 0; i < Favorites.Count; i++)
                {
                    favs.A.Add(Favorites[i].ToJson());
                }

                root.Set("favorites", favs);
                root.Set("collapsed", JVal.Bool(Collapsed));
                root.Set("opacity", JVal.Num(OpacityPercent));
                root.Set("wizardDone", JVal.Bool(WizardDone));
                root.Set("listExpanded", JVal.Bool(ListExpanded));
                root.Set("scorers", JVal.Bool(UseEspnScorers));
                root.Set("cups", JVal.Bool(Cups));
                root.Set("refreshSeconds", JVal.Num(RefreshSeconds));
                root.Set("apiCallsPerMinute", JVal.Num(ApiCallsPerMinute));
                root.Set("liveAll", JVal.Bool(LiveScopeAll));
                root.Set("onTop", JVal.Bool(AlwaysOnTop));
                root.Set("nextView", JVal.Bool(NextView));
                root.Set("notify", JVal.Bool(NotifyBeforeMatch));
                root.Set("notifyMinutes", JVal.Num(NotifyMinutes));
                root.Set("monitor", JVal.Str(Monitor));
                root.Set("expanded", WriteRect(ExpandedBounds));
                root.Set("collapsedBounds", WriteRect(CollapsedBounds));

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, root.ToJson(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Write("config save failed: " + ex.Message);
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Last good payload cache (cold start paints real data)
    // ----------------------------------------------------------------------------------------
    internal sealed class CachedPayload
    {
        internal DateTime SavedUtc = DateTime.MinValue;
        internal List<Fixture> Fixtures = new List<Fixture>();

        internal static CachedPayload Load(string path)
        {
            var cache = new CachedPayload();
            try
            {
                if (!File.Exists(path))
                {
                    return cache;
                }

                JVal root = JVal.Parse(File.ReadAllText(path, Encoding.UTF8));
                string saved = root.GetString("savedUtc", "");
                DateTime parsed;
                if (DateTime.TryParse(saved, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out parsed))
                {
                    cache.SavedUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
                }

                List<JVal> items = root.GetArray("fixtures");
                if (items != null)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        Fixture f = Fixture.FromJson(items[i]);
                        if (f != null && f.Id.Length > 0)
                        {
                            cache.Fixtures.Add(f);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("cache load failed: " + ex.Message);
            }

            return cache;
        }

        internal void Save(string path)
        {
            try
            {
                JVal root = JVal.Obj();
                root.Set("savedUtc", JVal.Str(SavedUtc.ToString("o", CultureInfo.InvariantCulture)));
                JVal arr = JVal.Arr();
                for (int i = 0; i < Fixtures.Count; i++)
                {
                    arr.A.Add(Fixtures[i].ToJson());
                }

                root.Set("fixtures", arr);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, root.ToJson(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Write("cache save failed: " + ex.Message);
            }
        }
    }
    // ----------------------------------------------------------------------------------------
    //  Provider abstraction - the UI never talks to a network API directly
    // ----------------------------------------------------------------------------------------
    internal interface IScoreProvider
    {
        string DisplayName { get; }
        string Attribution { get; }

        // One request, one poll. Returns the provider's current window: in-play matches plus
        // today's fixtures for the followed competitions. The network budget is one request per
        // poll, so the window has to arrive in a single call.
        Task<List<Fixture>> GetLiveFixturesAsync(CancellationToken ct);

        Task<List<Fixture>> GetTeamFixturesAsync(string teamId, DateTime fromUtc, DateTime toUtc,
            CancellationToken ct);

        Task<List<TeamResult>> SearchTeamAsync(string query, CancellationToken ct);
    }

    internal sealed class ProviderException : Exception
    {
        internal readonly int StatusCode;

        internal ProviderException(string message, int statusCode, Exception inner)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }

    internal sealed class RateLimitedException : Exception
    {
        internal readonly int RetryAfterSeconds;

        internal RateLimitedException(string message, int retryAfterSeconds)
            : base(message)
        {
            RetryAfterSeconds = retryAfterSeconds;
        }
    }

    // Trap #2: every provider speaks its own status dialect. Map once, here.
    internal static class StatusMapper
    {
        internal static MatchState Map(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return MatchState.Unknown;
            }

            switch (raw.Trim().ToUpperInvariant())
            {
                case "SCHEDULED":
                case "TIMED":
                case "NOT_STARTED":
                case "NS":
                case "UPCOMING":
                    return MatchState.Scheduled;
                case "IN_PLAY":
                case "INPLAY":
                case "LIVE":
                case "1H":
                case "2H":
                case "IN_PROGRESS":
                case "INPROGRESS":
                    return MatchState.Live;
                case "PAUSED":
                case "HT":
                case "HALFTIME":
                case "HALF_TIME":
                    return MatchState.Paused;
                case "FINISHED":
                case "FT":
                case "ENDED":
                case "AWARDED":
                    return MatchState.Finished;
                case "POSTPONED":
                case "PST":
                    return MatchState.Postponed;
                case "CANCELLED":
                case "CANCELED":
                case "CANC":
                    return MatchState.Cancelled;
                case "SUSPENDED":
                case "SUSP":
                case "INT":
                    return MatchState.Suspended;
                default:
                    return MatchState.Unknown;
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  football-data.org v4 (free tier, X-Auth-Token header)
    // ----------------------------------------------------------------------------------------
    internal sealed class FootballDataOrgProvider : IScoreProvider
    {
        private const string DefaultBaseUrl = "https://api.football-data.org/v4/";

        private readonly string _token;
        private readonly string _baseUrl;
        private readonly List<string> _competitions;
        private readonly Dictionary<string, List<TeamResult>> _teamCache =
            new Dictionary<string, List<TeamResult>>(StringComparer.OrdinalIgnoreCase);

        internal FootballDataOrgProvider(string token, string baseUrl, List<string> competitions)
        {
            _token = token == null ? "" : token.Trim();
            string root = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            if (!root.EndsWith("/", StringComparison.Ordinal))
            {
                root = root + "/";
            }

            _baseUrl = root;
            _competitions = new List<string>();
            if (competitions != null)
            {
                for (int i = 0; i < competitions.Count; i++)
                {
                    if (!string.IsNullOrEmpty(competitions[i]))
                    {
                        _competitions.Add(competitions[i].Trim());
                    }
                }
            }

            if (_competitions.Count == 0)
            {
                _competitions.AddRange(AppConfig.DefaultCompetitions);
            }
        }

        public string DisplayName
        {
            get { return "football-data.org"; }
        }

        public string Attribution
        {
            get { return "Football data provided by football-data.org"; }
        }

        public Task<List<Fixture>> GetLiveFixturesAsync(CancellationToken ct)
        {
            return Task.Run(delegate { return FetchWindow(ct); }, ct);
        }

        public Task<List<Fixture>> GetTeamFixturesAsync(string teamId, DateTime fromUtc, DateTime toUtc,
            CancellationToken ct)
        {
            return Task.Run(delegate
            {
                var list = new List<Fixture>();
                if (string.IsNullOrEmpty(teamId))
                {
                    return list;
                }

                string url = _baseUrl + "teams/" + Uri.EscapeDataString(teamId) + "/matches?dateFrom=" +
                             fromUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "&dateTo=" +
                             toUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                JVal json = GetJson(url, "team", ct);
                List<JVal> matches = json.GetArray("matches");
                if (matches != null)
                {
                    for (int i = 0; i < matches.Count; i++)
                    {
                        Fixture f = MapFixture(matches[i]);
                        if (f != null)
                        {
                            list.Add(f);
                        }
                    }
                }

                return list;
            }, ct);
        }

        // football-data.org's free tier has no global team search endpoint, so we search the team
        // lists of the followed competitions (each list is fetched at most once per process).
        public Task<List<TeamResult>> SearchTeamAsync(string query, CancellationToken ct)
        {
            return Task.Run(delegate { return SearchTeams(query, ct); }, ct);
        }

        private List<TeamResult> SearchTeams(string query, CancellationToken ct)
        {
            var results = new List<TeamResult>();
            string q = query == null ? "" : query.Trim();
            if (q.Length < 2)
            {
                return results;
            }

            int lookups = 0;
            for (int i = 0; i < _competitions.Count && lookups < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                string code = _competitions[i];

                List<TeamResult> teams;
                if (!_teamCache.TryGetValue(code, out teams))
                {
                    try
                    {
                        teams = FetchCompetitionTeams(code, ct);
                    }
                    catch (RateLimitedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("team list for " + code + " failed: " + ex.Message);
                        teams = new List<TeamResult>();
                    }

                    _teamCache[code] = teams;
                }

                lookups++;
                for (int t = 0; t < teams.Count; t++)
                {
                    TeamResult candidate = teams[t];
                    if (candidate.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    bool duplicate = false;
                    for (int r = 0; r < results.Count; r++)
                    {
                        if (results[r].Id == candidate.Id)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        results.Add(candidate);
                    }

                    if (results.Count >= 20)
                    {
                        return results;
                    }
                }
            }

            return results;
        }

        private List<TeamResult> FetchCompetitionTeams(string code, CancellationToken ct)
        {
            var list = new List<TeamResult>();
            JVal json = GetJson(_baseUrl + "competitions/" + Uri.EscapeDataString(code) + "/teams", "teams", ct);
            List<JVal> teams = json.GetArray("teams");
            if (teams == null)
            {
                return list;
            }

            for (int i = 0; i < teams.Count; i++)
            {
                JVal t = teams[i];
                if (t == null || t.K != JVal.Kind.Object)
                {
                    continue;
                }

                var tr = new TeamResult();
                tr.Id = t.GetInt("id", 0).ToString(CultureInfo.InvariantCulture);
                tr.Name = FirstNonEmpty(t.GetString("name", ""), t.GetString("shortName", ""),
                    t.GetString("tla", ""));
                tr.CompetitionCode = code;
                if (tr.Id != "0" && tr.Name.Length > 0)
                {
                    list.Add(tr);
                }
            }

            return list;
        }

        private List<Fixture> FetchWindow(CancellationToken ct)
        {
            var list = new List<Fixture>();
            string from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string url = _baseUrl + "matches?competitions=" + string.Join(",", _competitions.ToArray()) +
                         "&dateFrom=" + from + "&dateTo=" + to;

            JVal json = GetJson(url, "matches", ct);
            List<JVal> matches = json.GetArray("matches");
            if (matches == null)
            {
                return list;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                Fixture f = MapFixture(matches[i]);
                if (f != null)
                {
                    list.Add(f);
                }
            }

            return list;
        }

        private JVal GetJson(string url, string purpose, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers["X-Auth-Token"] = _token;
            request.UserAgent = "MatchPill/1.0 (Windows NT 10.0; .NET 4.8)";
            request.Accept = "application/json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.KeepAlive = true;                       // one connection, reused across polls
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (ct.Register(delegate
                   {
                       try
                       {
                           request.Abort();
                       }
                       catch
                       {
                       }
                   }))
            {
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        string body;
                        using (Stream stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            body = reader.ReadToEnd();
                        }

                        RequestLog.Add(purpose, url, (int)response.StatusCode, watch.ElapsedMilliseconds,
                            body.Length);
                        return JVal.Parse(body);
                    }
                }
                catch (WebException ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }

                    var http = ex.Response as HttpWebResponse;
                    int status = http != null ? (int)http.StatusCode : 0;
                    int retryAfter = 0;
                    string body = "";

                    if (http != null)
                    {
                        try
                        {
                            string header = http.Headers["Retry-After"];
                            if (!string.IsNullOrEmpty(header))
                            {
                                int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out retryAfter);
                            }

                            using (Stream stream = http.GetResponseStream())
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                body = reader.ReadToEnd();
                            }
                        }
                        catch
                        {
                        }
                        finally
                        {
                            http.Dispose();
                        }
                    }

                    RequestLog.Add(purpose + "!", url, status, watch.ElapsedMilliseconds, body.Length);

                    if (status == 429)
                    {
                        throw new RateLimitedException("HTTP 429 - provider asked us to slow down", retryAfter);
                    }

                    if (status == 0)
                    {
                        throw new ProviderException("Network unavailable: " + ex.Message, 0, ex);
                    }

                    throw new ProviderException(
                        "HTTP " + status.ToString(CultureInfo.InvariantCulture) + " from provider", status, ex);
                }
            }
        }

        internal static Fixture MapFixture(JVal m)
        {
            if (m == null || m.K != JVal.Kind.Object)
            {
                return null;
            }

            var f = new Fixture();
            f.Id = m.GetInt("id", 0).ToString(CultureInfo.InvariantCulture);
            if (f.Id == "0")
            {
                return null;                                // trap #4: no id, no dedupe, no display
            }

            JVal comp = m["competition"];
            if (comp != null)
            {
                f.CompetitionCode = comp.GetString("code", "");
                f.CompetitionName = FirstNonEmpty(comp.GetString("name", ""), f.CompetitionCode, "Competition");
            }

            JVal home = m["homeTeam"];
            if (home != null)
            {
                f.HomeId = home.GetInt("id", 0).ToString(CultureInfo.InvariantCulture);
                f.HomeName = FirstNonEmpty(home.GetString("shortName", ""), home.GetString("name", ""), "Home");
                f.HomeTla = home.GetString("tla", "");
                f.HomeCrest = home.GetString("crest", "");
            }

            JVal away = m["awayTeam"];
            if (away != null)
            {
                f.AwayId = away.GetInt("id", 0).ToString(CultureInfo.InvariantCulture);
                f.AwayName = FirstNonEmpty(away.GetString("shortName", ""), away.GetString("name", ""), "Away");
                f.AwayTla = away.GetString("tla", "");
                f.AwayCrest = away.GetString("crest", "");
            }

            string utc = m.GetString("utcDate", "");
            DateTime kickoff;
            if (DateTime.TryParse(utc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out kickoff))
            {
                f.KickoffUtc = kickoff.Kind == DateTimeKind.Utc ? kickoff : kickoff.ToUniversalTime();
            }
            else
            {
                f.KickoffUtc = DateTime.UtcNow;
            }

            f.RawStatus = m.GetString("status", "");
            f.State = StatusMapper.Map(f.RawStatus);

            // free-tier extras: venue, matchday and the referee crew are all in the same payload
            f.Venue = m.GetString("venue", "");
            f.Matchday = m.GetInt("matchday", 0);
            JVal referees = m["referees"];
            if (referees != null && referees.K == JVal.Kind.Array)
            {
                for (int i = 0; i < referees.A.Count; i++)
                {
                    JVal row = referees.A[i];
                    if (row == null)
                    {
                        continue;
                    }

                    string type = row.GetString("type", "");
                    string who = row.GetString("name", "");
                    if (who.Length == 0)
                    {
                        continue;
                    }

                    if (f.Referee.Length == 0 || string.Equals(type, "REFEREE", StringComparison.Ordinal))
                    {
                        f.Referee = who;
                    }

                    if (string.Equals(type, "REFEREE", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }

            int? minute = m.GetNullableInt("minute");
            if (minute.HasValue && minute.Value > 0)
            {
                f.MinuteValue = minute.Value;
                f.Minute = minute.Value.ToString(CultureInfo.InvariantCulture) + "'";
            }

            JVal score = m["score"];
            if (score != null)
            {
                JVal fullTime = score["fullTime"];
                if (fullTime != null)
                {
                    f.HomeScore = fullTime.GetNullableInt("home");
                    f.AwayScore = fullTime.GetNullableInt("away");
                }

                JVal halfTime = score["halfTime"];
                if (halfTime != null)
                {
                    f.HalfTimeHome = halfTime.GetNullableInt("home");
                    f.HalfTimeAway = halfTime.GetNullableInt("away");
                }
            }

            return f;
        }

        private static string FirstNonEmpty(string a, string b, string c)
        {
            if (!string.IsNullOrEmpty(a))
            {
                return a;
            }

            if (!string.IsNullOrEmpty(b))
            {
                return b;
            }

            return string.IsNullOrEmpty(c) ? "" : c;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  MockProvider - hardcoded fixtures on a fake clock. No key, no network, full UI.
    // ----------------------------------------------------------------------------------------
    internal sealed class MockProvider : IScoreProvider
    {
        private readonly DateTime _anchorUtc;
        private readonly bool _alwaysRateLimited;

        internal MockProvider()
            : this(false)
        {
        }

        internal MockProvider(bool alwaysRateLimited)
        {
            _anchorUtc = DateTime.UtcNow;
            _alwaysRateLimited = alwaysRateLimited;
        }

        public string DisplayName
        {
            get { return "MockProvider (offline demo)"; }
        }

        public string Attribution
        {
            get { return ""; }
        }

        public Task<List<Fixture>> GetLiveFixturesAsync(CancellationToken ct)
        {
            return Task.Run(delegate
            {
                var watch = Stopwatch.StartNew();
                Thread.Sleep(80);                           // stand-in for network latency
                RequestLog.Add("mock", "mock://fixtures", 200, watch.ElapsedMilliseconds, 0);
                if (_alwaysRateLimited)
                {
                    throw new RateLimitedException("HTTP 429 (mock)", 2);
                }

                return Build();
            }, ct);
        }

        public Task<List<Fixture>> GetTeamFixturesAsync(string teamId, DateTime fromUtc, DateTime toUtc,
            CancellationToken ct)
        {
            return Task.Run(delegate
            {
                var list = new List<Fixture>();
                List<Fixture> all = Build();
                for (int i = 0; i < all.Count; i++)
                {
                    Fixture f = all[i];
                    if (f.HomeId == teamId || f.AwayId == teamId)
                    {
                        if (f.KickoffUtc >= fromUtc.AddDays(-1) && f.KickoffUtc <= toUtc.AddDays(1))
                        {
                            list.Add(f);
                        }
                    }
                }

                return list;
            }, ct);
        }

        public Task<List<TeamResult>> SearchTeamAsync(string query, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                var results = new List<TeamResult>();
                string q = query == null ? "" : query.Trim();
                if (q.Length == 0)
                {
                    return results;
                }

                List<Fixture> all = Build();
                for (int i = 0; i < all.Count; i++)
                {
                    AddCandidate(results, all[i].HomeId, all[i].HomeName, all[i].CompetitionCode, q);
                    AddCandidate(results, all[i].AwayId, all[i].AwayName, all[i].CompetitionCode, q);
                }

                return results;
            }, ct);
        }

        private static void AddCandidate(List<TeamResult> results, string id, string name, string comp, string q)
        {
            if (name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Id == id)
                {
                    return;
                }
            }

            var t = new TeamResult();
            t.Id = id;
            t.Name = name;
            t.CompetitionCode = comp;
            results.Add(t);
        }

        private List<Fixture> Build()
        {
            DateTime now = DateTime.UtcNow;
            var list = new List<Fixture>();

            // The first match is on a fake clock that advances in real time: its minute grows and
            // goals land at fixed minutes, so the goal-detection path can be demoed in ~3 minutes.
            double elapsed = (now - _anchorUtc).TotalMinutes;
            int minute = 30 + (int)elapsed;
            if (minute > 90)
            {
                minute = 90;
            }

            int homeGoals = GoalsAt(minute, 33) + GoalsAt(minute, 52) + GoalsAt(minute, 74);
            int awayGoals = GoalsAt(minute, 41) + GoalsAt(minute, 63);
            bool stillPlaying = minute < 90;

            list.Add(New("mock-1", "PL", "Premier League", "57", "Arsenal", "61", "Chelsea",
                now.AddMinutes(-minute), stillPlaying ? MatchState.Live : MatchState.Finished,
                stillPlaying ? minute.ToString(CultureInfo.InvariantCulture) + "'" : "FT", homeGoals, awayGoals));

            list.Add(New("mock-2", "PD", "LaLiga", "86", "Real Madrid", "559", "Sevilla",
                now.AddMinutes(-87), MatchState.Live, "87'", 2, 1));

            list.Add(New("mock-3", "DED", "Eredivisie", "678", "Ajax", "674", "PSV",
                now.AddMinutes(-47), MatchState.Paused, "HT", 1, 1));

            // Kicks off in 25 minutes: exercises the "kickoff soon" 5-minute poll tier.
            list.Add(New("mock-4", "BL1", "Bundesliga", "5", "Bayern Munich", "4", "Borussia Dortmund",
                now.AddMinutes(25), MatchState.Scheduled, "", null, null));

            DateTime local1945 = DateTime.Today.AddHours(19).AddMinutes(45);
            list.Add(New("mock-5", "SA", "Serie A", "98", "Inter", "100", "AC Milan",
                local1945.ToUniversalTime(), MatchState.Scheduled, "", null, null));

            list.Add(New("mock-6", "FL1", "Ligue 1", "524", "Paris Saint-Germain", "523", "Lyon",
                now.AddHours(-3), MatchState.Finished, "FT", 3, 1));

            return list;
        }

        private static int GoalsAt(int minute, int at)
        {
            return minute >= at ? 1 : 0;
        }

        private static Fixture New(string id, string compCode, string compName, string homeId, string homeName,
            string awayId, string awayName, DateTime kickoffUtc, MatchState state, string minute, int? homeScore,
            int? awayScore)
        {
            var f = new Fixture();
            f.Id = id;
            f.CompetitionCode = compCode;
            f.CompetitionName = compName;
            f.HomeId = homeId;
            f.HomeName = homeName;
            f.AwayId = awayId;
            f.AwayName = awayName;
            f.KickoffUtc = kickoffUtc;
            f.State = state;
            f.Minute = minute;
            f.RawStatus = state.ToString().ToUpperInvariant();
            f.HomeScore = homeScore;
            f.AwayScore = awayScore;
            return f;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Poll engine - one background worker, adaptive interval, hard 30 s floor between requests
    // ----------------------------------------------------------------------------------------
    internal sealed class PollOutcome
    {
        internal bool Success;
        internal bool RateLimited;
        internal string Error = "";
        internal DateTime FetchedUtc = DateTime.MinValue;
        internal List<Fixture> Fixtures;
    }

    internal sealed class PollEngine : IDisposable
    {
        private readonly IScoreProvider _provider;
        private readonly Action<PollOutcome> _onResult;
        private readonly Func<DateTime, int> _intervalSelector;
        private readonly object _wakeLock = new object();

        // The provider publishes a hard quota (football-data.org: 10 requests a minute). The card
        // asks for news every couple of seconds; this gate is what turns that wish into a request
        // rate the key can actually survive - no 429 storms, no banned token.
        private readonly int _callsPerMinute;
        private readonly int _minGapMs;
        private readonly List<DateTime> _calls = new List<DateTime>();

        private TaskCompletionSource<bool> _wake = new TaskCompletionSource<bool>();
        private volatile bool _pendingWake;
        private CancellationTokenSource _cts;
        private Task _worker;
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private DateTime _backoffUntilUtc = DateTime.MinValue;
        private int _backoffMs;
        private bool _disposed;

        internal PollEngine(IScoreProvider provider, Func<DateTime, int> intervalSelector,
            Action<PollOutcome> onResult, int callsPerMinute)
        {
            _provider = provider;
            _intervalSelector = intervalSelector;
            _onResult = onResult;
            _callsPerMinute = callsPerMinute < 1 ? 1 : callsPerMinute;
            int gap = 60000 / _callsPerMinute + 150;
            _minGapMs = gap < 750 ? 750 : gap;
        }

        internal bool Paused { get; private set; }

        internal DateTime LastSuccessUtc { get; private set; }

        internal int LastDelayMs { get; private set; }

        internal string LastError { get; private set; }

        internal void Start()
        {
            if (_cts != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _worker = Task.Run(delegate { return LoopAsync(token); });
        }

        // Manual refresh, resume from sleep, network reconnect: fetch now (subject to the 30 s floor).
        internal void Wake()
        {
            TaskCompletionSource<bool> pending;
            lock (_wakeLock)
            {
                _pendingWake = true;
                pending = _wake;
                _wake = new TaskCompletionSource<bool>();
            }

            try
            {
                pending.TrySetResult(true);
            }
            catch
            {
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await FetchOnceAsync(ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    int delay = ComputeDelay();
                    LastDelayMs = delay;
                    await WaitAsync(delay, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Write("poll loop stopped: " + ex);
            }
        }

        private int ComputeDelay()
        {
            int backoffRemaining = (int)(_backoffUntilUtc - DateTime.UtcNow).TotalMilliseconds;
            if (backoffRemaining > 0)
            {
                return backoffRemaining;
            }

            int baseMs = _intervalSelector(DateTime.UtcNow);
            int budgetMs = MsUntilBudgetAllows();
            return budgetMs > baseMs ? budgetMs : baseMs;
        }

        // Rolling 60 s window: never more than _callsPerMinute requests, never closer than the
        // even gap that quota implies. Returns how long the next request has to wait.
        private int MsUntilBudgetAllows()
        {
            DateTime now = DateTime.UtcNow;
            for (int i = _calls.Count - 1; i >= 0; i--)
            {
                if ((now - _calls[i]).TotalSeconds >= 60)
                {
                    _calls.RemoveAt(i);
                }
            }

            int wait = 0;
            if (_calls.Count > 0)
            {
                int gapLeft = _minGapMs - (int)(now - _calls[_calls.Count - 1]).TotalMilliseconds;
                if (gapLeft > wait)
                {
                    wait = gapLeft;
                }
            }

            if (_calls.Count >= _callsPerMinute)
            {
                int windowLeft = 60000 - (int)(now - _calls[0]).TotalMilliseconds + 250;
                if (windowLeft > wait)
                {
                    wait = windowLeft;
                }
            }

            return wait < 0 ? 0 : wait;
        }

        private async Task WaitAsync(int ms, CancellationToken ct)
        {
            if (_pendingWake)
            {
                _pendingWake = false;
                return;
            }

            if (ms < 250)
            {
                ms = 250;
            }

            TaskCompletionSource<bool> pending;
            lock (_wakeLock)
            {
                pending = _wake;
            }

            await Task.WhenAny(Task.Delay(ms, ct), pending.Task).ConfigureAwait(false);
            _pendingWake = false;
        }

        private async Task FetchOnceAsync(CancellationToken ct)
        {
            if (DateTime.UtcNow < _backoffUntilUtc)
            {
                return;                                 // a manual refresh must not short-circuit a 429
            }

            int budgetWait = MsUntilBudgetAllows();
            if (budgetWait > 0)
            {
                await Task.Delay(budgetWait, ct).ConfigureAwait(false);   // a wake must not jump the quota
                if (ct.IsCancellationRequested)
                {
                    return;
                }
            }

            _lastRequestUtc = DateTime.UtcNow;
            _calls.Add(_lastRequestUtc);
            var outcome = new PollOutcome();

            try
            {
                List<Fixture> fixtures = await _provider.GetLiveFixturesAsync(ct).ConfigureAwait(false);
                LastSuccessUtc = DateTime.UtcNow;
                LastError = "";
                _backoffMs = 0;
                _backoffUntilUtc = DateTime.MinValue;
                Paused = false;
                outcome.Success = true;
                outcome.FetchedUtc = LastSuccessUtc;
                outcome.Fixtures = fixtures == null ? new List<Fixture>() : fixtures;
            }
            catch (RateLimitedException ex)
            {
                _backoffMs = NextBackoff(_backoffMs, ex.RetryAfterSeconds);
                _backoffUntilUtc = DateTime.UtcNow.AddMilliseconds(_backoffMs);
                Paused = true;
                LastError = ex.Message;
                outcome.RateLimited = true;
                outcome.Error = ex.Message;
                Log.Write("rate limited; next attempt in " + _backoffMs.ToString(CultureInfo.InvariantCulture) +
                          " ms");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                outcome.Error = ex.Message;
                Log.Write("fetch failed: " + ex.Message);
            }

            try
            {
                _onResult(outcome);
            }
            catch (Exception ex)
            {
                Log.Write("result handler failed: " + ex.Message);
            }
        }

        internal static int NextBackoff(int current, int retryAfterSeconds)
        {
            int next = current <= 0 ? 2000 : current * 2;
            if (retryAfterSeconds > 0)
            {
                int suggested = retryAfterSeconds * 1000;
                if (suggested > next)
                {
                    next = suggested;
                }
            }

            return next > 300000 ? 300000 : next;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }
            }
            catch
            {
            }

            try
            {
                if (_worker != null)
                {
                    _worker.Wait(2000);
                }
            }
            catch
            {
            }

            try
            {
                if (_cts != null)
                {
                    _cts.Dispose();
                }
            }
            catch
            {
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Match clock - the game time, derived from kickoff because the free feed has no minute
    // ----------------------------------------------------------------------------------------
    internal static class MatchClock
    {
        private const int HalfMinutes = 45;
        private const int BreakMinutes = 15;         // half time, not played
        private const int MaxMinutes = 120;
        private const int MaxStoppage = 15;          // "90+15" is as far as any clock goes

        // Seconds of football actually played, with the interval taken out of the count.
        internal static int PlaySeconds(Fixture f, DateTime nowUtc)
        {
            if (f == null || f.KickoffUtc == DateTime.MinValue)
            {
                return 0;
            }

            if (f.State == MatchState.Paused)
            {
                return HalfMinutes * 60;             // the clock is stopped at half time
            }

            DateTime anchor = AppController.EnsureUtc(f.KickoffUtc);
            if (f.LiveStartUtc != DateTime.MinValue && f.LiveStartUtc > anchor)
            {
                anchor = f.LiveStartUtc;             // the match started late: believe what we saw
            }

            if (f.SecondHalfStartUtc != DateTime.MinValue)
            {
                double second = HalfMinutes * 60 + (nowUtc - f.SecondHalfStartUtc).TotalSeconds;
                if (second < HalfMinutes * 60)
                {
                    second = HalfMinutes * 60;
                }

                if (second > MaxMinutes * 60)
                {
                    second = MaxMinutes * 60;
                }

                return (int)second;
            }

            double seconds = (nowUtc - anchor).TotalSeconds;
            if (seconds < 0)
            {
                return 0;
            }

            if (seconds > (HalfMinutes + BreakMinutes) * 60)
            {
                seconds -= BreakMinutes * 60;        // the 15 minute interval is not match time
            }

            if (seconds > MaxMinutes * 60)
            {
                seconds = MaxMinutes * 60;
            }

            return (int)seconds;
        }

        internal static int Minute(Fixture f, DateTime nowUtc)
        {
            int minutes = PlaySeconds(f, nowUtc) / 60;
            if (minutes < 1)
            {
                minutes = 1;                         // a live match is at least in its first minute
            }

            if (minutes > 90 + MaxStoppage)
            {
                minutes = 90 + MaxStoppage;          // a feed that never ends must not invent 118'
            }

            return minutes;
        }

        // "67:24" - the running clock. It advances on every 2 s tick, which is the whole point of
        // the fast refresh: you can see the game moving even when the score has not changed.
        internal static string Running(Fixture f, DateTime nowUtc)
        {
            int seconds = PlaySeconds(f, nowUtc);
            int minutes = seconds / 60;
            if (minutes > 90)
            {
                return "90+" + Stoppage(minutes).ToString(CultureInfo.InvariantCulture);
            }

            int rest = seconds % 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   rest.ToString("00", CultureInfo.InvariantCulture);
        }

        private static int Stoppage(int minutes)
        {
            int extra = minutes - 90;
            if (extra < 0)
            {
                extra = 0;
            }

            if (extra > MaxStoppage)
            {
                extra = MaxStoppage;
            }

            return extra;
        }

        // Badge text: the minute while live, HT/FT/PP otherwise, kickoff time when scheduled.
        internal static string Badge(Fixture f, DateTime nowUtc)
        {
            if (f == null)
            {
                return "";
            }

            switch (f.State)
            {
                case MatchState.Live:
                    if (f.MinuteValue > 0)
                    {
                        return f.MinuteValue.ToString(CultureInfo.InvariantCulture) + "'";
                    }

                    int minutes = Minute(f, nowUtc);
                    if (minutes > 90)
                    {
                        return "90+" + Stoppage(minutes).ToString(CultureInfo.InvariantCulture);
                    }

                    return minutes.ToString(CultureInfo.InvariantCulture) + "'";
                case MatchState.Paused:
                    return "HT";
                case MatchState.Finished:
                    return "FT";
                case MatchState.Postponed:
                    return "PP";
                case MatchState.Cancelled:
                    return "CANC";
                case MatchState.Suspended:
                    return "SUSP";
                case MatchState.Scheduled:
                    return FormatLocalTime(f.KickoffUtc);
                default:
                    return "?";
            }
        }

        internal static string FormatLocalTime(DateTime utc)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(AppController.EnsureUtc(utc), TimeZoneInfo.Local);
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // 0..1 progress through 90 minutes, for the thin bar under a live match.
        internal static float Progress(Fixture f, DateTime nowUtc)
        {
            int seconds = PlaySeconds(f, nowUtc);
            float value = seconds / (float)(90 * 60);
            if (value < 0f)
            {
                value = 0f;
            }

            if (value > 1f)
            {
                value = 1f;
            }

            return value;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Goal timeline - the feed gives a score, not a scorer list, so the timeline is built from
    //  score changes we actually witness, plus a seed for goals that went in before we started.
    // ----------------------------------------------------------------------------------------
    internal static class GoalMerge
    {
        internal static void Merge(List<Fixture> previous, List<Fixture> fresh, DateTime nowUtc)
        {
            if (fresh == null)
            {
                return;
            }

            var byId = new Dictionary<string, Fixture>(StringComparer.Ordinal);
            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    Fixture p = previous[i];
                    if (p != null && !string.IsNullOrEmpty(p.Id))
                    {
                        byId[p.Id] = p;
                    }
                }
            }

            for (int i = 0; i < fresh.Count; i++)
            {
                Fixture f = fresh[i];
                if (f == null || string.IsNullOrEmpty(f.Id))
                {
                    continue;
                }

                Fixture prev;
                if (!byId.TryGetValue(f.Id, out prev))
                {
                    Seed(f, nowUtc);
                    continue;
                }

                f.LiveStartUtc = prev.LiveStartUtc;
                f.SecondHalfStartUtc = prev.SecondHalfStartUtc;
                f.ScorersFetchedUtc = prev.ScorersFetchedUtc;
                f.Scorers = new List<GoalEvent>();
                for (int s2 = 0; s2 < prev.Scorers.Count; s2++)
                {
                    f.Scorers.Add(prev.Scorers[s2]);     // real scorer names survive a refresh
                }

                // Anchor the derived clock on what the feed actually told us, not on the schedule.
                DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
                if (f.State == MatchState.Live)
                {
                    bool justStarted = prev.State != MatchState.Live && prev.State != MatchState.Paused &&
                                       prev.State != MatchState.Finished;
                    if (f.LiveStartUtc == DateTime.MinValue && justStarted &&
                        nowUtc > kickoff && nowUtc - kickoff < TimeSpan.FromMinutes(30))
                    {
                        f.LiveStartUtc = nowUtc;     // it did not kick off on time; it does now
                    }
                }

                // only believe a half-time transition inside the window where it can happen,
                // otherwise a stale cache would rewind a match that is already an hour in
                if (prev.State == MatchState.Paused && f.State == MatchState.Live &&
                    f.SecondHalfStartUtc == DateTime.MinValue &&
                    nowUtc - kickoff < TimeSpan.FromMinutes(70))
                {
                    f.SecondHalfStartUtc = nowUtc;   // second half is under way, clock restarts at 45'
                }

                f.Goals = new List<GoalEvent>();
                for (int g = 0; g < prev.Goals.Count; g++)
                {
                    f.Goals.Add(prev.Goals[g]);
                }

                int beforeHome = prev.HomeScore.HasValue ? prev.HomeScore.Value : 0;
                int beforeAway = prev.AwayScore.HasValue ? prev.AwayScore.Value : 0;
                int nowHome = f.HomeScore.HasValue ? f.HomeScore.Value : 0;
                int nowAway = f.AwayScore.HasValue ? f.AwayScore.Value : 0;

                int addedHome = nowHome - beforeHome;
                int addedAway = nowAway - beforeAway;

                // the timeline must always account for exactly the goals on the board. A cache
                // written by an older build, a corrected score, or anything else that breaks that
                // invariant gets rebuilt from the score instead of silently drifting.
                bool consistent = f.Goals.Count == beforeHome + beforeAway;
                if (addedHome < 0 || addedAway < 0 || !consistent)
                {
                    f.Goals.Clear();
                    Seed(f, nowUtc);
                    continue;
                }

                if (addedHome == 0 && addedAway == 0)
                {
                    continue;
                }

                int minute = MatchClock.Minute(f, nowUtc);
                int runningHome = beforeHome;
                int runningAway = beforeAway;

                for (int g = 0; g < addedHome; g++)
                {
                    runningHome++;
                    f.Goals.Add(NewGoal(minute, runningHome, runningAway, true));
                }

                for (int g = 0; g < addedAway; g++)
                {
                    runningAway++;
                    f.Goals.Add(NewGoal(minute, runningHome, runningAway, false));
                }

                if (f.State == MatchState.Live || f.State == MatchState.Paused)
                {
                    f.GoalFlashUtc = nowUtc;         // the card flashes this match for a few seconds
                }
            }
        }

        private static GoalEvent NewGoal(int minute, int home, int away, bool homeScored)
        {
            var e = new GoalEvent();
            e.Minute = minute;
            e.HomeAfter = home;
            e.AwayAfter = away;
            e.HomeScored = homeScored;
            return e;
        }

        // First sight of a match that already has goals on the board: keep them, but never invent
        // a minute for them - the UI shows "-" instead.
        private static void Seed(Fixture f, DateTime nowUtc)
        {
            if (!f.HasScore || f.Goals.Count > 0)
            {
                return;
            }

            int home = f.HomeScore.Value;
            int away = f.AwayScore.Value;
            for (int i = 1; i <= home; i++)
            {
                f.Goals.Add(NewGoal(-1, i, 0, true));
            }

            for (int i = 1; i <= away; i++)
            {
                f.Goals.Add(NewGoal(-1, home, i, false));
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Scorer names - football-data.org's free tier has no goals array, so the player names come
    //  from ESPN's public site JSON. One scoreboard call and one summary call per match, cached,
    //  and only ever made for the match the user actually opened.
    // ----------------------------------------------------------------------------------------
    internal sealed class EspnScorers
    {
        private const string Base = "https://site.api.espn.com/apis/site/v2/sports/soccer/";

        // Words that carry no identifying weight, or that differ between the two feeds.
        private static readonly string[] Noise = new string[]
        {
            "fc", "afc", "cf", "sc", "ac", "club", "the", "de", "cd", "ss", "calcio", "as", "us",
            "sv", "vfl", "vfb", "tsg", "fsv", "ogc", "losc", "rc", "real"
        };

        // The handful of nicknames no string rule can bridge.
        private static readonly string[,] Aliases = new string[,]
        {
            { "psg", "paris" },
            { "hsv", "hamburg" },
            { "wolves", "wolverhampton" },
            { "spurs", "tottenham" },
            { "atleti", "atletico" },
            { "barca", "barcelona" },
            { "gladbach", "monchengladbach" },
            { "betis", "betis" },
            { "inter", "internazionale" },
            { "juve", "juventus" },
            { "bayern", "bayern" }
        };

        private readonly object _lock = new object();
        private readonly Dictionary<string, List<JVal>> _boards = new Dictionary<string, List<JVal>>();
        private readonly Dictionary<string, DateTime> _boardAt = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, List<GoalEvent>> _summaries =
            new Dictionary<string, List<GoalEvent>>();
        private readonly Dictionary<string, DateTime> _summaryAt = new Dictionary<string, DateTime>();

        internal static string LeagueFor(string competitionCode)
        {
            switch ((competitionCode == null ? "" : competitionCode.Trim()).ToUpperInvariant())
            {
                case "PL": return "eng.1";
                case "ELC": return "eng.2";
                case "PD": return "esp.1";
                case "SA": return "ita.1";
                case "BL1": return "ger.1";
                case "FL1": return "fra.1";
                case "CL": return "uefa.champions";
                case "DED": return "ned.1";
                case "PPL": return "por.1";
                case "BSA": return "bra.1";
                case "EC": return "uefa.euro";
                case "CLI": return "conmebol.libertadores";
                default: return null;
            }
        }

        // Returns the scoring plays for this fixture, or null when the source has nothing to say.
        internal List<GoalEvent> Get(Fixture f, CancellationToken ct)
        {
            if (f == null || f.KickoffUtc == DateTime.MinValue)
            {
                return null;
            }

            string league = LeagueFor(f.CompetitionCode);
            if (league == null)
            {
                return null;
            }

            DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);

            // ESPN groups its days in US Eastern, so a late kickoff can sit on the neighbouring
            // UTC date. Only the fallback dates cost an extra request, and only when needed.
            string[] dates = new string[]
            {
                kickoff.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                kickoff.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                kickoff.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            };

            for (int i = 0; i < dates.Length; i++)
            {
                List<JVal> events = Board(league, dates[i], ct);
                if (events == null || events.Count == 0)
                {
                    continue;
                }

                string eventId;
                string homeName;
                string homeAbbr;
                if (!Pick(events, f, out eventId, out homeName, out homeAbbr))
                {
                    continue;
                }

                return Summary(league, eventId, homeName, homeAbbr, ct);
            }

            return null;
        }

        // ------------------------------------------------------------------ scoreboard
        private List<JVal> Board(string league, string date, CancellationToken ct)
        {
            string key = league + "|" + date;
            lock (_lock)
            {
                DateTime at;
                if (_boardAt.TryGetValue(key, out at) && (DateTime.UtcNow - at).TotalSeconds < 300)
                {
                    List<JVal> hit;
                    if (_boards.TryGetValue(key, out hit))
                    {
                        return hit;
                    }
                }
            }

            var list = new List<JVal>();
            try
            {
                JVal json = FetchJson(Base + league + "/scoreboard?dates=" + date, ct);
                List<JVal> events = json == null ? null : json.GetArray("events");
                if (events != null)
                {
                    list.AddRange(events);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write("scorers: scoreboard " + league + " failed: " + ex.Message);
                return null;                        // do not cache a failure
            }

            lock (_lock)
            {
                Trim(_boards, _boardAt);
                _boards[key] = list;
                _boardAt[key] = DateTime.UtcNow;
            }

            return list;
        }

        private static bool Pick(List<JVal> events, Fixture f, out string eventId, out string homeName,
            out string homeAbbr)
        {
            eventId = null;
            homeName = null;
            homeAbbr = null;
            DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
            int bestScore = 0;
            bool bestScoreMatches = false;

            for (int i = 0; i < events.Count; i++)
            {
                JVal e = events[i];
                if (e == null || e.K != JVal.Kind.Object)
                {
                    continue;
                }

                DateTime when;
                if (!DateTime.TryParse(e.GetString("date", ""), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out when))
                {
                    continue;
                }

                if (when.Kind != DateTimeKind.Utc)
                {
                    when = when.ToUniversalTime();
                }

                if ((when - kickoff).Duration() > TimeSpan.FromHours(8))
                {
                    continue;
                }

                List<JVal> comps = e.GetArray("competitions");
                if (comps == null || comps.Count == 0)
                {
                    continue;
                }

                List<JVal> teams = comps[0].GetArray("competitors");
                if (teams == null || teams.Count < 2)
                {
                    continue;
                }

                string espnHome = "";
                string espnHomeAbbr = "";
                string espnAway = "";
                string espnAwayAbbr = "";
                int espnHomeGoals = -1;
                int espnAwayGoals = -1;

                for (int t = 0; t < teams.Count; t++)
                {
                    JVal team = teams[t]["team"];
                    string name = team == null ? "" : team.GetString("displayName", "");
                    string abbr = team == null ? "" : team.GetString("abbreviation", "");
                    string side = teams[t].GetString("homeAway", "");
                    int goals = teams[t].GetInt("score", -1);
                    if (string.Equals(side, "home", StringComparison.OrdinalIgnoreCase))
                    {
                        espnHome = name;
                        espnHomeAbbr = abbr;
                        espnHomeGoals = goals;
                    }
                    else
                    {
                        espnAway = name;
                        espnAwayAbbr = abbr;
                        espnAwayGoals = goals;
                    }
                }

                int homeScore = TeamScore(f.HomeName, f.HomeTla, espnHome, espnHomeAbbr);
                int awayScore = TeamScore(f.AwayName, f.AwayTla, espnAway, espnAwayAbbr);
                if (homeScore < 60 || awayScore < 60)
                {
                    continue;
                }

                int total = homeScore + awayScore;
                bool goalsMatch = espnHomeGoals >= 0 && espnAwayGoals >= 0 &&
                                  f.HomeScore.HasValue && f.AwayScore.HasValue &&
                                  espnHomeGoals == f.HomeScore.Value && espnAwayGoals == f.AwayScore.Value;

                // the scoreline is the tiebreaker: two teams can share a name, a result usually cannot
                bool better = total > bestScore || (total == bestScore && goalsMatch && !bestScoreMatches);
                if (better)
                {
                    bestScore = total;
                    bestScoreMatches = goalsMatch;
                    eventId = e.GetString("id", "");
                    homeName = espnHome;
                    homeAbbr = espnHomeAbbr;
                }
            }

            return eventId != null && eventId.Length > 0;
        }

        // ------------------------------------------------------------------ summary
        private List<GoalEvent> Summary(string league, string eventId, string espnHome, string espnHomeAbbr,
            CancellationToken ct)
        {
            string key = league + "|" + eventId;
            lock (_lock)
            {
                DateTime at;
                if (_summaryAt.TryGetValue(key, out at) && (DateTime.UtcNow - at).TotalSeconds < 60)
                {
                    List<GoalEvent> hit;
                    if (_summaries.TryGetValue(key, out hit))
                    {
                        return hit;
                    }
                }
            }

            List<GoalEvent> goals;
            try
            {
                JVal json = FetchJson(Base + league + "/summary?event=" + eventId, ct);
                goals = json == null ? new List<GoalEvent>() : ParseGoals(json, espnHome, espnHomeAbbr);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write("scorers: summary " + eventId + " failed: " + ex.Message);
                return null;
            }

            lock (_lock)
            {
                Trim(_summaries, _summaryAt);
                _summaries[key] = goals;
                _summaryAt[key] = DateTime.UtcNow;
            }

            return goals;
        }

        private static List<GoalEvent> ParseGoals(JVal json, string espnHome, string espnHomeAbbr)
        {
            var list = new List<GoalEvent>();
            List<JVal> events = json.GetArray("keyEvents");
            if (events == null)
            {
                return list;
            }

            for (int i = 0; i < events.Count; i++)
            {
                JVal e = events[i];
                if (e == null || e.K != JVal.Kind.Object)
                {
                    continue;
                }

                JVal type = e["type"];
                string kind = type == null ? "" : type.GetString("type", "");
                bool scoring = e.GetBool("scoringPlay", false) ||
                               kind.IndexOf("goal", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!scoring || kind.IndexOf("shootout", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var goal = new GoalEvent();

                JVal clock = e["clock"];
                string label = clock == null ? "" : clock.GetString("displayValue", "");
                goal.MinuteLabel = label;
                goal.Minute = MinuteFrom(label);

                JVal team = e["team"];
                string teamName = team == null ? "" : team.GetString("displayName", "");
                string teamAbbr = team == null ? "" : team.GetString("abbreviation", "");
                goal.HomeScored = TeamScore(espnHome, espnHomeAbbr, teamName, teamAbbr) >= 60;

                List<JVal> participants = e.GetArray("participants");
                if (participants != null && participants.Count > 0)
                {
                    JVal athlete = participants[0]["athlete"];
                    if (athlete != null)
                    {
                        goal.Player = athlete.GetString("displayName", "");
                    }
                }

                if (goal.Player.Length == 0)
                {
                    string shortText = e.GetString("shortText", "");
                    int cut = shortText.IndexOf(" Goal", StringComparison.OrdinalIgnoreCase);
                    goal.Player = cut > 0 ? shortText.Substring(0, cut) : shortText;
                }

                int home;
                int away;
                if (!ScoreFromText(e.GetString("text", ""), out home, out away))
                {
                    home = 0;
                    away = 0;
                    for (int g = 0; g < list.Count; g++)
                    {
                        home = list[g].HomeAfter;
                        away = list[g].AwayAfter;
                    }

                    if (goal.HomeScored)
                    {
                        home++;
                    }
                    else
                    {
                        away++;
                    }
                }

                goal.HomeAfter = home;
                goal.AwayAfter = away;
                list.Add(goal);
            }

            return list;
        }

        // "Goal! Manchester United 0, Bournemouth 1. Rayan (Bournemouth) ..."
        private static bool ScoreFromText(string text, out int home, out int away)
        {
            home = 0;
            away = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            Match m = Regex.Match(text, @"([0-9]{1,2})\s*,\s*[^0-9]{1,40}?([0-9]{1,2})\.");
            if (!m.Success)
            {
                return false;
            }

            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out home) &&
                   int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out away);
        }

        // "38'" -> 38, "45'+2'" -> 47, "90'+5'" -> 95
        private static int MinuteFrom(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return -1;
            }

            int plus = label.IndexOf('+');
            string head = plus >= 0 ? label.Substring(0, plus) : label;
            string tail = plus >= 0 ? label.Substring(plus + 1) : "";

            int baseMinute = FirstInt(head);
            if (baseMinute < 0)
            {
                return -1;
            }

            int extra = plus >= 0 ? FirstInt(tail) : 0;
            return baseMinute + (extra > 0 ? extra : 0);
        }

        private static int FirstInt(string text)
        {
            Match m = Regex.Match(text, "[0-9]+");
            if (!m.Success)
            {
                return -1;
            }

            int value;
            return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : -1;
        }

        // ------------------------------------------------------------------ name matching
        // "Man United" has to find "Manchester United", "Barça" has to find "Barcelona",
        // "Brighton Hove" has to find "Brighton & Hove Albion", and "Man City" must not
        // find "Manchester United".
        internal static int TeamScore(string ours, string ourTla, string theirs, string theirAbbr)
        {
            int byName = NameScore(ours, theirs);
            if (!string.IsNullOrEmpty(ourTla) && !string.IsNullOrEmpty(theirAbbr) &&
                string.Equals(ourTla.Trim(), theirAbbr.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return 100;                         // same three letters: settled
            }

            return byName;
        }

        internal static int NameScore(string ours, string theirs)
        {
            string[] a = Tokens(ours);
            string[] b = Tokens(theirs);
            if (a.Length == 0 || b.Length == 0)
            {
                return 0;
            }

            int matched = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    if (SameWord(a[i], b[j]))
                    {
                        matched++;
                        break;
                    }
                }
            }

            int denominator = a.Length < b.Length ? a.Length : b.Length;
            return matched * 100 / denominator;
        }

        private static bool SameWord(string a, string b)
        {
            if (a == b)
            {
                return true;
            }

            // man -> manchester, atleti -> atletico
            if (a.Length >= 3 && b.StartsWith(a, StringComparison.Ordinal))
            {
                return true;
            }

            if (b.Length >= 3 && a.StartsWith(b, StringComparison.Ordinal))
            {
                return true;
            }

            // gladbach -> monchengladbach
            if (a.Length >= 4 && b.EndsWith(a, StringComparison.Ordinal))
            {
                return true;
            }

            if (b.Length >= 4 && a.EndsWith(b, StringComparison.Ordinal))
            {
                return true;
            }

            // barca -> barcelona: four letters in a row is a strong signal for a short token
            int shortest = a.Length < b.Length ? a.Length : b.Length;
            if (shortest <= 6 && CommonPrefix(a, b) >= 4)
            {
                return true;
            }

            return false;
        }

        private static int CommonPrefix(string a, string b)
        {
            int limit = a.Length < b.Length ? a.Length : b.Length;
            int i = 0;
            while (i < limit && a[i] == b[i])
            {
                i++;
            }

            return i;
        }

        private static string[] Tokens(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return new string[0];
            }

            string lower = Fold(name);
            var parts = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (c >= 'a' && c <= 'z')
                {
                    current.Append(c);
                }
                else if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                }
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            var kept = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (!IsNoise(parts[i]) && parts[i].Length > 0)
                {
                    kept.Add(Alias(parts[i]));
                }
            }

            return kept.ToArray();
        }

        private static bool IsNoise(string word)
        {
            for (int i = 0; i < Noise.Length; i++)
            {
                if (word == Noise[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static string Alias(string word)
        {
            for (int i = 0; i < Aliases.GetLength(0); i++)
            {
                if (word == Aliases[i, 0])
                {
                    return Aliases[i, 1];
                }
            }

            return word;
        }

        // lower case, accents folded, punctuation gone: "Barça" and "Barcelona" share "barc"
        private static string Fold(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                switch (c)
                {
                    case 'à':
                    case 'á':
                    case 'â':
                    case 'ã':
                    case 'ä':
                    case 'å':
                        builder.Append('a');
                        break;
                    case 'è':
                    case 'é':
                    case 'ê':
                    case 'ë':
                        builder.Append('e');
                        break;
                    case 'ì':
                    case 'í':
                    case 'î':
                    case 'ï':
                        builder.Append('i');
                        break;
                    case 'ò':
                    case 'ó':
                    case 'ô':
                    case 'õ':
                    case 'ö':
                    case 'ø':
                        builder.Append('o');
                        break;
                    case 'ù':
                    case 'ú':
                    case 'û':
                    case 'ü':
                        builder.Append('u');
                        break;
                    case 'ç':
                        builder.Append('c');
                        break;
                    case 'ñ':
                        builder.Append('n');
                        break;
                    case 'ý':
                    case 'ÿ':
                        builder.Append('y');
                        break;
                    case 'ß':
                        builder.Append("ss");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        // ------------------------------------------------------------------ plumbing
        private static void Trim<T>(Dictionary<string, T> values, Dictionary<string, DateTime> stamps)
        {
            if (values.Count < 64)
            {
                return;
            }

            values.Clear();
            stamps.Clear();
        }

        private static JVal FetchJson(string url, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "MatchPill/1.3 (Windows NT 10.0; .NET 4.8)";
            request.Accept = "application/json";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (ct.Register(delegate
                   {
                       try
                       {
                           request.Abort();
                       }
                       catch
                       {
                       }
                   }))
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return JVal.Parse(reader.ReadToEnd());
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Autostart - start with Windows, and a watchdog that puts the card back if it dies.
    //
    //  Two independent belts, because "it disappeared again" is the one failure the user feels:
    //    1. HKCU\...\Run    - starts the card at every logon (no admin rights needed)
    //    2. Scheduled task  - every 5 minutes, runs "MatchPill.exe --ensure", which starts the
    //                         card only if it is not already running
    //  A clean Exit writes stopped.flag, and --ensure respects it, so quitting on purpose is
    //  final until the user starts the app again by hand.
    // ----------------------------------------------------------------------------------------
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "MatchPill";
        private const string TaskName = "MatchPill Watchdog";
        private const string MutexName = "MatchPill.SingleInstance.v1";

        internal static string ExePath
        {
            get
            {
                try
                {
                    return Application.ExecutablePath;
                }
                catch
                {
                    return Process.GetCurrentProcess().MainModule.FileName;
                }
            }
        }

        internal static bool IsEnabled
        {
            get { return ReadRunValue() != null; }
        }

        private static string ReadRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    return key.GetValue(RunValueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        // The card cannot restart itself once it is gone, so the two things that do that - the Run
        // key and the 5-minute watchdog task - are checked from inside the running card. A task
        // that some other program disabled is the one failure the watchdog cannot recover from on
        // its own: on 29 Oct 2026 the task was found disabled and the card stayed down for hours.
        // `schtasks /change /enable` is idempotent, so this costs one cheap call.
        internal static void Heal()
        {
            try
            {
                if (!IsEnabled)
                {
                    Install();
                    Log.Write("autostart: run key was missing, reinstalled");
                }
            }
            catch (Exception ex)
            {
                Log.Write("autostart: run key check failed: " + ex.Message);
            }

            try
            {
                if (SetTaskEnabled(TaskName, true))
                {
                    Log.Write("autostart: watchdog task was disabled, re-enabled");
                }
            }
            catch (Exception ex)
            {
                Log.Write("autostart: watchdog enable failed: " + ex.Message);
            }
        }

        // schtasks /change returns "Access is denied" from a normal user process; the Task
        // Scheduler COM API (what PowerShell's Enable-ScheduledTask uses) does not.
        // Returns true only when the state actually changed.
        internal static bool SetTaskEnabled(string name, bool enabled)
        {
            object service = null;
            object folder = null;
            object task = null;
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null)
                {
                    return false;
                }

                service = Activator.CreateInstance(serviceType);
                serviceType.InvokeMember("Connect", BindingFlags.InvokeMethod, null, service, new object[0]);
                folder = serviceType.InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, service,
                    new object[] { "\\" });
                task = folder.GetType().InvokeMember("GetTask", BindingFlags.InvokeMethod, null, folder,
                    new object[] { name });

                Type taskType = task.GetType();
                bool was = (bool)taskType.InvokeMember("Enabled", BindingFlags.GetProperty, null, task, null);
                if (was == enabled)
                {
                    return false;
                }

                taskType.InvokeMember("Enabled", BindingFlags.SetProperty, null, task,
                    new object[] { enabled });
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("autostart: task " + (enabled ? "enable" : "disable") + " failed: " +
                          AppController.Innermost(ex).Message);
                return false;
            }
            finally
            {
                Release(task);
                Release(folder);
                Release(service);
            }
        }

        private static void Release(object com)
        {
            try
            {
                if (com != null && Marshal.IsComObject(com))
                {
                    Marshal.ReleaseComObject(com);
                }
            }
            catch
            {
            }
        }

        internal static bool Install()
        {
            string exe = ExePath;
            bool ok = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(RunValueName, "\"" + exe + "\"", RegistryValueKind.String);
                        ok = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("autostart: run key failed: " + ex.Message);
            }

            bool watchdog = InstallWatchdog(exe);
            Log.Write("autostart: run key=" + ok + " watchdog=" + watchdog);
            return ok;
        }

        internal static bool Uninstall()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("autostart: run key removal failed: " + ex.Message);
            }

            RemoveWatchdog();
            Log.Write("autostart: removed");
            return true;
        }

        internal static bool InstallWatchdog(string exe)
        {
            try
            {
                // XML keeps the path and its spaces out of the command line quoting minefield.
                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                    "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                    "  <RegistrationInfo>\r\n" +
                    "    <Description>Restarts MatchPill if it is not running.</Description>\r\n" +
                    "  </RegistrationInfo>\r\n" +
                    "  <Triggers>\r\n" +
                    "    <TimeTrigger>\r\n" +
                    "      <StartBoundary>" + DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "</StartBoundary>\r\n" +
                    "      <Repetition>\r\n" +
                    "        <Interval>PT5M</Interval>\r\n" +
                    "        <StopAtDurationEnd>false</StopAtDurationEnd>\r\n" +
                    "      </Repetition>\r\n" +
                    "      <Enabled>true</Enabled>\r\n" +
                    "    </TimeTrigger>\r\n" +
                    "  </Triggers>\r\n" +
                    "  <Principals>\r\n" +
                    "    <Principal id=\"Author\">\r\n" +
                    "      <LogonType>InteractiveToken</LogonType>\r\n" +
                    "      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
                    "    </Principal>\r\n" +
                    "  </Principals>\r\n" +
                    "  <Settings>\r\n" +
                    "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                    "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                    "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                    "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                    "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                    "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                    "    <IdleSettings>\r\n" +
                    "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
                    "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
                    "    </IdleSettings>\r\n" +
                    "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                    "    <Enabled>true</Enabled>\r\n" +
                    "    <Hidden>false</Hidden>\r\n" +
                    "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                    "    <WakeToRun>false</WakeToRun>\r\n" +
                    "    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>\r\n" +
                    "    <Priority>7</Priority>\r\n" +
                    "  </Settings>\r\n" +
                    "  <Actions Context=\"Author\">\r\n" +
                    "    <Exec>\r\n" +
                    "      <Command>" + EscapeXml(exe) + "</Command>\r\n" +
                    "      <Arguments>--ensure</Arguments>\r\n" +
                    "    </Exec>\r\n" +
                    "  </Actions>\r\n" +
                    "</Task>\r\n";

                string temp = Path.Combine(Path.GetTempPath(), "matchpill-watchdog.xml");
                File.WriteAllText(temp, xml, Encoding.Unicode);

                string output;
                int code = RunTool("schtasks.exe",
                    "/create /tn \"" + TaskName + "\" /xml \"" + temp + "\" /f", out output);
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }

                if (code != 0)
                {
                    Log.Write("autostart: watchdog create failed (" + code + "): " + output.Trim());
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Write("autostart: watchdog failed: " + ex.Message);
                return false;
            }
        }

        internal static void RemoveWatchdog()
        {
            try
            {
                string output;
                RunTool("schtasks.exe", "/delete /tn \"" + TaskName + "\" /f", out output);
            }
            catch (Exception ex)
            {
                Log.Write("autostart: watchdog removal failed: " + ex.Message);
            }
        }

        private static string EscapeXml(string text)
        {
            if (text == null)
            {
                return "";
            }

            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static int RunTool(string file, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        return -1;
                    }

                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch
                        {
                        }

                        return -1;
                    }

                    output = stdout + stderr;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        // ------------------------------------------------------------------ stop flag
        internal static void WriteStopped()
        {
            try
            {
                File.WriteAllText(Paths.StopFlagFile, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        internal static void ClearStopped()
        {
            try
            {
                if (File.Exists(Paths.StopFlagFile))
                {
                    File.Delete(Paths.StopFlagFile);
                }
            }
            catch
            {
            }
        }

        internal static bool IsStoppedOnPurpose
        {
            get
            {
                try
                {
                    return File.Exists(Paths.StopFlagFile);
                }
                catch
                {
                    return false;
                }
            }
        }

        // ------------------------------------------------------------------ --ensure
        // Called by the watchdog every five minutes. Silent, fast, and idempotent.
        internal static int EnsureRunning()
        {
            if (IsStoppedOnPurpose)
            {
                return 0;                               // the user closed it on purpose
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;                           // already up
                }

                try
                {
                    mutex.ReleaseMutex();
                }
                catch
                {
                }
            }

            try
            {
                var psi = new ProcessStartInfo(ExePath);
                psi.WorkingDirectory = Paths.Dir;
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log.Write("watchdog: card was missing, started it again");
            }
            catch (Exception ex)
            {
                Log.Write("watchdog: restart failed: " + ex.Message);
                return 1;
            }

            return 0;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Next fixtures - the favourite teams' next match, which is usually NOT in today's window.
    //  One request per team, and only when the cached answer is missing or has gone stale.
    // ----------------------------------------------------------------------------------------
    // ----------------------------------------------------------------------------------------
    //  Cup rounds the free feed does not carry
    //
    //  football-data.org's free tier has no League Cup, no FA Cup, no Copa del Rey. On 28 Oct 2026
    //  Liverpool v Chelsea and Man City v Brighton were played and the feed did not know those
    //  fixtures existed - the card was polling fine and had nothing to show. ESPN does have them,
    //  keylessly, so cup days come from there and are merged into the same list.
    //
    //  A cup fixture is kept only when one of my teams is in it (or it is live and "show all live"
    //  is on), and its team ids are rewritten to the favourite's own id, so every downstream rule
    //  - favourites, alerts, "my teams first", the cache - works with no special case.
    // ----------------------------------------------------------------------------------------
    internal static class CupSource
    {
        private const string Base = "https://site.api.espn.com/apis/site/v2/sports/soccer/";

        private static readonly string[,] Leagues = new string[,]
        {
            { "eng.league_cup", "EFL", "Carabao Cup" },
            { "eng.fa", "FAC", "FA Cup" },
            { "esp.copa_del_rey", "CDR", "Copa del Rey" },
            { "fra.coupe_de_france", "CDF", "Coupe de France" },
            { "ita.coppa_italia", "CIT", "Coppa Italia" },
            { "ger.dfb_pokal", "DFB", "DFB-Pokal" }
        };

        private static readonly object _lock = new object();
        private static List<Fixture> _items = new List<Fixture>();
        private static readonly Dictionary<string, List<Fixture>> _byDate =
            new Dictionary<string, List<Fixture>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> _dateAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static DateTime _atUtc = DateTime.MinValue;
        private static DateTime _retryUtc = DateTime.MinValue;

        internal static List<Fixture> Snapshot()
        {
            lock (_lock)
            {
                return new List<Fixture>(_items);
            }
        }

        internal static DateTime FetchedUtc
        {
            get
            {
                lock (_lock)
                {
                    return _atUtc;
                }
            }
        }

        // force the next Due() to say yes
        internal static void Invalidate()
        {
            lock (_lock)
            {
                _atUtc = DateTime.MinValue;
                _retryUtc = DateTime.MinValue;
            }
        }

        // 30 s while a cup tie is being played or is about to start, 3 min otherwise, and 10 min
        // of silence after every league failed. The card's clock is derived locally, so half a
        // minute of score lag is invisible.
        internal static bool Due(DateTime nowUtc)
        {
            DateTime at;
            DateTime retry;
            lock (_lock)
            {
                at = _atUtc;
                retry = _retryUtc;
            }

            if (retry != DateTime.MinValue && nowUtc < retry)
            {
                return false;
            }

            if (at == DateTime.MinValue)
            {
                return true;
            }

            bool hot = false;
            List<Fixture> items = Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                Fixture f = items[i];
                if (f.IsLiveNow(nowUtc))
                {
                    hot = true;
                    break;
                }

                DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
                if (kickoff > nowUtc && kickoff - nowUtc < TimeSpan.FromMinutes(45))
                {
                    hot = true;
                    break;
                }
            }

            TimeSpan wait = hot ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(5);
            return nowUtc - at >= wait;
        }

        // ESPN's soccer scoreboard takes ONE date per call (a range is a 400), so the work is
        // cached per league+day. A quiet day answers in about 2.5 KB, a match day in about 67 KB,
        // which is why today can be refreshed often and tomorrow almost never.
        internal static bool Refresh(List<FavoriteTeam> favorites, bool liveAll, DateTime nowUtc)
        {
            DateTime day = nowUtc.Date;
            bool hot = AnyHot(nowUtc);

            var dates = new List<string>();
            var ttl = new List<TimeSpan>();
            dates.Add(day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            ttl.Add(hot ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(5));
            dates.Add(day.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            ttl.Add(TimeSpan.FromHours(1));
            if (nowUtc.Hour < 4)
            {
                dates.Add(day.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                ttl.Add(TimeSpan.FromHours(1));      // a tie that only ended after midnight
            }

            bool any = false;
            bool failed = false;

            for (int i = 0; i < Leagues.GetLength(0); i++)
            {
                for (int d = 0; d < dates.Count; d++)
                {
                    string key = Leagues[i, 1] + "|" + dates[d];
                    DateTime at;
                    lock (_lock)
                    {
                        _dateAt.TryGetValue(key, out at);
                    }

                    if (at != DateTime.MinValue && nowUtc - at < ttl[d])
                    {
                        continue;
                    }

                    JVal json;
                    try
                    {
                        json = GetJson(Base + Leagues[i, 0] + "/scoreboard?dates=" + dates[d]);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("cup fetch failed (" + Leagues[i, 0] + " " + dates[d] + "): " + ex.Message);
                        failed = true;
                        continue;
                    }

                    any = true;
                    var parsed = new List<Fixture>();
                    List<JVal> events = json == null ? null : json.GetArray("events");
                    if (events != null)
                    {
                        for (int j = 0; j < events.Count; j++)
                        {
                            Fixture f = MapEvent(events[j], Leagues[i, 1], Leagues[i, 2], favorites,
                                liveAll, false);
                            if (f != null)
                            {
                                parsed.Add(f);
                            }
                        }
                    }

                    lock (_lock)
                    {
                        _byDate[key] = parsed;
                        _dateAt[key] = nowUtc;
                    }
                }
            }

            if (!any && failed)
            {
                lock (_lock)
                {
                    _retryUtc = nowUtc.AddMinutes(10);
                }

                return false;
            }

            var merged = new List<Fixture>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            lock (_lock)
            {
                for (int i = 0; i < Leagues.GetLength(0); i++)
                {
                    for (int d = 0; d < dates.Count; d++)
                    {
                        List<Fixture> bucket;
                        if (!_byDate.TryGetValue(Leagues[i, 1] + "|" + dates[d], out bucket) || bucket == null)
                        {
                            continue;
                        }

                        for (int k = 0; k < bucket.Count; k++)
                        {
                            if (seen.Add(bucket[k].Id))
                            {
                                merged.Add(bucket[k]);
                            }
                        }
                    }
                }

                _items = merged;
                _atUtc = nowUtc;
                if (any)
                {
                    _retryUtc = DateTime.MinValue;
                }
            }

            return true;
        }

        private static bool AnyHot(DateTime nowUtc)
        {
            List<Fixture> items = Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                Fixture f = items[i];
                if (f.IsLiveNow(nowUtc))
                {
                    return true;
                }

                DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
                if (kickoff > nowUtc && kickoff - nowUtc < TimeSpan.FromMinutes(45))
                {
                    return true;
                }
            }

            return false;
        }

        // Diagnostic: the last few days of cup ties involving a favourite, in one list.
        internal static List<Fixture> Recent(List<FavoriteTeam> favorites, bool liveAll,
            DateTime nowUtc, int daysBack)
        {
            var result = new List<Fixture>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < Leagues.GetLength(0); i++)
            {
                for (int d = 0; d <= daysBack; d++)
                {
                    string day = nowUtc.AddDays(-d).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    JVal json;
                    try
                    {
                        json = GetJson(Base + Leagues[i, 0] + "/scoreboard?dates=" + day);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("cup probe failed (" + Leagues[i, 0] + " " + day + "): " + ex.Message);
                        continue;
                    }

                    List<JVal> events = json == null ? null : json.GetArray("events");
                    if (events == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < events.Count; j++)
                    {
                        Fixture f = MapEvent(events[j], Leagues[i, 1], Leagues[i, 2], favorites,
                            liveAll, false);
                        if (f != null && seen.Add(f.Id))
                        {
                            result.Add(f);
                        }
                    }
                }
            }

            return result;
        }

        // Diagnostic: every cup tie in the window and whether it was recognised as mine.
        internal static int Probe(List<FavoriteTeam> favorites, DateTime nowUtc, StringBuilder report)
        {
            int events = 0;
            int mine = 0;

            var dates = new List<string>();
            dates.Add(nowUtc.AddDays(-2).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            dates.Add(nowUtc.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            dates.Add(nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

            for (int i = 0; i < Leagues.GetLength(0); i++)
            {
                string slug = Leagues[i, 0];
                for (int d = 0; d < dates.Count; d++)
                {
                    JVal json;
                    try
                    {
                        json = GetJson(Base + slug + "/scoreboard?dates=" + dates[d]);
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine(slug + " " + dates[d] + ": FAILED " + ex.Message);
                        continue;
                    }

                    List<JVal> list = json == null ? null : json.GetArray("events");
                    if (list == null || list.Count == 0)
                    {
                        continue;
                    }

                    for (int j = 0; j < list.Count; j++)
                    {
                        Fixture f = MapEvent(list[j], Leagues[i, 1], Leagues[i, 2], favorites, true, true);
                        if (f == null)
                        {
                            continue;
                        }

                        events++;
                        bool isMine = MatchFavorite(f.HomeName, f.HomeTla, favorites) != null ||
                                      MatchFavorite(f.AwayName, f.AwayTla, favorites) != null;
                        if (isMine)
                        {
                            mine++;
                        }

                        string score = (f.HomeScore.HasValue
                                           ? f.HomeScore.Value.ToString(CultureInfo.InvariantCulture)
                                           : "-") + "-" +
                                       (f.AwayScore.HasValue
                                           ? f.AwayScore.Value.ToString(CultureInfo.InvariantCulture)
                                           : "-");

                        report.AppendLine(f.KickoffUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                                          "Z  " + f.CompetitionCode + "  " +
                                          f.State.ToString().PadRight(9) + "  " + score + "  " +
                                          f.HomeName + " v " + f.AwayName +
                                          (f.Note.Length > 0 ? "  [" + f.Note + "]" : "") +
                                          (isMine ? "   <== MINE" : ""));
                    }
                }
            }

            report.AppendLine("");
            report.AppendLine("events=" + events.ToString(CultureInfo.InvariantCulture) +
                              " mine=" + mine.ToString(CultureInfo.InvariantCulture));
            return mine;
        }

        private static JVal GetJson(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.UserAgent = "MatchPill/1.6";
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    return null;
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return JVal.Parse(reader.ReadToEnd());
                }
            }
        }

        // Pure: an ESPN event in, a Fixture out (or null when it is not mine).
        internal static Fixture MapEvent(JVal e, string code, string compName,
            List<FavoriteTeam> favorites, bool liveAll, bool keepEverything)
        {
            if (e == null)
            {
                return null;
            }

            string id = e.GetString("id", "");
            if (id.Length == 0)
            {
                return null;
            }

            List<JVal> comps = e.GetArray("competitions");
            if (comps == null || comps.Count == 0 || comps[0] == null)
            {
                return null;
            }

            JVal comp = comps[0];
            List<JVal> sides = comp.GetArray("competitors");
            if (sides == null)
            {
                return null;
            }

            JVal home = null;
            JVal away = null;
            for (int i = 0; i < sides.Count; i++)
            {
                JVal side = sides[i];
                if (side == null)
                {
                    continue;
                }

                string where = side.GetString("homeAway", "");
                if (string.Equals(where, "home", StringComparison.OrdinalIgnoreCase))
                {
                    home = side;
                }
                else if (string.Equals(where, "away", StringComparison.OrdinalIgnoreCase))
                {
                    away = side;
                }
            }

            if (home == null || away == null)
            {
                return null;
            }

            DateTime kickoff;
            if (!DateTime.TryParse(e.GetString("date", ""), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out kickoff))
            {
                return null;
            }

            if (kickoff.Kind != DateTimeKind.Utc)
            {
                kickoff = kickoff.ToUniversalTime();
            }

            var f = new Fixture();
            f.Id = "e" + id;
            f.CompetitionCode = code;
            f.CompetitionName = compName;
            f.KickoffUtc = kickoff;
            f.HomeName = TeamName(home);
            f.AwayName = TeamName(away);
            f.HomeTla = TeamAbbr(home);
            f.AwayTla = TeamAbbr(away);
            f.HomeCrest = TeamLogo(home);
            f.AwayCrest = TeamLogo(away);
            f.HomeScore = Number(home, "score");
            f.AwayScore = Number(away, "score");
            f.State = StateOf(comp);
            f.RawStatus = RawStatusOf(comp);
            f.Note = NoteOf(home, away);
            f.Venue = VenueOf(comp);

            // ESPN sends "0" for both sides before kick-off; a scheduled row must show "v"
            if (f.State == MatchState.Scheduled)
            {
                f.HomeScore = null;
                f.AwayScore = null;
            }

            if (f.State == MatchState.Live || f.State == MatchState.Paused)
            {
                f.MinuteValue = MinuteOf(comp);
                if (f.MinuteValue > 0)
                {
                    f.Minute = f.MinuteValue.ToString(CultureInfo.InvariantCulture) + "'";
                }
            }

            FavoriteTeam homeFav = MatchFavorite(f.HomeName, f.HomeTla, favorites);
            FavoriteTeam awayFav = MatchFavorite(f.AwayName, f.AwayTla, favorites);
            f.HomeId = homeFav != null ? homeFav.Id : "e" + TeamId(home);
            f.AwayId = awayFav != null ? awayFav.Id : "e" + TeamId(away);

            bool mine = homeFav != null || awayFav != null;
            bool live = f.State == MatchState.Live || f.State == MatchState.Paused;
            if (!keepEverything && !mine && !(liveAll && live))
            {
                return null;
            }

            return f;
        }

        internal static FavoriteTeam MatchFavorite(string name, string abbr, List<FavoriteTeam> favorites)
        {
            if (favorites == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int i = 0; i < favorites.Count; i++)
            {
                FavoriteTeam team = favorites[i];
                if (team == null)
                {
                    continue;
                }

                if (EspnScorers.TeamScore(team.Name, "", name, abbr) >= 60)
                {
                    return team;
                }
            }

            return null;
        }

        internal static MatchState StateOf(JVal comp)
        {
            JVal status = comp == null ? null : comp["status"];
            JVal type = status == null ? null : status["type"];
            string state = type == null ? "" : type.GetString("state", "");
            string name = type == null ? "" : type.GetString("name", "");
            string detail = type == null ? "" : type.GetString("detail", "");

            if (Has(name, "POSTPONED"))
            {
                return MatchState.Postponed;
            }

            if (Has(name, "CANCELED") || Has(name, "CANCELLED"))
            {
                return MatchState.Cancelled;
            }

            if (Has(name, "SUSPENDED"))
            {
                return MatchState.Suspended;
            }

            if (string.Equals(state, "pre", StringComparison.OrdinalIgnoreCase))
            {
                return MatchState.Scheduled;
            }

            if (string.Equals(state, "post", StringComparison.OrdinalIgnoreCase))
            {
                return MatchState.Finished;
            }

            if (Has(name, "HALFTIME") || string.Equals(detail, "HT", StringComparison.OrdinalIgnoreCase))
            {
                return MatchState.Paused;
            }

            if (string.Equals(state, "in", StringComparison.OrdinalIgnoreCase))
            {
                return MatchState.Live;
            }

            return MatchState.Unknown;
        }

        private static bool Has(string haystack, string needle)
        {
            return haystack != null &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string RawStatusOf(JVal comp)
        {
            JVal status = comp == null ? null : comp["status"];
            JVal type = status == null ? null : status["type"];
            return type == null ? "" : type.GetString("name", "");
        }

        // "Pens 4–3": the shoot-out, when there was one.
        internal static string NoteOf(JVal home, JVal away)
        {
            int? h = Number(home, "shootoutScore");
            int? a = Number(away, "shootoutScore");
            if (!h.HasValue || !a.HasValue)
            {
                return "";
            }

            if (h.Value <= 0 && a.Value <= 0)
            {
                return "";
            }

            return "Pens " + h.Value.ToString(CultureInfo.InvariantCulture) + "\u2013" +
                   a.Value.ToString(CultureInfo.InvariantCulture);
        }

        // "67'" -> 67, "90'+5'" -> 90, "" -> 0
        internal static int MinuteOf(JVal comp)
        {
            JVal status = comp == null ? null : comp["status"];
            if (status == null)
            {
                return 0;
            }

            string clock = status.GetString("displayClock", "");
            int value = 0;
            int i = 0;
            while (i < clock.Length && clock[i] >= '0' && clock[i] <= '9')
            {
                value = value * 10 + (clock[i] - '0');
                i++;
            }

            return value;
        }

        private static string VenueOf(JVal comp)
        {
            JVal venue = comp == null ? null : comp["venue"];
            if (venue == null)
            {
                return "";
            }

            string full = venue.GetString("fullName", "");
            return full.Length > 0 ? full : venue.GetString("name", "");
        }

        // ESPN sends numbers as strings in some fields and as numbers in others.
        internal static int? Number(JVal node, string key)
        {
            JVal v = node == null ? null : node[key];
            if (v == null)
            {
                return null;
            }

            if (v.K == JVal.Kind.Number)
            {
                return (int)Math.Round(v.N);
            }

            if (v.K == JVal.Kind.String)
            {
                int parsed;
                if (int.TryParse(v.S, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static JVal Team(JVal side)
        {
            return side == null ? null : side["team"];
        }

        private static string TeamName(JVal side)
        {
            JVal team = Team(side);
            if (team == null)
            {
                return "";
            }

            string shortName = team.GetString("shortDisplayName", "");
            return shortName.Length > 0 ? shortName : team.GetString("displayName", "");
        }

        private static string TeamAbbr(JVal side)
        {
            JVal team = Team(side);
            return team == null ? "" : team.GetString("abbreviation", "");
        }

        private static string TeamId(JVal side)
        {
            JVal team = Team(side);
            return team == null ? "" : team.GetString("id", "");
        }

        private static string TeamLogo(JVal side)
        {
            JVal team = Team(side);
            return team == null ? "" : team.GetString("logo", "");
        }
    }

    internal sealed class NextFixtures
    {
        private static readonly TimeSpan FreshFor = TimeSpan.FromHours(6);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);

        private readonly Dictionary<string, Fixture> _next = new Dictionary<string, Fixture>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _checkedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _retryAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        internal Fixture For(string teamId, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return null;
            }

            lock (_lock)
            {
                Fixture f;
                if (!_next.TryGetValue(teamId, out f) || f == null)
                {
                    return null;
                }

                // once kick-off passes it is no longer "next"; wait for the refetch
                DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
                if (kickoff <= nowUtc.AddMinutes(-5))
                {
                    return null;
                }

                return f;
            }
        }

        internal bool NeedsWork(string teamId, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return false;
            }

            lock (_lock)
            {
                DateTime retry;
                if (_retryAt.TryGetValue(teamId, out retry) && nowUtc < retry)
                {
                    return false;                       // a failure was just recorded; leave it alone
                }

                DateTime at;
                if (_checkedAt.TryGetValue(teamId, out at) && nowUtc - at < FreshFor)
                {
                    Fixture f;
                    if (!_next.TryGetValue(teamId, out f) || f == null)
                    {
                        return false;                   // asked recently, and there is no next fixture
                    }

                    if (AppController.EnsureUtc(f.KickoffUtc) > nowUtc.AddMinutes(-5))
                    {
                        return false;                   // fresh, and the match has not started
                    }
                }

                return true;
            }
        }

        // A failed lookup is NOT an answer. Record a short backoff and try again later, otherwise
        // one 429 would hide a team's next fixture for six hours.
        internal void Fail(string teamId, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return;
            }

            lock (_lock)
            {
                _retryAt[teamId] = nowUtc + RetryAfterFailure;
            }
        }

        internal void Put(string teamId, Fixture f, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return;
            }

            lock (_lock)
            {
                _checkedAt[teamId] = nowUtc;
                if (f == null)
                {
                    _next.Remove(teamId);
                }
                else
                {
                    _next[teamId] = f;
                }
            }
        }

        internal List<Fixture> All(DateTime nowUtc)
        {
            var list = new List<Fixture>();
            lock (_lock)
            {
                foreach (KeyValuePair<string, Fixture> pair in _next)
                {
                    Fixture f = pair.Value;
                    if (f == null)
                    {
                        continue;
                    }

                    if (AppController.EnsureUtc(f.KickoffUtc) <= nowUtc.AddMinutes(-5))
                    {
                        continue;
                    }

                    list.Add(f);
                }
            }

            return list;
        }

        internal static NextFixtures Load(string path)
        {
            var result = new NextFixtures();
            try
            {
                if (!File.Exists(path))
                {
                    return result;
                }

                JVal root = JVal.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (root == null || root.K != JVal.Kind.Object)
                {
                    return result;
                }

                List<JVal> teams = root.GetArray("teams");
                if (teams == null)
                {
                    return result;
                }

                for (int i = 0; i < teams.Count; i++)
                {
                    JVal row = teams[i];
                    if (row == null)
                    {
                        continue;
                    }

                    string id = row.GetString("team", "");
                    if (id.Length == 0)
                    {
                        continue;
                    }

                    DateTime at = ParseTime(row.GetString("checked", ""));
                    if (at != DateTime.MinValue)
                    {
                        result._checkedAt[id] = at;
                    }

                    Fixture f = Fixture.FromJson(row["fixture"]);
                    if (f != null)
                    {
                        result._next[id] = f;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("next.json read failed: " + ex.Message);
            }

            return result;
        }

        internal void Save(string path)
        {
            try
            {
                JVal root = JVal.Obj();
                JVal teams = JVal.Arr();
                lock (_lock)
                {
                    foreach (KeyValuePair<string, DateTime> pair in _checkedAt)
                    {
                        JVal row = JVal.Obj();
                        row.Set("team", JVal.Str(pair.Key));
                        row.Set("checked", JVal.Str(pair.Value.ToString("o", CultureInfo.InvariantCulture)));
                        Fixture f;
                        if (_next.TryGetValue(pair.Key, out f) && f != null)
                        {
                            row.Set("fixture", f.ToJson());
                        }

                        teams.A.Add(row);
                    }
                }

                root.Set("teams", teams);
                File.WriteAllText(path, root.ToJson(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("next.json write failed: " + ex.Message);
            }
        }

        private static DateTime ParseTime(string text)
        {
            DateTime value;
            if (!string.IsNullOrEmpty(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            }

            return DateTime.MinValue;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Alerts - exactly one notification per fixture, minutes before kick-off, ever.
    //  The ids are on disk so a restart cannot produce a second ping for the same match.
    // ----------------------------------------------------------------------------------------
    internal sealed class MatchAlerts
    {
        private readonly Dictionary<string, DateTime> _notified =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal bool HasNotified(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
            {
                return true;
            }

            lock (_notified)
            {
                return _notified.ContainsKey(fixtureId);
            }
        }

        internal void Mark(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
            {
                return;
            }

            lock (_notified)
            {
                _notified[fixtureId] = DateTime.UtcNow;
            }
        }

        internal static MatchAlerts Load(string path)
        {
            var result = new MatchAlerts();
            try
            {
                if (!File.Exists(path))
                {
                    return result;
                }

                JVal root = JVal.Parse(File.ReadAllText(path, Encoding.UTF8));
                List<JVal> ids = root == null ? null : root.GetArray("ids");
                if (ids == null)
                {
                    return result;
                }

                DateTime cutoff = DateTime.UtcNow.AddDays(-21);
                for (int i = 0; i < ids.Count; i++)
                {
                    JVal row = ids[i];
                    if (row == null || row.K != JVal.Kind.Object)
                    {
                        continue;
                    }

                    string id = row.GetString("id", "");
                    DateTime at;
                    if (id.Length == 0 || !DateTime.TryParse(row.GetString("at", ""),
                            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at))
                    {
                        continue;
                    }

                    if (at.Kind != DateTimeKind.Utc)
                    {
                        at = at.ToUniversalTime();
                    }

                    if (at < cutoff)
                    {
                        continue;                       // keep the file from growing forever
                    }

                    result._notified[id] = at;
                }
            }
            catch (Exception ex)
            {
                Log.Write("alerts.json read failed: " + ex.Message);
            }

            return result;
        }

        internal void Save(string path)
        {
            try
            {
                JVal root = JVal.Obj();
                JVal ids = JVal.Arr();
                lock (_notified)
                {
                    foreach (KeyValuePair<string, DateTime> pair in _notified)
                    {
                        JVal row = JVal.Obj();
                        row.Set("id", JVal.Str(pair.Key));
                        row.Set("at", JVal.Str(pair.Value.ToString("o", CultureInfo.InvariantCulture)));
                        ids.A.Add(row);
                    }
                }

                root.Set("ids", ids);
                File.WriteAllText(path, root.ToJson(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("alerts.json write failed: " + ex.Message);
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Controller - owns config, provider, engine, card, tray
    // ----------------------------------------------------------------------------------------
    internal sealed class AppController : IDisposable
    {
        internal AppConfig Config;
        internal IScoreProvider Provider;
        internal CardForm Card;
        internal NotifyIcon Tray;
        internal Action RequestExit;

        // set by the --shot tools: a layout render must never touch the user's real config
        internal bool ReadOnlyConfig;

        // the startup wizard hides the card on purpose; the rescue must not fight it
        private bool _wizardShowing;

        internal List<Fixture> LastGood = new List<Fixture>();
        internal DateTime LastSuccessUtc = DateTime.MinValue;
        internal bool Paused;
        internal string LastError = "";

        private PollEngine _engine;
        private EspnScorers _scorers;
        private bool _scorerBusy;
        private System.Windows.Forms.Timer _watch;
        private ContextMenuStrip _menu;
        private Icon _iconLive;
        private Icon _iconIdle;
        private bool _iconShowsLive;
        private bool _disposed;
        private bool _started;
        private int _ticks;
        private DateTime _sinkAtUtc = DateTime.MinValue;
        private List<string> _nextLines = new List<string>();

        // favourites' next match (usually beyond today's window) and the once-only alert book
        internal NextFixtures NextUp;
        internal MatchAlerts Alerts;
        private bool _nextBusy;
        private bool _cupBusy;
        private int _cupLogged = -1;
        private DateTime _healAtUtc = DateTime.UtcNow;
        private DateTime _nextSweepUtc = DateTime.MinValue;

        internal AppController()
        {
            Config = AppConfig.Load(Paths.ConfigFile);
            NextUp = NextFixtures.Load(Paths.NextFile);
            Alerts = MatchAlerts.Load(Paths.AlertsFile);

            CachedPayload cache = CachedPayload.Load(Paths.CacheFile);
            if (cache.Fixtures.Count > 0)
            {
                LastGood = cache.Fixtures;
                LastSuccessUtc = cache.SavedUtc;
            }
        }

        internal void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            AutoStart.ClearStopped();                   // a real start cancels a previous Exit
            RequestLog.TrimFile();
            Provider = CreateProvider();
            _scorers = new EspnScorers();
            CreateTray();
            Card = new CardForm(this);
            Card.ApplyInitialBounds();
            List<Fixture> view = BuildView(LastGood, DateTime.UtcNow, out _nextLines);
            Card.SetData(view, LastSuccessUtc, false, _nextLines);
            Card.ShowCard();
            UpdateTray();

            _watch = new System.Windows.Forms.Timer();
            _watch.Interval = 2000;
            _watch.Tick += OnWatchTick;
            _watch.Start();

            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            StartEngine();

            if (!Config.WizardDone)
            {
                EventHandler openOnce = null;
                openOnce = delegate
                {
                    Application.Idle -= openOnce;
                    Log.Write("first run: opening wizard");
                    ShowStartupWizard();
                };
                Application.Idle += openOnce;
            }
        }

        internal IScoreProvider CreateProvider()
        {
            string kind = Config.Provider == null ? "" : Config.Provider.Trim().ToLowerInvariant();
            if (kind == "footballdata" && Config.HasKey)
            {
                return new FootballDataOrgProvider(Config.ApiKey, Config.ApiBaseUrl,
                    Config.EffectiveCompetitions());
            }

            if (kind == "mock-ratelimited")
            {
                return new MockProvider(true);
            }

            return new MockProvider(false);             // never dead on arrival
        }

        internal IScoreProvider CreateProviderForSearch(string key)
        {
            if (!string.IsNullOrEmpty(key) && key.Trim().Length > 0)
            {
                return new FootballDataOrgProvider(key, Config.ApiBaseUrl,
                    Config.EffectiveCompetitions());
            }

            return new MockProvider(false);
        }

        private void StartEngine()
        {
            StopEngine();
            _engine = new PollEngine(Provider, SelectInterval, OnPollResult, Config.ApiCallsPerMinute);
            _engine.Start();
        }

        private void StopEngine()
        {
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
        }

        // ---------------------------------------------------------------- polling policy
        // The card is asked for news constantly while something is on. That wish is expressed here
        // as the base interval; PollEngine then stretches it to whatever the key's quota allows.
        internal static int IntervalFor(bool mineLive, bool otherLive, bool kickoffSoon, bool quiet,
            int quietMs, int refreshMs)
        {
            if (mineLive)
            {
                return refreshMs < 1000 ? 1000 : refreshMs;      // my match: every 2 s
            }

            if (otherLive)
            {
                // someone else's match is on: the clock still ticks locally, 15 s is plenty and
                // 2 s here would spend the whole daily quota on other people's games
                return refreshMs > 15000 ? refreshMs : 15000;
            }

            if (kickoffSoon)
            {
                return 15000;                           // kickoff within 30 min: every 15 s
            }

            if (quiet)
            {
                return quietMs;                         // 02:00-08:00 local: stop entirely
            }

            return 60000;                               // otherwise: once a minute
        }

        private int SelectInterval(DateTime nowUtc)
        {
            return IntervalFor(HasLiveNow(nowUtc), HasAnyLiveNow(nowUtc),
                NextKickoffWithin(TimeSpan.FromMinutes(30), nowUtc),
                IsQuietHours(), MsUntilQuietEnds(), Config.RefreshSeconds * 1000);
        }

        private static bool IsQuietHours()
        {
            int hour = DateTime.Now.Hour;
            return hour >= 2 && hour < 8;
        }

        private static int MsUntilQuietEnds()
        {
            DateTime end = DateTime.Today.AddHours(8);
            if (end <= DateTime.Now)
            {
                end = end.AddDays(1);
            }

            int ms = (int)(end - DateTime.Now).TotalMilliseconds;
            if (ms < 60000)
            {
                ms = 60000;
            }

            if (ms > 21600000)
            {
                ms = 21600000;
            }

            return ms;
        }

        internal bool IsFavorite(Fixture f)
        {
            if (f == null)
            {
                return false;
            }

            if (Config.Favorites.Count == 0)
            {
                return true;                            // no favourites yet: follow competitions
            }

            for (int i = 0; i < Config.Favorites.Count; i++)
            {
                string id = Config.Favorites[i].Id;
                if (id == f.HomeId || id == f.AwayId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLiveNow(DateTime nowUtc)
        {
            for (int i = 0; i < LastGood.Count; i++)
            {
                Fixture f = LastGood[i];
                if (f.IsLiveNow(nowUtc) && (IsFavorite(f) || Config.LiveScopeAll))
                {
                    return true;                    // a live match on the card means poll fast
                }
            }

            return false;
        }

        private bool HasAnyLiveNow(DateTime nowUtc)
        {
            for (int i = 0; i < LastGood.Count; i++)
            {
                if (LastGood[i].IsLiveNow(nowUtc))
                {
                    return true;
                }
            }

            return false;
        }

        private bool NextKickoffWithin(TimeSpan span, DateTime nowUtc)
        {
            DateTime from = nowUtc.AddMinutes(-10);     // already under way but not reported yet
            DateTime to = nowUtc.Add(span);

            for (int i = 0; i < LastGood.Count; i++)
            {
                Fixture f = LastGood[i];
                if (f.State != MatchState.Scheduled)
                {
                    continue;
                }

                if (!IsFavorite(f) && !Config.LiveScopeAll)
                {
                    continue;
                }

                DateTime kickoff = EnsureUtc(f.KickoffUtc);
                if (kickoff >= from && kickoff <= to)
                {
                    return true;
                }
            }

            // the tracked next fixtures cover days the feed window does not
            if (NextUp != null)
            {
                for (int i = 0; i < Config.Favorites.Count; i++)
                {
                    Fixture f = NextUp.For(Config.Favorites[i].Id, nowUtc);
                    if (f == null || f.State == MatchState.Finished)
                    {
                        continue;
                    }

                    DateTime kickoff = EnsureUtc(f.KickoffUtc);
                    if (kickoff >= from && kickoff <= to)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // AggregateException from a .Result call hides the real reason; dig it out for the log.
        internal static Exception Innermost(Exception ex)
        {
            AggregateException agg = ex as AggregateException;
            while (agg != null && agg.InnerExceptions.Count > 0)
            {
                ex = agg.InnerExceptions[0];
                agg = ex as AggregateException;
            }

            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex;
        }

        internal static DateTime EnsureUtc(DateTime value)
        {            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        // ---------------------------------------------------------------- poll results
        private void OnPollResult(PollOutcome outcome)
        {
            if (Card == null || Card.IsDisposed)
            {
                return;
            }

            try
            {
                Card.BeginInvoke((MethodInvoker)delegate { ApplyOutcome(outcome); });
            }
            catch
            {
            }
        }

        private void ApplyOutcome(PollOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }

            if (outcome.Success)
            {
                MergeCups(outcome.Fixtures);
                GoalMerge.Merge(LastGood, outcome.Fixtures, DateTime.UtcNow);
                LastGood = outcome.Fixtures;
                LastSuccessUtc = outcome.FetchedUtc;
                Paused = false;
                LastError = "";

                var cache = new CachedPayload();
                cache.SavedUtc = outcome.FetchedUtc;
                cache.Fixtures = outcome.Fixtures;
                cache.Save(Paths.CacheFile);

                ApplyData(false);
            }
            else
            {
                Paused = outcome.RateLimited;
                LastError = outcome.Error;
                ApplyData(true);                        // trap #7: never wipe the card
            }

            UpdateTray();
        }

        private void ApplyData(bool failed)
        {
            DateTime nowUtc = DateTime.UtcNow;
            List<Fixture> view = BuildView(LastGood, nowUtc, out _nextLines);

            if (Card != null && !Card.IsDisposed)
            {
                Card.SetData(view, LastSuccessUtc, Paused, _nextLines);
            }
        }

        internal List<Fixture> BuildView(List<Fixture> all, DateTime nowUtc, out List<string> nextLines)
        {
            var favorites = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Config.Favorites.Count; i++)
            {
                favorites.Add(Config.Favorites[i].Id);
            }

            bool useAll = favorites.Count == 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var live = new List<Fixture>();
            var upcoming = new List<Fixture>();
            var finished = new List<Fixture>();
            DateTime todayLocal = DateTime.Today;

            for (int i = 0; i < all.Count; i++)
            {
                Fixture f = all[i];
                if (f == null || string.IsNullOrEmpty(f.Id))
                {
                    continue;
                }

                bool liveNow = f.IsLiveNow(nowUtc);
                bool mine = useAll || favorites.Contains(f.HomeId) || favorites.Contains(f.AwayId);

                // A live match is the point of the card, so it is shown even when neither team is
                // a favourite (config "liveAll"). Scheduled and finished matches stay scoped to my
                // teams, otherwise the card becomes a league table.
                if (!mine && !(Config.LiveScopeAll && liveNow))
                {
                    continue;
                }

                if (!seen.Add(f.Id))
                {
                    continue;                           // trap #4: same match from two endpoints
                }

                DateTime localKickoff = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(f.KickoffUtc),
                    TimeZoneInfo.Local);

                if (f.IsLiveNow(nowUtc))
                {
                    live.Add(f);
                    continue;
                }

                if (f.State == MatchState.Finished)
                {
                    if (localKickoff.Date == todayLocal ||
                        nowUtc - EnsureUtc(f.KickoffUtc) < TimeSpan.FromHours(6))
                    {
                        finished.Add(f);
                    }

                    continue;
                }

                if (localKickoff.Date == todayLocal)
                {
                    upcoming.Add(f);
                }
            }

            live.Sort(delegate(Fixture a, Fixture b)
            {
                bool ma = IsFavorite(a);
                bool mb = IsFavorite(b);
                if (ma != mb)
                {
                    return ma ? -1 : 1;             // my teams lead the live block
                }

                int pa = CompetitionPriority(a);
                int pb = CompetitionPriority(b);
                if (pa != pb)
                {
                    return pa.CompareTo(pb);
                }

                return a.KickoffUtc.CompareTo(b.KickoffUtc);
            });

            upcoming.Sort(delegate(Fixture a, Fixture b) { return a.KickoffUtc.CompareTo(b.KickoffUtc); });
            finished.Sort(delegate(Fixture a, Fixture b) { return b.KickoffUtc.CompareTo(a.KickoffUtc); });

            var view = new List<Fixture>();
            view.AddRange(live);
            view.AddRange(upcoming);
            view.AddRange(finished);

            // Every favourite's next match, not just the earliest one: on a quiet day this is the
            // whole point of the card. Feed fixtures come first, then the tracked ones.
            var nextPool = new List<Fixture>();
            var nextSeen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < all.Count; i++)
            {
                Fixture f = all[i];
                if (f == null || string.IsNullOrEmpty(f.Id) || !IsFavorite(f))
                {
                    continue;
                }

                if (f.State == MatchState.Finished || f.State == MatchState.Cancelled ||
                    f.State == MatchState.Postponed)
                {
                    continue;
                }

                if (EnsureUtc(f.KickoffUtc) <= nowUtc || !nextSeen.Add(f.Id))
                {
                    continue;
                }

                nextPool.Add(f);
            }

            if (NextUp != null)
            {
                for (int i = 0; i < Config.Favorites.Count; i++)
                {
                    Fixture f = NextUp.For(Config.Favorites[i].Id, nowUtc);
                    if (f != null && !string.IsNullOrEmpty(f.Id) && nextSeen.Add(f.Id))
                    {
                        nextPool.Add(f);
                    }
                }
            }

            nextPool.Sort(delegate(Fixture a, Fixture b)
            {
                return EnsureUtc(a.KickoffUtc).CompareTo(EnsureUtc(b.KickoffUtc));
            });

            nextLines = new List<string>();
            for (int i = 0; i < nextPool.Count && i < 3; i++)
            {
                Fixture f = nextPool[i];
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(f.KickoffUtc), TimeZoneInfo.Local);
                nextLines.Add(local.ToString("ddd HH:mm", CultureInfo.CurrentCulture) + "  " +
                              f.HomeName + " v " + f.AwayName);
            }

            if (nextLines.Count == 0)
            {
                nextLines.Add("No further fixtures in the feed");
            }

            // "Next matches": the same card looking forward. A game in play still leads (it is
            // happening now), then everything scheduled for my teams, including fixtures days
            // away that today's window never contains. No results.
            if (Config.NextView)
            {
                var forward = new List<Fixture>();
                forward.AddRange(live);
                forward.AddRange(nextPool);
                return forward;
            }

            return view;
        }

        private int CompetitionPriority(Fixture f)
        {
            for (int i = 0; i < Config.Competitions.Count; i++)
            {
                if (string.Equals(Config.Competitions[i], f.CompetitionCode, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 99;
        }

        // ---------------------------------------------------------------- tray
        private void CreateTray()
        {
            _iconLive = BuildTrayIcon(true);
            _iconIdle = BuildTrayIcon(false);

            _menu = new ContextMenuStrip();
            _menu.Opening += delegate { MenuBuilder.Fill(_menu, this); };

            Tray = new NotifyIcon();
            Tray.Icon = _iconIdle;
            Tray.Text = Program.AppName;
            Tray.ContextMenuStrip = _menu;
            Tray.Visible = true;
            Tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    SummonCard();
                }
            };

            Tray.BalloonTipClicked += delegate { SummonCard(); };
        }

        private static Icon BuildTrayIcon(bool live)
        {
            const int size = 32;
            using (var bitmap = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color fill = live ? Color.FromArgb(192, 57, 43) : Color.FromArgb(122, 118, 110);
                using (var brush = new SolidBrush(fill))
                {
                    g.FillEllipse(brush, 2, 2, size - 4, size - 4);
                }

                using (var pen = new Pen(Color.FromArgb(245, 245, 245), 3.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, 9f, size / 2f, size - 9f, size / 2f);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIconHandle(handle);
                }
            }
        }

        internal void UpdateTray()
        {
            if (Tray == null)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            int liveCount = 0;
            for (int i = 0; i < LastGood.Count; i++)
            {
                if (IsFavorite(LastGood[i]) && LastGood[i].IsLiveNow(nowUtc))
                {
                    liveCount++;
                }
            }

            bool live = liveCount > 0;
            if (live != _iconShowsLive)
            {
                _iconShowsLive = live;
                Tray.Icon = live ? _iconLive : _iconIdle;
            }

            string text;
            if (Paused)
            {
                text = Program.AppName + " - paused (rate limited)";
            }
            else if (live)
            {
                text = Program.AppName + " - " + liveCount.ToString(CultureInfo.InvariantCulture) + " live";
            }
            else
            {
                text = Program.AppName + " - no live matches";
            }

            if (text.Length > 62)
            {
                text = text.Substring(0, 62);
            }

            try
            {
                Tray.Text = text;
            }
            catch
            {
            }
        }

        // The user asked for the card (tray click): expand it, put it in front for a few seconds,
        // then let it sink back to the desktop layer so it never sits over another app.
        private void SummonCard()
        {
            Config.Collapsed = false;
            if (Card != null && !Card.IsDisposed)
            {
                Card.SetCollapsed(false, true);
                Card.ShowCard();
                Card.RaiseTemporarily();
                _sinkAtUtc = DateTime.UtcNow.AddSeconds(6);
            }
        }

        // ---------------------------------------------------------------- shell events
        private void OnWatchTick(object sender, EventArgs e)
        {
            _ticks++;
            if (_ticks == 3 || _ticks % 150 == 0)
            {
                Native.TrimWorkingSet();                // startup painting is done, give the pages back
                if (Card != null && !Card.IsDisposed)
                {
                    if (!_wizardShowing)
                    {
                        Card.RescueIfOffScreen();
                    }

                    // The whole geometry, so "it disappeared" can be diagnosed from the log alone.
                    Log.Write("desktop layer: topmost=" + Card.TopMost +
                              " shown=" + Card.Visible +
                              " bounds=" + Card.Bounds +
                              " screen=" + Screen.FromControl(Card).Bounds +
                              " atCentre=" + Native.IsVisibleAtCentre(Card.Handle, Card.Bounds) +
                              " coveredBy=" + Native.ClassAtCentre(Card.Bounds));
                }
            }

            if (_sinkAtUtc != DateTime.MinValue && DateTime.UtcNow >= _sinkAtUtc)
            {
                _sinkAtUtc = DateTime.MinValue;
                // The card is a normal (non-topmost) window; after the temporary raise
                // expires it naturally sits behind any app the user activates.
            }

            // No hide-for-fullscreen here. The card is not topmost, so a fullscreen video or game
            // already covers it. Hiding on top of that only added a way to disappear for good:
            // SHQueryUserNotificationState reports "busy" for as long as a player like VLC is open,
            // so the card stayed hidden long after the video ended. That was the "it's gone" bug.

            if (Card != null && !Card.IsDisposed)
            {
                Card.RefreshVisualState();
            }

            // the kick-off balloon is checked on every tick so it is never more than 2 s late
            CheckAlerts();

            // the next-fixture sweep is a network call per stale team, so once a minute at most
            if (DateTime.UtcNow - _nextSweepUtc > TimeSpan.FromSeconds(60))
            {
                _nextSweepUtc = DateTime.UtcNow;
                RefreshNextFixtures();
            }

            // cup rounds are not in the feed at all, so they are pulled from ESPN on their own,
            // slower clock - and only when a cup tie is near or in play does it speed up
            RefreshCups();

            // every six hours: are the Run key and the watchdog task still armed?
            if (DateTime.UtcNow - _healAtUtc > TimeSpan.FromHours(6))
            {
                _healAtUtc = DateTime.UtcNow;
                AutoStart.Heal();
            }
        }

        // ------------------------------------------------------------------ cup rounds
        private void RefreshCups()
        {
            if (!Config.Cups || _cupBusy || Config.Favorites.Count == 0)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (!CupSource.Due(nowUtc))
            {
                return;
            }

            _cupBusy = true;
            List<FavoriteTeam> favorites = Config.Favorites;
            bool liveAll = Config.LiveScopeAll;

            Task.Run(delegate
            {
                bool ok = false;
                try
                {
                    ok = CupSource.Refresh(favorites, liveAll, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    Log.Write("cup refresh failed: " + ex.Message);
                }
                finally
                {
                    _cupBusy = false;
                }

                if (!ok || Card == null || Card.IsDisposed)
                {
                    return;
                }

                try
                {
                    Card.BeginInvoke((MethodInvoker)delegate
                    {
                        MergeCups(LastGood);
                        ApplyData(false);

                        int inView = 0;
                        for (int i = 0; i < LastGood.Count; i++)
                        {
                            if (LastGood[i] != null &&
                                LastGood[i].Id.StartsWith("e", StringComparison.Ordinal))
                            {
                                inView++;
                            }
                        }

                        if (inView != _cupLogged)
                        {
                            _cupLogged = inView;
                            Log.Write("cups: " + inView.ToString(CultureInfo.InvariantCulture) +
                                      " cup tie(s) on the card");
                        }
                    });
                }
                catch
                {
                }
            });
        }

        // Cup fixtures join the same list as everything else. The key is teams + UTC day, so the
        // same tie arriving from both sources still shows once.
        private void MergeCups(List<Fixture> fixtures)
        {
            if (!Config.Cups)
            {
                return;
            }

            MergeCupList(fixtures, CupSource.Snapshot());
        }

        internal static void MergeCupList(List<Fixture> fixtures, List<Fixture> cups)
        {
            if (fixtures == null || cups == null || cups.Count == 0)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                if (f != null)
                {
                    seen.Add(f.Id);
                    seen.Add(CupKey(f));
                }
            }

            for (int i = 0; i < cups.Count; i++)
            {
                Fixture c = cups[i];
                if (c == null || string.IsNullOrEmpty(c.Id))
                {
                    continue;
                }

                if (seen.Contains(c.Id) || seen.Contains(CupKey(c)))
                {
                    continue;
                }

                seen.Add(c.Id);
                seen.Add(CupKey(c));
                fixtures.Add(c);
            }
        }

        private static string CupKey(Fixture f)
        {
            string a = f.HomeName == null ? "" : f.HomeName.Trim().ToLowerInvariant();
            string b = f.AwayName == null ? "" : f.AwayName.Trim().ToLowerInvariant();
            if (string.CompareOrdinal(a, b) > 0)
            {
                string swap = a;
                a = b;
                b = swap;
            }

            return EnsureUtc(f.KickoffUtc).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + a + "|" + b;
        }

        // ------------------------------------------------------------------ next fixtures
        private void RefreshNextFixtures()
        {
            if (_nextBusy || Config.Favorites.Count == 0)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            var due = new List<FavoriteTeam>();
            for (int i = 0; i < Config.Favorites.Count; i++)
            {
                if (NextUp.NeedsWork(Config.Favorites[i].Id, nowUtc))
                {
                    due.Add(Config.Favorites[i]);
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            _nextBusy = true;
            IScoreProvider provider = Provider;
            Task.Run(delegate
            {
                try
                {
                    for (int i = 0; i < due.Count; i++)
                    {
                        FavoriteTeam team = due[i];
                        Fixture best = null;
                        bool ok = false;
                        try
                        {
                            List<Fixture> list = provider.GetTeamFixturesAsync(team.Id, nowUtc.AddHours(-3),
                                nowUtc.AddDays(28), CancellationToken.None).Result;
                            ok = true;
                            if (list != null)
                            {
                                for (int j = 0; j < list.Count; j++)
                                {
                                    Fixture f = list[j];
                                    if (f == null || f.State == MatchState.Finished ||
                                        f.State == MatchState.Cancelled || f.State == MatchState.Postponed)
                                    {
                                        continue;
                                    }

                                    DateTime kickoff = EnsureUtc(f.KickoffUtc);
                                    if (kickoff <= nowUtc.AddMinutes(-5))
                                    {
                                        continue;
                                    }

                                    if (best == null || kickoff < EnsureUtc(best.KickoffUtc))
                                    {
                                        best = f;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Write("next: " + team.Name + " failed: " + Innermost(ex).Message);
                        }

                        if (ok)
                        {
                            NextUp.Put(team.Id, best, nowUtc);
                        }
                        else
                        {
                            NextUp.Fail(team.Id, nowUtc);
                        }

                        // the quota is ten a minute for the whole app; do not burst the sweep
                        if (i + 1 < due.Count)
                        {
                            Thread.Sleep(2000);
                        }
                    }

                    NextUp.Save(Paths.NextFile);
                }
                finally
                {
                    _nextBusy = false;
                }

                if (Card != null && !Card.IsDisposed)
                {
                    try
                    {
                        Card.BeginInvoke((MethodInvoker)delegate { ApplyData(Paused); });
                    }
                    catch
                    {
                    }
                }
            });
        }

        // ------------------------------------------------------------------ kick-off alert
        private void CheckAlerts()
        {
            if (!Config.NotifyBeforeMatch || Tray == null)
            {
                return;
            }

            int minutes = Config.NotifyMinutes > 0 ? Config.NotifyMinutes : 30;
            DateTime nowUtc = DateTime.UtcNow;

            // candidates: the tracked next fixtures, plus anything already in today's feed
            var candidates = new List<Fixture>();
            for (int i = 0; i < Config.Favorites.Count; i++)
            {
                Fixture f = NextUp.For(Config.Favorites[i].Id, nowUtc);
                if (f != null)
                {
                    candidates.Add(f);
                }
            }

            for (int i = 0; i < LastGood.Count; i++)
            {
                Fixture f = LastGood[i];
                if (f != null && IsFavorite(f))
                {
                    candidates.Add(f);
                }
            }

            TimeSpan chosenLeft;
            Fixture chosen = ChooseAlert(candidates, Alerts, nowUtc, minutes, out chosenLeft);
            if (chosen == null)
            {
                return;
            }

            Alerts.Mark(chosen.Id);
            Alerts.Save(Paths.AlertsFile);
            NotifyKickoff(chosen, chosenLeft);
        }

        // The decision, separated from the balloon so it can be tested: pick the nearest fixture
        // that is inside the window, not started, and not already announced. Fixtures that have
        // already kicked off are stamped so they can never be announced late.
        internal static Fixture ChooseAlert(List<Fixture> candidates, MatchAlerts alerts, DateTime nowUtc,
            int minutes, out TimeSpan chosenLeft)
        {
            chosenLeft = TimeSpan.MaxValue;
            Fixture chosen = null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                Fixture f = candidates[i];
                if (f == null || string.IsNullOrEmpty(f.Id) || !seen.Add(f.Id))
                {
                    continue;
                }

                if (f.State == MatchState.Finished || f.State == MatchState.Cancelled ||
                    f.State == MatchState.Postponed || f.IsLiveNow(nowUtc))
                {
                    continue;
                }

                DateTime kickoff = EnsureUtc(f.KickoffUtc);
                TimeSpan left = kickoff - nowUtc;
                if (left <= TimeSpan.Zero)
                {
                    alerts.Mark(f.Id);                  // already under way: never alert late
                    continue;
                }

                if (left > TimeSpan.FromMinutes(minutes))
                {
                    continue;
                }

                if (alerts.HasNotified(f.Id))
                {
                    continue;
                }

                if (left < chosenLeft)
                {
                    chosenLeft = left;
                    chosen = f;
                }
            }

            return chosen;
        }

        private void NotifyKickoff(Fixture f, TimeSpan left)
        {
            int mins = (int)Math.Round(left.TotalMinutes);
            if (mins < 1)
            {
                mins = 1;
            }

            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(f.KickoffUtc), TimeZoneInfo.Local);
            string title = "Kick-off in " + mins.ToString(CultureInfo.InvariantCulture) + " min";
            string body = f.HomeName + " v " + f.AwayName + "  ·  " +
                          local.ToString("HH:mm", CultureInfo.CurrentCulture);
            if (!string.IsNullOrEmpty(f.CompetitionName))
            {
                body = body + "\n" + f.CompetitionName;
            }

            try
            {
                Tray.BalloonTipIcon = ToolTipIcon.Info;
                Tray.BalloonTipTitle = title;
                Tray.BalloonTipText = body;
                Tray.ShowBalloonTip(15000);
                Log.Write("alert: " + title + " - " + f.HomeName + " v " + f.AwayName);
            }
            catch (Exception ex)
            {
                Log.Write("alert failed: " + ex.Message);
            }
        }

        private void OnNetworkChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable)
            {
                Log.Write("network is back; refreshing");
                WakeEngine();
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Log.Write("resumed from sleep; refreshing");
                WakeEngine();
            }
        }

        private void WakeEngine()
        {
            if (_engine != null)
            {
                _engine.Wake();                         // single fetch, no queued burst
            }
        }

        // ---------------------------------------------------------------- menu commands
        internal void RefreshNow()
        {
            WakeEngine();
        }

        internal void ToggleCollapsed()
        {
            Config.Collapsed = !Config.Collapsed;
            if (Card != null)
            {
                Card.SetCollapsed(Config.Collapsed, true);
            }
        }

        internal void SetOpacity(int percent)
        {
            Config.OpacityPercent = percent;
            if (Card != null)
            {
                Card.RefreshVisualState();
            }

            Config.Save(Paths.ConfigFile);
        }

        internal void SetUseEspnScorers(bool on)
        {
            Config.UseEspnScorers = on;
            Config.Save(Paths.ConfigFile);
            if (!on)
            {
                for (int i = 0; i < LastGood.Count; i++)
                {
                    LastGood[i].Scorers.Clear();
                    LastGood[i].ScorersFetchedUtc = DateTime.MinValue;
                }

                ApplyData(false);
            }
        }

        internal void SetCups(bool on)
        {
            Config.Cups = on;
            Config.Save(Paths.ConfigFile);
            if (!on)
            {
                // drop the cup rows immediately: the next poll rebuilds LastGood from the feed
                var kept = new List<Fixture>();
                for (int i = 0; i < LastGood.Count; i++)
                {
                    if (LastGood[i] != null && !LastGood[i].Id.StartsWith("e", StringComparison.Ordinal))
                    {
                        kept.Add(LastGood[i]);
                    }
                }

                LastGood = kept;
                ApplyData(false);
                return;
            }

            CupSource.Invalidate();                     // ask for a fresh cup list on the next tick
        }

        internal void SetNotifyBeforeMatch(bool on)
        {
            Config.NotifyBeforeMatch = on;
            Config.Save(Paths.ConfigFile);
        }

        internal void SetAutoStart(bool on)
        {
            if (on)
            {
                AutoStart.Install();
            }
            else
            {
                AutoStart.Uninstall();
            }
        }

        // Called from the card while a match is open. One scoreboard + one summary call per
        // minute, on a background thread, for that match only.
        internal void MaybeFetchScorers(Fixture f)
        {            if (f == null || _scorers == null || !Config.UseEspnScorers || _scorerBusy)
            {
                return;
            }

            if (f.State == MatchState.Scheduled || string.IsNullOrEmpty(f.Id))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (f.ScorersFetchedUtc != DateTime.MinValue &&
                (now - f.ScorersFetchedUtc).TotalSeconds < 60)
            {
                return;
            }

            f.ScorersFetchedUtc = now;
            _scorerBusy = true;
            string id = f.Id;

            Task.Run(delegate
            {
                List<GoalEvent> got = null;
                try
                {
                    got = _scorers.Get(f, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Write("scorers failed: " + ex.Message);
                }

                if (Card == null || Card.IsDisposed)
                {
                    _scorerBusy = false;
                    return;
                }

                try
                {
                    Card.BeginInvoke((MethodInvoker)delegate
                    {
                        _scorerBusy = false;
                        if (got != null && got.Count >= 0)
                        {
                            for (int i = 0; i < LastGood.Count; i++)
                            {
                                if (string.Equals(LastGood[i].Id, id, StringComparison.Ordinal))
                                {
                                    LastGood[i].Scorers = got;
                                    LastGood[i].ScorersFetchedUtc = now;
                                    break;
                                }
                            }

                            ApplyData(false);
                        }
                    });
                }
                catch
                {
                    _scorerBusy = false;
                }
            });
        }

        internal void SetRefreshSeconds(int seconds)
        {
            Config.RefreshSeconds = seconds;
            Config.Save(Paths.ConfigFile);
            WakeEngine();
        }

        internal void SetNextView(bool on)
        {
            Config.NextView = on;
            Config.Save(Paths.ConfigFile);
            ApplyData(false);
        }

        internal void SetAlwaysOnTop(bool on)
        {
            Config.AlwaysOnTop = on;
            Config.Save(Paths.ConfigFile);
            if (Card != null && !Card.IsDisposed)
            {
                Card.TopMost = on;
                Card.RaiseTemporarily();
            }
        }

        internal void SetLiveScopeAll(bool on)
        {
            Config.LiveScopeAll = on;
            Config.Save(Paths.ConfigFile);
            ApplyData(false);
        }

        internal void ToggleListExpanded()
        {
            Config.ListExpanded = !Config.ListExpanded;
            Config.Save(Paths.ConfigFile);
            if (Card != null && !Card.IsDisposed)
            {
                Card.SetListExpanded(Config.ListExpanded);
            }
        }

        internal void RemoveFavorite(string id)
        {
            for (int i = Config.Favorites.Count - 1; i >= 0; i--)
            {
                if (Config.Favorites[i].Id == id)
                {
                    Config.Favorites.RemoveAt(i);
                }
            }

            Config.Save(Paths.ConfigFile);
            ApplyData(false);
        }

        internal void ShowStartupWizard()
        {
            ShowWizard(1);
        }

        internal void ShowTeamWizard()
        {
            ShowWizard(2);
        }

        internal void ShowCompetitionWizard()
        {
            ShowWizard(3);
        }

        private void ShowWizard(int step)
        {
            bool wasVisible = Card != null && !Card.IsDisposed;
            if (wasVisible)
            {
                Card.HideForFullscreen();
            }

            _wizardShowing = true;

            try
            {
                using (var wizard = new WizardForm(this, step))
                {
                    wizard.ShowDialog();
                }

                Log.Write("wizard closed");
            }
            catch (Exception ex)
            {
                Log.Write("wizard failed: " + ex.Message);
            }

            _wizardShowing = false;
            Config.WizardDone = true;
            Config.Save(Paths.ConfigFile);

            Provider = CreateProvider();
            StartEngine();

            if (Card != null && !Card.IsDisposed)
            {
                if (wasVisible)
                {
                    Card.RestoreFromFullscreen();
                }

                ApplyData(false);
            }
        }

        internal void ShowRequestLog()
        {
            var form = new RequestLogForm();
            form.Show();
        }

        internal void ShowAbout()
        {
            string attribution = Provider == null ? "" : Provider.Attribution;
            if (string.IsNullOrEmpty(attribution))
            {
                attribution = "Mock data - no external source.";
            }

            string text = Program.AppName + " " + Program.AppVersion + Environment.NewLine + Environment.NewLine +
                          "Source: " + (Provider == null ? "n/a" : Provider.DisplayName) + Environment.NewLine +
                          attribution + Environment.NewLine + Environment.NewLine +
                          "Data folder: " + Paths.Dir + Environment.NewLine +
                          "Refresh: every " + Config.RefreshSeconds.ToString(CultureInfo.InvariantCulture) +
                          " s while live (quota gate: " +
                          Config.ApiCallsPerMinute.ToString(CultureInfo.InvariantCulture) +
                          " requests/min)" + Environment.NewLine +
                          "Game time: derived from kickoff, the free feed carries no minute" + Environment.NewLine +
                          "Goal times: minute of the score change we witnessed" + Environment.NewLine +
                          "Scorer names: ESPN site JSON, fetched only for the match you open" +
                          (Config.UseEspnScorers ? "" : " (switched off)");

            MessageBox.Show(text, "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal void Exit()
        {
            AutoStart.WriteStopped();                   // tell the watchdog this was deliberate
            Config.Save(Paths.ConfigFile);
            if (RequestExit != null)
            {
                RequestExit();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch
            {
            }

            if (_watch != null)
            {
                try
                {
                    _watch.Stop();
                    _watch.Dispose();
                }
                catch
                {
                }

                _watch = null;
            }

            StopEngine();

            if (Tray != null)
            {
                try
                {
                    Tray.Visible = false;
                    Tray.Dispose();
                }
                catch
                {
                }

                Tray = null;
            }

            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }

            if (Card != null && !Card.IsDisposed)
            {
                try
                {
                    Card.Close();
                    Card.Dispose();
                }
                catch
                {
                }

                Card = null;
            }

            if (_iconLive != null)
            {
                _iconLive.Dispose();
                _iconLive = null;
            }

            if (_iconIdle != null)
            {
                _iconIdle.Dispose();
                _iconIdle = null;
            }

            try
            {
                if (!ReadOnlyConfig)
                {
                    Config.Save(Paths.ConfigFile);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class PillAppContext : ApplicationContext
    {
        private readonly AppController _app;

        internal PillAppContext()
        {
            _app = new AppController();
            _app.RequestExit = delegate { ExitThread(); };
            _app.Start();

            // once per launch, make sure the two things that can revive the card are still armed
            AutoStart.Heal();

            if (_app.Card != null && !_app.Card.IsDisposed)
            {
                MainForm = _app.Card;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _app != null)
            {
                _app.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Menu - six top level items, built fresh on every open
    // ----------------------------------------------------------------------------------------
    internal static class MenuBuilder
    {
        internal static void Fill(ContextMenuStrip menu, AppController app)
        {
            menu.Items.Clear();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;

            ToolStripMenuItem favorites = new ToolStripMenuItem("Favorites");
            for (int i = 0; i < app.Config.Favorites.Count; i++)
            {
                FavoriteTeam team = app.Config.Favorites[i];
                string id = team.Id;
                ToolStripMenuItem item = new ToolStripMenuItem(team.Name);
                item.Checked = true;
                item.Click += delegate { app.RemoveFavorite(id); };
                favorites.DropDownItems.Add(item);
            }

            if (app.Config.Favorites.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("(none - following competitions)");
                none.Enabled = false;
                favorites.DropDownItems.Add(none);
            }

            favorites.DropDownItems.Add(new ToolStripSeparator());
            favorites.DropDownItems.Add(new ToolStripMenuItem("Add team...", null,
                delegate { app.ShowTeamWizard(); }));
            favorites.DropDownItems.Add(new ToolStripMenuItem("Competitions...", null,
                delegate { app.ShowCompetitionWizard(); }));
            menu.Items.Add(favorites);

            menu.Items.Add(new ToolStripMenuItem("Refresh now", null, delegate { app.RefreshNow(); }));

            ToolStripMenuItem rate = new ToolStripMenuItem("Refresh rate");
            int[] rates = new int[] { 2, 5, 10, 30 };
            for (int i = 0; i < rates.Length; i++)
            {
                int seconds = rates[i];
                ToolStripMenuItem item = new ToolStripMenuItem(
                    "every " + seconds.ToString(CultureInfo.InvariantCulture) + " s");
                item.Checked = app.Config.RefreshSeconds == seconds;
                item.Click += delegate { app.SetRefreshSeconds(seconds); };
                rate.DropDownItems.Add(item);
            }

            rate.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem quota = new ToolStripMenuItem(
                "API quota: " + app.Config.ApiCallsPerMinute.ToString(CultureInfo.InvariantCulture) + "/min");
            quota.Enabled = false;
            rate.DropDownItems.Add(quota);
            menu.Items.Add(rate);

            ToolStripMenuItem expanded = new ToolStripMenuItem("Expanded");
            expanded.Checked = !app.Config.Collapsed;
            expanded.Click += delegate { app.ToggleCollapsed(); };
            menu.Items.Add(expanded);

            ToolStripMenuItem onTop = new ToolStripMenuItem("Always on top");
            onTop.Checked = app.Config.AlwaysOnTop;
            onTop.Click += delegate { app.SetAlwaysOnTop(!app.Config.AlwaysOnTop); };
            menu.Items.Add(onTop);

            ToolStripMenuItem nextView = new ToolStripMenuItem("Next matches");
            nextView.Checked = app.Config.NextView;
            nextView.Click += delegate { app.SetNextView(!app.Config.NextView); };
            menu.Items.Add(nextView);

            ToolStripMenuItem allMatches = new ToolStripMenuItem("All matches");
            allMatches.Checked = app.Config.ListExpanded;
            allMatches.Click += delegate { app.ToggleListExpanded(); };
            menu.Items.Add(allMatches);

            ToolStripMenuItem liveAll = new ToolStripMenuItem("Show all live matches");
            liveAll.Checked = app.Config.LiveScopeAll;
            liveAll.Click += delegate { app.SetLiveScopeAll(!app.Config.LiveScopeAll); };
            menu.Items.Add(liveAll);

            ToolStripMenuItem scorers = new ToolStripMenuItem("Scorer names (ESPN)");
            scorers.Checked = app.Config.UseEspnScorers;
            scorers.Click += delegate { app.SetUseEspnScorers(!app.Config.UseEspnScorers); };
            menu.Items.Add(scorers);

            ToolStripMenuItem cups = new ToolStripMenuItem("Cup matches (ESPN)");
            cups.Checked = app.Config.Cups;
            cups.Click += delegate { app.SetCups(!app.Config.Cups); };
            menu.Items.Add(cups);

            ToolStripMenuItem notify = new ToolStripMenuItem(
                "Notify " + app.Config.NotifyMinutes.ToString(CultureInfo.InvariantCulture) +
                " min before kick-off");
            notify.Checked = app.Config.NotifyBeforeMatch;
            notify.Click += delegate { app.SetNotifyBeforeMatch(!app.Config.NotifyBeforeMatch); };
            menu.Items.Add(notify);

            ToolStripMenuItem autoStart = new ToolStripMenuItem("Start with Windows");
            autoStart.Checked = AutoStart.IsEnabled;
            autoStart.Click += delegate { app.SetAutoStart(!AutoStart.IsEnabled); };
            menu.Items.Add(autoStart);

            menu.Items.Add(new ToolStripMenuItem("Request log...", null, delegate { app.ShowRequestLog(); }));
            menu.Items.Add(new ToolStripMenuItem("About MatchPill...", null, delegate { app.ShowAbout(); }));

            ToolStripMenuItem opacity = new ToolStripMenuItem("Opacity");
            int[] levels = new int[] { 100, 90, 80, 70 };
            for (int i = 0; i < levels.Length; i++)
            {
                int level = levels[i];
                ToolStripMenuItem item = new ToolStripMenuItem(
                    level.ToString(CultureInfo.InvariantCulture) + "%");
                item.Checked = app.Config.OpacityPercent == level;
                item.Click += delegate { app.SetOpacity(level); };
                opacity.DropDownItems.Add(item);
            }

            menu.Items.Add(opacity);
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { app.Exit(); }));
        }
    }

    // ----------------------------------------------------------------------------------------
    //  The card. Custom painted, borderless, never activates, never sits over another app.
    // ----------------------------------------------------------------------------------------
    // ----------------------------------------------------------------------------------------
    //  Crest cache - one small PNG per club, fetched on a worker, decoded once, plus the club's
    //  dominant colour (which the card uses for the edge bars). Painting NEVER waits for the
    //  network: a missing crest paints a tinted square and the download fills it in later.
    // ----------------------------------------------------------------------------------------
    internal static class Crests
    {
        internal const int MaxBytes = 60000;

        private static readonly Dictionary<string, Bitmap> _art =
            new Dictionary<string, Bitmap>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Color> _tint =
            new Dictionary<string, Color>(StringComparer.Ordinal);

        private static readonly HashSet<string> _busy = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _dead = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _lock = new object();
        private static string _dir;

        internal static Action Changed;             // set by the card; marshalled by the card

        internal static Bitmap Art(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            lock (_lock)
            {
                Bitmap cached;
                if (_art.TryGetValue(url, out cached))
                {
                    return cached;
                }
            }

            Kick(url);
            return null;
        }

        internal static Color Tint(string url, string key)
        {
            if (!string.IsNullOrEmpty(url))
            {
                lock (_lock)
                {
                    Color known;
                    if (_tint.TryGetValue(url, out known))
                    {
                        return known;
                    }
                }

                Bitmap art = Art(url);
                if (art != null)
                {
                    Color computed = TintOf(art);
                    lock (_lock)
                    {
                        _tint[url] = computed;
                    }

                    return computed;
                }
            }

            return FallbackTint(key);
        }

        // The colour of the shirt: average the saturated pixels, skip white trim and black
        // outlines. Then lift it until it is actually visible on the dark card.
        internal static Color TintOf(Bitmap bmp)
        {
            if (bmp == null)
            {
                return Color.FromArgb(255, 120, 120, 120);
            }

            try
            {
                int w = bmp.Width;
                int h = bmp.Height;
                if (w < 2 || h < 2)
                {
                    return Color.FromArgb(255, 120, 120, 120);
                }

                int stepX = Math.Max(1, w / 24);
                int stepY = Math.Max(1, h / 24);
                long r = 0;
                long g = 0;
                long b = 0;
                int n = 0;

                for (int y = 0; y < h; y += stepY)
                {
                    for (int x = 0; x < w; x += stepX)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.A < 200)
                        {
                            continue;
                        }

                        int luma = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                        if (luma > 232 || luma < 24)
                        {
                            continue;                   // white kit, black outline
                        }

                        int max = Math.Max(c.R, Math.Max(c.G, c.B));
                        int min = Math.Min(c.R, Math.Min(c.G, c.B));
                        if (max - min < 26)
                        {
                            continue;                   // grey, not a club colour
                        }

                        r += c.R;
                        g += c.G;
                        b += c.B;
                        n++;
                    }
                }

                if (n < 6)
                {
                    return Color.FromArgb(255, 120, 120, 120);
                }

                return Lift(Color.FromArgb(255, (int)(r / n), (int)(g / n), (int)(b / n)));
            }
            catch
            {
                return Color.FromArgb(255, 120, 120, 120);
            }
        }

        private static Color Lift(Color c)
        {
            float luma = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
            if (luma >= 74f && luma <= 200f)
            {
                return c;
            }

            float k = 132f / (luma < 1f ? 1f : luma);
            int r = Clamp255((int)(c.R * k));
            int g = Clamp255((int)(c.G * k));
            int b = Clamp255((int)(c.B * k));
            if (r + g + b < 210)
            {
                r = Clamp255(r + 26);
                g = Clamp255(g + 26);
                b = Clamp255(b + 26);
            }

            return Color.FromArgb(255, r, g, b);
        }

        private static int Clamp255(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        // Deterministic colour for a club whose crest never arrived, so the bar is never blank.
        internal static Color FallbackTint(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Color.FromArgb(255, 120, 120, 120);
            }

            uint h = 2166136261u;
            for (int i = 0; i < key.Length; i++)
            {
                h = (h ^ key[i]) * 16777619u;
            }

            return FromHsv((int)(h % 360u), 0.38f, 0.74f);
        }

        private static Color FromHsv(int hue, float sat, float val)
        {
            float c = val * sat;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = val - c;
            float r = 0f;
            float g = 0f;
            float b = 0f;

            if (hue < 60) { r = c; g = x; }
            else if (hue < 120) { r = x; g = c; }
            else if (hue < 180) { g = c; b = x; }
            else if (hue < 240) { g = x; b = c; }
            else if (hue < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromArgb(255, Clamp255((int)((r + m) * 255f)),
                Clamp255((int)((g + m) * 255f)), Clamp255((int)((b + m) * 255f)));
        }

        private static void Kick(string url)
        {
            lock (_lock)
            {
                if (_busy.Contains(url) || _dead.Contains(url))
                {
                    return;
                }

                _busy.Add(url);
            }

            try
            {
                ThreadPool.QueueUserWorkItem(delegate { Load(url); });
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _busy.Remove(url);
                    _dead.Add(url);
                }

                Log.Write("crest queue failed: " + ex.Message);
            }
        }

        private static void Load(string url)
        {
            Bitmap art = null;
            try
            {
                string path = DiskPath(url);
                byte[] bytes = null;

                try
                {
                    if (File.Exists(path))
                    {
                        bytes = File.ReadAllBytes(path);
                    }
                }
                catch
                {
                    bytes = null;
                }

                if (bytes == null || bytes.Length < 32)
                {
                    bytes = Download(url);
                    if (bytes != null && bytes.Length > 32)
                    {
                        try
                        {
                            File.WriteAllBytes(path, bytes);
                        }
                        catch
                        {
                            // a read-only folder must not stop the crest from being shown
                        }
                    }
                }

                if (bytes != null && bytes.Length > 32 && bytes.Length <= MaxBytes)
                {
                    using (var stream = new MemoryStream(bytes))
                    {
                        art = new Bitmap(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("crest load failed (" + url + "): " + ex.Message);
                art = null;
            }

            lock (_lock)
            {
                _busy.Remove(url);
                if (art == null)
                {
                    _dead.Add(url);
                }
                else
                {
                    _art[url] = art;
                    _tint[url] = TintOf(art);
                }
            }

            Action changed = Changed;
            if (art != null && changed != null)
            {
                try
                {
                    changed();
                }
                catch
                {
                }
            }
        }

        private static byte[] Download(string url)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.UserAgent = "MatchPill/1.5";
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var buffer = new MemoryStream())
                    {
                        var chunk = new byte[8192];
                        int read;
                        int total = 0;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            total += read;
                            if (total > MaxBytes)
                            {
                                return null;            // not a crest
                            }

                            buffer.Write(chunk, 0, read);
                        }

                        return buffer.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("crest fetch failed (" + url + "): " + ex.Message);
                return null;
            }
        }

        internal static string DiskPath(string url)
        {
            if (_dir == null)
            {
                string dir = Path.Combine(Paths.Dir, "crests");
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                }

                _dir = dir;
            }

            uint h = 2166136261u;
            for (int i = 0; i < url.Length; i++)
            {
                h = (h ^ url[i]) * 16777619u;
            }

            return Path.Combine(_dir, h.ToString("x8", CultureInfo.InvariantCulture) + ".png");
        }
    }

    internal sealed class CardForm : Form
    {
        private const int BaseWidth = 320;
        private const int PadX = 13;
        private const int PadY = 12;
        private const int RowH = 70;
        private const int RowGap = 8;
        private const int Radius = 12;
        private const int PillW = 152;
        private const int PillH = 32;
        private const int CollapsedRows = 3;        // before the "N more" row appears
        private const int MaxBlocks = 14;
        private const int MoreRowH = 24;
        private const int GoalRowH = 17;
        private const int MaxGoalRows = 6;
        private const int SwipeThreshold = 46;
        private const int FlashSeconds = 12;

        // Broadcast palette: the card reads like a TV scoreboard, not a table. Dark surface,
        // one bright accent, and the two clubs' own colours on the edges of every row.
        private static readonly Color ColAccent = Color.FromArgb(255, 55, 138, 221);
        private static readonly Color ColCard = Color.FromArgb(255, 22, 24, 29);
        private static readonly Color ColBorder = Color.FromArgb(255, 42, 45, 52);
        private static readonly Color ColComp = Color.FromArgb(255, 134, 134, 133);
        private static readonly Color ColTeam = Color.FromArgb(255, 241, 239, 232);
        private static readonly Color ColScore = Color.FromArgb(255, 255, 255, 255);
        private static readonly Color ColCompDim = Color.FromArgb(255, 96, 97, 99);
        private static readonly Color ColTeamDim = Color.FromArgb(255, 140, 139, 135);
        private static readonly Color ColScoreDim = Color.FromArgb(255, 160, 159, 155);
        private static readonly Color ColLiveBack = Color.FromArgb(255, 60, 33, 37);
        private static readonly Color ColLiveFore = Color.FromArgb(255, 240, 149, 149);
        private static readonly Color ColLiveDot = Color.FromArgb(255, 226, 75, 74);
        private static readonly Color ColPausedBack = Color.FromArgb(255, 58, 45, 24);
        private static readonly Color ColPausedFore = Color.FromArgb(255, 239, 159, 39);
        private static readonly Color ColDoneBack = Color.FromArgb(255, 44, 45, 49);
        private static readonly Color ColDoneFore = Color.FromArgb(255, 150, 150, 148);
        private static readonly Color ColDoneBackDim = Color.FromArgb(255, 38, 40, 44);
        private static readonly Color ColDoneForeDim = Color.FromArgb(255, 118, 118, 116);
        private static readonly Color ColDotIdle = Color.FromArgb(255, 150, 145, 136);
        private static readonly Color ColBarBack = Color.FromArgb(255, 58, 59, 62);
        private static readonly Color ColHair = Color.FromArgb(255, 44, 45, 49);
        private static readonly Color ColChipBack = Color.FromArgb(255, 38, 40, 45);
        private static readonly Color ColMuted = Color.FromArgb(255, 116, 116, 115);

        private static readonly StringFormat SfLeft = MakeFormat(StringAlignment.Near);
        private static readonly StringFormat SfRight = MakeFormat(StringAlignment.Far);
        private static readonly StringFormat SfCenter = MakeFormat(StringAlignment.Center);

        private readonly AppController _app;
        private readonly List<Block> _blocks = new List<Block>();
        private readonly List<Fixture> _view = new List<Fixture>();

        private readonly SolidBrush _bCard = new SolidBrush(ColCard);
        private readonly SolidBrush _bComp = new SolidBrush(ColComp);
        private readonly SolidBrush _bTeam = new SolidBrush(ColTeam);
        private readonly SolidBrush _bScore = new SolidBrush(ColScore);
        private readonly SolidBrush _bCompDim = new SolidBrush(ColCompDim);
        private readonly SolidBrush _bTeamDim = new SolidBrush(ColTeamDim);
        private readonly SolidBrush _bScoreDim = new SolidBrush(ColScoreDim);
        private readonly SolidBrush _bLiveBack = new SolidBrush(ColLiveBack);
        private readonly SolidBrush _bLiveFore = new SolidBrush(ColLiveFore);
        private readonly SolidBrush _bPausedBack = new SolidBrush(ColPausedBack);
        private readonly SolidBrush _bPausedFore = new SolidBrush(ColPausedFore);
        private readonly SolidBrush _bDoneBack = new SolidBrush(ColDoneBack);
        private readonly SolidBrush _bDoneFore = new SolidBrush(ColDoneFore);
        private readonly SolidBrush _bDoneBackDim = new SolidBrush(ColDoneBackDim);
        private readonly SolidBrush _bDoneForeDim = new SolidBrush(ColDoneForeDim);
        private readonly SolidBrush _bAccent = new SolidBrush(ColAccent);
        private readonly SolidBrush _bDotIdle = new SolidBrush(ColDotIdle);
        private readonly SolidBrush _bDotStale = new SolidBrush(Color.FromArgb(255, 214, 158, 46));
        private readonly SolidBrush _bBarBack = new SolidBrush(ColBarBack);
        private readonly SolidBrush _bHair = new SolidBrush(ColHair);
        private readonly SolidBrush _bNotch = new SolidBrush(Color.FromArgb(170, 255, 255, 255));
        private readonly SolidBrush _bLiveDot = new SolidBrush(ColLiveDot);
        private readonly SolidBrush _bChipBack = new SolidBrush(ColChipBack);
        private readonly SolidBrush _bMuted = new SolidBrush(ColMuted);
        private readonly SolidBrush _bOnTint = new SolidBrush(Color.FromArgb(255, 250, 250, 250));
        private readonly Pen _pBorder = new Pen(ColBorder, 1f);

        private Font _fComp;
        private Font _fTeam;
        private Font _fScore;
        private Font _fBadge;
        private Font _fPill;
        private Font _fSub;
        private Font _fClock;
        private Font _fBig;
        private Font _fBigScore;
        private Font _fGoal;
        private Font _fGoalBold;
        private Font _fHint;
        private Font _fRowScore;
        private GraphicsPath _bgPath;

        private float _scale = 1f;
        private bool _relayouting;
        private bool _resourcesDisposed;
        private bool _shown;
        private bool _dragging;
        private bool _moved;
        private Point _dragStartScreen;
        private Point _dragStartLocation;
        private Point _dragStartClient;
        private int _dragDx;
        private bool _suppressSave;
        private bool _stale;
        private bool _paused;
        private int _lastLiveCount = -1;
        private int _scroll;
        private int _contentHeight;
        private int _lastW = -1;
        private int _lastH = -1;
        private string _detailId = "";
        private DateTime _lastSuccessUtc = DateTime.MinValue;
        private readonly List<string> _nextLines = new List<string>();

        internal CardForm(AppController app)
        {
            _app = app;

            // a crest that arrives a second after the first paint must show up without waiting
            // for the next poll
            Crests.Changed = delegate
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        return;
                    }

                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed)
                        {
                            Invalidate();
                        }
                    });
                }
                catch
                {
                }
            };

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = app.Config.AlwaysOnTop;            // off = desktop layer, on = above other windows
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ColCard;
            Text = Program.AppName;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        // Win+D ("show desktop") minimises every window, the widget included. A desktop widget
        // that vanishes exactly when you look at the desktop is useless, so swallow that one
        // message. Nothing else in the app minimises the card, so there is no downside.
        protected override void WndProc(ref Message m)
        {
            const int WmSysCommand = 0x0112;
            const int ScMinimize = 0xF020;
            if (m.Msg == WmSysCommand && (m.WParam.ToInt32() & 0xFFF0) == ScMinimize)
            {
                return;
            }

            base.WndProc(ref m);
        }

        // Normal window styles for maximum compatibility; special behaviours are handled
        // via TopMost=false and manual z-order management instead of exotic ExStyles.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                return cp;
            }
        }

        private static StringFormat MakeFormat(StringAlignment alignment)
        {
            var format = new StringFormat(StringFormat.GenericTypographic);
            format.Alignment = alignment;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = format.FormatFlags | StringFormatFlags.NoWrap;
            return format;
        }

        private int S(int logical)
        {
            int value = (int)Math.Round(logical * _scale);
            return value < 1 ? 1 : value;
        }

        // ------------------------------------------------------------------ lifecycle
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateScale();
            Log.Write("card created; exstyle=0x" + ((long)CreateParams.ExStyle).ToString("X", CultureInfo.InvariantCulture));
            Relayout();                                 // fonts exist now, so the first paint is correct
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _scale = e.DeviceDpiNew / 96f;
            if (_scale < 0.75f)
            {
                _scale = 0.75f;
            }

            _bgPath = null;
            try
            {
                Bounds = e.SuggestedRectangle;
            }
            catch
            {
            }

            Relayout();
            SaveBounds();
        }

        private void UpdateScale()
        {
            float scale = 1f;
            try
            {
                scale = DeviceDpi / 96f;
            }
            catch
            {
                scale = 1f;
            }

            if (scale < 0.75f)
            {
                scale = 0.75f;
            }

            if (scale > 4f)
            {
                scale = 4f;
            }

            _scale = scale;
            RebuildFonts();
        }

        private void RebuildFonts()
        {
            DisposeFonts();
            _fComp = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _fTeam = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            _fScore = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            _fBadge = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
            _fPill = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            _fSub = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _fClock = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
            _fBig = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
            _fBigScore = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Point);
            _fGoal = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _fGoalBold = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
            _fHint = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
            _fRowScore = new Font("Segoe UI", 14.5f, FontStyle.Bold, GraphicsUnit.Point);
        }

        private void DisposeFonts()
        {
            if (_fComp != null)
            {
                _fComp.Dispose();
                _fComp = null;
            }

            if (_fTeam != null)
            {
                _fTeam.Dispose();
                _fTeam = null;
            }

            if (_fScore != null)
            {
                _fScore.Dispose();
                _fScore = null;
            }

            if (_fBadge != null)
            {
                _fBadge.Dispose();
                _fBadge = null;
            }

            if (_fPill != null)
            {
                _fPill.Dispose();
                _fPill = null;
            }

            if (_fSub != null)
            {
                _fSub.Dispose();
                _fSub = null;
            }

            if (_fClock != null)
            {
                _fClock.Dispose();
                _fClock = null;
            }

            if (_fBig != null)
            {
                _fBig.Dispose();
                _fBig = null;
            }

            if (_fBigScore != null)
            {
                _fBigScore.Dispose();
                _fBigScore = null;
            }

            if (_fGoal != null)
            {
                _fGoal.Dispose();
                _fGoal = null;
            }

            if (_fGoalBold != null)
            {
                _fGoalBold.Dispose();
                _fGoalBold = null;
            }

            if (_fHint != null)
            {
                _fHint.Dispose();
                _fHint = null;
            }

            if (_fRowScore != null)
            {
                _fRowScore.Dispose();
                _fRowScore = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_resourcesDisposed)
            {
                _resourcesDisposed = true;
                Crests.Changed = null;
                DisposeFonts();
                _bCard.Dispose();
                _bComp.Dispose();
                _bTeam.Dispose();
                _bScore.Dispose();
                _bCompDim.Dispose();
                _bTeamDim.Dispose();
                _bScoreDim.Dispose();
                _bLiveBack.Dispose();
                _bLiveFore.Dispose();
                _bPausedBack.Dispose();
                _bPausedFore.Dispose();
                _bDoneBack.Dispose();
                _bDoneFore.Dispose();
                _bDoneBackDim.Dispose();
                _bDoneForeDim.Dispose();
                _bAccent.Dispose();
                _bDotIdle.Dispose();
                _bDotStale.Dispose();
                _bBarBack.Dispose();
                _bHair.Dispose();
                _bNotch.Dispose();
                _bLiveDot.Dispose();
                _bChipBack.Dispose();
                _bMuted.Dispose();
                _bOnTint.Dispose();
                _pBorder.Dispose();
                if (_bgPath != null)
                {
                    _bgPath.Dispose();
                    _bgPath = null;
                }
            }

            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ public surface
        internal float ScaleFactor
        {
            get { return _scale; }
        }

        internal void ShowCard()
        {
            if (!_shown)
            {
                _shown = true;
                Visible = true;
                return;
            }

            Visible = true;
        }

        internal void SendToBottom()
        {
            Native.SendToBottom(Handle);
        }

        internal void RaiseTemporarily()
        {
            BringToFront();                             // only when the user asks for it (tray click)
        }

        internal void HideForFullscreen()
        {
            Native.HideWindow(Handle);
        }

        internal void RestoreFromFullscreen()
        {
            Visible = true;
        }

        internal void ApplyInitialBounds()
        {
            Rectangle bounds = _app.Config.Collapsed ? _app.Config.CollapsedBounds : _app.Config.ExpandedBounds;
            _suppressSave = true;
            try
            {
                bool monitorAlive = string.IsNullOrEmpty(_app.Config.Monitor) || MonitorExists(_app.Config.Monitor);
                if (bounds.Width > 0 && bounds.Height > 0 && IsOnAnyScreen(bounds) && monitorAlive)
                {
                    Bounds = bounds;
                }
                else
                {
                    PlaceBottomRight();
                }
            }
            finally
            {
                _suppressSave = false;
            }

            Relayout();
        }

        // Called from the watch timer. A monitor that was unplugged, a resolution change, or a
        // corrupted position must never leave the card parked outside every screen.
        internal void RescueIfOffScreen()
        {
            bool monitorAlive = string.IsNullOrEmpty(_app.Config.Monitor) || MonitorExists(_app.Config.Monitor);
            if (!IsOnAnyScreen(Bounds) || !monitorAlive)
            {
                PlaceBottomRight();
                if (!Visible)
                {
                    ShowCard();
                }

                Log.Write("rescue: card was off screen, moved to " + Bounds);
            }
            else if (!Visible)
            {
                ShowCard();                             // never leave it hidden by accident
                Log.Write("rescue: card was hidden, shown again");
            }
        }

        private void PlaceBottomRight()
        {            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int w = S(_app.Config.Collapsed ? PillW : BaseWidth);
            int h = S(_app.Config.Collapsed ? PillH : 240);
            Location = new Point(area.Right - w - S(28), area.Bottom - h - S(28));
        }

        private static bool IsOnAnyScreen(Rectangle bounds)
        {
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].WorkingArea.IntersectsWith(bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MonitorExists(string deviceName)
        {
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (string.Equals(screens[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal void SaveBounds()
        {
            if (_app == null || _suppressSave)
            {
                return;
            }

            if (_app.Config.Collapsed)
            {
                _app.Config.CollapsedBounds = Bounds;
            }
            else
            {
                _app.Config.ExpandedBounds = Bounds;
            }

            try
            {
                Screen screen = Screen.FromControl(this);
                if (screen != null)
                {
                    _app.Config.Monitor = screen.DeviceName;
                }
            }
            catch
            {
            }

            if (!_app.ReadOnlyConfig)
            {
                _app.Config.Save(Paths.ConfigFile);
            }
        }

        internal void SetCollapsed(bool collapsed, bool save)
        {
            bool wasCollapsed = _app.Config.Collapsed;
            _app.Config.Collapsed = collapsed;
            _scroll = 0;
            Relayout();

            if (!collapsed && wasCollapsed)
            {
                Rectangle stored = _app.Config.ExpandedBounds;
                if (stored.Width > 0 && stored.Height > 0 && IsOnAnyScreen(stored))
                {
                    Location = new Point(stored.X, stored.Y);
                }
            }

            if (save)
            {
                SaveBounds();
            }

            Invalidate();
        }

        internal void SetListExpanded(bool expanded)
        {
            _app.Config.ListExpanded = expanded;
            _app.Config.Save(Paths.ConfigFile);
            _scroll = 0;
            Relayout();
            Invalidate();
        }

        internal void SetData(List<Fixture> view, DateTime lastSuccessUtc, bool paused, List<string> nextLines)
        {
            _view.Clear();
            if (view != null)
            {
                _view.AddRange(view);
            }

            _lastSuccessUtc = lastSuccessUtc;
            _paused = paused;
            _nextLines.Clear();
            if (nextLines != null)
            {
                _nextLines.AddRange(nextLines);
            }
            _stale = IsStaleNow();

            // a refresh every couple of seconds must not yank the card: keep the scroll position
            // and the match you opened, drop the detail only when that match has left the feed
            if (_detailId.Length > 0 && DetailFixture() == null)
            {
                _detailId = "";
            }

            ApplyOpacity();
            Relayout();
            Invalidate();
        }

        private bool IsStaleNow()
        {
            if (_lastSuccessUtc == DateTime.MinValue)
            {
                return true;                            // nothing ever fetched
            }

            return DateTime.UtcNow - _lastSuccessUtc > TimeSpan.FromMinutes(10);
        }

        internal void RefreshVisualState()
        {
            bool stale = IsStaleNow();
            int live = CountLive();
            bool dirty = false;

            if (stale != _stale)
            {
                _stale = stale;
                dirty = true;
            }

            if (live != _lastLiveCount)
            {
                _lastLiveCount = live;
                dirty = true;
            }

            ApplyOpacity();

            Fixture open = DetailFixture();
            if (open != null)
            {
                _app.MaybeFetchScorers(open);       // one call a minute, only for the open match
            }

            // the running clock has to move on every tick, so any live match forces a repaint
            if (dirty || live > 0 || _detailId.Length > 0 || HasGoalFlash())
            {
                Invalidate();
            }
        }

        private bool HasGoalFlash()
        {
            DateTime nowUtc = DateTime.UtcNow;
            for (int i = 0; i < _view.Count; i++)
            {
                Fixture f = _view[i];
                if (f.GoalFlashUtc != DateTime.MinValue &&
                    (nowUtc - f.GoalFlashUtc).TotalSeconds < FlashSeconds)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyOpacity()
        {
            double user = _app.Config.OpacityPercent / 100.0;
            double factor = _stale ? 0.6 : 1.0;
            double value = user * factor;
            if (value < 0.25)
            {
                value = 0.25;
            }

            if (value > 1.0)
            {
                value = 1.0;
            }

            try
            {
                if (Math.Abs(Opacity - value) > 0.005)
                {
                    Opacity = value;
                }
            }
            catch
            {
            }
        }

        private int CountLive()
        {
            DateTime nowUtc = DateTime.UtcNow;
            int count = 0;
            for (int i = 0; i < _view.Count; i++)
            {
                if (_view[i].IsLiveNow(nowUtc))
                {
                    count++;
                }
            }

            return count;
        }

        private DateTime NextKickoffUtc()
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime best = DateTime.MinValue;
            for (int i = 0; i < _view.Count; i++)
            {
                Fixture f = _view[i];
                if (f.State != MatchState.Scheduled)
                {
                    continue;
                }

                DateTime kickoff = AppController.EnsureUtc(f.KickoffUtc);
                if (kickoff <= nowUtc)
                {
                    continue;
                }

                if (best == DateTime.MinValue || kickoff < best)
                {
                    best = kickoff;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ detail navigation
        private Fixture DetailFixture()
        {
            if (_detailId.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < _view.Count; i++)
            {
                if (string.Equals(_view[i].Id, _detailId, StringComparison.Ordinal))
                {
                    return _view[i];
                }
            }

            return null;
        }

        private int DetailIndex()
        {
            for (int i = 0; i < _view.Count; i++)
            {
                if (string.Equals(_view[i].Id, _detailId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        internal void OpenDetail(Fixture f)
        {
            if (f == null)
            {
                return;
            }

            _detailId = f.Id;
            _scroll = 0;
            Relayout();
            Invalidate();
        }

        internal void CloseDetail()
        {
            if (_detailId.Length == 0)
            {
                return;
            }

            _detailId = "";
            Relayout();
            Invalidate();
        }

        // swipe / wheel: move one match left or right in the detail view
        private void StepDetail(int delta)
        {
            int index = DetailIndex();
            if (index < 0)
            {
                return;
            }

            int next = index + delta;
            if (next < 0 || next >= _view.Count)
            {
                return;                                 // no wrap: the ends are the ends
            }

            _detailId = _view[next].Id;
            Relayout();
            Invalidate();
        }

        private Fixture NeighbourFixture(int delta)
        {
            int index = DetailIndex();
            if (index < 0)
            {
                return null;
            }

            int next = index + delta;
            if (next < 0 || next >= _view.Count)
            {
                return null;
            }

            return _view[next];
        }

        // ------------------------------------------------------------------ layout
        private void Relayout()
        {
            if (_relayouting)
            {
                return;
            }

            _relayouting = true;
            try
            {
                DoRelayout();
            }
            finally
            {
                _relayouting = false;
            }
        }

        private void DoRelayout()
        {
            if (_fTeam == null || _app == null)
            {
                return;
            }

            if (_app.Config.Collapsed)
            {
                ApplySize(S(PillW), S(PillH));
                _blocks.Clear();                        // no stale hit areas behind the pill
                return;
            }

            Fixture detail = DetailFixture();
            if (detail != null)
            {
                _blocks.Clear();
                ApplySize(S(BaseWidth), DetailHeight(detail));
                return;
            }

            SizeList();
        }

        private void SizeList()
        {
            int width = S(BaseWidth);
            int rows = VisibleRowCount();
            bool more = HasMoreRow();

            int content;
            if (_view.Count == 0)
            {
                content = S(58);
            }
            else
            {
                content = rows * S(RowH) + Math.Max(0, rows - 1) * S(RowGap);
                if (more)
                {
                    content += S(MoreRowH) + S(RowGap);
                }
            }

            int desired = content + S(PadY) * 2;
            ApplySize(width, desired);
            _contentHeight = content;
            ClampScroll();
            BuildBlocks();
            Invalidate();
        }

        private static List<GoalEvent> GoalRows(Fixture f, out bool withPlayer)
        {
            // only trust the scorer source when it accounts for every goal on the board,
            // otherwise the list would disagree with the scoreline next to it
            if (f.Scorers.Count > 0 && f.Scorers.Count == f.TotalGoals)
            {
                withPlayer = true;
                return f.Scorers;
            }

            withPlayer = false;
            return f.Goals;
        }

        private int DetailHeight(Fixture f)
        {
            bool withPlayer;
            int goals = GoalRows(f, out withPlayer).Count;
            if (goals > MaxGoalRows)
            {
                goals = MaxGoalRows;
            }

            if (goals < 1)
            {
                goals = 1;
            }

            int content = S(PadY) * 2 + S(18) + S(10) + S(30) * 2 + S(8) + S(4) + S(12) +
                          S(14) + S(GoalRowH) * goals + S(10) + S(16);
            return content;
        }

        private void ApplySize(int width, int desired)
        {
            int maxHeight = 0;
            try
            {
                Screen screen = Screen.FromControl(this);
                if (screen != null)
                {
                    maxHeight = (int)(screen.WorkingArea.Height * 0.78);
                }
            }
            catch
            {
            }

            if (maxHeight > 0 && desired > maxHeight)
            {
                desired = maxHeight;
            }

            if (desired < S(40))
            {
                desired = S(40);
            }

            if (Width != width || Height != desired)
            {
                Size = new Size(width, desired);
            }

            if (_lastW != Width || _lastH != Height)
            {
                _lastW = Width;
                _lastH = Height;
                _bgPath = null;
                UpdateRegion();
            }
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            try
            {
                using (GraphicsPath path = RoundRect(new Rectangle(0, 0, Width, Height), S(Radius)))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null)
                    {
                        previous.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            if (diameter <= 0 || rect.Width < diameter || rect.Height < diameter)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ClampScroll()
        {
            int viewport = Height - S(PadY) * 2;
            int overflow = _contentHeight - viewport;
            if (overflow <= 0)
            {
                _scroll = 0;
                return;
            }

            if (_scroll > overflow)
            {
                _scroll = overflow;
            }

            if (_scroll < 0)
            {
                _scroll = 0;
            }
        }

        private int VisibleRowCount()
        {
            int limit = _app.Config.ListExpanded ? MaxBlocks : CollapsedRows;
            return _view.Count > limit ? limit : _view.Count;
        }

        private bool HasMoreRow()
        {
            return !_app.Config.ListExpanded && _view.Count > CollapsedRows;
        }

        private void BuildBlocks()
        {
            _blocks.Clear();
            if (_view.Count == 0)
            {
                return;
            }

            int padX = S(PadX);
            int padY = S(PadY);
            int rowH = S(RowH);
            int gap = S(RowGap);
            int headH = S(14);
            int crestSize = S(22);
            int footH = S(12);
            int left = padX + S(2);
            int right = Width - padX - S(2);
            int center = Width / 2;
            int y = padY - _scroll;
            DateTime nowUtc = DateTime.UtcNow;

            int rows = VisibleRowCount();
            for (int i = 0; i < rows; i++)
            {
                Fixture f = _view[i];
                var block = new Block();
                block.Fixture = f;
                block.Top = y;
                block.Height = rowH;
                block.Live = f.IsLiveNow(nowUtc);
                block.Finished = f.State == MatchState.Finished;
                block.CompText = f.CompetitionName;
                block.BadgeIsClock = f.State == MatchState.Live;
                // the row shows the minute the way a TV overlay does; the seconds live in the
                // detail view, where there is room for them
                block.BadgeText = MatchClock.Badge(f, nowUtc);

                // looking ahead, the badge has to say which day: "18:30" alone is useless
                // for a fixture on Saturday
                if (_app.Config.NextView && f.State == MatchState.Scheduled)
                {
                    DateTime localKick = TimeZoneInfo.ConvertTimeFromUtc(
                        AppController.EnsureUtc(f.KickoffUtc), TimeZoneInfo.Local);
                    block.BadgeText = localKick.Date == DateTime.Today
                        ? localKick.ToString("HH:mm", CultureInfo.CurrentCulture)
                        : localKick.ToString("ddd HH:mm", CultureInfo.CurrentCulture);
                }
                block.IsScheduledBadge = f.State == MatchState.Scheduled;
                block.HomeText = f.HomeName;
                block.AwayText = f.AwayName;
                // a match in play always shows a score; the feed can lag a few seconds behind
                // the kick-off, and an empty cell next to a running clock looks broken
                block.HomeScoreText = f.HomeScore.HasValue
                    ? f.HomeScore.Value.ToString(CultureInfo.InvariantCulture)
                    : (block.Live || f.State == MatchState.Paused ? "0" : "");
                block.AwayScoreText = f.AwayScore.HasValue
                    ? f.AwayScore.Value.ToString(CultureInfo.InvariantCulture)
                    : (block.Live || f.State == MatchState.Paused ? "0" : "");
                block.Progress = f.State == MatchState.Live || f.State == MatchState.Paused
                    ? MatchClock.Progress(f, nowUtc)
                    : -1f;
                block.Flash = FlashStrength(f, nowUtc);

                if (block.HomeScoreText.Length > 0 || block.AwayScoreText.Length > 0)
                {
                    block.ScoreText = block.HomeScoreText + " : " + block.AwayScoreText;
                }
                else
                {
                    block.ScoreText = "v";
                    block.ScoreMuted = true;
                }

                block.ChipText = string.IsNullOrEmpty(f.CompetitionCode)
                    ? FirstTla(f.CompetitionName)
                    : f.CompetitionCode;
                block.HeadText = f.Matchday > 0
                    ? "Matchday " + f.Matchday.ToString(CultureInfo.InvariantCulture)
                    : f.CompetitionName;

                block.FootLeft = FootLeftText(f, block, nowUtc);
                block.FootRight = FootRightText(f);
                block.HomeTint = Crests.Tint(f.HomeCrest, f.HomeId);
                block.AwayTint = Crests.Tint(f.AwayCrest, f.AwayId);
                block.HomeTla = f.HomeTla;
                block.AwayTla = f.AwayTla;

                // a scheduled row only needs room for "v", so the names get the space back
                int scoreHalf = block.ScoreMuted ? S(18) : S(26);

                int badgeWidth = S(30);
                if (block.BadgeText.Length > 0)
                {
                    Font font = block.BadgeIsClock ? _fClock : _fBadge;
                    Size measured = TextRenderer.MeasureText(block.BadgeText, font);
                    badgeWidth = measured.Width + S(10);
                    if (block.Live)
                    {
                        badgeWidth += S(9);             // room for the pulsing dot
                    }
                }

                block.BadgeWidth = badgeWidth;
                block.BadgeHeight = S(15);

                int chipWidth = TextRenderer.MeasureText(block.ChipText, _fBadge).Width + S(9);

                int nameLeft = left + crestSize + S(5);
                int nameRight = right - crestSize - S(5);

                block.ChipRect = new Rectangle(left, y + S(2), chipWidth, S(14));
                block.CompRect = new Rectangle(left + chipWidth + S(6), y + S(2),
                    Math.Max(S(30), right - badgeWidth - S(8) - left - chipWidth - S(6)), headH);
                block.BadgeRect = new Rectangle(right - badgeWidth, y + S(2), badgeWidth, block.BadgeHeight);
                block.HomeCrestRect = new Rectangle(left, y + S(21), crestSize, crestSize);
                block.AwayCrestRect = new Rectangle(right - crestSize, y + S(21), crestSize, crestSize);
                block.HomeRect = new Rectangle(nameLeft, y + S(20), Math.Max(S(24), center - scoreHalf - nameLeft), S(25));
                block.AwayRect = new Rectangle(center + scoreHalf, y + S(20),
                    Math.Max(S(24), nameRight - center - scoreHalf), S(25));
                block.ScoreRect = new Rectangle(center - scoreHalf, y + S(20), scoreHalf * 2, S(25));
                block.FootLeftRect = new Rectangle(left, y + S(49), Math.Max(S(30), center - left), footH);
                block.FootRightRect = new Rectangle(center, y + S(49), Math.Max(S(30), right - center), footH);

                _blocks.Add(block);
                y += rowH + gap;
            }

            if (HasMoreRow())
            {
                var more = new Block();
                more.IsMore = true;
                more.Top = y;
                more.Height = S(MoreRowH);
                more.MoreText = (_view.Count - rows).ToString(CultureInfo.InvariantCulture) + " more matches";
                _blocks.Add(more);
            }
        }

        private static string FirstTla(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            string trimmed = name.Trim();
            return trimmed.Length <= 3 ? trimmed.ToUpperInvariant() : trimmed.Substring(0, 3).ToUpperInvariant();
        }

        private static string FootLeftText(Fixture f, Block b, DateTime nowUtc)
        {
            if (f.State == MatchState.Scheduled && !b.Live)
            {
                return CountdownText(f.KickoffUtc, nowUtc);
            }

            // a shoot-out is the whole story of a cup tie: say it before the half-time score
            if (!string.IsNullOrEmpty(f.Note))
            {
                return f.Note;
            }

            if (f.HalfTimeHome.HasValue && f.HalfTimeAway.HasValue)
            {
                return "HT " + f.HalfTimeHome.Value.ToString(CultureInfo.InvariantCulture) + "\u2013" +
                       f.HalfTimeAway.Value.ToString(CultureInfo.InvariantCulture);
            }

            return "";
        }

        private static string FootRightText(Fixture f)
        {
            string venue = f.Venue == null ? "" : f.Venue.Trim();
            string referee = f.Referee == null ? "" : f.Referee.Trim();
            if (venue.Length > 0 && referee.Length > 0)
            {
                return venue + " \u00B7 " + referee;
            }

            return venue.Length > 0 ? venue : referee;
        }

        internal static string CountdownText(DateTime kickoffUtc, DateTime nowUtc)
        {
            TimeSpan left = AppController.EnsureUtc(kickoffUtc) - nowUtc;
            if (left.TotalSeconds <= 0)
            {
                return "kicking off";
            }

            if (left.TotalDays >= 1)
            {
                return "in " + ((int)left.TotalDays).ToString(CultureInfo.InvariantCulture) + "d " +
                       left.Hours.ToString(CultureInfo.InvariantCulture) + "h";
            }

            if (left.TotalHours >= 1)
            {
                return "in " + ((int)left.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " +
                       left.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            }

            if (left.TotalMinutes >= 1)
            {
                return "in " + ((int)left.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
            }

            return "in " + Math.Max(1, (int)left.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
        }

        private static float FlashStrength(Fixture f, DateTime nowUtc)
        {
            if (f.GoalFlashUtc == DateTime.MinValue)
            {
                return 0f;
            }

            double age = (nowUtc - f.GoalFlashUtc).TotalSeconds;
            if (age < 0 || age > FlashSeconds)
            {
                return 0f;
            }

            return (float)(1.0 - age / FlashSeconds);
        }

        // ------------------------------------------------------------------ painting
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // fully custom painted
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_bgPath == null)
            {
                _bgPath = RoundRect(rect, S(Radius));
            }

            g.FillPath(_bCard, _bgPath);
            g.DrawPath(_pBorder, _bgPath);

            if (_app.Config.Collapsed)
            {
                DrawPill(g);
            }
            else
            {
                Fixture detail = DetailFixture();
                if (detail != null)
                {
                    DrawDetailSwipe(g, detail);
                }
                else if (_view.Count == 0)
                {
                    DrawEmpty(g);
                }
                else
                {
                    DrawBlocks(g);
                }
            }

            if (_paused || _stale)
            {
                int dot = S(6);
                g.FillEllipse(_paused ? _bDotStale : _bDotIdle, Width - S(PadX) - dot, S(4), dot, dot);
            }
        }

        private void DrawPill(Graphics g)
        {
            int live = CountLive();
            int cy = Height / 2;
            int left = S(11);

            if (live > 0)
            {
                Fixture clocked = MostAdvancedLive();
                int crest = S(16);
                // crests only when the pill is about one match: pairing two badges with a count
                // would suggest those two clubs are the ones at 78'
                if (clocked != null && live == 1)
                {
                    DrawPillCrest(g, new Rectangle(left, cy - crest / 2, crest, crest),
                        clocked.HomeCrest, Crests.Tint(clocked.HomeCrest, clocked.HomeId), clocked.HomeTla);
                    DrawPillCrest(g, new Rectangle(left + S(10), cy - crest / 2, crest, crest),
                        clocked.AwayCrest, Crests.Tint(clocked.AwayCrest, clocked.AwayId), clocked.AwayTla);
                }

                int x = live == 1 ? left + S(10) + crest + S(9) : left;
                int rightEdge = Width - S(11);
                string minute = clocked == null ? "" : MatchClock.Badge(clocked, DateTime.UtcNow);
                int minuteW = minute.Length == 0 ? 0 : TextRenderer.MeasureText(minute, _fSub).Width + S(4);

                if (live == 1 && clocked != null && clocked.HomeScore.HasValue && clocked.AwayScore.HasValue)
                {
                    string score = clocked.HomeScore.Value.ToString(CultureInfo.InvariantCulture) + " : " +
                                   clocked.AwayScore.Value.ToString(CultureInfo.InvariantCulture);
                    g.DrawString(score, _fPill, _bScore,
                        new RectangleF(x, 0, Math.Max(S(10), rightEdge - minuteW - S(9) - x), Height), SfLeft);
                }
                else
                {
                    int dot = S(5);
                    g.FillEllipse(_bLiveDot, x, cy - dot / 2, dot, dot);
                    string text = live.ToString(CultureInfo.InvariantCulture) + " live";
                    g.DrawString(text, _fPill, _bTeam,
                        new RectangleF(x + dot + S(5), 0,
                            Math.Max(S(10), rightEdge - minuteW - S(6) - x - dot - S(5)), Height), SfLeft);
                }

                if (minute.Length > 0)
                {
                    g.DrawString(minute, _fSub, _bLiveFore,
                        new RectangleF(rightEdge - minuteW, 0, minuteW, Height), SfRight);
                }

                return;
            }

            string line;
            DateTime next = NextKickoffUtc();
            if (next != DateTime.MinValue)
            {
                DateTime localNext = TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
                line = "Next " + (localNext.Date == DateTime.Today
                    ? localNext.ToString("HH:mm", CultureInfo.CurrentCulture)
                    : localNext.ToString("ddd HH:mm", CultureInfo.CurrentCulture));
            }
            else
            {
                line = "No matches";
            }

            if (_paused)
            {
                line = line + "  paused";
            }

            int idleDot = S(6);
            g.FillEllipse(_bDotIdle, left, cy - idleDot / 2, idleDot, idleDot);
            g.DrawString(line, _fPill, _bTeam,
                new RectangleF(left + idleDot + S(8), 0, Width - left - idleDot - S(16), Height), SfLeft);
        }

        private void DrawPillCrest(Graphics g, Rectangle rect, string url, Color tint, string tla)
        {
            var ring = new Rectangle(rect.Left - S(1), rect.Top - S(1), rect.Width + S(2), rect.Height + S(2));
            using (GraphicsPath path = RoundRect(ring, S(5)))
            {
                g.FillPath(_bCard, path);
            }

            DrawCrest(g, rect, url, tla, tint);
        }

        private Fixture MostAdvancedLive()
        {
            DateTime nowUtc = DateTime.UtcNow;
            Fixture best = null;
            int bestMinute = -1;
            for (int i = 0; i < _view.Count; i++)
            {
                Fixture f = _view[i];
                if (!f.IsLiveNow(nowUtc))
                {
                    continue;
                }

                int minute = MatchClock.Minute(f, nowUtc);
                if (minute > bestMinute)
                {
                    bestMinute = minute;
                    best = f;
                }
            }

            return best;
        }

        private void DrawEmpty(Graphics g)
        {
            // a 32 px pill has room for one line; an expanded card can list three
            bool headline = Height >= S(70);
            int lineH = S(15);
            int show = _nextLines.Count;
            if (show == 0)
            {
                show = 1;
            }

            if (show > 3)
            {
                show = 3;
            }

            if (!headline)
            {
                show = 1;
            }

            int block = (headline ? S(18) : 0) + show * lineH;
            int top = (Height - block) / 2;
            if (headline)
            {
                g.DrawString("No matches today", _fTeam, _bTeam,
                    new RectangleF(S(10), top, Width - S(20), S(18)), SfCenter);
                top += S(18);
            }

            for (int i = 0; i < show; i++)
            {
                string text = i < _nextLines.Count ? _nextLines[i] : "";
                g.DrawString(text, _fSub, _bComp,
                    new RectangleF(S(10), top + i * lineH, Width - S(20), lineH), SfCenter);
            }
        }

        private void DrawBlocks(Graphics g)
        {
            int firstMatch = -1;
            int lastMatch = -1;
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].IsMore)
                {
                    continue;
                }

                if (firstMatch < 0)
                {
                    firstMatch = i;
                }

                lastMatch = i;
            }

            bool moreRow = HasMoreRow();
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (b.Top + b.Height < 0 || b.Top > Height)
                {
                    continue;
                }

                if (b.IsMore)
                {
                    DrawMoreRow(g, b);
                    continue;
                }

                DrawRow(g, b, i == firstMatch, i == lastMatch && !moreRow);
            }
        }

        private void DrawRow(Graphics g, Block b, bool isFirst, bool isLast)
        {
            // ---- club colours down the edges: full bleed on a single-row card, one tab per row
            // when several matches are listed
            int barTop = isFirst ? 0 : b.Top;
            int barBottom = isLast ? Height : b.Top + b.Height;
            int barH = Math.Max(1, barBottom - barTop);
            using (var homeBar = new SolidBrush(b.HomeTint))
            using (var awayBar = new SolidBrush(b.AwayTint))
            {
                g.FillRectangle(homeBar, 0, barTop, S(3), barH);
                g.FillRectangle(awayBar, Width - S(3), barTop, S(3), barH);
            }

            if (b.Flash > 0f)
            {
                int alpha = (int)(b.Flash * 30f);
                using (var wash = new SolidBrush(Color.FromArgb(alpha, ColAccent)))
                {
                    g.FillRectangle(wash, S(4), b.Top + S(1), Width - S(8), b.Height - S(2));
                }
            }

            SolidBrush compBrush = b.Finished ? _bCompDim : _bComp;
            SolidBrush teamBrush = b.Finished ? _bTeamDim : _bTeam;
            SolidBrush scoreBrush = b.Finished ? _bScoreDim : _bScore;
            SolidBrush mutedBrush = b.Finished ? _bCompDim : _bMuted;

            if (b.Flash > 0.15f && b.Live)
            {
                scoreBrush = _bLiveFore;
            }

            // ---- header: competition chip, matchday, minute
            if (b.ChipText.Length > 0)
            {
                using (GraphicsPath chip = RoundRect(b.ChipRect, S(4)))
                {
                    g.FillPath(_bChipBack, chip);
                }

                g.DrawString(b.ChipText, _fBadge, compBrush, b.ChipRect, SfCenter);
            }

            g.DrawString(b.HeadText, _fComp, compBrush, b.CompRect, SfLeft);
            DrawBadge(g, b);

            // ---- crests
            DrawCrest(g, b.HomeCrestRect, b.Fixture.HomeCrest, b.HomeTla, b.HomeTint);
            DrawCrest(g, b.AwayCrestRect, b.Fixture.AwayCrest, b.AwayTla, b.AwayTint);

            // ---- names hug the score, the way a TV overlay does
            g.DrawString(b.HomeText, _fTeam, teamBrush, b.HomeRect, SfRight);
            g.DrawString(b.AwayText, _fTeam, teamBrush, b.AwayRect, SfLeft);
            g.DrawString(b.ScoreText, b.ScoreMuted ? _fSub : _fRowScore,
                b.ScoreMuted ? mutedBrush : scoreBrush, b.ScoreRect, SfCenter);

            // ---- footer: half-time score on the left, venue and referee on the right
            if (b.FootLeft.Length > 0)
            {
                g.DrawString(b.FootLeft, _fHint, mutedBrush, b.FootLeftRect, SfLeft);
            }

            if (b.FootRight.Length > 0)
            {
                g.DrawString(b.FootRight, _fHint, mutedBrush, b.FootRightRect, SfRight);
            }

            if (b.Progress >= 0f)
            {
                DrawProgress(g, b);
            }
        }

        // Two segments, split at the interval: the gap is the half-time line, so "we are past
        // the break" is readable without counting pixels.
        private void DrawProgress(Graphics g, Block b)
        {
            int barY = b.Top + S(RowH) - S(6);
            int barLeft = S(PadX) + S(2);
            int barRight = Width - S(PadX) - S(2);
            int gap = S(2);
            int half = (barRight - barLeft - gap) / 2;
            if (half < S(6))
            {
                return;
            }

            int h = S(3);
            float first = b.Progress / 0.5f;
            float second = (b.Progress - 0.5f) / 0.5f;
            if (first > 1f)
            {
                first = 1f;
            }

            if (second < 0f)
            {
                second = 0f;
            }

            if (second > 1f)
            {
                second = 1f;
            }

            g.FillRectangle(_bBarBack, barLeft, barY, half, h);
            g.FillRectangle(_bBarBack, barLeft + half + gap, barY, half, h);

            int fillOne = (int)(half * first);
            if (fillOne > 0)
            {
                g.FillRectangle(_bAccent, barLeft, barY, fillOne, h);
            }

            int fillTwo = (int)(half * second);
            if (fillTwo > 0)
            {
                g.FillRectangle(_bAccent, barLeft + half + gap, barY, fillTwo, h);
            }
        }

        private void DrawCrest(Graphics g, Rectangle rect, string url, string tla, Color tint)
        {
            Bitmap art = null;
            try
            {
                art = Crests.Art(url);
            }
            catch
            {
                art = null;
            }

            if (art != null)
            {
                try
                {
                    InterpolationMode previous = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(art, rect);
                    g.InterpolationMode = previous;
                    return;
                }
                catch
                {
                    // a broken image must not take the whole card down
                }
            }

            try
            {
                using (GraphicsPath path = RoundRect(rect, S(6)))
                using (var fill = new SolidBrush(tint))
                {
                    g.FillPath(fill, path);
                }

                if (tla != null && tla.Length > 0 && rect.Width >= S(18))
                {
                    // three letters in a 22 px square only fit at a smaller size; a single
                    // initial always reads better than "M…"
                    string mark = tla.Length <= 2 ? tla : tla.Substring(0, 1);
                    if (TextRenderer.MeasureText(tla, _fHint).Width <= rect.Width - S(4))
                    {
                        mark = tla;
                    }

                    g.DrawString(mark, _fHint, _bOnTint, rect, SfCenter);
                }
            }
            catch
            {
            }
        }

        private void DrawMoreRow(Graphics g, Block b)
        {
            g.FillRectangle(_bHair, S(PadX), b.Top, Width - S(PadX) * 2, 1);
            string text = b.MoreText + "  \u25BE";
            g.DrawString(text, _fSub, _bComp,
                new RectangleF(S(PadX), b.Top, Width - S(PadX) * 2, b.Height), SfCenter);
        }

        private void DrawBadge(Graphics g, Block b)
        {
            if (b.BadgeText.Length == 0)
            {
                return;
            }

            if (b.IsScheduledBadge)
            {
                g.DrawString(b.BadgeText, _fBadge, _bComp, b.BadgeRect, SfRight);
                return;
            }

            SolidBrush back;
            SolidBrush fore;

            if (b.Fixture.State == MatchState.Paused)
            {
                back = _bPausedBack;
                fore = _bPausedFore;
            }
            else if (b.Live)
            {
                back = _bLiveBack;
                fore = _bLiveFore;
            }
            else
            {
                back = b.Finished ? _bDoneBackDim : _bDoneBack;
                fore = b.Finished ? _bDoneForeDim : _bDoneFore;
            }

            using (GraphicsPath pill = RoundRect(b.BadgeRect, S(4)))
            {
                g.FillPath(back, pill);
            }

            if (b.Live)
            {
                int dot = S(5);
                int cy = b.BadgeRect.Top + b.BadgeRect.Height / 2;
                int x = b.BadgeRect.Left + S(6);
                g.FillEllipse(_bLiveDot, x, cy - dot / 2, dot, dot);
                var textRect = new Rectangle(x + dot + S(4), b.BadgeRect.Top,
                    b.BadgeRect.Width - (x - b.BadgeRect.Left) - dot - S(8), b.BadgeRect.Height);
                g.DrawString(b.BadgeText, _fClock, fore, textRect, SfLeft);
                return;
            }

            g.DrawString(b.BadgeText, b.BadgeIsClock ? _fClock : _fBadge, fore, b.BadgeRect, SfCenter);
        }

        // ------------------------------------------------------------------ detail view
        private void DrawDetailSwipe(Graphics g, Fixture current)
        {
            if (_dragDx != 0)
            {
                Fixture neighbour = NeighbourFixture(_dragDx > 0 ? -1 : 1);
                if (neighbour != null)
                {
                    int shift = _dragDx > 0 ? -Width : Width;
                    GraphicsState state = g.Save();
                    g.TranslateTransform(shift, 0);
                    DrawDetail(g, neighbour);
                    g.Restore(state);
                }
            }

            GraphicsState own = g.Save();
            g.TranslateTransform(_dragDx, 0);
            DrawDetail(g, current);
            g.Restore(own);
        }

        private void DrawDetail(Graphics g, Fixture f)
        {
            int padX = S(14);
            int left = padX;
            int right = Width - padX;
            int y = S(PadY);
            DateTime nowUtc = DateTime.UtcNow;
            bool live = f.IsLiveNow(nowUtc);

            // ---- header: back chevron + competition + clock
            int headH = S(18);
            var headRect = new Rectangle(0, y, Width, headH);
            g.DrawString("\u2039", _fBig, _bComp,
                new RectangleF(left, y - S(3), S(12), headH + S(6)), SfLeft);
            g.DrawString(f.CompetitionName, _fComp, _bComp,
                new RectangleF(left + S(13), y, right - left - S(80), headH), SfLeft);

            string badge = MatchClock.Badge(f, nowUtc);
            if (live || f.State == MatchState.Paused)
            {
                string text = f.State == MatchState.Paused ? "HT" : MatchClock.Running(f, nowUtc);
                Size measured = TextRenderer.MeasureText(text, _fClock);
                int bw = measured.Width + S(12);
                var badgeRect = new Rectangle(right - bw, y, bw, headH);
                g.FillRectangle(f.State == MatchState.Paused ? _bPausedBack : _bLiveBack, badgeRect);
                g.DrawString(text, _fClock, f.State == MatchState.Paused ? _bPausedFore : _bLiveFore,
                    badgeRect, SfCenter);
            }
            else if (badge.Length > 0)
            {
                g.DrawString(badge, _fBadge, _bComp, new RectangleF(right - S(60), y, S(60), headH), SfRight);
            }

            y += headH + S(10);

            // ---- scoreline
            int rowH = S(30);
            int scoreW = S(46);
            SolidBrush teamBrush = f.State == MatchState.Finished ? _bTeamDim : _bTeam;
            SolidBrush scoreBrush = f.State == MatchState.Finished ? _bScoreDim : _bScore;
            if (live && FlashStrength(f, nowUtc) > 0.15f)
            {
                scoreBrush = _bLiveFore;
            }

            int crestSize = S(22);
            int crestY = y + (rowH - crestSize) / 2;
            DrawCrest(g, new Rectangle(left, crestY, crestSize, crestSize),
                f.HomeCrest, f.HomeTla, Crests.Tint(f.HomeCrest, f.HomeId));
            g.DrawString(f.HomeName, _fBig, teamBrush,
                new RectangleF(left + crestSize + S(8), y, right - left - scoreW - crestSize - S(12), rowH), SfLeft);
            g.DrawString(f.HomeScore.HasValue ? f.HomeScore.Value.ToString(CultureInfo.InvariantCulture) : "-",
                _fBigScore, scoreBrush, new RectangleF(right - scoreW, y, scoreW, rowH), SfRight);
            y += rowH;
            crestY = y + (rowH - crestSize) / 2;
            DrawCrest(g, new Rectangle(left, crestY, crestSize, crestSize),
                f.AwayCrest, f.AwayTla, Crests.Tint(f.AwayCrest, f.AwayId));
            g.DrawString(f.AwayName, _fBig, teamBrush,
                new RectangleF(left + crestSize + S(8), y, right - left - scoreW - crestSize - S(12), rowH), SfLeft);
            g.DrawString(f.AwayScore.HasValue ? f.AwayScore.Value.ToString(CultureInfo.InvariantCulture) : "-",
                _fBigScore, scoreBrush, new RectangleF(right - scoreW, y, scoreW, rowH), SfRight);
            y += rowH + S(8);

            // ---- progress + half time
            int barH = S(4);
            g.FillRectangle(_bBarBack, left, y, right - left, barH);
            float progress = live || f.State == MatchState.Paused || f.State == MatchState.Finished
                ? MatchClock.Progress(f, nowUtc)
                : 0f;
            if (f.State == MatchState.Finished)
            {
                progress = 1f;
            }

            int filled = (int)((right - left) * progress);
            if (filled > 0)
            {
                g.FillRectangle(f.State == MatchState.Finished ? _bDoneFore : _bAccent,
                    left, y, filled, barH);
            }

            g.FillRectangle(_bNotch, left + (right - left) / 2, y, S(2), barH);   // half time notch
            y += barH + S(12);

            string half = "";
            if (f.HalfTimeHome.HasValue && f.HalfTimeAway.HasValue && f.State != MatchState.Scheduled)
            {
                half = "HT " + f.HalfTimeHome.Value.ToString(CultureInfo.InvariantCulture) + "-" +
                       f.HalfTimeAway.Value.ToString(CultureInfo.InvariantCulture);
            }

            // ---- goals
            g.DrawString("GOALS", _fBadge, _bComp, new RectangleF(left, y, S(120), S(14)), SfLeft);
            if (half.Length > 0)
            {
                g.DrawString(half, _fBadge, _bComp, new RectangleF(right - S(80), y, S(80), S(14)), SfRight);
            }

            y += S(14);

            bool withPlayer;
            List<GoalEvent> rows = GoalRows(f, out withPlayer);
            if (rows.Count == 0)
            {
                string note = f.State == MatchState.Scheduled ? "Not started" : "No goals";
                g.DrawString(note, _fGoal, _bCompDim,
                    new RectangleF(left, y, right - left, S(GoalRowH)), SfLeft);
                y += S(GoalRowH);
            }
            else
            {
                int shown = rows.Count > MaxGoalRows ? MaxGoalRows : rows.Count;
                for (int i = 0; i < shown; i++)
                {
                    DrawGoalRow(g, f, rows[i], left, right, y, withPlayer);
                    y += S(GoalRowH);
                }
            }

            // ---- footer: where you are in the list, and how to move
            y += S(10);
            int count = _view.Count;
            int index = DetailIndex();
            if (count > 1 && index >= 0)
            {
                if (count <= 7)
                {
                    int dot = S(5);
                    int gap = S(5);
                    for (int i = 0; i < count; i++)
                    {
                        g.FillEllipse(i == index ? _bAccent : _bBarBack, left + i * (dot + gap), y + S(5), dot, dot);
                    }
                }
                else
                {
                    g.DrawString("swipe \u2039 \u203A", _fHint, _bCompDim,
                        new RectangleF(left, y, S(90), S(16)), SfLeft);
                }

                string hint = (index + 1).ToString(CultureInfo.InvariantCulture) + " / " +
                              count.ToString(CultureInfo.InvariantCulture);
                g.DrawString(hint, _fHint, _bComp, new RectangleF(right - S(90), y, S(90), S(16)), SfRight);
            }
            else if (count == 1)
            {
                g.DrawString("click to go back", _fHint, _bCompDim,
                    new RectangleF(left, y, right - left, S(16)), SfLeft);
            }
        }

        private void DrawGoalRow(Graphics g, Fixture f, GoalEvent e, int left, int right, int y,
            bool withPlayer)
        {
            int minuteW = S(34);
            g.DrawString(MinuteText(e), _fGoal, _bCompDim,
                new RectangleF(left, y, minuteW, S(GoalRowH)), SfLeft);

            int dot = S(5);
            g.FillEllipse(e.HomeScored ? _bAccent : _bComp, left + minuteW + S(2), y + S(6), dot, dot);

            string team = e.HomeScored ? f.HomeName : f.AwayName;
            string running = e.HomeAfter.ToString(CultureInfo.InvariantCulture) + "-" +
                             e.AwayAfter.ToString(CultureInfo.InvariantCulture);
            int textLeft = left + minuteW + S(13);
            int textRight = right - S(34);

            if (withPlayer && e.Player.Length > 0)
            {
                int available = textRight - textLeft;
                Size measured = TextRenderer.MeasureText(e.Player, _fGoalBold);
                int playerWidth = measured.Width + S(2);
                int ceiling = (int)(available * 0.62);
                if (playerWidth > ceiling)
                {
                    playerWidth = ceiling;
                }

                g.DrawString(e.Player, _fGoalBold, _bTeam,
                    new RectangleF(textLeft, y, playerWidth, S(GoalRowH)), SfLeft);

                int teamLeft = textLeft + playerWidth + S(4);
                if (textRight - teamLeft > S(34))
                {
                    g.DrawString("\u00B7 " + team, _fGoal, _bCompDim,
                        new RectangleF(teamLeft, y, textRight - teamLeft, S(GoalRowH)), SfLeft);
                }
            }
            else
            {
                g.DrawString(team, _fGoal, _bTeam,
                    new RectangleF(textLeft, y, textRight - textLeft, S(GoalRowH)), SfLeft);
            }

            g.DrawString(running, _fScore, _bScore,
                new RectangleF(right - S(34), y, S(34), S(GoalRowH)), SfRight);
        }

        private static string MinuteText(GoalEvent e)
        {
            if (e.MinuteLabel.Length > 0)
            {
                return e.MinuteLabel;               // the source already wrote "45+2'"
            }

            if (e.Minute <= 0)
            {
                return "-";
            }

            if (e.Minute > 90)
            {
                return "90+" + (e.Minute - 90).ToString(CultureInfo.InvariantCulture);
            }

            return e.Minute.ToString(CultureInfo.InvariantCulture) + "'";
        }

        // ------------------------------------------------------------------ input
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _moved = false;
                _dragDx = 0;
                _dragStartScreen = Cursor.Position;
                _dragStartLocation = Location;
                _dragStartClient = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }

            Point now = Cursor.Position;
            int dx = now.X - _dragStartScreen.X;
            int dy = now.Y - _dragStartScreen.Y;

            bool detail = !_app.Config.Collapsed && DetailFixture() != null;
            if (detail && Math.Abs(dx) > 4 && Math.Abs(dx) > Math.Abs(dy))
            {
                _moved = true;
                _dragDx = dx;                           // content follows the pointer: a real swipe
                Invalidate();
                return;
            }

            if (!_moved && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            {
                _moved = true;
            }

            if (_moved)
            {
                Location = new Point(_dragStartLocation.X + dx, _dragStartLocation.Y + dy);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                ShowMenu(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left || !_dragging)
            {
                return;
            }

            bool moved = _moved;
            int swipe = _dragDx;
            _dragging = false;
            _moved = false;
            _dragDx = 0;

            bool detail = !_app.Config.Collapsed && DetailFixture() != null;
            if (detail && Math.Abs(swipe) >= S(SwipeThreshold))
            {
                StepDetail(swipe < 0 ? 1 : -1);
                return;
            }

            if (detail)
            {
                Invalidate();
                if (!moved && e.X <= S(48))
                {
                    CloseDetail();                      // tap the chevron: back to the list
                }

                return;
            }

            if (moved)
            {
                SaveBounds();
                return;
            }

            Block block = FindBlock(e.Location);
            if (block == null)
            {
                _app.ToggleCollapsed();
                return;
            }

            if (block.IsMore)
            {
                SetListExpanded(true);
                return;
            }

            if (block.Fixture != null)
            {
                OpenDetail(block.Fixture);
            }
        }

        private void ShowMenu(Point location)
        {
            ContextMenuStrip menu = _app.Tray == null ? null : _app.Tray.ContextMenuStrip;
            if (menu == null)
            {
                return;
            }

            menu.Show(this, location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Fixture target = null;
            Fixture detail = DetailFixture();
            if (detail != null)
            {
                target = detail;
            }
            else
            {
                Block block = FindBlock(e.Location);
                if (block != null && !block.IsMore)
                {
                    target = block.Fixture;
                }
            }

            if (target == null || string.IsNullOrEmpty(target.PageUrl))
            {
                return;
            }

            try
            {
                Process.Start(target.PageUrl);
            }
            catch (Exception ex)
            {
                Log.Write("open fixture page failed: " + ex.Message);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_app.Config.Collapsed)
            {
                return;
            }

            if (DetailFixture() != null)
            {
                StepDetail(e.Delta < 0 ? 1 : -1);       // wheel = swipe between matches
                return;
            }

            if (_contentHeight <= Height - S(PadY) * 2)
            {
                return;
            }

            _scroll -= e.Delta;
            ClampScroll();
            BuildBlocks();
            Invalidate();
        }

        private Block FindBlock(Point location)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                Block b = _blocks[i];
                if (location.Y >= b.Top && location.Y <= b.Top + b.Height)
                {
                    return b;
                }
            }

            return null;
        }

        private sealed class Block
        {
            internal Fixture Fixture;
            internal int Top;
            internal int Height;
            internal int BadgeWidth;
            internal int BadgeHeight;
            internal bool Live;
            internal bool Finished;
            internal bool IsScheduledBadge;
            internal bool BadgeIsClock;
            internal bool IsMore;
            internal float Progress;
            internal float Flash;
            internal string CompText = "";
            internal string ChipText = "";
            internal string HeadText = "";
            internal string ScoreText = "";
            internal string FootLeft = "";
            internal string FootRight = "";
            internal string HomeTla = "";
            internal string AwayTla = "";
            internal bool ScoreMuted;
            internal Color HomeTint = Color.Gray;
            internal Color AwayTint = Color.Gray;
            internal string BadgeText = "";
            internal string MoreText = "";
            internal string HomeText = "";
            internal string AwayText = "";
            internal string HomeScoreText = "";
            internal string AwayScoreText = "";
            internal Rectangle ChipRect;
            internal Rectangle HomeCrestRect;
            internal Rectangle AwayCrestRect;
            internal Rectangle ScoreRect;
            internal Rectangle FootLeftRect;
            internal Rectangle FootRightRect;
            internal Rectangle CompRect;
            internal Rectangle BadgeRect;
            internal Rectangle HomeRect;
            internal Rectangle AwayRect;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  First run wizard (3 steps, skippable)
    // ----------------------------------------------------------------------------------------
    internal sealed class WizardForm : Form
    {
        private static readonly string[,] CompetitionTable = new string[,]
        {
            { "PL", "Premier League (England)" },
            { "PD", "LaLiga (Spain)" },
            { "SA", "Serie A (Italy)" },
            { "BL1", "Bundesliga (Germany)" },
            { "FL1", "Ligue 1 (France)" },
            { "CL", "UEFA Champions League" },
            { "ELC", "Championship (England)" },
            { "DED", "Eredivisie (Netherlands)" },
            { "PPL", "Primeira Liga (Portugal)" },
            { "BSA", "Campeonato Brasileiro" },
            { "EC", "European Championship" },
            { "CLI", "Copa Libertadores" }
        };

        private readonly AppController _app;
        private int _step;
        private readonly Panel _body = new Panel();
        private readonly Label _title = new Label();
        private readonly Label _status = new Label();
        private readonly Button _back = new Button();
        private readonly Button _next = new Button();
        private readonly Button _skip = new Button();

        private string _draftKey = "";
        private readonly List<FavoriteTeam> _draftFavorites = new List<FavoriteTeam>();
        private readonly List<TeamResult> _searchResults = new List<TeamResult>();
        private readonly HashSet<string> _draftCompetitions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private TextBox _keyBox;
        private TextBox _searchBox;
        private ListBox _resultsList;
        private ListBox _favoritesList;
        private CheckedListBox _competitionList;
        private IScoreProvider _searchProvider;
        private string _searchKey = "";
        private bool _busy;

        internal WizardForm(AppController app, int startStep)
        {
            _app = app;
            _step = startStep < 1 ? 1 : (startStep > 3 ? 3 : startStep);

            Text = Program.AppName + " setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(470, 380);
            BackColor = Color.FromArgb(250, 249, 246);

            _draftKey = app.Config.ApiKey;
            for (int i = 0; i < app.Config.Favorites.Count; i++)
            {
                var copy = new FavoriteTeam();
                copy.Id = app.Config.Favorites[i].Id;
                copy.Name = app.Config.Favorites[i].Name;
                copy.CompetitionCode = app.Config.Favorites[i].CompetitionCode;
                _draftFavorites.Add(copy);
            }

            for (int i = 0; i < app.Config.Competitions.Count; i++)
            {
                _draftCompetitions.Add(app.Config.Competitions[i]);
            }

            _title.SetBounds(18, 14, 434, 22);
            _title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _title.Text = "Step " + _step.ToString(CultureInfo.InvariantCulture) + " of 3";

            _body.SetBounds(10, 40, 450, 280);

            _status.SetBounds(18, 326, 240, 20);
            _status.Font = new Font("Segoe UI", 8f);
            _status.ForeColor = Color.FromArgb(110, 106, 98);
            _status.Text = "";

            _back.SetBounds(196, 348, 80, 26);
            _back.Text = "Back";
            _back.Click += delegate { GoBack(); };

            _next.SetBounds(282, 348, 80, 26);
            _next.Text = "Next";
            _next.Click += delegate { GoNext(); };

            _skip.SetBounds(368, 348, 84, 26);
            _skip.Text = "Skip";
            _skip.Click += delegate { Finish(); };

            Controls.Add(_title);
            Controls.Add(_body);
            Controls.Add(_status);
            Controls.Add(_back);
            Controls.Add(_next);
            Controls.Add(_skip);

            BuildStep();
        }

        private void GoBack()
        {
            if (_busy)
            {
                return;
            }

            CaptureStep();
            if (_step > 1)
            {
                _step--;
                BuildStep();
            }
        }

        private void GoNext()
        {
            if (_busy)
            {
                return;
            }

            CaptureStep();
            if (_step < 3)
            {
                _step++;
                BuildStep();
            }
            else
            {
                Finish();
            }
        }

        private void CaptureStep()
        {
            if (_step == 1 && _keyBox != null)
            {
                _draftKey = _keyBox.Text.Trim();
            }
            else if (_step == 3 && _competitionList != null)
            {
                _draftCompetitions.Clear();
                CheckedListBox.CheckedIndexCollection checkedItems = _competitionList.CheckedIndices;
                for (int i = 0; i < checkedItems.Count; i++)
                {
                    int index = checkedItems[i];
                    if (index >= 0 && index < CompetitionTable.GetLength(0))
                    {
                        _draftCompetitions.Add(CompetitionTable[index, 0]);
                    }
                }
            }
        }

        private void BuildStep()
        {
            _body.SuspendLayout();
            while (_body.Controls.Count > 0)
            {
                Control child = _body.Controls[0];
                _body.Controls.RemoveAt(0);
                child.Dispose();
            }

            _keyBox = null;
            _searchBox = null;
            _resultsList = null;
            _favoritesList = null;
            _competitionList = null;

            _title.Text = "Step " + _step.ToString(CultureInfo.InvariantCulture) + " of 3";
            _back.Enabled = _step > 1;
            _next.Text = _step == 3 ? "Finish" : "Next";

            if (_step == 1)
            {
                BuildStepOne();
            }
            else if (_step == 2)
            {
                BuildStepTwo();
            }
            else
            {
                BuildStepThree();
            }

            _body.ResumeLayout();
        }

        private Label AddLabel(string text, int x, int y, int width, int height, float size, bool bold)
        {
            var label = new Label();
            label.SetBounds(x, y, width, height);
            label.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            label.Text = text;
            label.ForeColor = Color.FromArgb(52, 50, 46);
            _body.Controls.Add(label);
            return label;
        }

        private void BuildStepOne()
        {
            AddLabel("Paste your football-data.org API key", 8, 6, 420, 20, 10f, true);
            AddLabel("Register with your e-mail, copy the token from your dashboard, paste it below.\n" +
                     "Free tier: 10 requests per minute, the big European competitions, live scores.\n" +
                     "Leave it empty to run on built-in demo data.", 8, 30, 430, 56, 8.5f, false);

            _keyBox = new TextBox();
            _keyBox.SetBounds(8, 92, 430, 24);
            _keyBox.Text = _draftKey;
            _body.Controls.Add(_keyBox);

            var link = new LinkLabel();
            link.SetBounds(8, 122, 430, 20);
            link.Text = "Get a free key: football-data.org/client/register";
            link.LinkClicked += delegate { OpenUrl("https://www.football-data.org/client/register"); };
            _body.Controls.Add(link);

            AddLabel("The key is stored only in config.json next to MatchPill.exe. It is never logged " +
                     "and never sent anywhere else.", 8, 150, 430, 40, 8f, false);
        }

        private void BuildStepTwo()
        {
            AddLabel("Search and add your teams", 8, 6, 420, 20, 10f, true);

            _searchBox = new TextBox();
            _searchBox.SetBounds(8, 30, 330, 24);
            _body.Controls.Add(_searchBox);

            var search = new Button();
            search.SetBounds(346, 29, 90, 26);
            search.Text = "Search";
            search.Click += delegate { DoSearch(); };
            _body.Controls.Add(search);

            _resultsList = new ListBox();
            _resultsList.SetBounds(8, 64, 250, 130);
            _body.Controls.Add(_resultsList);

            var add = new Button();
            add.SetBounds(266, 64, 170, 26);
            add.Text = "Add selected team";
            add.Click += delegate { AddSelectedTeam(); };
            _body.Controls.Add(add);

            var remove = new Button();
            remove.SetBounds(266, 96, 170, 26);
            remove.Text = "Remove selected";
            remove.Click += delegate { RemoveSelectedTeam(); };
            _body.Controls.Add(remove);

            AddLabel("Favorites", 266, 128, 170, 18, 8.5f, true);

            _favoritesList = new ListBox();
            _favoritesList.SetBounds(266, 148, 170, 46);
            _body.Controls.Add(_favoritesList);

            AddLabel("Without an API key this searches the built-in demo teams.", 8, 200, 430, 20, 8f, false);
            RefreshFavoritesList();
        }

        private void BuildStepThree()
        {
            AddLabel("Which competitions do you follow?", 8, 6, 420, 20, 10f, true);

            _competitionList = new CheckedListBox();
            _competitionList.SetBounds(8, 30, 430, 200);
            _competitionList.CheckOnClick = true;
            for (int i = 0; i < CompetitionTable.GetLength(0); i++)
            {
                string code = CompetitionTable[i, 0];
                _competitionList.Items.Add(CompetitionTable[i, 1] + "  [" + code + "]",
                    _draftCompetitions.Contains(code));
            }

            _body.Controls.Add(_competitionList);
            AddLabel("These are the competitions the free tier covers. Live-first sorting follows " +
                     "this order.", 8, 234, 430, 20, 8f, false);
        }

        private void RefreshFavoritesList()
        {
            if (_favoritesList == null)
            {
                return;
            }

            _favoritesList.Items.Clear();
            for (int i = 0; i < _draftFavorites.Count; i++)
            {
                _favoritesList.Items.Add(_draftFavorites[i].Name);
            }
        }

        private async void DoSearch()
        {
            if (_busy || _searchBox == null)
            {
                return;
            }

            string query = _searchBox.Text.Trim();
            if (query.Length < 2)
            {
                _status.Text = "Type at least two characters.";
                return;
            }

            _busy = true;
            _status.Text = "Searching...";

            try
            {
                if (_searchProvider == null || _searchKey != _draftKey)
                {
                    _searchProvider = _app.CreateProviderForSearch(_draftKey);
                    _searchKey = _draftKey;
                }

                List<TeamResult> results = await _searchProvider.SearchTeamAsync(query, CancellationToken.None);

                _searchResults.Clear();
                _searchResults.AddRange(results);

                if (_resultsList != null)
                {
                    _resultsList.Items.Clear();
                    for (int i = 0; i < _searchResults.Count; i++)
                    {
                        _resultsList.Items.Add(new TeamItem(_searchResults[i]));
                    }
                }

                _status.Text = _searchResults.Count == 0
                    ? "No team matched. Check the competition list in step 3."
                    : _searchResults.Count.ToString(CultureInfo.InvariantCulture) + " result(s).";
            }
            catch (Exception ex)
            {
                _status.Text = "Search failed: " + ex.Message;
            }
            finally
            {
                _busy = false;
            }
        }

        private void AddSelectedTeam()
        {
            if (_resultsList == null || _resultsList.SelectedItem == null)
            {
                return;
            }

            var item = _resultsList.SelectedItem as TeamItem;
            if (item == null)
            {
                return;
            }

            for (int i = 0; i < _draftFavorites.Count; i++)
            {
                if (_draftFavorites[i].Id == item.Team.Id)
                {
                    return;
                }
            }

            var favorite = new FavoriteTeam();
            favorite.Id = item.Team.Id;
            favorite.Name = item.Team.Name;
            favorite.CompetitionCode = item.Team.CompetitionCode;
            _draftFavorites.Add(favorite);
            RefreshFavoritesList();
            _status.Text = item.Team.Name + " added.";
        }

        private void RemoveSelectedTeam()
        {
            if (_favoritesList == null || _favoritesList.SelectedIndex < 0)
            {
                return;
            }

            int index = _favoritesList.SelectedIndex;
            if (index < _draftFavorites.Count)
            {
                _draftFavorites.RemoveAt(index);
                RefreshFavoritesList();
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                Log.Write("open url failed: " + ex.Message);
            }
        }

        // Competition order is the live-first sort key, so it must be stable, not hash order.
        private List<string> OrderedCompetitions()
        {
            var ordered = new List<string>();
            for (int i = 0; i < CompetitionTable.GetLength(0); i++)
            {
                string code = CompetitionTable[i, 0];
                if (_draftCompetitions.Contains(code))
                {
                    ordered.Add(code);
                }
            }

            foreach (string code in _draftCompetitions)
            {
                if (!ordered.Contains(code))
                {
                    ordered.Add(code);
                }
            }

            return ordered;
        }

        private void Finish()
        {
            try
            {
                CaptureStep();

                _app.Config.ApiKey = _draftKey;
                _app.Config.Favorites = _draftFavorites;
                _app.Config.Competitions = OrderedCompetitions();
                if (_app.Config.Competitions.Count == 0)
                {
                    _app.Config.Competitions = new List<string>(AppConfig.DefaultCompetitions);
                }

                _app.Config.Provider = _draftKey.Length > 0 ? "footballdata" : "mock";
                _app.Config.WizardDone = true;
                _app.Config.Save(Paths.ConfigFile);
            }
            catch (Exception ex)
            {
                Log.Write("wizard finish failed: " + ex.Message);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }

        private sealed class TeamItem
        {
            internal readonly TeamResult Team;

            internal TeamItem(TeamResult team)
            {
                Team = team;
            }

            public override string ToString()
            {
                return Team.Name + "  (" + Team.CompetitionCode + ")";
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Request log window (debug visibility for the poll cadence)
    // ----------------------------------------------------------------------------------------
    internal sealed class RequestLogForm : Form
    {
        private readonly ListBox _list = new ListBox();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        internal RequestLogForm()
        {
            Text = Program.AppName + " - request log";
            ClientSize = new Size(780, 320);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimizeBox = false;

            _list.Dock = DockStyle.Fill;
            _list.Font = new Font("Consolas", 8.5f);
            _list.HorizontalScrollbar = true;
            Controls.Add(_list);

            _timer.Interval = 1000;
            _timer.Tick += delegate { Reload(); };
            _timer.Start();
            Reload();
        }

        private void Reload()
        {
            string[] lines = RequestLog.Snapshot();
            if (_list.Items.Count == lines.Length)
            {
                return;
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int i = 0; i < lines.Length; i++)
                {
                    _list.Items.Add(lines[i]);
                }

                if (_list.Items.Count > 0)
                {
                    _list.TopIndex = _list.Items.Count - 1;
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _timer.Stop();
                _timer.Dispose();
            }
            catch
            {
            }

            base.OnFormClosed(e);
        }
    }

    // ----------------------------------------------------------------------------------------
    //  --coverage: how many of today's matches actually resolve scorer names?
    //  Runs the real matcher, one match at a time, and reports a per match verdict so the
    //  "will it show in every match?" question is answered with data instead of hope.
    // ----------------------------------------------------------------------------------------
    // ----------------------------------------------------------------------------------------
    //  --cups: what the cup source sees right now, and whether it pairs it with my teams
    // ----------------------------------------------------------------------------------------
    internal static class CupProbe
    {
        internal static int Run()
        {
            var report = new StringBuilder();
            report.AppendLine("cup probe - competitions the free feed does not carry");
            report.AppendLine("");

            AppController app = null;
            try
            {
                app = new AppController();
                app.ReadOnlyConfig = true;

                var names = new List<string>();
                for (int i = 0; i < app.Config.Favorites.Count; i++)
                {
                    names.Add(app.Config.Favorites[i].Name);
                }

                report.AppendLine("favourites: " + string.Join(", ", names.ToArray()));
                report.AppendLine("");

                int mine = CupSource.Probe(app.Config.Favorites, DateTime.UtcNow, report);
                report.AppendLine(mine > 0
                    ? "OK: " + mine.ToString(CultureInfo.InvariantCulture) + " cup tie(s) matched a favourite"
                    : "no cup tie in this window involves a favourite");
            }
            catch (Exception ex)
            {
                report.AppendLine("FAILED: " + ex.Message);
            }
            finally
            {
                if (app != null)
                {
                    app.Dispose();
                }
            }

            string output = Path.Combine(Paths.Dir, "cupcheck.log");
            try
            {
                File.WriteAllText(output, report.ToString(), Encoding.UTF8);
            }
            catch
            {
            }

            return 0;
        }
    }

    internal static class CoverageTool
    {
        internal static int Run()
        {
            AppController app = null;
            var lines = new List<string>();
            int started = 0;
            int matched = 0;
            int consistent = 0;
            int goalsSeen = 0;

            try
            {
                app = new AppController();
                app.ReadOnlyConfig = true;

                IScoreProvider provider = app.CreateProvider();
                List<Fixture> raw = provider.GetLiveFixturesAsync(CancellationToken.None).Result;
                GoalMerge.Merge(null, raw, DateTime.UtcNow);

                EspnScorers espn = new EspnScorers();
                for (int i = 0; i < raw.Count; i++)
                {
                    Fixture f = raw[i];
                    if (f == null)
                    {
                        continue;
                    }

                    bool kickedOff = f.State == MatchState.Live || f.State == MatchState.Paused ||
                                     f.State == MatchState.Finished;
                    if (!kickedOff)
                    {
                        continue;
                    }

                    started++;
                    int total = f.TotalGoals;
                    goalsSeen = goalsSeen + total;

                    string verdict;
                    List<GoalEvent> got = null;
                    try
                    {
                        got = espn.Get(f, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        verdict = "ERROR " + ex.Message;
                        got = null;
                    }

                    if (got == null)
                    {
                        verdict = "no espn match";
                    }
                    else if (got.Count == total)
                    {
                        matched++;
                        consistent++;
                        verdict = "ok";
                    }
                    else
                    {
                        matched++;
                        verdict = "count " + got.Count + " vs " + total;
                    }

                    lines.Add(Pad(f.CompetitionCode, 4) + " " +
                              Pad(f.HomeName + " v " + f.AwayName, 42) +
                              Pad(ScoreText(f), 8) +
                              Pad(f.State.ToString(), 10) +
                              Pad(verdict, 16) +
                              Names(got));
                }
            }
            catch (Exception ex)
            {
                lines.Add("coverage failed: " + ex.Message);
            }

            string report = "MatchPill scorer coverage  (shipped matcher)\n" +
                            "utc now: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z\n\n" +
                            string.Join("\n", lines.ToArray()) + "\n\n" +
                            "started matches : " + started + "\n" +
                            "resolved        : " + matched + "\n" +
                            "count == score  : " + consistent + "\n" +
                            "goals on board  : " + goalsSeen + "\n" +
                            "coverage        : " + (started == 0 ? "n/a" :
                                ((matched * 100) / started) + "%") + "\n";

            try
            {
                File.WriteAllText(Path.Combine(Paths.Dir, "coverage.log"), report);
            }
            catch
            {
            }

            Log.Write("coverage: " + matched + "/" + started + " resolved, " + consistent + " consistent");
            Console.Out.Write(report);
            return 0;
        }

        private static string ScoreText(Fixture f)
        {
            string home = f.HomeScore.HasValue ? f.HomeScore.Value.ToString(CultureInfo.InvariantCulture) : "-";
            string away = f.AwayScore.HasValue ? f.AwayScore.Value.ToString(CultureInfo.InvariantCulture) : "-";
            return home + "-" + away;
        }

        private static string Names(List<GoalEvent> got)
        {
            if (got == null || got.Count == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < got.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(got[i].Player.Length == 0 ? "?" : got[i].Player);
                sb.Append(" ");
                sb.Append(got[i].MinuteLabel.Length == 0
                    ? (got[i].Minute < 0 ? "-" : got[i].Minute.ToString(CultureInfo.InvariantCulture))
                    : got[i].MinuteLabel);
            }

            return sb.ToString();
        }

        private static string Pad(string text, int width)
        {
            string s = text == null ? "" : text;
            if (s.Length > width)
            {
                return s.Substring(0, width);
            }

            return s.PadRight(width);
        }
    }

    // ----------------------------------------------------------------------------------------
    //  --alert-test: what the kick-off alerter would do right now, without sending anything.
    //  Fetches each favourite's next fixture, then reports distance to kick-off and whether the
    //  once-only book has already been stamped.
    // ----------------------------------------------------------------------------------------
    internal static class AlertProbe
    {
        internal static int Run(bool fire)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MatchPill alert probe (dry run - nothing is sent)");
            sb.AppendLine("utc now: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
            sb.AppendLine();

            try
            {
                var app = new AppController();
                app.ReadOnlyConfig = true;
                int window = app.Config.NotifyMinutes > 0 ? app.Config.NotifyMinutes : 30;
                sb.AppendLine("notify enabled : " + app.Config.NotifyBeforeMatch);
                sb.AppendLine("window         : " + window + " min");
                sb.AppendLine("favourites     : " + app.Config.Favorites.Count);
                sb.AppendLine();

                IScoreProvider provider = app.CreateProvider();
                DateTime nowUtc = DateTime.UtcNow;

                for (int i = 0; i < app.Config.Favorites.Count; i++)
                {
                    FavoriteTeam team = app.Config.Favorites[i];
                    string line = Pad(team.Name, 24);
                    Fixture next = null;
                    try
                    {
                        List<Fixture> list = provider.GetTeamFixturesAsync(team.Id, nowUtc.AddHours(-3),
                            nowUtc.AddDays(28), CancellationToken.None).Result;
                        for (int j = 0; list != null && j < list.Count; j++)
                        {
                            Fixture f = list[j];
                            if (f == null || f.State == MatchState.Finished ||
                                f.State == MatchState.Cancelled || f.State == MatchState.Postponed)
                            {
                                continue;
                            }

                            if (AppController.EnsureUtc(f.KickoffUtc) <= nowUtc.AddMinutes(-5))
                            {
                                continue;
                            }

                            if (next == null || AppController.EnsureUtc(f.KickoffUtc) <
                                AppController.EnsureUtc(next.KickoffUtc))
                            {
                                next = f;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine(line + "lookup failed: " + AppController.Innermost(ex).Message);
                        continue;
                    }

                    if (i + 1 < app.Config.Favorites.Count)
                    {
                        Thread.Sleep(1200);             // stay inside the ten a minute budget
                    }

                    if (next == null)
                    {
                        sb.AppendLine(line + "no upcoming fixture found");
                        continue;
                    }

                    DateTime kickoff = AppController.EnsureUtc(next.KickoffUtc);
                    DateTime local = TimeZoneInfo.ConvertTimeFromUtc(kickoff, TimeZoneInfo.Local);
                    TimeSpan left = kickoff - nowUtc;
                    bool inside = left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(window);
                    bool stamped = app.Alerts.HasNotified(next.Id);

                    sb.AppendLine(line + Pad(next.HomeName + " v " + next.AwayName, 34) +
                                  local.ToString("ddd HH:mm", CultureInfo.CurrentCulture) + "  " +
                                  Pad("in " + Human(left), 12) +
                                  (inside ? (stamped ? "would skip (already sent)" : "WOULD NOTIFY NOW")
                                          : "outside window"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("probe failed: " + ex.Message);
            }

            string report = sb.ToString();
            try
            {
                File.WriteAllText(Path.Combine(Paths.Dir, "alertprobe.log"), report);
            }
            catch
            {
            }

            Console.Out.Write(report);

            if (fire)
            {
                ShowSample();
            }

            return 0;
        }

        // A real tray balloon with a sample fixture, so the notification itself can be checked
        // without waiting for a real kick-off. Nothing is written to alerts.json.
        private static void ShowSample()
        {
            var form = new Form();
            form.ShowInTaskbar = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(-32000, -32000, 1, 1);
            form.ShowIcon = false;
            form.Text = "MatchPill alert preview";

            var tray = new NotifyIcon();
            tray.Icon = SystemIcons.Information;
            tray.Text = "MatchPill";
            tray.Visible = true;

            var show = new System.Windows.Forms.Timer();
            show.Interval = 900;
            show.Tick += delegate
            {
                show.Stop();
                tray.BalloonTipIcon = ToolTipIcon.Info;
                tray.BalloonTipTitle = "Kick-off in 28 min";
                tray.BalloonTipText = "Arsenal v Chelsea  \u00b7  19:30\nPremier League (sample)";
                tray.ShowBalloonTip(12000);
                Log.Write("alert preview shown");
            };
            show.Start();

            var close = new System.Windows.Forms.Timer();
            close.Interval = 14000;
            close.Tick += delegate
            {
                close.Stop();
                form.Close();
            };
            close.Start();

            Application.Run(form);
            tray.Visible = false;
            tray.Dispose();
        }

        private static string Human(TimeSpan span)
        {
            if (span.TotalMinutes < 1)
            {
                return "<1 min";
            }

            if (span.TotalHours < 1)
            {
                return ((int)span.TotalMinutes) + " min";
            }

            if (span.TotalDays < 1)
            {
                return ((int)span.TotalHours) + " h " + span.Minutes + " m";
            }

            return ((int)span.TotalDays) + " d " + span.Hours + " h";
        }

        private static string Pad(string text, int width)
        {
            string s = text == null ? "" : text;
            if (s.Length > width)
            {
                return s.Substring(0, width);
            }

            return s.PadRight(width);
        }
    }

    // ----------------------------------------------------------------------------------------
    //  --screen: capture the desktop exactly where the card lives.
    //  Answers "is it actually on screen, or is something covering it?" with pixels.
    // ----------------------------------------------------------------------------------------
    internal static class ScreenShot
    {
        internal static int Run(string target)
        {
            string path = string.IsNullOrEmpty(target) ? Path.Combine(Paths.Dir, "screen.png") : target;
            try
            {
                AppConfig cfg = AppConfig.Load(Paths.ConfigFile);
                Rectangle r = cfg.Collapsed ? cfg.CollapsedBounds : cfg.ExpandedBounds;
                if (r.Width <= 0 || r.Height <= 0)
                {
                    r = new Rectangle(0, 0, 320, 258);
                }

                r.Inflate(10, 10);

                using (var bmp = new Bitmap(r.Width, r.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                Log.Write("screen shot: " + path + " region=" + r);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("screen shot failed: " + ex.Message);
                return 1;
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Synthetic fixtures: several matches in play at the same instant, so the "more than one
    //  live match" layout can be rendered and inspected without waiting for a real kick-off.
    // ----------------------------------------------------------------------------------------
    internal static class DemoFixtures
    {
        internal static List<Fixture> Build(DateTime nowUtc)
        {
            return Build(nowUtc, 4);
        }

        internal static List<Fixture> Build(DateTime nowUtc, int liveCount)
        {
            var list = new List<Fixture>();

            Fixture a = Make("9001", "PL", "Premier League", "57", "Arsenal", "ARS", "61", "Chelsea", "CHE",
                MatchState.Live, 67, 2, 1, nowUtc);
            a.Goals.Add(Goal(23, "Bukayo Saka", 1, 0, true));
            a.Goals.Add(Goal(41, "Cole Palmer", 1, 1, false));
            a.Goals.Add(Goal(58, "Viktor Gyokeres", 2, 1, true));
            list.Add(a);

            Fixture b = Make("9002", "PD", "Primera Division", "86", "Real Madrid", "RMA", "559", "Sevilla FC",
                "SEV", MatchState.Live, 34, 1, 0, nowUtc);
            b.Goals.Add(Goal(12, "Kylian Mbappe", 1, 0, true));
            list.Add(b);

            Fixture c = Make("9003", "PD", "Primera Division", "81", "Barça", "FCB", "78", "Atletico", "ATM",
                MatchState.Paused, 45, 1, 1, nowUtc);
            c.Goals.Add(Goal(9, "Lamine Yamal", 1, 0, true));
            c.Goals.Add(Goal(38, "Julian Alvarez", 1, 1, false));
            list.Add(c);

            Fixture d = Make("9004", "PL", "Premier League", "66", "Man United", "MUN", "67", "Newcastle", "NEW",
                MatchState.Live, 78, 0, 2, nowUtc);
            d.Goals.Add(Goal(31, "Alexander Isak", 0, 1, false));
            d.Goals.Add(Goal(66, "Anthony Gordon", 0, 2, false));
            list.Add(d);

            Fixture e = Make("9005", "PL", "Premier League", "64", "Liverpool", "LIV", "57", "Arsenal", "ARS",
                MatchState.Scheduled, 0, 0, 0, nowUtc);
            e.KickoffUtc = nowUtc.AddMinutes(150);
            e.HomeScore = null;
            e.AwayScore = null;
            list.Add(e);

            Fixture g = Make("9006", "PL", "Premier League", "65", "Man City", "MCI", "397", "Brighton Hove",
                "BHA", MatchState.Finished, 90, 3, 0, nowUtc);
            g.KickoffUtc = nowUtc.AddHours(-3);
            list.Add(g);

            if (liveCount < 4)
            {
                var keep = new List<Fixture>();
                int liveKept = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    bool playing = list[i].State == MatchState.Live || list[i].State == MatchState.Paused;
                    if (playing)
                    {
                        if (liveKept >= liveCount)
                        {
                            continue;
                        }

                        liveKept++;
                    }

                    keep.Add(list[i]);
                }

                return keep;
            }

            return list;
        }

        private static Fixture Make(string id, string code, string name, string homeId, string home,
            string homeTla, string awayId, string away, string awayTla, MatchState state, int minute,
            int homeScore, int awayScore, DateTime nowUtc)
        {
            var f = new Fixture();
            f.Id = id;
            f.CompetitionCode = code;
            f.CompetitionName = name;
            f.HomeId = homeId;
            f.HomeName = home;
            f.HomeTla = homeTla;
            f.AwayId = awayId;
            f.AwayName = away;
            f.AwayTla = awayTla;
            f.HomeCrest = "https://crests.football-data.org/" + homeId + ".png";
            f.AwayCrest = "https://crests.football-data.org/" + awayId + ".png";
            f.Matchday = 10;
            f.State = state;
            f.RawStatus = state == MatchState.Live ? "IN_PLAY" :
                (state == MatchState.Paused ? "PAUSED" : (state == MatchState.Finished ? "FINISHED" : "TIMED"));
            f.HomeScore = homeScore;
            f.AwayScore = awayScore;
            f.MinuteValue = minute;
            f.Minute = minute > 0 ? minute.ToString(CultureInfo.InvariantCulture) + "'" : "";

            if (state == MatchState.Live && minute > 45)
            {
                f.KickoffUtc = nowUtc.AddMinutes(-minute);
                f.SecondHalfStartUtc = nowUtc.AddMinutes(-(minute - 45));
            }
            else
            {
                f.KickoffUtc = nowUtc.AddMinutes(-minute);
            }

            return f;
        }

        private static GoalEvent Goal(int minute, string player, int homeAfter, int awayAfter, bool homeScored)
        {
            var e = new GoalEvent();
            e.Minute = minute;
            e.MinuteLabel = minute.ToString(CultureInfo.InvariantCulture) + "'";
            e.Player = player;
            e.HomeAfter = homeAfter;
            e.AwayAfter = awayAfter;
            e.HomeScored = homeScored;
            return e;
        }
    }

    // ----------------------------------------------------------------------------------------
    //  --shot: render the card to a PNG without running the app (layout debugging)
    // ----------------------------------------------------------------------------------------
    internal static class ShotTool
    {
        internal static int Run(string target, bool collapsed, bool detail, bool live, bool all, bool flash,
            bool empty, bool demo, bool demoOne, bool nextOnly, bool cupRows)
        {
            string path = string.IsNullOrEmpty(target) ? Path.Combine(Paths.Dir, "card.png") : target;
            AppController app = null;
            CardForm card = null;

            try
            {
                app = new AppController();
                app.ReadOnlyConfig = true;
                app.Config.Collapsed = collapsed;
                app.Config.NextView = nextOnly;
                if (all)
                {
                    app.Config.Favorites.Clear();       // debug render: every match, no filter
                }

                List<Fixture> raw;
                if (demo)
                {
                    raw = DemoFixtures.Build(DateTime.UtcNow, demoOne ? 1 : 4);
                }
                else
                {
                    IScoreProvider provider = live ? app.CreateProvider() : new MockProvider();
                    raw = provider.GetLiveFixturesAsync(CancellationToken.None).Result;
                    GoalMerge.Merge(null, raw, DateTime.UtcNow);
                }

                if (cupRows)
                {
                    // the real cup pipeline, so the render proves the merge and not just the parser
                    raw.AddRange(CupSource.Recent(app.Config.Favorites, true, DateTime.UtcNow, 2));
                }

                if (flash)
                {
                    for (int i = 0; i < raw.Count; i++)
                    {
                        if (raw[i].State == MatchState.Live || raw[i].State == MatchState.Paused)
                        {
                            raw[i].GoalFlashUtc = DateTime.UtcNow;   // debug: show the goal wash
                            break;
                        }
                    }
                }

                List<string> next;
                List<Fixture> feed = empty ? new List<Fixture>() : raw;
                List<Fixture> view = app.BuildView(feed, DateTime.UtcNow, out next);

                card = new CardForm(app);
                card.ApplyInitialBounds();
                card.SetData(view, DateTime.UtcNow, false, next);
                if (detail && !collapsed && view.Count > 0)
                {
                    Fixture pick = view[0];
                    for (int i = 0; i < view.Count; i++)
                    {
                        if (view[i].Goals.Count > 0)
                        {
                            pick = view[i];
                            break;
                        }
                    }

                    for (int i = 0; i < view.Count; i++)
                    {
                        bool playing = view[i].State == MatchState.Live || view[i].State == MatchState.Paused;
                        if (playing && view[i].Goals.Count > 0)
                        {
                            pick = view[i];         // prefer a match in play for the render
                            break;
                        }
                    }

                    try
                    {
                        List<GoalEvent> got = demo ? null : new EspnScorers().Get(pick, CancellationToken.None);
                        if (got != null)
                        {
                            pick.Scorers = got;
                            Log.Write("shot scorers: " + got.Count + " for " + pick.HomeName + " v " +
                                      pick.AwayName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("shot scorers failed: " + ex.Message);
                    }

                    card.OpenDetail(pick);
                }

                card.ShowCard();
                Application.DoEvents();

                using (var bitmap = new Bitmap(card.Width, card.Height))
                {
                    card.DrawToBitmap(bitmap, new Rectangle(0, 0, card.Width, card.Height));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                Log.Write("shot written: " + path + " (" + card.Width.ToString(CultureInfo.InvariantCulture) +
                          "x" + card.Height.ToString(CultureInfo.InvariantCulture) + ") dpi=" +
                          card.DeviceDpi.ToString(CultureInfo.InvariantCulture) + " scale=" +
                          card.ScaleFactor.ToString("0.###", CultureInfo.InvariantCulture) + " client=" +
                          card.ClientSize.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                          card.ClientSize.Height.ToString(CultureInfo.InvariantCulture));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("shot failed: " + ex);
                return 1;
            }
            finally
            {
                if (card != null)
                {
                    try
                    {
                        card.HideForFullscreen();
                        card.Dispose();
                    }
                    catch
                    {
                    }
                }

                if (app != null)
                {
                    try
                    {
                        app.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------
    //  Self test - MatchPill.exe --selftest  (no window, writes selftest.log)
    // ----------------------------------------------------------------------------------------
    internal static class SelfTest
    {
        internal static int Run()
        {
            var report = new StringBuilder();
            int failures = 0;

            report.AppendLine("MatchPill " + Program.AppVersion + " self test");
            report.AppendLine("utc now      : " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            report.AppendLine("local zone   : " + TimeZoneInfo.Local.Id + " (DST now: " +
                              TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now) + ")");
            report.AppendLine("data folder  : " + Paths.Dir);
            report.AppendLine("");

            // 1. JSON round trip
            try
            {
                string path = Path.Combine(Paths.Dir, "selftest-config.json");
                var config = new AppConfig();
                config.Provider = "footballdata";
                config.ApiKey = "test-key";
                config.Collapsed = false;
                config.OpacityPercent = 80;
                config.Competitions = new List<string>(new string[] { "PL", "PD" });
                var team = new FavoriteTeam();
                team.Id = "57";
                team.Name = "Arsenal \"FC\"";
                team.CompetitionCode = "PL";
                config.Favorites.Add(team);
                config.Save(path);

                AppConfig loaded = AppConfig.Load(path);
                bool ok = loaded.ApiKey == "test-key" && loaded.Favorites.Count == 1 &&
                          loaded.Favorites[0].Name == "Arsenal \"FC\"" && loaded.Competitions.Count == 2 &&
                          loaded.Collapsed == false && loaded.OpacityPercent == 80;
                Check(report, "config.json round trip (incl. quoted text)", ok,
                    loaded.Favorites.Count > 0 ? loaded.Favorites[0].Name : "no favorites", ref failures);
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Check(report, "config.json round trip", false, ex.Message, ref failures);
            }

            // 2. Timezone / DST: a 23:30 UTC kickoff across a DST boundary
            try
            {
                TimeZoneInfo zone = FindDstZone();
                DateTime winter = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);
                DateTime summer = new DateTime(2026, 7, 15, 23, 30, 0, DateTimeKind.Utc);
                DateTime winterLocal = TimeZoneInfo.ConvertTimeFromUtc(winter, zone);
                DateTime summerLocal = TimeZoneInfo.ConvertTimeFromUtc(summer, zone);
                TimeSpan winterOffset = zone.GetUtcOffset(winter);
                TimeSpan summerOffset = zone.GetUtcOffset(summer);
                // winter 23:30Z renders 00:30 next day, summer 23:30Z renders 01:30 next day
                bool ok = winterOffset != summerOffset &&
                          Math.Abs((summerOffset - winterOffset).TotalHours - 1.0) < 0.001 &&
                          winterLocal.Hour == 0 && summerLocal.Hour == 1;
                Check(report, "23:30Z kickoff across DST boundary (" + zone.Id + ")", ok,
                    winterLocal.ToString("HH:mm", CultureInfo.InvariantCulture) + " vs " +
                    summerLocal.ToString("HH:mm", CultureInfo.InvariantCulture), ref failures);

                // the same instant must render identically no matter which local zone asks
                DateTime springForward = new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc);
                DateTime asLocal = TimeZoneInfo.ConvertTimeFromUtc(springForward, zone);
                bool stable = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(springForward, DateTimeKind.Utc), zone) == asLocal;
                Check(report, "UTC to local conversion is stable/idempotent", stable,
                    asLocal.ToString("o", CultureInfo.InvariantCulture), ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "DST conversion", false, ex.Message, ref failures);
            }

            // 3. Status mapping
            var mapCases = new string[,]
            {
                { "SCHEDULED", "Scheduled" }, { "TIMED", "Scheduled" }, { "IN_PLAY", "Live" },
                { "PAUSED", "Paused" }, { "FINISHED", "Finished" }, { "POSTPONED", "Postponed" },
                { "CANCELLED", "Cancelled" }, { "SUSPENDED", "Suspended" }, { "WHATEVER", "Unknown" }
            };
            bool mapOk = true;
            string mapDetail = "";
            for (int i = 0; i < mapCases.GetLength(0); i++)
            {
                MatchState mapped = StatusMapper.Map(mapCases[i, 0]);
                if (mapped.ToString() != mapCases[i, 1])
                {
                    mapOk = false;
                    mapDetail = mapCases[i, 0] + " -> " + mapped;
                }
            }

            Check(report, "provider status mapping (9 values)", mapOk, mapDetail, ref failures);

            // 4. "is it on now" guard
            DateTime nowUtc = DateTime.UtcNow;
            var staleLive = new Fixture();
            staleLive.Id = "x1";
            staleLive.State = MatchState.Live;
            staleLive.KickoffUtc = nowUtc.AddHours(-5);
            var freshLive = new Fixture();
            freshLive.Id = "x2";
            freshLive.State = MatchState.Live;
            freshLive.KickoffUtc = nowUtc.AddHours(-1);
            var unknownState = new Fixture();
            unknownState.Id = "x3";
            unknownState.State = MatchState.Unknown;
            unknownState.KickoffUtc = nowUtc.AddHours(-1);
            bool guardOk = !staleLive.IsLiveNow(nowUtc) && freshLive.IsLiveNow(nowUtc) &&
                           !unknownState.IsLiveNow(nowUtc);
            Check(report, "4 hour live guard + Unknown is never live", guardOk,
                "stale=" + staleLive.IsLiveNow(nowUtc) + " fresh=" + freshLive.IsLiveNow(nowUtc) +
                " unknown=" + unknownState.IsLiveNow(nowUtc), ref failures);

            // 5. Notification signature: goal = score change, not status change
            var before = new Fixture();
            before.Id = "77";
            before.State = MatchState.Live;
            before.HomeScore = 1;
            before.AwayScore = 0;
            var afterGoal = before.Clone();
            afterGoal.HomeScore = 2;
            var sameAgain = before.Clone();
            bool sigOk = before.Signature() == sameAgain.Signature() &&
                         before.Signature() != afterGoal.Signature();
            Check(report, "dedupe signature: same tuple equal, goal differs", sigOk,
                before.Signature() + " / " + afterGoal.Signature(), ref failures);

            // 6. Backoff ladder
            int b = PollEngine.NextBackoff(0, 0);
            int b2 = PollEngine.NextBackoff(b, 0);
            int b3 = PollEngine.NextBackoff(b2, 0);
            int cap = 0;
            for (int i = 0; i < 20; i++)
            {
                cap = PollEngine.NextBackoff(cap, 0);
            }

            bool backoffOk = b == 2000 && b2 == 4000 && b3 == 8000 && cap == 300000;
            Check(report, "429 backoff 2s -> 4s -> 8s, cap 5 min", backoffOk,
                b + "/" + b2 + "/" + b3 + " cap " + cap, ref failures);

            // 7. Poll interval policy
            bool intervalOk = AppController.IntervalFor(true, false, false, false, 0, 2000) == 2000 &&
                              AppController.IntervalFor(false, true, false, false, 0, 2000) == 15000 &&
                              AppController.IntervalFor(false, false, true, false, 0, 2000) == 15000 &&
                              AppController.IntervalFor(false, false, false, false, 0, 2000) == 60000 &&
                              AppController.IntervalFor(false, false, false, true, 123456, 2000) == 123456;
            Check(report, "poll interval policy (live 2s / soon 15s / idle 60s / quiet)", intervalOk,
                AppController.IntervalFor(true, false, false, false, 0, 2000) + "/" +
                AppController.IntervalFor(false, true, false, false, 0, 2000) + "/" +
                AppController.IntervalFor(false, false, true, false, 0, 2000) + "/" +
                AppController.IntervalFor(false, false, false, false, 0, 2000), ref failures);

            // 8. Cache round trip
            try
            {
                string path = Path.Combine(Paths.Dir, "selftest-cache.json");
                var cache = new CachedPayload();
                cache.SavedUtc = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
                var f = new Fixture();
                f.Id = "abc";
                f.CompetitionCode = "PL";
                f.CompetitionName = "Premier League";
                f.HomeId = "57";
                f.HomeName = "Arsenal";
                f.AwayId = "61";
                f.AwayName = "Chelsea";
                f.HomeScore = 2;
                f.AwayScore = 1;
                f.KickoffUtc = new DateTime(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc);
                f.State = MatchState.Live;
                f.Minute = "67'";
                cache.Fixtures.Add(f);
                cache.Save(path);

                CachedPayload loaded = CachedPayload.Load(path);
                bool ok = loaded.Fixtures.Count == 1 && loaded.Fixtures[0].HomeName == "Arsenal" &&
                          loaded.Fixtures[0].HomeScore == 2 && loaded.Fixtures[0].Minute == "67'" &&
                          loaded.Fixtures[0].KickoffUtc == f.KickoffUtc &&
                          loaded.Fixtures[0].State == MatchState.Live &&
                          loaded.SavedUtc == cache.SavedUtc;
                Check(report, "cache.json round trip (cold start paint)", ok,
                    loaded.Fixtures.Count.ToString(CultureInfo.InvariantCulture) + " fixture(s)", ref failures);
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Check(report, "cache.json round trip", false, ex.Message, ref failures);
            }

            // 9. Provider payload mapping
            try
            {
                string payload = "{\"matches\":[{\"id\":441234,\"utcDate\":\"2026-10-25T18:45:00Z\"," +
                                 "\"status\":\"IN_PLAY\",\"minute\":87,\"competition\":{\"id\":2021," +
                                 "\"code\":\"PL\",\"name\":\"Premier League\"},\"homeTeam\":{\"id\":57," +
                                 "\"name\":\"Arsenal FC\",\"shortName\":\"Arsenal\",\"tla\":\"ARS\"}," +
                                 "\"awayTeam\":{\"id\":61,\"name\":\"Chelsea FC\",\"shortName\":\"Chelsea\"," +
                                 "\"tla\":\"CHE\"},\"score\":{\"fullTime\":{\"home\":2,\"away\":1}," +
                                 "\"halfTime\":{\"home\":1,\"away\":0}}}]}";

                JVal root = JVal.Parse(payload);
                List<JVal> matches = root.GetArray("matches");
                Fixture f = FootballDataOrgProvider.MapFixture(matches[0]);
                bool ok = f != null && f.Id == "441234" && f.State == MatchState.Live && f.Minute == "87'" &&
                          f.HomeName == "Arsenal" && f.AwayName == "Chelsea" && f.HomeScore == 2 &&
                          f.AwayScore == 1 && f.CompetitionCode == "PL" &&
                          f.KickoffUtc == new DateTime(2026, 10, 25, 18, 45, 0, DateTimeKind.Utc);
                Check(report, "football-data.org payload -> Fixture", ok,
                    f == null ? "null" : f.HomeName + " " + f.ScoreText + " " + f.AwayName + " " + f.Minute,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "football-data.org payload -> Fixture", false, ex.Message, ref failures);
            }

            // 10. Sorting: live first by competition priority, then today's kickoff order
            try
            {
                var controller = new AppController();
                controller.Config.Favorites = new List<FavoriteTeam>();
                controller.Config.Competitions = new List<string>(new string[] { "PL", "PD" });

                var livePd = new Fixture();
                livePd.Id = "a";
                livePd.CompetitionCode = "PD";
                livePd.State = MatchState.Live;
                livePd.KickoffUtc = nowUtc.AddMinutes(-30);
                livePd.HomeName = "Real";
                livePd.AwayName = "Barca";

                var livePl = new Fixture();
                livePl.Id = "b";
                livePl.CompetitionCode = "PL";
                livePl.State = MatchState.Live;
                livePl.KickoffUtc = nowUtc.AddMinutes(-70);
                livePl.HomeName = "Arsenal";
                livePl.AwayName = "Chelsea";

                // deterministic "today" kickoffs, independent of the wall clock when the test runs
                DateTime lateLocal = DateTime.Today.AddHours(23).AddMinutes(50);
                DateTime earlyLocal = DateTime.Today.AddHours(23).AddMinutes(30);

                var later = new Fixture();
                later.Id = "c";
                later.CompetitionCode = "PL";
                later.State = MatchState.Scheduled;
                later.KickoffUtc = lateLocal.ToUniversalTime();
                later.HomeName = "Spurs";
                later.AwayName = "Everton";

                var sooner = new Fixture();
                sooner.Id = "d";
                sooner.CompetitionCode = "SA";
                sooner.State = MatchState.Scheduled;
                sooner.KickoffUtc = earlyLocal.ToUniversalTime();
                sooner.HomeName = "Inter";
                sooner.AwayName = "Milan";

                var all = new List<Fixture>();
                all.Add(later);
                all.Add(sooner);
                all.Add(livePd);
                all.Add(livePl);
                all.Add(livePl);                        // duplicate id, must collapse

                List<string> nextText;
                List<Fixture> view = controller.BuildView(all, nowUtc, out nextText);
                bool ok = view.Count == 4 && view[0].Id == "b" && view[1].Id == "a" && view[2].Id == "d" &&
                          view[3].Id == "c";
                Check(report, "sort: live by competition, then kickoff, dedupe by id", ok,
                    "count=" + view.Count + " order=" + view[0].Id + view[1].Id + view[2].Id + view[3].Id,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "sorting", false, ex.Message, ref failures);
            }

            // 11. Malformed JSON must not throw
            try
            {
                JVal broken = JVal.Parse("{\"a\":[1,2,{");
                bool ok = broken != null;
                Check(report, "malformed JSON does not throw", ok, "", ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "malformed JSON does not throw", false, ex.Message, ref failures);
            }

            // 12. Game clock, derived from kickoff (the free feed carries no minute)
            try
            {
                DateTime clockNow = new DateTime(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);
                var clocked = new Fixture();
                clocked.Id = "1";
                clocked.State = MatchState.Live;
                clocked.KickoffUtc = clockNow.AddMinutes(-37);

                bool firstHalf = MatchClock.Minute(clocked, clockNow) == 37 &&
                                 MatchClock.Running(clocked, clockNow) == "37:00" &&
                                 MatchClock.Badge(clocked, clockNow) == "37'";

                var atHalf = clocked.Clone();
                atHalf.State = MatchState.Paused;
                bool stopped = MatchClock.PlaySeconds(atHalf, clockNow) == 45 * 60 &&
                               MatchClock.Badge(atHalf, clockNow) == "HT";

                var secondHalf = clocked.Clone();
                secondHalf.KickoffUtc = clockNow.AddMinutes(-72);          // 45 + 15 break + 12
                bool afterBreak = MatchClock.Minute(secondHalf, clockNow) == 57;

                var lateStart = clocked.Clone();
                lateStart.KickoffUtc = clockNow.AddMinutes(-40);
                lateStart.LiveStartUtc = clockNow.AddMinutes(-30);         // it actually began 10 min late
                bool anchored = MatchClock.Minute(lateStart, clockNow) == 30;

                var anchoredHalf = clocked.Clone();
                anchoredHalf.KickoffUtc = clockNow.AddMinutes(-80);
                anchoredHalf.SecondHalfStartUtc = clockNow.AddMinutes(-20);
                bool secondAnchored = MatchClock.Minute(anchoredHalf, clockNow) == 65;

                bool clockOk = firstHalf && stopped && afterBreak && anchored && secondAnchored;
                Check(report, "game clock: 37' live, HT frozen at 45, break removed, late start + 2nd half anchors",
                    clockOk, MatchClock.Running(clocked, clockNow) + " / " +
                             MatchClock.Badge(atHalf, clockNow) + " / " +
                             MatchClock.Minute(secondHalf, clockNow) + "' / " +
                             MatchClock.Minute(lateStart, clockNow) + "' / " +
                             MatchClock.Minute(anchoredHalf, clockNow) + "'", ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "game clock", false, ex.Message, ref failures);
            }

            // 13. Goal timeline: new goals are appended with the minute we saw them, seeded ones
            //     are marked unknown, and a score correction rebuilds instead of corrupting.
            try
            {
                DateTime clockNow = new DateTime(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);
                var seedFixture = new Fixture();
                seedFixture.Id = "42";
                seedFixture.State = MatchState.Live;
                seedFixture.KickoffUtc = clockNow.AddMinutes(-20);
                seedFixture.HomeScore = 1;
                seedFixture.AwayScore = 0;

                GoalMerge.Merge(null, new List<Fixture>(new Fixture[] { seedFixture }), clockNow);
                bool seeded = seedFixture.Goals.Count == 1 && seedFixture.Goals[0].Minute == -1;

                var scored = seedFixture.Clone();
                scored.HomeScore = 1;
                scored.AwayScore = 2;
                GoalMerge.Merge(new List<Fixture>(new Fixture[] { seedFixture }),
                    new List<Fixture>(new Fixture[] { scored }), clockNow);

                bool appended = scored.Goals.Count == 3 &&
                                scored.Goals[1].Minute == 20 && scored.Goals[1].AwayAfter == 1 &&
                                scored.Goals[2].AwayAfter == 2 && !scored.Goals[2].HomeScored &&
                                scored.GoalFlashUtc == clockNow;

                var corrected = scored.Clone();
                corrected.HomeScore = 0;
                corrected.AwayScore = 2;
                GoalMerge.Merge(new List<Fixture>(new Fixture[] { scored }),
                    new List<Fixture>(new Fixture[] { corrected }), clockNow);
                bool rebuilt = corrected.Goals.Count == 2;

                bool goalsOk = seeded && appended && rebuilt;
                Check(report, "goal timeline: seed, append on score change, rebuild on correction", goalsOk,
                    "seeded=" + seedFixture.Goals.Count + " appended=" + scored.Goals.Count +
                    " rebuilt=" + corrected.Goals.Count, ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "goal timeline", false, ex.Message, ref failures);
            }

            // 15. Kick-off alert: one ping, 30 minutes out, never twice, never late
            try
            {
                DateTime alertNow = new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);
                var alerts = new MatchAlerts();

                var soon = new Fixture();
                soon.Id = "soon";
                soon.KickoffUtc = alertNow.AddMinutes(29);
                soon.State = MatchState.Scheduled;
                soon.HomeName = "Arsenal";
                soon.AwayName = "Chelsea";

                var later = new Fixture();
                later.Id = "later";
                later.KickoffUtc = alertNow.AddMinutes(31);
                later.State = MatchState.Scheduled;

                var started = new Fixture();
                started.Id = "started";
                started.KickoffUtc = alertNow.AddMinutes(-5);
                started.State = MatchState.Scheduled;

                var pool = new List<Fixture>(new Fixture[] { later, soon, started });
                TimeSpan left;
                Fixture first = AppController.ChooseAlert(pool, alerts, alertNow, 30, out left);
                int firstLeft = (int)Math.Round(left.TotalMinutes);
                bool firstOk = first != null && first.Id == "soon" && firstLeft == 29;

                // the once-only rule: the same fixture can never be chosen again
                alerts.Mark(first == null ? "none" : first.Id);
                Fixture second = AppController.ChooseAlert(pool, alerts, alertNow, 30, out left);
                bool onceOk = second == null;

                // a match already under way is stamped, not announced
                bool lateOk = alerts.HasNotified("started");

                Check(report, "kick-off alert: 29 min fires, 31 min waits, once only, never late",
                    firstOk && onceOk && lateOk,
                    "first=" + (first == null ? "none" : first.Id) + " left=" + firstLeft +
                    " second=" + (second == null ? "none" : second.Id) + " late-stamped=" + lateOk,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "kick-off alert", false, ex.Message, ref failures);
            }

            // 16. Scope: a favourite's league is polled even when the config still says "CL", and
            //     a match in play is on the card whether or not one of my teams is playing
            try
            {
                var scope = new AppConfig();
                scope.Competitions = new List<string>(new string[] { "CL" });
                var fav = new FavoriteTeam();
                fav.Id = "57";
                fav.Name = "Arsenal";
                fav.CompetitionCode = "PL";
                scope.Favorites.Add(fav);
                List<string> codes = scope.EffectiveCompetitions();
                bool compOk = codes.Contains("CL") && codes.Contains("PL") && codes.Contains("FL1") &&
                              codes.Count >= 6;

                var scopeApp = new AppController();
                scopeApp.Config.Favorites = new List<FavoriteTeam>(new FavoriteTeam[] { fav });
                scopeApp.Config.LiveScopeAll = true;

                var liveOther = new Fixture();
                liveOther.Id = "liveOther";
                liveOther.CompetitionCode = "PD";
                liveOther.State = MatchState.Live;
                liveOther.KickoffUtc = nowUtc.AddMinutes(-30);
                liveOther.HomeScore = 1;
                liveOther.AwayScore = 0;
                liveOther.HomeName = "Barca";
                liveOther.AwayName = "Atletico";

                var schedOther = new Fixture();
                schedOther.Id = "schedOther";
                schedOther.CompetitionCode = "PD";
                schedOther.State = MatchState.Scheduled;
                schedOther.KickoffUtc = DateTime.Today.AddHours(23).ToUniversalTime();
                schedOther.HomeName = "Sevilla";
                schedOther.AwayName = "Osasuna";

                var feedScope = new List<Fixture>(new Fixture[] { liveOther, schedOther });
                List<string> nextScope;
                List<Fixture> viewAll = scopeApp.BuildView(feedScope, nowUtc, out nextScope);
                bool liveIn = viewAll.Count == 1 && viewAll[0].Id == "liveOther";

                scopeApp.Config.LiveScopeAll = false;
                List<Fixture> viewMine = scopeApp.BuildView(feedScope, nowUtc, out nextScope);
                bool liveOut = viewMine.Count == 0;

                Check(report, "scope: favourite league polled, any live match shown",
                    compOk && liveIn && liveOut,
                    "codes=" + string.Join(",", codes.ToArray()) + " liveIn=" + liveIn +
                    " offExcludes=" + liveOut,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "scope", false, ex.Message, ref failures);
            }

            // 17. "Next matches": live first, then scheduled - including fixtures days away -
            //     and no results at all
            try
            {
                var fav17 = new FavoriteTeam();
                fav17.Id = "57";
                fav17.Name = "Arsenal";
                fav17.CompetitionCode = "PL";

                var fwd = new AppController();
                fwd.Config.Favorites = new List<FavoriteTeam>(new FavoriteTeam[] { fav17 });

                var playing = new Fixture();
                playing.Id = "now";
                playing.State = MatchState.Live;
                playing.KickoffUtc = nowUtc.AddMinutes(-40);
                playing.HomeId = "57";
                playing.HomeName = "Arsenal";
                playing.AwayName = "Chelsea";
                playing.HomeScore = 1;
                playing.AwayScore = 0;

                var done = new Fixture();
                done.Id = "done";
                done.State = MatchState.Finished;
                done.KickoffUtc = nowUtc.AddHours(-3);
                done.HomeId = "57";
                done.HomeName = "Arsenal";
                done.AwayName = "Everton";
                done.HomeScore = 2;
                done.AwayScore = 0;

                var far = new Fixture();
                far.Id = "far";
                far.State = MatchState.Scheduled;
                far.KickoffUtc = nowUtc.AddDays(6);
                far.HomeId = "57";
                far.HomeName = "Liverpool";
                far.AwayName = "Arsenal";

                var feed17 = new List<Fixture>(new Fixture[] { done, far, playing });
                List<string> next17;

                fwd.Config.NextView = false;
                List<Fixture> normal = fwd.BuildView(feed17, nowUtc, out next17);
                bool normalOk = normal.Count == 2 && normal[0].Id == "now" && normal[1].Id == "done";

                fwd.Config.NextView = true;
                List<Fixture> ahead = fwd.BuildView(feed17, nowUtc, out next17);
                bool hasFar = ahead.Exists(delegate(Fixture x) { return x.Id == "far"; });
                bool hasDone = ahead.Exists(delegate(Fixture x) { return x.Id == "done"; });
                bool aheadOk = ahead.Count >= 2 && ahead[0].Id == "now" && hasFar && !hasDone;

                Check(report, "next view: live kept, results dropped, fixtures days ahead included",
                    normalOk && aheadOk,
                    "normal=" + normal.Count + " ahead=" + ahead.Count +
                    " far=" + hasFar + " done=" + hasDone,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "next view", false, ex.Message, ref failures);
            }

            // 18: the two pure functions behind the redesign - club colour from a crest, and the
            // "kick-off in 2h 14m" text. Both must be deterministic and never throw.
            try
            {
                bool tintOk;
                using (var kit = new Bitmap(8, 8))
                {
                    using (Graphics art = Graphics.FromImage(kit))
                    {
                        art.Clear(Color.White);
                        using (var blue = new SolidBrush(Color.FromArgb(0, 51, 153)))
                        {
                            art.FillRectangle(blue, 0, 0, 6, 8);        // blue shirt, white trim
                        }
                    }

                    Color tint = Crests.TintOf(kit);
                    int luma = (tint.R * 299 + tint.G * 587 + tint.B * 114) / 1000;
                    tintOk = tint.B > tint.R + 30 && luma >= 70 && luma <= 210;
                }

                Color once = Crests.FallbackTint("57");
                Color twice = Crests.FallbackTint("57");
                bool fallbackOk = once.ToArgb() == twice.ToArgb();

                string hours = CardForm.CountdownText(DateTime.UtcNow.AddMinutes(134), DateTime.UtcNow);
                string days = CardForm.CountdownText(DateTime.UtcNow.AddDays(3).AddHours(1), DateTime.UtcNow);
                string started = CardForm.CountdownText(DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow);
                bool textOk = hours.StartsWith("in 2h", StringComparison.Ordinal) &&
                              days.StartsWith("in 3d", StringComparison.Ordinal) &&
                              started == "kicking off";

                Check(report, "crest tint + kick-off countdown", tintOk && fallbackOk && textOk,
                    "tint=" + tintOk + " fallback=" + fallbackOk + " two=" + hours + " days=" + days,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "crest tint + kick-off countdown", false, ex.Message, ref failures);
            }

            // 19: cup rounds from ESPN - a competition the feed does not carry at all. The parse
            // must survive ESPN's quirks: scores as strings, a shoot-out beside the score, "0" for
            // a match that has not kicked off, and team ids that are rewritten to my own.
            try
            {
                var favs = new List<FavoriteTeam>();
                var city = new FavoriteTeam();
                city.Id = "65";
                city.Name = "Manchester City FC";
                favs.Add(city);

                const string finished =
                    "{\"id\":\"401\",\"date\":\"2026-10-28T19:30Z\",\"competitions\":[{\"status\":" +
                    "{\"displayClock\":\"95'\",\"type\":{\"name\":\"STATUS_FINAL_PEN\",\"state\":\"post\",\"detail\":\"FT-Pens\"}}," +
                    "\"venue\":{\"fullName\":\"Etihad Stadium\"},\"competitors\":[" +
                    "{\"homeAway\":\"home\",\"score\":\"2\",\"shootoutScore\":4,\"team\":{\"id\":\"382\"," +
                    "\"abbreviation\":\"MNC\",\"displayName\":\"Manchester City\",\"shortDisplayName\":\"Man City\"}}," +
                    "{\"homeAway\":\"away\",\"score\":\"2\",\"shootoutScore\":3,\"team\":{\"id\":\"331\"," +
                    "\"abbreviation\":\"BHA\",\"displayName\":\"Brighton & Hove Albion\",\"shortDisplayName\":\"Brighton\"}}]}]}";

                const string live =
                    "{\"id\":\"402\",\"date\":\"2026-10-28T19:45Z\",\"competitions\":[{\"status\":" +
                    "{\"displayClock\":\"67'\",\"type\":{\"name\":\"STATUS_IN_PROGRESS\",\"state\":\"in\",\"detail\":\"67'\"}}," +
                    "\"competitors\":[" +
                    "{\"homeAway\":\"home\",\"score\":\"1\",\"team\":{\"id\":\"364\",\"abbreviation\":\"LIV\"," +
                    "\"displayName\":\"Liverpool\",\"shortDisplayName\":\"Liverpool\"}}," +
                    "{\"homeAway\":\"away\",\"score\":\"0\",\"team\":{\"id\":\"363\",\"abbreviation\":\"CHE\"," +
                    "\"displayName\":\"Chelsea\",\"shortDisplayName\":\"Chelsea\"}}]}]}";

                const string scheduled =
                    "{\"id\":\"403\",\"date\":\"2026-10-29T19:45Z\",\"competitions\":[{\"status\":" +
                    "{\"displayClock\":\"0'\",\"type\":{\"name\":\"STATUS_SCHEDULED\",\"state\":\"pre\",\"detail\":\"7:45 PM\"}}," +
                    "\"competitors\":[" +
                    "{\"homeAway\":\"home\",\"score\":\"0\",\"team\":{\"id\":\"368\",\"abbreviation\":\"EVE\"," +
                    "\"displayName\":\"Everton\",\"shortDisplayName\":\"Everton\"}}," +
                    "{\"homeAway\":\"away\",\"score\":\"0\",\"team\":{\"id\":\"361\",\"abbreviation\":\"NEW\"," +
                    "\"displayName\":\"Newcastle United\",\"shortDisplayName\":\"Newcastle\"}}]}]}";

                Fixture cup = CupSource.MapEvent(JVal.Parse(finished), "EFL", "Carabao Cup", favs, true, false);
                Fixture playing = CupSource.MapEvent(JVal.Parse(live), "EFL", "Carabao Cup", favs, true, true);
                Fixture upcoming = CupSource.MapEvent(JVal.Parse(scheduled), "EFL", "Carabao Cup", favs, true, true);

                bool finishedOk = cup != null && cup.State == MatchState.Finished &&
                                  cup.HomeScore == 2 && cup.AwayScore == 2 &&
                                  cup.Note == "Pens 4\u20133" && cup.Venue == "Etihad Stadium" &&
                                  cup.HomeId == "65" && cup.CompetitionCode == "EFL";

                bool liveOk = playing != null && playing.State == MatchState.Live &&
                              playing.MinuteValue == 67 && playing.HomeScore == 1;

                // not one of mine, but a cup tie in play still comes through when "all live" is on
                bool upcomingOk = upcoming != null && upcoming.State == MatchState.Scheduled &&
                                  !upcoming.HomeScore.HasValue && !upcoming.AwayScore.HasValue;

                bool dropOk = CupSource.MapEvent(JVal.Parse(live), "EFL", "Carabao Cup", favs, false, false) == null;

                // the merge: a tie the feed already has is not added twice, a new one is
                var feedList = new List<Fixture>();
                var fromFeed = new Fixture();
                fromFeed.Id = "560631";
                fromFeed.HomeName = "Man City";
                fromFeed.AwayName = "Brighton";
                fromFeed.KickoffUtc = new DateTime(2026, 10, 28, 19, 30, 0, DateTimeKind.Utc);
                feedList.Add(fromFeed);

                var cupList = new List<Fixture>();
                var sameTie = new Fixture();
                sameTie.Id = "e401";
                sameTie.HomeName = "Man City";
                sameTie.AwayName = "Brighton";
                sameTie.KickoffUtc = new DateTime(2026, 10, 28, 19, 30, 0, DateTimeKind.Utc);
                cupList.Add(sameTie);

                var newTie = new Fixture();
                newTie.Id = "e402";
                newTie.HomeName = "Liverpool";
                newTie.AwayName = "Chelsea";
                newTie.KickoffUtc = new DateTime(2026, 10, 28, 20, 0, 0, DateTimeKind.Utc);
                cupList.Add(newTie);

                AppController.MergeCupList(feedList, cupList);
                bool mergeOk = feedList.Count == 2 &&
                               feedList.Exists(delegate(Fixture x) { return x.Id == "e402"; }) &&
                               !feedList.Exists(delegate(Fixture x) { return x.Id == "e401"; });

                Check(report, "cup source: pens, live minute, scheduled, favourite rewrite",
                    finishedOk && liveOk && upcomingOk && dropOk && mergeOk,
                    "finished=" + finishedOk + " live=" + liveOk + " scheduled=" + upcomingOk +
                    " dropped=" + dropOk + " merged=" + mergeOk,
                    ref failures);
            }
            catch (Exception ex)
            {
                Check(report, "cup source: pens, live minute, scheduled, favourite rewrite", false,
                    ex.Message, ref failures);
            }

            report.AppendLine("");
            report.AppendLine(failures == 0 ? "RESULT: all checks passed" : "RESULT: " + failures + " failure(s)");

            string output = Path.Combine(Paths.Dir, "selftest.log");
            try
            {
                File.WriteAllText(output, report.ToString(), Encoding.UTF8);
            }
            catch
            {
            }

            return failures == 0 ? 0 : 1;
        }

        private static TimeZoneInfo FindDstZone()
        {
            string[] candidates = new string[]
            {
                "Central Europe Standard Time", "W. Europe Standard Time", "Romance Standard Time",
                "Pacific Standard Time", "Eastern Standard Time"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(candidates[i]);
                    if (zone.SupportsDaylightSavingTime)
                    {
                        return zone;
                    }
                }
                catch
                {
                }
            }

            return TimeZoneInfo.Utc;
        }

        private static void Check(StringBuilder report, string name, bool ok, string detail, ref int failures)
        {
            report.Append(ok ? "PASS  " : "FAIL  ");
            report.Append(name);
            if (!string.IsNullOrEmpty(detail))
            {
                report.Append("   [");
                report.Append(detail);
                report.Append("]");
            }

            report.AppendLine();
            if (!ok)
            {
                failures++;
            }
        }
    }
}
