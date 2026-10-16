using System.IO;
using System.Text;

namespace AchievementOverlay.Tests;

/// <summary>
/// Writes minimal 64-bit PE files for tests that read import tables and version info: one section
/// holding the import descriptors, the delay-load descriptors and an RT_VERSION resource, as far as
/// each file asks for them. Windows' own version API reads the result, so a test sees what a real
/// DLL's version info would give.
/// </summary>
internal static class PeFixture
{
    /// <summary>The GBE marker <c>SceneEmulatorDetector.IsGbe</c> looks for.</summary>
    private const string GbeMarker = "GSE Saves";

    private const int FileAlignment = 0x200;
    private const int SectionRva = 0x1000;
    private const ulong ImageBase = 0x140000000;

    /// <summary>A PE file importing <paramref name="imports"/> and delay-loading <paramref name="delayImports"/>.</summary>
    public static void WriteImporter(string path, string[]? imports = null, string[]? delayImports = null) => Write(path, imports ?? [], delayImports ?? [], company: null, marker: null);

    /// <summary>A Steam DLL whose version info names Valve, as Valve's own build does.</summary>
    public static void WriteValveDll(string path) => Write(path, [], [], company: "Valve Corporation", marker: null);

    /// <summary>A Steam DLL carrying GBE's marker and GBE's company name.</summary>
    public static void WriteGbeDll(string path) => Write(path, [], [], company: "GSE", marker: GbeMarker);

