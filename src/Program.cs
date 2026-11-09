// Frontend Injector - puts HomeNX or CardsNX on a modded Switch through the
// USB cable: finds the console's SD card (a drive from Hekate's USB mode, or
// an MTP responder such as DBI or Sphaira), downloads the newest HomeNX
// release and copies it over.
//
// Same shell as AppTotal and the other *Total apps: a WinForms window hosting
// Edge WebView2, the interface (web\*) embedded in the exe and served from a
// private https origin. This file is the host; Injector.cs and Wpd.cs do the work.
//
// Built with the .NET Framework compiler (C# 5).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("Frontend Injector")]
[assembly: AssemblyProduct("Frontend Injector")]
[assembly: AssemblyDescription("Puts HomeNX or CardsNX on a modded Switch over USB")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace FrontendInjector
{
    internal static class Program
    {
        internal const string AppName = "Frontend Injector";
        internal const string RuntimeUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        [STAThread]
        private static int Main(string[] argv)
        {
            if (argv.Length == 1 && argv[0] == "--selftest") return SelfTest.Run();

            AppDomain.CurrentDomain.AssemblyResolve += Embedded.Resolve;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, AppName + " hit a problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Launch();
            return 0;
        }

        // Kept out of Main so the WebView2 types load after the resolver is in place.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Launch()
        {
            Embedded.UnpackNativeLoader();
            Application.Run(new MainForm());
        }
    }

    internal static class Paths
    {
        internal static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrontendInjector"); }
        }
        internal static string BrowserData { get { return Path.Combine(DataDir, "WebView2"); } }
        internal static string NativeDir { get { return Path.Combine(DataDir, "bin"); } }
    }

    internal static class Embedded
    {
        internal static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name.IndexOf("Microsoft.Web.WebView2", StringComparison.OrdinalIgnoreCase) < 0) return null;
            byte[] raw = Read(name + ".dll");
            return raw == null ? null : Assembly.Load(raw);
        }

        // The WebView2 managed core P/Invokes WebView2Loader.dll by bare name.
        // Unpacking it and LoadLibrary-ing the full path puts it in the process
        // module list, so that later bare-name lookup resolves.
        internal static void UnpackNativeLoader()
        {
            try
            {
                byte[] raw = Read("WebView2Loader.dll");
                if (raw == null) return;
                Directory.CreateDirectory(Paths.NativeDir);
                string path = Path.Combine(Paths.NativeDir, "WebView2Loader.dll");
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length != raw.Length) File.WriteAllBytes(path, raw);
                Native.LoadLibrary(path);
            }
            catch
            {
                // Non-fatal: if the runtime can find its own loader we are fine.
            }
        }

        internal static byte[] Read(string resource)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) return null;
                using (MemoryStream copy = new MemoryStream())
                {
                    s.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }
    }

    internal static class Json
    {
        internal static object Parse(string text) { return new JavaScriptSerializer().DeserializeObject(text); }
        internal static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }

        internal static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    internal static class VirtualFiles
    {
        internal const string Host = "app.frontendinjector.example";
        internal const string Origin = "https://" + Host;

        internal const string Csp =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; " +
            "connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

        private static readonly string[][] Map =
        {
            new[] { "/", "index.html", "text/html; charset=utf-8" },
            new[] { "/app.css", "app.css", "text/css; charset=utf-8" },
            new[] { "/app.js", "app.js", "text/javascript; charset=utf-8" },
            new[] { "/homenx.jpg", "homenx.jpg", "image/jpeg" },
            new[] { "/cardsnx.jpg", "cardsnx.jpg", "image/jpeg" },
            new[] { "/nxtoggle.jpg", "nxtoggle.jpg", "image/jpeg" },
        };

        internal static string[] Lookup(Uri u)
        {
            if (!IsOurs(u)) return null;
            for (int i = 0; i < Map.Length; i++)
                if (string.Equals(Map[i][0], u.AbsolutePath, StringComparison.Ordinal))
                    return new[] { Map[i][1], Map[i][2] };
            return null;
        }

        internal static bool IsOurs(Uri u)
        {
            return u.Scheme == Uri.UriSchemeHttps && string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase) && u.IsDefaultPort;
        }

        internal static bool IsAppPage(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && IsOurs(u);
        }
    }

    internal static class Theme
    {
        internal static readonly Color Night = Color.FromArgb(8, 11, 22);
        internal static readonly Color Ink = Color.FromArgb(230, 234, 245);
        internal static readonly Color Edge = Color.FromArgb(28, 34, 56);
    }

    internal sealed class MainForm : Form
    {
        private readonly WebView2 _web = new WebView2();
        private readonly object _switch = new object();   // one job talks to the Switch at a time
        private int _lastProgress;

        internal MainForm()
        {
            Text = Program.AppName;
            BackColor = Theme.Night;
            ForeColor = Theme.Ink;
            byte[] icon = Embedded.Read("app.ico");
            if (icon != null) Icon = new Icon(new MemoryStream(icon));

            // Sized for the page, but never taller than the screen: at 150%
            // scaling a 1080p laptop has about 630 of these pixels to give.
            float scale = DeviceDpi / 96f;
            int room = Screen.PrimaryScreen.WorkingArea.Height - (int)(56 * scale);
            ClientSize = new Size((int)(720 * scale), Math.Min((int)(500 * scale), room));
            MinimumSize = new Size((int)(480 * scale), (int)(420 * scale));
            StartPosition = FormStartPosition.CenterScreen;

            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Theme.Night;
            _web.CreationProperties = new CoreWebView2CreationProperties();
            _web.CreationProperties.UserDataFolder = Paths.BrowserData;
            _web.CoreWebView2InitializationCompleted += OnCoreReady;
            Controls.Add(_web);
        }

        // Automated runs set this so the window doesn't take the keyboard from
        // whoever is using the PC.
        protected override bool ShowWithoutActivation
        {
            get { return Environment.GetEnvironmentVariable("FRONTEND_INJECTOR_TEST") == "1"; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleCaption(Handle, Theme.Night, Theme.Ink, Theme.Edge);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _web.EnsureCoreWebView2Async(null);
        }

        private void OnCoreReady(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                if (MessageBox.Show(Program.AppName + " needs the Microsoft Edge WebView2 runtime, which is missing or failed to start.\r\n\r\nOpen Microsoft's download page now?",
                        Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try { Process.Start(Program.RuntimeUrl); }
                    catch { }
                }
                Close();
                return;
            }

            CoreWebView2 core = _web.CoreWebView2;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsWebMessageEnabled = true;

            core.AddWebResourceRequestedFilter(VirtualFiles.Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs a)
            {
                if (!VirtualFiles.IsAppPage(a.Uri)) a.Cancel = true;
            };
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs a) { a.Handled = true; };
            core.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs a)
            {
                a.State = CoreWebView2PermissionState.Deny;
                a.Handled = true;
            };
            core.Navigate(VirtualFiles.Origin + "/");
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            Uri u;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out u)) return;
            CoreWebView2Environment env = _web.CoreWebView2.Environment;
            string[] hit = VirtualFiles.Lookup(u);
            byte[] raw = hit == null ? null : Embedded.Read(hit[0]);
            if (raw == null)
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }
            e.Response = env.CreateWebResourceResponse(new MemoryStream(raw), 200, "OK",
                "Content-Type: " + hit[1] + "\r\nCache-Control: no-cache\r\nX-Content-Type-Options: nosniff\r\n" +
                "Content-Security-Policy: " + VirtualFiles.Csp);
        }

        // ---- page <-> host ---------------------------------------------------

        private void Post(Dictionary<string, object> message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)delegate { Post(message); }); }
                catch { }
                return;
            }
            if (_web.CoreWebView2 == null) return;
            try { _web.CoreWebView2.PostWebMessageAsJson(Json.Write(message)); }
            catch { }
        }

        private void Reply(long id, bool ok, object data)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["type"] = "reply";
            m["id"] = id;
            m["ok"] = ok;
            m[ok ? "data" : "error"] = data;
            Post(m);
        }

        private void Progress(string step, long done, long total)
        {
            // A 64 MB download reports a thousand times; the page needs ten a second.
            int now = Environment.TickCount;
            if (done != total && done != 0 && unchecked(now - _lastProgress) < 100) return;
            _lastProgress = now;
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["type"] = "progress";
            m["step"] = step;
            m["done"] = done;
            m["total"] = total;
            Post(m);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!VirtualFiles.IsAppPage(e.Source)) return;
            Dictionary<string, object> msg;
            try { msg = Json.Parse(e.WebMessageAsJson) as Dictionary<string, object>; }
            catch { return; }
            if (msg == null) return;

            long id;
            try { id = Convert.ToInt64(msg["id"], CultureInfo.InvariantCulture); }
            catch { return; }
            string cmd = Json.Str(msg, "cmd");
            string look = Json.Str(msg, "look");

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    object data;
                    lock (_switch)
                    {
                        if (cmd == "detect") data = Detect();
                        else if (cmd == "inject") data = Inject();
                        else if (cmd == "look" && (look == "cards" || look == "homenx")) data = OnSwitch(delegate(ITarget sd) { Injector.SetLook(sd, look); });
                        else if (cmd == "off") data = OnSwitch(Injector.TurnOff);
                        else throw new InvalidOperationException("Unknown request.");
                    }
                    Reply(id, true, data);
                }
                catch (Exception ex) { Reply(id, false, ex.Message); }
            });
        }

        private static object Detect()
        {
            Found found = Finder.Find();
            if (found == null) return null;
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["label"] = found.Label;
            d["drive"] = found.Drive != null;
            d["installed"] = found.Installed;
            return d;
        }

        private static Found Connected()
        {
            Found found = Finder.Find();
            if (found == null) throw new IOException("The Switch isn't connected any more.");
            return found;
        }

        private object Inject()
        {
            Found found = Connected();
            Progress("download", 0, 0);
            string package = Injector.Fetch(delegate(long done, long total) { Progress("download", done, total); });

            Injector.Result result;
            try
            {
                using (ITarget sd = found.Open())
                    result = Injector.Inject(sd, package, delegate(int done, int total) { Progress("copy", done, total); });
            }
            catch (InvalidDataException)
            {
                Injector.DropPackage();
                throw new IOException("The download was damaged. Try again.");
            }
            finally { Finder.Forget(); }

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["build"] = result.Build;
            d["files"] = result.Files;
            d["kept"] = result.Kept;
            d["drive"] = found.Drive != null;
            return d;
        }

        // One small change on the card: the style, or off.
        private static object OnSwitch(Action<ITarget> change)
        {
            Found found = Connected();
            try
            {
                using (ITarget sd = found.Open()) change(sd);
            }
            finally { Finder.Forget(); }
            return null;
        }
    }

    internal static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadLibrary(string path);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Dark title bar in the window's own colours (the colours need Windows 11).
        internal static void StyleCaption(IntPtr hWnd, Color caption, Color text, Color border)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hWnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hWnd, 19, ref on, sizeof(int));
                int c = ColorRef(caption), t = ColorRef(text), b = ColorRef(border);
                DwmSetWindowAttribute(hWnd, 35, ref c, sizeof(int));
                DwmSetWindowAttribute(hWnd, 36, ref t, sizeof(int));
                DwmSetWindowAttribute(hWnd, 34, ref b, sizeof(int));
            }
            catch { }
        }
    }
}
