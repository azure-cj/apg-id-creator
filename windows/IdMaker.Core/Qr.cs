// QR code generation.
//
// Byte mode only, any version, any of the four error-correction levels,
// with the mask chosen by the specification's penalty rules. It follows
// qrcode-generator (Kazuhiko Arase, MIT) step for step, including how the
// penalty is scored while the format bits are still blank, so the square
// printed on the card is module for module the one the web app printed.
// The tests hold it to matrices taken from that library.

using System.Text;

namespace IdMaker.Core;

/// <summary>A finished QR symbol: Size x Size modules, true where dark.</summary>
public sealed class QrCode
{
    private readonly bool[] _dark;

    public int Size { get; }
    public bool IsDark(int row, int col) => _dark[row * Size + col];

    private QrCode(int size, bool[] dark) { Size = size; _dark = dark; }

    /// <summary>Encode text. errorCorrection is one of L, M, Q, H.</summary>
    public static QrCode Encode(string text, char errorCorrection)
    {
        var level = errorCorrection switch
        {
            'L' => 0, 'M' => 1, 'Q' => 2, 'H' => 3,
            _ => throw new ArgumentException($"Unknown error-correction level '{errorCorrection}'.", nameof(errorCorrection)),
        };
        var bytes = Encoding.UTF8.GetBytes(text);

        var version = 0;
        for (var v = 1; v <= 40; v++)
        {
            if (HeaderBits(v) + bytes.Length * 8 <= DataCodewords(v, level) * 8) { version = v; break; }
        }
        if (version == 0) throw new ArgumentException("This text is too long for a QR code.", nameof(text));

        var codewords = Codewords(bytes, version, level);
        var size = version * 4 + 17;

        var best = 0;
        var bestPenalty = double.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            var penalty = Penalty(Build(size, version, level, mask, codewords, test: true), size);
            if (mask == 0 || penalty < bestPenalty) { bestPenalty = penalty; best = mask; }
        }

