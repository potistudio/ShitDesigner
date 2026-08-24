using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;
using ShitDesigner.Runtime;

namespace ShitDesigner.Tests.Runtime {
	public sealed class RuntimeContractTests {
		[Test]
		public void GraphClock_PauseStopsAndResumesWithoutCatchup() {
			var clock = new GraphClock(new ManualSource(0d));
			clock.Update(0d);
			clock.Update(1d);
			Assert.That(clock.Time, Is.EqualTo(1d).Within(0.0001));
			clock.Pause();
			clock.Update(5d);
			Assert.That(clock.Time, Is.EqualTo(1d).Within(0.0001));
			clock.Resume();
			clock.Update(6d);
			Assert.That(clock.Time, Is.EqualTo(2d).Within(0.0001));
		}

		[Test]
		public void GraphClock_CapsPhysicsCatchupAtFourSteps() {
			var clock = new GraphClock(new ManualSource(0d));
			clock.Update(0d);
			var result = clock.Update(1d);
			Assert.That(result.StepCount, Is.EqualTo(4));
			Assert.That(result.RemainderSeconds, Is.GreaterThanOrEqualTo(0d));
		}

		[Test]
		public void PortValue_RejectsDummyImageFrameAndKeepsTypeDiscriminated() {
			Assert.Throws<ArgumentNullException>(() => PortValue.FromImageFrame(null));
			var value = PortValue.FromVector4(new Vector4Value(1, 2, 3, 4));
			Assert.That(value.Type, Is.EqualTo(PortType.Vector4));
			Assert.That(value.AsVector4().W, Is.EqualTo(4));
			Assert.Throws<InvalidOperationException>(() => value.AsFloat());
		}

		[Test]
		public void OutputWriter_RequiresRequestedOutputs() {
			var port = new PortId("image");
			var writer = new NodeOutputWriter(new[] { port });
			Assert.That(writer.SetAvailable(port, PortValue.FromFloat(1f)).IsSuccess, Is.True);
			Assert.That(writer.SetAvailable(port, PortValue.FromFloat(2f)).IsFailure, Is.True);
		}

		[Test]
		public void OutputWriter_SealFreezesThePublishedMapAndRejectsLaterMutation() {
			var port = new PortId("image");
			var writer = new NodeOutputWriter(new[] { port });
			Assert.That(writer.SetAvailable(port, PortValue.FromFloat(1f)).IsSuccess, Is.True);

			var sealedOutputs = (IReadOnlyDictionary<PortId, NodeOutputResult>)typeof(NodeOutputWriter)
				.GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(writer, null);
			Assert.That(writer.Outputs, Is.SameAs(sealedOutputs));
			Assert.That(writer.SetAvailable(port, PortValue.FromFloat(2f)).IsFailure, Is.True);
			Assert.That(sealedOutputs[port].Value.AsFloat(), Is.EqualTo(1f));
		}

