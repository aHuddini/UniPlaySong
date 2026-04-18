using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace UniPlaySong.Audio
{
    // Parses a VGM / VGZ file's header to detect which sound chips it references.
    // GME only emulates a small subset of the chips that VGM files can contain:
    // SN76489 (PSG), YM2413 (OPLL), YM2612 (Genesis FM). Files referencing other
    // chips (YM2610 Neo Geo, YM2151 arcade, YM2608 PC-88, QSound CPS-2, etc.)
    // will decode through GME without errors but produce silence.
    //
    // This sniffer detects unsupported-chip files at load time so the plugin can
    // skip them with a clear error instead of pretending to play silently.
    //
    // VGM spec reference: https://vgmrips.net/wiki/VGM_Specification
    public static class VgmHeaderSniffer
    {
        // Magic bytes
        private static readonly byte[] VgmMagic = { 0x56, 0x67, 0x6D, 0x20 }; // "Vgm "
        private const byte GzipMagic0 = 0x1F;
        private const byte GzipMagic1 = 0x8B;

        // How many header bytes we need to examine. VGM 1.71's last chip-clock field
        // (Irem GA20) is at offset 0xE0. Reading 0x100 (256) bytes is comfortably
        // enough for every defined VGM version.
        private const int HeaderBytesNeeded = 256;

        public sealed class VgmSniffResult
        {
            public bool IsValidVgm { get; set; }
            public uint Version { get; set; }       // BCD-encoded, e.g. 0x00000171 = version 1.71
            public string UnsupportedChip { get; set; }  // null if all chips are GME-compatible
        }

        // Reads the VGM header, decompressing if the file is gzipped (.vgz).
        // Returns a result with UnsupportedChip set if the file uses any chip GME can't emulate.
        // Returns IsValidVgm=false if the file doesn't look like VGM at all — caller should
        // proceed and let GME handle the error (which may succeed for non-VGM formats this
        // class wasn't asked about).
        public static VgmSniffResult Inspect(string filePath)
        {
            var result = new VgmSniffResult();

            try
            {
                byte[] header = ReadVgmHeaderBytes(filePath, HeaderBytesNeeded);
                if (header == null || header.Length < 0x40)
                    return result; // not VGM / file too short — let GME try

                // Check "Vgm " magic at offset 0
                if (header[0] != VgmMagic[0] || header[1] != VgmMagic[1]
                    || header[2] != VgmMagic[2] || header[3] != VgmMagic[3])
                    return result; // not a VGM — let GME handle by format

                result.IsValidVgm = true;
                result.Version = ReadUInt32LE(header, 0x08);

                // Walk the chip-clock offsets. Any non-zero value (masked to clear the
                // high dual-chip / sub-type flags) means the file uses that chip.
                // We enumerate ONLY the unsupported ones — the supported trio
                // (SN76489 / YM2413 / YM2612) doesn't need to be checked, because
                // if they're used we're fine.
                string first = FindFirstUnsupportedChip(header);
                result.UnsupportedChip = first;
            }
            catch
            {
                // Any I/O or decompression failure — treat as "not known VGM", let GME try.
                // GME will produce its own error message if it can't handle the file either.
            }

            return result;
        }

        // Reads up to `count` bytes from the start of the file.
        // Handles gzip-compressed `.vgz` transparently by detecting the gzip magic
        // and streaming through a GZipStream; reads raw bytes otherwise.
        private static byte[] ReadVgmHeaderBytes(string filePath, int count)
        {
            byte[] buffer = new byte[count];
            int totalRead;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int first = fs.ReadByte();
                int second = fs.ReadByte();
                if (first < 0 || second < 0)
                    return null;

                fs.Position = 0;

                if (first == GzipMagic0 && second == GzipMagic1)
                {
                    // Gzipped (.vgz). GZipStream handles it transparently.
                    using (var gz = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false))
                    {
                        totalRead = ReadFully(gz, buffer, count);
                    }
                }
                else
                {
                    totalRead = ReadFully(fs, buffer, count);
                }
            }

            if (totalRead <= 0) return null;
            if (totalRead < count)
            {
                byte[] truncated = new byte[totalRead];
                Array.Copy(buffer, truncated, totalRead);
                return truncated;
            }
            return buffer;
        }

        private static int ReadFully(Stream s, byte[] buf, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buf, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        private static uint ReadUInt32LE(byte[] buf, int offset)
        {
            if (offset + 4 > buf.Length) return 0;
            return (uint)(buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24));
        }

        // Returns the friendly name of the first unsupported chip found in the header,
        // or null if the only chip clocks set are GME-compatible (SN76489 / YM2413 / YM2612).
        // Each entry: (offset, friendly name, introduced in VGM version).
        // We only flag chips whose offset is within the bytes we actually read AND within
        // the range the file's declared version supports (so older VGM files don't trip
        // on uninitialized bytes past their header).
        private static string FindFirstUnsupportedChip(byte[] header)
        {
            uint version = ReadUInt32LE(header, 0x08);

            // Each item: (byte-offset, min VGM version required, friendly name)
            // Friendly names match what users see in VGMRips pack listings so the
            // error message is actionable.
            var unsupported = new (int offset, uint minVersion, string name)[]
            {
                (0x30, 0x00000110, "YM2151 (arcade — Capcom CPS-1, early Konami/SEGA)"),
                (0x38, 0x00000151, "SegaPCM (Sega arcade)"),
                (0x40, 0x00000151, "RF5C68 (Sega System 18)"),
                (0x44, 0x00000151, "YM2203"),
                (0x48, 0x00000151, "YM2608 (PC-88/PC-98)"),
                (0x4C, 0x00000151, "YM2610/YM2610B (Neo Geo)"),
                (0x50, 0x00000151, "YM3812 (Sound Blaster Pro)"),
                (0x54, 0x00000151, "YM3526"),
                (0x58, 0x00000151, "Y8950 (MSX Music Module)"),
                (0x5C, 0x00000151, "YMF262 (Sound Blaster 16 / OPL3)"),
                (0x60, 0x00000151, "YMF278B"),
                (0x64, 0x00000151, "YMF271"),
                (0x68, 0x00000151, "YMZ280B"),
                (0x6C, 0x00000151, "RF5C164 (Mega-CD)"),
                (0x70, 0x00000151, "PWM (Sega 32X)"),
                (0x74, 0x00000151, "AY8910 (VGM variant — GME supports AY only in .ay files)"),
                (0x80, 0x00000161, "Game Boy DMG (VGM variant — GME supports only in .gbs files)"),
                (0x84, 0x00000161, "NES APU (VGM variant — GME supports only in .nsf files)"),
                (0x88, 0x00000161, "MultiPCM"),
                (0x8C, 0x00000161, "uPD7759"),
                (0x90, 0x00000161, "OKIM6258"),
                (0x98, 0x00000161, "OKIM6295"),
                (0x9C, 0x00000161, "K051649 (Konami arcade)"),
                (0xA0, 0x00000161, "K054539 (Konami arcade)"),
                (0xA4, 0x00000161, "HuC6280 (VGM variant — GME supports only in .hes files)"),
                (0xA8, 0x00000161, "C140 (Namco arcade)"),
                (0xAC, 0x00000161, "K053260 (Konami arcade)"),
                (0xB0, 0x00000161, "POKEY (VGM variant — GME supports only in .sap files)"),
                (0xB4, 0x00000161, "QSound (Capcom CPS-2/CPS-3)"),
                (0xB8, 0x00000171, "SCSP (Sega Saturn)"),
                (0xC0, 0x00000171, "WonderSwan"),
                (0xC4, 0x00000171, "VSU (Virtual Boy)"),
                (0xC8, 0x00000171, "SAA1099"),
                (0xCC, 0x00000171, "ES5503"),
                (0xD0, 0x00000171, "ES5505/ES5506"),
                (0xD8, 0x00000171, "X1-010"),
                (0xDC, 0x00000171, "C352 (Namco arcade)"),
                (0xE0, 0x00000171, "GA20 (Irem arcade)"),
            };

            foreach (var chip in unsupported)
            {
                if (chip.offset + 4 > header.Length) continue;
                if (version < chip.minVersion) continue; // field not defined in this VGM version

                uint rawClock = ReadUInt32LE(header, chip.offset);
                // Top 2 bits are "dual chip" and sub-type flags — strip to get the real clock.
                uint clock = rawClock & 0x3FFFFFFF;
                if (clock != 0)
                {
                    return chip.name;
                }
            }

            return null;
        }
    }
}
