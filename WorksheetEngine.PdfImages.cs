using System.Buffers.Binary;
using System.IO.Compression;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed record PdfImage(int Width, int Height, string ColorSpace, byte[] Data, byte[]? Alpha, bool Jpeg);

    static PdfImage DecodePdfImage(DrawingObject item)
    {
        byte[] data = Convert.FromBase64String(item.Data ?? "");
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return DecodePng(data);
        return DecodeJpeg(data);
    }

    static PdfImage DecodeJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 255 || data[1] != 216) throw new InvalidDataException("JPEG 画像を読み取れません。");
        int position = 2;
        while (position + 3 < data.Length)
        {
            if (data[position++] != 255) throw new InvalidDataException("JPEG 画像を読み取れません。");
            while (position < data.Length && data[position] == 255) position++;
            if (position >= data.Length) break;
            byte marker = data[position++];
            if (marker is 0xd8 or 0xd9 or 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (position + 2 > data.Length) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position, 2));
            if (length < 2 || position + length > data.Length) break;
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (length < 8 || data[position + 2] != 8) break;
                int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 5, 2));
                string color = data[position + 7] switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => "" };
                if (width < 1 || height < 1 || color.Length == 0) break;
                return new(width, height, color, data, null, true);
            }
            position += length;
        }
        throw new InvalidDataException("JPEG 画像のサイズまたは色形式を読み取れません。");
    }

    static PdfImage DecodePng(byte[] data)
    {
        int position = 8, width = 0, height = 0, channels = 0, type = -1, depth = 0, interlace = 0;
        byte[]? palette = null, transparency = null;
        using MemoryStream compressed = new();
        while (position + 12 <= data.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
            if (size > int.MaxValue || (long)position + size + 12 > data.Length) throw new InvalidDataException("PNG 画像が壊れています。");
            string chunk = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            ReadOnlySpan<byte> content = data.AsSpan(position + 8, (int)size);
            if (chunk == "IHDR")
            {
                if (size != 13) throw new InvalidDataException("PNG 画像が壊れています。");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(content));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(content[4..]));
                type = content[9];
                depth = content[8];
                interlace = content[12];
                channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                bool validDepth = type switch
                {
                    0 => depth is 1 or 2 or 4 or 8 or 16,
                    3 => depth is 1 or 2 or 4 or 8,
                    2 or 4 or 6 => depth is 8 or 16,
                    _ => false
                };
                if (width <= 0 || height <= 0 || (long)width * height > 16_000_000 || !validDepth || content[10] != 0 ||
                    content[11] != 0 || interlace > 1)
                    throw new NotSupportedException("この PNG 形式は PDF 出力できません。");
            }
            else if (chunk == "PLTE") palette = content.ToArray();
            else if (chunk == "tRNS") transparency = content.ToArray();
            else if (chunk == "IDAT") compressed.Write(content);
            else if (chunk == "IEND") break;
            position += (int)size + 12;
        }
        if (channels == 0 || compressed.Length == 0 || (type == 3 && (palette is null ||
            palette.Length % 3 != 0))) throw new InvalidDataException("PNG 画像が壊れています。");
        bool gray = type is 0 or 4;
        int count = checked(width * height), colorChannels = gray ? 1 : 3;
        byte[] colors = new byte[checked(count * colorChannels)], alpha = new byte[count];
        bool hasAlpha = type is 4 or 6 || transparency is not null;
        compressed.Position = 0;
        using (ZLibStream decoder = new(compressed, CompressionMode.Decompress, true))
        {
            (int X, int Y, int StepX, int StepY)[] passes = interlace == 0 ? [(0, 0, 1, 1)] : [(0, 0, 8, 8), (4, 0, 8, 8),
                (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            foreach ((int xStart, int yStart, int stepX, int stepY) in passes)
            {
                int passWidth = Math.Max(0, (width - xStart + stepX - 1) / stepX);
                int passHeight = Math.Max(0, (height - yStart + stepY - 1) / stepY);
                if (passWidth == 0 || passHeight == 0) continue;
                int stride = checked((passWidth * channels * depth + 7) / 8), byteWidth = Math.Max(1, (channels * depth + 7) / 8);
                byte[] previous = new byte[stride], current = new byte[stride];
                for (int passRow = 0; passRow < passHeight; passRow++)
                {
                    int filter = decoder.ReadByte();
                    if (filter is < 0 or > 4) throw new InvalidDataException("PNG のフィルターが無効です。");
                    decoder.ReadExactly(current);
                    for (int x = 0; x < stride; x++)
                    {
                        int left = x >= byteWidth ? current[x - byteWidth] : 0;
                        int up = previous[x], upperLeft = x >= byteWidth ? previous[x - byteWidth] : 0;
                        int prediction = filter switch
                        {
                            0 => 0,
                            1 => left,
                            2 => up,
                            3 => (left + up) / 2,
                            _ => Paeth(left,
                            up, upperLeft)
                        };
                        current[x] = unchecked((byte)(current[x] + prediction));
                    }
                    for (int x = 0; x < passWidth; x++)
                    {
                        int destination = (yStart + passRow * stepY) * width + xStart + x * stepX;
                        WritePngPixel(current, x, depth, channels, type, palette, transparency, colors, alpha, destination);
                    }
                    (previous, current) = (current, previous);
                }
            }
        }
        return new(width, height, gray ? "DeviceGray" : "DeviceRGB", CompressPdfImage(colors),
            hasAlpha ? CompressPdfImage(alpha) : null, false);
    }

    static void WritePngPixel(byte[] row, int x, int depth, int channels, int type, byte[]? palette, byte[]? transparency,
        byte[] colors, byte[] alpha, int destination)
    {
        int Sample(int channel)
        {
            int sample = x * channels + channel;
            if (depth == 16) return BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(sample * 2, 2));
            if (depth == 8) return row[sample];
            int bit = sample * depth;
            return (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
        }
        byte Scale(int value) => depth == 16 ? (byte)(value >> 8) : (byte)(value * 255L / ((1 << depth) - 1));
        int first = Sample(0);
        if (type == 3)
        {
            int p = first * 3;
            if (palette is null || p + 2 >= palette.Length) throw new InvalidDataException("PNG のパレットが無効です。");
            colors[destination * 3] = palette[p];
            colors[destination * 3 + 1] = palette[p + 1];
            colors[destination * 3 + 2] = palette[p + 2];
            alpha[destination] = transparency is not null && first < transparency.Length ? transparency[first] : (byte)255;
            return;
        }
        int colorChannels = type is 0 or 4 ? 1 : 3;
        for (int channel = 0; channel < colorChannels; channel++) colors[destination * colorChannels + channel] = Scale(Sample(channel));
        alpha[destination] = type is 4 or 6 ? Scale(Sample(channels - 1)) : (byte)255;
        if (transparency is not null && type == 0 && transparency.Length == 2 &&
            first == BinaryPrimitives.ReadUInt16BigEndian(transparency)) alpha[destination] = 0;
        if (transparency is not null && type == 2 && transparency.Length == 6 &&
            first == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                Sample(1) == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                    Sample(2) == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) alpha[destination] = 0;
    }

    static int Paeth(int left, int up, int upperLeft)
    {
        int prediction = left + up - upperLeft, a = Math.Abs(prediction - left), b = Math.Abs(prediction - up),
            c = Math.Abs(prediction - upperLeft);
        return a <= b && a <= c ? left : b <= c ? up : upperLeft;
    }
    static byte[] CompressPdfImage(byte[] data)
    {
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, true)) compressor.Write(data);
        return output.ToArray();
    }
}