		[Test]
		public void Preset_WithBrokenItem_RejectsWholeTransaction() {
			var fixture = CreateParameterFixture();
			var preset = new PresetRecord(PresetId.New(), "Broken", entries: new[]
			{
				new PresetEntryRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float, ParameterValue.FromFloat(0.8f), true, "missing")
			});
			var events = new[] { RuntimeParameterEvent.Preset(1, preset.Id) };
			var result = fixture.DocumentCommands.AddPreset(preset);
			Assert.That(result.IsSuccess, Is.True);
			var commit = fixture.Store.ApplyEvents(events, fixture.Graph, fixture.Document);
			Assert.That(commit.HasFailures, Is.True);
			Assert.That(fixture.Store.BaseValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.2f));
		}

		[Test]
		public void ControlValue_UsesMinMaxExpressionInSnapshot() {
			var fixture = CreateParameterFixture(withExpression: true);
			var commit = fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, fixture.ControlId, 0.75f) }, fixture.Graph, fixture.Document);
			Assert.That(commit.HasFailures, Is.False);
			Assert.That(fixture.Store.EvaluateEffective(fixture.Graph, fixture.Document).IsSuccess, Is.True);
			Assert.That(fixture.Store.EffectiveValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.75f).Within(0.0001));
		}

		[Test]
		public void Synchronize_SeedsAndPreservesValueControlsButExcludesPresetTriggers() {
			var fixture = CreateParameterFixture();
			var triggerId = LogicalControlId.New();
			Assert.That(fixture.DocumentCommands.AddLogicalControl(new LogicalControlRecord(triggerId, "Trigger", LogicalControlKind.PresetTrigger)).IsSuccess, Is.True);

			fixture.Store.Synchronize(fixture.Graph, fixture.Document);
			Assert.That(fixture.Store.ControlValues[fixture.ControlId], Is.EqualTo(0.2f));
			Assert.That(fixture.Store.ControlValues.ContainsKey(triggerId), Is.False);

			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, fixture.ControlId, 0.75f) }, fixture.Graph, fixture.Document).HasFailures, Is.False);
			fixture.Store.Synchronize(fixture.Graph, fixture.Document);
			Assert.That(fixture.Store.ControlValues[fixture.ControlId], Is.EqualTo(0.75f));
			Assert.That(fixture.Store.ControlValues.ContainsKey(triggerId), Is.False);
		}

		[Test]
		public void PresetTrigger_ReleaseRearmsWithoutPublishingNumericControlValue() {
			var fixture = CreateParameterFixture();
			var presetId = PresetId.New();
			var triggerId = LogicalControlId.New();
			var preset = new PresetRecord(presetId, "Triggered", entries: new[]
			{
				new PresetEntryRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float, ParameterValue.FromFloat(0.8f))
			});
			Assert.That(fixture.DocumentCommands.AddPreset(preset).IsSuccess, Is.True);
			Assert.That(fixture.DocumentCommands.AddLogicalControl(new LogicalControlRecord(triggerId, "Trigger", LogicalControlKind.PresetTrigger, presetId: presetId)).IsSuccess, Is.True);

			var firstPress = fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, triggerId, 1f) }, fixture.Graph, fixture.Document);
			Assert.That(firstPress.HasFailures, Is.False);
			Assert.That(firstPress.FiredTriggers, Does.Contain(triggerId));
			Assert.That(fixture.Store.BaseValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.8f));
			Assert.That(fixture.Store.ControlValues.ContainsKey(triggerId), Is.False);

			var release = fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(2, triggerId, 0f) }, fixture.Graph, fixture.Document);
			Assert.That(release.HasFailures, Is.False);
			Assert.That(release.FiredTriggers, Is.Empty);
			Assert.That(fixture.Store.ControlValues.ContainsKey(triggerId), Is.False);

			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.BaseValue(3, fixture.NodeId, fixture.ParameterId, ParameterValue.FromFloat(0.3f)) }, fixture.Graph, fixture.Document).HasFailures, Is.False);
			var secondPress = fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(4, triggerId, 1f) }, fixture.Graph, fixture.Document);
			Assert.That(secondPress.HasFailures, Is.False);
			Assert.That(secondPress.FiredTriggers, Does.Contain(triggerId));
			Assert.That(fixture.Store.BaseValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.8f));
			Assert.That(fixture.Store.ControlValues.ContainsKey(triggerId), Is.False);
		}

		[Test]
		public void ControlRuntimeSnapshot_PublishesValueAndOneFramePresetTriggerPulse() {
			var fixture = CreateParameterFixture();
			var triggerId = LogicalControlId.New();
			var presetId = PresetId.New();
			Assert.That(fixture.DocumentCommands.AddPreset(new PresetRecord(presetId, "Pulse", entries: new[]
			{
				new PresetEntryRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float, ParameterValue.FromFloat(0.8f))
			})).IsSuccess, Is.True);
			Assert.That(fixture.DocumentCommands.AddLogicalControl(new LogicalControlRecord(triggerId, "Pulse", LogicalControlKind.PresetTrigger, presetId: presetId)).IsSuccess, Is.True);

			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, fixture.ControlId, 0.75f), RuntimeParameterEvent.ControlValue(2, triggerId, 1f) }, fixture.Graph, fixture.Document).HasFailures, Is.False);
			Assert.That(fixture.Store.ControlRuntime[fixture.ControlId].HasValue, Is.True);
			Assert.That(fixture.Store.ControlRuntime[fixture.ControlId].Value, Is.EqualTo(0.75f));
			Assert.That(fixture.Store.ControlRuntime[triggerId].HasValue, Is.False);
			Assert.That(fixture.Store.ControlRuntime[triggerId].IsFiring, Is.True, "The accepted trigger must be observable for its public commit frame.");

			fixture.Store.BeginFrame();
			Assert.That(fixture.Store.ControlRuntime[triggerId].IsFiring, Is.False, "The next Runtime frame clears the pulse but keeps the trigger armed until release.");
			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(3, triggerId, 0f) }, fixture.Graph, fixture.Document).HasFailures, Is.False);
			Assert.That(fixture.Store.ControlRuntime.ContainsKey(triggerId), Is.True, "A released PresetTrigger remains visibly Armed until the control is deleted or changes kind.");
			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(4, triggerId, 1f) }, fixture.Graph, fixture.Document).FiredTriggers, Does.Contain(triggerId), "Release must rearm the next press.");
			Assert.That(fixture.DocumentCommands.DeleteLogicalControl(triggerId).IsSuccess, Is.True);
			fixture.Store.Synchronize(fixture.Graph, fixture.Document);
			Assert.That(fixture.Store.ControlRuntime.ContainsKey(triggerId), Is.False, "Deleting a PresetTrigger removes its runtime state entry.");
		}

		[Test]
		public void ControlRuntimeSnapshot_PressThenDeleteBeforeNextFrameRemovesFiringEntry() {
			var fixture = CreateParameterFixture();
			var triggerId = LogicalControlId.New();
			var presetId = PresetId.New();
			Assert.That(fixture.DocumentCommands.AddPreset(new PresetRecord(presetId, "Delete", entries: new[] { new PresetEntryRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float, ParameterValue.FromFloat(0.7f)) })).IsSuccess, Is.True);
			Assert.That(fixture.DocumentCommands.AddLogicalControl(new LogicalControlRecord(triggerId, "Delete", LogicalControlKind.PresetTrigger, presetId: presetId)).IsSuccess, Is.True);
			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, triggerId, 1f) }, fixture.Graph, fixture.Document).FiredTriggers, Does.Contain(triggerId));
			Assert.That(fixture.Store.ControlRuntime[triggerId].IsFiring, Is.True);
			Assert.That(fixture.DocumentCommands.DeleteLogicalControl(triggerId).IsSuccess, Is.True);
			fixture.Store.Synchronize(fixture.Graph, fixture.Document);
			Assert.That(fixture.Store.ControlRuntime.ContainsKey(triggerId), Is.False, "A deleted trigger must not remain visible as Fired until BeginFrame.");
		}

		[Test]
		public void ControlRuntimeSnapshot_CaptureRestoreRollsBackRejectedTriggerPulse() {
			var fixture = CreateParameterFixture();
			var triggerId = LogicalControlId.New();
			var presetId = PresetId.New();
			Assert.That(fixture.DocumentCommands.AddPreset(new PresetRecord(presetId, "Rollback", entries: new[] { new PresetEntryRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float, ParameterValue.FromFloat(0.6f)) })).IsSuccess, Is.True);
			Assert.That(fixture.DocumentCommands.AddLogicalControl(new LogicalControlRecord(triggerId, "Rollback", LogicalControlKind.PresetTrigger, presetId: presetId)).IsSuccess, Is.True);
			fixture.Store.Synchronize(fixture.Graph, fixture.Document);
			var capture = typeof(ParameterStore).GetMethod("CaptureState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var restore = typeof(ParameterStore).GetMethod("RestoreState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.That(capture, Is.Not.Null); Assert.That(restore, Is.Not.Null);
			var prior = capture.Invoke(fixture.Store, null);
			Assert.That(fixture.Store.ApplyEvents(new[] { RuntimeParameterEvent.ControlValue(1, triggerId, 1f) }, fixture.Graph, fixture.Document).FiredTriggers, Does.Contain(triggerId));
			Assert.That(fixture.Store.ControlRuntime[triggerId].IsFiring, Is.True);
			restore.Invoke(fixture.Store, new[] { prior });
			Assert.That(fixture.Store.ControlRuntime[triggerId].IsFiring, Is.False, "A persistence transaction rollback must not publish the rejected trigger pulse.");
			Assert.That(fixture.Store.ControlRuntime.ContainsKey(triggerId), Is.True, "Rollback restores the prior Armed trigger entry.");
		}

		[Test]
		public void FrameCoordinator_PhasesRunExactlyOnceInOrder() {
			var fixture = CreateFrameFixture(false);
			var report = fixture.Coordinator.Tick(0d);
			Assert.That(report.Succeeded, Is.True);
			Assert.That(report.Phases, Is.EqualTo(Enum.GetValues(typeof(RuntimePhase))));
		}

		[Test]
		public void FrameCoordinator_PublishesPreparingNodeStateWhileItsLastFrameRemainsAvailable() {
			var fixture = CreateFrameFixture(false, sourcePreparing: true);
			var report = fixture.Coordinator.Tick(0d);

			Assert.That(report.Succeeded, Is.True);
			Assert.That(fixture.Session.OutputResults[fixture.NodeId].Values.Single().Status, Is.EqualTo(NodeOutputStatus.Available), "The last valid image remains available for presentation.");
			Assert.That(fixture.Session.FindNode(fixture.NodeId).State, Is.EqualTo(RuntimeNodeState.Preparing), "Public graph status must retain the node's asynchronous Preparing transition.");
		}

		[Test]
		public void ParameterResultsCorrelateAndCoalesceContinuousUpdates() {
			var fixture = CreateFrameFixture(false);
			Assert.That(fixture.Coordinator.EnqueueParameterEvent(RuntimeParameterEvent.BaseValue(1, fixture.NodeId, fixture.ParameterId, ParameterValue.FromFloat(0.3f))).IsSuccess, Is.True);
			Assert.That(fixture.Coordinator.EnqueueParameterEvent(RuntimeParameterEvent.BaseValue(2, fixture.NodeId, fixture.ParameterId, ParameterValue.FromFloat(0.8f))).IsSuccess, Is.True);
			var report = fixture.Coordinator.Tick(0d);
			Assert.That(report.ParameterEventResults.Count, Is.EqualTo(2));
			Assert.That(report.ParameterEventResults[0].Status, Is.EqualTo(ParameterEventStatus.Superseded));
			Assert.That(report.ParameterEventResults[1].Status, Is.EqualTo(ParameterEventStatus.Applied));
		}

		[Test]
		public void RuntimeFeedbackResetRejectsWhenServiceIsUnavailable() {
			var fixture = CreateFrameFixture(false);
			var report = fixture.Coordinator.EnqueueRuntimeCommand(RuntimeCommand.ResetFeedback(fixture.NodeId));
			Assert.That(report.IsSuccess, Is.True);
			var frame = fixture.Coordinator.Tick(0d);
			Assert.That(frame.RuntimeCommandResults.Single().Applied, Is.False);
			Assert.That(frame.RuntimeCommandResults.Single().Diagnostic.Code.Value, Is.EqualTo("runtime.feedback.reset_unavailable"));
		}

		[Test]
		public void FrameSnapshot_InputsArrivingAfterBoundaryWaitForNextFrame() {
			var fixture = CreateFrameFixture(false);
			var first = fixture.Coordinator.Tick(0d);
			Assert.That(first.Snapshot.EffectiveValues.Count, Is.GreaterThanOrEqualTo(0));
			Assert.That(fixture.Coordinator.EnqueueParameterEvent(RuntimeParameterEvent.BaseValue(1, fixture.NodeId, fixture.ParameterId, ParameterValue.FromFloat(0.9f))).IsSuccess, Is.True);
			Assert.That(first.Snapshot.EffectiveValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.2f));
			var second = fixture.Coordinator.Tick(1d / 60d);
			Assert.That(second.Snapshot.EffectiveValues[new ParameterKey(fixture.NodeId, fixture.ParameterId)].AsFloat(), Is.EqualTo(0.9f).Within(0.0001));
		}

		[Test]
		public void FrameSnapshot_ReusesStoreSnapshotsUntilTheirEffectiveOrControlValueChanges() {
			var fixture = CreateFrameFixture(false);
			var parameterKey = new ParameterKey(fixture.NodeId, fixture.ParameterId);
			var first = fixture.Coordinator.Tick(0d);
			var stable = fixture.Coordinator.Tick(1d / 60d);

			Assert.That(stable.Snapshot.EffectiveValues, Is.SameAs(first.Snapshot.EffectiveValues));
			Assert.That(stable.Snapshot.ControlValues, Is.SameAs(first.Snapshot.ControlValues));

			var controlId = LogicalControlId.New();
			var target = new LogicalControlTargetRecord(fixture.NodeId, fixture.ParameterId, ParameterType.Float,
				ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f));
			Assert.That(new ProjectCommandProcessor(fixture.Session.Document).AddLogicalControl(
				new LogicalControlRecord(controlId, "Frame snapshot control", LogicalControlKind.Value, 0f, new[] { target })).IsSuccess, Is.True);
			Assert.That(fixture.Coordinator.EnqueueParameterEvent(RuntimeParameterEvent.ControlValue(1, controlId, 0.75f)).IsSuccess, Is.True);
			var controlChanged = fixture.Coordinator.Tick(2d / 60d);

			Assert.That(controlChanged.Snapshot.EffectiveValues, Is.SameAs(stable.Snapshot.EffectiveValues));
			Assert.That(controlChanged.Snapshot.ControlValues, Is.Not.SameAs(stable.Snapshot.ControlValues));
			Assert.That(controlChanged.Snapshot.ControlValues[controlId], Is.EqualTo(0.75f));

			Assert.That(fixture.Coordinator.EnqueueParameterEvent(RuntimeParameterEvent.BaseValue(2, fixture.NodeId, fixture.ParameterId, ParameterValue.FromFloat(0.9f))).IsSuccess, Is.True);
			var effectiveChanged = fixture.Coordinator.Tick(3d / 60d);
			Assert.That(effectiveChanged.Snapshot.EffectiveValues, Is.Not.SameAs(controlChanged.Snapshot.EffectiveValues));
			Assert.That(effectiveChanged.Snapshot.ControlValues, Is.SameAs(controlChanged.Snapshot.ControlValues));
			Assert.That(first.Snapshot.EffectiveValues[parameterKey].AsFloat(), Is.EqualTo(0.2f), "A previous FrameSnapshot must retain its immutable store-owned effective dictionary after later frame mutation.");
			Assert.That(effectiveChanged.Snapshot.EffectiveValues[parameterKey].AsFloat(), Is.EqualTo(0.9f));
			Assert.That(first.Snapshot.OutputDemands, Is.Not.SameAs(stable.Snapshot.OutputDemands), "Phase-4 demand planning keeps per-frame copies even when ParameterStore snapshots are reused.");
		}

		[Test]
		public void FrameSnapshot_IsNotReplacedWhenPhase4RebuildsDemandPlan() {
			var fixture = CreateFrameFixture(false);
			var preparation = new RecordingPreparation();
			fixture.Session.ResourcePreparation = preparation;
			Assert.That(fixture.Session.SetOutputDemands(new[]
			{
				new OutputDemand(OutputTargetKind.Program, fixture.ProgramId, new PortId("image"), 1280, 720)
			}).IsSuccess, Is.True);

			var report = fixture.Coordinator.Tick(0d);

			Assert.That(ReferenceEquals(report.Snapshot, preparation.Snapshot), Is.True);
			Assert.That(ReferenceEquals(report.Snapshot, preparation.Evaluation.Snapshot), Is.True);
			Assert.That(preparation.Evaluation.OutputDemands.First(x => x.TargetKind == OutputTargetKind.Program).Width, Is.EqualTo(1920));
			Assert.That(preparation.Evaluation.OutputDemands.First(x => x.TargetKind == OutputTargetKind.Program).Height, Is.EqualTo(1080));
		}

		[Test]
		public void Phase3Snapshot_KeepsItsOldResolutionProjectionWhilePhase4UsesTheRebuiltProjection() {
			var fixture = CreateFrameFixture(false);
			var preparation = new RecordingPreparation();
			fixture.Session.ResourcePreparation = preparation;

			var first = fixture.Coordinator.Tick(0d);

			Assert.That(RuntimeOutputResolutionDemandAccess.GetAll(first.Snapshot), Is.Empty,
				"Phase 3 must retain the plan projection captured before Phase 4 applies the initial demand.");
			var phase4Entries = RuntimeOutputResolutionDemandAccess.GetAll(preparation.Evaluation);
			Assert.That(phase4Entries.Count, Is.GreaterThan(0));

			var second = fixture.Coordinator.Tick(1d / 60d);
			Assert.That(RuntimeOutputResolutionDemandAccess.GetAll(second.Snapshot), Is.SameAs(phase4Entries),
				"The next Phase-3 snapshot adopts the already-installed immutable projection.");
		}

		[Test]
		public void Phase3AndPhase4_DemandSnapshotsAreFrozenAndReuseTheStableFrameList() {
			var fixture = CreateFrameFixture(false);
			var preparation = new RecordingPreparation();
			fixture.Session.ResourcePreparation = preparation;

			var first = fixture.Coordinator.Tick(0d);
			Assert.That(first.Snapshot.OutputDemands, Is.Empty);
			Assert.That(preparation.Evaluation.OutputDemands.Count, Is.GreaterThan(0));

			var second = fixture.Coordinator.Tick(1d / 60d);
			var third = fixture.Coordinator.Tick(2d / 60d);
			Assert.That(second.Snapshot.OutputDemands, Is.SameAs(preparation.Evaluation.OutputDemands));
			Assert.That(third.Snapshot.OutputDemands, Is.SameAs(second.Snapshot.OutputDemands));
			Assert.That(first.Snapshot.OutputDemands, Is.Empty, "The earlier Phase-3 view must remain immutable after Phase 4 installs a demand list.");
		}

		[Test]
		public void NodeExecutionContext_TrustedInputsAreReadOnlyAndRemainFrozenAfterTheNextFrame() {
			var fixture = CreateFrameFixture(false);
			fixture.Coordinator.Tick(0d);
			var firstContext = fixture.LastHealthyContext;
			Assert.That(firstContext, Is.Not.Null);
			var firstInputs = firstContext.Inputs;
			var mutable = firstInputs as IDictionary<PortId, ResolvedInput>;
			Assert.That(mutable, Is.Not.Null, "The runtime map has a dictionary implementation, so it must actively reject mutation rather than rely on its IReadOnlyDictionary static type.");
			Assert.Throws<NotSupportedException>(() => mutable[new PortId("injected")] = ResolvedInput.Unavailable(new PortId("injected"), PortType.Float,
				new Diagnostic(new DiagnosticCode("test.input.mutation"), Severity.Error, "Mutation must be rejected.")));

			fixture.Coordinator.Tick(1d / 60d);
			Assert.That(firstContext.Inputs, Is.SameAs(firstInputs));
			Assert.That(firstInputs.ContainsKey(new PortId("injected")), Is.False);
		}

		[Test]
		public void RuntimeSession_CachesRequestedPreviewDescriptorsUntilDemandOrQualityChanges() {
			var fixture = CreateFrameFixture(false, connectHealthyPreview: true);
			fixture.Coordinator.Tick(0d);
			var first = fixture.Session.CapturePreviewOutputSnapshots();
			var stable = fixture.Session.CapturePreviewOutputSnapshots();
			Assert.That(stable, Is.SameAs(first));
			Assert.That(fixture.Session.IsPreviewRequested(fixture.PreviewId), Is.True);

			// Presentation frames are intentionally not a descriptor-cache
			// invalidation source. The production bridge observes their frame
			// number separately; repeated descriptor capture stays stable.
			Assert.That(fixture.Session.CapturePreviewOutputSnapshots(), Is.SameAs(first));

			for (var sample = 1UL; sample <= 30UL; sample++)
				fixture.Session.ObservePreviewTiming(fixture.PreviewId, 16d, 16d, sample);
			var degraded = fixture.Session.CapturePreviewOutputSnapshots();
			Assert.That(degraded, Is.Not.SameAs(first));
			Assert.That(degraded.Single().Width, Is.EqualTo(480));
			Assert.That(degraded.Single().Height, Is.EqualTo(270));

			Assert.That(fixture.Session.HideAllPreviews().IsSuccess, Is.True);
			fixture.Coordinator.Tick(31d / 60d);
			var hidden = fixture.Session.CapturePreviewOutputSnapshots();
			Assert.That(hidden, Is.Not.SameAs(degraded));
			Assert.That(hidden, Is.Empty);
			Assert.That(fixture.Session.IsPreviewRequested(fixture.PreviewId), Is.False);
		}

		[Test]
		public void OutputDemandStateCoalescesToLatestAndCannotReplayClosedPreview() {
			var fixture = CreateFrameFixture(false, connectHealthyPreview: true);
			var previewDemand = new OutputDemand(OutputTargetKind.Preview, fixture.PreviewId, new PortId("image"), 640, 360, true);
			for (var index = 0; index < 100; index++) {
				Assert.That(fixture.Session.SetOutputDemands(new[] { previewDemand }).IsSuccess, Is.True);
				Assert.That(fixture.Session.HideAllPreviews().IsSuccess, Is.True);
			}
			Assert.That(fixture.Session.SetOutputDemands(new[] { previewDemand }).IsSuccess, Is.True);
			Assert.That(fixture.Session.RemovePreview(fixture.PreviewId).IsSuccess, Is.True);

			var report = fixture.Coordinator.Tick(0d);

			Assert.That(report.Succeeded, Is.True);
			Assert.That(fixture.Session.RequestedOutputDemands.Any(x => x.TargetKind == OutputTargetKind.Preview), Is.False);
			Assert.That(fixture.Session.OutputDemands.Any(x => x.TargetKind == OutputTargetKind.Preview), Is.False);
		}

		[Test]
		public void DemandAwareNodeReceivesUndemandedTransitionEvenWhenEvaluateIsSkipped() {
			var fixture = CreateFrameFixture(true);
			var first = fixture.Coordinator.Tick(0d);
			Assert.That(first, Is.Not.Null);
			Assert.That(fixture.DemandTransitions, Is.GreaterThanOrEqualTo(1));
			Assert.That(fixture.LastDemanded, Is.True);

			Assert.That(fixture.Session.SetOutputDemands(new[]
			{
				new OutputDemand(OutputTargetKind.Program, fixture.ProgramId, new PortId("image"), 1920, 1080)
			}).IsSuccess, Is.True);
			var second = fixture.Coordinator.Tick(1d / 60d);
			Assert.That(second, Is.Not.Null);
			Assert.That(fixture.LastDemanded, Is.False);
			Assert.That(fixture.DemandTransitions, Is.GreaterThanOrEqualTo(2));
			Assert.That(fixture.DemandEvaluateCount, Is.EqualTo(1), "The preview source is omitted from Phase 6 while its demand is absent.");
		}

		[Test]
		public void NodeFault_DoesNotStopIndependentPreviewBranch() {
			var fixture = CreateFrameFixture(true);
			var report = fixture.Coordinator.Tick(0d);
			Assert.That(report.ProgramState, Is.EqualTo(ProgramRuntimeState.Available));
			Assert.That(report.Presentation.Previews[fixture.PreviewId].Status, Is.EqualTo(NodeOutputStatus.Faulted));
			Assert.That(fixture.HealthyEvaluateCount, Is.EqualTo(1));
		}

		[Test]
		public void HealthyPreviewTerminal_PassesThroughInputImageAlongsideProgram() {
			var fixture = CreateFrameFixture(false, connectHealthyPreview: true);
			var report = fixture.Coordinator.Tick(0d);

			Assert.That(report.ProgramState, Is.EqualTo(ProgramRuntimeState.Available));
			var preview = report.Presentation.Previews[fixture.PreviewId];
			Assert.That(preview.Status, Is.EqualTo(NodeOutputStatus.Available));
			Assert.That(preview.HasValue, Is.True);
			Assert.That(preview.Value.IsImageFrame, Is.True);
			Assert.That(preview.Value.AsImageFrame(), Is.InstanceOf<IRuntimeImageFrame>());
			Assert.That(preview.Value.AsImageFrame().LeaseId, Is.EqualTo(1ul));
		}

		[Test]
		public void PreviewPresentation_HoldsLastAvailableFrameWhenItsNextUpdateIsNotDue() {
			var fixture = CreateFrameFixture(false, connectHealthyPreview: true);

			var due = fixture.Coordinator.Tick(0d);
			Assert.That(due.Presentation.Previews[fixture.PreviewId].Status, Is.EqualTo(NodeOutputStatus.Available));
			Assert.That(fixture.Session.LastPresentation.Previews[fixture.PreviewId].Status, Is.EqualTo(NodeOutputStatus.Available),
				"The Phase-8 projection must be published for presentation consumers after a due Preview frame.");
			FrameExecutionReport nonDue = null;
			const int cadenceObservationDeadlineFrames = 8;
			for (var tick = 1; tick <= cadenceObservationDeadlineFrames; tick++) {
				var candidate = fixture.Coordinator.Tick(tick / 60d);
				// FrameSnapshot is deliberately captured before Phase 4. The
				// session demand projection is therefore the current-frame
				// evidence for whether this Preview was due.
				if (!fixture.Session.OutputDemands.Any(x => x.TargetKind == OutputTargetKind.Preview)) {
					nonDue = candidate;
					break;
				}
			}
			Assert.That(nonDue, Is.Not.Null, "Preview update cadence did not produce a non-due frame before the bounded observation deadline.");
			Assert.That(nonDue.Presentation.Previews[fixture.PreviewId].Status, Is.EqualTo(NodeOutputStatus.Available));
			Assert.That(nonDue.Presentation.Previews[fixture.PreviewId].Value.AsImageFrame().LeaseId, Is.EqualTo(due.Presentation.Previews[fixture.PreviewId].Value.AsImageFrame().LeaseId));
			Assert.That(fixture.Session.LastPresentation.Previews[fixture.PreviewId].Status, Is.EqualTo(NodeOutputStatus.Available),
				"A non-due evaluation must publish the held Preview frame instead of exposing only its empty current evaluation results.");
			Assert.That(fixture.Session.LastPresentation.Previews[fixture.PreviewId].Value.AsImageFrame().LeaseId,
				Is.EqualTo(due.Presentation.Previews[fixture.PreviewId].Value.AsImageFrame().LeaseId));
		}

		[Test]
		public void FeedbackOutput_EvaluatesBeforeProgramAndPresentsAcrossConsecutiveFrames() {
			var document = new ProjectDocument("Feedback Ordering Test");
			var commands = new ProjectCommandProcessor(document);
			var sourceId = new NodeInstanceId("e0000000-0000-4000-8000-000000000000");
			var programId = new NodeInstanceId("10000000-0000-4000-8000-000000000000");
			var feedbackId = new NodeInstanceId("f0000000-0000-4000-8000-000000000000");
			var image = new PortId("image");
			Assert.That(commands.AddNode(new NodeRecord(sourceId, new NodeTypeId("test.source.image"), 1, "Source", true, new ProjectPosition(0, 0), ports: new[] { new PortSnapshotRecord(image, PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(commands.AddNode(new NodeRecord(feedbackId, new NodeTypeId(GraphConstants.FeedbackTypeId), 1, "Feedback", true, new ProjectPosition(1, 0), ports: new[] { new PortSnapshotRecord(new PortId("input"), PortDirection.Input, PortType.ImageFrame, false), new PortSnapshotRecord(image, PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(commands.AddNode(new NodeRecord(programId, new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", true, new ProjectPosition(2, 0), ports: new[] { new PortSnapshotRecord(image, PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false)).IsSuccess, Is.True);
			Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), sourceId, image, feedbackId, new PortId("input"))).IsSuccess, Is.True);
			Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), feedbackId, image, programId, image)).IsSuccess, Is.True);

			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.source.image"), 1, "Source", "Test", new[] { new PortDefinition(image, "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.FeedbackTypeId), 1, "Feedback", "Test", new[] { new PortDefinition(new PortId("input"), "Input", PortDirection.Input, PortType.ImageFrame, false), new PortDefinition(image, "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", "System", new[] { new PortDefinition(image, "Image", PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false)).IsSuccess, Is.True);
			var session = new RuntimeSession(document, registry);
			var evaluationOrder = new List<NodeInstanceId>();
			Assert.That(session.RegisterFactory(new TestFactory(new NodeTypeId("test.source.image"), new TestNode(sourceId, new NodeTypeId("test.source.image"), false, evaluationOrder: evaluationOrder))).IsSuccess, Is.True);
			Assert.That(session.RegisterFactory(new TestFactory(new NodeTypeId(GraphConstants.FeedbackTypeId), new TestNode(feedbackId, new NodeTypeId(GraphConstants.FeedbackTypeId), false, evaluationOrder: evaluationOrder))).IsSuccess, Is.True);
			Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Program, programId, image, 1920, 1080) }).IsSuccess, Is.True);
			var coordinator = new FrameCoordinator(session, new GraphClock(new ManualSource(0d)));

			var first = coordinator.Tick(0d);
			var second = coordinator.Tick(1d / 60d);

			Assert.That(evaluationOrder, Does.Contain(feedbackId));
			Assert.That(first.Presentation.Program.Status, Is.EqualTo(NodeOutputStatus.Available));
			Assert.That(second.Presentation.Program.Status, Is.EqualTo(NodeOutputStatus.Available));
		}

		private static FrameFixture CreateFrameFixture(bool includeFault, bool connectHealthyPreview = false, bool sourcePreparing = false) {
			var document = new ProjectDocument("Runtime Test");
			var commands = new ProjectCommandProcessor(document);
			var parameterId = new ParameterId("value");
			var definition = new ParameterDefinition(parameterId, "Value", ParameterType.Float, ParameterValue.FromFloat(0.2f), new ParameterRange(ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)));
			var sourceId = NodeInstanceId.New();
			var programId = NodeInstanceId.New();
			var previewId = NodeInstanceId.New();
			var healthy = new NodeRecord(sourceId, new NodeTypeId("test.source.image"), 1, "Source", true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, ParameterValue.FromFloat(0.2f)) }, new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Output, PortType.ImageFrame, false) });
			var program = new NodeRecord(programId, new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", true, new ProjectPosition(1, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false);
			Assert.That(commands.AddNode(healthy).IsSuccess, Is.True);
			Assert.That(commands.AddNode(program).IsSuccess, Is.True);
			NodeInstanceId faultId = default(NodeInstanceId);
			if (includeFault || connectHealthyPreview) {
				var preview = new NodeRecord(previewId, new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", true, new ProjectPosition(3, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) });
				if (includeFault) {
					faultId = NodeInstanceId.New();
					var fault = new NodeRecord(faultId, new NodeTypeId("test.fault.image"), 1, "Fault", true, new ProjectPosition(2, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Output, PortType.ImageFrame, false) });
					Assert.That(commands.AddNode(fault).IsSuccess, Is.True);
				}
				Assert.That(commands.AddNode(preview).IsSuccess, Is.True);
				Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), includeFault ? faultId : sourceId, new PortId("image"), previewId, new PortId("image"))).IsSuccess, Is.True);
			}
			Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), sourceId, new PortId("image"), programId, new PortId("image"))).IsSuccess, Is.True);

			var registry = new NodeTypeRegistry();
			registry.Register(new NodeTypeDefinition(new NodeTypeId("test.source.image"), 1, "Source", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) }, new[] { definition }));
			if (includeFault) registry.Register(new NodeTypeDefinition(new NodeTypeId("test.fault.image"), 1, "Fault", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) }));
			registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", "System", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false));
			if (includeFault || connectHealthyPreview) registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", "System", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false));
			var session = new RuntimeSession(document, registry);
			var healthyNode = new TestNode(sourceId, new NodeTypeId("test.source.image"), false, sourcePreparing);
			var demandNode = healthyNode;
			session.RegisterFactory(new TestFactory(new NodeTypeId("test.source.image"), healthyNode));
			if (includeFault) {
				demandNode = new TestNode(faultId, new NodeTypeId("test.fault.image"), true);
				session.RegisterFactory(new TestFactory(new NodeTypeId("test.fault.image"), demandNode));
			}
			session.SetOutputDemands(includeFault || connectHealthyPreview ? new[] { new OutputDemand(OutputTargetKind.Program, programId, new PortId("image"), 1920, 1080), new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 640, 360) } : new[] { new OutputDemand(OutputTargetKind.Program, programId, new PortId("image"), 1920, 1080) });
			return new FrameFixture(session, new FrameCoordinator(session, new GraphClock(new ManualSource(0))), sourceId, programId, parameterId, previewId, healthyNode, demandNode);
		}

		private static ParameterFixture CreateParameterFixture(bool withExpression = false) {
			var document = new ProjectDocument("Parameter Test");
			var commands = new ProjectCommandProcessor(document);
			var nodeId = NodeInstanceId.New();
			var parameterId = new ParameterId("value");
			var definition = new ParameterDefinition(parameterId, "Value", ParameterType.Float, ParameterValue.FromFloat(0.2f), new ParameterRange(ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)));
			Assert.That(commands.AddNode(new NodeRecord(nodeId, new NodeTypeId("test.parameter.value"), 1, "Parameter", true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, ParameterValue.FromFloat(0.2f)) })).IsSuccess, Is.True);
			var controlId = LogicalControlId.New();
			var controlTarget = new LogicalControlTargetRecord(nodeId, parameterId, ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(1));
			Assert.That(commands.AddLogicalControl(new LogicalControlRecord(controlId, "Control", LogicalControlKind.Value, 0.2f, new[] { controlTarget })).IsSuccess, Is.True);
			if (withExpression) Assert.That(commands.AddExpression(new ParameterExpressionRecord(nodeId, parameterId, new LogicalControlLeaf(controlId))).IsSuccess, Is.True);
			return new ParameterFixture(document, commands, GraphState.FromProject(document), new ParameterStore(), nodeId, parameterId, controlId);
		}

		private sealed class ParameterFixture {
			public ProjectDocument Document { get; }
			public ProjectCommandProcessor DocumentCommands { get; }
			public GraphState Graph { get; }
			public ParameterStore Store { get; }
			public NodeInstanceId NodeId { get; }
			public ParameterId ParameterId { get; }
			public LogicalControlId ControlId { get; }
			public ParameterFixture(ProjectDocument document, ProjectCommandProcessor commands, GraphState graph, ParameterStore store, NodeInstanceId nodeId, ParameterId parameterId, LogicalControlId controlId) { Document = document; DocumentCommands = commands; Graph = graph; Store = store; NodeId = nodeId; ParameterId = parameterId; ControlId = controlId; Store.Synchronize(graph, document); }
		}

		private sealed class FrameFixture {
			public RuntimeSession Session { get; }
			public FrameCoordinator Coordinator { get; }
			public NodeInstanceId NodeId { get; }
			public NodeInstanceId ProgramId { get; }
			public ParameterId ParameterId { get; }
			public NodeInstanceId PreviewId { get; }
			private readonly TestNode _healthyNode;
			private readonly TestNode _demandNode;
			public int HealthyEvaluateCount => _healthyNode.EvaluateCount;
			public NodeExecutionContext LastHealthyContext => _healthyNode.LastContext;
			public int DemandEvaluateCount => _demandNode.EvaluateCount;
			public int DemandTransitions => _demandNode.DemandTransitions;
			public bool LastDemanded => _demandNode.LastDemanded;
			public FrameFixture(RuntimeSession session, FrameCoordinator coordinator, NodeInstanceId nodeId, NodeInstanceId programId, ParameterId parameterId, NodeInstanceId previewId, TestNode healthyNode, TestNode demandNode) { Session = session; Coordinator = coordinator; NodeId = nodeId; ProgramId = programId; ParameterId = parameterId; PreviewId = previewId; _healthyNode = healthyNode; _demandNode = demandNode; }
		}

		private sealed class RecordingPreparation : IRuntimeResourcePreparationWithPlan {
			public FrameSnapshot Snapshot { get; private set; }
			public FrameEvaluationContext Evaluation { get; private set; }
			public Result Prepare(FrameSnapshot snapshot) { Snapshot = snapshot; return Result.Success(); }
			public Result Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation) { Snapshot = snapshot; Evaluation = evaluation; return Result.Success(); }
		}

		private sealed class ManualSource : IMonotonicClock {
			public double Now { get; private set; }
			public ManualSource(double now) { Now = now; }
		}

		private sealed class TestImage : IRuntimeImageFrame {
			public int Width => 16; public int Height => 16; public string ColorFormat => "test"; public ulong FrameNumber => 1; public ulong LeaseId => 1;
		}

		private sealed class TestFactory : IRuntimeNodeFactory {
			private readonly TestNode _node;
			public NodeTypeId TypeId { get; }
			public TestFactory(NodeTypeId typeId, TestNode node) { TypeId = typeId; _node = node; }
			public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId) { _node.Generation = generationId; return Result<IRuntimeNode>.Success(_node); }
		}

		private sealed class TestNode : IRuntimeNode, IRuntimeDemandAwareNode {
			private readonly bool _fault;
			private readonly IList<NodeInstanceId> _evaluationOrder;
			public NodeInstanceId NodeId { get; }
			public NodeTypeId TypeId { get; }
			public ulong Generation { get; set; }
			public ulong GenerationId => Generation;
			public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Ready;
			public int EvaluateCount { get; private set; }
			public int DemandTransitions { get; private set; }
			public bool LastDemanded { get; private set; }
			public NodeExecutionContext LastContext { get; private set; }
			public TestNode(NodeInstanceId nodeId, NodeTypeId typeId, bool fault, bool preparing = false, IList<NodeInstanceId> evaluationOrder = null) { NodeId = nodeId; TypeId = typeId; _fault = fault; _evaluationOrder = evaluationOrder; State = preparing ? RuntimeNodeState.Preparing : RuntimeNodeState.Ready; }
			public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) {
				LastContext = context;
				EvaluateCount++;
				_evaluationOrder?.Add(NodeId);
				if (_fault) throw new InvalidOperationException("fault");
				foreach (var port in context.RequestedOutputs) outputs.SetAvailable(port, PortValue.FromImageFrame(new TestImage()));
			}
			public void OnDemandChanged(bool demanded, FrameEvaluationContext context) {
				DemandTransitions++;
				LastDemanded = demanded;
			}
			public void Dispose() { State = RuntimeNodeState.Disposed; }
		}

	}
}
