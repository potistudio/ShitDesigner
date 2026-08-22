using System;
using System.IO;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using Unity.Profiling;

namespace ShitDesigner.TestHarness.Tests
{
    [Category("docs/ARCHITECTURE/Testing.md/Standalone性能Harness")]
    public sealed class PerformanceHarnessContractTests
    {
        [Test]
        public void InteractionSchedule_ScalesDeterministicallyForFixtureDuration()
        {
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdates(2.5d), Is.EqualTo(300));
            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFires(2.5d), Is.EqualTo(0));
            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFires(20d), Is.EqualTo(2));
            Assert.That(HarnessInteractionContract.Validate(2.5d, 299, 0), Does.Contain("Logical control"));
            Assert.That(HarnessInteractionContract.Validate(2.5d, 300, 0), Is.Empty);
        }

        [Test]
        public void InteractionSchedule_UsesExactAbsoluteCountsForFifteenAndSixHundredSeconds()
        {
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdates(15d), Is.EqualTo(1800));
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdates(600d), Is.EqualTo(72000));
            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFires(600d), Is.EqualTo(60));

            const double start = 100d;
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdatesAt(start, 15d, start + 14.999d), Is.EqualTo(1799));
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdatesAt(start, 15d, start + 15d), Is.EqualTo(1800));
            Assert.That(HarnessInteractionContract.ExpectedLogicalControlUpdatesAt(start, 15d, start + 15.25d), Is.EqualTo(1800),
                "Absolute scheduling must clamp a host frame that crosses the fixed deadline.");
        }

        [Test]
        public void InteractionSchedule_DispatchesFinalPartialIntervalBeforeClosureAndRejectsPostDeadlineInput()
        {
            const double start = 100d;
            const double deadline = start + 15d;
            var scheduler = new HarnessInteractionScheduler(start, 15d);
            var accepted = 0;

            // This host frame crosses the deadline. DispatchDue must consume
            // the one final logical slot before the boundary is closed.
            Assert.That(scheduler.DispatchDue(deadline + 0.001d, () => { accepted++; return true; }), Is.EqualTo(1800));
            Assert.That(accepted, Is.EqualTo(1800));
            Assert.That(scheduler.DispatchedUpdates, Is.EqualTo(1800));
            Assert.That(scheduler.CloseIfDue(deadline + 0.001d), Is.True);
            Assert.That(scheduler.IsOpen, Is.False);

            // Once the fixed window is closed, no later host frame can emit
            // another input, even though the host time is beyond the fence.
            Assert.That(scheduler.DispatchDue(deadline + 1d, () => { accepted++; return true; }), Is.EqualTo(0));
            Assert.That(accepted, Is.EqualTo(1800));
        }

        [Test]
        public void MeasurementReset_RebasesProgramPerformanceConsecutiveWarningWindow()
        {
            var target = Path.Combine(Path.GetTempPath(), "ShitDesigner-MeasurementReset-" + Guid.NewGuid().ToString("N"));
            var factory = new MeasurementRuntimeFactory();
            try
            {
                using (var application = new ProjectApplication(new LocalProjectFileSystem(), runtimeFactory: factory))
                {
                    Assert.That(application.NewProject("Performance measurement reset", target).IsSuccess, Is.True);
                    var monitor = new ProgramPerformanceMonitor();
                    factory.Session.ProgramPerformanceSink = monitor;
                    for (var frame = 1UL; frame <= 59UL; frame++)
                        factory.Session.ObserveFrameTiming(new RuntimeFrameTimingSample(frame, 58d, 17d, 17d));
                    Assert.That(factory.Session.ProgramPerformance.ConsecutiveBadFrames, Is.EqualTo(59));
                    Assert.That(factory.Session.ProgramPerformance.WarningActive, Is.False);

                    Assert.That(application.ResetDiagnosticsForMeasurement(100UL).IsSuccess, Is.True);
                    factory.Session.ObserveFrameTiming(new RuntimeFrameTimingSample(100UL, 58d, 17d, 17d));
                    Assert.That(factory.Session.ProgramPerformance.ConsecutiveBadFrames, Is.EqualTo(1));
                    Assert.That(factory.Session.ProgramPerformance.WarningActive, Is.False);
                }
            }
            finally
            {
                try { if (Directory.Exists(target)) Directory.Delete(target, true); }
                catch { }
            }
        }

        [Test]
        public void PerformanceTickSpeedTarget_UsesTheExistingVideoTransportSpeedRange()
        {
            var target = HarnessInteractionContract.CreatePerformanceTickSpeedTarget("video");
            Assert.That(HarnessInteractionContract.ValidatePerformanceTickSpeedTarget(), Is.Empty);
            Assert.That(target.NodeId, Is.EqualTo("video"));
            Assert.That(target.ParameterId, Is.EqualTo(VideoPlayerContract.SpeedParameterId));
            Assert.That(target.TargetMin.AsFloat(), Is.EqualTo(0.5f));
            Assert.That(target.TargetMax.AsFloat(), Is.EqualTo(1.5f));
            Assert.That(HarnessInteractionContract.PerformancePresetSpeedValue.AsFloat(), Is.GreaterThan(target.TargetMax.AsFloat()),
                "The preset must win Max(Base, LogicalControl) even while the 120 Hz input is held high.");
            Assert.That(HarnessInteractionContract.PerformancePresetSpeedValue.AsFloat(), Is.LessThanOrEqualTo(4f),
                "The existing VideoPlayer speed hard range remains the authority.");
        }

        [Test]
        public void GcAllocationMeasurement_RequiresTheAllThreadMemoryBytesCounter()
        {
            Assert.That(HarnessGcAllocationContract.CounterName, Is.EqualTo("GC Allocated In Frame"));
            Assert.That(HarnessGcAllocationContract.CounterCategory.Name, Is.EqualTo(ProfilerCategory.Memory.Name));
            Assert.That(HarnessGcAllocationContract.SampleCapacity, Is.EqualTo(1),
                "The harness consumes the latest completed frame rather than retaining an allocation history.");
            Assert.That(HarnessGcAllocationContract.MarkerOptions & ProfilerRecorderOptions.SumAllSamplesInFrame,
                Is.EqualTo(ProfilerRecorderOptions.SumAllSamplesInFrame));
            Assert.That(HarnessGcAllocationContract.MarkerOptions & ProfilerRecorderOptions.WrapAroundWhenCapacityReached,
                Is.EqualTo(ProfilerRecorderOptions.WrapAroundWhenCapacityReached),
                "A one-sample recorder must replace LastValue every frame rather than re-adding the first sample throughout the run.");
            Assert.That(HarnessGcAllocationContract.MarkerOptions & ProfilerRecorderOptions.CollectOnlyOnCurrentThread,
                Is.EqualTo(ProfilerRecorderOptions.None), "The performance artifact must include allocation bytes from all Player threads.");
            Assert.That(HarnessGcAllocationContract.IsAllThreadByteMeasurement(true, ProfilerMarkerDataUnit.Bytes), Is.True);
            Assert.That(HarnessGcAllocationContract.IsAllThreadByteMeasurement(false, ProfilerMarkerDataUnit.Bytes), Is.False);
            Assert.That(HarnessGcAllocationContract.IsAllThreadByteMeasurement(true, ProfilerMarkerDataUnit.TimeNanoseconds), Is.False,
                "A marker duration must never be reported as gcAllocatedBytes.");
            Assert.That(HarnessGcAllocationContract.IsAllThreadByteMeasurement(true, ProfilerMarkerDataUnit.Count), Is.False,
                "An allocation count must never be reported as gcAllocatedBytes.");
            Assert.That(HarnessGcAllocationContract.AccumulateBytes(16L, 32L), Is.EqualTo(48L));
            Assert.That(HarnessGcAllocationContract.AccumulateBytes(16L, -1L), Is.EqualTo(16L));
            Assert.That(HarnessGcAllocationContract.AccumulateBytes(long.MaxValue - 1L, 2L), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void FrameTimingReadiness_FencesTheBootstrapCompletionBeforeMeasurement()
        {
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(90UL, 100UL, 0UL, 60d, 8d, 7d), Is.False,
                "A warm-up output frame without a public timing completion is not a measurement fence.");
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(90UL, 100UL, 100UL, double.NaN, 8d, 7d), Is.False,
                "The first completion has no preceding presentation timestamp, so its FPS must not enter measurement.");
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(100UL, 101UL, 100UL, 60d, 8d, 7d), Is.False,
                "A stale finite warm-up completion must not satisfy the measurement-start fence.");
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(90UL, 100UL, 101UL, 60d, 8d, 7d), Is.False,
                "A timing result cannot be attributed to a future public output frame.");
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(90UL, 100UL, 99UL, 60d, 8d, 7d), Is.True,
                "A fully finite delayed completion is ready before the fixed measurement window starts.");
            Assert.That(HarnessFrameTimingReadinessContract.IsReady(90UL, 100UL, 100UL, 60d, 0d, 7d), Is.False,
                "A zero GPU timing remains unavailable; the fence does not weaken the timing requirement.");
        }

        [Test]
        public void TimingCompletionTracker_FencesWarmupJoinsOwnPresentationAndReportsUndrainedFrames()
        {
            var tracker = new HarnessTimingCompletionTracker();
            tracker.BeginMeasurement(10UL);
            Assert.That(tracker.RecordPresentation(10UL, new HarnessMetricSample { programFrameNumber = 10UL }), Is.False,
                "A timing completion for an in-flight warm-up frame must never enter the measured interval.");
            var first = new HarnessMetricSample { programFrameNumber = 101UL, programHealthy = true };
            var second = new HarnessMetricSample { programFrameNumber = 102UL, programHealthy = true };
            Assert.That(tracker.RecordPresentation(11UL, first), Is.True);
            Assert.That(tracker.RecordPresentation(12UL, second), Is.True);
            Assert.That(tracker.TryTakeCompletion(10UL, out _), Is.False);
            Assert.That(tracker.TryTakeCompletion(12UL, out var matched), Is.True);
            Assert.That(matched, Is.SameAs(second), "The delayed timing must join its own saved Program descriptor, not the current frame.");
            Assert.That(tracker.TryTakeCompletion(12UL, out _), Is.False, "A completed timing must be consumed exactly once.");
            var unresolved = tracker.DrainUncompleted();
            Assert.That(unresolved.Count, Is.EqualTo(1));
            Assert.That(unresolved[0], Is.SameAs(first), "The four-frame drain must turn an uncompleted measured presentation into an explicit failure.");
            Assert.That(tracker.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void TimingCompletionTracker_JoinsTheFiniteSixteenFrameScalarHistoryWithoutDroppingFrames()
        {
            var tracker = new HarnessTimingCompletionTracker();
            tracker.BeginMeasurement(100UL);
            for (var frame = 101UL; frame <= 100UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++)
            {
                Assert.That(tracker.RecordPresentation(frame, new HarnessMetricSample { programFrameNumber = frame }), Is.True);
            }
            for (var frame = 101UL; frame <= 100UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++)
            {
                Assert.That(tracker.TryTakeCompletion(frame, out var matched), Is.True);
                Assert.That(matched.programFrameNumber, Is.EqualTo(frame));
            }
            Assert.That(tracker.PendingCount, Is.EqualTo(0),
                "The end drain must join every completion in the finite scalar FrameTiming history exactly once.");
        }

        [Test]
        public void CoalescedTailTimingCompletion_CrossesTheSeventeenthTickBeforeDrain()
        {
            var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.PerformanceHarness.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var application = new ProjectApplication(new LocalProjectFileSystem()))
                {
                    Assert.That(application.NewProject("Tail timing", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
                    var timing = new CompletesAtPollTiming(FrameTimingCompletionCorrelation.MaximumPendingFrames, false);
                    var driver = new ApplicationLoopDriverCore(application, new NullInput(), new NullPresentation(), timing);
                    try
                    {
                        var tracker = new HarnessTimingCompletionTracker();
                        tracker.BeginMeasurement(0UL);
                        ulong tailFrame = 0UL;
                        for (var poll = 1; poll <= FrameTimingCompletionCorrelation.MaximumPendingFrames; poll++)
                        {
                            var frame = driver.LateUpdate(poll);
                            Assert.That(tracker.RecordPresentation(frame.FrameNumber, ValidSample(poll, frame.FrameNumber)), Is.True);
                            if (poll == FrameTimingCompletionCorrelation.MaximumPendingFrames)
                            {
                                tailFrame = frame.FrameNumber;
                                Assert.That(application.ReadModel.Output.Model.PerformanceFrameNumber, Is.Not.EqualTo(tailFrame),
                                    "The completion is recorded after the tail presentation and cannot rebuild the ReadModel in that same host frame.");
                            }
                        }

                        driver.LateUpdate(FrameTimingCompletionCorrelation.MaximumPendingFrames + 1d);
                        Assert.That(application.ReadModel.Output.Model.PerformanceFrameNumber, Is.EqualTo(tailFrame));
                        Assert.That(tracker.TryTakeCompletion(tailFrame, out var matched), Is.True,
                            "The seventeenth Tick must project the tail completion before the finite drain converts it to NaN.");
                        Assert.That(matched.programFrameNumber, Is.EqualTo(tailFrame));
                        var unresolved = tracker.DrainUncompleted();
                        foreach (var item in unresolved)
                            Assert.That(item.programFrameNumber, Is.Not.EqualTo(tailFrame));
                    }
                    finally { driver.Dispose(); }
                }
            }
            finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
        }

        [Test]
        public void CoalescedTailUnavailableTiming_CrossesTheSeventeenthTickExactlyOnce()
        {
            var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.PerformanceHarness.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var application = new ProjectApplication(new LocalProjectFileSystem()))
                {
                    Assert.That(application.NewProject("Tail unavailable timing", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
                    var timing = new CompletesAtPollTiming(FrameTimingCompletionCorrelation.MaximumPendingFrames, true);
                    var driver = new ApplicationLoopDriverCore(application, new NullInput(), new NullPresentation(), timing);
                    try
                    {
                        var tracker = new HarnessTimingCompletionTracker();
                        tracker.BeginMeasurement(0UL);
                        ulong tailFrame = 0UL;
                        for (var poll = 1; poll <= FrameTimingCompletionCorrelation.MaximumPendingFrames; poll++)
                        {
                            var frame = driver.LateUpdate(poll);
                            Assert.That(tracker.RecordPresentation(frame.FrameNumber, ValidSample(poll, frame.FrameNumber)), Is.True);
                            tailFrame = frame.FrameNumber;
                        }

                        driver.LateUpdate(FrameTimingCompletionCorrelation.MaximumPendingFrames + 1d);
                        var unavailable = application.ReadModel.Output.Model;
                        Assert.That(unavailable.PerformanceFrameNumber, Is.EqualTo(tailFrame));
                        Assert.That(double.IsNaN(unavailable.CpuFrameTimeMilliseconds), Is.True);
                        Assert.That(double.IsNaN(unavailable.GpuFrameTimeMilliseconds), Is.True);
                        Assert.That(tracker.TryTakeCompletion(tailFrame, out var matched), Is.True);
                        Assert.That(matched.programFrameNumber, Is.EqualTo(tailFrame));

                        driver.LateUpdate(FrameTimingCompletionCorrelation.MaximumPendingFrames + 2d);
                        Assert.That(tracker.TryTakeCompletion(tailFrame, out _), Is.False,
                            "The delayed unavailable completion is one original-frame observation, never a second metric on a later empty poll.");
                    }
                    finally { driver.Dispose(); }
                }
            }
            finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
        }

        [Test]
        public void ExpiredFrameTiming_PublishesOneUnavailableOriginalFrameAndConsumesOneHarnessPresentation()
        {
            var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.PerformanceHarness.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var application = new ProjectApplication(new LocalProjectFileSystem()))
                {
                    Assert.That(application.NewProject("Expired timing", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
                    var timing = new ExpiredOnceTiming();
                    var driver = new ApplicationLoopDriverCore(application, new NullInput(), new NullPresentation(), timing);
                    try
                    {
                        driver.LateUpdate(1d);
                        Assert.That(timing.FirstPresentedFrame, Is.GreaterThan(0UL));

                        var tracker = new HarnessTimingCompletionTracker();
                        tracker.BeginMeasurement(timing.FirstPresentedFrame - 1UL);
                        var stored = new HarnessMetricSample { programFrameNumber = timing.FirstPresentedFrame, programHealthy = true };
                        Assert.That(tracker.RecordPresentation(timing.FirstPresentedFrame, stored), Is.True);

                        // This is the source's bounded-window expiry result,
                        // represented by the same original presentation frame.
                        // It is recorded after this host presentation and
                        // crosses the public boundary with the next Tick.
                        driver.LateUpdate(2d);
                        Assert.That(application.ReadModel.Output.Model.PerformanceFrameNumber, Is.Not.EqualTo(timing.FirstPresentedFrame));
                        driver.LateUpdate(3d);
                        var expired = application.ReadModel.Output.Model;
                        Assert.That(expired.PerformanceFrameNumber, Is.EqualTo(timing.FirstPresentedFrame));
                        Assert.That(double.IsNaN(expired.CpuFrameTimeMilliseconds), Is.True);
                        Assert.That(double.IsNaN(expired.GpuFrameTimeMilliseconds), Is.True);
                        Assert.That(tracker.TryTakeCompletion(expired.PerformanceFrameNumber, out var matched), Is.True);
                        Assert.That(matched, Is.SameAs(stored));
                        Assert.That(tracker.PendingCount, Is.EqualTo(0));

                        // A pending poll has no timing publication. It must
                        // neither create a second metric nor update quality
                        // evidence for the expired frame.
                        driver.LateUpdate(4d);
                        var retained = application.ReadModel.Output.Model;
                        Assert.That(retained.PerformanceFrameNumber, Is.EqualTo(timing.FirstPresentedFrame));
                        Assert.That(double.IsNaN(retained.CpuFrameTimeMilliseconds), Is.True);
                        Assert.That(tracker.TryTakeCompletion(retained.PerformanceFrameNumber, out _), Is.False);
                        Assert.That(timing.ExpiredSamplesReturned, Is.EqualTo(1));
                    }
                    finally { driver.Dispose(); }
                }
            }
            finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
        }

        [Test]
        public void MeasurementBoundary_StopsInputGcAndPresentationAtDeadlineWhileAwaitingPresetAndDrain()
        {
            Assert.That(HarnessMeasurementBoundaryContract.ShouldCloseWindow(true, true), Is.True);
            Assert.That(HarnessMeasurementBoundaryContract.AllowsMeasurementEvidence(false), Is.False,
                "GC and new presentation snapshots must stop exactly at the measurement deadline.");
            Assert.That(HarnessMeasurementBoundaryContract.IsNewProgramPresentation(100UL, 100UL), Is.False,
                "A faster Unity host must not count one Program presentation twice.");
            Assert.That(HarnessMeasurementBoundaryContract.IsNewProgramPresentation(100UL, 101UL), Is.True);
            Assert.That(HarnessMeasurementBoundaryContract.AllowsInteractionInput(false), Is.False,
                "120 Hz logical input must stop exactly at the measurement deadline.");
            Assert.That(HarnessMeasurementBoundaryContract.CanFinalize(true, true), Is.False,
                "A pending final PresetTrigger release/rearm must finish before artifact finalization.");
            Assert.That(HarnessMeasurementBoundaryContract.CanFinalize(false, false), Is.False,
                "Four-frame completion draining must also finish before artifact finalization.");
            Assert.That(HarnessMeasurementBoundaryContract.CanFinalize(true, false), Is.True);
        }

        [Test]
        public void MeasurementBoundary_StartsTheFinalPresetOnceWhenDeadlineFrameCrossesAndNeverAfterClose()
        {
            const double start = 100d;
            const double duration = 600d;
            const double deadline = start + duration;

            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFiresAt(start, duration, start + 599.999d), Is.EqualTo(59));
            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFiresAt(start, duration, deadline), Is.EqualTo(60));
            Assert.That(HarnessInteractionContract.ExpectedPresetTriggerFiresAt(start, duration, deadline + 0.1d), Is.EqualTo(60));
            Assert.That(HarnessInteractionContract.DuePresetTriggerFiresAt(start, duration, deadline + 0.25d, 59), Is.EqualTo(1));

            var verificationActive = false;
            var completed = 59;
            var starts = 0;
            if (HarnessMeasurementBoundaryContract.ShouldStartPresetTrigger(true, verificationActive, start, duration,
                deadline + 0.25d, completed))
            {
                verificationActive = true;
                starts++;
            }
            Assert.That(starts, Is.EqualTo(1), "The deadline-crossing host frame must start the final due trigger.");

            Assert.That(HarnessMeasurementBoundaryContract.ShouldStartPresetTrigger(true, verificationActive, start, duration,
                deadline + 0.25d, completed), Is.False,
                "An in-flight final verification must not be double-started.");

            verificationActive = false;
            completed++;
            Assert.That(HarnessMeasurementBoundaryContract.ShouldStartPresetTrigger(false, verificationActive, start, duration,
                deadline + 1d, completed), Is.False,
                "Closing the fixed window must reject a 61st trigger on later host frames.");
            Assert.That(HarnessInteractionContract.DuePresetTriggerFiresAt(start, duration, deadline + 1d, completed), Is.EqualTo(0));
            Assert.That(HarnessMeasurementBoundaryContract.CanFinalize(true, true), Is.False,
                "Timing drain completion cannot finalize while the final public verification is active.");
            Assert.That(HarnessMeasurementBoundaryContract.CanFinalize(true, false), Is.True);
        }

        private sealed class NullInput : IApplicationInputPoller
        {
            public void Poll() { }
        }

        private sealed class MeasurementRuntimeFactory : IApplicationRuntimeSessionFactory
        {
            public RuntimeSession Session { get; private set; }

            public Result<ApplicationRuntimeComposition> Create(ProjectDocument document, NodeTypeRegistry registry)
            {
                Session = new RuntimeSession(document, registry, new DiagnosticHub("test.measurement"));
                return Result<ApplicationRuntimeComposition>.Success(new ApplicationRuntimeComposition(
                    Session, new FrameCoordinator(Session), true));
            }
        }

        private sealed class NullPresentation : IApplicationPresentationFrame
        {
            public void Read(ApplicationFrameResult frame) { }
            public void Apply(ApplicationFrameResult frame) { }
            public void Present(ApplicationFrameResult frame) { }
        }

        private sealed class ExpiredOnceTiming : IProductionFrameTimingSource
        {
            private int _polls;
            public ulong FirstPresentedFrame { get; private set; }
            public int ExpiredSamplesReturned { get; private set; }

            public bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample)
            {
                _polls++;
                if (_polls == 1)
                {
                    FirstPresentedFrame = presentedFrameNumber;
                    sample = default(RuntimeFrameTimingSample);
                    return false;
                }
                if (_polls == 2)
                {
                    ExpiredSamplesReturned++;
                    sample = RuntimeFrameTimingSample.Unavailable(FirstPresentedFrame);
                    return true;
                }
                sample = default(RuntimeFrameTimingSample);
                return false;
            }
        }

        private sealed class CompletesAtPollTiming : IProductionFrameTimingSource
        {
            private readonly int _completionPoll;
            private readonly bool _unavailable;
            private int _polls;

            public CompletesAtPollTiming(int completionPoll, bool unavailable)
            {
                _completionPoll = completionPoll;
                _unavailable = unavailable;
            }

            public bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample)
            {
                _polls++;
                if (_polls == _completionPoll)
                {
                    sample = _unavailable
                        ? RuntimeFrameTimingSample.Unavailable(presentedFrameNumber)
                        : new RuntimeFrameTimingSample(presentedFrameNumber, 60d, 8d, 7d);
                    return true;
                }

                sample = default(RuntimeFrameTimingSample);
                return false;
            }
        }

        [Test]
        public void PresetApplicationContract_RequiresPublicAppliedObservation()
        {
            Assert.That(HarnessPresetApplicationContract.ValidateObservation(
                "(0.8, 0.1, 0.9, 1)", "(0.8, 0.1, 0.9, 1)", "(0.8, 0.1, 0.9, 1)", "preset", "preset", false, true), Is.Empty);
            Assert.That(HarnessPresetApplicationContract.ValidateObservation(
                "(0.2, 0.4, 0.6, 1)", "(0.8, 0.1, 0.9, 1)", "(0.8, 0.1, 0.9, 1)", "preset", "preset", false, true), Does.Contain("base value"));
            Assert.That(HarnessPresetApplicationContract.ValidateObservation(
                "(0.8, 0.1, 0.9, 1)", "(0.2, 0.4, 0.6, 1)", "(0.8, 0.1, 0.9, 1)", "preset", "preset", false, true), Does.Contain("effective value"));
            Assert.That(HarnessPresetApplicationContract.ValidateObservation(
                "(0.8, 0.1, 0.9, 1)", "(0.8, 0.1, 0.9, 1)", "(0.8, 0.1, 0.9, 1)", "", "preset", false, true), Does.Contain("binding"));
        }

        [Test]
        public void DiagnosticsExportContract_IsRequiredOnlyForFailureArtifacts()
        {
            Assert.That(HarnessDiagnosticsExportContract.RequiredForStatus(HarnessRunStatus.Passed.ToString()), Is.False);
            Assert.That(HarnessDiagnosticsExportContract.RequiredForStatus(HarnessRunStatus.Failed.ToString()), Is.True);
            Assert.That(HarnessDiagnosticsExportContract.RequiredForStatus(HarnessRunStatus.EnvironmentFailed.ToString()), Is.True);
        }

        [Test]
        public void DiagnosticsExportCandidate_IsCapturedBeforeTeardownAndNeverGatesPassed()
        {
            var candidate = new HarnessDiagnosticsExportArtifact
            {
                attempted = true,
                textWritten = true,
                jsonWritten = false,
                textPath = "run/diagnostics.txt",
                jsonPath = "run/diagnostics.json",
                failure = "json export failed after text export"
            };
            var failedArtifactExport = HarnessDiagnosticsExportContract.AttachCandidate(
                HarnessRunStatus.Failed.ToString(), candidate);
            Assert.That(failedArtifactExport, Is.SameAs(candidate));
            Assert.That(failedArtifactExport.failure, Does.Contain("json export failed"));

            var passedArtifactExport = HarnessDiagnosticsExportContract.AttachCandidate(
                HarnessRunStatus.Passed.ToString(), candidate);
            Assert.That(passedArtifactExport, Is.SameAs(candidate));
            Assert.That(HarnessDiagnosticsExportContract.PreserveOriginalFailure(
                string.Empty, HarnessRunStatus.Passed.ToString(), candidate.failure), Is.Empty);
            Assert.That(HarnessDiagnosticsExportContract.PreserveOriginalFailure(
                "measured failure", HarnessRunStatus.Failed.ToString(), candidate.failure), Is.EqualTo("measured failure"));
        }

        [Test]
        public void MetricEvaluator_FailsWhenMeasuredInteractionCountsAreBelowExpected()
        {
            var metrics = new HarnessMetricAccumulator();
            metrics.Add(ValidSample(0d, 1));
            metrics.Add(ValidSample(1d / 60d, 2));
            var result = HarnessMetricEvaluator.Evaluate(metrics, 1920, 1080, "R16G16B16A16_SFloat", 100, 1000,
                0, 0, 0, 0, 0, 239, 0, 2d);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failure, Does.Contain("Logical control"));
        }

        [Test]
        public void OwnershipArtifact_IsStructuredProjectionOfPublicSnapshot()
        {
            var snapshot = HarnessOwnershipContract.CreateTestSnapshot(2, 3, 4, 5, 6, 1920, 1080,
                "R16G16B16A16_SFloat", 60, false);
            var artifact = HarnessOwnershipSnapshotArtifact.From(snapshot);
            Assert.That(artifact.available, Is.True);
            Assert.That(artifact.sceneCount, Is.EqualTo(2));
            Assert.That(artifact.layerCount, Is.EqualTo(3));
            Assert.That(artifact.backendCount, Is.EqualTo(4));
            Assert.That(artifact.nativeContextCount, Is.EqualTo(5));
            Assert.That(artifact.activeOutputLeaseCount, Is.EqualTo(6));
            Assert.That(artifact.program.width, Is.EqualTo(1920));
            Assert.That(artifact.previews, Has.Length.EqualTo(2));
        }

        [Test]
        public void ArtifactText_ContainsPackageVersionInteractionsAndOwnership()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesignerPerformanceArtifact-" + Guid.NewGuid().ToString("N"));
            try
            {
                var artifact = new HarnessArtifact
                {
                    runId = "performance-contract",
                    status = HarnessRunStatus.Failed.ToString(),
                    failure = "original-failure",
                    packageVersion = "0.1.0",
                    developmentBuild = true,
                    buildOptions = "Development",
                    projectRoot = Path.Combine(root, "canonical-project"),
                    nativePluginProbe = new HarnessNativePluginProbeArtifact
                    {
                        path = "PInvokeHapNativeApi.ProbeInstalledBinary",
                        supportedPlatform = true,
                        passed = true,
                        abiVersion = 1,
                        capabilities = 3,
                        diagnosticCode = string.Empty,
                        diagnostic = "verified"
                    },
                    codecProbe = new HarnessCodecProbeArtifact
                    {
                        path = "ExtensionVideoCapabilityProbe(FileVideoMetadataProbe)",
                        passed = true,
                        supported = true,
                        backend = "HapVideoBackend",
                        container = "Mov",
                        codec = "HapY",
                        hasAlpha = false,
                        hasAudio = false,
                        diagnostic = string.Empty
                    },
                    interactions = new HarnessInteractionArtifact
                    {
                        logicalControlUpdatesPerSecond = 120d,
                        presetTriggerIntervalSeconds = 10d,
                        measurementSeconds = 20d,
                        logicalControlUpdates = 2400,
                        expectedLogicalControlUpdates = 2400,
                        presetTriggerFires = 2,
                        expectedPresetTriggerFires = 2
                    },
                    canonicalScenarioSaved = true,
                    ownership = HarnessOwnershipSnapshotArtifact.From(HarnessOwnershipContract.CreateTestSnapshot(
                        1, 1, 1, 1, 1, 1920, 1080, "R16G16B16A16_SFloat", 60, false)),
                    diagnostics = HarnessDiagnosticsArtifact.Empty(),
                    diagnosticsExport = new HarnessDiagnosticsExportArtifact
                    {
                        attempted = true,
                        textWritten = true,
                        jsonWritten = true,
                        textPath = Path.Combine(root, "diagnostics", "diagnostics.txt"),
                        jsonPath = Path.Combine(root, "diagnostics", "diagnostics.json")
                    },
                    failureCapture = HarnessFailureCaptureArtifact.PublicProgramReadbackUnavailable(),
                    operationSequence = new[] { "AddNode:shitdesigner.scene.3d", "Connect:image->image", "SaveProject:canonical-scenario", "Measure:20" }
                };
                var result = HarnessArtifactWriter.Write(root, artifact);
                Assert.That(result.Success, Is.True, result.Error);
                var json = File.ReadAllText(result.JsonPath);
                var text = File.ReadAllText(result.TextPath);
                var jsonArtifact = UnityEngine.JsonUtility.FromJson<HarnessArtifact>(json);
                Assert.That(json, Does.Contain("\"projectRoot\""));
                Assert.That(json, Does.Contain("\"developmentBuild\""));
                Assert.That(json, Does.Contain("\"buildOptions\""));
                Assert.That(jsonArtifact.developmentBuild, Is.True);
                Assert.That(jsonArtifact.buildOptions, Is.EqualTo("Development"));
                Assert.That(json, Does.Contain("\"nativePluginProbe\""));
                Assert.That(json, Does.Contain("\"codecProbe\""));
                Assert.That(json, Does.Contain("\"diagnostics\""));
                Assert.That(json, Does.Contain("\"diagnosticsExport\""));
                Assert.That(json, Does.Contain("\"failureCapture\""));
                Assert.That(json, Does.Contain("\"operationSequence\""));
                Assert.That(json, Does.Contain("\"canonicalScenarioSaved\": true"));
                Assert.That(text, Does.Contain("packageVersion=0.1.0"));
                Assert.That(text, Does.Contain("developmentBuild=True"));
                Assert.That(text, Does.Contain("buildOptions=Development"));
                Assert.That(text, Does.Contain("projectRoot="));
                Assert.That(text, Does.Contain("nativePluginPath=PInvokeHapNativeApi.ProbeInstalledBinary"));
                Assert.That(text, Does.Contain("codecProbeBackend=HapVideoBackend"));
                Assert.That(text, Does.Contain("diagnosticCurrentCodes="));
                Assert.That(text, Does.Contain("logicalControlUpdates=2400"));
                Assert.That(text, Does.Contain("expectedPresetTriggerFires=2"));
                Assert.That(text, Does.Contain("ownershipSceneCount=1"));
                Assert.That(text, Does.Contain("diagnosticsExportJsonWritten=True"));
                Assert.That(text, Does.Contain("failureCaptureProgramReadbackAvailable=False"));
                Assert.That(text, Does.Contain("operationSequence=AddNode:shitdesigner.scene.3d;Connect:image->image;SaveProject:canonical-scenario;Measure:20"));
                Assert.That(text, Does.Contain("canonicalScenarioSaved=True"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void FailureArtifactDiagnosticsFallback_IsAlwaysNonNullAndStructured()
        {
            var diagnostics = HarnessDiagnosticsArtifact.Empty();
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics.currentCodes, Is.Not.Null);
            Assert.That(diagnostics.historyCodes, Is.Not.Null);
            Assert.That(diagnostics.intervals, Is.Not.Null);
            Assert.That(diagnostics.currentCodes, Is.Empty);
            Assert.That(diagnostics.historyCodes, Is.Empty);
            Assert.That(diagnostics.intervals, Is.Empty);
        }

        [Test]
        public void FinalizationGuard_InvokesNonIdempotentCleanupOnlyOnce()
        {
            var guard = new HarnessFinalizationGuard();
            var cleanupCalls = 0;
            var failures = 0;
            Assert.That(guard.Try(() =>
            {
                cleanupCalls++;
                throw new InvalidOperationException("teardown");
            }, _ => failures++), Is.True);
            Assert.That(guard.Try(() => cleanupCalls++), Is.False);
            Assert.That(guard.Attempted, Is.True);
            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(failures, Is.EqualTo(1));
        }

        [Test]
        public void FinalizationContract_PostWriteTeardownFailureCannotRemainPassedOrSkipQuit()
        {
            var quitCalls = 0;
            var quitCode = -1;
            var decision = HarnessFinalizationContract.Decide(string.Empty, HarnessRunStatus.Passed.ToString(), true,
                "teardown failed after artifact preparation", code =>
                {
                    quitCalls++;
                    quitCode = code;
                });
            Assert.That(decision.status, Is.EqualTo(HarnessRunStatus.Failed.ToString()));
            Assert.That(decision.failure, Does.Contain("teardown failed"));
            Assert.That(decision.exitCode, Is.EqualTo(1));
            Assert.That(decision.quitAttempted, Is.True);
            Assert.That(quitCalls, Is.EqualTo(1));
            Assert.That(quitCode, Is.EqualTo(1));
        }

        [Test]
        public void FinalizationQuitPolicy_ClosesStandalonePlayersButNeverTheEditor()
        {
            Assert.That(HarnessFinalizationContract.ShouldQuitPlayer(true, false), Is.True,
                "A normal non-batch Standalone Player must exit after artifact finalization.");
            Assert.That(HarnessFinalizationContract.ShouldQuitPlayer(true, true), Is.False,
                "Harness PlayMode coverage must never terminate the Editor.");
            Assert.That(HarnessFinalizationContract.ShouldQuitPlayer(false, false), Is.False,
                "The explicit no-quit option remains available for diagnostics.");
        }

        [Test]
        public void FinalizationContract_PreservesOriginalFailureWhenCleanupAlsoFails()
        {
            var decision = HarnessFinalizationContract.Decide("original test failure", HarnessRunStatus.Passed.ToString(), true,
                "teardown failed", _ => { });
            Assert.That(decision.status, Is.EqualTo(HarnessRunStatus.Failed.ToString()));
            Assert.That(decision.failure, Is.EqualTo("original test failure"));
            Assert.That(decision.exitCode, Is.EqualTo(1));
        }

        private static HarnessMetricSample ValidSample(double seconds, ulong frame)
        {
            return new HarnessMetricSample
            {
                cpuMilliseconds = 8d,
                gpuMilliseconds = 7d,
                sampleSeconds = seconds,
                programFrameNumber = frame,
                programWidth = 1920,
                programHeight = 1080,
                programFormat = "R16G16B16A16_SFloat",
                programTargetFramesPerSecond = 60,
                poolBudgetBytes = 1000,
                poolLeasedBytes = 40,
                poolFreeBytes = 40,
                poolHighWaterBytes = 80,
                previews = new[]
                {
                    Preview("preview1", frame),
                    Preview("preview2", frame)
                },
                programPresented = true,
                programHealthy = true
            };
        }

        private static HarnessPreviewMetric Preview(string id, ulong frame)
        {
            return new HarnessPreviewMetric
            {
                id = id,
                width = 640,
                height = 360,
                format = "R8G8B8A8_UNorm",
                targetFramesPerSecond = 30,
                frameNumber = frame,
                quality = "Stage0",
                qualityStage = 0
            };
        }
    }
}
