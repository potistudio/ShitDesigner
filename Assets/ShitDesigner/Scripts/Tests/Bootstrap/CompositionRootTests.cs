using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Nodes;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace ShitDesigner.Bootstrap.Tests {
	[TestFixture]
	public sealed class CompositionRootTests {
		[Test]
		public void EntrySceneKeepsSerializedApplicationHostAfterRename() {
			var scene = EditorSceneManager.OpenScene("Assets/ShitDesigner/Scenes/ShitDesignerBootstrap.unity", OpenSceneMode.Additive);
			try {
				var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == "Host");
				Assert.That(root, Is.Not.Null);
				Assert.That(root.GetComponent<ApplicationHost>(), Is.Not.Null, "The preserved MonoScript GUID must resolve to ApplicationHost.");
			}
			finally { EditorSceneManager.CloseScene(scene, removeScene: true); }
		}

		[Test]
		public void StartupSequenceRunsNamedBoundariesInOrderAndReachesOnline() {
			var order = new List<string>();
			var startup = new StartupSequence();

			var result = startup.Run(
				() => RecordSuccessfulPhase(order, "preflight"),
				() => RecordSuccessfulPhase(order, "compose"),
				() => RecordSuccessfulHandshake(order),
				() => RecordSuccessfulPhase(order, "activate"));

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(startup.State, Is.EqualTo(SystemState.Online));
			Assert.That(startup.LastDiagnostic, Is.Null);
			CollectionAssert.AreEqual(new[] { "preflight", "compose", "handshake", "activate" }, order);
		}

		[Test]
		public void StartupSequenceFaultsAtFailedBoundaryAndDoesNotRunLaterPhases() {
			var order = new List<string>();
			var startup = new StartupSequence();
			var expected = new Diagnostic(new DiagnosticCode("test.handshake.unavailable"), Severity.Error, "Handshake failed.");

			var result = startup.Run(
				() => RecordSuccessfulPhase(order, "preflight"),
				() => {
					order.Add("compose");
					startup.RegisterShutdown(ShutdownStage.Stop, () => order.Add("rollback"));
					return Result.Success();
				},
				() => { order.Add("handshake"); return Result<HandshakeReport>.Failure(expected); },
				() => RecordSuccessfulPhase(order, "activate"));

			Assert.That(result.IsFailure, Is.True);
			Assert.That(result.Diagnostic, Is.SameAs(expected));
			Assert.That(startup.State, Is.EqualTo(SystemState.Faulted));
			Assert.That(startup.LastDiagnostic, Is.SameAs(expected));
			CollectionAssert.AreEqual(new[] { "preflight", "compose", "handshake", "rollback" }, order);
		}

		[Test]
		public void StartupSequencePublishesDegradedWhenOptionalCapabilityIsUnavailable() {
			var startup = new StartupSequence();
			var unavailable = CapabilityStatus.Unavailable("midi", new Diagnostic(new DiagnosticCode("test.midi.unavailable"), Severity.Warning, "No MIDI."));

			var result = startup.Run(
				() => Result.Success(),
				() => Result.Success(),
				() => Result<HandshakeReport>.Success(new HandshakeReport(unavailable, CapabilityStatus.Ready("display"))),
				() => Result.Success());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(startup.State, Is.EqualTo(SystemState.Degraded));
			Assert.That(startup.HandshakeReport.IsDegraded, Is.True);
			Assert.That(startup.HandshakeReport.Midi.Diagnostic.Code.Value, Is.EqualTo("test.midi.unavailable"));
		}

		[Test]
		public void CapabilitySupervisorMovesSystemBetweenDegradedAndOnlineAtProbeInterval() {
			var midiProbeCount = 0;
			var displayProbeCount = 0;
			var midi = CapabilityStatus.Unavailable("midi",
				new Diagnostic(new DiagnosticCode("test.midi.unavailable"), Severity.Warning, "No MIDI."));
			var supervisor = new CapabilitySupervisor(
				() => { midiProbeCount++; return Result<CapabilityStatus>.Success(midi); },
				() => { displayProbeCount++; return Result<CapabilityStatus>.Success(CapabilityStatus.Ready("display")); });
			var startup = new StartupSequence();
			supervisor.Changed += startup.Observe;

			Assert.That(startup.Run(
				() => Result.Success(),
				() => Result.Success(),
				supervisor.Handshake,
				() => Result.Success()).IsSuccess, Is.True);
			Assert.That(startup.State, Is.EqualTo(SystemState.Degraded));

			midi = CapabilityStatus.Ready("midi");
			supervisor.Tick(0d);
			Assert.That(startup.State, Is.EqualTo(SystemState.Online));
			Assert.That(midiProbeCount, Is.EqualTo(2));
			Assert.That(displayProbeCount, Is.EqualTo(2));

			midi = CapabilityStatus.Unavailable("midi",
				new Diagnostic(new DiagnosticCode("test.midi.disconnected"), Severity.Warning, "MIDI disconnected."));
			supervisor.Tick(0.5d);
			Assert.That(startup.State, Is.EqualTo(SystemState.Online), "The supervisor must not probe every frame.");
			supervisor.Tick(1d);
			Assert.That(startup.State, Is.EqualTo(SystemState.Degraded));
			Assert.That(startup.HandshakeReport.Midi.Diagnostic.Code.Value, Is.EqualTo("test.midi.disconnected"));
			Assert.That(midiProbeCount, Is.EqualTo(3));
			Assert.That(displayProbeCount, Is.EqualTo(3));
		}

		[Test]
		public void StartupShutdownDrainsStopsAndTearsDownInOrder() {
			var order = new List<string>();
			var startup = new StartupSequence();
			Assert.That(startup.Run(
				() => Result.Success(),
				() => {
					startup.RegisterShutdown(ShutdownStage.Teardown, () => order.Add("teardown"));
					startup.RegisterShutdown(ShutdownStage.Stop, () => order.Add("stop"));
					return Result.Success();
				},
				() => Result<HandshakeReport>.Success(HandshakeReport.Ready),
				() => {
					startup.RegisterShutdown(ShutdownStage.Drain, () => order.Add("drain"));
					return Result.Success();
				}).IsSuccess, Is.True);

			startup.Shutdown();

			Assert.That(startup.State, Is.EqualTo(SystemState.Offline));
			CollectionAssert.AreEqual(new[] { "drain", "stop", "teardown" }, order);
		}

		[Test]
		public void StartupShutdownContinuesAfterOneBoundaryThrows() {
			var order = new List<string>();
			var startup = new StartupSequence();
			Assert.That(startup.Run(
				() => Result.Success(),
				() => {
					startup.RegisterShutdown(ShutdownStage.Stop, () => order.Add("stop"));
					startup.RegisterShutdown(ShutdownStage.Teardown, () => order.Add("teardown"));
					return Result.Success();
				},
				() => Result<HandshakeReport>.Success(HandshakeReport.Ready),
				() => {
					startup.RegisterShutdown(ShutdownStage.Drain, () => throw new InvalidOperationException("drain failed"));
					return Result.Success();
				}).IsSuccess, Is.True);

			startup.Shutdown();

			Assert.That(startup.State, Is.EqualTo(SystemState.Offline));
			Assert.That(startup.LastDiagnostic?.Code.Value, Is.EqualTo("bootstrap.shutdown.phase_failed"));
			CollectionAssert.AreEqual(new[] { "stop", "teardown" }, order);
		}

		[Test]
		public void ApplicationRegistryUsesImmutableDefinitionsWithoutCreatingDisposableSessionBindings() {
			var provider = new RecordingProvider();
			var registry = new NodeTypeRegistry();
			var catalog = NodeDefinitionCatalog.CreateInitial();
			Assert.That(NodeCatalogBootstrap.EnsureDefinitions(catalog, registry).IsSuccess, Is.True);
			Assert.That(provider.CreateCount, Is.EqualTo(0));
			Assert.That(registry.Definitions, Is.Not.Null);
		}

		[Test]
		public void BootstrapCatalogContainsAllInitialNodeDefinitions() {
			var catalog = NodeDefinitionCatalog.CreateInitial();
			Assert.That(catalog.Entries.Count, Is.EqualTo(23));
			var registry = new NodeTypeRegistry();
			Assert.That(NodeCatalogBootstrap.EnsureDefinitions(catalog, registry).IsSuccess, Is.True);
			Assert.That(registry.Definitions.Count, Is.EqualTo(23));
		}

		[Test]
		public void DriverSupervisesCapabilitiesThenCallsInputReadApplyPresentOncePerLateUpdate() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
			var order = new List<string>();
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Loop", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var supervisor = new CapabilitySupervisor(
						() => { order.Add("midi"); return Result<CapabilityStatus>.Success(CapabilityStatus.Ready("midi")); },
						() => { order.Add("display"); return Result<CapabilityStatus>.Success(CapabilityStatus.Ready("display")); });
					var driver = new ApplicationLoopDriverCore(app, new RecordingInput(order), new RecordingPresentation(order), new RecordingTiming(order), supervisor);
					try {
						Assert.That(driver.LateUpdate(1.0), Is.Not.Null);
						Assert.That(driver.TickCount, Is.EqualTo(1));
						CollectionAssert.AreEqual(new[] { "midi", "display", "input", "read", "apply", "present", "timing" }, order);
						driver.Dispose();
						Assert.That(driver.LateUpdate(2.0), Is.Null);
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[TestCase(60)]
		[TestCase(90)]
		[TestCase(117)]
		[TestCase(120)]
		public void DriverTicksOncePerLateUpdateAtSupportedHostCadences(int hostFramesPerSecond) {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.CadencePerLateUpdate", hostFramesPerSecond.ToString(), Guid.NewGuid().ToString("N"));
			var order = new List<string>();
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Cadence", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var driver = new ApplicationLoopDriverCore(app, new RecordingInput(order), new RecordingPresentation(order), new RecordingTiming(order));
					try {
						var presented = new List<ulong>();
						for (var hostFrame = 0; hostFrame < hostFramesPerSecond; hostFrame++) {
							var frame = driver.LateUpdate(hostFrame / (double)hostFramesPerSecond);
							Assert.That(frame, Is.Not.Null, "Every normal LateUpdate must execute one Application Tick.");
							presented.Add(frame.FrameNumber);
						}
						Assert.That(driver.TickCount, Is.EqualTo(hostFramesPerSecond));
						CollectionAssert.AreEqual(Enumerable.Range(1, hostFramesPerSecond).Select(value => (ulong)value), presented);
						Assert.That(order.Count(value => value == "input"), Is.EqualTo(hostFramesPerSecond));
						Assert.That(order.Count(value => value == "present"), Is.EqualTo(hostFramesPerSecond));
						Assert.That(order.Count(value => value == "timing"), Is.EqualTo(hostFramesPerSecond));
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void DriverPerformsOneTickForEachLateUpdateEvenAfterAStall() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.CadenceStall", Guid.NewGuid().ToString("N"));
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Stalled cadence", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var driver = new ApplicationLoopDriverCore(app, new NullApplicationInputPoller(), new NullPresentationFrame());
					try {
						var first = driver.LateUpdate(0d);
						var afterStall = driver.LateUpdate(10d);
						var resumed = driver.LateUpdate(10.001d);

						Assert.That(first, Is.Not.Null);
						Assert.That(afterStall, Is.Not.Null);
						Assert.That(resumed, Is.Not.Null);
						Assert.That(driver.TickCount, Is.EqualTo(3));
						CollectionAssert.AreEqual(new[] { 1UL, 2UL, 3UL }, new[] { first.FrameNumber, afterStall.FrameNumber, resumed.FrameNumber });
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void DriverPassesNonFiniteMonotonicTimeToTheApplicationContract() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.CadenceNonFinite", Guid.NewGuid().ToString("N"));
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Non-finite cadence", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var driver = new ApplicationLoopDriverCore(app, new NullApplicationInputPoller(), new NullPresentationFrame());
					try {
						// NaN is the existing Application.Tick sentinel for
						// reading its own clock; Infinity reaches GraphClock's
						// finite-value contract and returns a failed frame.
						var nan = driver.LateUpdate(double.NaN);
						var infinity = driver.LateUpdate(double.PositiveInfinity);
						var finite = driver.LateUpdate(0d);

						Assert.That(nan, Is.Not.Null);
						Assert.That(nan.Succeeded, Is.True);
						Assert.That(infinity, Is.Not.Null);
						Assert.That(infinity.Succeeded, Is.False);
						Assert.That(finite, Is.Not.Null);
						Assert.That(finite.Succeeded, Is.True);
						Assert.That(driver.TickCount, Is.EqualTo(3));
						CollectionAssert.AreEqual(new[] { 1UL, 2UL, 3UL }, new[] { nan.FrameNumber, infinity.FrameNumber, finite.FrameNumber });
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void SchedulerRejectsReentryAndStopsAfterDispose() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.CadenceLifecycle", Guid.NewGuid().ToString("N"));
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Scheduler lifecycle", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					ApplicationLoopDriverCore driver = null;
					var nestedCalls = 0;
					var input = new CallbackInput(() => {
						nestedCalls++;
						Assert.That(driver.LateUpdate(1d), Is.Null, "A callback cannot re-enter an active Tick.");
					});
					driver = new ApplicationLoopDriverCore(app, input, new NullPresentationFrame());
					try {
						Assert.That(driver.LateUpdate(0d), Is.Not.Null);
						Assert.That(nestedCalls, Is.EqualTo(1));
						Assert.That(driver.TickCount, Is.EqualTo(1));
						driver.Dispose();
						Assert.That(driver.LateUpdate(2d), Is.Null);
						Assert.That(driver.TickCount, Is.EqualTo(1));
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void DelayedFrameTimingCompletion_PublishesAtTheFollowingFrameBoundaryAndEmptyPollsDoNotEraseIt() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
			try {
				using (var app = new ProjectApplication(new LocalProjectFileSystem())) {
					Assert.That(app.NewProject("Delayed Timing", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var timing = new DelayedTiming();
					var driver = new ApplicationLoopDriverCore(app, new RecordingInput(new List<string>()), new RecordingPresentation(new List<string>()), timing);
					try {
						driver.LateUpdate(1d);
						Assert.That(double.IsNaN(app.ReadModel.Output.Model.CpuFrameTimeMilliseconds), Is.True, "An empty completion poll is not an unavailable timing sample.");
						driver.LateUpdate(2d);
						var versionBeforeCompletion = app.ReadModel.Project.ReadModelVersion;
						driver.LateUpdate(3d);
						Assert.That(app.ReadModel.Project.ReadModelVersion, Is.EqualTo(versionBeforeCompletion + 1),
							"A late FrameTiming completion must not cause a second full ReadModel projection after this host frame has already ticked.");
						Assert.That(double.IsNaN(app.ReadModel.Output.Model.CpuFrameTimeMilliseconds), Is.True,
							"A completion recorded after presentation must wait for the following application frame instead of rebuilding the full ReadModel twice in one host frame.");

						driver.LateUpdate(4d);
						Assert.That(app.ReadModel.Output.Model.PerformanceFrameNumber, Is.EqualTo(1UL));
						Assert.That(app.ReadModel.Output.Model.CpuFrameTimeMilliseconds, Is.EqualTo(8d));
						Assert.That(app.ReadModel.Output.Model.GpuFrameTimeMilliseconds, Is.EqualTo(7d));

						driver.LateUpdate(5d);
						Assert.That(app.ReadModel.Output.Model.PerformanceFrameNumber, Is.EqualTo(1UL), "A later empty poll must retain, not duplicate or erase, the completed sample.");
						Assert.That(app.ReadModel.Output.Model.CpuFrameTimeMilliseconds, Is.EqualTo(8d));
						Assert.That(timing.CompletedSamplesReturned, Is.EqualTo(1));
					}
					finally { driver.Dispose(); }
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void FrameTimingCompletionCorrelation_BoundsDelayedPollsAndRejectsInvalidOrDuplicateIdentities() {
			CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, Enumerable.Range(0, 4)
				.Select(ordinal => FrameTimingCompletionCorrelation.OldestFirstIndex(4, ordinal)).ToArray(),
				"Unity FrameTiming history is newest-first; completion correlation consumes it oldest-first.");
			Assert.That(FrameTimingCompletionCorrelation.OldestFirstIndex(4, 4), Is.EqualTo(-1));
			Assert.That(FrameTimingCompletionCorrelation.OldestFirstIndex(0, 0), Is.EqualTo(-1));

			var correlation = new FrameTimingCompletionCorrelation();
			correlation.RecordPresentation(10UL, 1d);
			correlation.RecordPresentation(11UL, 1.1d);
			correlation.RecordPresentation(12UL, 1.2d);
			correlation.RecordPresentation(13UL, 1.3d);

			// The source always records the current presentation before it
			// consumes Unity's delayed result. This direct trace proves F5
			// remains pending when it completes F1, through F9->F5.
			for (var current = 14UL; current <= 18UL; current++) {
				correlation.RecordPresentation(current, current / 10d);
				Assert.That(correlation.TryComplete(100d + current, 8d, 7d, out var completed), Is.True);
				Assert.That(completed.FrameNumber, Is.EqualTo(current - 4UL));
				Assert.That(correlation.PendingCount, Is.EqualTo(FrameTimingCompletionCorrelation.CompletionDelayFrames));
			}

			Assert.That(correlation.TryComplete(118d, 8d, 7d, out _), Is.False, "The same Unity completed timing must never be published twice.");
			Assert.That(correlation.TryComplete(0d, 8d, 7d, out _), Is.False, "A zero FrameTiming identity is not a completed Unity frame.");
			Assert.That(correlation.TryComplete(double.NaN, 8d, 7d, out _), Is.False);
			Assert.That(correlation.TryComplete(double.PositiveInfinity, 8d, 7d, out _), Is.False);

			// A scalar public read-model poll may receive a full Unity
			// history. It publishes one oldest unseen completion per poll,
			// so all sixteen retained boundaries must remain joinable rather
			// than being dropped behind a private completion FIFO.
			var scalarHistoryCorrelation = new FrameTimingCompletionCorrelation();
			for (var frame = 101UL; frame <= 100UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++)
				scalarHistoryCorrelation.RecordPresentation(frame, frame / 10d);
			for (var frame = 101UL; frame <= 100UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++) {
				Assert.That(scalarHistoryCorrelation.TryComplete(1000d + frame, 8d, 7d, out var scalarCompleted), Is.True);
				Assert.That(scalarCompleted.FrameNumber, Is.EqualTo(frame));
			}
			Assert.That(scalarHistoryCorrelation.PendingCount, Is.EqualTo(0));

			// A four-frame delay is normal and a finite extra window tolerates
			// intermittent GPU availability. Once that bounded window is
			// exhausted, the oldest presentation becomes exactly one public
			// unavailable observation instead of growing forever.
			var unavailableCorrelation = new FrameTimingCompletionCorrelation();
			for (var frame = 14UL; frame < 14UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++) {
				unavailableCorrelation.RecordPresentation(frame, frame / 10d);
				Assert.That(unavailableCorrelation.TryExpire(out _), Is.False,
					"Intermittent empty FrameTiming polls inside the finite jitter window are not a completed unavailable frame.");
			}
			Assert.That(unavailableCorrelation.TryComplete(400d, 9d, 6d, out var delayed), Is.True,
				"A valid completion after intermittent empty polls must still correlate to its original presentation.");
			Assert.That(delayed.FrameNumber, Is.EqualTo(14UL));
			for (var frame = 30UL; frame < 80UL; frame++) {
				unavailableCorrelation.RecordPresentation(frame, frame / 10d);
				if (unavailableCorrelation.TryExpire(out var expired)) {
					Assert.That(expired.IsAvailable, Is.False);
					Assert.That(expired.FrameNumber, Is.LessThan(frame));
				}
				Assert.That(unavailableCorrelation.PendingCount, Is.LessThanOrEqualTo(FrameTimingCompletionCorrelation.MaximumPendingFrames));
			}

			Assert.That(unavailableCorrelation.TryExpire(out _), Is.False, "An expired presentation is dequeued once; repeated expiry polls cannot duplicate it.");
		}

		[Test]
		public void FrameTimingExpiration_AdvancesCadenceBoundaryBeforeNextValidCompletion() {
			const double frameInterval = 1d / 60d;
			var correlation = new FrameTimingCompletionCorrelation();

			// F0 is the bootstrap observation; F1 is the first successful
			// sample and establishes the 60 Hz cadence.
			correlation.RecordPresentation(0UL, 0d);
			Assert.That(correlation.TryComplete(100d, 8d, 7d, out var bootstrap), Is.True);
			Assert.That(bootstrap.IsAvailable, Is.False);
			correlation.RecordPresentation(1UL, frameInterval);
			Assert.That(correlation.TryComplete(101d, 8d, 7d, out var first), Is.True);
			Assert.That(first.IsAvailable, Is.True);
			Assert.That(first.FramesPerSecond, Is.EqualTo(60d).Within(0.0001d));

			// F2 is missing. Fill the finite pending window so that F2 is
			// retired as unavailable, then present F19 before F3's delayed
			// completion is consumed, matching the production poll order.
			correlation.RecordPresentation(2UL, 2d * frameInterval);
			for (var frame = 3UL; frame <= 18UL; frame++)
				correlation.RecordPresentation(frame, frame * frameInterval);
			Assert.That(correlation.TryExpire(out var missing), Is.True);
			Assert.That(missing.FrameNumber, Is.EqualTo(2UL));
			Assert.That(missing.IsAvailable, Is.False);

			correlation.RecordPresentation(19UL, 19d * frameInterval);
			Assert.That(correlation.TryComplete(103d, 8d, 7d, out var afterGap), Is.True);
			Assert.That(afterGap.FrameNumber, Is.EqualTo(3UL));
			Assert.That(afterGap.IsAvailable, Is.True);
			Assert.That(afterGap.FramesPerSecond, Is.EqualTo(60d).Within(0.0001d),
				"The next valid completion must use the expired F2 boundary, not the older successful F1 boundary.");
		}

		[Test]
		public void FrameTimingCompletionCorrelation_RejectsLateMonotonicStaleIdentityAfterExpiry() {
			const double frameInterval = 1d / 60d;
			var correlation = new FrameTimingCompletionCorrelation();
			correlation.RecordPresentation(0UL, 0d);
			Assert.That(correlation.TryComplete(100d, 8d, 7d, out _), Is.True);
			correlation.RecordPresentation(1UL, frameInterval);
			Assert.That(correlation.TryComplete(101d, 8d, 7d, out _), Is.True);

			correlation.RecordPresentation(2UL, 2d * frameInterval);
			for (var frame = 3UL; frame <= 18UL; frame++)
				correlation.RecordPresentation(frame, frame * frameInterval);
			Assert.That(correlation.TryExpire(out var expired), Is.True);
			Assert.That(expired.FrameNumber, Is.EqualTo(2UL));

			// F3 is accepted first. A later raw identity for the expired F2
			// is older than the consumed identity and must not consume F4's
			// boundary with F2's CPU/GPU values.
			correlation.RecordPresentation(19UL, 19d * frameInterval);
			Assert.That(correlation.TryComplete(103d, 8d, 7d, out var third), Is.True);
			Assert.That(third.FrameNumber, Is.EqualTo(3UL));
			var pendingBeforeStale = correlation.PendingCount;
			Assert.That(correlation.TryComplete(102d, 99d, 98d, out _), Is.False,
				"A late completion older than the last consumed identity is stale, not a completion for the next pending frame.");
			Assert.That(correlation.PendingCount, Is.EqualTo(pendingBeforeStale));

			correlation.RecordPresentation(20UL, 20d * frameInterval);
			Assert.That(correlation.TryComplete(104d, 8d, 7d, out var fourth), Is.True);
			Assert.That(fourth.FrameNumber, Is.EqualTo(4UL));
			Assert.That(fourth.CpuFrameMilliseconds, Is.EqualTo(8d));
			Assert.That(fourth.GpuFrameMilliseconds, Is.EqualTo(7d));
		}

		[Test]
		public void UnityFrameTimingHistory_ReplaysOldestUnseenCompletionOnePerPollWithoutPrivateBatching() {
			var history = new ReplayedFrameTimingHistory();
			var time = 0d;
			var source = new FrameTimingSource(history, () => ++time);

			// Unity returns element zero as newest. The same sixteen-entry
			// history is replayed on every poll; the source must therefore
			// skip its seen entries and expose F1 through F16 exactly once,
			// oldest first, without expiring while valid backlog remains.
			for (var frame = 1UL; frame <= FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++) {
				Assert.That(source.TryReadCompleted(frame, out var sample), Is.True);
				Assert.That(sample.FrameNumber, Is.EqualTo(frame));
				// The first source completion has no preceding presentation
				// timestamp from which FPS can be calculated. Runtime's
				// existing all-metrics contract correctly marks that bootstrap
				// sample unavailable; it is still consumed, not an expiry.
				if (frame == 1UL) Assert.That(sample.IsAvailable, Is.False);
				else Assert.That(sample.IsAvailable, Is.True);
				Assert.That(source.PendingCount, Is.EqualTo(0),
					"Each scalar poll consumes exactly one history completion and leaves no private completion backlog.");
			}

			// Once the history contains only a duplicate and invalid identities,
			// no valid completion may be substituted. Pending presentations stay
			// finite and the first overdue one is published exactly once as
			// unavailable.
			history.ReturnOnlyDuplicateAndInvalidEntries = true;
			for (var frame = 17UL; frame < 17UL + FrameTimingCompletionCorrelation.MaximumPendingFrames; frame++) {
				Assert.That(source.TryReadCompleted(frame, out _), Is.False);
				Assert.That(source.PendingCount, Is.LessThanOrEqualTo(FrameTimingCompletionCorrelation.MaximumPendingFrames));
			}
			Assert.That(source.TryReadCompleted(33UL, out var expired), Is.True);
			Assert.That(expired.IsAvailable, Is.False);
			Assert.That(expired.FrameNumber, Is.EqualTo(17UL));
			Assert.That(source.PendingCount, Is.EqualTo(FrameTimingCompletionCorrelation.MaximumPendingFrames));
			Assert.That(source.LastDiagnostic.Outcome, Is.EqualTo("Expired"));
			Assert.That(source.LastDiagnostic.CandidateOutcome, Is.EqualTo("Duplicate"));
		}

		[Test]
		public void UnityFrameTimingHistory_RawInvalidTimingPublishesOneUnavailableOriginalBoundary() {
			var source = new FrameTimingSource(new RawInvalidFrameTimingHistory(), () => 1d);

			Assert.That(source.TryReadCompleted(41UL, out var unavailable), Is.True);
			Assert.That(unavailable.IsAvailable, Is.False);
			Assert.That(unavailable.FrameNumber, Is.EqualTo(41UL),
				"A GPU-zero raw timing belongs to the oldest pending presentation; it must not be skipped and joined to a newer frame.");
			Assert.That(source.LastDiagnostic.Outcome, Is.EqualTo("RawInvalid"));
			Assert.That(source.LastDiagnostic.RawIdentity, Is.EqualTo(200d));
			Assert.That(source.LastDiagnostic.RawGpuMilliseconds, Is.EqualTo(0d));
			Assert.That(source.LastDiagnostic.PerformanceFrameNumber, Is.EqualTo(41UL));
			Assert.That(source.PendingCount, Is.EqualTo(0));

			Assert.That(source.TryReadCompleted(42UL, out _), Is.False,
				"The same invalid Unity history identity is seen once and cannot publish a second unavailable boundary.");
			Assert.That(source.LastDiagnostic.Outcome, Is.EqualTo("Duplicate"));
			Assert.That(source.PendingCount, Is.EqualTo(1));

			var cpuInvalid = new FrameTimingSource(new RawInvalidFrameTimingHistory(201d, double.NaN, 7d), () => 2d);
			Assert.That(cpuInvalid.TryReadCompleted(43UL, out var cpuUnavailable), Is.True);
			Assert.That(cpuUnavailable.IsAvailable, Is.False);
			Assert.That(cpuUnavailable.FrameNumber, Is.EqualTo(43UL));
			Assert.That(cpuInvalid.LastDiagnostic.Outcome, Is.EqualTo("RawInvalid"));
			Assert.That(double.IsNaN(cpuInvalid.LastDiagnostic.RawCpuMilliseconds), Is.True);
		}

		[Test]
		public void UnityFrameTimingHistory_UsesCpuCriticalPathAndRetainsRawWaitDiagnostics() {
			Assert.That(FrameTimingCompletionCorrelation.ComputeCpuWorkloadMilliseconds(8d, 10d), Is.EqualTo(10d));
			Assert.That(FrameTimingCompletionCorrelation.ComputeCpuWorkloadMilliseconds(0d, 10d), Is.EqualTo(10d),
				"A zero main-thread value falls back to the positive render-thread workload.");
			Assert.That(FrameTimingCompletionCorrelation.ComputeCpuWorkloadMilliseconds(double.NaN, 9d), Is.EqualTo(9d),
				"An invalid main-thread value falls back to the positive render-thread workload.");
			Assert.That(double.IsNaN(FrameTimingCompletionCorrelation.ComputeCpuWorkloadMilliseconds(0d, double.NaN)), Is.True,
				"When both CPU workload sources are invalid, the timing must remain unavailable rather than using wait-inclusive total CPU.");

			var time = 0d;
			var source = new FrameTimingSource(new CpuWorkloadFrameTimingHistory(), () => ++time);
			Assert.That(source.TryReadCompleted(41UL, out var bootstrap), Is.True);
			Assert.That(bootstrap.IsAvailable, Is.False, "The first completion still has no preceding presentation timestamp.");

			Assert.That(source.TryReadCompleted(42UL, out var sample), Is.True);
			Assert.That(sample.IsAvailable, Is.True);
			Assert.That(sample.FrameNumber, Is.EqualTo(42UL));
			Assert.That(sample.CpuFrameMilliseconds, Is.EqualTo(10d),
				"Public CPU must be max(main=8, render=10), not total cpuFrameTime=20.");
			Assert.That(sample.CpuWorkloadMilliseconds, Is.EqualTo(10d));
			Assert.That(sample.GpuFrameMilliseconds, Is.EqualTo(7d));

			var diagnostic = source.LastDiagnostic;
			Assert.That(diagnostic.RawCpuFrameTimeMilliseconds, Is.EqualTo(20d));
			Assert.That(diagnostic.RawCpuMainThreadFrameTimeMilliseconds, Is.EqualTo(8d));
			Assert.That(diagnostic.RawCpuRenderThreadFrameTimeMilliseconds, Is.EqualTo(10d));
			Assert.That(diagnostic.RawCpuMainThreadPresentWaitMilliseconds, Is.EqualTo(12d));
			Assert.That(diagnostic.RawGpuMilliseconds, Is.EqualTo(7d));
			Assert.That(diagnostic.RawCpuMilliseconds, Is.EqualTo(20d),
				"The legacy raw CPU alias remains explicitly the total value.");
		}

		[Test]
		public void UnityFrameTimingHistory_ApiExceptionIsReportedWithoutInventingACompletion() {
			var source = new FrameTimingSource(new ThrowingFrameTimingHistory(), () => 1d);
			Assert.That(source.TryReadCompleted(51UL, out _), Is.False);
			Assert.That(source.LastDiagnostic.Outcome, Is.EqualTo("ApiException"));
			Assert.That(source.LastDiagnostic.CandidateOutcome, Is.EqualTo("None"));
			Assert.That(source.LastDiagnostic.ExceptionType, Is.EqualTo(typeof(InvalidOperationException).Name));
			Assert.That(source.LastDiagnostic.PerformanceFrameNumber, Is.EqualTo(0UL));
			Assert.That(source.PendingCount, Is.EqualTo(1));
		}

		[Test]
		public void PreviewOpenPresentationCommandSelectsOneFocusedRuntimeDemand() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
			var previews = Enumerable.Range(0, 2).Select(index => new NodeRecord(NodeInstanceId.New(), new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview " + index, true, new ProjectPosition(index, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) })).ToList();
			var created = ProjectDocumentFactory.TryCreate("Preview Focus", 1, nodes: previews, connections: Array.Empty<ConnectionRecord>(), logicalControls: Array.Empty<LogicalControlRecord>(), expressions: Array.Empty<ParameterExpressionRecord>(), presets: Array.Empty<PresetRecord>(), mediaAssets: Array.Empty<MediaAssetRecord>(), ui: new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }), markDirty: false);
			Assert.That(created.IsSuccess, Is.True, created.Diagnostic?.Message);
			try {
				Assert.That(new ProjectSaver().Save(created.Value, target, new LocalProjectFileSystem()).IsSuccess, Is.True);
				using (var application = new ProjectApplication(new LocalProjectFileSystem()))
				using (var adapter = new ApplicationPresentationAdapter(application, application)) {
					Assert.That(application.OpenProject(target).IsSuccess, Is.True);
					var ids = application.ReadModel.Graph.Model.Nodes.Where(x => x.TypeId == GraphConstants.PreviewTypeId).Select(x => x.Id).Take(2).ToList();
					var session = application.ProjectSessionId;
					adapter.Submit(new PresentationCommandRequest(session, Guid.NewGuid(), Guid.NewGuid(), application.ReadModel.Project.DocumentRevision, ids[0], "preview.open", new[] { new KeyValuePair<string, string>("previewId", ids[0]) }));
					adapter.Submit(new PresentationCommandRequest(session, Guid.NewGuid(), Guid.NewGuid(), application.ReadModel.Project.DocumentRevision, ids[1], "preview.open", new[] { new KeyValuePair<string, string>("previewId", ids[1]) }));
					application.Tick(0d);
					Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == ids[0]).IsFocused, Is.False);
					Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == ids[1]).IsFocused, Is.True);
					Assert.That(application.ReadModel.Output.Model.Previews.All(x => x.IsDemanded), Is.True);
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void ApplicationPresentationAdapter_ReusesShellAndWorkspaceSourcesWhileOuterVersionsAdvance() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.AdapterStaticSlices", Guid.NewGuid().ToString("N"));
			try {
				using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
					var userSettings = new InMemoryUserSettingsPort();
					using (var adapter = new ApplicationPresentationAdapter(application, application, userSettings: userSettings)) {
						Assert.That(application.NewProject("Adapter static slices", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
						application.Tick(0d);
						var first = adapter.ReadLatest(false);
						var shell = first.Model.Shell;
						var workspace = first.Model.Workspace;
						for (var frame = 1; frame <= 100; frame++) application.Tick(frame / 60d);
						var stable = adapter.ReadLatest(false);
						Assert.That(stable.ReadModelVersion, Is.GreaterThan(first.ReadModelVersion));
						Assert.That(stable.Model.Shell, Is.SameAs(shell));
						Assert.That(stable.Model.Workspace, Is.SameAs(workspace));

						Assert.That(application.SetWorkspaceLayout("adapter-layout", true).IsSuccess, Is.True);
						var layoutChanged = adapter.ReadLatest(false);
						Assert.That(layoutChanged.Model.Shell, Is.SameAs(shell));
						Assert.That(layoutChanged.Model.Workspace, Is.Not.SameAs(workspace));

						Assert.That(userSettings.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.25f)).IsSuccess, Is.True);
						var settingsChanged = adapter.ReadLatest(false);
						Assert.That(settingsChanged.Model.Workspace, Is.Not.SameAs(layoutChanged.Model.Workspace),
							"A successful user-settings snapshot replacement must invalidate the adapter Workspace projection once.");
					}
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void ApplicationPresentationAdapter_ReusesDescribedOutputLeasesUntilTheirGenerationChanges() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.AdapterSurfaceLeases", Guid.NewGuid().ToString("N"));
			var previews = Enumerable.Range(0, 2).Select(index => new NodeRecord(NodeInstanceId.New(), new NodeTypeId(GraphConstants.PreviewTypeId), 1,
				"Preview " + index, true, new ProjectPosition(index, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) })).ToList();
			var created = ProjectDocumentFactory.TryCreate("Adapter surface leases", 1, nodes: previews, connections: Array.Empty<ConnectionRecord>(),
				logicalControls: Array.Empty<LogicalControlRecord>(), expressions: Array.Empty<ParameterExpressionRecord>(), presets: Array.Empty<PresetRecord>(),
				mediaAssets: Array.Empty<MediaAssetRecord>(), ui: new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }), markDirty: false);
			Assert.That(created.IsSuccess, Is.True, created.Diagnostic?.Message);
			try {
				Assert.That(new ProjectSaver().Save(created.Value, target, new LocalProjectFileSystem()).IsSuccess, Is.True);
				var surfaces = new DescribedOutputSurfacePort();
				using (var application = new ProjectApplication(new LocalProjectFileSystem()))
				using (var adapter = new ApplicationPresentationAdapter(application, application, surfaces)) {
					Assert.That(application.OpenProject(target).IsSuccess, Is.True);
					var ids = application.ReadModel.Graph.Model.Nodes.Where(node => node.TypeId == GraphConstants.PreviewTypeId).Select(node => node.Id).Take(2).ToArray();
					Assert.That(ids, Has.Length.EqualTo(2));
					Assert.That(application.OpenPreview(ids[0]).IsSuccess, Is.True);
					Assert.That(application.OpenPreview(ids[1]).IsSuccess, Is.True);
					application.Tick(0d);

					var programTexture = new object();
					var firstPreviewTexture = new object();
					var secondPreviewTexture = new object();
					surfaces.Set("program", 11UL, 1920, 1080, 1UL, programTexture);
					surfaces.Set(ids[0], 21UL, 640, 360, 1UL, firstPreviewTexture);
					surfaces.Set(ids[1], 31UL, 640, 360, 1UL, secondPreviewTexture);

					var first = adapter.ReadLatest(false).Model.Output;
					Assert.That(first.Program.Generation, Is.EqualTo(11UL));
					Assert.That(first.Previews.Count, Is.EqualTo(2));
					Assert.That(surfaces.AcquireCount, Is.EqualTo(3));
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(3));

					for (var index = 0; index < 100; index++) {
						surfaces.AdvanceFrames();
						var stable = adapter.ReadLatest(false).Model.Output;
						Assert.That(stable.Program.Generation, Is.EqualTo(11UL));
						Assert.That(stable.Previews.Single(preview => preview.NodeId == ids[0]).Surface.Generation, Is.EqualTo(21UL));
						Assert.That(stable.Previews.Single(preview => preview.NodeId == ids[1]).Surface.Generation, Is.EqualTo(31UL));
					}
					Assert.That(surfaces.AcquireCount, Is.EqualTo(3), "Descriptor probes must not borrow replacement OutputSurfaceLeases while generation, texture, and descriptor are unchanged.");
					Assert.That(surfaces.ReleaseCount, Is.EqualTo(0));
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(3));

					var replacementTexture = new object();
					surfaces.Set(ids[0], 22UL, 480, 270, 102UL, replacementTexture);
					var replaced = adapter.ReadLatest(false).Model.Output;
					Assert.That(surfaces.AcquireCount, Is.EqualTo(4));
					Assert.That(surfaces.ReleaseCount, Is.EqualTo(1), "The replaced Preview lease must release only after the new generation is acquired.");
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(3));
					var replacedPreview = replaced.Previews.Single(preview => preview.NodeId == ids[0]).Surface;
					Assert.That(replacedPreview.Generation, Is.EqualTo(22UL));
					Assert.That(replacedPreview.Width, Is.EqualTo(480));
					Assert.That(replacedPreview.Height, Is.EqualTo(270));
					Assert.That(replacedPreview.Texture, Is.SameAs(replacementTexture));

					surfaces.Remove(ids[1]);
					adapter.ReadLatest(false);
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(2), "A deleted or hidden Preview descriptor must release its retained adapter lease.");
					surfaces.Clear();
					adapter.ReadLatest(false);
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(0), "Clearing all described surfaces must leave no active adapter-held lease before disposal.");
				}
				Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(0), "Adapter Dispose must remain idempotent after descriptor removal and Clear.");
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void ApplicationPresentationAdapter_LegacySurfacePortPublishesTheFreshSameGenerationFrame() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.LegacySurfaceLease", Guid.NewGuid().ToString("N"));
			try {
				var surfaces = new LegacyOutputSurfacePort();
				using (var application = new ProjectApplication(new LocalProjectFileSystem()))
				using (var adapter = new ApplicationPresentationAdapter(application, application, surfaces)) {
					Assert.That(application.NewProject("Legacy output port", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
					var first = adapter.ReadLatest(false).Model.Output.Program;
					var second = adapter.ReadLatest(false).Model.Output.Program;
					Assert.That(first.Generation, Is.EqualTo(7UL));
					Assert.That(first.FrameNumber, Is.EqualTo(1UL));
					Assert.That(second.Generation, Is.EqualTo(7UL));
					Assert.That(second.FrameNumber, Is.EqualTo(2UL),
						"A legacy same-generation acquisition must publish the fresh acquired frame before that transient lease is released.");
					Assert.That(surfaces.AcquireCount, Is.EqualTo(2));
					Assert.That(surfaces.ReleaseCount, Is.EqualTo(1));
					Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(1));
				}
				Assert.That(surfaces.ActiveLeaseCount, Is.EqualTo(0));
				Assert.That(surfaces.ReleaseCount, Is.EqualTo(2));
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[Test]
		public void PlatformFileAdapterDropsStaleSessionResults() {
			var backend = new RecordingFileDialogBackend();
			using (var adapter = new PlatformFileInteractionAdapter(backend)) {
				var request = new PlatformPathRequest(Guid.NewGuid(), Guid.NewGuid(), PlatformPathRequestKind.MultiFile, "Import");
				PlatformPathResult result = null;
				adapter.PickPath(request, value => result = value);
				backend.Complete(new PlatformPathResult(request.RequestId, Guid.NewGuid(), true, new[] { "C:\\outside.mov" }));
				Assert.That(result, Is.Not.Null);
				Assert.That(result.Succeeded, Is.False);
				Assert.That(result.Error, Does.Contain("stale"));
			}
		}

		[Test]
		public void PlatformFileAdapterCancelMakesLateCallbackInert() {
			var backend = new RecordingFileDialogBackend();
			using (var adapter = new PlatformFileInteractionAdapter(backend)) {
				var request = new PlatformPathRequest(Guid.NewGuid(), Guid.NewGuid(), PlatformPathRequestKind.File, "Open");
				var callbackCount = 0;
				adapter.PickPath(request, _ => callbackCount++);
				adapter.Cancel(request.RequestId);
				backend.Complete(new PlatformPathResult(request.RequestId, request.ProjectSessionId, true, new[] { "C:\\input.mov" }));
				Assert.That(callbackCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void ProjectDisplayNumberIsConvertedToZeroBasedUnityIndexAtBoundary() {
			Assert.That(ProgramDisplayPolicy.ToUnityIndex(1), Is.EqualTo(0));
			Assert.That(ProgramDisplayPolicy.ToUnityIndex(2), Is.EqualTo(1));
			Assert.Throws<ArgumentOutOfRangeException>(() => ProgramDisplayPolicy.ToUnityIndex(0));
		}

		private sealed class DescribedOutputSurfacePort : IOutputSurfaceDescriptorPort {
			private sealed class Entry {
				public ulong Generation;
				public int Width;
				public int Height;
				public ulong Frame;
				public object Texture;
			}

			private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
			public int AcquireCount { get; private set; }
			public int ReleaseCount { get; private set; }
			public int ActiveLeaseCount { get; private set; }

			public void Set(string surfaceId, ulong generation, int width, int height, ulong frame, object texture) {
				_entries[surfaceId] = new Entry { Generation = generation, Width = width, Height = height, Frame = frame, Texture = texture };
			}

			public void Remove(string surfaceId) => _entries.Remove(surfaceId);
			public void Clear() => _entries.Clear();

			public void AdvanceFrames() {
				foreach (var entry in _entries.Values) entry.Frame++;
			}

			public bool TryDescribe(string surfaceId, out OutputSurfaceDescriptor descriptor) {
				if (!_entries.TryGetValue(surfaceId ?? string.Empty, out var entry)) {
					descriptor = default(OutputSurfaceDescriptor);
					return false;
				}
				descriptor = new OutputSurfaceDescriptor(surfaceId, entry.Generation, entry.Width, entry.Height, entry.Frame, entry.Texture, true);
				return true;
			}

			public bool TryAcquire(string surfaceId, out OutputSurfaceLease lease) {
				if (!TryDescribe(surfaceId, out var descriptor)) {
					lease = null;
					return false;
				}
				AcquireCount++;
				ActiveLeaseCount++;
				lease = new OutputSurfaceLease(descriptor.SurfaceId, descriptor.Generation, descriptor.Width, descriptor.Height, descriptor.FrameNumber, descriptor.Texture,
					() => { ReleaseCount++; ActiveLeaseCount--; });
				return true;
			}
		}

		private sealed class LegacyOutputSurfacePort : IOutputSurfacePort {
			private readonly object _texture = new object();
			private ulong _frame;
			public int AcquireCount { get; private set; }
			public int ReleaseCount { get; private set; }
			public int ActiveLeaseCount { get; private set; }

			public bool TryAcquire(string surfaceId, out OutputSurfaceLease lease) {
				AcquireCount++;
				ActiveLeaseCount++;
				lease = new OutputSurfaceLease(surfaceId, 7UL, 1920, 1080, ++_frame, _texture,
					() => { ReleaseCount++; ActiveLeaseCount--; });
				return true;
			}
		}

		private sealed class RecordingProvider : IVisualBindingProvider {
			public int CreateCount;
			public Result<VisualBindingSet> Create(string sessionId) {
				CreateCount++;
				return Result<VisualBindingSet>.Failure(new Diagnostic(new DiagnosticCode("test.unexpected_session_create"), Severity.Error, "Unexpected session binding creation."));
			}
		}
		private static Result RecordSuccessfulPhase(ICollection<string> order, string phase) {
			order.Add(phase);
			return Result.Success();
		}
		private static Result<HandshakeReport> RecordSuccessfulHandshake(ICollection<string> order) {
			order.Add("handshake");
			return Result<HandshakeReport>.Success(HandshakeReport.Ready);
		}
		private sealed class NullPresentationFrame : IApplicationPresentationFrame {
			public void Read(ApplicationFrameResult frame) { }
			public void Apply(ApplicationFrameResult frame) { }
			public void Present(ApplicationFrameResult frame) { }
		}
		private sealed class CallbackInput : IApplicationInputPoller {
			private readonly Action _callback;
			public CallbackInput(Action callback) { _callback = callback ?? throw new ArgumentNullException(nameof(callback)); }
			public void Poll() => _callback();
		}
		private sealed class RecordingInput : IApplicationInputPoller {
			private readonly IList<string> _order;
			public RecordingInput(IList<string> order) { _order = order; }
			public void Poll() { _order.Add("input"); }
		}
		private sealed class RecordingPresentation : IApplicationPresentationFrame {
			private readonly IList<string> _order;
			public RecordingPresentation(IList<string> order) { _order = order; }
			public void Read(ApplicationFrameResult frame) { _order.Add("read"); }
			public void Apply(ApplicationFrameResult frame) { _order.Add("apply"); }
			public void Present(ApplicationFrameResult frame) { _order.Add("present"); }
		}
		private sealed class RecordingTiming : IFrameTimingSource {
			private readonly IList<string> _order;
			public RecordingTiming(IList<string> order) { _order = order; }
			public bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample) {
				_order.Add("timing");
				sample = default(RuntimeFrameTimingSample);
				return false;
			}
		}

		private sealed class DelayedTiming : IFrameTimingSource {
			private int _polls;
			public int CompletedSamplesReturned { get; private set; }
			public bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample) {
				_polls++;
				if (_polls != 3) {
					sample = default(RuntimeFrameTimingSample);
					return false;
				}
				CompletedSamplesReturned++;
				sample = new RuntimeFrameTimingSample(1UL, 60d, 8d, 7d);
				return true;
			}
		}

		private sealed class ReplayedFrameTimingHistory : IUnityFrameTimingHistoryReader {
			public bool ReturnOnlyDuplicateAndInvalidEntries { get; set; }

			public int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination) {
				if (ReturnOnlyDuplicateAndInvalidEntries) {
					destination[0] = new UnityFrameTimingHistoryEntry(1016d, 8d, 7d);
					destination[1] = new UnityFrameTimingHistoryEntry(double.PositiveInfinity, 8d, 7d);
					destination[2] = new UnityFrameTimingHistoryEntry(0d, 8d, 7d);
					return 3;
				}

				// element zero is newest; the source's oldest-first helper
				// must select 1001, then 1002, through 1016 on later polls.
				for (var index = 0; index < destination.Length; index++)
					destination[index] = new UnityFrameTimingHistoryEntry(1016d - index, 8d, 7d);
				return destination.Length;
			}
		}
		private sealed class RawInvalidFrameTimingHistory : IUnityFrameTimingHistoryReader {
			private readonly double _identity;
			private readonly double _cpu;
			private readonly double _gpu;
			public RawInvalidFrameTimingHistory(double identity = 200d, double cpu = 8d, double gpu = 0d) { _identity = identity; _cpu = cpu; _gpu = gpu; }
			public int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination) {
				destination[0] = new UnityFrameTimingHistoryEntry(_identity, _cpu, _gpu);
				return 1;
			}
		}
		private sealed class CpuWorkloadFrameTimingHistory : IUnityFrameTimingHistoryReader {
			private double _identity = 300d;

			public int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination) {
				destination[0] = new UnityFrameTimingHistoryEntry(_identity++, 20d, 8d, 10d, 12d, 7d);
				return 1;
			}
		}
		private sealed class ThrowingFrameTimingHistory : IUnityFrameTimingHistoryReader {
			public int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination) => throw new InvalidOperationException("fixture");
		}
		private sealed class RecordingFileDialogBackend : IPlatformFileDialogBackend {
			private Action<PlatformPathResult> _complete;
			public bool IsSupported => true;
			public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) { _complete = completed; }
			public void Complete(PlatformPathResult result) => _complete?.Invoke(result);
			public void Cancel(Guid requestId) { }
			public void Dispose() { _complete = null; }
		}
	}
}
