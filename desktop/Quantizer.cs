/* Variance-based colour quantization.
 *
 * Ported from colorquant.c as shipped in Macromedia Fireworks 8
 * (Configuration/Third Party Source Code/Color Quantization). Original notice,
 * preserved as its licence requires:
 *
 *   This software is copyrighted as noted below. It may be freely copied,
 *   modified, and redistributed, provided that the copyright notice is
 *   preserved on all copies.
 *   There is no warranty or other guarantee of fitness for this software,
 *   it is provided solely "as is".
 *   Author: Craig Kolb, Department of Mathematics, Yale University
 *   Date:   Tue Aug 22 1989
 *   Copyright (C) 1989 Craig E. Kolb
 *
 * Ported to C# 2026-11-07 for Flint; the source of this port is this file.
 *
 * Deviation from the original: UpdateFrequencies() maintained each box's
 * projected frequency arrays incrementally. Here ProjectFreq() recomputes them
 * from the histogram over the box extent. Same maths, less state.
 */
using System;
using System.Collections.Generic;

namespace Flint;

public sealed class Palette
{
    public byte[] R = Array.Empty<byte>();
    public byte[] G = Array.Empty<byte>();
    public byte[] B = Array.Empty<byte>();
    public int Count => R.Length;
}

public static class Quantizer
{
    private const int Bits = 5;              // significant bits per channel
    private const int Side = 1 << Bits;      // 32
    private const int Shift = 8 - Bits;      // 3

    private sealed class Box
    {
        public double WeightedVar;
        public readonly double[] Mean = new double[3];
        public double Weight;
        public readonly double[][] Freq =
        {
            new double[Side + 1], new double[Side + 1], new double[Side + 1]
        };
        public readonly int[] Low = { 0, 0, 0 };
        public readonly int[] High = { Side, Side, Side };

        public Box Clone()
        {
            var d = new Box { WeightedVar = WeightedVar, Weight = Weight };
            Array.Copy(Mean, d.Mean, 3);
            for (int c = 0; c < 3; c++) Array.Copy(Freq[c], d.Freq[c], Side + 1);
            Array.Copy(Low, d.Low, 3);
            Array.Copy(High, d.High, 3);
            return d;
        }
    }

