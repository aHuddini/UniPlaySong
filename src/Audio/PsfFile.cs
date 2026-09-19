using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace UniPlaySong.Audio
{
    // A PlayStation Sound Format file (Corlett's spec v1.4), read into what the engine needs: the
    // decompressed PS-EXE sections in load order, the tag block, and the refresh rate.
    //
    // A .minipsf carries only the song data and names the driver it needs in a _lib tag; that
    // .psflib sits beside it and is never a track in its own right - it is not in
    // Constants.SupportedAudioExtensions, which is all it takes to keep it out of every song list.
    public sealed class PsfFile
    {
        public static readonly string[] SupportedExtensions = { ".psf", ".minipsf" };

        // Bytes the spec treats as whitespace around tag names and values.
        private static readonly char[] TagWhitespace = BuildTagWhitespace();

        private const int MaxLibraryDepth = 10;

        // Spec: "Maximum uncompressed executable size is 2,033,664 bytes" plus the 2048-byte header.
        private const int MaxExecutableBytes = 2033664 + 2048;

        // Decompressed PS-EXEs in the order the engine must load them. The first one supplies the
        // initial PC and SP; the rest are written over it.
        public IReadOnlyList<byte[]> Sections { get; private set; }

        // Every tag in the file, case-insensitive. Multi-line values are joined with '\n'.
        public IReadOnlyDictionary<string, string> Tags { get; private set; }

        public string Title => Tag("title");
        public string Artist => Tag("artist");
        public string Game => Tag("game");
        public TimeSpan? Length { get; private set; }
        public TimeSpan? Fade { get; private set; }

        // Length plus fade, or null when the rip carries no length.
        public TimeSpan? Duration => Length.HasValue ? Length.Value + (Fade ?? TimeSpan.Zero) : (TimeSpan?)null;

        // 50 or 60. From the _refresh tag, else the primary EXE's region marker.
        public int RefreshHz { get; private set; }

        // Where this file's own program landed in Sections (a library loads ahead of it).
        private int _ownSection;

        private PsfFile() { }

        public static bool IsPsfExtension(string extension)
        {
            foreach (var ext in SupportedExtensions)
            {
                if (string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Everything: inflates the program and resolves the library chain. Throws on a malformed
        // file or a missing library, with the offending path in the message.
        public static PsfFile Load(string path)
        {
            var sections = new List<byte[]>();
            var primary = LoadInto(sections, path, 0);

            int refresh = ParseRefresh(primary.Tags);
            if (refresh == 0) refresh = RegionRefresh(sections[primary._ownSection]);

            primary.Sections = sections;
            primary.RefreshHz = refresh;
            return primary;
        }

        // Tags only, for metadata and statistics. No decompression, no library resolution.
        public static PsfFile ReadTags(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var psf = ParseTags(bytes, path);
            psf.Sections = new byte[0][];
            return psf;
        }

        // Spec load order: the _lib file first (its PC/SP win), then this file's program on top,
        // then _lib2, _lib3... each resolved recursively against the file's own folder.
        private static PsfFile LoadInto(List<byte[]> sections, string path, int depth)
        {
            if (depth > MaxLibraryDepth)
                throw new InvalidDataException($"PSF library chain is deeper than {MaxLibraryDepth}: {path}");

            var bytes = File.ReadAllBytes(path);
            var psf = ParseTags(bytes, path);
            var folder = Path.GetDirectoryName(path) ?? string.Empty;

            string lib = psf.Tag("_lib");
            if (!string.IsNullOrWhiteSpace(lib))
                LoadInto(sections, ResolveLibrary(folder, lib, path), depth + 1);

            psf._ownSection = sections.Count;
            sections.Add(Inflate(bytes, path));

            for (int n = 2; ; n++)
            {
                string libN = psf.Tag("_lib" + n.ToString(CultureInfo.InvariantCulture));
                if (string.IsNullOrWhiteSpace(libN)) break;
                LoadInto(sections, ResolveLibrary(folder, libN, path), depth + 1);
            }

            return psf;
        }

        private static string ResolveLibrary(string folder, string name, string referrer)
        {
            var libPath = Path.Combine(folder, name.Trim());
            if (!File.Exists(libPath))
                throw new FileNotFoundException($"PSF library '{name}' referenced by {Path.GetFileName(referrer)} is missing", libPath);
            return libPath;
        }

        // ---- container ------------------------------------------------------------------------------

        // Header: "PSF" 0x01, reserved size, compressed program size, CRC-32 of the compressed
        // program, reserved area, zlib program, optional "[TAG]" block.
        private static void Locate(byte[] bytes, string path, out int programAt, out int programSize, out int tagAt)
        {
            if (bytes.Length < 16 || bytes[0] != (byte)'P' || bytes[1] != (byte)'S' || bytes[2] != (byte)'F')
                throw new InvalidDataException($"Not a PSF file: {path}");
            if (bytes[3] != 0x01)
                throw new InvalidDataException($"Not a PlayStation (PSF1) file, version byte is 0x{bytes[3]:x2}: {path}");

            int reserved = (int)BitConverter.ToUInt32(bytes, 4);
            programSize = (int)BitConverter.ToUInt32(bytes, 8);
            uint crc = BitConverter.ToUInt32(bytes, 12);
            programAt = 16 + reserved;

            if (reserved < 0 || programSize < 0 || (long)programAt + programSize > bytes.Length)
                throw new InvalidDataException($"PSF is truncated: {path}");
            if (Crc32(bytes, programAt, programSize) != crc)
                throw new InvalidDataException($"PSF program CRC does not match, file is corrupt: {path}");

            tagAt = programAt + programSize;
        }

        private static byte[] Inflate(byte[] bytes, string path)
        {
            Locate(bytes, path, out int at, out int size, out _);
            if (size < 2)
                throw new InvalidDataException($"PSF program is empty: {path}");

            // zlib stream: 2-byte header, raw deflate, Adler-32 trailer. DeflateStream reads the
            // deflate block and stops, so the trailer never matters.
            using (var source = new MemoryStream(bytes, at + 2, size - 2, writable: false))
            using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
            using (var target = new MemoryStream())
            {
                var chunk = new byte[65536];
                int read;
                while ((read = inflate.Read(chunk, 0, chunk.Length)) > 0)
                {
                    target.Write(chunk, 0, read);
                    if (target.Length > MaxExecutableBytes)
                        throw new InvalidDataException($"PSF program is larger than a PlayStation's memory: {path}");
                }

                var exe = target.ToArray();
                if (exe.Length < 2048 || Encoding.ASCII.GetString(exe, 0, 8) != "PS-X EXE")
                    throw new InvalidDataException($"PSF program is not a PS-X EXE: {path}");
                return exe;
            }
        }

        // ---- tags -------------------------------------------------------------------------------

        private static PsfFile ParseTags(byte[] bytes, string path)
        {
            Locate(bytes, path, out _, out _, out int tagAt);

            var psf = new PsfFile();
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            psf.Tags = tags;

            const string marker = "[TAG]";
            if (tagAt + marker.Length <= bytes.Length &&
                Encoding.ASCII.GetString(bytes, tagAt, marker.Length) == marker)
            {
                int blockAt = tagAt + marker.Length;
                int blockLength = bytes.Length - blockAt;

                // The encoding is announced inside the block itself, so read it once as Latin-1 to
                // find the utf8 tag, then again in the encoding it names.
                var probe = ParseTagBlock(Encoding.GetEncoding(28591).GetString(bytes, blockAt, blockLength));
                var encoding = probe.ContainsKey("utf8") ? Encoding.UTF8 : Encoding.Default;
                foreach (var kv in ParseTagBlock(encoding.GetString(bytes, blockAt, blockLength)))
                    tags[kv.Key] = kv.Value;
            }

            psf.Length = ParseTime(psf.Tag("length"));
            psf.Fade = ParseTime(psf.Tag("fade"));
            return psf;
        }

        // "name=value" per line, names case-insensitive, whitespace (0x01-0x20) trimmed from both
        // sides, a repeated name continuing the previous value on a new line.
        internal static Dictionary<string, string> ParseTagBlock(string block)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in block.Split('\n'))
            {
                int eq = rawLine.IndexOf('=');
                if (eq < 0) continue;

                string name = rawLine.Substring(0, eq).Trim(TagWhitespace);
                string value = rawLine.Substring(eq + 1).Trim(TagWhitespace);
                if (name.Length == 0) continue;

                tags[name] = tags.TryGetValue(name, out var previous) ? previous + "\n" + value : value;
            }
            return tags;
        }

        // s.ddd, m:ss.ddd or h:mm:ss.ddd, comma or dot for the fraction.
        internal static TimeSpan? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            double total = 0;
            foreach (var part in text.Trim().Split(':'))
            {
                if (!double.TryParse(part.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    return null;
                total = total * 60 + value;
            }
            return total >= 0 ? TimeSpan.FromSeconds(total) : (TimeSpan?)null;
        }

        private static int ParseRefresh(IReadOnlyDictionary<string, string> tags)
        {
            if (tags.TryGetValue("_refresh", out var text) &&
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hz) &&
                (hz == 50 || hz == 60))
            {
                return hz;
            }
            return 0;
        }

        // The PS-EXE header carries a region marker at 0x4c ("Sony Computer Entertainment Inc. for
        // Europe area"). Spec: only the primary program's marker counts, never a library's.
        private static int RegionRefresh(byte[] primaryExe)
        {
            int length = Math.Min(80, primaryExe.Length - 0x4c);
            string marker = length > 0 ? Encoding.ASCII.GetString(primaryExe, 0x4c, length) : string.Empty;
            return marker.IndexOf("Europe", StringComparison.OrdinalIgnoreCase) >= 0 ? 50 : 60;
        }

        private string Tag(string name)
        {
            return Tags != null && Tags.TryGetValue(name, out var value) ? value : null;
        }

        private static char[] BuildTagWhitespace()
        {
            var chars = new char[0x20];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(i + 1);
            return chars;
        }

        // ---- CRC-32 (IEEE 802.3, as zlib computes it) -------------------------------------------------

        private static readonly uint[] CrcTable = BuildCrcTable();

        internal static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xffffffff;
            for (int i = offset; i < offset + count; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xff] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
