using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UniPlaySong.Services
{
    // Extracts dominant colors from a game's background/cover image for the Dynamic visualizer theme.
    // Uses quantized histogram approach — fast, no external dependencies, .NET 4.6.2 compatible.
    public static class GameColorExtractor
    {
        private static readonly Color FallbackBottom = Color.FromArgb(200, 255, 255, 255);
        private static readonly Color FallbackTop = Color.FromArgb(200, 255, 255, 255);

        // Extract a dark (bottom) and bright (top) color from the image at imagePath.
        // Brightness/saturation floors are user-configurable via settings sliders.
        // Returns Classic white fallback if extraction fails.
        public static (Color Bottom, Color Top) ExtractDominantColors(
            string imagePath,
            int minBrightnessBottom = 100,
            int minBrightnessTop = 140,
            float minSatBottom = 0.30f,
            float minSatTop = 0.35f)
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

                // Quantize to 4-bit per channel (16 levels each = 4096 buckets)
                // Center-weighted: pixels near image center count more (game subjects are centered)
                var buckets = new float[4096];  // weighted counts
                var bucketR = new double[4096]; // weighted sum of R values per bucket
                var bucketG = new double[4096];
                var bucketB = new double[4096];

                float cx = width / 2f;
                float cy = height / 2f;
                float maxDist = (float)Math.Sqrt(cx * cx + cy * cy);

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    // Center weight: 1.0 at center, 0.3 at corners (linear falloff)
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

                // Merge neighboring buckets: absorb small buckets into their largest adjacent neighbor.
                // Prevents color splitting at quantization boundaries (e.g., R=127 vs R=128).
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
                    return (FallbackBottom, FallbackTop);

                // Intensity threshold: skip clusters whose max channel is below this
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
                byte bottomR = (byte)(bucketR[bci] / bWeight);
                byte bottomG = (byte)(bucketG[bci] / bWeight);
                byte bottomB = (byte)(bucketB[bci] / bWeight);

                // Top color: best saturation + brightness, with minimum distance from bottom color.
                // Ensures visual contrast between gradient base and tip.
                const float MinColorDistance = 40f;
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
                    float score = saturation * 0.4f + (brightness / 255f) * 0.35f + freqWeight * 0.25f;

                    // Bonus for color diversity — reward clusters that differ from bottom
                    if (colorDist >= MinColorDistance)
                        score += 0.15f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTopIdx = j;
                    }
                }

                int tci = topIndices[bestTopIdx];
                float tWeight = buckets[tci];
                byte topR = (byte)(bucketR[tci] / tWeight);
                byte topG = (byte)(bucketG[tci] / tWeight);
                byte topB = (byte)(bucketB[tci] / tWeight);

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

        // Merge small buckets into their largest adjacent neighbor (within 1 step per channel).
        // Fixes color splitting at quantization boundaries where similar colors land in different buckets.
        private static void MergeNeighborBuckets(float[] buckets, double[] bucketR, double[] bucketG, double[] bucketB)
        {
            // Threshold: buckets with less than 10% of the max bucket's weight get merged
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

                // Find largest neighbor (±1 per channel, 6 direct neighbors)
                int bestNeighbor = -1;
                float bestWeight = buckets[i]; // only merge into something bigger

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

        // Boost saturation if below minSat (0-1) by pushing the dominant channel up
        private static void EnsureMinSaturation(ref byte r, ref byte g, ref byte b, float minSat)
        {
            float maxV = Math.Max(r, Math.Max(g, b));
            float minV = Math.Min(r, Math.Min(g, b));
            if (maxV == 0) return;

            float sat = (maxV - minV) / maxV;
            if (sat >= minSat) return;

            // Scale min channels down to achieve target saturation
            // target: (maxV - newMin) / maxV = minSat → newMin = maxV * (1 - minSat)
            float newMin = maxV * (1f - minSat);
            float range = maxV - minV;
            if (range < 1) range = 1;

            // Lerp each channel: channels at maxV stay, channels at minV go to newMin
            r = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - r) / range * (maxV - newMin)));
            g = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - g) / range * (maxV - newMin)));
            b = (byte)Math.Max(0, Math.Min(255, maxV - (maxV - b) / range * (maxV - newMin)));
        }
    }
}
