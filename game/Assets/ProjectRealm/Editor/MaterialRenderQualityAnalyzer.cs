using System;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    // Diagnostic evidence, not a substitute for art review. Box filtering reveals macro motifs.
    public static class MaterialRenderQualityAnalyzer
    {
        public static Result Measure(Texture2D texture, int tileCount)
        {
            var width = Math.Min(128, texture.width);
            var height = Math.Min(128, texture.height);
            var pixels = texture.GetPixels32();
            var samples = new double[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;
                var count = 0;
                for (var py = y * texture.height / height; py < (y + 1) * texture.height / height; py++)
                for (var px = x * texture.width / width; px < (x + 1) * texture.width / width; px++)
                {
                    var pixel = pixels[py * texture.width + px];
                    sum += (pixel.r * 0.2126 + pixel.g * 0.7152 + pixel.b * 0.0722) / 255d;
                    count++;
                }
                samples[y * width + x] = sum / count;
            }
            return Measure(samples, width, height, tileCount);
        }

        public static Result Measure(double[] samples, int width, int height, int tileCount)
        {
            if (samples == null || width < 2 || height < 2 || samples.Length != width * height || tileCount < 2)
                throw new ArgumentException("Quality analysis requires a rectangular image and at least two repeats.");
            double mean = 0, squared = 0;
            foreach (var sample in samples) { mean += sample; squared += sample * sample; }
            mean /= samples.Length;
            var deviation = Math.Sqrt(Math.Max(0, squared / samples.Length - mean * mean));
            return new Result(mean, deviation,
                Correlation(samples, width, height, (double)width / tileCount, 0),
                Correlation(samples, width, height, 0, (double)height / tileCount),
                Correlation(samples, width, height, (double)width / tileCount, (double)height / tileCount));
        }

        private static double Correlation(double[] pixels, int width, int height, double dx, double dy)
        {
            double sumA = 0, sumB = 0, aa = 0, bb = 0, ab = 0;
            var count = 0;
            for (var y = 0; y + dy < height - 1; y++)
            for (var x = 0; x + dx < width - 1; x++)
            {
                var bx = x + dx;
                var by = y + dy;
                var ix = (int)bx;
                var iy = (int)by;
                var fx = bx - ix;
                var fy = by - iy;
                var lower = pixels[iy * width + ix] * (1 - fx) + pixels[iy * width + ix + 1] * fx;
                var upper = pixels[(iy + 1) * width + ix] * (1 - fx) + pixels[(iy + 1) * width + ix + 1] * fx;
                var b = lower * (1 - fy) + upper * fy;
                var a = pixels[y * width + x];
                sumA += a; sumB += b; aa += a * a; bb += b * b; ab += a * b; count++;
            }
            if (count == 0) return 0;
            var varianceA = Math.Max(0, aa - sumA * sumA / count);
            var varianceB = Math.Max(0, bb - sumB * sumB / count);
            var denominator = Math.Sqrt(varianceA * varianceB);
            return denominator < 1e-9 ? 0 : (ab - sumA * sumB / count) / denominator;
        }

        public readonly struct Result
        {
            public readonly double Mean;
            public readonly double CoarseContrast;
            public readonly double HorizontalCorrelation;
            public readonly double VerticalCorrelation;
            public readonly double DiagonalCorrelation;
            public double MaxAbsoluteCorrelation => Math.Max(Math.Abs(HorizontalCorrelation),
                Math.Max(Math.Abs(VerticalCorrelation), Math.Abs(DiagonalCorrelation)));
            public Result(double mean, double contrast, double horizontal, double vertical, double diagonal)
            {
                Mean = mean; CoarseContrast = contrast;
                HorizontalCorrelation = horizontal; VerticalCorrelation = vertical; DiagonalCorrelation = diagonal;
            }
        }
    }
}
