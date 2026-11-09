// Frontend Injector - the Switch as a USB file-transfer (MTP) device, reached
// through Windows Portable Devices.
//
// Only the calls an install needs are declared. A COM interface is a table of
// slots, so the unused ones before the last call we make are kept as SlotN
// placeholders. Ids, slot order and parameter directions were checked against
// PortableDeviceApi.dll's own type library: tools\wpd-check.ps1 prints it.
//
// Built with the .NET Framework compiler (C# 5), x64 only (PropVariant's size).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ComStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace FrontendInjector
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;

        public PropertyKey(string format, uint id)
        {
            Format = new Guid(format);
            Id = id;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("A1567595-4C2F-4574-A6FA-ECEF917B9A40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceManager
    {
        [PreserveSig] int GetDevices([In, Out] IntPtr[] ids, ref uint count);
    }

    [ComImport, Guid("625E2DF8-6392-4CF0-9AD1-3CFA5F17775C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevice
    {
        void Open([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IPortableDeviceValues clientInfo);
        void Slot4();
        void Content(out IPortableDeviceContent content);
        void Slot6();
        void Slot7();
        void Close();
    }

    [ComImport, Guid("6A96ED84-7C73-4480-9938-BF5AF477D426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceContent
    {
        void EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parentId,
                         IPortableDeviceValues filter, out IEnumPortableDeviceObjectIDs result);
        void Properties(out IPortableDeviceProperties properties);
        void Slot5();
        void CreateObjectWithPropertiesOnly(IPortableDeviceValues values, ref IntPtr objectId);
        void CreateObjectWithPropertiesAndData(IPortableDeviceValues values, out ComStream data,
                                               ref uint bestChunk, IntPtr cookie);
        [PreserveSig] int Delete(uint options, IPortableDevicePropVariantCollection ids, IntPtr results);
    }

    [ComImport, Guid("10ECE955-CF41-4728-BFA0-41EEDF1BBF19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumPortableDeviceObjectIDs
    {
        [PreserveSig] int Next(uint count, [In, Out] IntPtr[] ids, ref uint fetched);
    }

    [ComImport, Guid("7F6D695C-03DF-4439-A809-59266BEEE3A6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceProperties
    {
        void Slot3();
        void Slot4();
        [PreserveSig] int GetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceKeyCollection keys,
                                    out IPortableDeviceValues values);
        [PreserveSig] int SetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceValues values,
                                    out IPortableDeviceValues results);
    }

    [ComImport, Guid("6848F6F2-3155-4F86-B6F5-263EEEAB3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceValues
    {
        void Slot3();
        void Slot4();
        void Slot5();
        void Slot6();
        void SetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetStringValue(ref PropertyKey key, out IntPtr value);
        void SetUnsignedIntegerValue(ref PropertyKey key, uint value);
        void Slot10();
        void Slot11();
        void Slot12();
        void SetUnsignedLargeIntegerValue(ref PropertyKey key, ulong value);
        void Slot14();
        void Slot15();
        void Slot16();
        void Slot17();
        void Slot18();
        void Slot19();
        void Slot20();
        void Slot21();
        void Slot22();
        void Slot23();
        void Slot24();
        void Slot25();
        void Slot26();
        void SetGuidValue(ref PropertyKey key, ref Guid value);
    }

    [ComImport, Guid("DADA2357-E0AD-492E-98DB-DD61C53BA353"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDeviceKeyCollection
    {
        void Slot3();
        void Slot4();
        void Add(ref PropertyKey key);
    }

    [ComImport, Guid("89B2E422-4F1B-4316-BCEF-A44AFEA83EB3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPortableDevicePropVariantCollection
    {
        void Slot3();
        void Slot4();
        void Add(ref PropVariant value);
    }

    [ComImport, Guid("0AF10CEC-2ECD-4B92-9581-34F6AE0637F3")]
    internal class PortableDeviceManager { }

    // The free-threaded device object: detection and installs run on pool threads.
    [ComImport, Guid("F7C0039A-4762-488A-B4B3-760EF9A1BA9B")]
    internal class PortableDeviceFtm { }

    [ComImport, Guid("0C15D503-D017-47CE-9016-7B3F978721CC")]
    internal class PortableDeviceValues { }

    [ComImport, Guid("DE2D022D-2480-43BE-97F0-D1FA2CF98F4F")]
    internal class PortableDeviceKeyCollection { }

    [ComImport, Guid("08A99E2F-6D6D-4B80-AF5A-BAF2BCBE4CB9")]
    internal class PortableDevicePropVariantCollection { }

    // The SD card of a Switch that is running an MTP responder (DBI, Sphaira,
    // Atmosphere's USB File Transfer, HomeNX's own).
    internal sealed class MtpTarget : ITarget
    {
        private const string ObjectProps = "EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C";
        private const string ClientProps = "204D9F0C-2292-4080-9F42-40664E70F859";
        private const string DeviceRoot = "DEVICE";

        // Not readonly: they are passed by reference to COM.
        private static PropertyKey ParentId = new PropertyKey(ObjectProps, 3);
        private static PropertyKey Name = new PropertyKey(ObjectProps, 4);
        private static PropertyKey ContentType = new PropertyKey(ObjectProps, 7);
        private static PropertyKey Size = new PropertyKey(ObjectProps, 11);
        private static PropertyKey FileName = new PropertyKey(ObjectProps, 12);
        private static PropertyKey ClientName = new PropertyKey(ClientProps, 2);
        private static PropertyKey ClientMajor = new PropertyKey(ClientProps, 3);
        private static PropertyKey ClientMinor = new PropertyKey(ClientProps, 4);
        private static PropertyKey ClientRevision = new PropertyKey(ClientProps, 5);
        private static Guid FolderType = new Guid("27E2E392-A111-48E0-AB0C-E17705A05F85");

        private readonly IPortableDevice _device;
        private readonly IPortableDeviceContent _content;
        private readonly IPortableDeviceProperties _props;
        private readonly IPortableDeviceKeyCollection _nameKeys;
        private string _root;

        // MTP has no "open by path": every folder is listed once and remembered,
        // name -> object id. A file sent in this session is there with an empty
        // id, because its id is never read back.
        private readonly Dictionary<string, Dictionary<string, string>> _listings =
            new Dictionary<string, Dictionary<string, string>>();

        internal static string[] DeviceIds()
        {
            IPortableDeviceManager manager = (IPortableDeviceManager)new PortableDeviceManager();
            try
            {
                uint count = 0;
                if (manager.GetDevices(null, ref count) < 0 || count == 0) return new string[0];
                IntPtr[] raw = new IntPtr[count];
                if (manager.GetDevices(raw, ref count) < 0) return new string[0];
                string[] ids = new string[Math.Min(count, (uint)raw.Length)];
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = Marshal.PtrToStringUni(raw[i]);
                    Marshal.FreeCoTaskMem(raw[i]);
                }
                return ids;
            }
            finally { Marshal.ReleaseComObject(manager); }
        }

        // Null when the device isn't a Switch showing its SD card. Responders
        // name and number their storages differently, so the SD card is the
        // storage that has an "atmosphere" folder at the top.
        internal static MtpTarget Open(string deviceId)
        {
            MtpTarget target = new MtpTarget(deviceId);
            try
            {
                foreach (string storage in target.Listing(DeviceRoot).Values)
                {
                    if (!target.Listing(storage).ContainsKey("atmosphere")) continue;
                    target._root = storage;
                    return target;
                }
            }
            catch
            {
                target.Dispose();
                throw;
            }
            target.Dispose();
            return null;
        }

        private MtpTarget(string deviceId)
        {
            IPortableDeviceValues who = (IPortableDeviceValues)new PortableDeviceValues();
            who.SetStringValue(ref ClientName, Program.AppName);
            who.SetUnsignedIntegerValue(ref ClientMajor, 1);
            who.SetUnsignedIntegerValue(ref ClientMinor, 0);
            who.SetUnsignedIntegerValue(ref ClientRevision, 0);

            _device = (IPortableDevice)new PortableDeviceFtm();
            _device.Open(deviceId, who);
            Marshal.ReleaseComObject(who);
            _device.Content(out _content);
            _content.Properties(out _props);

            _nameKeys = (IPortableDeviceKeyCollection)new PortableDeviceKeyCollection();
            _nameKeys.Add(ref FileName);
            _nameKeys.Add(ref Name);
        }

        public bool Exists(string path)
        {
            string folder = Folder(path, false);
            return folder != null && Listing(folder).ContainsKey(Leaf(path));
        }

        public void Delete(string path)
        {
            string folder = Folder(path, false);
            if (folder != null) Drop(folder, Leaf(path));
        }

        // MTP renames by changing the object's file name property.
        public void Rename(string path, string newName)
        {
            string folder = Folder(path, false), name = Leaf(path);
            string id = folder == null ? null : IdOf(folder, name);
            if (id == null) throw new FileNotFoundException(path);

            IPortableDeviceValues values = (IPortableDeviceValues)new PortableDeviceValues();
            IPortableDeviceValues results = null;
            try
            {
                values.SetStringValue(ref FileName, newName);
                int hr = _props.SetValues(id, values, out results);
                if (hr != 0)   // S_FALSE: the one property asked for was refused
                    throw new IOException("The Switch wouldn't rename " + name + " (0x" + hr.ToString("X8") + ").");
                Listing(folder).Remove(name);
                Listing(folder)[newName] = id;
            }
            finally
            {
                if (results != null) Marshal.ReleaseComObject(results);
                Marshal.ReleaseComObject(values);
            }
        }

        public void Write(string path, Stream data, long size)
        {
            string folder = Folder(path, true), name = Leaf(path);
            Drop(folder, name);   // MTP can't overwrite: delete, then send

            IPortableDeviceValues values = (IPortableDeviceValues)new PortableDeviceValues();
            ComStream stream = null;
            try
            {
                values.SetStringValue(ref ParentId, folder);
                values.SetUnsignedLargeIntegerValue(ref Size, (ulong)size);
                values.SetStringValue(ref FileName, name);
                values.SetStringValue(ref Name, name);

                uint best = 0;
                _content.CreateObjectWithPropertiesAndData(values, out stream, ref best, IntPtr.Zero);
                // The driver says which chunk size suits this device's USB link.
                byte[] chunk = new byte[best > 0 ? (int)Math.Min(best, 1u << 20) : 1 << 18];
                int n;
                while ((n = data.Read(chunk, 0, chunk.Length)) > 0) stream.Write(chunk, n, IntPtr.Zero);
                stream.Commit(0);
                Listing(folder)[name] = "";
            }
            finally
            {
                if (stream != null) Marshal.ReleaseComObject(stream);
                Marshal.ReleaseComObject(values);
            }
        }

        public bool Alive()
        {
            try
            {
                _listings.Remove(DeviceRoot);
                Listing(DeviceRoot);
                return true;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            try { _device.Close(); }
            catch { }
            object[] held = { _nameKeys, _props, _content, _device };
            foreach (object o in held)
            {
                try { if (o != null) Marshal.ReleaseComObject(o); }
                catch { }
            }
        }

        private static string Leaf(string path)
        {
            return path.Substring(path.LastIndexOf('/') + 1);
        }

        // The object id of the folder holding `path`, null when it isn't there
        // and wasn't asked for.
        private string Folder(string path, bool create)
        {
            string[] parts = path.Split('/');
            string id = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                Dictionary<string, string> inside = Listing(id);
                string next;
                if (!inside.TryGetValue(parts[i], out next))
                {
                    if (!create) return null;
                    next = CreateFolder(id, parts[i]);
                    inside[parts[i]] = next;
                    _listings[next] = NewListing();   // just made, so empty
                }
                id = next;
            }
            return id;
        }

        private static Dictionary<string, string> NewListing()
        {
            // The SD card is FAT or exFAT: names don't differ by case.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // ponytail: one property request per object. A folder with thousands of
        // entries (a huge cheat pack in atmosphere/contents) lists slowly;
        // IPortableDevicePropertiesBulk is the upgrade if that ever hurts.
        private Dictionary<string, string> Listing(string folder)
        {
            Dictionary<string, string> listing;
            if (_listings.TryGetValue(folder, out listing)) return listing;

            listing = NewListing();
            IEnumPortableDeviceObjectIDs objects;
            _content.EnumObjects(0, folder, null, out objects);
            try
            {
                IntPtr[] raw = new IntPtr[64];
                while (true)
                {
                    uint got = 0;
                    int hr = objects.Next((uint)raw.Length, raw, ref got);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                    string[] ids = new string[got];
                    for (int i = 0; i < ids.Length; i++)
                    {
                        ids[i] = Marshal.PtrToStringUni(raw[i]);
                        Marshal.FreeCoTaskMem(raw[i]);
                    }
                    foreach (string id in ids)
                    {
                        string name = NameOf(id);
                        if (name != null) listing[name] = id;
                    }
                    if (hr != 0 || got == 0) break;   // S_FALSE: that was the last batch
                }
            }
            finally { Marshal.ReleaseComObject(objects); }

            _listings[folder] = listing;
            return listing;
        }

        private string NameOf(string id)
        {
            IPortableDeviceValues values;
            // An object that won't describe itself is left out of the listing.
            if (_props.GetValues(id, _nameKeys, out values) < 0 || values == null) return null;
            try
            {
                // Files and folders carry a file name; a storage only has a name.
                return Text(values, ref FileName) ?? Text(values, ref Name);
            }
            finally { Marshal.ReleaseComObject(values); }
        }

        private static string Text(IPortableDeviceValues values, ref PropertyKey key)
        {
            IntPtr raw;
            if (values.GetStringValue(ref key, out raw) != 0 || raw == IntPtr.Zero) return null;
            string text = Marshal.PtrToStringUni(raw);
            Marshal.FreeCoTaskMem(raw);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private string CreateFolder(string parent, string name)
        {
            IPortableDeviceValues values = (IPortableDeviceValues)new PortableDeviceValues();
            try
            {
                values.SetStringValue(ref ParentId, parent);
                values.SetStringValue(ref Name, name);
                values.SetStringValue(ref FileName, name);
                values.SetGuidValue(ref ContentType, ref FolderType);

                IntPtr raw = IntPtr.Zero;
                _content.CreateObjectWithPropertiesOnly(values, ref raw);
                string id = Marshal.PtrToStringUni(raw);
                Marshal.FreeCoTaskMem(raw);
                if (string.IsNullOrEmpty(id)) throw new IOException("The Switch didn't make the folder " + name + ".");
                return id;
            }
            finally { Marshal.ReleaseComObject(values); }
        }

        // The object id of a file in a folder, null when it isn't there.
        private string IdOf(string folder, string name)
        {
            string id;
            if (!Listing(folder).TryGetValue(name, out id)) return null;
            if (id.Length > 0) return id;
            // Sent earlier in this session: list the folder again for its id.
            _listings.Remove(folder);
            return Listing(folder).TryGetValue(name, out id) ? id : null;
        }

        // Removes a file from a folder when it's there.
        private void Drop(string folder, string name)
        {
            string id = IdOf(folder, name);
            if (id == null) return;

            IPortableDevicePropVariantCollection list =
                (IPortableDevicePropVariantCollection)new PortableDevicePropVariantCollection();
            PropVariant value = new PropVariant();
            value.Type = 31;   // VT_LPWSTR
            value.Pointer = Marshal.StringToCoTaskMemUni(id);
            try
            {
                list.Add(ref value);   // the collection keeps its own copy
                int hr = _content.Delete(0, list, IntPtr.Zero);   // 0: this object only
                if (hr != 0)
                    throw new InUseException("The Switch wouldn't let go of " + name + " (0x" + hr.ToString("X8") + ").");
                Listing(folder).Remove(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
                Marshal.ReleaseComObject(list);
            }
        }
    }
}