    /// <summary>
    /// Accumulate every frame into one histogram. Passing all frames here is
    /// what gives an animation a single shared palette instead of one palette
    /// per frame (the original's accum_hist).
    /// </summary>
    public static double[] BuildHistogram(IEnumerable<byte[]> framesBgra, out double total)
    {
        var hist = new double[Side * Side * Side];
        double n = 0;
        foreach (var px in framesBgra)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] < 128) continue;   // transparent pixels get their own slot
                int idx = ((px[i + 2] >> Shift) << (2 * Bits))
                        | ((px[i + 1] >> Shift) << Bits)
                        | (px[i] >> Shift);      // BGRA in memory
                hist[idx]++;
                n++;
            }
        }
        total = n;
        return hist;
    }

    private static void ProjectFreq(double[] hist, Box box)
    {
        for (int c = 0; c < 3; c++) Array.Clear(box.Freq[c]);
        double weight = 0;
        for (int r = box.Low[0]; r < box.High[0]; r++)
        {
            for (int g = box.Low[1]; g < box.High[1]; g++)
            {
                int baseIdx = (r << (2 * Bits)) | (g << Bits);
                for (int b = box.Low[2]; b < box.High[2]; b++)
                {
                    double v = hist[baseIdx | b];
                    if (v == 0) continue;
                    box.Freq[0][r] += v;
                    box.Freq[1][g] += v;
                    box.Freq[2][b] += v;
                    weight += v;
                }
            }
        }
        box.Weight = weight;
    }

    private static void BoxStats(Box box, double sumPixels)
    {
        if (box.Weight == 0) { box.WeightedVar = 0; return; }
        box.WeightedVar = 0;
        for (int color = 0; color < 3; color++)
        {
            double mean = 0, varsum = 0;
            for (int i = box.Low[color]; i < box.High[color]; i++)
            {
                double f = box.Freq[color][i];
                mean += i * f;
                varsum += (double)i * i * f;
            }
            box.Mean[color] = mean / box.Weight;
            box.WeightedVar += varsum - box.Mean[color] * box.Mean[color] * box.Weight;
        }
        box.WeightedVar /= sumPixels;
    }

    private static Box[]? FindCutpoint(double[] hist, Box box, int color, double sumPixels)
    {
        if (box.Low[color] + 1 == box.High[color]) return null;
        int minindex = (int)((box.Low[color] + box.Mean[color]) * 0.5);
        int maxindex = (int)((box.Mean[color] + box.High[color]) * 0.5);

        int cutpoint = minindex;
        double curweight = 0;
        for (int i = box.Low[color]; i < minindex; i++) curweight += box.Freq[color][i];

        double u = 0, max = -1;
        for (int i = minindex; i <= maxindex; i++)
        {
            curweight += box.Freq[color][i];
            if (curweight == box.Weight) break;
            u += i * box.Freq[color][i] / box.Weight;
            double d = box.Mean[color] - u;
            double v = curweight / (box.Weight - curweight) * d * d;
            if (v > max) { max = v; cutpoint = i; }
        }
        cutpoint++;
        int clip = Math.Min(Math.Max(cutpoint, box.Low[color]), box.High[color]);

        var b1 = box.Clone();
        var b2 = box.Clone();
        b1.High[color] = clip;
        b2.Low[color] = clip;
        ProjectFreq(hist, b1); ProjectFreq(hist, b2);
        BoxStats(b1, sumPixels); BoxStats(b2, sumPixels);
        return new[] { b1, b2 };
    }

    private static Box[]? CutBox(double[] hist, Box box, double sumPixels)
    {
        if (box.WeightedVar == 0 || box.Weight == 0) return null;
        Box[]? best = null;
        double bestVar = double.PositiveInfinity;
        for (int c = 0; c < 3; c++)
        {
            var pair = FindCutpoint(hist, box, c, sumPixels);
            if (pair == null) continue;
            double tv = pair[0].WeightedVar + pair[1].WeightedVar;
            if (tv < bestVar) { bestVar = tv; best = pair; }
        }
        return best;
    }

    public static Palette Quantize(double[] hist, double total, int maxColors)
    {
        double sumPixels = Math.Max(total, 1);
        var boxes = new List<Box> { new Box() };
        ProjectFreq(hist, boxes[0]);
        BoxStats(boxes[0], sumPixels);

        while (boxes.Count < maxColors)
        {
            int which = 0;
            double max = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].WeightedVar > max) { max = boxes[i].WeightedVar; which = i; }
            }
            var pair = CutBox(hist, boxes[which], sumPixels);
            if (pair == null) break;
            boxes[which] = pair[0];
            boxes.Add(pair[1]);
        }

        const double scale = 256.0 / Side;
        var r = new List<byte>(); var g = new List<byte>(); var b = new List<byte>();
        foreach (var box in boxes)
        {
            if (box.Weight <= 0) continue;
            r.Add(Clamp(box.Mean[0] * scale + scale / 2));
            g.Add(Clamp(box.Mean[1] * scale + scale / 2));
            b.Add(Clamp(box.Mean[2] * scale + scale / 2));
        }
        return new Palette { R = r.ToArray(), G = g.ToArray(), B = b.ToArray() };
    }

    private static byte Clamp(double v) =>
        (byte)Math.Min(255, Math.Max(0, (int)Math.Round(v)));

    /// <summary>
    /// Nearest-colour lookup memoised over the reduced cube (the original's
    /// inv_cmap, filled on demand rather than up front).
    /// </summary>
    public sealed class Indexer
    {
        private readonly Palette _pal;
        private readonly short[] _cache = new short[Side * Side * Side];

        public Indexer(Palette pal)
        {
            _pal = pal;
            Array.Fill(_cache, (short)-1);
        }

        public int Nearest(byte r, byte g, byte b)
        {
            int key = ((r >> Shift) << (2 * Bits)) | ((g >> Shift) << Bits) | (b >> Shift);
            short hit = _cache[key];
            if (hit >= 0) return hit;
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < _pal.Count; i++)
            {
                int dr = r - _pal.R[i], dg = g - _pal.G[i], db = b - _pal.B[i];
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; if (d == 0) break; }
            }
            _cache[key] = (short)best;
            return best;
        }
    }
}
