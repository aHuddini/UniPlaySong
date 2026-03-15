using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using SkiaSharp;

namespace UniPlaySong.IconGlow
{
    // Multi-layer neon glow renderer using SkiaSharp.
    // Draws 3 additive layers at different blur levels for authentic neon:
    //   Layer 1: Wide soft outer glow (large sigma, moderate alpha)
    //   Layer 2: Medium glow (medium sigma, higher alpha)
    //   Layer 3: Tight bright inner glow (small sigma, near-full alpha)
    // Each layer is drawn twice (color1 offset + color2 offset) for gradient effect.
    public static class GlowRenderer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static BitmapSource RenderGlow(ImageSource iconSource, Color color1, Color color2,
            double blurSigma, double displayWidth, double displayHeight, double intensity = 1.8)
        {
            try
            {
                var bitmapSource = iconSource as BitmapSource;
                if (bitmapSource == null) return null;

                int targetW = (int)Math.Ceiling(displayWidth);
                int targetH = (int)Math.Ceiling(displayHeight);
                if (targetW <= 0 || targetH <= 0) return null;

                if (bitmapSource.Format != PixelFormats.Bgra32 && bitmapSource.Format != PixelFormats.Pbgra32)
                    bitmapSource = new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0);

                int srcWidth = bitmapSource.PixelWidth;
                int srcHeight = bitmapSource.PixelHeight;
                int srcStride = srcWidth * 4;
                byte[] srcPixels = new byte[srcHeight * srcStride];
                bitmapSource.CopyPixels(srcPixels, srcStride, 0);

                // Use larger extend for the wide outer layer
                double outerSigma = blurSigma * 2.0;
                int extend = (int)Math.Ceiling(outerSigma * 2.5);
                int outWidth = targetW + extend * 2;
                int outHeight = targetH + extend * 2;

                float shift = (float)(blurSigma * 0.4);

                using (var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight, SKColorType.Bgra8888, SKAlphaType.Premul)))
                {
                    if (surface == null) return null;

                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.Transparent);

                    var skInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                    using (var skBitmap = new SKBitmap(skInfo))
                    {
                        var ptr = skBitmap.GetPixels();
                        System.Runtime.InteropServices.Marshal.Copy(srcPixels, 0, ptr, srcPixels.Length);
                        skBitmap.NotifyPixelsChanged();

                        // Build tinted bitmaps once, reuse across layers
                        var tinted1 = BuildTintedBitmap(skBitmap, color1, intensity);
                        var tinted2 = BuildTintedBitmap(skBitmap, color2, intensity);

                        var rect1 = new SKRect(
                            extend - shift, extend - shift,
                            extend + targetW - shift, extend + targetH - shift);
                        var rect2 = new SKRect(
                            extend + shift, extend + shift,
                            extend + targetW + shift, extend + targetH + shift);

                        // Layer 1: Wide soft outer glow
                        float outerS = (float)outerSigma;
                        DrawBlurredLayer(canvas, tinted1, rect1, outerS, 0.5f);
                        DrawBlurredLayer(canvas, tinted2, rect2, outerS, 0.5f);

                        // Layer 2: Medium glow
                        float midS = (float)blurSigma;
                        DrawBlurredLayer(canvas, tinted1, rect1, midS, 0.7f);
                        DrawBlurredLayer(canvas, tinted2, rect2, midS, 0.7f);

                        // Layer 3: Tight bright inner glow
                        float innerS = (float)(blurSigma * 0.5);
                        DrawBlurredLayer(canvas, tinted1, rect1, innerS, 0.9f);
                        DrawBlurredLayer(canvas, tinted2, rect2, innerS, 0.9f);

                        tinted1.Dispose();
                        tinted2.Dispose();
                    }

                    using (var snapshot = surface.Snapshot())
                    using (var data = snapshot.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        if (data == null) return null;

                        var ms = new MemoryStream();
                        data.SaveTo(ms);
                        ms.Position = 0;

                        var result = new BitmapImage();
                        result.BeginInit();
                        result.CacheOption = BitmapCacheOption.OnLoad;
                        result.StreamSource = ms;
                        result.EndInit();
                        result.Freeze();
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[IconGlow] SkiaSharp RenderGlow failed");
                return null;
            }
        }

        // Builds a tinted bitmap: RGB = tint color, Alpha = source luminance * source alpha.
        private static SKBitmap BuildTintedBitmap(SKBitmap srcBitmap, Color tint, double intensity)
        {
            int w = srcBitmap.Width, h = srcBitmap.Height;
            var tintedInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var tinted = new SKBitmap(tintedInfo);

            var srcPtr = srcBitmap.GetPixels();
            var dstPtr = tinted.GetPixels();
            int pixelCount = w * h;
            byte[] srcPixels = new byte[pixelCount * 4];
            byte[] dstPixels = new byte[pixelCount * 4];
            System.Runtime.InteropServices.Marshal.Copy(srcPtr, srcPixels, 0, srcPixels.Length);

            byte tR = tint.R, tG = tint.G, tB = tint.B;
            for (int i = 0; i < pixelCount; i++)
            {
                int off = i * 4;
                byte sB = srcPixels[off], sG = srcPixels[off + 1], sR = srcPixels[off + 2], sA = srcPixels[off + 3];

                double lum = (0.299 * sR + 0.587 * sG + 0.114 * sB) / 255.0;
                double boosted = Math.Min(1.0, lum * intensity);
                byte alpha = (byte)(boosted * sA);

                double af = alpha / 255.0;
                dstPixels[off]     = (byte)(tB * af);
                dstPixels[off + 1] = (byte)(tG * af);
                dstPixels[off + 2] = (byte)(tR * af);
                dstPixels[off + 3] = alpha;
            }

            System.Runtime.InteropServices.Marshal.Copy(dstPixels, 0, dstPtr, dstPixels.Length);
            tinted.NotifyPixelsChanged();
            return tinted;
        }

        // Draws a blurred layer with additive blending (SKBlendMode.Plus).
        private static void DrawBlurredLayer(SKCanvas canvas, SKBitmap tinted, SKRect destRect,
            float sigma, float alphaScale)
        {
            using (var blur = SKImageFilter.CreateBlur(sigma, sigma, null, null))
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = true;
                paint.FilterQuality = SKFilterQuality.High;
                paint.ImageFilter = blur;
                paint.BlendMode = SKBlendMode.Plus; // additive — light accumulates
                paint.Color = new SKColor(255, 255, 255, (byte)(alphaScale * 255));
                canvas.DrawBitmap(tinted, destRect, paint);
            }
        }

        public static Image CreateGlowImage(BitmapSource glowBitmap, double iconWidth, double iconHeight, double blurSigma)
        {
            double outerSigma = blurSigma * 2.0;
            double extend = Math.Ceiling(outerSigma * 2.5);

            return new Image
            {
                Source = glowBitmap,
                Width = iconWidth + extend * 2,
                Height = iconHeight + extend * 2,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Opacity = 1.0,
                Margin = new Thickness(-extend, -extend, -extend, -extend),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}
