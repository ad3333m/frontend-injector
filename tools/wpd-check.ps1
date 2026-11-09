# Checks the hand-written WPD interop against what Windows itself ships:
# interface ids, method order and parameter directions from the type libraries,
# and property keys by finding their bytes in the system WPD binaries.
$src = @"
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using CT = System.Runtime.InteropServices.ComTypes;

public static class Tlb
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string file, int regkind, out CT.ITypeLib tlb);

    static string TypeName(CT.ITypeInfo ti, CT.TYPEDESC td)
    {
        int vt = td.vt;
        if (vt == 26)
        {
            CT.TYPEDESC inner = (CT.TYPEDESC)Marshal.PtrToStructure(td.lpValue, typeof(CT.TYPEDESC));
            return TypeName(ti, inner) + "*";
        }
        if (vt == 29)
        {
            try
            {
                CT.ITypeInfo rt; ti.GetRefTypeInfo((int)td.lpValue.ToInt64(), out rt);
                string n, d, h; int c; rt.GetDocumentation(-1, out n, out d, out c, out h);
                return n;
            }
            catch { return "user"; }
        }
        return ((VarEnum)vt).ToString().Replace("VT_", "");
    }

    public static string Dump(string path, string[] want)
    {
        CT.ITypeLib tlb; LoadTypeLibEx(path, 2, out tlb);
        StringBuilder sb = new StringBuilder();
        int n = tlb.GetTypeInfoCount();
        for (int i = 0; i < n; i++)
        {
            CT.ITypeInfo ti; tlb.GetTypeInfo(i, out ti);
            string name, doc, help; int ctx; ti.GetDocumentation(-1, out name, out doc, out ctx, out help);
            if (Array.IndexOf(want, name) < 0) continue;
            IntPtr pa; ti.GetTypeAttr(out pa);
            CT.TYPEATTR ta = (CT.TYPEATTR)Marshal.PtrToStructure(pa, typeof(CT.TYPEATTR));
            sb.AppendLine(ta.typekind + " " + name + " {" + ta.guid + "}");
            for (int f = 0; f < ta.cFuncs; f++)
            {
                IntPtr pf; ti.GetFuncDesc(f, out pf);
                CT.FUNCDESC fd = (CT.FUNCDESC)Marshal.PtrToStructure(pf, typeof(CT.FUNCDESC));
                string[] names = new string[fd.cParams + 1]; int got;
                ti.GetNames(fd.memid, names, names.Length, out got);
                StringBuilder ps = new StringBuilder();
                int size = Marshal.SizeOf(typeof(CT.ELEMDESC));
                for (int p = 0; p < fd.cParams; p++)
                {
                    CT.ELEMDESC ed = (CT.ELEMDESC)Marshal.PtrToStructure(new IntPtr(fd.lprgelemdescParam.ToInt64() + p * size), typeof(CT.ELEMDESC));
                    int fl = (int)ed.desc.paramdesc.wParamFlags;
                    string dir = ((fl & 1) != 0 ? "in" : "") + ((fl & 2) != 0 ? "out" : "");
                    if (p > 0) ps.Append(", ");
                    ps.Append("[" + dir + "] " + TypeName(ti, ed.tdesc) + " " + (p + 1 < got ? names[p + 1] : "?"));
                }
                sb.AppendLine("  [" + (fd.oVft / IntPtr.Size) + "] " + names[0] + "(" + ps + ")");
                ti.ReleaseFuncDesc(pf);
            }
            ti.ReleaseTypeAttr(pa);
        }
        return sb.ToString();
    }

    public static bool Has(string file, byte[] pattern)
    {
        byte[] data = File.ReadAllBytes(file);
        for (int i = 0; i + pattern.Length <= data.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j]) j++;
            if (j == pattern.Length) return true;
        }
        return false;
    }
}
"@
Add-Type -TypeDefinition $src

$sys = "$env:WINDIR\System32"
[Tlb]::Dump("$sys\PortableDeviceApi.dll", @(
    "PortableDeviceManager", "PortableDevice", "PortableDeviceFTM",
    "IPortableDeviceManager", "IPortableDevice", "IPortableDeviceContent",
    "IPortableDeviceProperties", "IEnumPortableDeviceObjectIDs"))
[Tlb]::Dump("$sys\PortableDeviceTypes.dll", @(
    "PortableDeviceValues", "PortableDeviceKeyCollection", "PortableDevicePropVariantCollection",
    "IPortableDeviceValues", "IPortableDeviceKeyCollection", "IPortableDevicePropVariantCollection"))

$bins = @("$sys\PortableDeviceApi.dll", "$sys\wpdshext.dll", "$sys\PortableDeviceTypes.dll",
          "$sys\drivers\UMDF\WpdMtpDr.dll", "$sys\WpdMtp.dll") | Where-Object { Test-Path $_ }
$obj = [Guid]"EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C"
$cli = [Guid]"204D9F0C-2292-4080-9F42-40664E70F859"
$keys = [ordered]@{
    "OBJECT_PARENT_ID(3)" = $obj.ToByteArray() + [BitConverter]::GetBytes([uint32]3)
    "OBJECT_NAME(4)" = $obj.ToByteArray() + [BitConverter]::GetBytes([uint32]4)
    "OBJECT_CONTENT_TYPE(7)" = $obj.ToByteArray() + [BitConverter]::GetBytes([uint32]7)
    "OBJECT_SIZE(11)" = $obj.ToByteArray() + [BitConverter]::GetBytes([uint32]11)
    "OBJECT_ORIGINAL_FILE_NAME(12)" = $obj.ToByteArray() + [BitConverter]::GetBytes([uint32]12)
    "CLIENT_NAME(2)" = $cli.ToByteArray() + [BitConverter]::GetBytes([uint32]2)
    "CLIENT_MAJOR(3)" = $cli.ToByteArray() + [BitConverter]::GetBytes([uint32]3)
    "CLIENT_SQOS(8)" = $cli.ToByteArray() + [BitConverter]::GetBytes([uint32]8)
    "CONTENT_TYPE_FOLDER" = ([Guid]"27E2E392-A111-48E0-AB0C-E17705A05F85").ToByteArray()
}
foreach ($k in $keys.Keys) {
    $hits = @($bins | Where-Object { [Tlb]::Has($_, [byte[]]$keys[$k]) } | ForEach-Object { Split-Path $_ -Leaf })
    "{0,-32} {1}" -f $k, ($(if ($hits.Count) { $hits -join ", " } else { "NOT FOUND" }))
}
