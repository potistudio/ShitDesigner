using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Rendering.VJ.Audio
{
    /// <summary>
    /// A deterministic CPU-side analysis frame.  The arrays intentionally use
    /// fixed lengths so a shader binding can upload them without depending on
    /// the input clip length.
    /// </summary>
    public sealed class AudioAnalysisFrame
    {
        public const int WaveformLength = 256;
        public const int MelBandCount = 24;

        public float Rms { get; }
        public float Peak { get; }
        public float Beat { get; }
        public float BpmPhase { get; }
        public float[] Waveform { get; }
        public float[] Fft64 { get; }
        public float[] Fft128 { get; }
        public float[] Fft512 { get; }
        public float[] MelBands { get; }

        internal AudioAnalysisFrame(float rms, float peak, float beat, float bpmPhase, float[] waveform,
            float[] fft64, float[] fft128, float[] fft512, float[] melBands)
        {
            Rms = Finite(rms);
            Peak = Finite(peak);
            Beat = Mathf.Clamp01(Finite(beat));
            BpmPhase = Mathf.Repeat(Finite(bpmPhase), 1f);
            Waveform = waveform ?? throw new ArgumentNullException(nameof(waveform));
            Fft64 = fft64 ?? throw new ArgumentNullException(nameof(fft64));
            Fft128 = fft128 ?? throw new ArgumentNullException(nameof(fft128));
            Fft512 = fft512 ?? throw new ArgumentNullException(nameof(fft512));
            MelBands = melBands ?? throw new ArgumentNullException(nameof(melBands));
        }

        public bool IsFinite()
        {
            return IsFiniteArray(Waveform) && IsFiniteArray(Fft64) && IsFiniteArray(Fft128)
                && IsFiniteArray(Fft512) && IsFiniteArray(MelBands)
                && IsFinite(Rms) && IsFinite(Peak) && IsFinite(Beat) && IsFinite(BpmPhase);
        }

        private static bool IsFiniteArray(IReadOnlyList<float> values)
        {
            if (values == null) return false;
            for (var i = 0; i < values.Count; i++) if (!IsFinite(values[i])) return false;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Finite(float value) => IsFinite(value) ? value : 0f;
    }

    public static class AudioAnalysisFixture
    {
        public static AudioAnalysisFrame Analyze(IReadOnlyList<float> samples, int sampleRate, double timeSeconds,
            float bpm = 120f, float previousRms = 0f)
        {
            if (sampleRate < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (samples == null || samples.Count == 0) samples = new[] { 0f };
            var sum = 0d;
            var peak = 0f;
            for (var i = 0; i < samples.Count; i++)
            {
                var value = SafeSample(samples[i]);
                sum += value * value;
                peak = Mathf.Max(peak, Mathf.Abs(value));
            }

            var rms = Mathf.Sqrt(Mathf.Max(0f, (float)(sum / samples.Count)));
            var beatDelta = Mathf.Max(0f, rms - Mathf.Max(0f, SafeSample(previousRms)));
            var beat = Mathf.Clamp01(beatDelta * 8f);
            var phase = Frac((float)(SafeDouble(timeSeconds) * Mathf.Max(0f, bpm) / 60d));
            var waveform = Resample(samples, AudioAnalysisFrame.WaveformLength);
            var fft64 = ComputeSpectrum(samples, sampleRate, 64);
            var fft128 = ComputeSpectrum(samples, sampleRate, 128);
            var fft512 = ComputeSpectrum(samples, sampleRate, 512);
            var mel = ComputeMelBands(fft512, AudioAnalysisFrame.MelBandCount);
            return new AudioAnalysisFrame(rms, peak, beat, phase, waveform, fft64, fft128, fft512, mel);
        }

        public static float[] Sine(int sampleCount, int sampleRate, float frequency, float amplitude = 1f, float phase = 0f)
        {
            ValidateSignalArguments(sampleCount, sampleRate);
            var output = new float[sampleCount];
            var safeFrequency = Finite(frequency);
            var safeAmplitude = Mathf.Clamp(Finite(amplitude), -1f, 1f);
            for (var i = 0; i < output.Length; i++)
                output[i] = safeAmplitude * Mathf.Sin((i * safeFrequency / sampleRate + phase) * Mathf.PI * 2f);
            return output;
        }

        public static float[] Impulse(int sampleCount, float amplitude = 1f, int index = 0)
        {
            if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            var output = new float[sampleCount];
            if (index >= 0 && index < sampleCount) output[index] = Mathf.Clamp(Finite(amplitude), -1f, 1f);
            return output;
        }

        public static float[] Sweep(int sampleCount, int sampleRate, float startFrequency, float endFrequency, float amplitude = 1f)
        {
            ValidateSignalArguments(sampleCount, sampleRate);
            var output = new float[sampleCount];
            var safeAmplitude = Mathf.Clamp(Finite(amplitude), -1f, 1f);
            var start = Finite(startFrequency);
            var end = Finite(endFrequency);
            for (var i = 0; i < output.Length; i++)
            {
                var t = sampleCount == 1 ? 0f : i / (float)(sampleCount - 1);
                var frequency = Mathf.Lerp(start, end, t);
                output[i] = safeAmplitude * Mathf.Sin((frequency * i / sampleRate) * Mathf.PI * 2f);
            }
            return output;
        }

        public static float[] Noise(int sampleCount, uint seed, float amplitude = 1f)
        {
            if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            var output = new float[sampleCount];
            var state = seed == 0u ? 0x6E624EB7u : seed;
            var safeAmplitude = Mathf.Clamp(Finite(amplitude), -1f, 1f);
            for (var i = 0; i < output.Length; i++)
            {
                state = state * 1664525u + 1013904223u;
                output[i] = (((state >> 8) & 0x00FFFFFFu) / 8388607.5f - 1f) * safeAmplitude;
            }
            return output;
        }

        private static float[] ComputeSpectrum(IReadOnlyList<float> samples, int sampleRate, int fftSize)
        {
            var spectrum = new float[fftSize];
            var half = fftSize / 2;
            for (var bin = 0; bin <= half; bin++)
            {
                var real = 0d;
                var imaginary = 0d;
                for (var sampleIndex = 0; sampleIndex < fftSize; sampleIndex++)
                {
                    var sample = SafeSample(samples[sampleIndex % samples.Count]);
                    var window = 0.5d - 0.5d * Math.Cos(Math.PI * 2d * sampleIndex / Math.Max(1, fftSize - 1));
                    var angle = Math.PI * 2d * bin * sampleIndex / fftSize;
                    real += sample * window * Math.Cos(angle);
                    imaginary -= sample * window * Math.Sin(angle);
                }
                spectrum[bin] = Finite((float)(Math.Sqrt(real * real + imaginary * imaginary) * 2d / Math.Max(1, fftSize)));
            }
            return spectrum;
        }

        private static float[] ComputeMelBands(float[] spectrum, int bandCount)
        {
            var result = new float[bandCount];
            var half = Math.Max(1, spectrum.Length / 2);
            for (var band = 0; band < result.Length; band++)
            {
                var low = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(band / (float)result.Length, 2f) * half), 0, half - 1);
                var high = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow((band + 1) / (float)result.Length, 2f) * half), low + 1, half);
                var sum = 0f;
                for (var index = low; index < high; index++) sum += spectrum[index];
                result[band] = Finite(sum / Math.Max(1, high - low));
            }
            return result;
        }

        private static float[] Resample(IReadOnlyList<float> samples, int length)
        {
            var result = new float[length];
            for (var i = 0; i < result.Length; i++)
            {
                var index = samples.Count == 1 ? 0f : i * (samples.Count - 1f) / Math.Max(1, length - 1);
                var low = Mathf.FloorToInt(index);
                var high = Mathf.Min(low + 1, samples.Count - 1);
                result[i] = Mathf.Lerp(SafeSample(samples[low]), SafeSample(samples[high]), index - low);
            }
            return result;
        }

        private static void ValidateSignalArguments(int sampleCount, int sampleRate)
        {
            if (sampleCount < 1) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (sampleRate < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        private static float SafeSample(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -1e6f, 1e6f);
        private static double SafeDouble(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -1e20f, 1e20f);
        private static float Frac(float value) => value - Mathf.Floor(value);
    }
}
