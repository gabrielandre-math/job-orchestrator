internal static class CaptchaImage
{
    // Dígitos desenhados em matrizes de 5 por 7. O BMP contém somente pixels, sem texto.
    private static readonly string[][] Digits =
    [
        ["01110", "11011", "11011", "11011", "11011", "11011", "01110"],
        ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ["01110", "11011", "00011", "00110", "01100", "11000", "11111"],
        ["11110", "00011", "00011", "01110", "00011", "00011", "11110"],
        ["00011", "00111", "01111", "11011", "11111", "00011", "00011"],
        ["11111", "11000", "11000", "11110", "00011", "11011", "01110"],
        ["01110", "11000", "11000", "11110", "11011", "11011", "01110"],
        ["11111", "00011", "00110", "00110", "01100", "01100", "01100"],
        ["01110", "11011", "11011", "01110", "11011", "11011", "01110"],
        ["01110", "11011", "11011", "01111", "00011", "00011", "01110"]
    ];

    public static byte[] Render(string code)
    {
        const int cellWidth = 34, height = 72, headerSize = 54;
        var width = (code.Length + 1) * cellWidth;
        var stride = (width * 3 + 3) & ~3; // Linhas BMP são alinhadas a 4 bytes.
        using var bmp = new MemoryStream(headerSize + stride * height);
        using var writer = new BinaryWriter(bmp);
        writer.Write((ushort)0x4D42); // BM
        writer.Write(headerSize + stride * height);
        writer.Write(0); // Reservado.
        writer.Write(headerSize);
        writer.Write(40); // BITMAPINFOHEADER.
        writer.Write(width);
        writer.Write(-height); // Linhas de cima para baixo.
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0); // Sem compressão.
        writer.Write(stride * height);
        writer.Write(0); // Resolução horizontal.
        writer.Write(0); // Resolução vertical.
        writer.Write(0); // Cores na paleta.
        writer.Write(0); // Cores importantes.

        var pixels = new byte[stride * height];
        Array.Fill(pixels, (byte)255);
        // Ruído claro e posição variável permitem praticar limiarização e segmentação.
        for (var i = 0; i < width * height / 14; i++)
        {
            var offset = Random.Shared.Next(height) * stride + Random.Shared.Next(width) * 3;
            Array.Fill(pixels, (byte)190, offset, 3);
        }

        for (var i = 0; i < code.Length; i++)
        {
            var left = 18 + i * cellWidth + Random.Shared.Next(-2, 3);
            var top = 15 + Random.Shared.Next(-5, 6);
            var digit = Digits[code[i] - '0'];
            for (var y = 0; y < digit.Length; y++)
            for (var x = 0; x < digit[y].Length; x++)
            {
                if (digit[y][x] != '1') continue;
                for (var row = 0; row < 6; row++)
                    Array.Clear(pixels, (top + y * 6 + row) * stride + (left + x * 5) * 3, 5 * 3);
            }
        }

        writer.Write(pixels);
        return bmp.ToArray();
    }
}
