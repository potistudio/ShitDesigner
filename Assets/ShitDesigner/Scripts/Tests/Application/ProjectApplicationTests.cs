using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Runtime;

namespace ShitDesigner.Application.Tests {
	[TestFixture]
	public sealed class ProjectApplicationTests {
		private string _root;

		[SetUp]
		public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "ShitDesigner-App-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }

		[TearDown]
		public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

		[Test]
		public void NewProjectStagesFactoryAndSwitchesOnlyAfterReadback() {
			var target = Path.Combine(_root, "Demo");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				var result = application.NewProject("Demo", target);
				Assert.That(result.IsSuccess, Is.True, result.Diagnostic == null ? string.Empty : result.Diagnostic.Message);
				Assert.That(application.State, Is.EqualTo(ApplicationProjectState.Ready));
				Assert.That(application.ReadModel.Project.Model.ProjectName, Is.EqualTo("Demo"));
				Assert.That(application.ReadModel.Project.Model.NodeCount, Is.GreaterThanOrEqualTo(1));
				Assert.That(File.Exists(Path.Combine(target, PersistenceConstants.MainFileName)), Is.True);
				Assert.That(File.Exists(Path.Combine(target, "Assets")), Is.False);
				Assert.That(Directory.Exists(Path.Combine(target, "Assets")), Is.True);
				Assert.That(Directory.Exists(Path.Combine(target, "Backups")), Is.True);
			}
		}