    private static void Write(string path, string[] imports, string[] delayImports, string? company, string? marker)
    {
        var section = new List<byte>();
        var importDir = (Rva: 0, Size: 0);
        var delayDir = (Rva: 0, Size: 0);
        var resourceDir = (Rva: 0, Size: 0);

        if (imports.Length > 0)
        {
            var table = section.Count;
            section.AddRange(new byte[(imports.Length + 1) * 20]);
            for (var i = 0; i < imports.Length; i++)
                WriteInt(section, table + i * 20 + 12, SectionRva + AppendAsciiz(section, imports[i]));
            importDir = (SectionRva + table, (imports.Length + 1) * 20);
        }

        if (delayImports.Length > 0)
        {
            var table = section.Count;
            section.AddRange(new byte[(delayImports.Length + 1) * 32]);
            for (var i = 0; i < delayImports.Length; i++)
            {
                WriteInt(section, table + i * 32, 1); // attributes: the name is an RVA
                WriteInt(section, table + i * 32 + 4, SectionRva + AppendAsciiz(section, delayImports[i]));
            }
            delayDir = (SectionRva + table, (delayImports.Length + 1) * 32);
        }

        if (company != null)
        {
            Align(section, 4);
            var start = section.Count;
            section.AddRange(VersionResource(company, SectionRva + start));
            resourceDir = (SectionRva + start, section.Count - start);
        }

        if (marker != null)
            section.AddRange(Encoding.ASCII.GetBytes(marker));

        if (section.Count == 0)
            section.Add(0);
        var rawSize = (section.Count + FileAlignment - 1) / FileAlignment * FileAlignment;
        section.AddRange(new byte[rawSize - section.Count]);

        var file = new List<byte>(new byte[FileAlignment])
        {
            [0] = (byte)'M',
            [1] = (byte)'Z'
        };
        WriteInt(file, 0x3C, 0x40);
        var pe = 0x40;
        file[pe] = (byte)'P';
        file[pe + 1] = (byte)'E';
        var coff = pe + 4;
        WriteShort(file, coff, 0x8664);
        WriteShort(file, coff + 2, 1);
        WriteShort(file, coff + 16, 240);
        WriteShort(file, coff + 18, 0x2022);
        var opt = coff + 20;
        WriteShort(file, opt, 0x20B);
        WriteLong(file, opt + 24, ImageBase);
        WriteInt(file, opt + 32, SectionRva);
        WriteInt(file, opt + 36, FileAlignment);
        WriteShort(file, opt + 40, 6);
        WriteShort(file, opt + 48, 6);
        WriteInt(file, opt + 56, SectionRva + (rawSize + 0xFFF) / 0x1000 * 0x1000);
        WriteInt(file, opt + 60, FileAlignment);
        WriteShort(file, opt + 68, 3);
        WriteLong(file, opt + 72, 0x100000);
        WriteLong(file, opt + 80, 0x1000);
        WriteLong(file, opt + 88, 0x100000);
        WriteLong(file, opt + 96, 0x1000);
        WriteInt(file, opt + 108, 16);
        var dirs = opt + 112;
        WriteDir(file, dirs, 1, importDir);
        WriteDir(file, dirs, 2, resourceDir);
        WriteDir(file, dirs, 13, delayDir);
        var header = opt + 240;
        for (var i = 0; i < 5; i++)
            file[header + i] = (byte)".data"[i];
        WriteInt(file, header + 8, rawSize);
        WriteInt(file, header + 12, SectionRva);
        WriteInt(file, header + 16, rawSize);
        WriteInt(file, header + 20, FileAlignment);
        WriteInt(file, header + 36, unchecked((int)0xC0000040));

        file.AddRange(section);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, file.ToArray());
    }

    /// <summary>
    /// A resource tree holding one RT_VERSION entry whose string table carries
    /// <paramref name="company"/>, laid out for a section starting at <paramref name="rva"/>.
    /// </summary>
    private static byte[] VersionResource(string company, int rva)
    {
        var tree = new List<byte>();
        // Three directory levels (type, name, language), each one header plus one entry, then the data entry.
        AddDirectory(tree, 16, 0x80000000 | 24);
        AddDirectory(tree, 1, 0x80000000 | 48);
        AddDirectory(tree, 0x409, 72);
        var info = VersionInfo(company);
        tree.AddRange(new byte[16]);
        WriteInt(tree, 72, rva + 88);
        WriteInt(tree, 76, info.Length);
        tree.AddRange(info);
        return tree.ToArray();

        static void AddDirectory(List<byte> tree, int id, uint offset)
        {
            var at = tree.Count;
            tree.AddRange(new byte[24]);
            WriteShort(tree, at + 14, 1); // one entry, by id
            WriteInt(tree, at + 16, id);
            WriteInt(tree, at + 20, unchecked((int)offset));
        }
    }

    /// <summary>A VS_VERSIONINFO block with a fixed file info and the strings <c>CompanyName</c> and <c>FileVersion</c>.</summary>
    private static byte[] VersionInfo(string company)
    {
        var fixedInfo = new byte[52];
        WriteInt(fixedInfo, 0, unchecked((int)0xFEEF04BD));
        WriteInt(fixedInfo, 4, 0x10000);

        // .NET's FileVersionInfo takes a string table only when it holds a FileVersion, and otherwise moves on
        // to other code pages, emptying CompanyName as it goes.
        var table = Block("040904b0", [], 0, 1, [Text("CompanyName", company), Text("FileVersion", "1, 0, 0, 1")]);
        var strings = Block("StringFileInfo", [], 0, 1, [table]);
        var translation = Block("Translation", [0x09, 0x04, 0xB0, 0x04], 4, 0, []);
        var vars = Block("VarFileInfo", [], 0, 1, [translation]);
        return Block("VS_VERSION_INFO", fixedInfo, fixedInfo.Length, 0, [strings, vars]);

        static byte[] Text(string key, string value) => Block(key, Encoding.Unicode.GetBytes(value + "\0"), value.Length + 1, 1, []);

        // Every version block is length, value length, type, a UTF-16 key, its value and its children,
        // each piece padded to four bytes.
        static byte[] Block(string key, byte[] value, int valueLength, int type, byte[][] children)
        {
            var block = new List<byte>(new byte[6]);
            block.AddRange(Encoding.Unicode.GetBytes(key + "\0"));
            Align(block, 4);
            block.AddRange(value);
            foreach (var child in children)
            {
                Align(block, 4);
                block.AddRange(child);
            }
            WriteShort(block, 0, block.Count);
            WriteShort(block, 2, valueLength);
            WriteShort(block, 4, type);
            return block.ToArray();
        }
    }

    private static int AppendAsciiz(List<byte> section, string text)
    {
        var at = section.Count;
        section.AddRange(Encoding.ASCII.GetBytes(text + "\0"));
        return at;
    }

    private static void Align(List<byte> bytes, int to)
    {
        while (bytes.Count % to != 0)
            bytes.Add(0);
    }

    private static void WriteDir(List<byte> file, int dirs, int index, (int Rva, int Size) dir)
    {
        WriteInt(file, dirs + index * 8, dir.Rva);
        WriteInt(file, dirs + index * 8 + 4, dir.Size);
    }

    private static void WriteShort(IList<byte> bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
    }

    private static void WriteInt(IList<byte> bytes, int at, int value)
    {
        for (var i = 0; i < 4; i++)
            bytes[at + i] = (byte)(value >> (8 * i));
    }

    private static void WriteLong(IList<byte> bytes, int at, ulong value)
    {
        for (var i = 0; i < 8; i++)
            bytes[at + i] = (byte)(value >> (8 * i));
    }
}
