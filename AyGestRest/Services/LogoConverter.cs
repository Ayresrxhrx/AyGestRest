using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace AyGestRest.Helpers
{
    public static class LogoConverter
    {
        public static byte[] ConvertToEscPos(string imagePath, int maxWidth = 384)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return null;

                using (var originalImage = Image.FromFile(imagePath))
                {
                    // Converter para escala de cinza
                    var grayImage = ConvertToGrayscale(originalImage);

                    // Redimensionar para largura máxima da impressora (384 pixels para 58mm)
                    int targetWidth = Math.Min(maxWidth, grayImage.Width);
                    int targetHeight = (int)(grayImage.Height * ((float)targetWidth / grayImage.Width));

                    using (var resizedImage = new Bitmap(targetWidth, targetHeight))
                    using (var g = Graphics.FromImage(resizedImage))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(grayImage, 0, 0, targetWidth, targetHeight);

                        // Converter para bitmap monocromático (1-bit) ESC/POS
                        return ConvertToEscPosBitmap(resizedImage);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao converter logo: {ex.Message}");
                return null;
            }
        }

        private static Bitmap ConvertToGrayscale(Image original)
        {
            var grayImage = new Bitmap(original.Width, original.Height);
            using (var g = Graphics.FromImage(grayImage))
            {
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                    new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                    new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                            0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return grayImage;
        }

        private static byte[] ConvertToEscPosBitmap(Bitmap image)
        {
            // ESC/POS GS v 0 para imagem raster
            var bytes = new System.Collections.Generic.List<byte>();

            int width = image.Width;
            int height = image.Height;

            // Calcular largura em bytes (8 pixels por byte)
            int widthBytes = (width + 7) / 8;

            // Comando ESC * para modo de imagem raster
            bytes.Add(0x1B); // ESC
            bytes.Add(0x2A); // *
            bytes.Add(33);   // m=33 (24-dot double density)
            bytes.Add((byte)(widthBytes % 256));   // xL
            bytes.Add((byte)(widthBytes / 256));   // xH
            bytes.Add((byte)(height % 256));       // yL
            bytes.Add((byte)(height / 256));       // yH

            // Dados da imagem
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < widthBytes; x++)
                {
                    byte byteValue = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int pixelX = x * 8 + bit;
                        if (pixelX < width && y < height)
                        {
                            var pixel = image.GetPixel(pixelX, y);
                            // Se o pixel for escuro (preto), seta o bit
                            if (pixel.GetBrightness() < 0.5)
                            {
                                byteValue |= (byte)(0x80 >> bit);
                            }
                        }
                    }
                    bytes.Add(byteValue);
                }
            }

            return bytes.ToArray();
        }

        public static string CreateEscPosLogoCommands(string logoPath)
        {
            try
            {
                if (!File.Exists(logoPath))
                    return string.Empty;

                var escPosBytes = ConvertToEscPos(logoPath, 384); // 384 pixels = 58mm
                if (escPosBytes == null || escPosBytes.Length == 0)
                    return string.Empty;

                // Converter bytes para string hexadecimal
                var sb = new StringBuilder();
                foreach (byte b in escPosBytes)
                {
                    sb.Append($"\\x{b:X2}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao criar comandos logo: {ex.Message}");
                return string.Empty;
            }
        }

        public static string CreateSimpleLogoCommands(string logoPath)
        {
            try
            {
                if (!File.Exists(logoPath))
                    return string.Empty;

                var escPosBytes = ConvertToEscPos(logoPath, 384);
                if (escPosBytes == null || escPosBytes.Length == 0)
                    return string.Empty;

                // Usar codificação mais simples
                return Encoding.Default.GetString(escPosBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}