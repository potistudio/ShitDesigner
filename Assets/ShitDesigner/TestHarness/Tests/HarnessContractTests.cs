using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Bootstrap;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using UnityEngine;

namespace ShitDesigner.TestHarness.Tests
{
    [Category("docs/ARCHITECTURE/Testing.md/Standalone性能Harness")]
    public sealed class HarnessContractTests
    {
        [Test]
        public void Corpus_MissingManifest_IsEnvironmentFailure()
        {
            var result = PerformanceCorpusValidator.Validate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), HarnessCodec.H264);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Does.Contain("manifest"));
        }

        [Test]
        public void Corpus_MissingFile_IsEnvironmentFailure()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":\"v1\",\"entries\":[{\"codec\":\"H264\",\"file\":\"clip.mp4\",\"xxh3_128\":\"00000000000000000000000000000000\",\"bytes\":1,\"width\":1920,\"height\":1080,\"fps\":60}]}" );
                var result = PerformanceCorpusValidator.Validate(root, HarnessCodec.H264);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Error, Does.Contain("missing"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Corpus_HashMismatch_IsEnvironmentFailure()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllBytes(Path.Combine(root, "clip.mp4"), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":\"v1\",\"entries\":[{\"codec\":\"H264\",\"file\":\"clip.mp4\",\"xxh3_128\":\"00000000000000000000000000000000\",\"bytes\":3,\"width\":1920,\"height\":1080,\"fps\":60}]}");
                var result = PerformanceCorpusValidator.Validate(root, HarnessCodec.H264);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Error, Does.Contain("XXH3"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Corpus_NonFhdMetadata_IsEnvironmentFailure()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":\"v1\",\"entries\":[{\"codec\":\"H264\",\"file\":\"clip.mp4\",\"xxh3_128\":\"00000000000000000000000000000000\",\"bytes\":1,\"width\":1280,\"height\":720,\"fps\":30}]}");
                var result = PerformanceCorpusValidator.Validate(root, HarnessCodec.H264);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Error, Does.Contain("FHD"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Metrics_ProductionThresholds_Pass()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var i = 0; i < 100; i++) metrics.Add(ValidSample(i));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.True, result.Failure);
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(1d));
        }

        [TestCase(15d)]
        [TestCase(600d)]
        public void PreviewQualityCoverage_AppendsExactTerminalBoundarySample(double measureSeconds)
        {
            var actual = new[]
            {
                QualitySample(14.95d, 101UL, 0),
                QualitySample(14.9849861d, 102UL, 2)
            };
            var covered = HarnessPreviewQualityContract.AppendTerminalSample(actual, measureSeconds);

            Assert.That(covered.Length, Is.EqualTo(actual.Length + 1));
            Assert.That(covered[covered.Length - 1].sampleSeconds, Is.EqualTo(measureSeconds));
            Assert.That(covered[covered.Length - 1].programFrameNumber, Is.EqualTo(actual[actual.Length - 1].programFrameNumber));
            Assert.That(covered[covered.Length - 1].previews, Is.Not.SameAs(actual[actual.Length - 1].previews));
            AssertPreviewMetricsEqual(actual[actual.Length - 1].previews, covered[covered.Length - 1].previews);
        }

        [Test]
        public void PreviewQualityCoverage_IsMonotonicAndDoesNotAddTimingFrameOrCount()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0));
            metrics.Add(ValidSample(1));
            var actual = metrics.Samples.Select(x => new HarnessPreviewQualitySample
            {
                sampleSeconds = x.sampleSeconds, programFrameNumber = x.programFrameNumber, previews = x.previews
            }).ToArray();
            var sampleCountBefore = metrics.Samples.Count;
            var presentedFramesBefore = metrics.PresentedFrames;
            var covered = HarnessPreviewQualityContract.AppendTerminalSample(actual, 600d);

            for (var index = 1; index < covered.Length; index++)
                Assert.That(covered[index].sampleSeconds, Is.GreaterThanOrEqualTo(covered[index - 1].sampleSeconds));
            Assert.That(covered[covered.Length - 1].sampleSeconds, Is.EqualTo(600d));
            Assert.That(metrics.Samples.Count, Is.EqualTo(sampleCountBefore));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(presentedFramesBefore));
        }

        [Test]
        public void PreviewQualityCoverage_SortsOutOfOrderDrainTailWithoutMutatingMetrics()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var index = 0; index < 4; index++) metrics.Add(ValidSample(index));
            var actual = new[]
            {
                QualitySample(599.955d, 37704UL, 0),
                QualitySample(599.971d, 37705UL, 0),
                QualitySample(599.988d, 37706UL, 0),
                // Delayed unresolved completion arrives after the later
                // presentation in _metrics.Samples completion order.
                QualitySample(599.921d, 37702UL, 0)
            };
            var sampleCountBefore = metrics.Samples.Count;
            var presentedFramesBefore = metrics.PresentedFrames;
            var covered = HarnessPreviewQualityContract.AppendTerminalSample(actual, 600d);

            Assert.That(covered.Length, Is.EqualTo(actual.Length + 1));
            for (var index = 1; index < covered.Length; index++)
                Assert.That(covered[index].sampleSeconds, Is.GreaterThanOrEqualTo(covered[index - 1].sampleSeconds));
            Assert.That(covered.Take(actual.Length).Select(x => x.sampleSeconds), Is.EqualTo(new[] { 599.921d, 599.955d, 599.971d, 599.988d }));
            Assert.That(covered[covered.Length - 1].sampleSeconds, Is.EqualTo(600d));
            Assert.That(covered[covered.Length - 1].programFrameNumber, Is.EqualTo(37706UL));
            AssertPreviewMetricsEqual(actual[2].previews, covered[covered.Length - 1].previews);
            Assert.That(metrics.Samples.Count, Is.EqualTo(sampleCountBefore));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(presentedFramesBefore));
        }

        [Test]
        public void PreviewQualityCoverage_EmptySamplesRemainEmpty()
        {
            Assert.That(HarnessPreviewQualityContract.AppendTerminalSample(null, 600d), Is.Empty);
            Assert.That(HarnessPreviewQualityContract.AppendTerminalSample(Array.Empty<HarnessPreviewQualitySample>(), 15d), Is.Empty);
        }

        [Test]
        public void Metrics_SingleSlowCadenceInterval_RemainsDiagnosticAndDoesNotGatePassFail()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0));
            var slowInterval = ValidSample(1, true, 2);
            slowInterval.sampleSeconds = 1d / 58.5d;
            metrics.Add(slowInterval);

            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);

            Assert.That(metrics.MinimumProgramCadenceFps, Is.LessThan(59d),
                "The single worst cadence interval remains available as diagnostic evidence.");
            Assert.That(result.Passed, Is.True, result.Failure);
        }

        [Test]
        public void Metrics_ThreeMissingFrames_Fail()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0));
            metrics.Add(ValidSample(1, false));
            metrics.Add(ValidSample(2, false));
            metrics.Add(ValidSample(3, false));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("consecutive"));
        }

        [Test]
        public void Metrics_SkippedProgramFrameNumbersCountAsMissingFrames()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0, true, 1));
            var resumed = ValidSample(4, true, 5);
            metrics.Add(resumed);
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("consecutive"));
            Assert.That(metrics.MaxConsecutiveMissing, Is.EqualTo(3));
        }

        [Test]
        public void Metrics_PresentedGpuTimingUnavailable_FailsThroughTheNinetyNinePercentRatio()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var index = 0; index < 100; index++)
            {
                var sample = ValidSample(index);
                if (index == 0 || index == 1) sample.gpuMilliseconds = double.NaN;
                metrics.Add(sample);
            }
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(100));
            Assert.That(metrics.TimingAvailableFrames, Is.EqualTo(98));
            Assert.That(metrics.TimingUnavailableFrames, Is.EqualTo(2));
            Assert.That(metrics.AverageCpuMilliseconds, Is.EqualTo(8d));
            Assert.That(metrics.MaxGpuMilliseconds, Is.EqualTo(7d));
        }

        [Test]
        public void Metrics_PartialUnavailableTimingRetainsIndependentCpuAndGpuStatistics()
        {
            var metrics = new HarnessMetricAccumulator();
            var cpuOnly = ValidSample(0);
            cpuOnly.gpuMilliseconds = double.NaN;
            var gpuOnly = ValidSample(1);
            gpuOnly.cpuMilliseconds = double.NaN;
            var complete = ValidSample(2);
            complete.cpuMilliseconds = 4d;
            complete.gpuMilliseconds = 5d;
            metrics.Add(cpuOnly);
            metrics.Add(gpuOnly);
            metrics.Add(complete);

            Assert.That(metrics.TimingAvailableFrames, Is.EqualTo(1));
            Assert.That(metrics.TimingUnavailableFrames, Is.EqualTo(2));
            Assert.That(metrics.AverageCpuMilliseconds, Is.EqualTo(6d));
            Assert.That(metrics.MaxCpuMilliseconds, Is.EqualTo(8d));
            Assert.That(metrics.AverageGpuMilliseconds, Is.EqualTo(6d));
            Assert.That(metrics.MaxGpuMilliseconds, Is.EqualTo(7d));
        }

        [Test]
        public void Metrics_OneUnavailableTimingOutOfOneHundredMeetsTheNinetyNinePercentThreshold()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var index = 0; index < 100; index++)
            {
                var sample = ValidSample(index);
                if (index == 37) sample.gpuMilliseconds = 0d;
                metrics.Add(sample);
            }
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.True, result.Failure);
            Assert.That(metrics.GoodFrames, Is.EqualTo(99));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(100));
            Assert.That(metrics.TimingAvailableFrames, Is.EqualTo(99));
            Assert.That(metrics.TimingUnavailableFrames, Is.EqualTo(1));
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(0.99d));
        }

        [Test]
        public void Metrics_TwoUnavailableTimingsOutOfOneHundredFailTheNinetyNinePercentThreshold()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var index = 0; index < 100; index++)
            {
                var sample = ValidSample(index);
                if (index == 37 || index == 61) sample.cpuMilliseconds = double.NaN;
                metrics.Add(sample);
            }
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
            Assert.That(metrics.GoodFrames, Is.EqualTo(98));
            Assert.That(metrics.TimingUnavailableFrames, Is.EqualTo(2));
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(0.98d));
        }

        [Test]
        public void Metrics_AllUnavailableTimingsFailWithoutTurningProgramFramesIntoMissingFrames()
        {
            var metrics = new HarnessMetricAccumulator();
            for (var index = 0; index < 4; index++)
            {
                var sample = ValidSample(index);
                sample.cpuMilliseconds = double.NaN;
                sample.gpuMilliseconds = double.NaN;
                metrics.Add(sample);
            }
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("99 percent"));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(4));
            Assert.That(metrics.MaxConsecutiveMissing, Is.EqualTo(0));
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(0d));
            Assert.That(double.IsNaN(metrics.AverageCpuMilliseconds), Is.True);
        }

        [Test]
        public void TimingDrain_UnresolvedPresentedFrameBecomesOneNaNBadSampleWithoutProgramMissing()
        {
            var tracker = new HarnessTimingCompletionTracker();
            tracker.BeginMeasurement(100UL);
            var presented = ValidSample(2, true, 102UL);
            Assert.That(tracker.RecordPresentation(102UL, presented), Is.True);

            var unresolved = tracker.DrainUncompleted();
            Assert.That(unresolved.Count, Is.EqualTo(1));
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(HarnessTimingCompletionTracker.MarkUnresolvedTimingUnavailable(unresolved[0]));

            Assert.That(metrics.PresentedFrames, Is.EqualTo(1));
            Assert.That(metrics.TimingUnavailableFrames, Is.EqualTo(1));
            Assert.That(metrics.GoodFrames, Is.EqualTo(0));
            Assert.That(metrics.MaxConsecutiveMissing, Is.EqualTo(0));
            Assert.That(tracker.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Metrics_NonPresentedMissingTimingDoesNotPollutePresentedStatistics()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0, true, 1));
            var updateOnly = ValidSample(1, false, 1);
            updateOnly.cpuMilliseconds = double.NaN;
            updateOnly.gpuMilliseconds = double.PositiveInfinity;
            metrics.Add(updateOnly);
            metrics.Add(ValidSample(1, true, 2));

            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.True, result.Failure);
            Assert.That(metrics.PresentedFrames, Is.EqualTo(2));
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(1d));
            Assert.That(metrics.AverageCpuMilliseconds, Is.EqualTo(8d));
            Assert.That(metrics.AverageGpuMilliseconds, Is.EqualTo(7d));
        }

        [Test]
        public void Metrics_NoPresentedProgramFrameFailsExplicitly()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0, false, 1));
            metrics.Add(ValidSample(1, false, 2));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("Presented Program"));
        }

        [Test]
        public void Metrics_LeakedSessionResource_Fails()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0));
            metrics.Add(ValidSample(1));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 1, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("resources"));
        }

        [Test]
        public void Metrics_ProgramDescriptorMismatch_Fails()
        {
            var metrics = new HarnessMetricAccumulator();
            var sample = ValidSample(0);
            sample.programWidth = 1280;
            metrics.Add(sample);
            metrics.Add(ValidSample(1));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("descriptor"));
        }

        [Test]
        public void Metrics_ProgramTargetCadenceMismatch_Fails()
        {
            var metrics = new HarnessMetricAccumulator();
            var sample = ValidSample(0);
            sample.programTargetFramesPerSecond = 59;
            metrics.Add(sample);
            metrics.Add(ValidSample(1));

            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("descriptor"));
        }

        [Test]
        public void Metrics_PreviewDescriptorMismatch_Fails()
        {
            var metrics = new HarnessMetricAccumulator();
            var sample = ValidSample(0);
            sample.previews[1].targetFramesPerSecond = 15;
            metrics.Add(sample);
            metrics.Add(ValidSample(1));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("Preview"));
        }

        [Test]
        public void Metrics_LowerPreviewQualityStages_AreAllowedWithoutProgramDegradation()
        {
            var metrics = new HarnessMetricAccumulator();
            var sample = ValidSample(0);
            sample.previews[0] = PreviewSample("preview1", 1, 1);
            sample.previews[1] = PreviewSample("preview2", 2, 2);
            metrics.Add(sample);
            metrics.Add(ValidSample(1));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.True, result.Failure);
        }

        [Test]
        public void Metrics_AllDocumentedPreviewQualityStages_AreValid()
        {
            for (var stage = 0; stage <= 4; stage++)
            {
                var metrics = new HarnessMetricAccumulator();
                var first = ValidSample(0);
                first.previews[0] = PreviewSample("preview1", stage, 1);
                first.previews[1] = PreviewSample("preview2", stage, 1);
                var second = ValidSample(1);
                second.previews[0] = PreviewSample("preview1", stage, 2);
                second.previews[1] = PreviewSample("preview2", stage, 2);
                metrics.Add(first);
                metrics.Add(second);
                var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
                Assert.That(result.Passed, Is.True, "Stage" + stage + ": " + result.Failure);
            }
        }

        [Test]
        public void Metrics_UnknownPreviewQualityStage_Fails()
        {
            var metrics = new HarnessMetricAccumulator();
            var sample = ValidSample(0);
            sample.previews[0].quality = "Stage9";
            sample.previews[0].qualityStage = 9;
            metrics.Add(sample);
            metrics.Add(ValidSample(1));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("quality stage"));
        }

        [Test]
        public void Metrics_RepeatedUpdatesForSameProgramFrameAreNotMissingFrames()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0, true, 1));
            var duplicateOne = ValidSample(1, false, 1);
            duplicateOne.cpuMilliseconds = 40;
            duplicateOne.gpuMilliseconds = 40;
            duplicateOne.sampleSeconds = 1d / 240d;
            metrics.Add(duplicateOne);
            var duplicateTwo = ValidSample(2, false, 1);
            duplicateTwo.sampleSeconds = 2d / 240d;
            metrics.Add(duplicateTwo);
            var duplicateThree = ValidSample(3, false, 1);
            duplicateThree.sampleSeconds = 3d / 240d;
            metrics.Add(duplicateThree);
            var nextFrame = ValidSample(4, true, 2);
            nextFrame.sampleSeconds = 4d / 240d;
            metrics.Add(nextFrame);
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0);
            Assert.That(result.Passed, Is.True, result.Failure);
            Assert.That(metrics.MaxConsecutiveMissing, Is.EqualTo(0));
            Assert.That(metrics.PresentedFrames, Is.EqualTo(2));
            Assert.That(metrics.GoodFrameRatio, Is.EqualTo(1d));
            Assert.That(metrics.AverageCpuMilliseconds, Is.EqualTo(8d));
            Assert.That(metrics.AverageGpuMilliseconds, Is.EqualTo(7d));
        }

        [Test]
        public void Metrics_PoolWarningOrHighWaterExceeded_Fails()
        {
            var warning = ValidSample(0);
            warning.poolBudgetWarning = true;
            var warningMetrics = new HarnessMetricAccumulator();
            warningMetrics.Add(warning);
            warningMetrics.Add(ValidSample(1));
            Assert.That(HarnessMetricEvaluator.Evaluate(warningMetrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0).Failure, Does.Contain("budget"));

            var highWater = ValidSample(0);
            highWater.poolHighWaterBytes = 1001;
            var highWaterMetrics = new HarnessMetricAccumulator();
            highWaterMetrics.Add(highWater);
            highWaterMetrics.Add(ValidSample(1));
            Assert.That(HarnessMetricEvaluator.Evaluate(highWaterMetrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000, 0, 0, 0, 0, 0).Failure, Does.Contain("budget"));
        }

        [Test]
        public void Diagnostics_ResetRebasesActiveFaultWithoutHidingCurrent()
        {
            var observation = HarnessDiagnosticContract.ObserveResetRebase();
            Assert.That(observation.CurrentCount, Is.EqualTo(1));
            Assert.That(observation.HistoryCount, Is.EqualTo(1));
            Assert.That(observation.FirstFrame, Is.EqualTo(10));
            Assert.That(observation.AggregateCount, Is.EqualTo(2));
            Assert.That(observation.LastFrame, Is.EqualTo(11));
        }

        [Test]
        public void Diagnostics_IntervalsCaptureStartEndDuration()
        {
            var metrics = new HarnessMetricAccumulator();
            var first = ValidSample(0);
            first.faulted = true;
            first.holdingLastFrame = true;
            metrics.Add(first);
            var second = ValidSample(1);
            second.faulted = true;
            second.holdingLastFrame = true;
            metrics.Add(second);
            metrics.Add(ValidSample(2));
            metrics.CompleteIntervals(0.5d);
            var fault = metrics.Intervals.First(x => x.kind == "Fault");
            Assert.That(fault.startSeconds, Is.EqualTo(0d));
            Assert.That(fault.endSeconds, Is.EqualTo(2d / 60d).Within(0.0001d));
            Assert.That(fault.durationSeconds, Is.EqualTo(2d / 60d).Within(0.0001d));
            Assert.That(fault.samples, Is.EqualTo(2));
        }

        [Test]
        public void ScenarioTopology_VideoMustReachProgram()
        {
            var nodes = new[]
            {
                new HarnessTopologyNode("3d", "shitdesigner.scene.3d"), new HarnessTopologyNode("2d", "shitdesigner.scene.2d"),
                new HarnessTopologyNode("effect", "shitdesigner.shader.effect"), new HarnessTopologyNode("video", "shitdesigner.video.player"),
                new HarnessTopologyNode("blendA", "shitdesigner.shader.blend2"), new HarnessTopologyNode("blendB", "shitdesigner.shader.blend2"),
                new HarnessTopologyNode("feedback", "system.feedback"), new HarnessTopologyNode("program", "system.program_output"),
                new HarnessTopologyNode("preview1", "system.preview"), new HarnessTopologyNode("preview2", "system.preview")
            };
            var edges = new[]
            {
                new HarnessTopologyEdge("3d", "blendA"), new HarnessTopologyEdge("2d", "blendA"), new HarnessTopologyEdge("blendA", "blendB"),
                new HarnessTopologyEdge("video", "blendB"), new HarnessTopologyEdge("blendB", "effect"), new HarnessTopologyEdge("effect", "feedback"),
                new HarnessTopologyEdge("feedback", "program"), new HarnessTopologyEdge("video", "preview1"), new HarnessTopologyEdge("video", "preview2")
            };
            Assert.That(HarnessScenarioTopology.Validate(nodes, edges), Is.Empty);
            var videoOnlyPreview = edges.Where(x => !(x.SourceId == "video" && x.DestinationId == "blendB"))
                .Concat(new[] { new HarnessTopologyEdge("video", "preview1"), new HarnessTopologyEdge("video", "preview2") }).ToArray();
            Assert.That(HarnessScenarioTopology.Validate(nodes, videoOnlyPreview), Does.Contain("video.player"));
        }

        [Test]
        public void OwnershipSnapshot_LeakAndDescriptorMismatch_AreObservable()
        {
            var before = HarnessOwnershipContract.CreateTestSnapshot(2, 2, 1, 1, 1, 1920, 1080, "R16G16B16A16_SFloat", 60, false);
            var after = HarnessOwnershipContract.CreateTestSnapshot(1, 0, 0, 0, 0, 1920, 1080, "R16G16B16A16_SFloat", 60, true);
            Assert.That(HarnessOwnershipContract.ValidateTeardown(after), Does.Contain("resources"));
            Assert.That(HarnessOwnershipContract.ValidateActiveDescriptors(before), Is.Empty);
            var lowerPreviewQuality = new ProductionOwnershipSnapshot(null, 0, 0, 0, 0, 3,
                new ProductionSurfaceOwnershipSnapshot("program", "Program", 1920, 1080, "R16G16B16A16_SFloat", 60, 2),
                new[]
                {
                    new ProductionSurfaceOwnershipSnapshot("preview1", "Preview", 320, 180, "R8G8B8A8_UNorm", 20, 2),
                    new ProductionSurfaceOwnershipSnapshot("preview2", "Preview", 160, 90, "R8G8B8A8_UNorm", 10, 2)
                }, false);
            Assert.That(HarnessOwnershipContract.ValidateActiveDescriptors(lowerPreviewQuality), Is.Empty);
            var wrongProgram = HarnessOwnershipContract.CreateTestSnapshot(2, 2, 1, 1, 1, 1280, 720, "R16G16B16A16_SFloat", 60, false);
            Assert.That(HarnessOwnershipContract.ValidateActiveDescriptors(wrongProgram), Does.Contain("Program"));
        }

        [Test]
        public void Options_Defaults_AreProductionConditions()
        {
            var options = HarnessOptions.Parse(new string[0]);
            Assert.That(options.Codec, Is.EqualTo(HarnessCodec.H264));
            Assert.That(options.WarmupSeconds, Is.EqualTo(30d));
            Assert.That(options.MeasureSeconds, Is.EqualTo(600d));
            Assert.That(options.FixtureMode, Is.False);
        }

        [Test]
        public void Options_FixtureFlag_IsExplicitAndDoesNotChangeProductionDefaults()
        {
            var options = HarnessOptions.Parse(new[] { "-sdHarnessFixtureMode" });
            Assert.That(options.FixtureMode, Is.True);
            Assert.That(options.HasDurationOverrides, Is.False);
            Assert.That(HarnessOptions.Parse(new[] { "-sdHarnessFixtureMode", "-sdHarnessMeasureSeconds", "3" }).HasDurationOverrides, Is.True);
        }

        [Test]
        public void AcceptanceOptions_ParseModeStageFixtureProjectAndFingerprint()
        {
            var options = HarnessOptions.Parse(new[]
            {
                "-sdHarnessMode", "acceptance", "-sdHarnessStage", "recovery",
                "-sdHarnessFixtureRoot", "fixtures", "-sdHarnessProjectRoot", "project",
                "-sdHarnessExpectedFingerprint", "fingerprint",
                "-sdHarnessExpectedBackupFingerprint", "backup-fingerprint"
            });
            Assert.That(options.Mode, Is.EqualTo(HarnessMode.Acceptance));
            Assert.That(options.AcceptanceStage, Is.EqualTo(HarnessAcceptanceStage.Recovery));
            Assert.That(options.FixtureRoot, Is.EqualTo("fixtures"));
            Assert.That(options.ProjectRoot, Is.EqualTo("project"));
            Assert.That(options.ExpectedFingerprint, Is.EqualTo("fingerprint"));
            Assert.That(options.ExpectedBackupFingerprint, Is.EqualTo("backup-fingerprint"));
        }

        [Test]
        public void AcceptanceFixture_MissingManifestIsEnvironmentFailure()
        {
            var result = AcceptanceFixtureValidator.Validate(Path.Combine(Path.GetTempPath(), "ShitDesignerAcceptance-" + System.Guid.NewGuid().ToString("N")));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error, Does.Contain("manifest"));
        }

        [Test]
        public void AcceptanceFixture_MissingRequiredCodecIsEnvironmentFailure()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerAcceptance-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":1,\"fixtures\":[]}");
                var result = AcceptanceFixtureValidator.Validate(root);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Error, Does.Contain("codec H264"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void AcceptanceFixture_EnforcesH264AudioAndVp8AlphaMetadata()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerAcceptance-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":1,\"fixtures\":[{\"codec\":\"H264\",\"file\":\"h.mp4\",\"width\":1920,\"height\":1080,\"fps\":60,\"hasAudio\":false,\"probe\":\"Supported\",\"bytes\":1,\"xxh3_128\":\"00000000000000000000000000000000\"}]}");
                var result = AcceptanceFixtureValidator.Validate(root);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Error, Does.Contain("metadata"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void AcceptanceContract_InitialRequiresAllCodecProbeAndFrameResults()
        {
            var artifact = new HarnessAcceptanceArtifact
            {
                mode = "acceptance", stage = "Initial", productionCompositionUsed = true, productionCatalogUsed = true,
                presentationRootAvailable = true, programAndPreviewsReady = true, editorAssemblyExcluded = true,
                fileProjectReadable = true, fileProjectWritable = true,
                persistence = new HarnessAcceptancePersistenceArtifact { saved = true, fingerprint = "abc", backupFingerprint = "backup", backupReadable = true },
                fixtures = new HarnessAcceptanceFixtureArtifact[0]
            };
            Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Initial, artifact), Does.Contain("fixture"));
        }

        [Test]
        public void AcceptanceContract_ManualDisplayIsNeverAnAutomatedPassCondition()
        {
            var artifact = new HarnessAcceptanceArtifact
            {
                mode = "acceptance", stage = "Reopen", productionCompositionUsed = true, productionCatalogUsed = true,
                presentationRootAvailable = true, programAndPreviewsReady = true, editorAssemblyExcluded = true,
                fileProjectReadable = true, fileProjectWritable = true,
                persistence = new HarnessAcceptancePersistenceArtifact { reopened = true, fingerprint = "abc", expectedFingerprint = "abc" },
                fixtures = new HarnessAcceptanceFixtureArtifact[0]
            };
            Assert.That(artifact.manualDisplayCheck, Is.EqualTo("manual-required"));
            Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Reopen, artifact), Is.Empty);
        }

        [Test]
        public void AcceptanceContract_RecoveryRequiresKnownBackupFingerprintAndRecoveryFileAccess()
        {
            var artifact = new HarnessAcceptanceArtifact
            {
                mode = "acceptance", stage = "Recovery", productionCompositionUsed = true, productionCatalogUsed = true,
                editorAssemblyExcluded = true, presentationRootAvailable = true, programAndPreviewsReady = true,
                fileProjectReadable = false, fileProjectWritable = true, backupFileReadable = true,
                persistence = new HarnessAcceptancePersistenceArtifact
                {
                    recovered = true, dirtyAfterRecovery = true, mainFilePreservedAfterRecovery = true,
                    fingerprint = "loaded-from-backup", expectedFingerprint = "different",
                    backupFingerprint = "loaded-from-backup", expectedBackupFingerprint = "different"
                }
            };
            Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Recovery, artifact), Does.Contain("fingerprint"));
        }

        [Test]
        public void AcceptanceContract_RejectsStaleCodecFrameEvenWhenNodeIsReady()
        {
            var codecs = new[] { "H264", "VP8", "Hap1", "Hap5", "HapY", "HapM" };
            var artifact = new HarnessAcceptanceArtifact
            {
                mode = "acceptance", stage = "Initial", productionCompositionUsed = true, productionCatalogUsed = true,
                editorAssemblyExcluded = true, presentationRootAvailable = true, programAndPreviewsReady = true,
                fileProjectReadable = true, fileProjectWritable = true,
                persistence = new HarnessAcceptancePersistenceArtifact { saved = true, fingerprint = "main", backupFingerprint = "backup", backupReadable = true },
                fixtures = codecs.Select(codec => new HarnessAcceptanceFixtureArtifact
                {
                    codec = codec, probePassed = true, prepareObserved = true, mediaBindingApplied = true,
                    frameBefore = 10, frameAfter = 10, previewFrameBefore = 10, previewFrameAfter = 11, frameReady = true
                }).ToArray()
            };
            Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Initial, artifact), Does.Contain("fixture"));
        }

        [Test]
        public void AcceptanceContract_InitialRequiresEveryFixtureOwnershipAndPublicOutputEvidence()
        {
            foreach (var missingEvidence in new[] { "ownership", "output", "real" })
            {
                var artifact = CompleteInitialFixtureArtifact();
                Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Initial, artifact), Is.Empty,
                    "The complete fixture baseline must satisfy the Initial acceptance contract.");
                var fixture = artifact.fixtures[0];
                if (missingEvidence == "ownership") fixture.ownershipFramesObserved = false;
                else if (missingEvidence == "output") fixture.outputReadyObserved = false;
                else fixture.realFrameObserved = false;

                Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Initial, artifact), Does.Contain("fixture"),
                    "Initial artifact must reject a fixture missing " + missingEvidence + " evidence.");
            }
        }

        [Test]
        public void AcceptanceContract_OutputsRequirePresentedProgramAndDemandedPreviewDescriptors()
        {
            var program = new ApplicationOutputSurfaceReadModel("program", "Program", "Available", 1920, 1080, "Fit", "Black", "Project", true, false);
            var preview1 = new ApplicationOutputSurfaceReadModel("preview-1", "Preview", "Available", 640, 360, "Fit", "Black", "Project", true, false);
            var preview2 = new ApplicationOutputSurfaceReadModel("preview-2", "Preview", "Available", 640, 360, "Fit", "Black", "Project", true, false);
            Assert.That(AcceptanceContract.OutputsReady(new ApplicationOutputReadModel(1, "Available", false, program, new[] { preview1, preview2 })), Is.True);
            Assert.That(AcceptanceContract.OutputsReady(new ApplicationOutputReadModel(1, "Available", false, program, new[] { preview1 })), Is.False);
            Assert.That(AcceptanceContract.OutputsReady(new ApplicationOutputReadModel(1, "Preparing", false,
                new ApplicationOutputSurfaceReadModel("program", "Program", "Preparing", 1920, 1080, "Fit", "Black", "Project", true, false), new[] { preview1, preview2 })), Is.False);
        }

        [Test]
        public void AcceptanceOutputs_RequireVideoBindingBeforeTheRequiredTopologyCanBecomeReady()
        {
            const string videoNodeId = "video-node";
            var program = new ApplicationOutputSurfaceReadModel("program", "Program", "Available", 1920, 1080, "Fit", "Black", "Project", true, false);
            var preview1 = new ApplicationOutputSurfaceReadModel("preview-1", "Preview", "Available", 640, 360, "Fit", "Black", "Project", true, false);
            var preview2 = new ApplicationOutputSurfaceReadModel("preview-2", "Preview", "Available", 640, 360, "Fit", "Black", "Project", true, false);
            var output = new ApplicationOutputReadModel(1, "Available", false, program, new[] { preview1, preview2 });
            var unbound = new[]
            {
                new ApplicationParameterReadModel("media", videoNodeId, VideoPlayerContract.MediaAssetParameterId, "Media", string.Empty, string.Empty),
                new ApplicationParameterReadModel("playing", videoNodeId, VideoPlayerContract.PlayingParameterId, "Playing", "False", "False"),
                new ApplicationParameterReadModel("loop", videoNodeId, VideoPlayerContract.LoopParameterId, "Loop", "True", "True")
            };
            var bound = new[]
            {
                new ApplicationParameterReadModel("media", videoNodeId, VideoPlayerContract.MediaAssetParameterId, "Media", "media-asset", "media-asset"),
                new ApplicationParameterReadModel("playing", videoNodeId, VideoPlayerContract.PlayingParameterId, "Playing", "True", "True"),
                new ApplicationParameterReadModel("loop", videoNodeId, VideoPlayerContract.LoopParameterId, "Loop", "True", "True")
            };

            Assert.That(AcceptanceContract.OutputsReadyAfterVideoBinding(output, unbound, videoNodeId), Is.False);
            Assert.That(AcceptanceContract.OutputsReadyAfterVideoBinding(output, bound, videoNodeId), Is.True);
        }

        [Test]
        public void AcceptanceOutputEvidence_IsCapturedFromOneBoundPublicSnapshotAndNeverErasedByLaterTransition()
        {
            const string videoNodeId = "video-node";
            var program = new ApplicationOutputSurfaceReadModel("program", "Program", "Available", 1920, 1080, "Fit", "Black", "Project", true, false);
            var preview1 = new ApplicationOutputSurfaceReadModel("preview-1", "Preview", "Available", 640, 360, "Fit", "Black", "Project", true, false);
            var preview2 = new ApplicationOutputSurfaceReadModel("preview-2", "Preview", "HoldingLastFrame", 640, 360, "Fit", "Black", "Project", true, true);
            var presented = new ApplicationOutputReadModel(17, "Available", false, program, new[] { preview1, preview2 });
            var holding = new ApplicationOutputReadModel(18, "HoldingLastFrame", true,
                new ApplicationOutputSurfaceReadModel("program", "Program", "HoldingLastFrame", 1920, 1080, "Fit", "Black", "Project", true, true), new[] { preview1, preview2 });
            var bound = new[]
            {
                new ApplicationParameterReadModel("media", videoNodeId, VideoPlayerContract.MediaAssetParameterId, "Media", "media-asset", "media-asset"),
                new ApplicationParameterReadModel("playing", videoNodeId, VideoPlayerContract.PlayingParameterId, "Playing", "True", "True"),
                new ApplicationParameterReadModel("loop", videoNodeId, VideoPlayerContract.LoopParameterId, "Loop", "True", "True")
            };
            var unbound = new[]
            {
                new ApplicationParameterReadModel("media", videoNodeId, VideoPlayerContract.MediaAssetParameterId, "Media", string.Empty, string.Empty),
                new ApplicationParameterReadModel("playing", videoNodeId, VideoPlayerContract.PlayingParameterId, "Playing", "False", "False"),
                new ApplicationParameterReadModel("loop", videoNodeId, VideoPlayerContract.LoopParameterId, "Loop", "True", "True")
            };

            Assert.That(AcceptanceContract.RealPresentedFrame(presented), Is.True);
            Assert.That(AcceptanceContract.RealPresentedFrame(holding), Is.False, "Program HoldingLastFrame is output readiness, not new-frame evidence.");
            var outputsObserved = AcceptanceContract.ObserveOutputsReadyAfterVideoBinding(false, presented, bound, videoNodeId);
            var realObserved = AcceptanceContract.ObserveRealPresentedFrame(false, presented);
            Assert.That(outputsObserved, Is.True, "The same bound public snapshot must prove the required outputs.");
            Assert.That(realObserved, Is.True, "The same public snapshot must prove a real Program presentation.");
            Assert.That(AcceptanceContract.ObserveOutputsReadyAfterVideoBinding(outputsObserved, holding, unbound, videoNodeId), Is.True, "A later fixture transition must not erase bound-output evidence.");
            Assert.That(AcceptanceContract.ObserveRealPresentedFrame(realObserved, holding), Is.True, "A later transient read-model snapshot must not erase the observed real frame.");
            Assert.That(AcceptanceContract.FixtureFrameEvidenceObserved(true, false, false), Is.False, "Ownership frame increments alone must not complete a fixture.");
            Assert.That(AcceptanceContract.FixtureFrameEvidenceObserved(true, true, false), Is.False, "HoldingLastFrame output readiness is not real-frame evidence.");
            Assert.That(AcceptanceContract.FixtureFrameEvidenceObserved(true, outputsObserved, realObserved), Is.True, "A later public Available snapshot may complete evidence that ownership frames observed earlier in the same fixture.");
            Assert.That(AcceptanceContract.FixtureFrameEvidenceObserved(false, outputsObserved, realObserved), Is.False, "A public real frame cannot complete the fixture before its concrete Program and Preview frame increments are observed.");
        }

        [Test]
        public void AcceptanceVideoPrepare_RequiresObservedPreparingBeforePlaybackBinding()
        {
            const string videoNodeId = "video-node";
            var preparing = new ApplicationGraphReadModel(new[]
            {
                new ApplicationGraphNodeReadModel(videoNodeId, VideoPlayerContract.NodeTypeId, "Video", 0, 0, "Preparing")
            });
            var alreadyReady = new ApplicationGraphReadModel(new[]
            {
                new ApplicationGraphNodeReadModel(videoNodeId, VideoPlayerContract.NodeTypeId, "Video", 0, 0, "Ready")
            });

            Assert.That(AcceptanceContract.VideoPrepareObserved(preparing, videoNodeId), Is.True);
            Assert.That(AcceptanceContract.VideoPrepareObserved(alreadyReady, videoNodeId), Is.False, "Ready after an instantaneous completion is not retroactive Preparing evidence.");
            Assert.That(AcceptanceContract.CanStartVideoPlaybackAfterPrepare(false, true), Is.False);
            Assert.That(AcceptanceContract.CanStartVideoPlaybackAfterPrepare(true, false), Is.False);
            Assert.That(AcceptanceContract.CanStartVideoPlaybackAfterPrepare(true, true), Is.True);
        }

        [Test]
        public void VideoTransportBinding_UsesCatalogIdsAndExplicitPlaybackState()
        {
            var video = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == VideoPlayerContract.NodeTypeId);
            var ids = video.Parameters.Select(x => x.Id.Value).ToArray();
            Assert.That(ids, Does.Contain(VideoPlayerContract.MediaAssetParameterId));
            Assert.That(ids, Does.Contain(VideoPlayerContract.PlayingParameterId));
            Assert.That(ids, Does.Contain(VideoPlayerContract.LoopParameterId));
            Assert.That(video.Parameters.Single(x => x.Id.Value == VideoPlayerContract.MediaAssetParameterId).Type, Is.EqualTo(ParameterType.MediaAssetReference));
            Assert.That(video.Parameters.Single(x => x.Id.Value == VideoPlayerContract.PlayingParameterId).DefaultValue.AsBool(), Is.False);
            Assert.That(video.Parameters.Single(x => x.Id.Value == VideoPlayerContract.LoopParameterId).DefaultValue.AsBool(), Is.True);
            Assert.That(HarnessVideoTransportContract.RequiredParameterIds, Is.EquivalentTo(new[]
            {
                VideoPlayerContract.MediaAssetParameterId,
                VideoPlayerContract.PlayingParameterId,
                VideoPlayerContract.LoopParameterId
            }));
        }

        [Test]
        public void VideoTransportBinding_RequiresBaseAndEffectiveValuesToBeApplied()
        {
            const string nodeId = "video-node";
            const string mediaId = "media-asset";
            var parameters = new[]
            {
                new ApplicationParameterReadModel("media", nodeId, VideoPlayerContract.MediaAssetParameterId, "Media", mediaId, mediaId),
                new ApplicationParameterReadModel("playing", nodeId, VideoPlayerContract.PlayingParameterId, "Playing", "True", "True"),
                new ApplicationParameterReadModel("loop", nodeId, VideoPlayerContract.LoopParameterId, "Loop", "True", "True")
            };
            Assert.That(HarnessVideoTransportContract.IsApplied(parameters, nodeId, mediaId), Is.True);
            var pending = parameters.ToArray();
            pending[1] = new ApplicationParameterReadModel("playing", nodeId, VideoPlayerContract.PlayingParameterId, "Playing", "True", "False");
            Assert.That(HarnessVideoTransportContract.IsApplied(pending, nodeId, mediaId), Is.False);
        }

        [Test]
        public void MediaImportTask_UsesPublicProbeConfirmationStageAndWaitsForCompleted()
        {
            var waiting = new ApplicationTaskReadModel(Guid.NewGuid(), "MediaImport", "ProbeConfirmation", "Waiting");
            Assert.That(HarnessMediaImportContract.RequiresProbeConfirmation(waiting), Is.True);
            Assert.That(HarnessMediaImportContract.ShouldConfirmProbe(waiting, false), Is.True);
            Assert.That(HarnessMediaImportContract.ShouldConfirmProbe(waiting, true), Is.False);
            Assert.That(HarnessMediaImportContract.IsCompleted(waiting), Is.False);
            Assert.That(HarnessMediaImportContract.IsFailed(waiting), Is.False);

            // The internal transaction enum must never be consumed as a
            // public read-model stage by a standalone harness.
            var internalStage = new ApplicationTaskReadModel(Guid.NewGuid(), "MediaImport", "AwaitingProbeConfirmation", "Waiting");
            Assert.That(HarnessMediaImportContract.RequiresProbeConfirmation(internalStage), Is.False);

            var completed = new ApplicationTaskReadModel(Guid.NewGuid(), "MediaImport", "Register", "Completed");
            Assert.That(HarnessMediaImportContract.RequiresProbeConfirmation(completed), Is.False);
            Assert.That(HarnessMediaImportContract.IsCompleted(completed), Is.True);
        }

        [Test]
        public void Warmup_ReadinessRequiresShaderVideoFrameAndInitialTextures()
        {
            var pending = HarnessWarmupEvaluator.Evaluate(new HarnessWarmupObservation(true, true, false, true));
            Assert.That(pending.IsReady, Is.False);
            Assert.That(pending.IsFailure, Is.False);
            Assert.That(pending.Reason, Does.Contain("video frame ready"));

            var ready = HarnessWarmupEvaluator.Evaluate(new HarnessWarmupObservation(true, true, true, true));
            Assert.That(ready.IsReady, Is.True);
            Assert.That(ready.IsFailure, Is.False);
        }

        [Test]
        public void Warmup_TerminalFaultFailsInsteadOfWaitingForever()
        {
            var result = HarnessWarmupEvaluator.Evaluate(new HarnessWarmupObservation(false, false, false, false, true, "video.prepare: decoder fault"));
            Assert.That(result.IsReady, Is.False);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Reason, Does.Contain("decoder fault"));
        }

        [Test]
        public void Warmup_InitialPreviewBlockedWaitsForDemandedPresentationWithoutAcceptingMissingDescriptors()
        {
            Assert.That(HarnessWarmupEvaluator.IsTerminalNodeFailure("system.preview", "Blocked"), Is.False,
                "A Preview outside its update interval is not a terminal runtime failure.");
            Assert.That(HarnessWarmupEvaluator.IsTerminalNodeFailure("shitdesigner.video.player", "Blocked"), Is.True);
            Assert.That(HarnessWarmupEvaluator.IsTerminalNodeFailure("system.preview", "Faulted"), Is.True);

            var pending = HarnessWarmupEvaluator.Evaluate(new HarnessWarmupObservation(true, true, true, false));
            Assert.That(pending.IsReady, Is.False);
            Assert.That(pending.IsFailure, Is.False);
            Assert.That(pending.Reason, Does.Contain("initial textures"),
                "Warm-up remains strict until both 640x360 Preview descriptors are allocated and exposed.");
        }

        [Test]
        public void ArtifactWriter_WritesJsonAndText()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarnessArtifact-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifact = new HarnessArtifact
                {
                    runId = "test", mode = "acceptance", status = "Failed", failure = "original",
                    timing = new HarnessTimingArtifact
                    {
                        updateSamples = 3, measuredFrames = 2, presentedFrames = 2, timingAvailableFrames = 1, timingUnavailableFrames = 1,
                        frameTimingGateStartPerformanceFrame = 40UL,
                        frameTimingGateReadyPerformanceFrame = 44UL,
                        frameTimingGateWaitSeconds = 0.25d,
                        frameTimingSource = new HarnessFrameTimingSourceArtifact
                        {
                            rawCount = 16, rawIdentity = 42d, rawCpuMilliseconds = double.NaN, rawGpuMilliseconds = 0d,
                            pendingBefore = 16, pendingAfter = 15, outcome = "RawInvalid", candidateOutcome = "RawInvalid",
                            performanceFrameNumber = 41UL, exceptionType = ""
                        },
                        previewQualitySamples = new[]
                        {
                            new HarnessPreviewQualitySample { sampleSeconds = 1d, programFrameNumber = 2,
                            previews = new[] { PreviewSample("preview1", 2, 2), PreviewSample("preview2", 3, 2) } }
                        }
                    },
                    acceptance = new HarnessAcceptanceArtifact
                    {
                        persistence = new HarnessAcceptancePersistenceArtifact
                        {
                            fingerprint = "fingerprint", expectedFingerprint = "fingerprint",
                            fingerprintComponents = "project=1;graph=2;parameters=3;controls=4;presets=5;dashboard=6;previews=7;media=8",
                            expectedFingerprintComponents = "project=1;graph=2;parameters=3;controls=4;presets=5;dashboard=6;previews=7;media=8"
                        },
                        uiSave = new HarnessAcceptanceUiSaveArtifact
                        {
                            taskAfterId = "save-task", taskAfterKind = "Save", taskAfterStage = "Failed", taskAfterStatus = "Failed", taskAfterPath = "C:/acceptance/project",
                            taskAfterDiagnosticCode = "persistence.json_invalid", taskAfterDiagnosticMessage = "readback\nrejected",
                            taskAfterExceptionType = "System.IO.InvalidDataException", taskAfterExceptionMessage = "required value\nwas null", taskAfterExceptionStackTrace = "stack\ntrace"
                        },
                        fixtures = new[]
                        {
                            new HarnessAcceptanceFixtureArtifact { codec = "VP8", ownershipFramesObserved = true, outputReadyObserved = true, realFrameObserved = true, frameReady = true }
                        },
                        lastOutput = new HarnessAcceptanceOutputArtifact
                        {
                            frameNumber = 42, programState = "HoldingLastFrame", programWidth = 1920, programHeight = 1080, programReason = "fixture transition",
                            previews = new[]
                            {
                                new HarnessAcceptanceOutputSurfaceArtifact { id = "preview-1", state = "Available", width = 640, height = 360, demanded = true, reason = string.Empty },
                                new HarnessAcceptanceOutputSurfaceArtifact { id = "preview-2", state = "HoldingLastFrame", width = 640, height = 360, demanded = true, reason = "fixture transition" }
                            }
                        }
                    }
                };
                var result = HarnessArtifactWriter.Write(root, artifact);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(File.Exists(result.JsonPath), Is.True);
                Assert.That(File.Exists(result.TextPath), Is.True);
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("original"));
                Assert.That(File.ReadAllText(result.JsonPath), Does.Contain("Stage2"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("previewQualitySamples"));
                var written = JsonUtility.FromJson<HarnessArtifact>(File.ReadAllText(result.JsonPath));
                Assert.That(written?.acceptance?.uiSave, Is.Not.Null, "Acceptance Save diagnostics must be retained in JSON.");
                Assert.That(written.acceptance.uiSave.taskAfterStage, Is.EqualTo("Failed"));
                Assert.That(written.acceptance.uiSave.taskAfterPath, Is.EqualTo("C:/acceptance/project"));
                Assert.That(written.acceptance.uiSave.taskAfterDiagnosticCode, Is.EqualTo("persistence.json_invalid"));
                Assert.That(written.acceptance.uiSave.taskAfterDiagnosticMessage, Is.EqualTo("readback\nrejected"));
                Assert.That(written.acceptance.uiSave.taskAfterExceptionType, Is.EqualTo("System.IO.InvalidDataException"));
                Assert.That(written.acceptance.uiSave.taskAfterExceptionMessage, Is.EqualTo("required value\nwas null"));
                Assert.That(written.acceptance.uiSave.taskAfterExceptionStackTrace, Is.EqualTo("stack\ntrace"));
                Assert.That(written.acceptance.persistence.fingerprintComponents, Does.Contain("previews=7"), "Component-level Canonical Project evidence must survive JSON serialization.");
                Assert.That(written.acceptance.fixtures.Single().ownershipFramesObserved, Is.True);
                Assert.That(written.acceptance.fixtures.Single().outputReadyObserved, Is.True);
                Assert.That(written.acceptance.fixtures.Single().realFrameObserved, Is.True);
                Assert.That(written.acceptance.lastOutput.programState, Is.EqualTo("HoldingLastFrame"));
                Assert.That(written.acceptance.lastOutput.previews[1].reason, Is.EqualTo("fixture transition"));
                Assert.That(written.timing.frameTimingGateStartPerformanceFrame, Is.EqualTo(40UL));
                Assert.That(written.timing.frameTimingGateReadyPerformanceFrame, Is.EqualTo(44UL));
                Assert.That(written.timing.timingAvailableFrames, Is.EqualTo(1));
                Assert.That(written.timing.timingUnavailableFrames, Is.EqualTo(1));
                Assert.That(written.timing.frameTimingSource.outcome, Is.EqualTo("RawInvalid"));
                Assert.That(written.timing.frameTimingSource.rawCount, Is.EqualTo(16));
                Assert.That(double.IsNaN(written.timing.frameTimingSource.rawCpuMilliseconds), Is.True);
                Assert.That(written.timing.frameTimingSource.performanceFrameNumber, Is.EqualTo(41UL));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("taskAfter=save-task/Save/Failed/Failed:path=C:/acceptance/project:diagnosticCode=persistence.json_invalid"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("diagnosticMessage=readback\\nrejected"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("exceptionStack=stack\\ntrace"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("acceptanceFingerprintComponents=project=1;graph=2"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("acceptanceLastOutput=frame=42:program=HoldingLastFrame@1920x1080:reason=fixture transition"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("frameTimingGateStartPerformanceFrame=40"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("timingAvailableFrames=1"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("timingUnavailableFrames=1"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("frameTimingSource=outcome=RawInvalid;candidate=RawInvalid;rawCount=16"));
                Assert.That(HarnessArtifactWriter.GetExitCode(artifact, result), Is.EqualTo(1));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ArtifactWriter_PreservesEachRawFrameTimingMeaning()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarnessRawTiming-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifact = new HarnessArtifact
                {
                    runId = "raw-timing",
                    status = HarnessRunStatus.Failed.ToString(),
                    timing = new HarnessTimingArtifact
                    {
                        frameTimingSource = new HarnessFrameTimingSourceArtifact
                        {
                            rawCount = 1,
                            rawIdentity = 301d,
                            rawCpuMilliseconds = 20d,
                            rawCpuFrameTimeMilliseconds = 20d,
                            rawCpuMainThreadFrameTimeMilliseconds = 8d,
                            rawCpuRenderThreadFrameTimeMilliseconds = 10d,
                            rawCpuMainThreadPresentWaitMilliseconds = 12d,
                            rawGpuMilliseconds = 7d,
                            outcome = "Completed",
                            candidateOutcome = "Completed",
                            performanceFrameNumber = 42UL
                        }
                    }
                };

                var result = HarnessArtifactWriter.Write(root, artifact);
                Assert.That(result.Success, Is.True, result.Error);
                var written = JsonUtility.FromJson<HarnessArtifact>(File.ReadAllText(result.JsonPath));
                var timing = written.timing.frameTimingSource;
                Assert.That(timing.rawCpuFrameTimeMilliseconds, Is.EqualTo(20d));
                Assert.That(timing.rawCpuMainThreadFrameTimeMilliseconds, Is.EqualTo(8d));
                Assert.That(timing.rawCpuRenderThreadFrameTimeMilliseconds, Is.EqualTo(10d));
                Assert.That(timing.rawCpuMainThreadPresentWaitMilliseconds, Is.EqualTo(12d));
                Assert.That(timing.rawGpuMilliseconds, Is.EqualTo(7d));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("cpuFrameTime=20"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("cpuMainThread=8"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("cpuRenderThread=10"));
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("cpuPresentWait=12"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ArtifactWriter_FailureDoesNotReplaceOriginalFailure()
        {
            var artifact = new HarnessArtifact { runId = "failure-preservation", status = "Failed", failure = "original-test-failure" };
            var result = HarnessArtifactWriter.Write(string.Empty, artifact);
            Assert.That(result.Success, Is.False);
            Assert.That(artifact.failure, Is.EqualTo("original-test-failure"));
            Assert.That(HarnessArtifactWriter.GetExitCode(artifact, result), Is.EqualTo(1));
        }

        [Test]
        public void ArtifactWriter_FailureForPassedRunForcesFailureExitCodeWithoutReplacingStatus()
        {
            var artifact = new HarnessArtifact { runId = "passed-write-failure", status = "Passed", failure = string.Empty };
            var result = HarnessArtifactWriter.Write(string.Empty, artifact);
            Assert.That(result.Success, Is.False);
            Assert.That(artifact.status, Is.EqualTo("Passed"));
            Assert.That(artifact.failure, Is.Empty);
            Assert.That(HarnessArtifactWriter.GetExitCode(artifact, result), Is.EqualTo(1));
        }

        [Test]
        public void ArtifactWriter_PartialCommitRollsBackTheOtherArtifact()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerHarnessArtifactRollback-" + Guid.NewGuid().ToString("N"));
            var textPath = Path.Combine(root, "rollback.txt");
            try
            {
                Directory.CreateDirectory(root);
                // Make the second destination unmovable. JSON must not be
                // left behind when TXT cannot be committed.
                Directory.CreateDirectory(textPath);
                var artifact = new HarnessArtifact { runId = "rollback", status = "Passed" };
                var result = HarnessArtifactWriter.Write(root, artifact);
                Assert.That(result.Success, Is.False);
                Assert.That(File.Exists(Path.Combine(root, "rollback.json")), Is.False);
                Assert.That(Directory.Exists(textPath), Is.True);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ProductBuild_HarnessAssemblyIsDefineConstrainedAndNotAutoReferenced()
        {
            var asmdefPath = Path.Combine(UnityEngine.Application.dataPath, "ShitDesigner/TestHarness/ShitDesigner.TestHarness.asmdef");
            var asmdef = File.ReadAllText(asmdefPath);
            Assert.That(asmdef, Does.Contain("\"autoReferenced\": false"));
            Assert.That(asmdef, Does.Contain("\"defineConstraints\": [\"SHITDESIGNER_TEST_HARNESS\"]"));
        }

        [Test]
        public void HarnessContractTests_DeclareProductionReferencesUsedByTheirPublicContracts()
        {
            var asmdefPath = Path.Combine(UnityEngine.Application.dataPath, "ShitDesigner/TestHarness/Tests/ShitDesigner.TestHarness.Tests.EditMode.asmdef");
            var asmdef = File.ReadAllText(asmdefPath);
            Assert.That(asmdef, Does.Contain("\"ShitDesigner.Application\""));
            Assert.That(asmdef, Does.Contain("\"ShitDesigner.Media\""));
            Assert.That(asmdef, Does.Contain("\"ShitDesigner.Nodes\""));
        }

        private static HarnessAcceptanceArtifact CompleteInitialFixtureArtifact()
        {
            var codecs = new[] { "H264", "VP8", "Hap1", "Hap5", "HapY", "HapM" };
            return new HarnessAcceptanceArtifact
            {
                mode = "acceptance",
                stage = HarnessAcceptanceStage.Initial.ToString(),
                acceptanceContractVersion = AcceptanceContract.CurrentArtifactContractVersion,
                productionCompositionUsed = true,
                productionCatalogUsed = true,
                editorAssemblyExcluded = true,
                presentationRootAvailable = true,
                programAndPreviewsReady = true,
                requiredGraphObserved = true,
                realFrameObserved = true,
                valueControlUpdated = true,
                valueControlRemapped = true,
                presetTriggerFired = true,
                mediaPortable = true,
                valueControlId = "value-control",
                presetTriggerId = "preset-trigger",
                presetId = "preset",
                fileProjectReadable = true,
                fileProjectWritable = true,
                persistence = new HarnessAcceptancePersistenceArtifact
                {
                    saved = true,
                    fingerprint = "fingerprint",
                    backupFingerprint = "backup",
                    expectedBackupFingerprint = "backup",
                    backupReadable = true
                },
                fixtures = codecs.Select(codec => new HarnessAcceptanceFixtureArtifact
                {
                    codec = codec,
                    probePassed = true,
                    prepareObserved = true,
                    mediaBindingApplied = true,
                    frameBefore = 1,
                    frameAfter = 2,
                    previewFrameBefore = 1,
                    previewFrameAfter = 2,
                    preview1FrameBefore = 1,
                    preview1FrameAfter = 2,
                    preview2FrameBefore = 1,
                    preview2FrameAfter = 2,
                    ownershipFramesObserved = true,
                    outputReadyObserved = true,
                    realFrameObserved = true,
                    frameReady = true
                }).ToArray()
            };
        }

        private static HarnessMetricSample ValidSample(int index, bool healthy = true, ulong? frame = null)
        {
            return new HarnessMetricSample
            {
                cpuMilliseconds = 8, gpuMilliseconds = 7, sampleSeconds = index / 60d, programFrameNumber = frame ?? (ulong)(index + 1),
                programWidth = 1920, programHeight = 1080, programFormat = "R16G16B16A16_SFloat", programTargetFramesPerSecond = 60,
                poolBudgetBytes = 1000, poolLeasedBytes = 40, poolFreeBytes = 40, poolHighWaterBytes = 80,
                previews = new[]
                {
                    PreviewSample("preview1", 0, index + 1),
                    PreviewSample("preview2", 0, index + 1)
                },
                programPresented = healthy, programHealthy = healthy
            };
        }

        private static HarnessPreviewQualitySample QualitySample(double seconds, ulong frame, int stage)
        {
            return new HarnessPreviewQualitySample
            {
                sampleSeconds = seconds,
                programFrameNumber = frame,
                previews = new[]
                {
                    PreviewSample("preview1", stage, (int)frame),
                    PreviewSample("preview2", stage, (int)frame)
                }
            };
        }

        private static void AssertPreviewMetricsEqual(HarnessPreviewMetric[] expected, HarnessPreviewMetric[] actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.Length, Is.EqualTo(expected == null ? 0 : expected.Length));
            if (expected == null) return;
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] == null)
                {
                    Assert.That(actual[index], Is.Null);
                    continue;
                }
                Assert.That(actual[index], Is.Not.SameAs(expected[index]));
                Assert.That(actual[index].id, Is.EqualTo(expected[index].id));
                Assert.That(actual[index].width, Is.EqualTo(expected[index].width));
                Assert.That(actual[index].height, Is.EqualTo(expected[index].height));
                Assert.That(actual[index].format, Is.EqualTo(expected[index].format));
                Assert.That(actual[index].targetFramesPerSecond, Is.EqualTo(expected[index].targetFramesPerSecond));
                Assert.That(actual[index].frameNumber, Is.EqualTo(expected[index].frameNumber));
                Assert.That(actual[index].quality, Is.EqualTo(expected[index].quality));
                Assert.That(actual[index].qualityStage, Is.EqualTo(expected[index].qualityStage));
            }
        }

        private static HarnessPreviewMetric PreviewSample(string id, int stage, int frame)
        {
            var widths = new[] { 640, 480, 320, 160, 160 };
            var heights = new[] { 360, 270, 180, 90, 90 };
            var fps = new[] { 30, 30, 20, 10, 5 };
            return new HarnessPreviewMetric { id = id, width = widths[stage], height = heights[stage], format = "R8G8B8A8_UNorm",
                targetFramesPerSecond = fps[stage], frameNumber = (ulong)frame, quality = "Stage" + stage, qualityStage = stage };
        }
    }
}
