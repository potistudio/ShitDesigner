using NUnit.Framework;

namespace ShitDesigner.TestHarness.Tests
{
    [Category("docs/ARCHITECTURE/Testing.md/Standalone性能Harness")]
    public sealed class HarnessMetricValidationContractTests
    {
        [Test]
        public void MetricEvaluator_PresentedZeroCpuTimingCountsAsABadRatioSample()
        {
            var metrics = ValidMetrics();
            metrics.PresentedSamples[0].cpuMilliseconds = 0d;

            var result = Evaluate(metrics);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
        }

        [Test]
        public void MetricEvaluator_PresentedZeroGpuTimingCountsAsABadRatioSample()
        {
            var metrics = ValidMetrics();
            metrics.PresentedSamples[0].gpuMilliseconds = 0d;

            var result = Evaluate(metrics);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
        }

        [Test]
        public void MetricEvaluator_PresentedNegativeTimingCountsAsABadRatioSample()
        {
            var metrics = ValidMetrics();
            metrics.PresentedSamples[0].cpuMilliseconds = -0.001d;

            var result = Evaluate(metrics);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
        }

        private static HarnessEvaluationResult Evaluate(HarnessMetricAccumulator metrics) =>
            HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000,
                0, 0, 0, 0, 0);

        private static HarnessMetricAccumulator ValidMetrics()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(new HarnessMetricSample
            {
                cpuMilliseconds = 8d,
                gpuMilliseconds = 7d,
                sampleSeconds = 0d,
                programFrameNumber = 1,
                programWidth = 1920,
                programHeight = 1080,
                programFormat = "R16G16B16A16_SFloat",
                programTargetFramesPerSecond = 60,
                poolBudgetBytes = 1000,
                poolLeasedBytes = 40,
                poolFreeBytes = 40,
                poolHighWaterBytes = 80,
                previews = new[] { Preview("preview-1"), Preview("preview-2") },
                programPresented = true,
                programHealthy = true
            });
            metrics.Add(new HarnessMetricSample
            {
                cpuMilliseconds = 8d,
                gpuMilliseconds = 7d,
                sampleSeconds = 1d / 60d,
                programFrameNumber = 2,
                programWidth = 1920,
                programHeight = 1080,
                programFormat = "R16G16B16A16_SFloat",
                programTargetFramesPerSecond = 60,
                poolBudgetBytes = 1000,
                poolLeasedBytes = 40,
                poolFreeBytes = 40,
                poolHighWaterBytes = 80,
                previews = new[] { Preview("preview-1"), Preview("preview-2") },
                programPresented = true,
                programHealthy = true
            });
            return metrics;
        }

        private static HarnessPreviewMetric Preview(string id) => new HarnessPreviewMetric
        {
            id = id,
            width = 640,
            height = 360,
            format = "R8G8B8A8_UNorm",
            targetFramesPerSecond = 30,
            frameNumber = 1,
            quality = "Stage0",
            qualityStage = 0
        };
    }
}