		[Test]
		public void FailedOpenKeepsCurrentCandidateAndSession() {
			var target = Path.Combine(_root, "Demo");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Demo", target).IsSuccess, Is.True);
				var session = application.ProjectSessionId;
				var result = application.OpenProject(Path.Combine(_root, "does-not-exist"), UnsavedChangesDecision.Discard);
				Assert.That(result.IsSuccess, Is.False);
				Assert.That(application.ProjectSessionId, Is.EqualTo(session));
				Assert.That(application.ReadModel.Project.Model.ProjectName, Is.EqualTo("Demo"));
			}
		}

		[Test]
		public void SaveAsSwitchesRootOnlyAfterPortableDirectoryRename() {
			var source = Path.Combine(_root, "Source");
			var target = Path.Combine(_root, "Target");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Portable", source).IsSuccess, Is.True);
				var result = application.SaveAs(target);
				Assert.That(result.IsSuccess, Is.True, result.Diagnostic == null ? string.Empty : result.Diagnostic.Message);
				Assert.That(application.CurrentRoot, Is.EqualTo(target));
				Assert.That(File.Exists(Path.Combine(target, PersistenceConstants.MainFileName)), Is.True);
				Assert.That(Directory.Exists(source), Is.True);
			}
		}

		[Test]
		public void LearnKeyChangesOnlyPhysicalMappingAndPreservesLogicalControl() {
			var target = Path.Combine(_root, "Input");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Input", target).IsSuccess, Is.True);
				var id = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(id, "Intensity", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.BeginKeyboardLearn(id).IsSuccess, Is.True);
				var captured = application.HandleKeyboard(new PhysicalKey("space", "<Keyboard>/space"), true);
				Assert.That(captured.IsSuccess, Is.True);
				var control = application.ReadModel.Project.Model.LogicalControls.Single(x => x.Id == id.Value);
				Assert.That(control.Mappings.Single().PhysicalId, Is.EqualTo("space"));
				Assert.That(control.Mappings.Single().ControlPath, Is.EqualTo("<Keyboard>/space"));
				Assert.That(application.IsKeyboardLearnActive, Is.False);
			}
		}

		[Test]
		public void LearnMidiMapsAndNormalizesControlChangeInput() {
			var target = Path.Combine(_root, "MidiInput");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("MIDI Input", target).IsSuccess, Is.True);
				var id = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(id, "Intensity", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.BeginKeyboardLearn(id).IsSuccess, Is.True);
				var control = new MidiControl("LCXL3 1 MIDI", MidiControlKind.ControlChange, 1, 21);

				Assert.That(application.HandleMidi(new MidiInputEvent(control, 64)).IsSuccess, Is.True);
				var mapping = application.ReadModel.Project.Model.LogicalControls.Single(x => x.Id == id.Value).Mappings.Single();
				Assert.That(mapping.Kind, Is.EqualTo(ApplicationPhysicalInputKind.Midi));
				Assert.That(mapping.PhysicalId, Is.EqualTo("LCXL3 1 MIDI:controlchange:1:21"));
				Assert.That(mapping.RawMin, Is.Zero);
				Assert.That(mapping.RawMax, Is.EqualTo(127));
				Assert.That(application.IsKeyboardLearnActive, Is.False);

				Assert.That(application.HandleMidi(new MidiInputEvent(control, 127)).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.ControlValues[id.Value], Is.EqualTo(1f));
				Assert.That(application.HandleMidi(new MidiInputEvent(control, 0)).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.ControlValues[id.Value], Is.EqualTo(0f));
			}
		}

		[Test]
		public void InspectorLiveControlValueUsesRuntimeWithoutChangingMappings() {
			var target = Path.Combine(_root, "InspectorLiveControl");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Inspector Live Control", target).IsSuccess, Is.True);
				var id = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(id, "Intensity", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLiveControlValue(id, 0.25f).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.ControlValues[id.Value], Is.EqualTo(0.25f));
				Assert.That(application.ReadModel.Project.Model.LogicalControls.Single(x => x.Id == id.Value).Mappings, Is.Empty);
				Assert.That(application.SetLiveControlValue(id, 1.1f).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
			}
		}

		[Test]
		public void MediaDeleteWaitsForCommittedManifestBeforeRemovingDirectory() {
			var target = Path.Combine(_root, "Media");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Media", target).IsSuccess, Is.True);
				var id = MediaAssetId.New();
				var bytes = new byte[] { 1, 2, 3, 4 };
				var relative = "Assets/" + id.Value + "/source.bin";
				Directory.CreateDirectory(Path.Combine(target, "Assets", id.Value));
				File.WriteAllBytes(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
				var asset = new MediaAssetRecord(id, "Source", relative, bytes.Length, AssetIntegrity.Hash(bytes));
				Assert.That(application.AddMediaAsset(asset).IsSuccess, Is.True);
				Assert.That(application.SaveProject().IsSuccess, Is.True);
				Assert.That(application.DeleteMediaAsset(id).IsSuccess, Is.True);
				Assert.That(Directory.Exists(Path.Combine(target, "Assets", id.Value)), Is.True);
				Assert.That(application.ReadModel.PendingMediaDeletions.Count, Is.EqualTo(1));
				var pendingProjection = application.ReadModel.PendingMediaDeletions;
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.PendingMediaDeletions, Is.SameAs(pendingProjection), "A stable media-deletion session must reuse its frozen projection across publishes.");
				Assert.That(application.SaveProject().IsSuccess, Is.True);
				Assert.That(Directory.Exists(Path.Combine(target, "Assets", id.Value)), Is.False);
				Assert.That(application.ReadModel.PendingMediaDeletions.Count, Is.EqualTo(0));
				Assert.That(application.ReadModel.PendingMediaDeletions, Is.Not.SameAs(pendingProjection), "Finalize/clear must advance the deletion projection exactly once.");
			}
		}

		[Test]
		public void KeyboardMappingInvertIsAppliedAtPhysicalToLogicalBoundary() {
			var target = Path.Combine(_root, "Invert");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Invert", target).IsSuccess, Is.True);
				var id = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(id, "Value", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(id, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "x", "<Keyboard>/x", 0f, 1f, true) }).IsSuccess, Is.True);
				Assert.That(application.HandleKeyboard(new PhysicalKey("x", "<Keyboard>/x"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.ControlValues[id.Value], Is.EqualTo(0f));
			}
		}

		[Test]
		public void AcceptanceStyleColorControl_UsesPersistedExpressionForValueAndPresetTrigger() {
			var target = Path.Combine(_root, "AcceptanceControl");
			var registry = new NodeTypeRegistry();
			var color = new ParameterDefinition(new ParameterId("color"), "Color", ParameterType.Color, ParameterValue.FromColor(new ColorValue(0f, 0f, 0f, 1f)));
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.acceptance.shader.generator"), 1, "Shader Generator", "Test", Array.Empty<PortDefinition>(), new[] { color })).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Acceptance Control", target).IsSuccess, Is.True);
				var nodeId = NodeInstanceId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, nodeId, nodeTypeId: "test.acceptance.shader.generator")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);

				var valueId = LogicalControlId.New().Value;
				Assert.That(application.AddLogicalControl(new ApplicationLogicalControlRequest(valueId, "Acceptance Value", ApplicationLogicalControlKind.Value,
					mappings: new[] { new ApplicationControlMappingRequest("acceptance.value", "<Acceptance>/value") })).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlTargets(valueId, new[]
				{
					new ApplicationLogicalControlTargetRequest(nodeId, "color", ParameterValue.FromColor(new ColorValue(0f, 0f, 0f, 1f)), ParameterValue.FromColor(new ColorValue(1f, 1f, 1f, 1f)))
				}).IsSuccess, Is.True);
				Assert.That(application.ApplyExpression(new ApplicationExpressionDraft(nodeId, "color", ApplicationExpressionKind.Max,
					left: new ApplicationExpressionDraft(nodeId, "color", ApplicationExpressionKind.BaseValue),
					right: new ApplicationExpressionDraft(nodeId, "color", ApplicationExpressionKind.LogicalControl, valueId))).IsSuccess, Is.True);

				var presetValue = ParameterValue.FromColor(new ColorValue(0.7f, 0.1f, 0.2f, 1f));
				var presetId = PresetId.New().Value;
				Assert.That(application.AddPreset(new ApplicationPresetRequest(presetId, "Acceptance Preset", "Acceptance", 0,
					new[] { new ApplicationPresetEntryRequest(nodeId, "color", presetValue) })).IsSuccess, Is.True);
				var triggerId = LogicalControlId.New().Value;
				Assert.That(application.AddLogicalControl(new ApplicationLogicalControlRequest(triggerId, "Acceptance Preset Trigger", ApplicationLogicalControlKind.PresetTrigger,
					presetId: presetId, mappings: new[] { new ApplicationControlMappingRequest("acceptance.preset", "<Acceptance>/preset") })).IsSuccess, Is.True);

				Assert.That(application.HandleKeyboard(PhysicalKey.From("acceptance.value", "<Acceptance>/value"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				Assert.That(EffectiveColorValue(application, nodeId), Is.EqualTo(ParameterValue.FromColor(new ColorValue(1f, 1f, 1f, 1f)).ToString()));
				Assert.That(application.HandleKeyboard(PhysicalKey.From("acceptance.value", "<Acceptance>/value"), false).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(2d / 60d);

				Assert.That(application.HandleKeyboard(PhysicalKey.From("acceptance.preset", "<Acceptance>/preset"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(3d / 60d);
				Assert.That(BaseColorValue(application, nodeId), Is.EqualTo(presetValue.ToString()));
				Assert.That(EffectiveColorValue(application, nodeId), Is.EqualTo(presetValue.ToString()));
				Assert.That(application.ReadModel.ControlValues.ContainsKey(triggerId), Is.False, "PresetTrigger is an event and must not appear as a persistent Value.");
				Assert.That(application.ReadModel.ControlRuntime[triggerId].HasValue, Is.False);
				Assert.That(application.ReadModel.ControlRuntime[triggerId].IsFiring, Is.True, "The public Runtime control slice must expose the accepted PresetTrigger pulse.");

				Assert.That(application.HandleKeyboard(PhysicalKey.From("acceptance.preset", "<Acceptance>/preset"), false).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(4d / 60d);
				Assert.That(application.ReadModel.ControlRuntime[triggerId].IsFiring, Is.False, "The subsequent public frame clears the trigger pulse after release/rearm.");
				var shiftedValue = ParameterValue.FromColor(new ColorValue(0.2f, 0.4f, 0.6f, 1f));
				Assert.That(application.EditParameter(new ApplicationParameterEditRequest(nodeId, "color", shiftedValue)).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(5d / 60d);
				Assert.That(BaseColorValue(application, nodeId), Is.EqualTo(shiftedValue.ToString()));

				Assert.That(application.HandleKeyboard(PhysicalKey.From("acceptance.preset", "<Acceptance>/preset"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(6d / 60d);
				Assert.That(BaseColorValue(application, nodeId), Is.EqualTo(presetValue.ToString()), "Release must re-arm the same PresetTrigger for a later edge.");
				Assert.That(EffectiveColorValue(application, nodeId), Is.EqualTo(presetValue.ToString()));
				Assert.That(application.ReadModel.ControlValues.ContainsKey(triggerId), Is.False);
			}
		}

		[Test]
		public void PerformanceStyleInput_TargetsTheExistingVideoSpeedParameter() {
			var target = Path.Combine(_root, "PerformanceControl");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("shitdesigner.scene.3d"), 1, "3D", "3D", Array.Empty<PortDefinition>())).IsSuccess, Is.True);
			var speed = new ParameterDefinition(new ParameterId(VideoPlayerContract.SpeedParameterId), "Speed", ParameterType.Float,
				ParameterValue.FromFloat(1f), new ParameterRange(ParameterValue.FromFloat(0f), ParameterValue.FromFloat(4f)));
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(VideoPlayerContract.NodeTypeId), 1, "VideoPlayer", "Video", Array.Empty<PortDefinition>(), new[] { speed })).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Performance Control", target).IsSuccess, Is.True);
				var sceneId = NodeInstanceId.New().Value;
				var videoId = NodeInstanceId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, sceneId, nodeTypeId: "shitdesigner.scene.3d")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, videoId, nodeTypeId: VideoPlayerContract.NodeTypeId)).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);

				var controlId = LogicalControlId.New().Value;
				Assert.That(application.AddLogicalControl(new ApplicationLogicalControlRequest(controlId, "Performance Tick", ApplicationLogicalControlKind.Value,
					mappings: new[] { new ApplicationControlMappingRequest("harness.tick", "<Harness>/tick") })).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlTargets(controlId, new[]
				{
					new ApplicationLogicalControlTargetRequest(videoId, VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(0.5f), ParameterValue.FromFloat(1.5f))
				}).IsSuccess, Is.True);
				Assert.That(application.ApplyExpression(new ApplicationExpressionDraft(videoId, VideoPlayerContract.SpeedParameterId, ApplicationExpressionKind.Max,
					left: new ApplicationExpressionDraft(videoId, VideoPlayerContract.SpeedParameterId, ApplicationExpressionKind.BaseValue),
					right: new ApplicationExpressionDraft(videoId, VideoPlayerContract.SpeedParameterId, ApplicationExpressionKind.LogicalControl, controlId))).IsSuccess, Is.True);

				var presetId = PresetId.New().Value;
				Assert.That(application.AddPreset(new ApplicationPresetRequest(presetId, "Performance Speed", "Performance", 0,
					new[] { new ApplicationPresetEntryRequest(videoId, VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(1.75f)) })).IsSuccess, Is.True);
				var triggerId = LogicalControlId.New().Value;
				Assert.That(application.AddLogicalControl(new ApplicationLogicalControlRequest(triggerId, "Performance Preset", ApplicationLogicalControlKind.PresetTrigger,
					presetId: presetId, mappings: new[] { new ApplicationControlMappingRequest("harness.preset", "<Harness>/preset") })).IsSuccess, Is.True);

				Assert.That(application.HandleKeyboard(PhysicalKey.From("harness.tick", "<Harness>/tick"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				var parameter = application.ReadModel.Parameters.Model.Single(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.SpeedParameterId);
				Assert.That(application.ReadModel.ControlValues[controlId], Is.EqualTo(1f));
				Assert.That(parameter.LogicalTargets, Does.Contain(controlId));
				Assert.That(parameter.EffectiveValue, Is.EqualTo(ParameterValue.FromFloat(1.5f).ToString()));

				Assert.That(application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(0.75f))).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(2d / 60d);
				Assert.That(application.HandleKeyboard(PhysicalKey.From("harness.preset", "<Harness>/preset"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(3d / 60d);
				parameter = application.ReadModel.Parameters.Model.Single(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.SpeedParameterId);
				Assert.That(parameter.BaseValue, Is.EqualTo(ParameterValue.FromFloat(1.75f).ToString()));
				Assert.That(parameter.EffectiveValue, Is.EqualTo(ParameterValue.FromFloat(1.75f).ToString()),
					"The preset must remain observable while the Value control is concurrently at its mapped maximum.");
				var trigger = application.ReadModel.Project.Model.LogicalControls.Single(x => x.Id == triggerId);
				Assert.That(trigger.PresetId, Is.EqualTo(presetId));
				Assert.That(trigger.PresetIsBroken, Is.False);
				Assert.That(trigger.Mappings.Single().PhysicalId, Is.EqualTo("harness.preset"));
				Assert.That(application.ReadModel.ControlValues.ContainsKey(triggerId), Is.False);

				Assert.That(application.HandleKeyboard(PhysicalKey.From("harness.preset", "<Harness>/preset"), false).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(4d / 60d);
				Assert.That(application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(0.5f))).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(5d / 60d);
				Assert.That(application.HandleKeyboard(PhysicalKey.From("harness.preset", "<Harness>/preset"), true).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(6d / 60d);
				parameter = application.ReadModel.Parameters.Model.Single(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.SpeedParameterId);
				Assert.That(parameter.BaseValue, Is.EqualTo(ParameterValue.FromFloat(1.75f).ToString()), "Release must re-arm the Performance PresetTrigger.");
				Assert.That(application.ReadModel.Parameters.Model.Any(x => x.NodeId == sceneId && x.ParameterId == "color"), Is.False,
					"The required 3D Generator is parameterless; Performance must not target it as a Shader Generator.");
			}
		}

		[Test]
		public void AcceptedParameterRequestsResolveIndependentlyAtFrameBoundary() {
			var target = Path.Combine(_root, "Correlated");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Correlated", target).IsSuccess, Is.True);
				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Correlated", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "x", "<Keyboard>/x") }).IsSuccess, Is.True);
				var accepted = application.HandleKeyboard(new PhysicalKey("x", "<Keyboard>/x"), true);
				var rejected = application.EnqueueBaseValue(new BaseValueUpdate(new NodeInstanceId("missing.node"), new ParameterId("missing.parameter"), ParameterValue.FromFloat(0.5f)));
				Assert.That(accepted.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(rejected.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == accepted.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == rejected.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
			}
		}

		[Test]
		public void HighRateKeyboardInput_DefersReadModelPublicationUntilItsTerminalFrame() {
			var target = Path.Combine(_root, "DeferredKeyboardPublication");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Deferred Keyboard Publication", target).IsSuccess, Is.True);
				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Tick", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "tick", "<Harness>/tick") }).IsSuccess, Is.True);

				var versionBeforeInputs = application.ReadModel.Project.ReadModelVersion;
				var accepted = new System.Collections.Generic.List<ApplicationCommandResult>();
				for (var index = 0; index < 120; index++) {
					var command = application.HandleKeyboard(new PhysicalKey("tick", "<Harness>/tick"), (index & 1) == 0);
					Assert.That(command.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
					accepted.Add(command);
				}

				Assert.That(application.ReadModel.Project.ReadModelVersion, Is.EqualTo(versionBeforeInputs),
					"High-rate keyboard requests must not rebuild the full public ReadModel before their shared terminal frame.");

				application.Tick(0d);

				Assert.That(application.ReadModel.Project.ReadModelVersion, Is.EqualTo(versionBeforeInputs + 1),
					"The frame boundary must publish one correlated snapshot for the queued keyboard input batch.");
				var terminal = accepted.Select(command => application.ReadModel.Commands.Single(item => item.CommandRequestId == command.CommandRequestId)).ToArray();
				Assert.That(terminal.All(item => item.IsTerminal), Is.True,
					"Every accepted physical input must publish a terminal outcome at its correlated frame boundary.");
				Assert.That(terminal.Count(item => item.Status == ApplicationCommandStatus.Superseded), Is.EqualTo(119),
					"Only the newest same-control value survives a shared frame; earlier values must be explicitly Superseded rather than disappear.");
				Assert.That(terminal.Count(item => item.Status == ApplicationCommandStatus.Applied), Is.EqualTo(1));
				Assert.That(terminal.Last().Status, Is.EqualTo(ApplicationCommandStatus.Applied),
					"The newest physical input must remain the applied terminal result.");
				Assert.That(application.ReadModel.ControlValues[control.Value], Is.EqualTo(0f));
			}
		}

		[Test]
		public void SustainedLogicalInput_UsesBoundedTerminalCommandHistoryWithoutLosingTheLatestCompletion() {
			var target = Path.Combine(_root, "BoundedCommandHistory");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Bounded Command History", target).IsSuccess, Is.True);
				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Tick", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "tick", "<Harness>/tick") }).IsSuccess, Is.True);

				ApplicationCommandResult first = default(ApplicationCommandResult);
				ApplicationCommandResult latest = default(ApplicationCommandResult);
				for (var index = 0; index < ProjectApplication.TerminalCommandHistoryLimit + 32; index++) {
					var command = application.HandleKeyboard(new PhysicalKey("tick", "<Harness>/tick"), (index & 1) == 0);
					if (index == 0) first = command;
					latest = command;
					Assert.That(command.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
					application.Tick(index / 60d);
				}

				// Publish a following frame: the prior frame exposed its
				// terminal transition, and this one proves retention has
				// already been bounded for subsequent consumers.
				application.Tick((ProjectApplication.TerminalCommandHistoryLimit + 33) / 60d);
				var commands = application.ReadModel.Commands;
				Assert.That(commands.Count(x => x.IsTerminal), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
				Assert.That(commands.Any(x => x.CommandRequestId == first.CommandRequestId), Is.False,
					"Old terminal feedback must be pruned after it has been exposed; it must not make a 120 Hz run grow forever.");
				Assert.That(commands.Single(x => x.CommandRequestId == latest.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied),
					"The latest queued input must still publish its terminal result through the public snapshot.");
			}
		}

		[Test]
		public void Sustained120HzLogicalInput_72000AcceptedRequestsRemainTerminalAndBounded() {
			var target = Path.Combine(_root, "Sustained120Hz");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Sustained 120Hz", target).IsSuccess, Is.True);
				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Tick", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "tick", "<Harness>/tick") }).IsSuccess, Is.True);

				const int commandCount = 72000;
				var terminalCount = 0;
				var elapsed = Stopwatch.StartNew();
				for (var index = 0; index < commandCount; index++) {
					var accepted = application.HandleKeyboard(new PhysicalKey("tick", "<Harness>/tick"), (index & 1) == 0);
					Assert.That(accepted.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
					var frame = application.Tick(index / 120d);
					if (frame.CommandResults.Any(result => result.RequestId == accepted.CommandRequestId.ToString("D") && result.Status == ApplicationCommandStatus.Applied)) terminalCount++;
				}
				elapsed.Stop();
				application.Tick(commandCount / 120d);

				Assert.That(terminalCount, Is.EqualTo(commandCount), "Every accepted 120 Hz input must reach a terminal public frame result.");
				Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(45)),
					"The command-correlation path must stay bounded for the 600 second Performance run; ledger-wide scans regress this contract to quadratic work.");
				Assert.That(application.ReadModel.Commands.Count(item => item.IsTerminal), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
				Assert.That(PrivateDictionaryCount(application, "_ledger"), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
				Assert.That(PrivateDictionaryCount(application, "_parameterRequests"), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
				Assert.That(PrivateDictionaryCount(application, "_commandIndices"), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
			}
		}

		[Test]
		public void CommandCorrelationIndices_CleanUpParameterGraphRuntimeAndCancelledRequests() {
			var target = Path.Combine(_root, "CorrelationCleanup");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.correlation.node"), 1, "Correlation", "Test", Array.Empty<PortDefinition>())).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Correlation Cleanup", target).IsSuccess, Is.True);
				var interaction = Guid.NewGuid();
				var superseded = application.EnqueueBaseValue(new BaseValueUpdate(new NodeInstanceId("missing.node"), new ParameterId("missing.parameter"), ParameterValue.FromFloat(0f)), interaction);
				var latest = application.EnqueueBaseValue(new BaseValueUpdate(new NodeInstanceId("missing.node"), new ParameterId("missing.parameter"), ParameterValue.FromFloat(1f)), interaction);
				Assert.That(superseded.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(latest.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.Commands.Single(item => item.CommandRequestId == superseded.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Superseded));
				Assert.That(application.ReadModel.Commands.Single(item => item.CommandRequestId == latest.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));

				var graph = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, NodeInstanceId.New().Value, nodeTypeId: "test.correlation.node"));
				Assert.That(graph.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.Commands.Single(item => item.CommandRequestId == graph.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));

				var runtime = application.ResetFeedback(NodeInstanceId.New().Value);
				Assert.That(runtime.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(2d / 60d);
				Assert.That(application.ReadModel.Commands.Single(item => item.CommandRequestId == runtime.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));

				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Cancel", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "cancel", "<Harness>/cancel") }).IsSuccess, Is.True);
				var cancelled = application.HandleKeyboard(new PhysicalKey("cancel", "<Harness>/cancel"), true);
				Assert.That(cancelled.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.OpenProject(target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Commands.Single(item => item.CommandRequestId == cancelled.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Cancelled));

				// Terminal entries remain visible once.  Push them past the
				// public retention limit, then verify every correlation map
				// released the corresponding terminal request.
				for (var index = 0; index <= ProjectApplication.TerminalCommandHistoryLimit; index++)
					Assert.That(application.SetWorkspaceLayout("cleanup-" + index, false).IsSuccess, Is.True);
				application.Tick(3d / 60d);
				Assert.That(PrivateDictionaryCount(application, "_parameterRequests"), Is.EqualTo(0));
				Assert.That(PrivateDictionaryCount(application, "_graphRequests"), Is.EqualTo(0));
				Assert.That(PrivateDictionaryCount(application, "_runtimeRequests"), Is.EqualTo(0));
				Assert.That(PrivateDictionaryCount(application, "_latestParameterRequestByInteraction"), Is.EqualTo(0));
				Assert.That(PrivateDictionaryCount(application, "_ledger"), Is.LessThanOrEqualTo(ProjectApplication.TerminalCommandHistoryLimit));
			}
		}

		[Test]
		public void PresetRequestRemainsAcceptedUntilNextFrame() {
			var target = Path.Combine(_root, "PresetQueue");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("PresetQueue", target).IsSuccess, Is.True);
				var request = application.ApplyPreset(PresetId.New());
				Assert.That(request.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == request.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == request.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
			}
		}

		[Test]
		public void SessionSwitchCancelsOldAcceptedRequestsWithoutDroppingTheirResults() {
			var target = Path.Combine(_root, "Switch");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Switch", target).IsSuccess, Is.True);
				var control = LogicalControlId.New();
				Assert.That(application.AddLogicalControl(new LogicalControlRecord(control, "Value", LogicalControlKind.Value)).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlMappings(control, new[] { new ControlMappingRecord(PhysicalInputKind.Keyboard, "x", "<Keyboard>/x") }).IsSuccess, Is.True);
				var accepted = application.HandleKeyboard(new PhysicalKey("x", "<Keyboard>/x"), true);
				Assert.That(accepted.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.OpenProject(target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == accepted.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Cancelled));
			}
		}

		[Test]
		public void ReadModelReadsDoNotAdvanceVersionAndGapsRequestFullSnapshot() {
			var target = Path.Combine(_root, "Versions");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Versions", target).IsSuccess, Is.True);
				var first = application.ReadModel;
				var second = application.ReadModel;
				Assert.That(second.Shell.ReadModelVersion, Is.EqualTo(first.Shell.ReadModelVersion));
				var initial = application.ReadSnapshot(0);
				Assert.That(initial.Shell.IsFullSnapshot, Is.True);
				Assert.That(initial.Shell.ReadModelVersion, Is.EqualTo(first.Shell.ReadModelVersion));
				var same = application.ReadSnapshot(first.Shell.ReadModelVersion);
				Assert.That(same.Shell.ReadModelVersion, Is.EqualTo(first.Shell.ReadModelVersion));
				var gap = application.ReadSnapshot(first.Shell.ReadModelVersion - 100);
				Assert.That(gap.Shell.IsFullSnapshot, Is.True);
				Assert.That(gap.Shell.ReadModelVersion, Is.EqualTo(first.Shell.ReadModelVersion));
			}
		}

		[Test]
		public void TypedGraphFacadeQueuesAddAndDeleteWithApplicationIssuedCorrelation() {
			var target = Path.Combine(_root, "GraphFacade");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.graph.node"), 1, "Test Node", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("GraphFacade", target).IsSuccess, Is.True);
				var nodeId = NodeInstanceId.New().Value;
				var request = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, nodeId, nodeTypeId: "test.graph.node", nodeDisplayName: "Added"));
				Assert.That(request.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == request.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));
				Assert.That(application.ReadModel.Graph.Model.Nodes.Any(x => x.Id == nodeId), Is.True);
				var delete = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.DeleteNode, nodeId));
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == delete.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));
				Assert.That(application.ReadModel.Graph.Model.Nodes.Any(x => x.Id == nodeId), Is.False);
			}
		}

		[Test]
		public void TypedGraphFacadeConnectsAndRejectsInvalidReplaceWithoutDroppingExistingEdge() {
			var target = Path.Combine(_root, "GraphConnect");
			var registry = new NodeTypeRegistry();
			registry.Register(new NodeTypeDefinition(new NodeTypeId("test.graph.source"), 1, "Source", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) }));
			registry.Register(new NodeTypeDefinition(new NodeTypeId("test.graph.dest"), 1, "Destination", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Input, PortType.ImageFrame, true) }));
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("GraphConnect", target).IsSuccess, Is.True);
				var source = NodeInstanceId.New().Value; var dest = NodeInstanceId.New().Value; var edge = ConnectionId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, source, nodeTypeId: "test.graph.source")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, dest, nodeTypeId: "test.graph.dest")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				var connect = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, edge, sourceId: source, sourcePortId: "image", destinationId: dest, destinationPortId: "image"));
				Assert.That(connect.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(2d / 60d);
				Assert.That(application.ReadModel.Graph.Model.Connections.Any(x => x.Id == edge), Is.True);
				var invalid = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.ReplaceInputConnection, ConnectionId.New().Value, sourceId: source, sourcePortId: "missing", destinationId: dest, destinationPortId: "image"));
				application.Tick(3d / 60d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == invalid.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
				Assert.That(application.ReadModel.Graph.Model.Connections.Any(x => x.Id == edge), Is.True);
			}
		}

		[Test]
		public void PreviewTabsRejectNonPreviewNodes() {
			var target = Path.Combine(_root, "Preview");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Preview", target).IsSuccess, Is.True);
				var invalid = application.OpenPreview("not-a-preview");
				Assert.That(invalid.Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
				var settings = application.SetPreviewSettings(new ApplicationPreviewSettingsRequest("not-a-preview", ApplicationOutputFitMode.Fill, "Checker", "Project", true));
				Assert.That(settings.Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
			}
		}

		[Test]
		public void PreviewTabsEnforceEightAndPersistPerNodeDisplayMode() {
			var target = Path.Combine(_root, "PreviewProject");
			var previews = Enumerable.Range(0, 9).Select(index => new NodeRecord(NodeInstanceId.New(), new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview " + index, true, new ProjectPosition(index, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) }, rawState: "{\"futureField\":123}", systemOwned: true, userAddable: false)).ToList();
			var created = ProjectDocumentFactory.TryCreate("PreviewProject", 1, nodes: previews, connections: Enumerable.Empty<ConnectionRecord>(), logicalControls: Enumerable.Empty<LogicalControlRecord>(), expressions: Enumerable.Empty<ParameterExpressionRecord>(), presets: Enumerable.Empty<PresetRecord>(), mediaAssets: Enumerable.Empty<MediaAssetRecord>(), ui: new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }), markDirty: false);
			Assert.That(created.IsSuccess, Is.True, created.Error == null ? string.Empty : created.Error.Message);
			Assert.That(new ProjectSaver().Save(created.Value, target, new LocalProjectFileSystem()).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.OpenProject(target).IsSuccess, Is.True);
				var previewIds = application.ReadModel.Graph.Model.Nodes.Where(x => x.TypeId == GraphConstants.PreviewTypeId).Select(x => x.Id).Take(9).ToList();
				Assert.That(previewIds.Count, Is.EqualTo(9));
				foreach (var id in previewIds.Take(8)) Assert.That(application.OpenPreview(id).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.First(x => x.Id == previewIds[0]).IsDemanded, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.First(x => x.Id == previewIds[0]).Width, Is.EqualTo(640));
				Assert.That(application.ReadModel.Output.Model.Previews.First(x => x.Id == previewIds[0]).Height, Is.EqualTo(360));
				var ninth = application.OpenPreview(previewIds[8]);
				Assert.That(ninth.Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
			Assert.That(ninth.Diagnostic.Message, Does.Contain("Preview"));
				foreach (var id in previewIds.Take(3)) Assert.That(application.RequestPreviewDemand(new ApplicationOutputDemandRequest(id, width: 320, height: 180)).IsSuccess, Is.True);
				application.Tick(0d);
				Assert.That(application.ReadModel.Output.Model.Program.IsDemanded, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.Count, Is.EqualTo(8));
				Assert.That(application.ReadModel.Output.Model.Previews.Where(x => previewIds.Take(3).Contains(x.Id)).All(x => x.IsDemanded), Is.True);
				Assert.That(application.RequestPreviewDemand(new ApplicationOutputDemandRequest(previewIds[0], width: 800, height: 450, focused: true)).IsSuccess, Is.True);
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == previewIds[0]).IsFocused, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == previewIds[1]).IsFocused, Is.False);
				Assert.That(application.RequestPreviewDemand(new ApplicationOutputDemandRequest(previewIds[1], width: 320, height: 180, focused: true)).IsSuccess, Is.True);
				application.Tick(1.5d / 60d);
				Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == previewIds[0]).IsFocused, Is.False);
				Assert.That(application.ReadModel.Output.Model.Previews.Single(x => x.Id == previewIds[1]).IsFocused, Is.True);
				var dirtyBeforeHostHide = application.ReadModel.Project.Model.IsDirty;
				Assert.That(application.SetPreviewHostVisible(false).IsSuccess, Is.True);
				application.Tick(2d / 60d);
				Assert.That(application.ReadModel.Output.Model.Program.IsDemanded, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.Any(x => x.IsDemanded), Is.False);
				Assert.That(application.ReadModel.Project.Model.IsDirty, Is.EqualTo(dirtyBeforeHostHide));
				Assert.That(application.SetPreviewHostVisible(true).IsSuccess, Is.True);
				application.Tick(3d / 60d);
				Assert.That(application.ReadModel.Output.Model.Previews.Where(x => previewIds.Take(3).Contains(x.Id)).All(x => x.IsDemanded), Is.True);
				Assert.That(application.ClosePreview(previewIds[0]).IsSuccess, Is.True);
				application.Tick(4d / 60d);
				Assert.That(application.ReadModel.Output.Model.Previews.Any(x => x.Id == previewIds[0]), Is.False);
				Assert.That(application.ReadModel.Output.Model.Previews.Where(x => previewIds.Skip(1).Take(2).Contains(x.Id)).All(x => x.IsDemanded), Is.True);
				Assert.That(application.SetPreviewSettings(new ApplicationPreviewSettingsRequest(previewIds[0], ApplicationOutputFitMode.Fill, "Checker", "Project", true)).IsSuccess, Is.True);
				Assert.That(application.SetPreviewSettings(new ApplicationPreviewSettingsRequest(previewIds[0], (ApplicationOutputFitMode)99, "Checker", "Project", true)).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
				Assert.That(application.SaveProject().IsSuccess, Is.True);
				var loaded = new ProjectLoader().Load(target, new LocalProjectFileSystem());
				Assert.That(loaded.IsSuccess, Is.True);
				Assert.That(loaded.Value.Document.FindNode(new NodeInstanceId(previewIds[0])).RawState, Does.Contain("Fill"));
				Assert.That(loaded.Value.Document.FindNode(new NodeInstanceId(previewIds[0])).RawState, Does.Contain("Checker"));
				Assert.That(loaded.Value.Document.FindNode(new NodeInstanceId(previewIds[0])).RawState, Does.Contain("futureField"));
			}
		}

		[Test]
		public void PreviewTabOrder_SurvivesSaveFreshOpenAndChangesCanonicalProjectFingerprint() {
			var target = Path.Combine(_root, "PreviewTabOrder");
			var first = NodeInstanceId.New();
			var second = NodeInstanceId.New();
			var nodes = new[]
			{
				new NodeRecord(first, new NodeTypeId(GraphConstants.PreviewTypeId), 1, "First", true, new ProjectPosition(0, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) }),
				new NodeRecord(second, new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Second", true, new ProjectPosition(1, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) })
			};
			var created = ProjectDocumentFactory.TryCreate("PreviewTabOrder", 1, nodes: nodes, connections: Enumerable.Empty<ConnectionRecord>(), logicalControls: Enumerable.Empty<LogicalControlRecord>(), expressions: Enumerable.Empty<ParameterExpressionRecord>(), presets: Enumerable.Empty<PresetRecord>(), mediaAssets: Enumerable.Empty<MediaAssetRecord>(), ui: new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }), markDirty: false);
			Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
			Assert.That(new ProjectSaver().Save(created.Value, target, new LocalProjectFileSystem()).IsSuccess, Is.True);

			string firstThenSecondFingerprint;
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.OpenProject(target).IsSuccess, Is.True);
				Assert.That(application.OpenPreview(first.Value).IsSuccess, Is.True);
				Assert.That(application.OpenPreview(second.Value).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.Select(x => x.Id), Is.EqualTo(new[] { first.Value, second.Value }));
				var captured = application.CaptureCanonicalProjectFingerprint();
				Assert.That(captured.IsSuccess, Is.True, captured.Error?.Message);
				firstThenSecondFingerprint = captured.Value;
				Assert.That(application.SaveProject().IsSuccess, Is.True);
			}

			using (var reopened = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(reopened.OpenProject(target).IsSuccess, Is.True);
				Assert.That(reopened.ReadModel.Output.Model.Previews.Select(x => x.Id), Is.EqualTo(new[] { first.Value, second.Value }));
				var reopenedFingerprint = reopened.CaptureCanonicalProjectFingerprint();
				Assert.That(reopenedFingerprint.IsSuccess, Is.True, reopenedFingerprint.Error?.Message);
				Assert.That(reopenedFingerprint.Value, Is.EqualTo(firstThenSecondFingerprint));

				Assert.That(reopened.ClosePreview(first.Value).IsSuccess, Is.True);
				Assert.That(reopened.ClosePreview(second.Value).IsSuccess, Is.True);
				Assert.That(reopened.OpenPreview(second.Value).IsSuccess, Is.True);
				Assert.That(reopened.OpenPreview(first.Value).IsSuccess, Is.True);
				Assert.That(reopened.ReadModel.Output.Model.Previews.Select(x => x.Id), Is.EqualTo(new[] { second.Value, first.Value }));
				var reversedFingerprint = reopened.CaptureCanonicalProjectFingerprint();
				Assert.That(reversedFingerprint.IsSuccess, Is.True, reversedFingerprint.Error?.Message);
				Assert.That(reversedFingerprint.Value, Is.Not.EqualTo(firstThenSecondFingerprint));
			}
		}

		[Test]
		public void CatalogPreviewNodes_PreserveInstanceTitlesAndDisplayModesAcrossFreshOpenCanonicalFingerprint() {
			var target = Path.Combine(_root, "CatalogPreviewPersistence");
			var first = NodeInstanceId.New().Value;
			var second = NodeInstanceId.New().Value;
			var mode = new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
				enumOptionIds: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", "System",
				new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Input, PortType.ImageFrame, true) }, new[] { mode })).IsSuccess, Is.True);
			string expectedFingerprint;
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Catalog previews", target).IsSuccess, Is.True);
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, first, nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Acceptance Preview 1")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, second, nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Acceptance Preview 2")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(1d / 60d);
				Assert.That(application.EditParameter(new ApplicationParameterEditRequest(first, "display.mode", ParameterValue.FromEnum("fill"))).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(2d / 60d);
				var captured = application.CaptureCanonicalProjectFingerprint();
				Assert.That(captured.IsSuccess, Is.True, captured.Error?.Message);
				expectedFingerprint = captured.Value;
				Assert.That(application.SaveProject().IsSuccess, Is.True);
			}

			using (var reopened = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(reopened.OpenProject(target).IsSuccess, Is.True);
				Assert.That(reopened.ReadModel.Graph.Model.Nodes.Single(x => x.Id == first).DisplayName, Is.EqualTo("Acceptance Preview 1"));
				Assert.That(reopened.ReadModel.Graph.Model.Nodes.Single(x => x.Id == second).DisplayName, Is.EqualTo("Acceptance Preview 2"));
				Assert.That(reopened.ReadModel.Parameters.Model.Single(x => x.NodeId == first && x.ParameterId == "display.mode").BaseValue, Is.EqualTo("fill"));
				var actual = reopened.CaptureCanonicalProjectFingerprint();
				Assert.That(actual.IsSuccess, Is.True, actual.Error?.Message);
				Assert.That(actual.Value, Is.EqualTo(expectedFingerprint));
			}
		}

		[Test]
		public void PreviewDemandLatestWinsAcrossRapidOpenCloseBeforeTick() {
			var target = Path.Combine(_root, "PreviewLatestWins");
			var previews = Enumerable.Range(0, 3).Select(index => new NodeRecord(NodeInstanceId.New(), new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview " + index, true, new ProjectPosition(index, 0), ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) })).ToList();
			var created = ProjectDocumentFactory.TryCreate("PreviewLatestWins", 1, nodes: previews, connections: Enumerable.Empty<ConnectionRecord>(), logicalControls: Enumerable.Empty<LogicalControlRecord>(), expressions: Enumerable.Empty<ParameterExpressionRecord>(), presets: Enumerable.Empty<PresetRecord>(), mediaAssets: Enumerable.Empty<MediaAssetRecord>(), ui: new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }), markDirty: false);
			Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
			Assert.That(new ProjectSaver().Save(created.Value, target, new LocalProjectFileSystem()).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.OpenProject(target).IsSuccess, Is.True);
				var ids = application.ReadModel.Graph.Model.Nodes.Where(x => x.TypeId == GraphConstants.PreviewTypeId).Select(x => x.Id).Take(3).ToList();
				for (var index = 0; index < 80; index++) {
					var transient = ids[index % 2];
					Assert.That(application.OpenPreview(transient).IsSuccess, Is.True);
					Assert.That(application.ClosePreview(transient).IsSuccess, Is.True);
				}
				Assert.That(application.OpenPreview(ids[2]).IsSuccess, Is.True);
				application.Tick(0d);
				Assert.That(application.ReadModel.Output.Model.Previews.Select(x => x.Id), Is.EqualTo(new[] { ids[2] }));
				Assert.That(application.ReadModel.Output.Model.Previews.Single().IsDemanded, Is.True);
				Assert.That(application.ReadModel.Output.Model.Previews.Single().IsFocused, Is.True);
			}
		}

		[Test]
		public void MediaImportBatchReportsStagesAndRegistersOnlyAfterStreamingCopies() {
			var target = Path.Combine(_root, "MediaBatch");
			var sourceA = Path.Combine(_root, "a.bin"); var sourceB = Path.Combine(_root, "b.bin");
			File.WriteAllBytes(sourceA, new byte[] { 1, 2, 3 }); File.WriteAllBytes(sourceB, new byte[] { 4, 5, 6, 7 });
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("MediaBatch", target).IsSuccess, Is.True);
				var result = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(sourceA, "A"), new ApplicationMediaImportRequest(sourceB, "B") });
				Assert.That(result.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.ReadModel.Media.Model.Count, Is.EqualTo(0));
				var stages = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
				for (var frame = 0; frame < 32; frame++) {
					stages.Add(application.ReadModel.Task.Model.Stage);
					application.Tick(frame / 60d);
					stages.Add(application.ReadModel.Task.Model.Stage);
					if (application.ReadModel.Commands.Single(x => x.CommandRequestId == result.CommandRequestId).IsTerminal) break;
				}
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == result.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));
				Assert.That(application.ReadModel.Media.Model.Count, Is.EqualTo(2));
				Assert.That(application.ReadModel.Task.Model.TotalItems, Is.EqualTo(2));
				Assert.That(application.ReadModel.Task.Model.CompletedItems, Is.EqualTo(2));
				Assert.That(application.ReadModel.Task.Model.Status, Is.EqualTo("Completed"));
				Assert.That(stages, Does.Contain("Copy"));
				Assert.That(stages, Does.Contain("SizeHash"));
				Assert.That(stages, Does.Contain("Probe"));
				Assert.That(stages, Does.Contain("Rename"));
				Assert.That(stages, Does.Contain("Register"));
			}
		}

		[Test]
		public void FramePublish_ReusesStableProjectCatalogAndMediaProjectionsWhileOutputAdvances() {
			var target = Path.Combine(_root, "CachedReadModelProjections");
			var source = Path.Combine(_root, "clip.bin");
			File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Cached projections", target).IsSuccess, Is.True);
				var import = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(source, "Clip") });
				Assert.That(import.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				for (var frame = 0; frame < 32 && !application.ReadModel.Commands.Single(x => x.CommandRequestId == import.CommandRequestId).IsTerminal; frame++)
					application.Tick(frame / 60d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == import.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));

				var project = application.ReadModel.Project.Model;
				var catalog = application.ReadModel.NodeCatalog.Model;
				var media = application.ReadModel.Media.Model;
				var dashboards = application.ReadModel.Dashboard.Model;
				var presets = application.ReadModel.Presets.Model;
				var graph = application.ReadModel.Graph.Model;
				var parameters = application.ReadModel.Parameters.Model;
				var diagnostics = application.ReadModel.DiagnosticModel.Model;
				var outputFrame = application.ReadModel.Output.Model.FrameNumber;

				application.Tick(1d);

				Assert.That(application.ReadModel.Project.Model, Is.SameAs(project), "Project metadata and LogicalControl definitions are immutable until dirty/revision state changes.");
				Assert.That(application.ReadModel.NodeCatalog.Model, Is.SameAs(catalog), "Catalog metadata must not be remapped for every runtime frame.");
				Assert.That(application.ReadModel.Media.Model, Is.SameAs(media), "Imported-media integrity projection must not rehash unchanged files on every runtime frame.");
				Assert.That(application.ReadModel.Dashboard.Model, Is.SameAs(dashboards), "Dashboard layout metadata is document-revision scoped, not frame scoped.");
				Assert.That(application.ReadModel.Presets.Model, Is.SameAs(presets), "Preset metadata is document-revision scoped, not frame scoped.");
				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(graph), "Stable graph topology/status must retain the same projection instance across a frame publish.");
				Assert.That(application.ReadModel.Parameters.Model, Is.SameAs(parameters), "Stable ordered parameter rows must not be wrapped in a new collection per frame.");
				Assert.That(application.ReadModel.DiagnosticModel.Model, Is.SameAs(diagnostics), "An unchanged DiagnosticHub revision must retain the diagnostics projection instance.");
				Assert.That(application.ReadModel.ChangeSets.GraphNodes.Changes, Is.Empty, "Stable publishes must not replay an old graph delta under a newer outer envelope version.");
				Assert.That(application.ReadModel.ChangeSets.Parameters.Changes, Is.Empty, "Stable publishes must not replay an old parameter delta.");
				Assert.That(application.ReadModel.ChangeSets.Diagnostics.Changes, Is.Empty, "Stable publishes must not replay an old diagnostics delta.");
				Assert.That(application.ReadModel.Output.Model.FrameNumber, Is.GreaterThan(outputFrame), "Output remains a frame-local projection and must continue to advance.");
			}
		}

		[Test]
		public void FramePublish_ReusesShellAndWorkspaceUntilTheirSemanticSourcesChange() {
			var target = Path.Combine(_root, "StableShellWorkspace");
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("Shell workspace", target).IsSuccess, Is.True);
				application.Tick(0d);
				var shell = application.ReadModel.Shell.Model;
				var workspace = application.ReadModel.Workspace.Model;
				var dashboards = application.ReadModel.Dashboard.Model;
				var version = application.ReadModel.Shell.ReadModelVersion;

				for (var frame = 1; frame <= 100; frame++) application.Tick(frame / 60d);
				Assert.That(application.ReadModel.Shell.Model, Is.SameAs(shell), "A fresh outer frame must not recreate unchanged Shell state.");
				Assert.That(application.ReadModel.Workspace.Model, Is.SameAs(workspace), "A fresh outer frame must not recreate unchanged Workspace state.");
				Assert.That(application.ReadModel.Dashboard.Model, Is.SameAs(dashboards), "Workspace-visible dashboard IDs must reuse their frozen dashboard projection.");
				Assert.That(application.ReadModel.Shell.ReadModelVersion, Is.GreaterThan(version), "Envelope metadata must still advance for each publication.");

				Assert.That(application.SetWorkspaceLayout("performance-layout", true).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Workspace.Model, Is.Not.SameAs(workspace));
				Assert.That(application.ReadModel.Shell.Model, Is.SameAs(shell), "Layout state belongs to Workspace, not Shell.");
				workspace = application.ReadModel.Workspace.Model;

				Assert.That(application.AddDashboardPage(new ApplicationDashboardPageRequest("performance", "Performance")).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Dashboard.Model, Is.Not.SameAs(dashboards));
				Assert.That(application.ReadModel.Workspace.Model, Is.Not.SameAs(workspace), "Dashboard panel identity changes must invalidate Workspace exactly once.");
				Assert.That(application.ReadModel.Workspace.Model.VisiblePanelIds, Does.Contain("performance"));
				var dirtyShell = application.ReadModel.Shell.Model;
				Assert.That(dirtyShell, Is.Not.SameAs(shell));
				Assert.That(dirtyShell.IsDirty, Is.True);

				Assert.That(application.Undo().IsSuccess, Is.True);
				Assert.That(application.ReadModel.Shell.Model, Is.Not.SameAs(dirtyShell), "Undo availability/status is Shell-owned semantic state.");
			}
		}

		[Test]
		public void StableGraphRowsAndRecentProjectionReuseUntilTopologyOrRecentStateChanges() {
			var firstRoot = Path.Combine(_root, "StableGraphRows");
			var secondRoot = Path.Combine(_root, "StableGraphRowsSecond");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.cache.graph"), 1, "Cache Graph", "Test",
				new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Stable graph rows", firstRoot).IsSuccess, Is.True);
				var nodeId = NodeInstanceId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, nodeId,
					nodeTypeId: "test.cache.graph")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				// The graph command's initial project Add delta belongs to its
				// terminal publication. Advance once before baselining the
				// shared empty delta used by subsequent stable envelopes.
				application.Tick(1d / 60d);

				var graph = application.ReadModel.Graph.Model;
				var row = graph.Nodes.Single(node => node.Id == nodeId);
				var recent = application.ReadModel.RecentProjectRoots;
				var emptyProjectChanges = application.ReadModel.ChangeSet.Changes;
				var stableVersion = application.ReadModel.Shell.ReadModelVersion;
				Assert.That(emptyProjectChanges, Is.Empty);
				for (var frame = 2; frame <= 101; frame++) application.Tick(frame / 60d);

				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(graph), "Stable runtime status must reuse the graph model across sustained frame publishes.");
				Assert.That(application.ReadModel.Graph.Model.Nodes.Single(node => node.Id == nodeId), Is.SameAs(row), "Stable graph rows must not be recreated while neither topology nor status changed.");
				Assert.That(application.ReadModel.RecentProjectRoots, Is.SameAs(recent), "Stable recent-project roots must remain a frozen shared projection.");
				Assert.That(application.ReadModel.ChangeSet.Changes, Is.SameAs(emptyProjectChanges), "Stable project deltas must reuse the immutable empty change list while their envelope version advances.");
				Assert.That(application.ReadModel.Shell.ReadModelVersion, Is.GreaterThan(stableVersion));

				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.SetEnabled, nodeId, enabled: false)).Status,
					Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(102d / 60d);
				var disabledGraph = application.ReadModel.Graph.Model;
				Assert.That(disabledGraph, Is.Not.SameAs(graph));
				Assert.That(disabledGraph.Nodes.Single(node => node.Id == nodeId).Status, Is.EqualTo("Disabled"));
				application.Tick(103d / 60d);
				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(disabledGraph), "The topology/status rebuild must occur once, then remain stable again.");

				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.DeleteNode, nodeId)).Status,
					Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(104d / 60d);
				var deletedGraph = application.ReadModel.Graph.Model;
				Assert.That(deletedGraph, Is.Not.SameAs(disabledGraph));
				Assert.That(deletedGraph.Nodes.Any(node => node.Id == nodeId), Is.False, "A structural graph revision must remove the cached row.");
				application.Tick(105d / 60d);
				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(deletedGraph), "Node deletion invalidates the projection once rather than every later frame.");

				Assert.That(application.NewProject("Changed recent roots", secondRoot, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				Assert.That(application.ReadModel.RecentProjectRoots, Is.Not.SameAs(recent));
				Assert.That(application.ReadModel.RecentProjectRoots.First(), Is.EqualTo(secondRoot));
			}
		}

		[Test]
		public void CatalogProjection_ReusesStableRevisionAndRefreshesAfterRegistryRegistration() {
			var target = Path.Combine(_root, "CatalogProjectionRevision");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.catalog.first"), 1, "First", "Test", Array.Empty<PortDefinition>())).IsSuccess, Is.True);
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry)) {
				Assert.That(application.NewProject("Catalog revision", target).IsSuccess, Is.True);
				var revision = registry.Revision;
				var first = application.ReadModel.NodeCatalog.Model;
				application.Tick(0d);
				Assert.That(registry.Revision, Is.EqualTo(revision));
				Assert.That(application.ReadModel.NodeCatalog.Model, Is.SameAs(first), "An unchanged registry revision must not materialize Definitions again for a frame publish.");

				Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId("test.catalog.second"), 1, "Second", "Test", Array.Empty<PortDefinition>())).IsSuccess, Is.True);
				application.Tick(1d / 60d);
				var refreshed = application.ReadModel.NodeCatalog.Model;
				Assert.That(refreshed, Is.Not.SameAs(first));
				Assert.That(refreshed.Any(item => item.TypeId == "test.catalog.second"), Is.True);
			}
		}

		[Test]
		public void MediaProbeWarningWaitsForConfirmationBeforeRegistration() {
			var target = Path.Combine(_root, "MediaProbe");
			var source = Path.Combine(_root, "warning.bin");
			File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), mediaProbe: new WarningProbe())) {
				Assert.That(application.NewProject("MediaProbe", target).IsSuccess, Is.True);
				var batch = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(source, "Warning") });
				application.Tick(0d); application.Tick(1d / 60d); application.Tick(2d / 60d);
				Assert.That(application.ReadModel.Task.Model.Status, Is.EqualTo("Waiting"));
				Assert.That(application.ReadModel.Task.Model.Stage, Is.EqualTo("ProbeConfirmation"));
				Assert.That(application.ReadModel.Media.Model.Count, Is.EqualTo(0));
				Assert.That(application.ConfirmMediaImport(true).IsSuccess, Is.True);
				for (var frame = 3; frame < 12; frame++) application.Tick(frame / 60d);
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == batch.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Applied));
				Assert.That(application.ReadModel.Media.Model.Count, Is.EqualTo(1));
			}
		}

		[Test]
		public void MediaProbeRejectionAndExceptionRejectBatchAndCleanupWithoutRegistration() {
			foreach (var probe in new IMediaAssetProbe[] { new RejectingProbe(), new ThrowingProbe() }) {
				var target = Path.Combine(_root, "MediaFailure-" + probe.GetType().Name);
				var source = Path.Combine(_root, probe.GetType().Name + ".bin");
				File.WriteAllBytes(source, new byte[] { 8, 9, 10 });
				using (var application = new ProjectApplication(new LocalProjectFileSystem(), mediaProbe: probe)) {
					Assert.That(application.NewProject("MediaFailure", target).IsSuccess, Is.True);
					var batch = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(source, "Failure") });
					for (var frame = 0; frame < 8; frame++) application.Tick(frame / 60d);
					Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == batch.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Rejected));
					Assert.That(application.ReadModel.Media.Model.Count, Is.EqualTo(0));
					Assert.That(Directory.Exists(Path.Combine(target, "Assets")), Is.True);
					Assert.That(Directory.EnumerateDirectories(Path.Combine(target, "Assets")).Any(), Is.False);
				}
			}
		}

		[Test]
		public void MediaBatchSessionSwitchCancelsOriginalRequestAndCleansStaging() {
			var target = Path.Combine(_root, "MediaCancelled");
			var replacement = Path.Combine(_root, "MediaReplacement");
			var source = Path.Combine(_root, "cancel.bin");
			File.WriteAllBytes(source, new byte[] { 11, 12, 13 });
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("MediaCancelled", target).IsSuccess, Is.True);
				var batch = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(source, "Cancelled") });
				application.Tick(0d);
				Assert.That(Directory.EnumerateDirectories(Path.Combine(target, "Assets")).Any(), Is.True);
				Assert.That(application.NewProject("MediaReplacement", replacement, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				Assert.That(application.ReadModel.Commands.Any(x => x.CommandRequestId == batch.CommandRequestId && x.Status == ApplicationCommandStatus.Cancelled), Is.True);
				Assert.That(Directory.EnumerateDirectories(Path.Combine(target, "Assets")).Any(), Is.False);
			}
		}

		[Test]
		public void MediaBatchExplicitCancelTerminatesRequestAndCleansStaging() {
			var target = Path.Combine(_root, "MediaExplicitCancel");
			var source = Path.Combine(_root, "explicit-cancel.bin");
			File.WriteAllBytes(source, new byte[] { 14, 15, 16 });
			using (var application = new ProjectApplication(new LocalProjectFileSystem())) {
				Assert.That(application.NewProject("MediaExplicitCancel", target).IsSuccess, Is.True);
				var batch = application.ImportMediaBatch(new[] { new ApplicationMediaImportRequest(source, "Cancelled") });
				application.Tick(0d);
				Assert.That(application.CancelMediaImport().Status, Is.EqualTo(ApplicationCommandStatus.Cancelled));
				Assert.That(application.ReadModel.Commands.Single(x => x.CommandRequestId == batch.CommandRequestId).Status, Is.EqualTo(ApplicationCommandStatus.Cancelled));
				Assert.That(application.ReadModel.Task.Model.Status, Is.EqualTo("Cancelled"));
				Assert.That(Directory.EnumerateDirectories(Path.Combine(target, "Assets")).Any(), Is.False);
			}
		}

		[Test]
		public void RuntimeCompositionFactoryOwnsSessionSwitchAndTearDown() {
			var target = Path.Combine(_root, "RuntimeComposition");
			var registry = new NodeTypeRegistry();
			registry.Register(new NodeTypeDefinition(new NodeTypeId("test.runtime.node"), 1, "Runtime Node", "Test", new[] { new PortDefinition(new PortId("image"), "Image", PortDirection.Output, PortType.ImageFrame, false) }));
			var factory = new TrackingRuntimeFactory();
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry, runtimeFactory: factory)) {
				Assert.That(application.NewProject("RuntimeComposition", target).IsSuccess, Is.True);
				Assert.That(factory.Compositions.Count, Is.EqualTo(1));
				Assert.That(application.ReadModel.NodeCatalog.Model.Single(x => x.TypeId == "test.runtime.node").RuntimeAvailable, Is.True);
				Assert.That(application.OpenProject(target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				Assert.That(factory.Compositions.Count, Is.EqualTo(2));
				Assert.That(factory.Compositions[0].Session.IsDisposed, Is.True);
			}
			Assert.That(factory.Compositions[1].Session.IsDisposed, Is.True);
		}

		[Test]
		public void DiagnosticsChangeSet_IncludesCurrentConditionAddAndRemove() {
			var target = Path.Combine(_root, "CurrentDiagnosticChanges");
			var factory = new TrackingRuntimeFactory();
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), runtimeFactory: factory)) {
				Assert.That(application.NewProject("Current diagnostics", target).IsSuccess, Is.True);
				var key = new CurrentConditionKey("runtime", "node", "n", new DiagnosticCode("runtime.waiting"));
				factory.Compositions[0].Session.Diagnostics.SetCurrent(key, new Diagnostic(new DiagnosticCode("runtime.waiting"), Severity.Warning, "Waiting"));
				application.Tick(1d / 60d);
				Assert.That(application.ReadModel.ChangeSets.Diagnostics.Changes.Any(change => change.StableId.StartsWith("current:", StringComparison.Ordinal) && change.Kind == ReadModelChangeKind.Add), Is.True);
				factory.Compositions[0].Session.Diagnostics.ClearCurrent(key);
				application.Tick(2d / 60d);
				Assert.That(application.ReadModel.ChangeSets.Diagnostics.Changes.Any(change => change.StableId.StartsWith("current:", StringComparison.Ordinal) && change.Kind == ReadModelChangeKind.Remove), Is.True);
			}
		}

		[Test]
		public void GraphRuntimeFallbackConditionReplacesOnlyAffectedRowThenReusesIt() {
			var target = Path.Combine(_root, "GraphRuntimeFallbackStatus");
			var typeId = new NodeTypeId("test.status.runtime");
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(typeId, 1, "Status Runtime", "Test", Array.Empty<PortDefinition>())).IsSuccess, Is.True);
			var factory = new TrackingRuntimeFactory();
			using (var application = new ProjectApplication(new LocalProjectFileSystem(), registry, runtimeFactory: factory)) {
				Assert.That(application.NewProject("Graph status", target).IsSuccess, Is.True);
				var nodeId = NodeInstanceId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, nodeId, nodeTypeId: typeId.Value)).Status,
					Is.EqualTo(ApplicationCommandStatus.Accepted));
				application.Tick(0d);
				var session = factory.Compositions[0].Session;
				Assert.That(session.RegisterFactory(new ReadyRuntimeFactory(typeId)).IsSuccess, Is.True);
				application.Tick(1d / 60d);
				var readyGraph = application.ReadModel.Graph.Model;
				var readyRow = readyGraph.Nodes.Single(node => node.Id == nodeId);
				Assert.That(readyRow.Status, Is.EqualTo("Ready"));

				var key = new CurrentConditionKey("test.runtime", "Node", nodeId, new DiagnosticCode("runtime.input.fallback"));
				session.Diagnostics.SetCurrent(key, new Diagnostic(new DiagnosticCode("runtime.input.fallback"), Severity.Warning,
					"Fallback input", nodeId: new NodeInstanceId(nodeId)));
				application.Tick(2d / 60d);
				var fallbackGraph = application.ReadModel.Graph.Model;
				var fallbackRow = fallbackGraph.Nodes.Single(node => node.Id == nodeId);
				Assert.That(fallbackGraph, Is.Not.SameAs(readyGraph));
				Assert.That(fallbackRow, Is.Not.SameAs(readyRow));
				Assert.That(fallbackRow.Status, Is.EqualTo("UsingFallback"));

				application.Tick(3d / 60d);
				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(fallbackGraph));
				Assert.That(application.ReadModel.Graph.Model.Nodes.Single(node => node.Id == nodeId), Is.SameAs(fallbackRow));
			}
		}

		private sealed class TrackingRuntimeFactory : IApplicationRuntimeSessionFactory {
			public readonly System.Collections.Generic.List<ApplicationRuntimeComposition> Compositions = new System.Collections.Generic.List<ApplicationRuntimeComposition>();
			public CSharpFunctionalExtensions.Result<ApplicationRuntimeComposition, Diagnostic> Create(ProjectDocument document, NodeTypeRegistry registry) {
				var session = new RuntimeSession(document, registry, new DiagnosticHub("test.runtime"));
				var composition = new ApplicationRuntimeComposition(session, new FrameCoordinator(session), true);
				Compositions.Add(composition);
				return CSharpFunctionalExtensions.Result.Success<ApplicationRuntimeComposition, Diagnostic>(composition);
			}
		}

		private sealed class ReadyRuntimeFactory : IRuntimeNodeFactory {
			public NodeTypeId TypeId { get; }
			public ReadyRuntimeFactory(NodeTypeId typeId) { TypeId = typeId; }
			public CSharpFunctionalExtensions.Result<IRuntimeNode, Diagnostic> Create(RuntimeNodeCreateInfo node, ulong generationId)
				=> CSharpFunctionalExtensions.Result.Success<IRuntimeNode, Diagnostic>(new ReadyRuntimeNode(node.Id, node.TypeId, generationId));
		}

		private sealed class ReadyRuntimeNode : IRuntimeNode {
			public NodeInstanceId NodeId { get; }
			public NodeTypeId TypeId { get; }
			public ulong GenerationId { get; }
			public RuntimeNodeState State => RuntimeNodeState.Ready;
			public ReadyRuntimeNode(NodeInstanceId nodeId, NodeTypeId typeId, ulong generationId) { NodeId = nodeId; TypeId = typeId; GenerationId = generationId; }
			public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) { }
			public void Dispose() { }
		}

		private static string EffectiveColorValue(ProjectApplication application, string nodeId)
			=> application.ReadModel.Parameters.Model.Single(parameter => parameter.NodeId == nodeId && parameter.ParameterId == "color").EffectiveValue;

		private static string BaseColorValue(ProjectApplication application, string nodeId)
			=> application.ReadModel.Parameters.Model.Single(parameter => parameter.NodeId == nodeId && parameter.ParameterId == "color").BaseValue;

		private static int PrivateDictionaryCount(ProjectApplication application, string fieldName) {
			var field = typeof(ProjectApplication).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "The command-correlation implementation no longer exposes its required " + fieldName + " map for this bounded-retention contract.");
			var collection = field.GetValue(application) as ICollection;
			Assert.That(collection, Is.Not.Null, fieldName + " must remain a collection with observable bounded retention.");
			return collection.Count;
		}

		private sealed class WarningProbe : IMediaAssetProbe {
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Probe(Stream stagedStream, string extension) {
				return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("media.probe.unsupported"), Severity.Warning, "Format requires confirmation."));
			}
		}

		private sealed class RejectingProbe : IMediaAssetProbe {
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Probe(Stream stagedStream, string extension) => CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("media.probe.rejected"), Severity.Error, "Probe rejected content."));
		}

		private sealed class ThrowingProbe : IMediaAssetProbe {
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Probe(Stream stagedStream, string extension) => throw new InvalidDataException("Probe failed.");
		}
	}
}
