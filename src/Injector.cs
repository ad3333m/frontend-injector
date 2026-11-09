// Frontend Injector - the work: find the Switch's SD card, fetch the newest
// HomeNX package and copy it over.
//
// HomeNX and CardsNX ship as one package (the release's HomeNX-full.zip, made
// of atmosphere/ and switch/), with NXToggle inside it. Which of the two the
// HOME button opens is one line in config/SwitchU/frontend.txt, and "off" is
// the daemon renamed aside: both exactly what NXToggle itself does, so the
// console and this app always agree.
//
// Built with the .NET Framework compiler (C# 5).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace FrontendInjector
{
    // The file that is there can't be taken away because something has it
    // open. Thrown before anything is changed: the old copy is intact.
    internal sealed class InUseException : IOException
    {
        internal InUseException(string message) : base(message) { }
    }

    // The top of an SD card, however it is reached. Paths are "a/b/c".
    internal interface ITarget : IDisposable
    {
        bool Exists(string path);
        // Makes the folders on the way and replaces a file that is there.
        void Write(string path, Stream data, long size);
        // A file that isn't there is not an error.
        void Delete(string path);
        // A new name in the same folder.
        void Rename(string path, string newName);
        bool Alive();
    }

    // The SD card as a drive letter: Hekate's USB mode, or a card reader.
    internal sealed class FolderTarget : ITarget
    {
        private readonly string _root;

        internal FolderTarget(string root) { _root = root; }

        public bool Exists(string path) { return File.Exists(Local(path)); }

        public void Write(string path, Stream data, long size)
        {
            string file = Local(path);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            FileStream output;
            try { output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None); }
            catch (IOException ex)
            {
                if (ex.HResult == unchecked((int)0x80070020)) throw new InUseException(ex.Message);   // sharing violation
                throw;
            }
            using (output)
            {
                data.CopyTo(output);
                // On the card before the next file starts: people pull the
                // cable the moment a progress bar is full.
                output.Flush(true);
            }
        }

        public void Delete(string path) { File.Delete(Local(path)); }

        public void Rename(string path, string newName)
        {
            string from = Local(path);
            File.Move(from, Path.Combine(Path.GetDirectoryName(from), newName));
        }

        public bool Alive() { return Directory.Exists(_root); }
        public void Dispose() { }

        private string Local(string path)
        {
            return Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    // A Switch that is plugged in and showing its SD card.
    internal sealed class Found
    {
        internal string Drive;      // "E:\" when the card is a drive letter
        internal string DeviceId;   // the MTP device otherwise
        internal bool Installed;    // a frontend is switched on

        internal string Label
        {
            get { return Drive != null ? "SD card (" + Drive.Substring(0, 2) + ")" : "USB file transfer"; }
        }

        internal ITarget Open()
        {
            if (Drive != null) return new FolderTarget(Drive);
            MtpTarget target = MtpTarget.Open(DeviceId);
            if (target == null) throw new IOException("The Switch stopped showing its SD card.");
            return target;
        }
    }

    internal static class Finder
    {
        // What each plugged-in MTP device turned out to be (null: not a Switch
        // showing its SD card), so a device is opened once, not every poll.
        private static readonly Dictionary<string, Found> Seen =
            new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);

        internal static void Forget() { Seen.Clear(); }

        internal static Found Find()
        {
            string system = Path.GetPathRoot(Environment.SystemDirectory);
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Network || drive.DriveType == DriveType.CDRom) continue;
                    if (string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase) || !drive.IsReady) continue;
                    if (!Directory.Exists(Path.Combine(drive.Name, "atmosphere"))) continue;
                    Found card = new Found();
                    card.Drive = drive.Name;
                    card.Installed = File.Exists(Path.Combine(drive.Name, Injector.Daemon.Replace('/', '\\')));
                    return card;
                }
                catch { }
            }

            string[] ids = MtpTarget.DeviceIds();
            foreach (string gone in new List<string>(Seen.Keys))
                if (Array.IndexOf(ids, gone) < 0) Seen.Remove(gone);

            foreach (string id in ids)
            {
                Found known;
                if (!Seen.TryGetValue(id, out known))
                {
                    try
                    {
                        using (MtpTarget target = MtpTarget.Open(id))
                        {
                            if (target != null)
                            {
                                known = new Found();
                                known.DeviceId = id;
                                known.Installed = target.Exists(Injector.Daemon);
                            }
                        }
                        Seen[id] = known;
                    }
                    catch
                    {
                        // Still starting up, or busy: ask again on the next poll.
                    }
                }
                if (known != null) return known;
            }
            return null;
        }
    }

    internal static class Injector
    {
        // The HOME menu replacement itself. While this file is there the
        // frontend is on; without it Nintendo's HOME menu starts.
        internal const string Daemon = "atmosphere/contents/0100000000001000/exefs.nsp";
        internal const string DaemonOff = "exefs.nsp.disabled";   // NXToggle's name for "off"
        internal const string LookFile = "config/SwitchU/frontend.txt";
        internal const string BuildFile = "switch/SwitchU/homenx-build.txt";
        // NXToggle as the package carries it, and where the Switch lists apps.
        internal const string TogglePacked = "switch/SwitchU/bin/homenx-toggle.nro";
        internal const string ToggleApp = "switch/NXToggle/NXToggle.nro";
        internal const string PackageUrl = "https://github.com/ad3333m/homenx/releases/download/latest/HomeNX-full.zip";

        internal sealed class Result
        {
            internal string Build = "";
            internal int Files;
            internal int Kept;   // in use on the Switch, old copy left in place
        }

        // Only plain files under the two folders a HomeNX package is made of.
        internal static bool Wanted(string name)
        {
            if (name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return false;
            string[] parts = name.Split('/');
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == "..") return false;   // folders end in "/"
            return parts.Length > 1 && (parts[0] == "atmosphere" || parts[0] == "switch");
        }

        // Copy order. The menu and the daemon talk to each other and must be
        // the same build, and the daemon is what switches the frontend on: so
        // pictures and sounds first, programs after, the daemon last. A copy
        // that stops halfway leaves whatever worked before.
        internal static int Rank(string name)
        {
            if (name == Daemon) return 2;
            return name.StartsWith("switch/SwitchU/bin/", StringComparison.Ordinal) ? 1 : 0;
        }

        // Copies the whole package, NXToggle included, and switches the frontend on.
        internal static Result Inject(ITarget sd, string package, Action<int, int> progress)
        {
            Result result = new Result();
            using (ZipArchive zip = ZipFile.OpenRead(package))
            {
                // Where each file goes. HomeNX only puts NXToggle in the app
                // list the first time it starts; doing it here means it's
                // there from the start, to switch off or change style.
                List<KeyValuePair<string, ZipArchiveEntry>> files = new List<KeyValuePair<string, ZipArchiveEntry>>();
                bool daemon = false, toggle = false;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!Wanted(entry.FullName)) continue;
                    files.Add(new KeyValuePair<string, ZipArchiveEntry>(entry.FullName, entry));
                    if (entry.FullName == Daemon) daemon = true;
                    if (entry.FullName != TogglePacked) continue;
                    files.Add(new KeyValuePair<string, ZipArchiveEntry>(ToggleApp, entry));
                    toggle = true;
                }
                if (!daemon || !toggle) throw new InvalidDataException("That isn't a HomeNX package.");
                files.Sort(delegate(KeyValuePair<string, ZipArchiveEntry> a, KeyValuePair<string, ZipArchiveEntry> b)
                {
                    return Rank(a.Key).CompareTo(Rank(b.Key));
                });

                for (int i = 0; i < files.Count; i++)
                {
                    string name = files[i].Key;
                    ZipArchiveEntry entry = files[i].Value;
                    try
                    {
                        using (Stream data = entry.Open()) sd.Write(name, data, entry.Length);
                    }
                    catch (InUseException ex)
                    {
                        // A pulled cable can look like "in use" too.
                        if (!sd.Alive()) throw new IOException("The Switch went away while copying " + name + ".", ex);
                        if (Rank(name) > 0)
                            throw new IOException(name + " is in use on the Switch. Start it in Hekate's USB mode " +
                                "(Tools > USB Tools > SD Card) and inject again.", ex);
                        result.Kept++;   // a picture or sound the running menu holds open keeps its old copy
                    }
                    catch (Exception ex)
                    {
                        throw new IOException("Couldn't copy " + name + ": " + ex.Message, ex);
                    }
                    if (name == BuildFile)
                        using (StreamReader reader = new StreamReader(entry.Open())) result.Build = reader.ReadToEnd().Trim();
                    progress(i + 1, files.Count);
                }
                result.Files = files.Count;

                // Left over from an earlier "off": with both files there NXToggle reports a conflict.
                sd.Delete(Daemon + ".disabled");
            }
            return result;
        }

        // Which home menu the HOME button opens: "cards" or "homenx".
        internal static void SetLook(ITarget sd, string look)
        {
            byte[] line = Encoding.ASCII.GetBytes(look + "\n");
            using (MemoryStream data = new MemoryStream(line)) sd.Write(LookFile, data, line.Length);
        }

        // Nintendo's HOME menu from the next start. Everything stays on the
        // card, so NXToggle, or injecting again, turns it back on.
        internal static void TurnOff(ITarget sd)
        {
            if (!sd.Exists(Daemon)) return;
            try
            {
                sd.Delete(Daemon + ".disabled");
                sd.Rename(Daemon, DaemonOff);
            }
            catch
            {
                // Not every MTP responder can rename. Off is what was asked
                // for, and without the file it is off; injecting brings it back.
                sd.Delete(Daemon);
            }
        }

        // The newest package, downloaded once per release: GitHub's ETag says
        // whether the copy from last time is still the newest.
        internal static string Fetch(Action<long, long> progress)
        {
            string package = Path.Combine(Paths.DataDir, "HomeNX-full.zip");
            string tag = package + ".etag", part = package + ".part";
            Directory.CreateDirectory(Paths.DataDir);
            bool have = File.Exists(package) && File.Exists(tag);

            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(PackageUrl);
                request.UserAgent = "FrontendInjector/1.0";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 30000;
                if (have) request.Headers[HttpRequestHeader.IfNoneMatch] = File.ReadAllText(tag);

                string newTag;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    long total = response.ContentLength, done = 0;
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = File.Create(part))
                    {
                        byte[] chunk = new byte[1 << 16];
                        int n;
                        while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            output.Write(chunk, 0, n);
                            done += n;
                            progress(done, total);
                        }
                    }
                    if (total > 0 && done != total) throw new IOException("The download stopped early.");
                    newTag = response.Headers[HttpResponseHeader.ETag] ?? "";
                }
                File.Delete(package);
                File.Move(part, package);
                File.WriteAllText(tag, newTag);
            }
            catch (Exception ex)
            {
                // 304 Not Modified lands here too. Either way the copy from
                // last time is the best there is; with none, say why.
                if (!have) throw new IOException("Couldn't download HomeNX: " + ex.Message, ex);
            }
            return package;
        }

        internal static void DropPackage()
        {
            try
            {
                File.Delete(Path.Combine(Paths.DataDir, "HomeNX-full.zip"));
                File.Delete(Path.Combine(Paths.DataDir, "HomeNX-full.zip.etag"));
            }
            catch { }
        }
    }

    // tests\run.cmd: the copy rules, run against a folder standing in for the SD card.
    internal static class SelfTest
    {
        internal static int Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "frontend-injector-selftest");
            string report = dir + ".txt";
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                string sd = Path.Combine(dir, "sd"), package = Path.Combine(dir, "pack.zip");
                string contents = Path.Combine(sd, "atmosphere", "contents", "0100000000001000");
                string daemon = Path.Combine(contents, "exefs.nsp"), off = daemon + ".disabled";
                string look = Path.Combine(sd, "config", "SwitchU", "frontend.txt");
                Directory.CreateDirectory(contents);
                File.WriteAllText(daemon, "old");
                File.WriteAllText(off, "old");

                using (ZipArchive zip = ZipFile.Open(package, ZipArchiveMode.Create))
                {
                    Add(zip, "switch/SwitchU/bin/menu/main", "menu");
                    Add(zip, Injector.Daemon, "daemon");
                    Add(zip, Injector.TogglePacked, "toggle");
                    Add(zip, Injector.BuildFile, "build 7\n");
                    Add(zip, "switch/SwitchU/empty.bin", "");
                    Add(zip, "switch/SwitchU/sounds/", "");
                    Add(zip, "../outside.txt", "no");
                    Add(zip, "switch/../../outside.txt", "no");
                    Add(zip, "bootloader/hekate_ipl.ini", "no");
                    Add(zip, "loose.txt", "no");
                }

                Check(Injector.Rank(Injector.Daemon) > Injector.Rank("switch/SwitchU/bin/menu/main"), "daemon after menu");
                Check(Injector.Rank("switch/SwitchU/bin/menu/main") > Injector.Rank("switch/SwitchU/fonts/a.ttf"), "menu after assets");
                Check(Injector.Wanted("atmosphere/a") && !Injector.Wanted("switch\\a") && !Injector.Wanted("C:/a")
                      && !Injector.Wanted("switch//a") && !Injector.Wanted("switch"), "path rules");

                Action<int, int> quiet = delegate { };
                Injector.Result r;
                using (ITarget t = new FolderTarget(sd)) r = Injector.Inject(t, package, quiet);
                Check(r.Build == "build 7" && r.Files == 6 && r.Kept == 0, "result " + r.Build + "/" + r.Files + "/" + r.Kept);
                Check(File.ReadAllText(daemon) == "daemon", "daemon replaced");
                Check(!File.Exists(off), "off copy removed");
                Check(File.ReadAllText(Path.Combine(sd, "switch", "SwitchU", "bin", "menu", "main")) == "menu", "menu copied");
                Check(File.ReadAllText(Path.Combine(sd, "switch", "NXToggle", "NXToggle.nro")) == "toggle", "NXToggle in the app list");
                Check(File.ReadAllText(Path.Combine(sd, "switch", "SwitchU", "bin", "homenx-toggle.nro")) == "toggle", "NXToggle's packed copy");
                Check(new FileInfo(Path.Combine(sd, "switch", "SwitchU", "empty.bin")).Length == 0, "empty file copied");
                Check(!File.Exists(look), "style left for the user to pick");
                Check(!File.Exists(Path.Combine(dir, "outside.txt")) && !File.Exists(Path.Combine(sd, "outside.txt"))
                      && !Directory.Exists(Path.Combine(sd, "bootloader")) && !File.Exists(Path.Combine(sd, "loose.txt")),
                      "nothing outside atmosphere/ and switch/");

                using (ITarget t = new FolderTarget(sd))
                {
                    Injector.SetLook(t, "cards");
                    Check(File.ReadAllText(look) == "cards\n", "style written");
                    Injector.SetLook(t, "homenx");
                    Check(File.ReadAllText(look) == "homenx\n", "style changed");

                    // Off is NXToggle's off; injecting again is on.
                    Injector.TurnOff(t);
                    Check(!t.Exists(Injector.Daemon) && File.ReadAllText(off) == "daemon", "turned off");
                    Injector.TurnOff(t);
                    Check(File.ReadAllText(off) == "daemon", "off twice changes nothing");
                    Injector.Inject(t, package, quiet);
                    Check(File.ReadAllText(daemon) == "daemon" && !File.Exists(off), "injecting turns it back on");
                    Check(File.ReadAllText(look) == "homenx\n", "injecting keeps the style");
                }

                // A file the Switch is using keeps its old copy; a program in use stops the copy.
                using (File.Open(Path.Combine(sd, "switch", "SwitchU", "homenx-build.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                using (ITarget t = new FolderTarget(sd))
                    Check(Injector.Inject(t, package, quiet).Kept == 1, "busy asset kept");
                bool stopped = false;
                using (File.Open(daemon, FileMode.Open, FileAccess.Read, FileShare.None))
                using (ITarget t = new FolderTarget(sd))
                {
                    try { Injector.Inject(t, package, quiet); }
                    catch (IOException) { stopped = true; }
                }
                Check(stopped, "busy daemon stops the copy");

                // A package missing the daemon, or NXToggle, isn't one.
                string bad = Path.Combine(dir, "bad.zip");
                using (ZipArchive zip = ZipFile.Open(bad, ZipArchiveMode.Create)) Add(zip, Injector.Daemon, "x");
                bool refused = false;
                using (ITarget t = new FolderTarget(sd))
                {
                    try { Injector.Inject(t, bad, quiet); }
                    catch (InvalidDataException) { refused = true; }
                }
                Check(refused && File.ReadAllText(daemon) == "daemon", "incomplete package refused, nothing touched");

                Directory.Delete(dir, true);
                File.WriteAllText(report, "ok");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(report, "FAILED: " + ex);
                return 1;
            }
        }

        private static void Add(ZipArchive zip, string name, string text)
        {
            using (StreamWriter w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(text);
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception(what);
        }
    }
}
