using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UniPlaySong.Services
{
    // Extracts dominant colors from a game's background/cover image for the Dynamic visualizer theme.
    // Uses quantized histogram approach — fast, no external dependencies, .NET 4.6.2 compatible.
    // Two algorithm paths: v6 (simple histogram) and v7 (center-weighted + bucket merging + diversity bonus).
    public static class GameColorExtractor
    {
        private static readonly Color FallbackBottom = Color.FromArgb(200, 255, 255, 255);
        private static readonly Color FallbackTop = Color.FromArgb(200, 255, 255, 255);

        // Extract a dark (bottom) and bright (top) color from the image at imagePath.
        // Brightness/saturation floors are user-configurable via settings sliders.
        // useAdvancedAlgo: true = v7 (center-weighted, bucket merging, diversity bonus), false = v6 (simple histogram).
        // vividMode (Vibrant Vibes): aggressive color separation for creative gradients (requires useAdvancedAlgo).
        // Returns Classic white fallback if extraction fails.
        public static (Color Bottom, Color Top) ExtractDominantColors(
            string imagePath,
            int minBrightnessBottom = 100,
            int minBrightnessTop = 140,
            float minSatBottom = 0.30f,
            float minSatTop = 0.35f,
            bool useAdvancedAlgo = false,
            bool vividMode = false)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 100; // Scale down for speed
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();

                // Convert to Bgra32 for consistent pixel access
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.Freeze();

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                byte bottomR, bottomG, bottomB, topR, topG, topB;

                if (useAdvancedAlgo)
                    ExtractAdvanced(pixels, width, height, stride, vividMode,
                        out bottomR, out bottomG, out bottomB, out topR, out topG, out topB);
                else
                    ExtractSimple(pixels, width, height, stride,
                        out bottomR, out bottomG, out bottomB, out topR, out topG, out topB);

                // Ensure minimum brightness — bars must be visible against dark backgrounds
                EnsureMinBrightness(ref bottomR, ref bottomG, ref bottomB, minBrightnessBottom);
                EnsureMinBrightness(ref topR, ref topG, ref topB, minBrightnessTop);

                // Ensure minimum saturation
                EnsureMinSaturation(ref bottomR, ref bottomG, ref bottomB, minSatBottom);
                EnsureMinSaturation(ref topR, ref topG, ref topB, minSatTop);

                return (
                    Color.FromArgb(255, bottomR, bottomG, bottomB),
                    Color.FromArgb(255, topR, topG, topB)
                );
            }
            catch
            {
                return (FallbackBottom, FallbackTop);
            }
        }

        // v6 algorithm: simple flat histogram, no center-weighting, no bucket merging, no diversity bonus.
        // Straightforward frequency-based color extraction with saturation+brightness scoring.
        private static void ExtractSimple(byte[] pixels, int width, int height, int stride,
            out byte bottomR, out byte bottomG, out byte bottomB,
            out byte topR, out byte topG, out byte topB)
        {
            var buckets = new int[4096];
            var bucketR = new long[4096];
            var bucketG = new long[4096];
            var bucketB = new long[4096];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];
                if (a < 128) continue;

                int sum = r + g + b;
                int maxC = Math.Max(r, Math.Max(g, b));
                int minC = Math.Min(r, Math.Min(g, b));

                if (sum < 30) continue;   // near-black
                if (sum > 720) continue;  // near-white
                if (maxC - minC < 10) continue; // achromatic

                int qr = r >> 4;
                int qg = g >> 4;
                int qb = b >> 4;
                int idx = (qr << 8) | (qg << 4) | qb;

                buckets[idx]++;
                bucketR[idx] += r;
                bucketG[idx] += g;
                bucketB[idx] += b;
            }

            // Find top clusters by frequency
            const int TopN = 10;
            var topIndices = new int[TopN];
            var topCounts = new int[TopN];

            for (int i = 0; i < 4096; i++)
            {
                if (buckets[i] == 0) continue;

                for (int j = 0; j < TopN; j++)
                {
                    if (buckets[i] > topCounts[j])
                    {
                        for (int k = TopN - 1; k > j; k--)
                        {
                            topIndices[k] = topIndices[k - 1];
                            topCounts[k] = topCounts[k - 1];
                        }
                        topIndices[j] = i;
                        topCounts[j] = buckets[i];
                        break;
                    }
                }
            }

            if (topCounts[0] == 0)
            {
                bottomR = bottomG = bottomB = topR = topG = topB = 255;
                return;
            }

            const float IntensityThreshold = 65f;

            // Bottom color: most common cluster that meets intensity threshold
            int bottomIdx = -1;
            for (int j = 0; j < TopN && topCounts[j] > 0; j++)
            {
                int ci = topIndices[j];
                int cnt = buckets[ci];
                float avgR = bucketR[ci] / (float)cnt;
                float avgG = bucketG[ci] / (float)cnt;
                float avgB = bucketB[ci] / (float)cnt;
                float maxV = Math.Max(avgR, Math.Max(avgG, avgB));
                if (maxV >= IntensityThreshold)
                {
                    bottomIdx = j;
                    break;
                }
            }
            if (bottomIdx < 0) bottomIdx = 0;

            int bci = topIndices[bottomIdx];
            int bCnt = buckets[bci];
            bottomR = (byte)(bucketR[bci] / bCnt);
            bottomG = (byte)(bucketG[bci] / bCnt);
            bottomB = (byte)(bucketB[bci] / bCnt);

            // Top color: best saturation + brightness score (no diversity bonus in v6)
            int bestTopIdx = bottomIdx;
            float bestScore = 0;
            for (int j = 0; j < TopN && topCounts[j] > 0; j++)
            {
                int ci = topIndices[j];
                int cnt = buckets[ci];
                float avgR = bucketR[ci] / (float)cnt;
                float avgG = bucketG[ci] / (float)cnt;
                float avgB = bucketB[ci] / (float)cnt;

                float maxV = Math.Max(avgR, Math.Max(avgG, avgB));
                if (maxV < IntensityThreshold) continue;

                float brightness = (avgR + avgG + avgB) / 3f;
                float minV = Math.Min(avgR, Math.Min(avgG, avgB));
                float saturation = maxV > 0 ? (maxV - minV) / maxV : 0;

                float freqWeight = topCounts[j] / (float)topCounts[0];

                float score = saturation * 0.4f + (brightness / 255f) * 0.35f + freqWeight * 0.25f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTopIdx = j;
                }
            }

            int tci = topIndices[bestTopIdx];
            int tCnt = buckets[tci];
            topR = (byte)(bucketR[tci] / tCnt);
            topG = (byte)(bucketG[tci] / tCnt);
            topB = (byte)(bucketB[tci] / tCnt);
        }

        // v7 algorithm: center-weighted sampling, neighbor bucket merging, diversity bonus.
        // vividMode: aggressive color separation for Vibrant Vibes preset.
        private static void ExtractAdvanced(byte[] pixels, int width, int height, int stride, bool vividMode,
            out byte bottomR, out byte bottomG, out byte bottomB,
            out byte topR, out byte topG, out byte topB)
        {
            // Center-weighted: pixels near image center count more (game subjects are centered)
            var buckets = new float[4096];
            var bucketR = new double[4096];
            var bucketG = new double[4096];
            var bucketB = new double[4096];

            float cx = width / 2f;
            float cy = height / 2f;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                float dy = (y - cy) / cy;
                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x * 4;
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a < 128) continue;

                    int sum = r + g + b;
                    int maxC = Math.Max(r, Math.Max(g, b));
                    int minC = Math.Min(r, Math.Min(g, b));

                    if (sum < 30) continue;   // near-black
                    if (sum > 720) continue;  // near-white
                    if (maxC - minC < 10) continue; // achromatic

                    float dx = (x - cx) / cx;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float weight = 1f - 0.7f * Math.Min(dist / 1.414f, 1f); // 1.0 center, 0.3 corners

                    int qr = r >> 4;
                    int qg = g >> 4;
                    int qb = b >> 4;
                    int idx = (qr << 8) | (qg << 4) | qb;

                    buckets[idx] += weight;
                    bucketR[idx] += r * weight;
                    bucketG[idx] += g * weight;
                    bucketB[idx] += b * weight;
                }
            }

            // Merge neighboring buckets: absorb small buckets into their largest adjacent neighbor
            MergeNeighborBuckets(buckets, bucketR, bucketG, bucketB);

            // Find top clusters by weighted frequency
            const int TopN = 10;
            var topIndices = new int[TopN];
            var topCounts = new float[TopN];

            for (int i = 0; i < 4096; i++)
            {
                if (buckets[i] < 0.001f) continue;

                for (int j = 0; j < TopN; j++)
                {
                    if (buckets[i] > topCounts[j])
                    {
                        for (int k = TopN - 1; k > j; k--)
                        {
                            topIndices[k] = topIndices[k - 1];
                            topCounts[k] = topCounts[k - 1];
                        }
                        topIndices[j] = i;
                        topCounts[j] = buckets[i];
                        break;
                    }
                }
            }

            if (topCounts[0] < 0.001f)
            {
                bottomR = bottomG = bottomB = topR = topG = topB = 255;
                return;
            }

            const float IntensityThreshold = 65f;

            // Bottom color: most common cluster that meets intensity threshold
            int bottomIdx = -1;
            for (int j = 0; j < TopN && topCounts[j] > 0.001f; j++)
            {
                int ci = topIndices[j];
                float w = buckets[ci];
                float avgR = (float)(bucketR[ci] / w);
                float avgG = (float)(bucketG[ci] / w);
                float avgB = (float)(bucketB[ci] / w);
                float maxV = Math.Max(avgR, Math.Max(avgG, avgB));
                if (maxV >= IntensityThreshold)
                {
                    bottomIdx = j;
                    break;
                }
            }
            if (bottomIdx < 0) bottomIdx = 0;

            int bci = topIndices[bottomIdx];
            float bWeight = buckets[bci];
            bottomR = (byte)(bucketR[bci] / bWeight);
            bottomG = (byte)(bucketG[bci] / bWeight);
            bottomB = (byte)(bucketB[bci] / bWeight);

            // Top color: best saturation + brightness, with diversity bonus for color contrast.
            // Vivid mode: much stronger diversity enforcement for creative gradients.
            float minColorDist = vividMode ? 25f : 40f;
            float diversityBonus = vividMode ? 0.35f : 0.15f;
            int bestTopIdx = bottomIdx;
            float bestScore = 0;
            for (int j = 0; j < TopN && topCounts[j] > 0.001f; j++)
            {
                int ci = topIndices[j];
                float w = buckets[ci];
                float avgR = (float)(bucketR[ci] / w);
                float avgG = (float)(bucketG[ci] / w);
                float avgB = (float)(bucketB[ci] / w);

                float maxV = Math.Max(avgR, Math.Max(avgG, avgB));
                if (maxV < IntensityThreshold) continue;

                // Color distance from bottom — Euclidean RGB distance
                float dr = avgR - bottomR;
                float dg = avgG - bottomG;
                float db = avgB - bottomB;
                float colorDist = (float)Math.Sqrt(dr * dr + dg * dg + db * db);

                float brightness = (avgR + avgG + avgB) / 3f;
                float minV = Math.Min(avgR, Math.Min(avgG, avgB));
                float saturation = maxV > 0 ? (maxV - minV) / maxV : 0;

                float freqWeight = topCounts[j] / topCounts[0];

                // Vivid mode: prioritize saturation and color distance over frequency
                float score;
                if (vividMode)
                    score = saturation * 0.5f + (brightness / 255f) * 0.2f + freqWeight * 0.1f + (colorDist / 441f) * 0.2f;
                else
                    score = saturation * 0.4f + (brightness / 255f) * 0.35f + freqWeight * 0.25f;

                if (colorDist >= minColorDist)
                    score += diversityBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTopIdx = j;
                }
            }

            int tci = topIndices[bestTopIdx];
            float tWeight = buckets[tci];
            topR = (byte)(bucketR[tci] / tWeight);
            topG = (byte)(bucketG[tci] / tWeight);
            topB = (byte)(bucketB[tci] / tWeight);

            // Vivid mode: if top and bottom are still too similar, shift top hue
            if (vividMode)
            {
                float finalDr = topR - bottomR;
                float finalDg = topG - bottomG;
                float finalDb = topB - bottomB;
                float finalDist = (float)Math.Sqrt(finalDr * finalDr + finalDg * finalDg + finalDb * finalDb);
                if (finalDist < 50f)
                {
                    ShiftHue(ref topR, ref topG, ref topB, 120f);
                }
            }
        }

        // Merge small buckets into their largest adjacent neighbor (within 1 step per channel).
        // Fixes color splitting at quantization boundaries where similar colors land in different buckets.
        private static void MergeNeighborBuckets(float[] buckets, double[] bucketR, double[] bucketG, double[] bucketB)
        {
            float maxBucket = 0;
            for (int i = 0; i < 4096; i++)
                if (buckets[i] > maxBucket) maxBucket = buckets[i];

            if (maxBucket < 0.001f) return;
            float mergeThreshold = maxBucket * 0.10f;

            for (int i = 0; i < 4096; i++)
            {
                if (buckets[i] < 0.001f || buckets[i] >= mergeThreshold) continue;

                int qr = (i >> 8) & 0xF;
                int qg = (i >> 4) & 0xF;
                int qb = i & 0xF;

                int bestNeighbor = -1;
                float bestWeight = buckets[i];

                int[] offsets = { -1, 1 };
                foreach (int dr in offsets)
                {
                    int nr = qr + dr;
                    if (nr >= 0 && nr < 16)
                    {
                        int ni = (nr << 8) | (qg << 4) | qb;
                        if (buckets[ni] > bestWeight) { bestWeight = buckets[ni]; bestNeighbor = ni; }
                    }
                }
                foreach (int dg in offsets)
                {
                    int ng = qg + dg;
                    if (ng >= 0 && ng < 16)
                    {
                        int ni = (qr << 8) | (ng << 4) | qb;
                        if (buckets[ni] > bestWeight) { bestWeight = buckets[ni]; bestNeighbor = ni; }
                    }
                }
                foreach (int db in offsets)
                {
                    int nb = qb + db;
                    if (nb >= 0 && nb < 16)
                    {
                        int ni = (qr << 8) | (qg << 4) | nb;
                        if (buckets[ni] > bestWeight) { bestWeight = buckets[ni]; bestNeighbor = ni; }
                    }
                }

                if (bestNeighbor >= 0)
                {
                    buckets[bestNeighbor] += buckets[i];
                    bucketR[bestNeighbor] += bucketR[i];
                    bucketG[bestNeighbor] += bucketG[i];
                    bucketB[bestNeighbor] += bucketB[i];
                    buckets[i] = 0;
                    bucketR[i] = 0;
                    bucketG[i] = 0;
                    bucketB[i] = 0;
                }
            }
        }

        // Lift all channels proportionally so the brightest reaches at least minBrightness.
        // Preserves hue — just scales up.
        private static void EnsureMinBrightness(ref byte r, ref byte g, ref byte b, int minBrightness)
        {
            int maxV = Math.Max(r, Math.Max(g, b));
            if (maxV >= minBrightness || maxV == 0) return;

            float scale = minBrightness / (float)maxV;
            r = (byte)Math.Min(255, (int)(r * scale));
            g = (byte)Math.Min(255, (int)(g * scale));
            b = (byte)Math.Min(255, (int)(b * scale));
        }

        // Boost saturation if below minSat (0-1) by pushing non-dominant channels down
        private static void EnsureMinSaturation(ref byte r, ref byte g, ref byte b, float minSat)
        {
            float maxV = Math.Max(r, Math.Max(g, b));
            float minV = Math.Min(r, Math.Min(g, b));
            if (maxV == 0) return;

            float sat = (maxV - minV) / maxV;
            if (sat >= minSat) return;

            float newMin = maxV * (1f - minSat);
            float range = maxV - minV;
            if (range < 1) range = 1;

            r = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - r) / range * (maxV - newMin)));
            g = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - g) / range * (maxV - newMin)));
            b = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - b) / range * (maxV - newMin)));
        }

        // Rotate hue by degrees while preserving brightness and saturation.
        // Used by vivid mode to force color contrast when bottom/top are too similar.
        private static void ShiftHue(ref byte r, ref byte g, ref byte b, float degrees)
        {
            float rf = r / 255f, gf = g / 255f, bf = b / 255f;
            float maxV = Math.Max(rf, Math.Max(gf, bf));
            float minV = Math.Min(rf, Math.Min(gf, bf));
            float delta = maxV - minV;

            float h = 0;
            if (delta > 0.001f)
            {
                if (maxV == rf) h = 60f * (((gf - bf) / delta) % 6);
                else if (maxV == gf) h = 60f * ((bf - rf) / delta + 2);
                else h = 60f * ((rf - gf) / delta + 4);
            }
            if (h < 0) h += 360f;

            float s = maxV > 0 ? delta / maxV : 0;

            h = (h + degrees) % 360f;
            if (h < 0) h += 360f;

            float c = maxV * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2 - 1));
            float m = maxV - c;

            float r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            r = (byte)Math.Min(255, Math.Max(0, (int)((r1 + m) * 255)));
            g = (byte)Math.Min(255, Math.Max(0, (int)((g1 + m) * 255)));
            b = (byte)Math.Min(255, Math.Max(0, (int)((b1 + m) * 255)));
        }
    }
}