        var final = Build(size, version, level, best, codewords, test: false);
        var dark = new bool[size * size];
        for (var i = 0; i < dark.Length; i++) dark[i] = final[i] == 1;
        return new QrCode(size, dark);
    }

    /* ---- data ---------------------------------------------------- */

    private static int HeaderBits(int version) => 4 + (version < 10 ? 8 : 16);

    private static IEnumerable<(int Total, int Data)> Blocks(int version, int level)
    {
        var row = QrTables.RsBlocks[(version - 1) * 4 + level];
        for (var i = 0; i < row.Length; i += 3)
            for (var n = 0; n < row[i]; n++)
                yield return (row[i + 1], row[i + 2]);
    }

    private static int DataCodewords(int version, int level) => Blocks(version, level).Sum(b => b.Data);

    private static byte[] Codewords(byte[] bytes, int version, int level)
    {
        var blocks = Blocks(version, level).ToArray();
        var capacity = blocks.Sum(b => b.Data);

        var bits = new List<bool>(capacity * 8);
        void Put(int value, int length) { for (var i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) == 1); }

        Put(4, 4);                                       // byte mode
        Put(bytes.Length, version < 10 ? 8 : 16);
        foreach (var b in bytes) Put(b, 8);
        if (bits.Count + 4 <= capacity * 8) Put(0, 4);   // terminator
        while (bits.Count % 8 != 0) bits.Add(false);

        var data = new byte[capacity];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) data[i / 8] |= (byte)(0x80 >> (i % 8));
        for (int i = bits.Count / 8, pad = 0; i < capacity; i++, pad++) data[i] = (byte)(pad % 2 == 0 ? 0xEC : 0x11);

        var dc = new byte[blocks.Length][];
        var ec = new byte[blocks.Length][];
        var offset = 0;
        for (var r = 0; r < blocks.Length; r++)
        {
            dc[r] = data.AsSpan(offset, blocks[r].Data).ToArray();
            offset += blocks[r].Data;
            ec[r] = ReedSolomon(dc[r], blocks[r].Total - blocks[r].Data);
        }

        // interleaved: first codeword of every block, then the second, …
        var result = new List<byte>(blocks.Sum(b => b.Total));
        for (var i = 0; i < dc.Max(d => d.Length); i++)
            foreach (var d in dc) if (i < d.Length) result.Add(d[i]);
        for (var i = 0; i < ec.Max(e => e.Length); i++)
            foreach (var e in ec) if (i < e.Length) result.Add(e[i]);
        return result.ToArray();
    }

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static QrCode()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    /// <summary>The remainder of data · x^count divided by the generator polynomial.</summary>
    private static byte[] ReedSolomon(byte[] data, int count)
    {
        var gen = new byte[] { 1 };
        for (var i = 0; i < count; i++)
        {
            var next = new byte[gen.Length + 1];
            for (var j = 0; j < gen.Length; j++)
            {
                next[j] ^= gen[j];
                next[j + 1] ^= Mul(gen[j], Exp[i]);
            }
            gen = next;
        }

        var rem = new byte[count];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, count - 1);
            rem[count - 1] = 0;
            for (var j = 0; j < count; j++) rem[j] ^= Mul(gen[j + 1], factor);
        }
        return rem;
    }

    /* ---- the symbol ---------------------------------------------- */

    // A module is -1 until something claims it, then 0 (light) or 1 (dark).
    private static sbyte[] Build(int size, int version, int level, int mask, byte[] codewords, bool test)
    {
        var m = new sbyte[size * size];
        Array.Fill(m, (sbyte)-1);

        Finder(m, size, 0, 0);
        Finder(m, size, size - 7, 0);
        Finder(m, size, 0, size - 7);
        Alignment(m, size, version);
        for (var i = 8; i < size - 8; i++)
        {
            if (m[i * size + 6] < 0) m[i * size + 6] = (sbyte)(i % 2 == 0 ? 1 : 0);
            if (m[6 * size + i] < 0) m[6 * size + i] = (sbyte)(i % 2 == 0 ? 1 : 0);
        }
        FormatInfo(m, size, level, mask, test);
        if (version >= 7) VersionInfo(m, size, version, test);
        Place(m, size, codewords, mask);
        return m;
    }

    private static void Finder(sbyte[] m, int size, int row, int col)
    {
        for (var r = -1; r <= 7; r++)
        {
            if (row + r <= -1 || size <= row + r) continue;
            for (var c = -1; c <= 7; c++)
            {
                if (col + c <= -1 || size <= col + c) continue;
                var dark = (0 <= r && r <= 6 && (c == 0 || c == 6))
                        || (0 <= c && c <= 6 && (r == 0 || r == 6))
                        || (2 <= r && r <= 4 && 2 <= c && c <= 4);
                m[(row + r) * size + col + c] = (sbyte)(dark ? 1 : 0);
            }
        }
    }

    private static void Alignment(sbyte[] m, int size, int version)
    {
        var pos = QrTables.AlignmentPositions[version - 1];
        foreach (var row in pos)
        {
            foreach (var col in pos)
            {
                if (m[row * size + col] >= 0) continue;      // a finder pattern is already there
                for (var r = -2; r <= 2; r++)
                    for (var c = -2; c <= 2; c++)
                        m[(row + r) * size + col + c] = (sbyte)(r == -2 || r == 2 || c == -2 || c == 2 || (r == 0 && c == 0) ? 1 : 0);
            }
        }
    }

    private static int BchDigit(int data)
    {
        var digit = 0;
        while (data != 0) { digit++; data >>= 1; }
        return digit;
    }

    private const int G15 = 0x537, G18 = 0x1F25, G15Mask = 0x5412;

    private static void FormatInfo(sbyte[] m, int size, int level, int mask, bool test)
    {
        // the specification numbers the levels L=1, M=0, Q=3, H=2
        var levelBits = level switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
        var data = (levelBits << 3) | mask;
        var d = data << 10;
        while (BchDigit(d) - BchDigit(G15) >= 0) d ^= G15 << (BchDigit(d) - BchDigit(G15));
        var bits = ((data << 10) | d) ^ G15Mask;

        for (var i = 0; i < 15; i++)
        {
            var mod = (sbyte)(!test && ((bits >> i) & 1) == 1 ? 1 : 0);
            if (i < 6) m[i * size + 8] = mod;
            else if (i < 8) m[(i + 1) * size + 8] = mod;
            else m[(size - 15 + i) * size + 8] = mod;

            if (i < 8) m[8 * size + size - i - 1] = mod;
            else if (i < 9) m[8 * size + 15 - i] = mod;
            else m[8 * size + 15 - i - 1] = mod;
        }
        m[(size - 8) * size + 8] = (sbyte)(test ? 0 : 1);
    }

    private static void VersionInfo(sbyte[] m, int size, int version, bool test)
    {
        var d = version << 12;
        while (BchDigit(d) - BchDigit(G18) >= 0) d ^= G18 << (BchDigit(d) - BchDigit(G18));
        var bits = (version << 12) | d;
        for (var i = 0; i < 18; i++)
        {
            var mod = (sbyte)(!test && ((bits >> i) & 1) == 1 ? 1 : 0);
            m[(i / 3) * size + i % 3 + size - 8 - 3] = mod;
            m[(i % 3 + size - 8 - 3) * size + i / 3] = mod;
        }
    }

    private static bool Masked(int mask, int i, int j) => mask switch
    {
        0 => (i + j) % 2 == 0,
        1 => i % 2 == 0,
        2 => j % 3 == 0,
        3 => (i + j) % 3 == 0,
        4 => (i / 2 + j / 3) % 2 == 0,
        5 => (i * j) % 2 + (i * j) % 3 == 0,
        6 => ((i * j) % 2 + (i * j) % 3) % 2 == 0,
        _ => ((i * j) % 3 + (i + j) % 2) % 2 == 0,
    };

    private static void Place(sbyte[] m, int size, byte[] data, int mask)
    {
        var inc = -1;
        var row = size - 1;
        var bitIndex = 7;
        var byteIndex = 0;

        for (var col = size - 1; col > 0; col -= 2)
        {
            if (col == 6) col--;                         // the vertical timing pattern
            while (true)
            {
                for (var c = 0; c < 2; c++)
                {
                    if (m[row * size + col - c] >= 0) continue;
                    var dark = byteIndex < data.Length && ((data[byteIndex] >> bitIndex) & 1) == 1;
                    if (Masked(mask, row, col - c)) dark = !dark;
                    m[row * size + col - c] = (sbyte)(dark ? 1 : 0);
                    if (--bitIndex == -1) { byteIndex++; bitIndex = 7; }
                }
                row += inc;
                if (row < 0 || size <= row) { row -= inc; inc = -inc; break; }
            }
        }
    }

    private static double Penalty(sbyte[] m, int size)
    {
        bool Dark(int r, int c) => m[r * size + c] == 1;
        double lost = 0;

        // runs: a module with more than five like-coloured neighbours
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var same = 0;
                var dark = Dark(row, col);
                for (var r = -1; r <= 1; r++)
                {
                    if (row + r < 0 || size <= row + r) continue;
                    for (var c = -1; c <= 1; c++)
                    {
                        if (col + c < 0 || size <= col + c) continue;
                        if (r == 0 && c == 0) continue;
                        if (dark == Dark(row + r, col + c)) same++;
                    }
                }
                if (same > 5) lost += 3 + same - 5;
            }
        }

        // 2x2 blocks of one colour
        for (var row = 0; row < size - 1; row++)
        {
            for (var col = 0; col < size - 1; col++)
            {
                var count = 0;
                if (Dark(row, col)) count++;
                if (Dark(row + 1, col)) count++;
                if (Dark(row, col + 1)) count++;
                if (Dark(row + 1, col + 1)) count++;
                if (count == 0 || count == 4) lost += 3;
            }
        }

        // anything that looks like a finder pattern: 1011101
        for (var row = 0; row < size; row++)
            for (var col = 0; col < size - 6; col++)
                if (Dark(row, col) && !Dark(row, col + 1) && Dark(row, col + 2) && Dark(row, col + 3)
                    && Dark(row, col + 4) && !Dark(row, col + 5) && Dark(row, col + 6)) lost += 40;
        for (var col = 0; col < size; col++)
            for (var row = 0; row < size - 6; row++)
                if (Dark(row, col) && !Dark(row + 1, col) && Dark(row + 2, col) && Dark(row + 3, col)
                    && Dark(row + 4, col) && !Dark(row + 5, col) && Dark(row + 6, col)) lost += 40;

        // balance of dark and light
        var darkCount = 0;
        for (var i = 0; i < m.Length; i++) if (m[i] == 1) darkCount++;
        lost += Math.Abs(100.0 * darkCount / size / size - 50) / 5 * 10;
        return lost;
    }

    /// <summary>The symbol as rows of '1' and '0', for tests and diagnosis.</summary>
    public string[] ToRows()
    {
        var rows = new string[Size];
        for (var r = 0; r < Size; r++)
        {
            var sb = new StringBuilder(Size);
            for (var c = 0; c < Size; c++) sb.Append(IsDark(r, c) ? '1' : '0');
            rows[r] = sb.ToString();
        }
        return rows;
    }
}
