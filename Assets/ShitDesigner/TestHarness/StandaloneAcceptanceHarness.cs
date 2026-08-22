using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace ShitDesigner.TestHarness
{
    public sealed partial class StandalonePerformanceHarness
    {
        private bool _acceptanceArtifactWritten;
        private AcceptanceFixtureValidationResult _acceptanceFixtures;
        private readonly List<HarnessAcceptanceFixtureArtifact> _acceptanceFixtureArtifacts = new List<HarnessAcceptanceFixtureArtifact>();
        private readonly List<string> _acceptanceUiActions = new List<string>();
        private HarnessAcceptancePersistenceArtifact _acceptancePersistence;
        private PresentationRoot _acceptancePresentationRoot;
        private string _acceptanceSaveButtonPickTarget;
        private HarnessAcceptanceUiSaveArtifact _acceptanceUiSave;
        private string _acceptanceParameterNodeId;
        private string _acceptanceParameterId;
        private string _acceptanceNativeDiagnostic;
        private bool _acceptanceNativeProbePassed;
        private bool _acceptanceMainFilePreserved;
        private bool _acceptanceSaved;
        private bool _acceptanceReopened;
        private bool _acceptanceRecovered;
        private bool _acceptanceDirtyAfterRecovery;
        private bool _acceptanceOutputsObserved;
        private bool _acceptanceEditorAssemblyExcluded;
        private bool _acceptanceProjectReadable;
        private bool _acceptanceProjectWritable;
        private bool _acceptanceBackupReadable;
        private string _acceptancePreview1;
        private string _acceptancePreview2;
        private bool _acceptanceMediaProbeConfirmationRequested;
        private string _acceptanceThreeDNodeId;
        private string _acceptanceTwoDNodeId;
        private string _acceptanceEffectNodeId;
        private string _acceptanceBlendNodeId;
        private string _acceptanceVideoBlendNodeId;
        private string _acceptanceFeedbackNodeId;
        private string _acceptanceProgramNodeId;
        private string _acceptanceValueControlId;
        private string _acceptancePresetTriggerId;
        private string _acceptancePresetId;
        private string _acceptanceValueMappingPhysicalId;
        private bool _acceptanceRequiredGraphObserved;
        private bool _acceptanceRealFrameObserved;
        private bool _acceptanceValueControlUpdated;
        private bool _acceptanceValueControlRemapped;
        private bool _acceptancePresetTriggerFired;
        private bool _acceptanceLogicalControlStateObserved;
        private bool _acceptanceMediaPortable;
        private HarnessAcceptanceOutputArtifact _acceptanceLastOutput;

        private IEnumerator RunAcceptance()
        {
            _running = true;
            var core = RunAcceptanceCore();
            while (true)
            {
                bool moved;
                try { moved = core.MoveNext(); }
                catch (Exception exception) { Fail("Unhandled acceptance exception: " + exception); break; }
                if (!moved) break;
                yield return core.Current;
            }
            _running = false;
            if (!_acceptanceArtifactWritten) FinishAcceptance();
        }

        private IEnumerator RunAcceptanceCore()
        {
            if (_options == null || _options.Mode != HarnessMode.Acceptance)
            {
                FailEnvironment("Acceptance mode options were not parsed.");
                yield break;
            }

            _acceptanceEditorAssemblyExcluded = !AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly != null && assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase));
            if (!_acceptanceEditorAssemblyExcluded)
            {
                Fail("A UnityEditor assembly is loaded in the Standalone Player.");
                yield break;
            }

            if (_options.AcceptanceStage == HarnessAcceptanceStage.Initial)
            {
                var fixtureRoot = _options.FixtureRoot;
                _acceptanceFixtures = AcceptanceFixtureValidator.Validate(fixtureRoot);
                if (!_acceptanceFixtures.IsValid) { FailEnvironment(_acceptanceFixtures.Error); yield break; }
            }
            else if (string.IsNullOrWhiteSpace(_options.ProjectRoot))
            {
                FailEnvironment("Acceptance reopen/recovery requires an explicit project root.");
                yield break;
            }

            if (!TryAcquireComposition()) { Fail("Production Composition Root was not available."); yield break; }
            _acceptancePresentationRoot = FindAnyObjectByType<PresentationRoot>();
            if (_acceptancePresentationRoot == null || _acceptancePresentationRoot.RootVisualElement == null)
            {
                Fail("Production PresentationRoot visual tree was not available.");
                yield break;
            }

            switch (_options.AcceptanceStage)
            {
                case HarnessAcceptanceStage.Initial:
                    yield return RunAcceptanceInitial();
                    break;
                case HarnessAcceptanceStage.Reopen:
                    yield return RunAcceptanceReopen();
                    break;
                case HarnessAcceptanceStage.Recovery:
                    yield return RunAcceptanceRecovery();
                    break;
                default:
                    FailEnvironment("Unknown acceptance stage.");
                    break;
            }
        }

        private IEnumerator RunAcceptanceInitial()
        {
            var application = _composition.Application;
            _acceptancePersistence = new HarnessAcceptancePersistenceArtifact();
            _projectRoot = Path.Combine(UnityApplication.persistentDataPath, "ShitDesigner", "Acceptance", _runId);
            var created = application.NewProject("Standalone Acceptance " + _runId, _projectRoot, UnsavedChangesDecision.Discard);
            if (created.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance NewProject rejected: " + DiagnosticText(created.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel?.Project?.Model != null, 20d);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            string generatorId;
            string videoId;
            string preview1;
            string preview2;
            string temporaryId;
            if (!SubmitAcceptanceGraph(application, out generatorId, out videoId, out preview1, out preview2, out temporaryId)) yield break;
            yield return WaitForCondition(() => AcceptanceGraphHasNodes(application, generatorId, videoId, preview1, preview2, temporaryId), 30d);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var graphConnection = application.ReadModel.Graph.Model.Connections.FirstOrDefault(x => x.FromNodeId == _acceptanceThreeDNodeId && x.ToNodeId == _acceptanceBlendNodeId);
            if (graphConnection == null) { Fail("Acceptance graph connection for disconnect/reconnect was not exposed."); yield break; }
            var disconnected = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Disconnect, graphConnection.Id));
            if (disconnected.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance Disconnect was rejected: " + DiagnosticText(disconnected.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel.Graph.Model.Connections.All(x => x.Id != graphConnection.Id), 20d);
            if (!string.IsNullOrEmpty(_failure)) yield break;
            var reconnected = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceThreeDNodeId, "image", _acceptanceBlendNodeId, "a"));
            if (reconnected.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance reconnect was rejected: " + DiagnosticText(reconnected.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel.Graph.Model.Connections.Count >= 3, 20d);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var delete = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.DeleteNode, temporaryId));
            if (delete.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance DeleteNode was rejected: " + DiagnosticText(delete.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel.Graph.Model.Nodes.All(x => x.Id != temporaryId), 20d);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            if (!ConfigureAcceptanceControls(application, generatorId)) yield break;
            yield return ExerciseAcceptanceControls(application);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var graphValidation = AcceptanceContract.ValidateRequiredGraph(application.ReadModel.Graph?.Model);
            if (!string.IsNullOrEmpty(graphValidation)) { Fail(graphValidation); yield break; }
            _acceptanceRequiredGraphObserved = true;

            if (!ConfigureAcceptanceOutputs(application, preview1, preview2)) yield break;
            _acceptancePreview1 = preview1;
            _acceptancePreview2 = preview2;
            // The required topology routes the VideoPlayer to Program and
            // both Preview nodes.  Bind and prepare a real fixture before
            // waiting for those outputs; an unbound player cannot produce
            // the frame that the output contract requires.
            yield return ImportAndProbeAcceptanceFixtures(application, videoId, preview1, preview2);
            if (!string.IsNullOrEmpty(_failure)) yield break;

            yield return WaitForCondition(() => AcceptanceRealFrameReady(application), 60d, "Acceptance real Program/Preview frame after fixture binding");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!_acceptanceRealFrameObserved) { Fail("Acceptance did not observe a real presented Program frame."); yield break; }

            var portableError = AcceptanceContract.ValidatePortableMedia(application.ReadModel, _projectRoot, _options.FixtureRoot);
            if (!string.IsNullOrEmpty(portableError)) { Fail(portableError); yield break; }
            _acceptanceMediaPortable = true;

            yield return SaveAcceptanceProjectThroughUi(application, "initial", "project-save");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!TryCaptureCanonicalProjectFingerprint(application, "backup", out var backupFingerprint)) yield break;
            var backupFingerprintComponents = AcceptanceFingerprint.ComputeComponents(application.ReadModel);
            var backupPath = Path.Combine(_projectRoot, PersistenceConstants.BackupFileName);
            if (!File.Exists(backupPath)) { Fail("Acceptance second-save backup project.json.bak was not created."); yield break; }

            // Make one deterministic post-save edit, then save again. The
            // second atomic save makes the first, known public-model
            // fingerprint the durable backup that recovery will load.
            var backupEdit = application.EditParameter(new ApplicationParameterEditRequest(_acceptanceParameterNodeId, _acceptanceParameterId,
                ParameterValue.FromColor(new ColorValue(0.91f, 0.17f, 0.23f, 1f))));
            if (backupEdit.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance backup-seeding parameter edit was rejected."); yield break; }
            yield return null;
            var secondSave = application.SaveProject();
            if (secondSave.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance backup-seeding SaveProject was rejected: " + DiagnosticText(secondSave.Diagnostic)); yield break; }
            yield return WaitForCondition(() => AcceptanceSaveReady(application, application.ReadModel.Task?.Model?.TaskId ?? Guid.Empty), 30d, "Acceptance backup-seeding save completion");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            _acceptanceSaved = true;
            if (!TryCaptureCanonicalProjectFingerprint(application, "initial", out var persistedFingerprint)) yield break;
            var persistedFingerprintComponents = AcceptanceFingerprint.ComputeComponents(application.ReadModel);
            _acceptancePersistence = new HarnessAcceptancePersistenceArtifact
            {
                projectRoot = _projectRoot,
                saved = true,
                fingerprint = persistedFingerprint,
                expectedFingerprint = persistedFingerprint,
                backupFingerprint = backupFingerprint,
                expectedBackupFingerprint = backupFingerprint,
                fingerprintComponents = persistedFingerprintComponents?.Describe() ?? string.Empty,
                expectedFingerprintComponents = persistedFingerprintComponents?.Describe() ?? string.Empty,
                backupFingerprintComponents = backupFingerprintComponents?.Describe() ?? string.Empty,
                expectedBackupFingerprintComponents = backupFingerprintComponents?.Describe() ?? string.Empty,
                backupReadable = true
            };
            if (string.IsNullOrWhiteSpace(_acceptancePersistence.fingerprint)) { Fail("Acceptance canonical Project fingerprint was empty."); yield break; }
            File.WriteAllText(Path.Combine(_projectRoot, ".acceptance-marker.txt"),
                "runId=" + _runId + "\nfingerprint=" + _acceptancePersistence.fingerprint + "\nfingerprintComponents=" + _acceptancePersistence.fingerprintComponents + "\nbackupFingerprint=" + backupFingerprint + "\nbackupFingerprintComponents=" + _acceptancePersistence.backupFingerprintComponents + "\nbuildId=" + UnityApplication.buildGUID + "\n",
                new System.Text.UTF8Encoding(false));
            if (!ValidateProjectFiles(_projectRoot, true, out var fileError)) { Fail(fileError); yield break; }
            _acceptancePersistence.backupReadable = _acceptanceBackupReadable;
            ExitApplicationForAcceptance(application);
        }

        private IEnumerator RunAcceptanceReopen()
        {
            var application = _composition.Application;
            _projectRoot = Path.GetFullPath(_options.ProjectRoot);
            _acceptancePersistence = new HarnessAcceptancePersistenceArtifact
            {
                projectRoot = _projectRoot,
                expectedFingerprint = _options.ExpectedFingerprint,
                expectedFingerprintComponents = ReadAcceptanceMarkerValue(_projectRoot, "fingerprintComponents")
            };
            var opened = application.OpenProject(_projectRoot, UnsavedChangesDecision.Discard);
            if (opened.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance OpenProject was rejected: " + DiagnosticText(opened.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel?.Project?.Model != null && string.Equals(application.ReadModel.Task?.Model?.Status, "Completed", StringComparison.OrdinalIgnoreCase), 30d);
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (application.ReadModel.IsRecovered || application.ReadModel.Project.Model.IsRecovered) { Fail("Normal reopen unexpectedly entered recovery state."); yield break; }
            if (!TryCaptureCanonicalProjectFingerprint(application, "reopen", out var fingerprint)) yield break;
            var fingerprintComponents = AcceptanceFingerprint.ComputeComponents(application.ReadModel);
            _acceptancePersistence.fingerprint = fingerprint;
            _acceptancePersistence.fingerprintComponents = fingerprintComponents?.Describe() ?? string.Empty;
            _acceptancePersistence.reopened = true;
            _acceptanceReopened = true;
            if (!string.Equals(fingerprint, _options.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Fail("Acceptance reopen fingerprint differs from initial Canonical Project: " +
                    AcceptanceFingerprint.DescribeDifference(_acceptancePersistence.expectedFingerprintComponents, fingerprintComponents));
                yield break;
            }
            var graphValidation = AcceptanceContract.ValidateRequiredGraph(application.ReadModel.Graph?.Model);
            if (!string.IsNullOrEmpty(graphValidation)) { Fail(graphValidation); yield break; }
            _acceptanceRequiredGraphObserved = true;
            if (!PopulateAcceptanceControlState(application)) { yield break; }
            var logicalValidation = AcceptanceContract.ValidateLogicalControlContract(application.ReadModel, _acceptanceValueControlId,
                _acceptancePresetTriggerId, _acceptancePresetId, _acceptanceParameterNodeId, _acceptanceParameterId, "acceptance.value.remapped");
            if (!string.IsNullOrEmpty(logicalValidation)) { Fail(logicalValidation); yield break; }
            var portableError = AcceptanceContract.ValidatePortableMedia(application.ReadModel, _projectRoot, _options.FixtureRoot);
            if (!string.IsNullOrEmpty(portableError)) { Fail(portableError); yield break; }
            _acceptanceMediaPortable = true;
            if (!ValidateProjectFiles(_projectRoot, true, out var fileError)) { Fail(fileError); yield break; }
            yield return WaitForCondition(() => AcceptanceRealFrameReady(application), 60d, "Acceptance reopened real Program/Preview frame");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!_acceptanceRealFrameObserved) { Fail("Acceptance reopen did not observe a real presented Program frame."); yield break; }
            yield return SaveAcceptanceProjectThroughUi(application, "reopen", "project-save-reopen");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            ExitApplicationForAcceptance(application);
        }

        private IEnumerator RunAcceptanceRecovery()
        {
            var application = _composition.Application;
            _projectRoot = Path.GetFullPath(_options.ProjectRoot);
            var mainPath = Path.Combine(_projectRoot, PersistenceConstants.MainFileName);
            var backupPath = Path.Combine(_projectRoot, PersistenceConstants.BackupFileName);
            if (!File.Exists(mainPath) || !File.Exists(backupPath)) { FailEnvironment("Recovery project must contain exact project.json and project.json.bak targets."); yield break; }
            var damaged = File.ReadAllBytes(mainPath);
            var opened = application.OpenProject(_projectRoot, UnsavedChangesDecision.Discard);
            if (opened.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance recovery OpenProject was rejected: " + DiagnosticText(opened.Diagnostic)); yield break; }
            yield return WaitForCondition(() => application.ReadModel?.Project?.Model != null && string.Equals(application.ReadModel.Task?.Model?.Status, "Completed", StringComparison.OrdinalIgnoreCase), 30d);
            if (!string.IsNullOrEmpty(_failure)) yield break;
            var project = application.ReadModel.Project.Model;
            _acceptanceRecovered = application.ReadModel.IsRecovered || project.IsRecovered;
            _acceptanceDirtyAfterRecovery = project.IsDirty;
            _acceptanceMainFilePreserved = File.Exists(mainPath) && damaged.SequenceEqual(File.ReadAllBytes(mainPath));
            if (!TryCaptureCanonicalProjectFingerprint(application, "recovery", out var recoveredFingerprint)) yield break;
            var recoveredFingerprintComponents = AcceptanceFingerprint.ComputeComponents(application.ReadModel);
            _acceptancePersistence = new HarnessAcceptancePersistenceArtifact
            {
                projectRoot = _projectRoot,
                recovered = _acceptanceRecovered,
                dirtyAfterRecovery = _acceptanceDirtyAfterRecovery,
                mainFilePreservedAfterRecovery = _acceptanceMainFilePreserved,
                fingerprint = recoveredFingerprint,
                expectedFingerprint = _options.ExpectedBackupFingerprint,
                expectedBackupFingerprint = _options.ExpectedBackupFingerprint,
                backupFingerprint = recoveredFingerprint,
                fingerprintComponents = recoveredFingerprintComponents?.Describe() ?? string.Empty,
                backupFingerprintComponents = recoveredFingerprintComponents?.Describe() ?? string.Empty,
                expectedFingerprintComponents = ReadAcceptanceMarkerValue(_projectRoot, "backupFingerprintComponents"),
                expectedBackupFingerprintComponents = ReadAcceptanceMarkerValue(_projectRoot, "backupFingerprintComponents"),
                backupReadable = true
            };
            if (!_acceptanceRecovered || !_acceptanceDirtyAfterRecovery || !_acceptanceMainFilePreserved ||
                !string.Equals(_acceptancePersistence.fingerprint, _options.ExpectedBackupFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Fail("Acceptance recovery did not expose Recovered+Dirty, preserve the damaged main file, and match the known backup fingerprint. " +
                    AcceptanceFingerprint.DescribeDifference(_acceptancePersistence.expectedBackupFingerprintComponents, recoveredFingerprintComponents));
                yield break;
            }
            var graphValidation = AcceptanceContract.ValidateRequiredGraph(application.ReadModel.Graph?.Model);
            if (!string.IsNullOrEmpty(graphValidation)) { Fail(graphValidation); yield break; }
            _acceptanceRequiredGraphObserved = true;
            if (!PopulateAcceptanceControlState(application)) { yield break; }
            var logicalValidation = AcceptanceContract.ValidateLogicalControlContract(application.ReadModel, _acceptanceValueControlId,
                _acceptancePresetTriggerId, _acceptancePresetId, _acceptanceParameterNodeId, _acceptanceParameterId, "acceptance.value.remapped");
            if (!string.IsNullOrEmpty(logicalValidation)) { Fail(logicalValidation); yield break; }
            var portableError = AcceptanceContract.ValidatePortableMedia(application.ReadModel, _projectRoot, _options.FixtureRoot);
            if (!string.IsNullOrEmpty(portableError)) { Fail(portableError); yield break; }
            _acceptanceMediaPortable = true;
            if (!ValidateProjectFiles(_projectRoot, false, out var recoveryFileError)) { Fail(recoveryFileError); yield break; }
            _acceptancePersistence.backupReadable = _acceptanceBackupReadable;
            yield return WaitForCondition(() => AcceptanceRealFrameReady(application), 60d, "Acceptance recovered real Program/Preview frame");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!_acceptanceRealFrameObserved) { Fail("Acceptance recovery did not observe a real presented Program frame."); yield break; }
            ExitApplicationForAcceptance(application);
        }

        private bool SubmitAcceptanceGraph(ProjectApplication application, out string generatorId, out string videoId, out string preview1, out string preview2, out string temporaryId)
        {
            generatorId = Guid.NewGuid().ToString("D");
            videoId = Guid.NewGuid().ToString("D");
            preview1 = Guid.NewGuid().ToString("D");
            preview2 = Guid.NewGuid().ToString("D");
            temporaryId = Guid.NewGuid().ToString("D");
            _acceptanceThreeDNodeId = Guid.NewGuid().ToString("D");
            _acceptanceTwoDNodeId = Guid.NewGuid().ToString("D");
            _acceptanceEffectNodeId = Guid.NewGuid().ToString("D");
            _acceptanceBlendNodeId = Guid.NewGuid().ToString("D");
            _acceptanceVideoBlendNodeId = Guid.NewGuid().ToString("D");
            _acceptanceFeedbackNodeId = Guid.NewGuid().ToString("D");
            var nodes = new[]
            {
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, generatorId, nodeTypeId: "shitdesigner.shader.generator", nodeDisplayName: "Acceptance Color Generator"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceThreeDNodeId, nodeTypeId: "shitdesigner.scene.3d", nodeDisplayName: "Acceptance 3D"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceTwoDNodeId, nodeTypeId: "shitdesigner.scene.2d", nodeDisplayName: "Acceptance 2D"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceEffectNodeId, nodeTypeId: "shitdesigner.shader.effect", nodeDisplayName: "Acceptance Shader Effect"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, videoId, nodeTypeId: VideoPlayerContract.NodeTypeId, nodeDisplayName: "Acceptance Video"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceBlendNodeId, nodeTypeId: "shitdesigner.shader.blend2", nodeDisplayName: "Acceptance Blend 1"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceVideoBlendNodeId, nodeTypeId: "shitdesigner.shader.blend2", nodeDisplayName: "Acceptance Blend 2"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, _acceptanceFeedbackNodeId, nodeTypeId: "system.feedback", nodeDisplayName: "Acceptance Feedback"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, preview1, nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Acceptance Preview 1"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, preview2, nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Acceptance Preview 2"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, temporaryId, nodeTypeId: "shitdesigner.shader.generator", nodeDisplayName: "Acceptance Temporary")
            };
            foreach (var node in nodes)
            {
                var result = application.SubmitGraph(node);
                if (result.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance AddNode was rejected for " + node.NodeDisplayName + ": " + DiagnosticText(result.Diagnostic)); return false; }
            }
            var program = application.ReadModel.Graph?.Model?.Nodes.FirstOrDefault(x => x.TypeId == GraphConstants.ProgramOutputTypeId);
            if (program == null) { Fail("Acceptance graph did not expose ProgramOutput."); return false; }
            _acceptanceProgramNodeId = program.Id;
            var connections = new[]
            {
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceThreeDNodeId, "image", _acceptanceBlendNodeId, "a"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceTwoDNodeId, "image", _acceptanceBlendNodeId, "b"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceBlendNodeId, "image", _acceptanceVideoBlendNodeId, "a"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), videoId, "image", _acceptanceVideoBlendNodeId, "b"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceVideoBlendNodeId, "image", _acceptanceEffectNodeId, "input"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceEffectNodeId, "image", _acceptanceFeedbackNodeId, "input"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), _acceptanceFeedbackNodeId, "image", program.Id, "image"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), videoId, "image", preview1, "image"),
                new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), videoId, "image", preview2, "image")
            };
            foreach (var connection in connections)
            {
                var result = application.SubmitGraph(connection);
                if (result.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance Connect was rejected: " + DiagnosticText(result.Diagnostic)); return false; }
            }
            return true;
        }

        private bool AcceptanceGraphHasNodes(ProjectApplication application, string generatorId, string videoId, string preview1, string preview2, string temporaryId)
        {
            var graph = application.ReadModel.Graph?.Model;
            return graph != null && new[] { generatorId, videoId, preview1, preview2, temporaryId }.All(id => graph.Nodes.Any(x => x.Id == id)) && graph.Connections.Count >= 3;
        }

        private bool ConfigureAcceptanceControls(ProjectApplication application, string generatorId)
        {
            var parameter = AcceptanceContract.FindWritableShaderGeneratorColorParameter(application.ReadModel.Parameters?.Model, generatorId);
            if (parameter == null) { Fail("Acceptance generator color parameter was not exposed by the public catalog."); return false; }
            _acceptanceParameterNodeId = generatorId;
            _acceptanceParameterId = parameter.ParameterId;
            var controlId = LogicalControlId.New().Value;
            _acceptanceValueControlId = controlId;
            _acceptanceValueMappingPhysicalId = "acceptance.value";
            var control = application.AddLogicalControl(new ApplicationLogicalControlRequest(controlId, "Acceptance Value", ApplicationLogicalControlKind.Value,
                mappings: new[] { new ApplicationControlMappingRequest(_acceptanceValueMappingPhysicalId, "<Acceptance>/value") }));
            if (control.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance logical control was rejected: " + DiagnosticText(control.Diagnostic)); return false; }
            var target = application.SetLogicalControlTargets(controlId, new[] { new ApplicationLogicalControlTargetRequest(generatorId, _acceptanceParameterId,
                ParameterValue.FromColor(new ColorValue(0f, 0f, 0f, 1f)), ParameterValue.FromColor(new ColorValue(1f, 1f, 1f, 1f))) });
            if (target.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance logical target was rejected: " + DiagnosticText(target.Diagnostic)); return false; }
            // A target supplies the Value-to-Color conversion, but only the
            // persisted expression selects it as this parameter's effective
            // value.  Keep Base as a Max operand so the PresetTrigger remains
            // observable after the physical Value is released.
            var expression = application.ApplyExpression(new ApplicationExpressionDraft(generatorId, _acceptanceParameterId, ApplicationExpressionKind.Max,
                left: new ApplicationExpressionDraft(generatorId, _acceptanceParameterId, ApplicationExpressionKind.BaseValue),
                right: new ApplicationExpressionDraft(generatorId, _acceptanceParameterId, ApplicationExpressionKind.LogicalControl, controlId)));
            if (expression.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance logical control expression was rejected: " + DiagnosticText(expression.Diagnostic)); return false; }
            var presetId = PresetId.New().Value;
            _acceptancePresetId = presetId;
            var preset = application.AddPreset(new ApplicationPresetRequest(presetId, "Acceptance Preset", "Acceptance", 0,
                new[] { new ApplicationPresetEntryRequest(generatorId, _acceptanceParameterId, ParameterValue.FromColor(new ColorValue(0.7f, 0.1f, 0.2f, 1f))) }));
            if (preset.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance preset was rejected: " + DiagnosticText(preset.Diagnostic)); return false; }
            var triggerId = LogicalControlId.New().Value;
            _acceptancePresetTriggerId = triggerId;
            var trigger = application.AddLogicalControl(new ApplicationLogicalControlRequest(triggerId, "Acceptance Preset Trigger", ApplicationLogicalControlKind.PresetTrigger,
                presetId: presetId, mappings: new[] { new ApplicationControlMappingRequest("acceptance.preset", "<Acceptance>/preset") }));
            if (trigger.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance preset trigger was rejected: " + DiagnosticText(trigger.Diagnostic)); return false; }
            return true;
        }

        private IEnumerator ExerciseAcceptanceControls(ProjectApplication application)
        {
            yield return WaitForCondition(() => AcceptanceControlConfigured(application), 20d, "Acceptance logical-control configuration publication");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            var parameterBefore = AcceptanceParameter(application);
            if (parameterBefore == null) { Fail("Acceptance parameter disappeared before logical control input."); yield break; }
            var baseline = parameterBefore.EffectiveValue;
            var pressed = application.HandleKeyboard(PhysicalKey.From(_acceptanceValueMappingPhysicalId, "<Acceptance>/value"), true);
            if (pressed.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance Value physical input was rejected."); yield break; }
            yield return WaitForCondition(() => AcceptanceValueApplied(application, baseline, 1f, _acceptanceValueMappingPhysicalId), 20d, "Acceptance Value press propagation");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            _acceptanceValueControlUpdated = true;

            var released = application.HandleKeyboard(PhysicalKey.From(_acceptanceValueMappingPhysicalId, "<Acceptance>/value"), false);
            if (released.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance Value physical release was rejected."); yield break; }
            yield return WaitForCondition(() => AcceptanceControlValue(application, _acceptanceValueControlId) <= 0.01f, 20d, "Acceptance Value release propagation");
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var remappedPhysicalId = "acceptance.value.remapped";
            var remapped = application.SetLogicalControlMappings(_acceptanceValueControlId,
                new[] { new ApplicationControlMappingRequest(remappedPhysicalId, "<Acceptance>/value-remapped") });
            if (remapped.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance Value remapping was rejected: " + DiagnosticText(remapped.Diagnostic)); yield break; }
            yield return WaitForCondition(() => AcceptanceMappingApplied(application, _acceptanceValueControlId, remappedPhysicalId), 20d, "Acceptance Value remapping publication");
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var remappedPress = application.HandleKeyboard(PhysicalKey.From(remappedPhysicalId, "<Acceptance>/value-remapped"), true);
            if (remappedPress.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance remapped Value physical input was rejected."); yield break; }
            yield return WaitForCondition(() => AcceptanceValueApplied(application, baseline, 1f, remappedPhysicalId), 20d, "Acceptance remapped Value press propagation");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            _acceptanceValueControlRemapped = true;
            var remappedRelease = application.HandleKeyboard(PhysicalKey.From(remappedPhysicalId, "<Acceptance>/value-remapped"), false);
            if (remappedRelease.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance remapped Value physical release was rejected."); yield break; }
            yield return WaitForCondition(() => AcceptanceControlValue(application, _acceptanceValueControlId) <= 0.01f, 20d, "Acceptance remapped Value release propagation");
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var preset = (application.ReadModel.Presets?.Model ?? Array.Empty<ApplicationPresetReadModel>()).FirstOrDefault(x => x != null && x.Id == _acceptancePresetId);
            var presetEntry = preset?.Entries?.FirstOrDefault(x => x != null && x.NodeId == _acceptanceParameterNodeId && x.ParameterId == _acceptanceParameterId);
            if (presetEntry == null || preset.IsBroken) { Fail("Acceptance preset was not exposed by the public read model."); yield break; }
            var triggerPress = application.HandleKeyboard(PhysicalKey.From("acceptance.preset", "<Acceptance>/preset"), true);
            if (triggerPress.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance PresetTrigger physical input was rejected."); yield break; }
            yield return WaitForCondition(() => AcceptancePresetApplied(application, presetEntry.Value), 20d, "Acceptance PresetTrigger propagation");
            if (!string.IsNullOrEmpty(_failure)) yield break;
            _acceptancePresetTriggerFired = true;
            _acceptanceLogicalControlStateObserved = true;
            var triggerRelease = application.HandleKeyboard(PhysicalKey.From("acceptance.preset", "<Acceptance>/preset"), false);
            if (triggerRelease.Status == ApplicationCommandStatus.Rejected) { Fail("Acceptance PresetTrigger physical release was rejected."); yield break; }
        }

        private bool AcceptanceControlConfigured(ProjectApplication application)
        {
            var model = application?.ReadModel;
            var controls = model?.Project?.Model?.LogicalControls;
            var parameter = AcceptanceParameter(application);
            return controls != null && controls.Any(x => x != null && x.Id == _acceptanceValueControlId && x.Kind == ApplicationLogicalControlKind.Value &&
                x.Mappings.Any(mapping => mapping != null && mapping.PhysicalId == _acceptanceValueMappingPhysicalId)) &&
                controls.Any(x => x != null && x.Id == _acceptancePresetTriggerId && x.Kind == ApplicationLogicalControlKind.PresetTrigger && x.PresetId == _acceptancePresetId) &&
                parameter != null && parameter.LogicalTargets.Split(',').Contains(_acceptanceValueControlId);
        }

        private bool PopulateAcceptanceControlState(ProjectApplication application)
        {
            var model = application?.ReadModel;
            var graph = model?.Graph?.Model;
            var generator = graph?.Nodes?.FirstOrDefault(x => x != null && string.Equals(x.TypeId, "shitdesigner.shader.generator", StringComparison.Ordinal));
            var parameter = AcceptanceContract.FindWritableShaderGeneratorColorParameter(model?.Parameters?.Model, generator?.Id);
            var controls = model?.Project?.Model?.LogicalControls;
            if (controls == null) { Fail("Acceptance project logical controls were not exposed after reopen."); return false; }
            var value = controls.FirstOrDefault(x => x != null && x.Kind == ApplicationLogicalControlKind.Value && string.Equals(x.Name, "Acceptance Value", StringComparison.Ordinal));
            var trigger = controls.FirstOrDefault(x => x != null && x.Kind == ApplicationLogicalControlKind.PresetTrigger && string.Equals(x.Name, "Acceptance Preset Trigger", StringComparison.Ordinal));
            if (generator == null || parameter == null || value == null || trigger == null || string.IsNullOrWhiteSpace(trigger.PresetId))
            {
                Fail("Acceptance logical controls or their original parameter were not exposed after reopen.");
                return false;
            }
            _acceptanceParameterNodeId = generator.Id;
            _acceptanceParameterId = parameter.ParameterId;
            _acceptanceValueControlId = value.Id;
            _acceptancePresetTriggerId = trigger.Id;
            _acceptancePresetId = trigger.PresetId;
            _acceptanceLogicalControlStateObserved = true;
            return true;
        }

        private ApplicationParameterReadModel AcceptanceParameter(ProjectApplication application)
            => (application?.ReadModel?.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>()).FirstOrDefault(x => x != null && x.NodeId == _acceptanceParameterNodeId && x.ParameterId == _acceptanceParameterId);

        private static float AcceptanceControlValue(ProjectApplication application, string id)
        {
            if (application?.ReadModel?.ControlValues == null || string.IsNullOrWhiteSpace(id)) return float.NaN;
            return application.ReadModel.ControlValues.TryGetValue(id, out var value) ? value : float.NaN;
        }

        private bool AcceptanceMappingApplied(ProjectApplication application, string controlId, string physicalId)
        {
            var control = application?.ReadModel?.Project?.Model?.LogicalControls?.FirstOrDefault(x => x != null && x.Id == controlId);
            return control != null && control.Mappings != null && control.Mappings.Count == 1 && control.Mappings[0].PhysicalId == physicalId;
        }

        private bool AcceptanceValueApplied(ProjectApplication application, string baseline, float expectedControlValue, string physicalId)
        {
            var parameter = AcceptanceParameter(application);
            var control = application?.ReadModel?.Project?.Model?.LogicalControls?.FirstOrDefault(x => x != null && x.Id == _acceptanceValueControlId);
            return parameter != null && control != null && AcceptanceControlValue(application, _acceptanceValueControlId) >= expectedControlValue - 0.01f &&
                control.Mappings.Any(x => x != null && x.PhysicalId == physicalId) &&
                parameter.LogicalTargets.Split(',').Contains(_acceptanceValueControlId) &&
                !string.Equals(parameter.EffectiveValue, baseline, StringComparison.Ordinal);
        }

        private bool AcceptancePresetApplied(ProjectApplication application, string expectedValue)
        {
            var parameter = AcceptanceParameter(application);
            var trigger = application?.ReadModel?.Project?.Model?.LogicalControls?.FirstOrDefault(x => x != null && x.Id == _acceptancePresetTriggerId);
            return parameter != null && trigger != null && trigger.PresetId == _acceptancePresetId && !trigger.PresetIsBroken &&
                trigger.Mappings.Any(x => x != null && x.PhysicalId == "acceptance.preset") &&
                string.Equals(parameter.BaseValue, expectedValue, StringComparison.Ordinal) &&
                string.Equals(parameter.EffectiveValue, expectedValue, StringComparison.Ordinal);
        }

        private bool ConfigureAcceptanceOutputs(ProjectApplication application, string preview1, string preview2)
        {
            if (application.OpenPreview(preview1).Status == ApplicationCommandStatus.Rejected || application.OpenPreview(preview2).Status == ApplicationCommandStatus.Rejected)
            { Fail("Acceptance Preview open was rejected."); return false; }
            if (application.RequestPreviewDemand(new ApplicationOutputDemandRequest(preview1, "image", 640, 360)).Status == ApplicationCommandStatus.Rejected ||
                application.RequestPreviewDemand(new ApplicationOutputDemandRequest(preview2, "image", 640, 360)).Status == ApplicationCommandStatus.Rejected)
            { Fail("Acceptance Preview demand was rejected."); return false; }
            return true;
        }

        private bool AcceptanceOutputsReady(ProjectApplication application)
        {
            if (application == null) return _acceptanceOutputsObserved;
            var model = application.ReadModel;
            var videoNodeId = model?.Graph?.Model?.Nodes?.FirstOrDefault(node => node != null &&
                string.Equals(node.TypeId, VideoPlayerContract.NodeTypeId, StringComparison.Ordinal))?.Id;
            ObserveAcceptanceOutputEvidence(model, videoNodeId);
            return _acceptanceOutputsObserved;
        }

        private bool AcceptanceRealFrameReady(ProjectApplication application)
        {
            if (application == null) return _acceptanceOutputsObserved && _acceptanceRealFrameObserved;
            var model = application.ReadModel;
            var videoNodeId = model?.Graph?.Model?.Nodes?.FirstOrDefault(node => node != null &&
                string.Equals(node.TypeId, VideoPlayerContract.NodeTypeId, StringComparison.Ordinal))?.Id;
            ObserveAcceptanceOutputEvidence(model, videoNodeId);
            return _acceptanceOutputsObserved && _acceptanceRealFrameObserved;
        }

        private void ObserveAcceptanceOutputEvidence(ApplicationReadModel model, string videoNodeId, HarnessAcceptanceFixtureArtifact fixture = null)
        {
            // Capture both requirements from this one immutable public
            // read-model evaluation. The fixture's concrete frame counters
            // can advance while a subsequent ReadModel access already sees
            // the next media transition, which used to make this evidence
            // timing-dependent for short codecs.
            var output = model?.Output?.Model;
            _acceptanceLastOutput = CaptureAcceptanceOutput(output);
            var outputsReady = AcceptanceContract.OutputsReadyAfterVideoBinding(output, model?.Parameters?.Model, videoNodeId);
            var realFrame = outputsReady && AcceptanceContract.RealPresentedFrame(output);
            if (outputsReady) _acceptanceOutputsObserved = true;
            if (realFrame) _acceptanceRealFrameObserved = true;
            if (fixture != null)
            {
                if (outputsReady) fixture.outputReadyObserved = true;
                if (realFrame) fixture.realFrameObserved = true;
            }
        }

        private static HarnessAcceptanceOutputArtifact CaptureAcceptanceOutput(ApplicationOutputReadModel output)
        {
            if (output == null) return null;
            var program = output.Program;
            return new HarnessAcceptanceOutputArtifact
            {
                frameNumber = output.FrameNumber,
                programState = program?.State ?? output.ProgramState ?? string.Empty,
                programWidth = program?.Width ?? 0,
                programHeight = program?.Height ?? 0,
                programReason = program?.StatusReason ?? string.Empty,
                previews = (output.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>()).Select(preview => preview == null ? null : new HarnessAcceptanceOutputSurfaceArtifact
                {
                    id = preview.Id ?? string.Empty,
                    state = preview.State ?? string.Empty,
                    width = preview.Width,
                    height = preview.Height,
                    demanded = preview.IsDemanded,
                    reason = preview.StatusReason ?? string.Empty
                }).ToArray()
            };
        }

        private IEnumerator ImportAndProbeAcceptanceFixtures(ProjectApplication application, string videoId, string preview1, string preview2)
        {
            foreach (var entry in _acceptanceFixtures.Entries)
            {
                var requiresNative = entry.codec.StartsWith("Hap", StringComparison.OrdinalIgnoreCase);
                var fixture = new HarnessAcceptanceFixtureArtifact { codec = entry.codec, file = entry.file, nativeProbeRequired = requiresNative };
                if (requiresNative && !ProbeAcceptanceNative(fixture)) { _acceptanceFixtureArtifacts.Add(fixture); FailEnvironment(_acceptanceNativeDiagnostic); yield break; }
                var path = Path.Combine(_acceptanceFixtures.Root, entry.file);
                _acceptanceMediaProbeConfirmationRequested = false;
                var import = application.ImportMedia(new ApplicationMediaImportRequest(path, entry.file, "Video", "SRgb", entry.hasAlpha ? "Straight" : "Opaque"));
                if (import.Status == ApplicationCommandStatus.Rejected) { fixture.error = DiagnosticText(import.Diagnostic); _acceptanceFixtureArtifacts.Add(fixture); Fail("Acceptance media import was rejected for " + entry.codec + "."); yield break; }
                var beforeCount = application.ReadModel.Media?.Model?.Count ?? 0;
                yield return WaitForCondition(() => AcceptanceMediaTaskStep(application), 60d, "Acceptance " + entry.codec + " media import/probe completion");
                if (!string.IsNullOrEmpty(_failure)) { fixture.error = _failure; _acceptanceFixtureArtifacts.Add(fixture); yield break; }
                var media = (application.ReadModel.Media?.Model ?? Array.Empty<ApplicationMediaReadModel>()).Skip(beforeCount).FirstOrDefault() ?? application.ReadModel.Media?.Model?.LastOrDefault();
                if (media == null || media.IsBroken) { fixture.error = "Imported media is not ready."; _acceptanceFixtureArtifacts.Add(fixture); Fail("Acceptance media is broken for " + entry.codec + "."); yield break; }
                fixture.probePassed = true;
                var beforeOutput = application.ReadModel.Output?.Model;
                fixture.frameBefore = beforeOutput?.FrameNumber ?? 0UL;
                CaptureAcceptancePreviewFrames(preview1, preview2, out fixture.preview1FrameBefore, out fixture.preview2FrameBefore);
                fixture.previewFrameBefore = Math.Min(fixture.preview1FrameBefore, fixture.preview2FrameBefore);
                // Stop the previous codec first. This makes the next media
                // prepare a distinct public transport transition rather than
                // accepting a frame that was already being presented.
                var stopPlaying = application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.PlayingParameterId, ParameterValue.FromBool(false)));
                if (stopPlaying.Status == ApplicationCommandStatus.Rejected) { fixture.error = "Video stop binding was rejected."; _acceptanceFixtureArtifacts.Add(fixture); Fail("Acceptance video stop failed for " + entry.codec + "."); yield break; }
                yield return WaitForCondition(() => AcceptanceVideoPlayingState(application, videoId, false), 30d, "Acceptance " + entry.codec + " video stop publication");
                if (!string.IsNullOrEmpty(_failure)) { fixture.error = _failure; _acceptanceFixtureArtifacts.Add(fixture); yield break; }
                // Submit the new MediaAsset by itself. The production runtime
                // enters Preparing from this change; queuing playing/loop
                // first can let a short Unity backend finish the public
                // transition before the harness observes it.
                var mediaEdit = application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.MediaAssetParameterId, ParameterValue.FromMediaAsset(new MediaAssetId(media.Id))));
                if (mediaEdit.Status == ApplicationCommandStatus.Rejected)
                { fixture.error = "Video media binding was rejected."; _acceptanceFixtureArtifacts.Add(fixture); Fail("Acceptance video media binding failed for " + entry.codec + "."); yield break; }
                fixture.mediaAssetId = media.Id;
                // Wait until the public parameter read model reflects the
                // new asset before observing its asynchronous Prepare state.
                // An initial empty media parameter is not a valid stop-state
                // for this codec.
                yield return WaitForCondition(() => AcceptanceVideoMediaAssetState(application, videoId, media.Id), 30d, "Acceptance " + entry.codec + " media asset publication");
                if (!string.IsNullOrEmpty(_failure)) { fixture.error = _failure; _acceptanceFixtureArtifacts.Add(fixture); yield break; }
                yield return WaitForCondition(() => AcceptanceVideoPrepareStarted(application, videoId), 30d, "Acceptance " + entry.codec + " VideoPlayer Preparing transition");
                fixture.prepareObserved = string.IsNullOrEmpty(_failure);
                if (!fixture.prepareObserved)
                {
                    fixture.error = "Video node did not expose a Preparing transition after media binding.";
                    _acceptanceFixtureArtifacts.Add(fixture);
                    Fail("Acceptance video prepare transition was not observed for " + entry.codec + ".");
                    yield break;
                }
                if (!AcceptanceContract.CanStartVideoPlaybackAfterPrepare(true, fixture.prepareObserved))
                {
                    fixture.error = "Video playback was not permitted before the new media Prepare transition.";
                    _acceptanceFixtureArtifacts.Add(fixture);
                    Fail("Acceptance video playback attempted before Prepare observation for " + entry.codec + ".");
                    yield break;
                }
                var playingEdit = application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.PlayingParameterId, ParameterValue.FromBool(true)));
                var loopEdit = application.EditParameter(new ApplicationParameterEditRequest(videoId, VideoPlayerContract.LoopParameterId, ParameterValue.FromBool(true)));
                if (playingEdit.Status == ApplicationCommandStatus.Rejected || loopEdit.Status == ApplicationCommandStatus.Rejected)
                { fixture.error = "Video playback binding was rejected."; _acceptanceFixtureArtifacts.Add(fixture); Fail("Acceptance video playback binding failed for " + entry.codec + "."); yield break; }
                yield return WaitForCondition(() => AcceptanceVideoTransportState(application, videoId, media.Id, true, true), 30d, "Acceptance " + entry.codec + " playing/loop transport publication");
                if (!string.IsNullOrEmpty(_failure)) { fixture.error = _failure; _acceptanceFixtureArtifacts.Add(fixture); yield break; }
                yield return WaitForCondition(() => AcceptanceVideoFrameReady(application, videoId, media.Id, preview1, preview2, fixture), 90d, "Acceptance " + entry.codec + " decoded Program/Preview frame");
                fixture.frameReady = string.IsNullOrEmpty(_failure);
                fixture.nativeProbePassed = !requiresNative || _acceptanceNativeProbePassed;
                _acceptanceFixtureArtifacts.Add(fixture);
                if (!fixture.frameReady) yield break;
            }
        }

        private bool AcceptanceMediaTaskStep(ProjectApplication application)
        {
            var task = application.ReadModel.Task?.Model;
            if (task == null) return false;
            if (HarnessMediaImportContract.ShouldConfirmProbe(task, _acceptanceMediaProbeConfirmationRequested))
            {
                var confirmation = application.ConfirmMediaImport(true);
                if (confirmation.Status == ApplicationCommandStatus.Rejected)
                {
                    Fail("Acceptance media probe confirmation was rejected: " + DiagnosticText(confirmation.Diagnostic));
                    return false;
                }
                _acceptanceMediaProbeConfirmationRequested = true;
            }
            if (HarnessMediaImportContract.IsFailed(task))
            {
                Fail("Acceptance media probe failed: " + (task.Diagnostic == null ? task.Stage : task.Diagnostic.Message));
                return false;
            }
            return HarnessMediaImportContract.IsCompleted(task);
        }

        private bool AcceptanceVideoMediaAssetState(ProjectApplication application, string videoId, string mediaId)
        {
            var parameters = application.ReadModel.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>();
            var media = parameters.FirstOrDefault(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.MediaAssetParameterId);
            return media != null && string.Equals(media.BaseValue, mediaId, StringComparison.Ordinal) &&
                string.Equals(media.EffectiveValue, mediaId, StringComparison.Ordinal);
        }

        private bool AcceptanceVideoTransportState(ProjectApplication application, string videoId, string mediaId, bool playing, bool loop)
        {
            return AcceptanceVideoTransportState(application?.ReadModel, videoId, mediaId, playing, loop);
        }

        private static bool AcceptanceVideoTransportState(ApplicationReadModel model, string videoId, string mediaId, bool playing, bool loop)
        {
            var parameters = model?.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>();
            var media = parameters.FirstOrDefault(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.MediaAssetParameterId);
            var current = parameters.FirstOrDefault(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.PlayingParameterId);
            var looping = parameters.FirstOrDefault(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.LoopParameterId);
            return media != null && current != null && string.Equals(media.BaseValue, mediaId, StringComparison.Ordinal) &&
                string.Equals(media.EffectiveValue, mediaId, StringComparison.Ordinal) &&
                string.Equals(current.BaseValue, playing.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.EffectiveValue, playing.ToString(), StringComparison.OrdinalIgnoreCase) &&
                looping != null && string.Equals(looping.BaseValue, loop.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(looping.EffectiveValue, loop.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private bool AcceptanceVideoPlayingState(ProjectApplication application, string videoId, bool playing)
        {
            var parameters = application.ReadModel.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>();
            var current = parameters.FirstOrDefault(x => x.NodeId == videoId && x.ParameterId == VideoPlayerContract.PlayingParameterId);
            return current != null && string.Equals(current.EffectiveValue, playing.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private bool AcceptanceVideoFrameReady(ProjectApplication application, string videoId, string mediaId, string preview1, string preview2, HarnessAcceptanceFixtureArtifact fixture)
        {
            var model = application?.ReadModel;
            var graph = model?.Graph?.Model;
            var node = graph?.Nodes.FirstOrDefault(x => x.Id == videoId);
            var output = model?.Output?.Model;
            if (node == null || output == null || !string.Equals(node.Status, "Ready", StringComparison.OrdinalIgnoreCase)) return false;
            if (!AcceptanceVideoTransportState(model, videoId, mediaId, true, true)) return false;
            fixture.mediaBindingApplied = true;
            fixture.frameAfter = output.FrameNumber;
            CaptureAcceptancePreviewFrames(preview1, preview2, out fixture.preview1FrameAfter, out fixture.preview2FrameAfter);
            fixture.previewFrameAfter = Math.Min(fixture.preview1FrameAfter, fixture.preview2FrameAfter);
            var ownershipFrames = fixture.frameAfter > fixture.frameBefore && fixture.preview1FrameAfter > fixture.preview1FrameBefore && fixture.preview2FrameAfter > fixture.preview2FrameBefore;
            if (ownershipFrames) fixture.ownershipFramesObserved = true;
            ObserveAcceptanceOutputEvidence(model, videoId, fixture);
            return AcceptanceContract.FixtureFrameEvidenceObserved(fixture.ownershipFramesObserved, fixture.outputReadyObserved, fixture.realFrameObserved);
        }

        private bool AcceptanceVideoPrepareStarted(ProjectApplication application, string videoId)
        {
            return AcceptanceContract.VideoPrepareObserved(application?.ReadModel?.Graph?.Model, videoId);
        }

        private void CaptureAcceptancePreviewFrames(string preview1, string preview2, out ulong firstFrame, out ulong secondFrame)
        {
            var ownership = _composition?.CaptureOwnershipSnapshot();
            var first = ownership?.Previews?.FirstOrDefault(x => x != null && string.Equals(x.Id, preview1, StringComparison.Ordinal));
            var second = ownership?.Previews?.FirstOrDefault(x => x != null && string.Equals(x.Id, preview2, StringComparison.Ordinal));
            firstFrame = first?.FrameNumber ?? 0UL;
            secondFrame = second?.FrameNumber ?? 0UL;
        }

        private IEnumerator SaveAcceptanceProjectThroughUi(ProjectApplication application, string stage, string action)
        {
            yield return WaitForCondition(AcceptanceSaveButtonReady, 20d, "Acceptance " + stage + " UI save button attached, enabled, and pointer-ready");
            if (!string.IsNullOrEmpty(_failure)) yield break;

            var saveButton = _acceptancePresentationRoot?.RootVisualElement?.Q<Button>("project-save");
            if (saveButton == null || saveButton.panel == null || !saveButton.enabledInHierarchy ||
                saveButton.worldBound.width <= 0f || saveButton.worldBound.height <= 0f || !AcceptanceSaveButtonReady())
            {
                Fail("Acceptance " + stage + " UI save button is not attached, enabled, and pointer-ready; center pick=" + (_acceptanceSaveButtonPickTarget ?? "unknown") + ".");
                yield break;
            }
            var priorTaskId = application?.ReadModel?.Task?.Model?.TaskId ?? Guid.Empty;
            var callbackCount = 0;
            Action callbackObserver = () => callbackCount++;
            saveButton.clicked += callbackObserver;
            try
            {
                _acceptanceUiSave = CaptureAcceptanceUiSave(application, callbackCount, string.Empty);
                var submitError = FocusAndSubmitPickVerifiedAcceptanceSave(saveButton, out var focusedElement);
                _acceptanceUiSave = CaptureAcceptanceUiSave(application, callbackCount, focusedElement);
                if (!string.IsNullOrEmpty(submitError))
                {
                    Fail("Acceptance " + stage + " UI save focus/NavigationSubmit failed: " + submitError);
                    yield break;
                }
                if (callbackCount != 1)
                {
                    _acceptanceUiSave = CaptureAcceptanceUiSave(application, callbackCount, focusedElement);
                    Fail("Acceptance " + stage + " UI save Button callback count was " + callbackCount + ", expected exactly 1.");
                    yield break;
                }
                yield return WaitForCondition(() => AcceptanceSaveStarted(application, priorTaskId), 10d, "Acceptance " + stage + " UI save command task publication");
                if (!string.IsNullOrEmpty(_failure)) yield break;
                var saveTask = application?.ReadModel?.Task?.Model;
                _acceptanceUiSave = CaptureAcceptanceUiSave(application, callbackCount, focusedElement);
                if (AcceptanceContract.SaveTaskFailed(saveTask))
                {
                    Fail("Acceptance " + stage + " UI save task failed: " + AcceptanceContract.DescribeSaveTaskFailure(saveTask));
                    yield break;
                }
                var saveTaskId = saveTask.TaskId;
                yield return WaitForCondition(() => AcceptanceSaveReady(application, saveTaskId), 30d, "Acceptance " + stage + " UI save completion");
                if (!string.IsNullOrEmpty(_failure)) yield break;
                _acceptanceUiActions.Add(action);
            }
            finally
            {
                _acceptanceUiSave = CaptureAcceptanceUiSave(application, callbackCount, _acceptanceUiSave?.focusedElement);
                saveButton.clicked -= callbackObserver;
            }
        }

        private bool AcceptanceSaveButtonReady()
        {
            var button = _acceptancePresentationRoot?.RootVisualElement?.Q<Button>("project-save");
            if (button == null) { _acceptanceSaveButtonPickTarget = "button-missing"; return false; }
            if (button.panel == null) { _acceptanceSaveButtonPickTarget = "panel-missing"; return false; }
            if (!button.enabledInHierarchy) { _acceptanceSaveButtonPickTarget = "button-disabled"; return false; }
            if (button.worldBound.width <= 0f || button.worldBound.height <= 0f)
            {
                _acceptanceSaveButtonPickTarget = "button-has-no-usable-world-bound";
                return false;
            }

            var picked = button.panel.Pick(button.worldBound.center);
            _acceptanceSaveButtonPickTarget = DescribeAcceptancePickTarget(picked);
            // A pointer event bubbles from its picked leaf to its ancestors.
            // The Save Button must therefore be the picked leaf or contain it;
            // accepting an ancestor would permit an overlay to hide the Button.
            return picked != null && (ReferenceEquals(picked, button) || button.Contains(picked));
        }

        private static string DescribeAcceptancePickTarget(VisualElement target)
        {
            if (target == null) return "none";
            return string.IsNullOrWhiteSpace(target.name) ? target.GetType().Name : target.name + " (" + target.GetType().Name + ")";
        }

        private static string FocusAndSubmitPickVerifiedAcceptanceSave(Button target, out string focusedElement)
        {
            // AcceptanceSaveButtonReady has already proved that these physical
            // coordinates pick this Button (or one of its children). Focus the
            // proven Button, verify panel focus, then use the public keyboard
            // activation event. Button.Clickable owns the production callback;
            // this never calls Application or Coordinator directly.
            focusedElement = string.Empty;
            if (target == null) return "The Pick-verified Save Button is unavailable.";

            try
            {
                target.Focus();
                var focused = target.panel?.focusController?.focusedElement as VisualElement;
                focusedElement = DescribeAcceptancePickTarget(focused);
                if (focused == null || (!ReferenceEquals(focused, target) && !target.Contains(focused)))
                    return "Panel focus did not resolve to project-save or its child; focused=" + focusedElement + ".";
                target.SendEvent(NavigationSubmitEvent.GetPooled());
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private HarnessAcceptanceUiSaveArtifact CaptureAcceptanceUiSave(ProjectApplication application, int callbackCount, string focusedElement)
        {
            var banner = _acceptancePresentationRoot?.RootVisualElement?.Q<Label>("banner-layer");
            var task = application?.ReadModel?.Task?.Model;
            return new HarnessAcceptanceUiSaveArtifact
            {
                callbackCount = callbackCount,
                focusedElement = focusedElement ?? string.Empty,
                bannerText = banner?.text ?? string.Empty,
                bannerVisible = banner != null && banner.ClassListContains("is-visible"),
                taskBeforeId = _acceptanceUiSave?.taskBeforeId ?? task?.TaskId.ToString() ?? string.Empty,
                taskBeforeKind = _acceptanceUiSave?.taskBeforeKind ?? task?.Kind ?? string.Empty,
                taskBeforeStatus = _acceptanceUiSave?.taskBeforeStatus ?? task?.Status ?? string.Empty,
                taskAfterId = task?.TaskId.ToString() ?? string.Empty,
                taskAfterKind = task?.Kind ?? string.Empty,
                taskAfterStage = task?.Stage ?? string.Empty,
                taskAfterStatus = task?.Status ?? string.Empty,
                taskAfterPath = task?.Path ?? string.Empty,
                taskAfterDiagnosticCode = task?.Diagnostic?.Code.Value ?? string.Empty,
                taskAfterDiagnosticMessage = task?.Diagnostic?.Message ?? string.Empty,
                taskAfterExceptionType = task?.Diagnostic?.Exception?.TypeName ?? string.Empty,
                taskAfterExceptionMessage = task?.Diagnostic?.Exception?.Message ?? string.Empty,
                taskAfterExceptionStackTrace = task?.Diagnostic?.Exception?.StackTrace ?? string.Empty
            };
        }

        private static bool AcceptanceSaveStarted(ProjectApplication application, Guid priorTaskId)
        {
            var task = application?.ReadModel?.Task?.Model;
            return AcceptanceContract.SaveTaskPublished(task, priorTaskId);
        }

        private static bool AcceptanceSaveReady(ProjectApplication application, Guid expectedTaskId)
        {
            var project = application?.ReadModel?.Project?.Model;
            var task = application?.ReadModel?.Task?.Model;
            return project != null && !project.IsDirty && task != null && task.TaskId == expectedTaskId &&
                string.Equals(task.Kind, "Save", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(task.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        private bool ProbeAcceptanceNative(HarnessAcceptanceFixtureArtifact fixture)
        {
            try
            {
                var probe = new PInvokeHapNativeApi().ProbeInstalledBinary();
                _acceptanceNativeProbePassed = probe != null && probe.IsAvailable;
                _acceptanceNativeDiagnostic = probe == null ? "Hap native probe returned no result." : probe.DiagnosticCode + ": " + probe.Message;
                fixture.nativeProbePassed = _acceptanceNativeProbePassed;
                return _acceptanceNativeProbePassed;
            }
            catch (Exception exception)
            {
                _acceptanceNativeProbePassed = false;
                _acceptanceNativeDiagnostic = exception.Message;
                fixture.nativeProbePassed = false;
                return false;
            }
        }

        private bool ValidateProjectFiles(string root, bool canonicalMainReadable, out string error)
        {
            error = string.Empty;
            _acceptanceProjectReadable = false;
            _acceptanceProjectWritable = false;
            _acceptanceBackupReadable = false;
            try
            {
                var fullRoot = Path.GetFullPath(root);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(fullRoot))
                {
                    error = "Acceptance project root is missing.";
                    return false;
                }
                var main = Path.Combine(fullRoot, PersistenceConstants.MainFileName);
                var backup = Path.Combine(fullRoot, PersistenceConstants.BackupFileName);
                if (canonicalMainReadable)
                {
                    if (!File.Exists(main)) { error = "Acceptance project.json is missing."; return false; }
                    using (var stream = File.OpenRead(main))
                    {
                        if (stream.Length <= 0) { error = "Acceptance project.json is empty."; return false; }
                    }
                    _acceptanceProjectReadable = true;
                }
                else if (File.Exists(main))
                {
                    // Recovery deliberately keeps the damaged main file. It
                    // must not be reported as readable merely because it
                    // exists; only the backup is a readable source here.
                    _acceptanceProjectReadable = false;
                }
                if (File.Exists(backup))
                {
                    using (var stream = File.OpenRead(backup))
                    {
                        _acceptanceBackupReadable = stream.Length > 0;
                    }
                }
                var probe = Path.Combine(fullRoot, ".acceptance-write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, _runId, System.Text.Encoding.UTF8);
                File.Delete(probe);
                _acceptanceProjectWritable = true;
                return true;
            }
            catch (Exception exception) { error = "Acceptance project file access failed: " + exception.Message; return false; }
        }

        private bool TryCaptureCanonicalProjectFingerprint(ProjectApplication application, string stage, out string fingerprint)
        {
            fingerprint = string.Empty;
            var captured = application?.CaptureCanonicalProjectFingerprint();
            if (!captured.HasValue || captured.Value.IsFailure)
            {
                var diagnostic = captured.HasValue ? captured.Value.Diagnostic : null;
                Fail("Acceptance " + stage + " canonical Project fingerprint failed: " + DiagnosticText(diagnostic));
                return false;
            }
            fingerprint = captured.Value.Value;
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                Fail("Acceptance " + stage + " canonical Project fingerprint was empty.");
                return false;
            }
            return true;
        }

        private static string ReadAcceptanceMarkerValue(string projectRoot, string key)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(key)) return string.Empty;
            try
            {
                var marker = Path.Combine(Path.GetFullPath(projectRoot), ".acceptance-marker.txt");
                if (!File.Exists(marker)) return string.Empty;
                var prefix = key + "=";
                foreach (var line in File.ReadAllLines(marker))
                {
                    if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(prefix.Length);
                }
            }
            catch (Exception) { }
            return string.Empty;
        }

        private void ExitApplicationForAcceptance(ProjectApplication application)
        {
            var result = application.Exit(UnsavedChangesDecision.Discard);
            if (result.Status == ApplicationCommandStatus.Rejected) Fail("Acceptance Application.Exit was rejected: " + DiagnosticText(result.Diagnostic));
        }

        private void FinishAcceptance()
        {
            if (_acceptanceArtifactWritten) return;
            try
            {
                FinishAcceptanceCore();
            }
            catch (Exception exception)
            {
                if (string.IsNullOrEmpty(_failure)) _failure = "Acceptance finalization failed: " + exception;
                FinishAcceptanceFallback();
            }
        }

        private void FinishAcceptanceCore()
        {
            var application = _composition?.Application;
            var model = application?.ReadModel;
            var before = _composition?.CaptureOwnershipSnapshot();
            var uiLayout = CaptureAcceptanceUiLayout();
            var composition = _composition;
            var productionCompositionUsed = composition != null;
            var productionCatalogUsed = composition?.RuntimeFactory?.CurrentComposition != null;
            if (composition != null)
            {
                composition.Dispose();
                var after = composition.CaptureOwnershipSnapshot();
                var teardown = HarnessOwnershipContract.ValidateTeardown(after);
                if (string.IsNullOrEmpty(_failure) && !string.IsNullOrEmpty(teardown)) _failure = teardown;
            }
            var artifact = new HarnessArtifact
            {
                runId = _runId,
                mode = "acceptance",
                stage = _options.AcceptanceStage.ToString(),
                status = string.IsNullOrEmpty(_failure) ? HarnessRunStatus.Passed.ToString() : (IsEnvironmentFailure(_failure) ? HarnessRunStatus.EnvironmentFailed.ToString() : HarnessRunStatus.Failed.ToString()),
                failure = _failure ?? string.Empty,
                codec = "H264,VP8,Hap1,Hap5,HapY,HapM",
                platform = UnityApplication.platform.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                unityVersion = UnityApplication.unityVersion,
                buildId = UnityApplication.buildGUID,
                developmentBuild = Debug.isDebugBuild,
                buildOptions = Debug.isDebugBuild ? "Development" : "None",
                projectRevision = application?.ReadProject()?.DocumentRevision.ToString() ?? string.Empty,
                productionCompositionUsed = productionCompositionUsed,
                productionCatalogUsed = productionCatalogUsed,
                acceptance = new HarnessAcceptanceArtifact
                {
                    stage = _options.AcceptanceStage.ToString(),
                    acceptanceContractVersion = AcceptanceContract.CurrentArtifactContractVersion,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    buildId = UnityApplication.buildGUID,
                    fixtureRoot = _options.FixtureRoot,
                    editorAssemblyExcluded = _acceptanceEditorAssemblyExcluded,
                    productionCompositionUsed = productionCompositionUsed,
                    productionCatalogUsed = productionCatalogUsed,
                    presentationRootAvailable = _acceptancePresentationRoot != null,
                    programAndPreviewsReady = _acceptanceOutputsObserved,
                    requiredGraphObserved = _acceptanceRequiredGraphObserved,
                    realFrameObserved = _acceptanceRealFrameObserved,
                    valueControlUpdated = _acceptanceValueControlUpdated,
                    valueControlRemapped = _acceptanceValueControlRemapped,
                    presetTriggerFired = _acceptancePresetTriggerFired,
                    logicalControlStateObserved = _acceptanceLogicalControlStateObserved,
                    mediaPortable = _acceptanceMediaPortable,
                    valueControlId = _acceptanceValueControlId,
                    presetTriggerId = _acceptancePresetTriggerId,
                    presetId = _acceptancePresetId,
                    uiSavePickTarget = _acceptanceSaveButtonPickTarget ?? string.Empty,
                    uiSave = _acceptanceUiSave,
                    uiLayout = uiLayout,
                    uiActions = _acceptanceUiActions.ToArray(),
                    fixtures = _acceptanceFixtureArtifacts.ToArray(),
                    lastOutput = _acceptanceLastOutput,
                    persistence = _acceptancePersistence ?? new HarnessAcceptancePersistenceArtifact { projectRoot = _projectRoot, expectedFingerprint = _options.ExpectedFingerprint, expectedBackupFingerprint = _options.ExpectedBackupFingerprint, saved = _acceptanceSaved, reopened = _acceptanceReopened, recovered = _acceptanceRecovered, dirtyAfterRecovery = _acceptanceDirtyAfterRecovery, mainFilePreservedAfterRecovery = _acceptanceMainFilePreserved },
                    fileProjectReadable = _acceptanceProjectReadable,
                    fileProjectWritable = _acceptanceProjectWritable,
                    backupFileReadable = _acceptanceBackupReadable,
                    nativeProbePassed = _acceptanceNativeProbePassed,
                    nativeProbeDiagnostic = _acceptanceNativeDiagnostic ?? string.Empty,
                    ownershipTeardown = string.IsNullOrEmpty(_failure) ? string.Empty : _failure
                }
            };
            var contractFailure = AcceptanceContract.ValidateStage(_options.AcceptanceStage, artifact.acceptance);
            if (string.IsNullOrEmpty(_failure) && !string.IsNullOrEmpty(contractFailure))
            {
                _failure = contractFailure;
                artifact.status = HarnessRunStatus.Failed.ToString();
                artifact.failure = contractFailure;
            }
            var directory = string.IsNullOrWhiteSpace(_artifactDirectory) ? (_options.ArtifactDirectory ?? Path.Combine(UnityApplication.persistentDataPath, "ShitDesigner", "AcceptanceArtifacts")) : _artifactDirectory;
            var write = HarnessArtifactWriter.Write(directory, artifact);
            if (!write.Success) { artifact.artifactWriteError = write.Error; Debug.LogError("Standalone Acceptance artifact write failed: " + write.Error); }
            ExitCode = HarnessArtifactWriter.GetExitCode(artifact, write);
            _acceptanceArtifactWritten = true;
            if (_options.ShouldQuit && UnityApplication.isBatchMode) UnityApplication.Quit(ExitCode);
        }

        private void FinishAcceptanceFallback()
        {
            if (_composition != null)
            {
                try { _composition.Dispose(); }
                catch (Exception exception)
                {
                    if (string.IsNullOrEmpty(_failure)) _failure = "Acceptance teardown failed during finalization: " + exception;
                }
            }
            var status = IsEnvironmentFailure(_failure) ? HarnessRunStatus.EnvironmentFailed.ToString() : HarnessRunStatus.Failed.ToString();
            var artifact = new HarnessArtifact
            {
                runId = _runId,
                mode = "acceptance",
                stage = _options == null ? string.Empty : _options.AcceptanceStage.ToString(),
                status = status,
                failure = _failure ?? "Acceptance finalization failed.",
                platform = UnityApplication.platform.ToString(),
                operatingSystem = SystemInfo.operatingSystem,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                unityVersion = UnityApplication.unityVersion,
                buildId = UnityApplication.buildGUID,
                developmentBuild = Debug.isDebugBuild,
                buildOptions = Debug.isDebugBuild ? "Development" : "None",
                acceptance = new HarnessAcceptanceArtifact
                {
                    stage = _options == null ? string.Empty : _options.AcceptanceStage.ToString(),
                    acceptanceContractVersion = AcceptanceContract.CurrentArtifactContractVersion,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    buildId = UnityApplication.buildGUID,
                    fixtureRoot = _options == null ? string.Empty : _options.FixtureRoot,
                    editorAssemblyExcluded = _acceptanceEditorAssemblyExcluded,
                    productionCompositionUsed = _composition != null,
                    presentationRootAvailable = _acceptancePresentationRoot != null,
                    programAndPreviewsReady = _acceptanceOutputsObserved,
                    requiredGraphObserved = _acceptanceRequiredGraphObserved,
                    realFrameObserved = _acceptanceRealFrameObserved,
                    valueControlUpdated = _acceptanceValueControlUpdated,
                    valueControlRemapped = _acceptanceValueControlRemapped,
                    presetTriggerFired = _acceptancePresetTriggerFired,
                    logicalControlStateObserved = _acceptanceLogicalControlStateObserved,
                    mediaPortable = _acceptanceMediaPortable,
                    valueControlId = _acceptanceValueControlId,
                    presetTriggerId = _acceptancePresetTriggerId,
                    presetId = _acceptancePresetId,
                    uiSavePickTarget = _acceptanceSaveButtonPickTarget ?? string.Empty,
                    uiSave = _acceptanceUiSave,
                    uiLayout = CaptureAcceptanceUiLayout(),
                    fixtures = _acceptanceFixtureArtifacts.ToArray(),
                    lastOutput = _acceptanceLastOutput,
                    persistence = _acceptancePersistence,
                    fileProjectReadable = _acceptanceProjectReadable,
                    fileProjectWritable = _acceptanceProjectWritable,
                    backupFileReadable = _acceptanceBackupReadable,
                    nativeProbePassed = _acceptanceNativeProbePassed,
                    nativeProbeDiagnostic = _acceptanceNativeDiagnostic ?? string.Empty,
                    ownershipTeardown = _failure ?? string.Empty
                }
            };
            var directory = string.IsNullOrWhiteSpace(_artifactDirectory) ? (_options?.ArtifactDirectory ?? Path.Combine(UnityApplication.persistentDataPath, "ShitDesigner", "AcceptanceArtifacts")) : _artifactDirectory;
            try
            {
                var write = HarnessArtifactWriter.Write(directory, artifact);
                if (!write.Success) artifact.artifactWriteError = write.Error;
                ExitCode = HarnessArtifactWriter.GetExitCode(artifact, write);
            }
            catch (Exception exception)
            {
                artifact.artifactWriteError = exception.ToString();
                ExitCode = 1;
            }
            _acceptanceArtifactWritten = true;
            if (_options != null && _options.ShouldQuit && UnityApplication.isBatchMode) UnityApplication.Quit(ExitCode);
        }

        private HarnessAcceptanceUiLayoutArtifact CaptureAcceptanceUiLayout()
        {
            var root = _acceptancePresentationRoot?.RootVisualElement;
            var panelSettings = _acceptancePresentationRoot?.Document?.panelSettings;
            return new HarnessAcceptanceUiLayoutArtifact
            {
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                screenFullScreen = Screen.fullScreen,
                panelScale = panelSettings == null ? 0f : panelSettings.scale,
                panelScaleMode = panelSettings == null ? string.Empty : panelSettings.scaleMode.ToString(),
                panelReferenceWidth = panelSettings == null ? 0 : panelSettings.referenceResolution.x,
                panelReferenceHeight = panelSettings == null ? 0 : panelSettings.referenceResolution.y,
                elements = new[]
                {
                    CaptureAcceptanceUiElement(root, "root", root, 1),
                    CaptureAcceptanceUiElement(root, "top-bar", root?.Q("top-bar")),
                    CaptureAcceptanceUiElement(root, "project-save", root?.Q<Button>("project-save")),
                    CaptureAcceptanceUiElement(root, "dock-tree", root?.Q("dock-tree")),
                    CaptureAcceptanceUiElement(root, "graph-toolbar", root?.Q("graph-toolbar")),
                    CaptureAcceptanceUiElement(root, "status-bar", root?.Q("status-bar"))
                }
            };
        }

        private static HarnessAcceptanceUiElementLayoutArtifact CaptureAcceptanceUiElement(VisualElement root, string name, VisualElement element, int count = -1)
        {
            if (count < 0) count = root == null ? 0 : root.Query<VisualElement>(name).ToList().Count;
            if (element == null) return new HarnessAcceptanceUiElementLayoutArtifact { name = name, count = count };
            var bounds = element.worldBound;
            var style = element.resolvedStyle;
            return new HarnessAcceptanceUiElementLayoutArtifact
            {
                name = name,
                count = count,
                x = bounds.x,
                y = bounds.y,
                width = bounds.width,
                height = bounds.height,
                flexDirection = style.flexDirection.ToString(),
                flexGrow = style.flexGrow,
                flexShrink = style.flexShrink,
                flexBasis = style.flexBasis.ToString(),
                display = style.display.ToString(),
                pickingMode = element.pickingMode.ToString(),
                enabled = element.enabledInHierarchy
            };
        }
    }
}
