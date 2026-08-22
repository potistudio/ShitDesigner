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
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityApplication = UnityEngine.Application;

namespace ShitDesigner.TestHarness
{
    /// <summary>
    /// Standalone-only acceptance and soak runner.  This component talks to
    /// the production composition through ProjectApplication's command/read
    /// ports and never reaches into Runtime or Node implementation state.
    /// The assembly is guarded by SHITDESIGNER_TEST_HARNESS, so a normal
    /// product build does not contain this component.
    /// </summary>
    public sealed partial class StandalonePerformanceHarness : MonoBehaviour
    {
        [SerializeField] private ProductionBootstrapBehaviour _bootstrap;
        [SerializeField] private ProductionBootstrapAssets _assets;
        [SerializeField] private bool _runOnStart = true;
        [SerializeField] private string _corpusRoot;
        [SerializeField] private string _artifactDirectory;
        [SerializeField] private double _warmupSeconds = 30d;
        [SerializeField] private double _measureSeconds = 600d;
        [SerializeField] private bool _fixtureMode;

        private ProductionCompositionRoot _composition;
        private HarnessOptions _options;
        private HarnessMetricAccumulator _metrics;
        private bool _running;
        private bool _collecting;
        private bool _finished;
        private double _measureDeadline;
        private HarnessInteractionScheduler _interactionScheduler;
        private int _controlEvents;
        private int _presetTicks;
        private string _presetId;
        private string _presetTriggerId;
        private string _presetParameterId = VideoPlayerContract.SpeedParameterId;
        private string _presetExpectedValue;
        private bool _presetVerificationActive;
        private double _measurementStart;
        private string _runId;
        private string _failure;
        private CorpusValidationResult _corpus;
        private string _projectRoot;
        private string _videoNodeId;
        private string _generatorNodeId;
        private bool _mediaImportStarted;
        private bool _mediaProbeConfirmationRequested;
        private bool _mediaBindingQueued;
        private readonly List<Guid> _videoBindingCommandIds = new List<Guid>();
        private bool _controlsConfigured;
        private bool _previewsConfigured;
        private bool _artifactWritten;
        private bool _scenarioSaved;
        private HarnessDiagnosticsExportArtifact _diagnosticsExport;
        private readonly List<string> _operationSequence = new List<string>();
        private HarnessCodec _codec;
        private int _gcCollections0;
        private int _gcCollections1;
        private int _gcCollections2;
        private long _gcAllocatedDuringMeasurement;
        private ProfilerRecorder _gcAllocationRecorder;
        private bool _gcRecorderStarted;
        private string _gcRecorderStartFailure;
        private ProductionOwnershipSnapshot _lastOwnershipSnapshot;
        private readonly ProductionPerformanceSurfaceSnapshot[] _performancePreviewBuffer = new ProductionPerformanceSurfaceSnapshot[8];
        private readonly HarnessTimingCompletionTracker _timingCompletions = new HarnessTimingCompletionTracker();
        private ulong _measurementStartFrame;
        private bool _timingDrainActive;
        private bool _timingDrainCompleted;
        private int _timingDrainPresentedFrames;
        private ulong _lastDrainPresentationFrame;
        private ulong _lastCollectedPresentationFrame;
        private ulong _frameTimingGateStartPerformanceFrame;
        private ulong _frameTimingGateReadyPerformanceFrame;
        private double _frameTimingGateStartedAt = double.NaN;
        private double _frameTimingGateWaitSeconds = double.NaN;
        private ProductionFrameTimingDiagnostic _lastFrameTimingDiagnostic = ProductionFrameTimingDiagnostic.Unavailable;
        // Unity may complete the final retained timing after the last measured
        // presentation.  Application publishes that completion with the next
        // Tick, so the finite FrameTiming history needs one extra host frame
        // to cross the public ReadModel boundary before unresolved metrics are
        // converted to explicit unavailable samples.
        private const int FrameTimingDrainPresentationCount = FrameTimingCompletionCorrelation.MaximumPendingFrames + 1;
        private const double FrameTimingWarmupTimeoutSeconds = 30d;
        private const double PresetVerificationTimeoutSeconds = 20d;
        private readonly HarnessFinalizationGuard _compositionTeardown = new HarnessFinalizationGuard();

        public bool IsFinished => _finished;
        public int ExitCode { get; private set; }
        public string Failure => _failure ?? string.Empty;

        private void Start()
        {
            _options = HarnessOptions.Parse(Environment.GetCommandLineArgs());
            _runId = "harness-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (_options.Mode == HarnessMode.Acceptance)
            {
                if (_runOnStart) StartCoroutine(RunAcceptance());
                return;
            }
            if (_options.HasOverrides)
            {
                _corpusRoot = _options.CorpusRoot ?? _corpusRoot;
                _artifactDirectory = _options.ArtifactDirectory ?? _artifactDirectory;
                _warmupSeconds = _options.WarmupSeconds;
                _measureSeconds = _options.MeasureSeconds;
                _fixtureMode = _options.FixtureMode;
            }
            if (_fixtureMode && !_options.HasDurationOverrides)
            {
                // Fixture mode exists for the short contract/integration
                // loop only. The production defaults remain 30s + 600s and
                // are never silently shortened.
                _warmupSeconds = 0.1d;
                _measureSeconds = 2d;
            }
            _codec = _options.Codec;
            _metrics = new HarnessMetricAccumulator();
            if (_runOnStart) StartCoroutine(Run());
        }

        private void Update()
        {
            if (_options != null && _options.Mode == HarnessMode.Acceptance) return;
            if (!_running || _composition == null) return;
            var hostTime = Time.realtimeSinceStartupAsDouble;

            // Schedule logical input from absolute measurement time. A host
            // frame can cross the deadline, so consume the final partial
            // interval before closing the fixed measurement window. The
            // scheduler clamps its due count to the deadline and is closed
            // below, so no later frame can dispatch another input.
            if (HarnessMeasurementBoundaryContract.AllowsInteractionInput(_collecting) && _interactionScheduler != null)
                _interactionScheduler.DispatchDue(hostTime, DispatchLogicalControlUpdate);
            if (HarnessMeasurementBoundaryContract.ShouldCloseWindow(_collecting, hostTime >= _measureDeadline))
            {
                // The deadline frame is still allowed to start the final
                // due preset. Its public read-model verification/release may
                // complete during the existing timing-drain grace, but no
                // later frame can start another trigger or collect evidence.
                TryStartDuePresetVerification(hostTime);
                BeginTimingDrain();
            }
            if (_collecting || _timingDrainActive) CollectCompletedFrame();
        }

        // The production LateUpdate driver polls input before ticking the
        // Application. This call uses only the public keyboard port and is
        // intentionally made from Update, before that driver runs.
        private bool DispatchLogicalControlUpdate()
        {
            var pressed = (_controlEvents & 1) == 0;
            var result = _composition.Application.HandleKeyboard(PhysicalKey.From("harness.tick", "<Harness>/tick"), pressed);
            if (result.Status == ApplicationCommandStatus.Rejected)
            {
                Fail("Logical control update was rejected: " + DiagnosticText(result.Diagnostic));
                return false;
            }
            // A matched logical-control input is queued as Accepted. An
            // Applied result means no mapping matched and must not count
            // toward the 120 updates/second contract.
            if (result.Status == ApplicationCommandStatus.Accepted) _controlEvents++;
            return true;
        }

        private IEnumerator Run()
        {
            _running = true;
            var core = RunCore();
            while (true)
            {
                bool moved;
                try { moved = core.MoveNext(); }
                catch (Exception exception) { Fail("Unhandled harness exception: " + exception); break; }
                if (!moved) break;
                yield return core.Current;
            }
            _running = false;
            if (!_finished && string.IsNullOrEmpty(_failure)) Fail("Harness stopped before completion.");
            if (!_artifactWritten) FinishRun();
        }

        private IEnumerator RunCore()
        {
            _corpus = PerformanceCorpusValidator.Validate(ResolveCorpusRoot(), _codec);
            if (!_corpus.IsValid) { FailEnvironment(_corpus.Error); yield break; }
            if (!TryAcquireComposition()) { Fail("Production Composition Root was not available."); yield break; }
            if (!ConfigureScenario()) { yield break; }
            yield return WaitForCondition(() => ScenarioReady(), 120d);
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!SaveCanonicalScenario()) yield break;

            var warmupDeadline = Time.realtimeSinceStartupAsDouble + Math.Max(0d, _warmupSeconds);
            while (Time.realtimeSinceStartupAsDouble < warmupDeadline)
            {
                var readiness = ObserveWarmupReadiness();
                if (readiness.IsFailure) { Fail("Warm-up readiness failed: " + readiness.Reason); yield break; }
                yield return null;
            }
            var completedWarmup = ObserveWarmupReadiness();
            if (completedWarmup.IsFailure) { Fail("Warm-up readiness failed: " + completedWarmup.Reason); yield break; }
            if (!completedWarmup.IsReady) { Fail("Warm-up did not complete: " + completedWarmup.Reason); yield break; }
            // FrameTiming completes after the presented frame. Do not let a
            // first, FPS-baselineless completion cross the measurement fence:
            // wait for one fully valid public timing projection while the
            // harness is still in warm-up, then start the exact interval.
            _frameTimingGateStartPerformanceFrame = _composition.Application.ReadModel?.Output?.Model?.PerformanceFrameNumber ?? 0UL;
            _frameTimingGateStartedAt = Time.realtimeSinceStartupAsDouble;
            yield return WaitForCondition(InitialFrameTimingReady, FrameTimingWarmupTimeoutSeconds,
                "initial public Unity FrameTiming completion");
            _frameTimingGateWaitSeconds = Math.Max(0d, Time.realtimeSinceStartupAsDouble - _frameTimingGateStartedAt);
            _frameTimingGateReadyPerformanceFrame = _composition.Application.ReadModel?.Output?.Model?.PerformanceFrameNumber ?? 0UL;
            if (!string.IsNullOrEmpty(_failure)) yield break;
            var measurementFrame = _composition.Application.ReadModel?.Output?.Model?.FrameNumber ?? 0UL;
            var diagnosticsReset = _composition.Application.ResetDiagnosticsForMeasurement(measurementFrame);
            if (diagnosticsReset.IsFailure) { Fail("Runtime diagnostics could not be reset for measurement: " + DiagnosticText(diagnosticsReset.Diagnostic)); yield break; }
            _metrics.Reset();
            // Warm-up input is intentionally excluded. Start the interaction
            // counters at the same boundary as timing and diagnostics.
            _controlEvents = 0;
            _interactionScheduler = null;
            _presetTicks = 0;
            _presetVerificationActive = false;
            _measurementStartFrame = _composition.Application.ReadModel?.Output?.Model?.FrameNumber ?? 0UL;
            _lastCollectedPresentationFrame = _measurementStartFrame;
            _timingCompletions.BeginMeasurement(_measurementStartFrame);
            _timingDrainActive = false;
            _timingDrainCompleted = false;
            _timingDrainPresentedFrames = 0;
            _lastDrainPresentationFrame = _measurementStartFrame;
            _gcAllocatedDuringMeasurement = 0;
            _gcRecorderStartFailure = string.Empty;
            try
            {
                _gcAllocationRecorder = ProfilerRecorder.StartNew(HarnessGcAllocationContract.CounterCategory,
                    HarnessGcAllocationContract.CounterName, HarnessGcAllocationContract.SampleCapacity, HarnessGcAllocationContract.MarkerOptions);
                _gcRecorderStarted = HarnessGcAllocationContract.IsAllThreadByteMeasurement(
                    _gcAllocationRecorder.Valid, _gcAllocationRecorder.UnitType);
            }
            catch (Exception exception)
            {
                _gcRecorderStarted = false;
                _gcRecorderStartFailure = exception.GetType().Name + ": " + exception.Message;
            }
            if (!_gcRecorderStarted)
            {
                try { _gcAllocationRecorder.Dispose(); }
                catch { }
                Fail("Unity GC allocation byte measurement is unavailable: " + DescribeGcAllocationRecorder());
                yield break;
            }
            _gcCollections0 = GC.CollectionCount(0);
            _gcCollections1 = GC.CollectionCount(1);
            _gcCollections2 = GC.CollectionCount(2);
            _collecting = true;
            _measurementStart = Time.realtimeSinceStartupAsDouble;
            _measureDeadline = _measurementStart + Math.Max(0.01d, _measureSeconds);
            _interactionScheduler = new HarnessInteractionScheduler(_measurementStart, _measureSeconds);
            // The fixed 600s window ends at _measureDeadline. A final preset
            // may already be verifying there, so permit its 20s public
            // confirmation plus the bounded timing-drain grace without
            // extending input, GC, or presentation collection.
            yield return WaitForCondition(() => _finished, Math.Max(1d, _measureSeconds + PresetVerificationTimeoutSeconds + 15d));
            if (!string.IsNullOrEmpty(_failure)) yield break;
            if (!_finished) { Fail("Measurement did not reach its completion condition."); yield break; }
        }

        private bool TryAcquireComposition()
        {
            _bootstrap = _bootstrap == null ? GetComponent<ProductionBootstrapBehaviour>() : _bootstrap;
            if (_bootstrap == null) _bootstrap = FindAnyObjectByType<ProductionBootstrapBehaviour>();
            _assets = _assets == null ? GetComponent<ProductionBootstrapAssets>() : _assets;
            if (_assets == null) _assets = FindAnyObjectByType<ProductionBootstrapAssets>();
            // A normal production scene already owns the root. Reuse it so
            // the Player harness observes exactly the same provider/session.
            if (_bootstrap != null && _bootstrap.Composition != null)
            {
                _composition = _bootstrap.Composition;
                return true;
            }
            return false;
        }

        private bool ConfigureScenario()
        {
            var application = _composition?.Application;
            if (application == null) { Fail("Production Application API was unavailable."); return false; }
            _projectRoot = Path.Combine(UnityApplication.persistentDataPath, "ShitDesigner", "Harness", _runId);
            var created = application.NewProject("Standalone Harness " + _runId, _projectRoot, UnsavedChangesDecision.Discard);
            if (created.Status == ApplicationCommandStatus.Rejected) { Fail("NewProject rejected: " + DiagnosticText(created.Diagnostic)); return false; }
            _operationSequence.Clear();
            _operationSequence.Add("NewProject");
            var graph = new[]
            {
                new HarnessNode("3D Generator", "shitdesigner.scene.3d", out _generatorNodeId),
                new HarnessNode("2D Generator", "shitdesigner.scene.2d", out var twoD),
                new HarnessNode("Shader Effect", "shitdesigner.shader.effect", out var effect),
                new HarnessNode("VideoPlayer", "shitdesigner.video.player", out _videoNodeId),
                new HarnessNode("2-input Blend (Generators)", "shitdesigner.shader.blend2", out var blend),
                new HarnessNode("2-input Blend (Video)", "shitdesigner.shader.blend2", out var blendVideo),
                new HarnessNode("Feedback", "system.feedback", out var feedback),
                new HarnessNode("Preview 1", "system.preview", out var preview1),
                new HarnessNode("Preview 2", "system.preview", out var preview2)
            };
            foreach (var node in graph)
            {
                var request = new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, node.Id,
                    nodeTypeId: node.Type, nodeDisplayName: node.Name, positionX: node.X, positionY: node.Y);
                if (application.SubmitGraph(request).Status == ApplicationCommandStatus.Rejected)
                { Fail("AddNode rejected for " + node.Name); return false; }
                _operationSequence.Add("AddNode:" + node.Type + ":" + node.Name);
            }
            var program = application.ReadModel.Graph?.Model?.Nodes.FirstOrDefault(x => x.TypeId == GraphConstants.ProgramOutputTypeId);
            // Connections are queued through Application and become visible
            // only after the production frame boundary, preserving the real
            // FrameCoordinator transaction.
            var connections = new[]
            {
                new HarnessConnection(_generatorNodeId, "image", blend, "a"),
                new HarnessConnection(twoD, "image", blend, "b"),
                new HarnessConnection(blend, "image", blendVideo, "a"),
                new HarnessConnection(_videoNodeId, "image", blendVideo, "b"),
                new HarnessConnection(blendVideo, "image", effect, "input"),
                new HarnessConnection(effect, "image", feedback, "input"),
                new HarnessConnection(feedback, "image", program?.Id ?? string.Empty, "image"),
                new HarnessConnection(_videoNodeId, "image", preview1, "image"),
                new HarnessConnection(_videoNodeId, "image", preview2, "image")
            };
            var topology = HarnessScenarioTopology.Validate(
                graph.Select(x => new HarnessTopologyNode(x.Id, x.Type)).Concat(
                    program == null ? Enumerable.Empty<HarnessTopologyNode>() : new[] { new HarnessTopologyNode(program.Id, GraphConstants.ProgramOutputTypeId) }),
                connections.Select(x => new HarnessTopologyEdge(x.Source, x.Destination)).Concat(
                    program == null ? Enumerable.Empty<HarnessTopologyEdge>() : new[] { new HarnessTopologyEdge(feedback, program.Id) }));
            if (!string.IsNullOrEmpty(topology)) { Fail("Harness scenario topology is invalid: " + topology); return false; }
            foreach (var edge in connections)
            {
                var request = new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), edge.Source, edge.SourcePort, edge.Destination, edge.DestinationPort);
                if (application.SubmitGraph(request).Status == ApplicationCommandStatus.Rejected) { Fail("Connect rejected: " + edge.DestinationPort); return false; }
                _operationSequence.Add("Connect:" + edge.SourcePort + "->" + edge.DestinationPort);
            }
            _operationSequence.Add("ScenarioGraphCommitted");
            return true;
        }

        private bool ScenarioReady()
        {
            var model = _composition?.Application?.ReadModel;
            if (model == null || model.Graph?.Model == null) return false;
            var graph = model.Graph.Model;
            if (graph.Nodes.Count < 10 || graph.Connections.Count < 9) return false;
            if (model.Output?.Model == null || model.Output.Model.Program == null) return false;
            if (!_controlsConfigured)
            {
                var controlId = LogicalControlId.New().Value;
                var control = _composition.Application.AddLogicalControl(new ApplicationLogicalControlRequest(controlId, "Harness Tick", ApplicationLogicalControlKind.Value,
                    mappings: new[] { new ApplicationControlMappingRequest("harness.tick", "<Harness>/tick") }));
                if (control.Status == ApplicationCommandStatus.Rejected) { Fail("Logical control creation rejected: " + DiagnosticText(control.Diagnostic)); return false; }
                var target = _composition.Application.SetLogicalControlTargets(controlId, new[]
                {
                    HarnessInteractionContract.CreatePerformanceTickSpeedTarget(_videoNodeId)
                });
                if (target.Status == ApplicationCommandStatus.Rejected) { Fail("Logical control target rejected: " + DiagnosticText(target.Diagnostic)); return false; }
                var expression = _composition.Application.ApplyExpression(new ApplicationExpressionDraft(_videoNodeId, _presetParameterId,
                    ApplicationExpressionKind.Max,
                    left: new ApplicationExpressionDraft(_videoNodeId, _presetParameterId, ApplicationExpressionKind.BaseValue),
                    right: new ApplicationExpressionDraft(_videoNodeId, _presetParameterId, ApplicationExpressionKind.LogicalControl, controlId)));
                if (expression.Status == ApplicationCommandStatus.Rejected) { Fail("Logical control expression rejected: " + DiagnosticText(expression.Diagnostic)); return false; }
                var presetId = PresetId.New().Value;
                _presetId = presetId;
                _presetExpectedValue = HarnessInteractionContract.PerformancePresetSpeedValue.ToString();
                var preset = _composition.Application.AddPreset(new ApplicationPresetRequest(presetId, "Harness Preset", "Performance", 0,
                    new[] { new ApplicationPresetEntryRequest(_videoNodeId, _presetParameterId, HarnessInteractionContract.PerformancePresetSpeedValue) }));
                if (preset.Status == ApplicationCommandStatus.Rejected) { Fail("Preset creation rejected: " + DiagnosticText(preset.Diagnostic)); return false; }
                _operationSequence.Add("AddPreset:" + presetId);
                var triggerId = LogicalControlId.New().Value;
                _presetTriggerId = triggerId;
                var trigger = _composition.Application.AddLogicalControl(new ApplicationLogicalControlRequest(triggerId, "Harness Preset Trigger", ApplicationLogicalControlKind.PresetTrigger,
                    presetId: presetId, mappings: new[] { new ApplicationControlMappingRequest("harness.preset", "<Harness>/preset") }));
                if (trigger.Status == ApplicationCommandStatus.Rejected) { Fail("Preset trigger creation rejected: " + DiagnosticText(trigger.Diagnostic)); return false; }
                _operationSequence.Add("AddPresetTrigger:" + triggerId);
                _operationSequence.Add("ConfigureLogicalControl:harness.tick");
                _controlsConfigured = true;
            }
            if (!_previewsConfigured)
            {
                var previewIds = graph.Nodes.Where(x => x.TypeId == GraphConstants.PreviewTypeId).OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id).ToArray();
                if (previewIds.Length != 2) return false;
                if (_composition.Application.OpenPreview(previewIds[0]).Status == ApplicationCommandStatus.Rejected || _composition.Application.OpenPreview(previewIds[1]).Status == ApplicationCommandStatus.Rejected)
                { Fail("Preview open was rejected."); return false; }
                if (_composition.Application.RequestPreviewDemand(new ApplicationOutputDemandRequest(previewIds[0], "image", 640, 360)).Status == ApplicationCommandStatus.Rejected ||
                    _composition.Application.RequestPreviewDemand(new ApplicationOutputDemandRequest(previewIds[1], "image", 640, 360)).Status == ApplicationCommandStatus.Rejected)
                { Fail("Preview demand was rejected."); return false; }
                _previewsConfigured = true;
                _operationSequence.Add("OpenPreview:1");
                _operationSequence.Add("OpenPreview:2");
                _operationSequence.Add("RequestPreviewDemand:640x360@30");
            }
            // Import is driven at the application boundary after the graph is
            // committed, so a VideoPlayer cannot silently run without the
            // corpus asset.
            var task = model.Task?.Model;
            if (HarnessMediaImportContract.ShouldConfirmProbe(task, _mediaProbeConfirmationRequested))
            {
                var confirmation = _composition.Application.ConfirmMediaImport(true);
                if (confirmation.Status == ApplicationCommandStatus.Rejected)
                {
                    Fail("Media probe confirmation was rejected: " + DiagnosticText(confirmation.Diagnostic));
                    return false;
                }
                _mediaProbeConfirmationRequested = true;
            }
            if (model.Media?.Model == null || model.Media.Model.Count == 0)
            {
                var request = new ApplicationMediaImportRequest(Path.Combine(_corpus.Root, _corpus.Entry.file), _corpus.Entry.name ?? _corpus.Entry.file,
                    _codec == HarnessCodec.H264 ? "Video" : "Video", "Linear", "Opaque");
                if (_mediaImportStarted) return false;
                _mediaImportStarted = true;
                var accepted = _composition.Application.ImportMedia(request);
                if (accepted.Status == ApplicationCommandStatus.Rejected) { Fail("Media import rejected: " + DiagnosticText(accepted.Diagnostic)); return false; }
                _operationSequence.Add("ImportMedia:" + _corpus.Entry.file);
                return false;
            }
            if (HarnessMediaImportContract.IsFailed(task))
            {
                Fail("Media import failed: " + (task.Diagnostic == null ? task.Stage : DiagnosticText(task.Diagnostic)));
                return false;
            }
            if (task != null && !HarnessMediaImportContract.IsCompleted(task)) return false;
            var media = model.Media.Model.FirstOrDefault();
            if (media == null || media.IsBroken) { Fail("Imported performance media is not Ready."); return false; }
            if (!_mediaBindingQueued)
            {
                var parameters = model.Parameters?.Model;
                if (!HarnessVideoTransportContract.HasRequiredParameters(parameters, _videoNodeId))
                {
                    Fail("Video transport parameters are missing from the public Application catalog.");
                    return false;
                }
                var edits = new[]
                {
                    new ApplicationParameterEditRequest(_videoNodeId, VideoPlayerContract.MediaAssetParameterId,
                        ParameterValue.FromMediaAsset(new MediaAssetId(media.Id))),
                    new ApplicationParameterEditRequest(_videoNodeId, VideoPlayerContract.PlayingParameterId,
                        ParameterValue.FromBool(true)),
                    new ApplicationParameterEditRequest(_videoNodeId, VideoPlayerContract.LoopParameterId,
                        ParameterValue.FromBool(true))
                };
                var commandIds = new List<Guid>(edits.Length);
                foreach (var request in edits)
                {
                    // All three public commands are queued before the next
                    // production frame. FrameCoordinator commits them as one
                    // parameter event batch, so the VideoPlayer cannot be
                    // observed with an asset but still stopped.
                    var edit = _composition.Application.EditParameter(request);
                    if (edit.Status == ApplicationCommandStatus.Rejected)
                    {
                        Fail("Video transport binding rejected for " + request.ParameterId + ": " + DiagnosticText(edit.Diagnostic));
                        return false;
                    }
                    commandIds.Add(edit.CommandRequestId);
                }
                _videoBindingCommandIds.Clear();
                _videoBindingCommandIds.AddRange(commandIds);
                _mediaBindingQueued = true;
                _operationSequence.Add("BindVideoTransport:media_asset+playing+loop");
                return false;
            }
            if (TryGetVideoBindingFailure(model, out var bindingFailure)) { Fail(bindingFailure); return false; }
            if (!HarnessVideoTransportContract.IsApplied(model.Parameters?.Model, _videoNodeId, media.Id)) return false;
            // ScenarioReady does not proceed until every queued command is
            // terminal and the public read model exposes both BaseValue and
            // EffectiveValue for all three transport parameters.
            return graph.Connections.Count >= 9 && model.Output.Model.Program.Width == 1920;
        }

        private bool TryGetVideoBindingFailure(ApplicationReadModel model, out string failure)
        {
            failure = string.Empty;
            if (_videoBindingCommandIds.Count == 0) return false;
            var commands = model?.Commands ?? Array.Empty<PendingCommandReadModel>();
            foreach (var commandId in _videoBindingCommandIds)
            {
                var command = commands.FirstOrDefault(x => x.CommandRequestId == commandId);
                if (command == null || !command.IsTerminal) continue;
                if (command.Status == ApplicationCommandStatus.Applied) continue;
                failure = "Video transport binding command " + commandId.ToString("D") + " failed: " + DiagnosticText(command.Diagnostic);
                return true;
            }
            return false;
        }

        private HarnessWarmupEvaluation ObserveWarmupReadiness()
        {
            var model = _composition?.Application?.ReadModel;
            var output = model?.Output?.Model;
            var graph = model?.Graph?.Model;
            var ownership = _composition?.CaptureOwnershipSnapshot();
            if (model == null || output == null || graph == null || ownership == null)
                return HarnessWarmupEvaluation.Pending("public production read models are not available");

            var currentDiagnostics = model.DiagnosticModel?.Model?.Current ?? Array.Empty<ApplicationDiagnosticReadModel>();
            var diagnosticFailure = currentDiagnostics.FirstOrDefault(x =>
                string.Equals(x.Severity, "Fault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Severity, "Fatal", StringComparison.OrdinalIgnoreCase));
            if (diagnosticFailure != null)
                return HarnessWarmupEvaluation.Failure(diagnosticFailure.Code + ": " + diagnosticFailure.Message);

            var terminalNode = graph.Nodes.FirstOrDefault(x =>
                HarnessWarmupEvaluator.IsTerminalNodeFailure(x.TypeId, x.Status));
            if (terminalNode != null)
                return HarnessWarmupEvaluation.Failure("Node " + terminalNode.TypeId + " is " + terminalNode.Status + ".");

            var task = model.Task?.Model;
            if (task != null && string.Equals(task.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                return HarnessWarmupEvaluation.Failure("Media task failed: " + (task.Diagnostic == null ? task.Stage : task.Diagnostic.Message));

            var media = model.Media?.Model ?? Array.Empty<ApplicationMediaReadModel>();
            if (media.Any(x => x.IsBroken))
                return HarnessWarmupEvaluation.Failure("Imported performance media is broken.");

            var requiredTypes = new[]
            {
                "shitdesigner.scene.3d", "shitdesigner.scene.2d", "shitdesigner.shader.effect",
                "shitdesigner.shader.blend2", "shitdesigner.video.player", "system.feedback", "system.program_output"
            };
            var graphReady = requiredTypes.All(type => graph.Nodes.Any(x => string.Equals(x.TypeId, type, StringComparison.Ordinal) &&
                x.Enabled && !x.IsPending && !string.Equals(x.Status, "Preparing", StringComparison.OrdinalIgnoreCase)));
            var shaderTypes = new[] { "shitdesigner.shader.effect", "shitdesigner.shader.blend2" };
            var catalog = model.NodeCatalog?.Model ?? Array.Empty<ApplicationNodeCatalogEntry>();
            var shaderCompilationReady = graphReady && shaderTypes.All(type => catalog.Any(x => string.Equals(x.TypeId, type, StringComparison.Ordinal) && x.RuntimeAvailable));

            var videoPrepared = task != null && string.Equals(task.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                media.Count > 0 && media.All(x => !x.IsBroken && string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase));
            // The public graph projection reports Ready only after the
            // VideoPlayer node has produced an available image output.  The
            // media task alone proves Prepare/file integrity, while this
            // state plus the presented Program frame proves the first decoded
            // frame crossed the normal production output boundary.  Do not
            // infer readiness from private backend or VideoPlayer fields.
            var videoNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.TypeId, "shitdesigner.video.player", StringComparison.Ordinal));
            var videoFrameReadyInGraph = videoNode != null && videoNode.Enabled && !videoNode.IsPending &&
                string.Equals(videoNode.Status, "Ready", StringComparison.OrdinalIgnoreCase);
            var program = output.Program;
            var videoFrameReady = videoPrepared && videoFrameReadyInGraph && output.FrameNumber > 0 && program != null &&
                string.Equals(program.State, "Available", StringComparison.OrdinalIgnoreCase) && program.Width == 1920 && program.Height == 1080;
            var initialTexturesReady = ownership.TexturePool != null && ownership.Program != null &&
                ownership.Program.Width == 1920 && ownership.Program.Height == 1080 &&
                HarnessMetricEvaluator.IsPermittedProgramFormat(ownership.Program.GraphicsFormat) &&
                ownership.Previews.Count == 2 && ownership.Previews.All(x =>
                    HarnessPreviewQualityContract.IsValidDescriptor(x.Width, x.Height, x.TargetFramesPerSecond) &&
                    !string.IsNullOrWhiteSpace(x.GraphicsFormat)) && ownership.ActiveOutputLeaseCount >= 3;
            return HarnessWarmupEvaluator.Evaluate(new HarnessWarmupObservation(shaderCompilationReady, videoPrepared,
                videoFrameReady, initialTexturesReady));
        }

        private bool SaveCanonicalScenario()
        {
            var application = _composition?.Application;
            if (application == null) { Fail("Canonical scenario could not be saved because the public Application API was unavailable."); return false; }
            var result = application.SaveProject();
            if (result.Status == ApplicationCommandStatus.Rejected)
            {
                Fail("Canonical scenario SaveProject was rejected: " + DiagnosticText(result.Diagnostic));
                return false;
            }
            var projectPath = Path.Combine(_projectRoot ?? string.Empty, PersistenceConstants.MainFileName);
            var project = application.ReadModel?.Project?.Model;
            var task = application.ReadModel?.Task?.Model;
            if (!File.Exists(projectPath) || project == null || project.IsDirty || task == null ||
                !string.Equals(task.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                Fail("Canonical scenario SaveProject did not produce a clean project.json.");
                return false;
            }
            _scenarioSaved = true;
            _operationSequence.Add("SaveProject:canonical-scenario");
            return true;
        }

        private IEnumerator WaitForCondition(Func<bool> condition, double timeoutSeconds, string conditionName = null)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + Math.Max(0.1d, timeoutSeconds);
            while (!condition())
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    if (string.IsNullOrEmpty(_failure)) Fail(string.IsNullOrWhiteSpace(conditionName) ? "Harness condition timed out." : "Harness condition timed out: " + conditionName + ".");
                    yield break;
                }
                yield return null;
            }
        }

        private void CollectCompletedFrame()
        {
            if (_composition == null) return;
            _lastFrameTimingDiagnostic = _composition.Loop?.FrameTimingDiagnostic ?? ProductionFrameTimingDiagnostic.Unavailable;
            if (HarnessMeasurementBoundaryContract.AllowsMeasurementEvidence(_collecting) && !AccumulateGcAllocations()) return;
            var model = _composition.Application.ReadModel;
            var output = model?.Output?.Model;
            if (output == null || !HarnessMeasurementBoundaryContract.IsNewProgramPresentation(_lastCollectedPresentationFrame, output.FrameNumber)) return;
            _lastCollectedPresentationFrame = output.FrameNumber;
            if (!_composition.TryCapturePerformanceHealth(_performancePreviewBuffer, out var previewCount, out var health))
            {
                _failure = "Performance health preview buffer is insufficient: required " + health.RequiredPreviewCount + ", capacity " + _performancePreviewBuffer.Length + ".";
                return;
            }
            var program = output?.Program;
            var activeProgram = health.Program;
            var healthy = program != null && activeProgram.IsBound && activeProgram.Width == 1920 && activeProgram.Height == 1080 &&
                          HarnessMetricEvaluator.IsPermittedProgramFormat(activeProgram.GraphicsFormat) && activeProgram.TargetFramesPerSecond == 60 &&
                          (string.Equals(program.State, "Available", StringComparison.OrdinalIgnoreCase) || string.Equals(program.State, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase)) &&
                          health.RequiredPreviewCount == previewCount;
            var current = model?.DiagnosticModel?.Model?.Current ?? Array.Empty<ApplicationDiagnosticReadModel>();
            var faulted = current.Any(x => string.Equals(x.Severity, "Fault", StringComparison.OrdinalIgnoreCase) || x.Code.IndexOf("fault", StringComparison.OrdinalIgnoreCase) >= 0);
            var fatal = current.Any(x => string.Equals(x.Severity, "Fatal", StringComparison.OrdinalIgnoreCase) || x.Code.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0);
            var outputPreviews = output?.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>();
            var previews = new HarnessPreviewMetric[previewCount];
            for (var index = 0; index < previewCount; index++) previews[index] = ToPreviewMetric(_performancePreviewBuffer[index], outputPreviews);

            // Keep presentation health/descriptors under their own Application
            // frame until Unity returns that frame's delayed timing result.
            if (HarnessMeasurementBoundaryContract.AllowsMeasurementEvidence(_collecting) && output != null)
                _timingCompletions.RecordPresentation(output.FrameNumber,
                    CreateTimingMetricSnapshot(health, previews, healthy, faulted, fatal, program));

            var timingFrame = output?.PerformanceFrameNumber ?? 0UL;
            if (_timingCompletions.TryTakeCompletion(timingFrame, out var completed))
            {
                completed.cpuMilliseconds = output.CpuFrameTimeMilliseconds;
                completed.gpuMilliseconds = output.GpuFrameTimeMilliseconds;
                completed.programPresented = completed.programHealthy;
                _metrics.Add(completed);
            }

            if (_timingDrainActive)
            {
                var presentationFrame = output?.FrameNumber ?? 0UL;
                if (presentationFrame > _lastDrainPresentationFrame)
                {
                    _lastDrainPresentationFrame = presentationFrame;
                    _timingDrainPresentedFrames++;
                }
                if (_timingDrainPresentedFrames >= FrameTimingDrainPresentationCount) CompleteTimingDrain();
                return;
            }

            if (_collecting && string.IsNullOrEmpty(_failure))
                TryStartDuePresetVerification(Time.realtimeSinceStartupAsDouble);
        }

        private HarnessMetricSample CreateTimingMetricSnapshot(ProductionPerformanceHealthSnapshot health,
            HarnessPreviewMetric[] previews, bool healthy, bool faulted, bool fatal, ApplicationOutputSurfaceReadModel program)
        {
            return new HarnessMetricSample
            {
                cpuMilliseconds = double.NaN,
                gpuMilliseconds = double.NaN,
                sampleSeconds = Math.Max(0d, Time.realtimeSinceStartupAsDouble - _measurementStart),
                programFrameNumber = health.Program.FrameNumber,
                programWidth = health.Program.Width,
                programHeight = health.Program.Height,
                programFormat = health.Program.GraphicsFormat,
                programTargetFramesPerSecond = health.Program.TargetFramesPerSecond,
                previews = previews,
                poolBudgetBytes = health.PoolBudgetBytes,
                poolLeasedBytes = health.PoolLeasedBytes,
                poolFreeBytes = health.PoolFreeBytes,
                poolHighWaterBytes = health.PoolHighWaterBytes,
                poolBudgetWarning = health.PoolBudgetWarning,
                programPresented = false,
                programHealthy = healthy,
                faulted = faulted,
                fatal = fatal,
                holdingLastFrame = program != null && program.IsHoldingLastFrame
            };
        }

        private void BeginTimingDrain()
        {
            if (_timingDrainActive || !_collecting) return;
            _interactionScheduler?.Close();
            _collecting = false;
            _timingDrainActive = true;
            _timingDrainPresentedFrames = 0;
            _lastDrainPresentationFrame = _composition?.Application?.ReadModel?.Output?.Model?.FrameNumber ?? _measurementStartFrame;
        }

        private void CompleteTimingDrain()
        {
            if (!_timingDrainActive) return;
            _timingDrainActive = false;
            var unresolved = _timingCompletions.DrainUncompleted();
            foreach (var missing in unresolved)
            {
                _metrics.Add(HarnessTimingCompletionTracker.MarkUnresolvedTimingUnavailable(missing));
            }
            _timingDrainCompleted = true;
            TryFinishAfterMeasurementBoundary();
        }

        private bool InitialFrameTimingReady()
        {
            _lastFrameTimingDiagnostic = _composition?.Loop?.FrameTimingDiagnostic ?? ProductionFrameTimingDiagnostic.Unavailable;
            var output = _composition?.Application?.ReadModel?.Output?.Model;
            return output != null && HarnessFrameTimingReadinessContract.IsReady(_frameTimingGateStartPerformanceFrame, output.FrameNumber, output.PerformanceFrameNumber,
                output.MeasuredFramesPerSecond, output.CpuFrameTimeMilliseconds, output.GpuFrameTimeMilliseconds);
        }

        private bool TryStartDuePresetVerification(double hostTime)
        {
            if (!HarnessMeasurementBoundaryContract.ShouldStartPresetTrigger(_collecting, _presetVerificationActive,
                _measurementStart, _measureSeconds, hostTime, _presetTicks)) return false;

            // Set the guard before StartCoroutine. Unity normally executes an
            // IEnumerator immediately until its first yield, but the guard
            // must remain correct even if that scheduling detail changes.
            _presetVerificationActive = true;
            StartCoroutine(TriggerAndVerifyPreset());
            return true;
        }

        private void TryFinishAfterMeasurementBoundary()
        {
            if (HarnessMeasurementBoundaryContract.CanFinalize(_timingDrainCompleted, _presetVerificationActive)) _finished = true;
        }

        private IEnumerator TriggerAndVerifyPreset()
        {
            var application = _composition?.Application;
            var shiftedValue = ParameterValue.FromFloat(0.75f).ToString();
            var shift = application == null ? default(ApplicationCommandResult) : application.EditParameter(
                new ApplicationParameterEditRequest(_videoNodeId, _presetParameterId, ParameterValue.FromFloat(0.75f)));
            if (application == null || shift.Status == ApplicationCommandStatus.Rejected)
            {
                Fail("Preset verification parameter shift was rejected: " + (application == null ? "Application unavailable." : DiagnosticText(shift.Diagnostic)));
                _presetVerificationActive = false;
                yield break;
            }
            var deadline = Time.realtimeSinceStartupAsDouble + PresetVerificationTimeoutSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var parameter = FindPresetParameter(application);
                if (parameter != null && string.Equals(parameter.BaseValue, shiftedValue, StringComparison.Ordinal)) break;
                yield return null;
            }
            if (FindPresetParameter(application) == null || !string.Equals(FindPresetParameter(application).BaseValue, shiftedValue, StringComparison.Ordinal))
            {
                Fail("Preset verification parameter shift was not visible in the public read model.");
                _presetVerificationActive = false;
                yield break;
            }

            var pressed = application.HandleKeyboard(PhysicalKey.From("harness.preset", "<Harness>/preset"), true);
            if (pressed.Status != ApplicationCommandStatus.Accepted)
            {
                Fail("PresetTrigger press was not accepted: " + pressed.Status + ": " + DiagnosticText(pressed.Diagnostic));
                _presetVerificationActive = false;
                yield break;
            }
            yield return null;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var parameter = FindPresetParameter(application);
                var trigger = application.ReadModel?.Project?.Model?.LogicalControls?.FirstOrDefault(x => x != null && x.Id == _presetTriggerId);
                var mappingPresent = trigger?.Mappings != null && trigger.Mappings.Any(x => x != null && x.PhysicalId == "harness.preset");
                var observationFailure = HarnessPresetApplicationContract.ValidateObservation(parameter?.BaseValue ?? string.Empty,
                    parameter?.EffectiveValue ?? string.Empty, _presetExpectedValue ?? string.Empty, trigger?.PresetId ?? string.Empty,
                    _presetId ?? string.Empty, trigger?.PresetIsBroken ?? true, mappingPresent);
                if (string.IsNullOrEmpty(observationFailure))
                {
                    var released = application.HandleKeyboard(PhysicalKey.From("harness.preset", "<Harness>/preset"), false);
                    if (released.Status == ApplicationCommandStatus.Rejected)
                    {
                        Fail("PresetTrigger release was rejected: " + DiagnosticText(released.Diagnostic));
                        _presetVerificationActive = false;
                        yield break;
                    }
                    _presetTicks++;
                    _presetVerificationActive = false;
                    if (Time.realtimeSinceStartupAsDouble >= _measureDeadline)
                    {
                        BeginTimingDrain();
                        TryFinishAfterMeasurementBoundary();
                    }
                    yield break;
                }
                yield return null;
            }
            Fail("PresetTrigger was accepted but public ReadModel did not confirm preset application.");
            _presetVerificationActive = false;
        }

        private ApplicationParameterReadModel FindPresetParameter(ProjectApplication application)
        {
            return (application?.ReadModel?.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>())
                .FirstOrDefault(x => x != null && x.NodeId == _videoNodeId && x.ParameterId == _presetParameterId);
        }

        private bool AccumulateGcAllocations()
        {
            if (!_gcRecorderStarted || !_gcAllocationRecorder.Valid ||
                _gcAllocationRecorder.UnitType != ProfilerMarkerDataUnit.Bytes)
            {
                Fail("Unity GC allocation byte measurement became unavailable during the measured interval: " + DescribeGcAllocationRecorder());
                return false;
            }
            _gcAllocatedDuringMeasurement = HarnessGcAllocationContract.AccumulateBytes(_gcAllocatedDuringMeasurement,
                _gcAllocationRecorder.LastValue);
            return true;
        }

        private string DescribeGcAllocationRecorder()
        {
            var valid = _gcAllocationRecorder.Valid;
            var unit = _gcAllocationRecorder.UnitType;
            var startFailure = string.IsNullOrWhiteSpace(_gcRecorderStartFailure) ? string.Empty : "; startFailure=" + _gcRecorderStartFailure;
            return "counter='" + HarnessGcAllocationContract.CounterName + "', category='" + HarnessGcAllocationContract.CounterCategory.Name +
                   "', valid=" + valid + ", unit=" + unit + startFailure + "; availableMemoryCounters=" + DescribeAvailableMemoryCounters();
        }

        private static string DescribeAvailableMemoryCounters()
        {
            try
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var counters = new List<string>();
                foreach (var handle in handles)
                {
                    if (!handle.Valid) continue;
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    if (!string.Equals(description.Category.Name, ProfilerCategory.Memory.Name, StringComparison.Ordinal)) continue;
                    counters.Add(description.Name + "(" + description.UnitType + ")");
                    if (counters.Count == 8) break;
                }
                return counters.Count == 0 ? "none" : string.Join(",", counters);
            }
            catch (Exception exception) { return "enumeration-failed:" + exception.GetType().Name; }
        }

        private void FinishRun()
        {
            if (_artifactWritten) return;
            // Capture the public diagnostics while the Application runtime is
            // still alive.  BuildPerformanceArtifact tears the composition
            // down before its final ownership/evaluator pass; exporting only
            // after that point would lose the runtime diagnostic history.
            if (_diagnosticsExport == null) _diagnosticsExport = ExportDiagnosticsSafely();
            HarnessArtifact artifact = null;
            var write = default(ArtifactWriteResult);
            try
            {
                artifact = BuildPerformanceArtifact();
                AttachDiagnosticsExport(artifact);
                write = TryWriteArtifact(artifact);
            }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Harness finalization failed: " + exception);
                DisposeGcRecorderSafely();
                DisposeCompositionOnce();
                try
                {
                    artifact = CreateFallbackArtifact();
                    AttachDiagnosticsExport(artifact);
                    write = TryWriteArtifact(artifact);
                }
                catch (Exception fallbackException)
                {
                    PreserveFinalizationFailure("Harness fallback artifact failed: " + fallbackException);
                    write = new ArtifactWriteResult(false, null, null, fallbackException.ToString());
                }
            }
            finally
            {
                // A failure while capturing the after-snapshot must not skip
                // disposal. Dispose is idempotent in the production root.
                DisposeCompositionOnce();
                _artifactWritten = true;
                if (artifact == null)
                {
                    try
                    {
                        artifact = CreateFallbackArtifact();
                        AttachDiagnosticsExport(artifact);
                        write = TryWriteArtifact(artifact);
                    }
                    catch (Exception exception)
                    {
                        PreserveFinalizationFailure("Harness fallback artifact failed: " + exception);
                        write = new ArtifactWriteResult(false, null, null, exception.ToString());
                    }
                }
                // Performance requires a presented Player for FrameTiming, so
                // this cannot be gated on batch mode. The only host that must
                // remain alive is the Editor when its PlayMode tests exercise
                // the same finalization path.
                var shouldQuit = HarnessFinalizationContract.ShouldQuitPlayer(_options != null && _options.ShouldQuit, UnityApplication.isEditor);
                var decision = HarnessFinalizationContract.Decide(_failure, artifact?.status, write.Success, null,
                    shouldQuit ? (Action<int>)(code => UnityApplication.Quit(code)) : null);
                ExitCode = decision.exitCode;
            }
        }

        private void AttachDiagnosticsExport(HarnessArtifact artifact)
        {
            if (artifact == null) return;
            artifact.diagnosticsExport = HarnessDiagnosticsExportContract.AttachCandidate(artifact.status, _diagnosticsExport);
        }

        private HarnessArtifact BuildPerformanceArtifact()
        {
            if (!_finished && string.IsNullOrEmpty(_failure)) _failure = "Harness finished without a measured result.";
            var application = _composition?.Application;
            var model = application?.ReadModel;
            var composition = _composition;
            var before = SafeCaptureOwnership(composition, "before teardown");
            _lastOwnershipSnapshot = before ?? _lastOwnershipSnapshot;
            var projectRoot = SafeProjectRoot(application);
            var mediaProbes = SafeCaptureMediaProbes();
            var program = model?.Output?.Model?.Program;
            var activeProgram = before?.Program ?? _lastOwnershipSnapshot?.Program;
            var diagnostics = SafeReadDiagnostics(model);
            var productionCompositionUsed = composition != null;
            var productionCatalogUsed = composition?.RuntimeFactory?.CurrentComposition != null;
            DisposeCompositionOnce();
            var after = SafeCaptureOwnership(composition, "after teardown");
            var teardownFailure = HarnessOwnershipContract.ValidateTeardown(after);
            if (string.IsNullOrEmpty(_failure) && !string.IsNullOrEmpty(teardownFailure)) _failure = teardownFailure;
            try { _metrics?.CompleteIntervals(Math.Max(_measureSeconds, _metrics?.Samples.LastOrDefault()?.sampleSeconds ?? 0d)); }
            catch (Exception exception) { PreserveFinalizationFailure("Harness metric finalization failed: " + exception); }
            DisposeGcRecorderSafely();

            var endLeases = after?.ActiveOutputLeaseCount ?? 0;
            var endPoolEntries = after?.TexturePool?.Entries.Count ?? 0;
            var poolCurrentBytes = (before?.TexturePool?.LeasedBytes ?? 0) + (before?.TexturePool?.FreeBytes ?? 0);
            var evaluation = HarnessMetricEvaluator.Evaluate(_metrics, activeProgram?.Width ?? 0, activeProgram?.Height ?? 0,
                activeProgram?.GraphicsFormat ?? string.Empty, poolCurrentBytes, before?.TexturePool?.BudgetBytes ?? 0,
                endLeases + endPoolEntries, after?.SceneCount ?? 0, after?.LayerCount ?? 0, after?.BackendCount ?? 0, after?.NativeContextCount ?? 0,
                _controlEvents, _presetTicks, _measureSeconds);
            if (string.IsNullOrEmpty(_failure) && !evaluation.Passed) _failure = evaluation.Failure;
            var outputPreviews = model?.Output?.Model?.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>();
            var previewDescriptors = (before?.Previews ?? Array.Empty<ProductionSurfaceOwnershipSnapshot>())
                .Select(x => ToPreviewMetric(x, outputPreviews)).ToArray();
            var previewQualitySamples = (_metrics?.Samples ?? Array.Empty<HarnessMetricSample>()).Select(x => new HarnessPreviewQualitySample
            {
                sampleSeconds = x.sampleSeconds, programFrameNumber = x.programFrameNumber, previews = x.previews
            }).ToArray();
            var ownershipArtifact = SafeOwnershipArtifact(before);
            return new HarnessArtifact
            {
                runId = _runId,
                status = string.IsNullOrEmpty(_failure) && evaluation.Passed ? HarnessRunStatus.Passed.ToString() : (IsEnvironmentFailure(_failure) ? HarnessRunStatus.EnvironmentFailed.ToString() : HarnessRunStatus.Failed.ToString()),
                failure = _failure ?? string.Empty,
                scenario = "3D Generator + 2D Generator + Shader Effect + VideoPlayer + 2-input Blend + Feedback + ProgramOutput",
                codec = _codec.ToString(), corpusVersion = _corpus?.Version ?? string.Empty, corpusFile = _corpus?.Entry?.file ?? string.Empty,
                platform = UnityApplication.platform.ToString(), operatingSystem = SystemInfo.operatingSystem, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName, graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion, unityVersion = UnityApplication.unityVersion,
                packageVersion = UnityApplication.version, buildId = UnityApplication.buildGUID, developmentBuild = Debug.isDebugBuild,
                buildOptions = Debug.isDebugBuild ? "Development" : "None", projectRoot = projectRoot,
                projectRevision = application?.ReadProject()?.DocumentRevision.ToString() ?? string.Empty,
                seed = _runId, fixtureMode = _fixtureMode, warmupSeconds = _warmupSeconds, measureSeconds = _measureSeconds,
                canonicalScenarioSaved = _scenarioSaved,
                productionCompositionUsed = productionCompositionUsed, productionCatalogUsed = productionCatalogUsed,
                renderPipeline = GraphicsSettings.currentRenderPipeline == null ? string.Empty : GraphicsSettings.currentRenderPipeline.GetType().FullName,
                timing = new HarnessTimingArtifact { updateSamples = _metrics?.Samples.Count ?? 0, measuredFrames = _metrics?.PresentedFrames ?? 0, presentedFrames = _metrics?.PresentedFrames ?? 0,
                    timingAvailableFrames = _metrics?.TimingAvailableFrames ?? 0, timingUnavailableFrames = _metrics?.TimingUnavailableFrames ?? 0, goodFrameRatio = _metrics?.GoodFrameRatio ?? 0d,
                    averageCpuMilliseconds = _metrics?.AverageCpuMilliseconds ?? double.NaN, averageGpuMilliseconds = _metrics?.AverageGpuMilliseconds ?? double.NaN,
                    maxCpuMilliseconds = _metrics?.MaxCpuMilliseconds ?? double.NaN, maxGpuMilliseconds = _metrics?.MaxGpuMilliseconds ?? double.NaN, maxConsecutiveProgramMissing = _metrics?.MaxConsecutiveMissing ?? 0,
                    minimumProgramCadenceFps = _metrics?.MinimumProgramCadenceFps ?? double.NaN,
                    gcAllocatedBytes = _gcAllocatedDuringMeasurement, gcCollectionCount0 = Math.Max(0, GC.CollectionCount(0) - _gcCollections0),
                    gcCollectionCount1 = Math.Max(0, GC.CollectionCount(1) - _gcCollections1), gcCollectionCount2 = Math.Max(0, GC.CollectionCount(2) - _gcCollections2),
                    frameTimingGateStartPerformanceFrame = _frameTimingGateStartPerformanceFrame,
                    frameTimingGateReadyPerformanceFrame = _frameTimingGateReadyPerformanceFrame,
                     frameTimingGateWaitSeconds = double.IsNaN(_frameTimingGateWaitSeconds) && !double.IsNaN(_frameTimingGateStartedAt)
                         ? Math.Max(0d, Time.realtimeSinceStartupAsDouble - _frameTimingGateStartedAt) : _frameTimingGateWaitSeconds,
                     frameTimingSource = ToFrameTimingArtifact(_lastFrameTimingDiagnostic),
                     previewQualitySamples = HarnessPreviewQualityContract.AppendTerminalSample(previewQualitySamples, _measureSeconds) },
                interactions = new HarnessInteractionArtifact
                {
                    logicalControlUpdatesPerSecond = HarnessInteractionContract.LogicalControlUpdatesPerSecond,
                    presetTriggerIntervalSeconds = HarnessInteractionContract.PresetTriggerIntervalSeconds,
                    measurementSeconds = _measureSeconds,
                    logicalControlUpdates = _controlEvents,
                    expectedLogicalControlUpdates = HarnessInteractionContract.ExpectedLogicalControlUpdates(_measureSeconds),
                    presetTriggerFires = _presetTicks,
                    expectedPresetTriggerFires = HarnessInteractionContract.ExpectedPresetTriggerFires(_measureSeconds)
                },
                operationSequence = _operationSequence.Concat(new[] { "Warmup:" + _warmupSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Measure:" + _measureSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
                output = new HarnessOutputArtifact { programWidth = activeProgram?.Width ?? 0, programHeight = activeProgram?.Height ?? 0,
                    programFormat = activeProgram?.GraphicsFormat ?? string.Empty, programTargetFps = activeProgram?.TargetFramesPerSecond ?? 0,
                    programState = program?.State ?? string.Empty, previewCount = previewDescriptors.Length,
                    previewWidth = previewDescriptors.Length == 0 ? 0 : previewDescriptors[0].width,
                    previewHeight = previewDescriptors.Length == 0 ? 0 : previewDescriptors[0].height,
                    previewTargetFps = previewDescriptors.Length == 0 ? 0 : previewDescriptors[0].targetFramesPerSecond,
                    previews = previewDescriptors,
                    previewQualities = (model?.Output?.Model?.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>()).Select(x => x.Quality).ToArray() },
                resources = new HarnessResourceArtifact { poolBudgetBytes = before?.TexturePool?.BudgetBytes ?? 0, poolCurrentBytes = poolCurrentBytes,
                    poolLeasedBytes = before?.TexturePool?.LeasedBytes ?? 0, poolFreeBytes = before?.TexturePool?.FreeBytes ?? 0, poolHighWaterBytes = before?.TexturePool?.HighWaterBytes ?? 0,
                    sceneCount = before?.SceneCount ?? 0, layerCount = before?.LayerCount ?? 0, backendCount = before?.BackendCount ?? 0, nativeContextCount = before?.NativeContextCount ?? 0,
                    activeOutputLeases = before?.ActiveOutputLeaseCount ?? 0, poolEntryCount = before?.TexturePool?.Entries.Count ?? 0,
                    endLeases = endLeases + endPoolEntries, endPoolEntryCount = endPoolEntries, endActiveOutputLeases = endLeases,
                    endSceneCount = after?.SceneCount ?? 0, endLayerCount = after?.LayerCount ?? 0, endBackendCount = after?.BackendCount ?? 0, endNativeContextCount = after?.NativeContextCount ?? 0 },
                ownership = ownershipArtifact,
                diagnostics = SafeDiagnosticsArtifact(diagnostics),
                diagnosticsExport = _diagnosticsExport ?? HarnessDiagnosticsExportArtifact.NotAttempted("Diagnostics export was not attempted."),
                failureCapture = HarnessFailureCaptureArtifact.PublicProgramReadbackUnavailable(),
                nativePluginProbe = mediaProbes.nativePlugin,
                codecProbe = mediaProbes.codec
            };
        }

        private HarnessArtifact CreateFallbackArtifact()
        {
            var projectRoot = SafeProjectRoot(_composition?.Application);
            var mediaProbes = SafeCaptureMediaProbes();
            var failure = string.IsNullOrEmpty(_failure) ? "Harness finalization failed." : _failure;
            return new HarnessArtifact
            {
                runId = _runId,
                status = IsEnvironmentFailure(failure) ? HarnessRunStatus.EnvironmentFailed.ToString() : HarnessRunStatus.Failed.ToString(),
                failure = failure,
                scenario = "3D Generator + 2D Generator + Shader Effect + VideoPlayer + 2-input Blend + Feedback + ProgramOutput",
                codec = _codec.ToString(), corpusVersion = _corpus?.Version ?? string.Empty, corpusFile = _corpus?.Entry?.file ?? string.Empty,
                platform = UnityApplication.platform.ToString(), operatingSystem = SystemInfo.operatingSystem,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion, unityVersion = UnityApplication.unityVersion,
                packageVersion = UnityApplication.version, buildId = UnityApplication.buildGUID, developmentBuild = Debug.isDebugBuild,
                buildOptions = Debug.isDebugBuild ? "Development" : "None", projectRoot = projectRoot, seed = _runId,
                fixtureMode = _fixtureMode, warmupSeconds = _warmupSeconds, measureSeconds = _measureSeconds,
                canonicalScenarioSaved = _scenarioSaved,
                productionCompositionUsed = _composition != null,
                timing = new HarnessTimingArtifact
                {
                    timingAvailableFrames = _metrics?.TimingAvailableFrames ?? 0,
                    timingUnavailableFrames = _metrics?.TimingUnavailableFrames ?? 0,
                    frameTimingGateStartPerformanceFrame = _frameTimingGateStartPerformanceFrame,
                    frameTimingGateReadyPerformanceFrame = _frameTimingGateReadyPerformanceFrame,
                    frameTimingGateWaitSeconds = double.IsNaN(_frameTimingGateWaitSeconds) && !double.IsNaN(_frameTimingGateStartedAt)
                        ? Math.Max(0d, Time.realtimeSinceStartupAsDouble - _frameTimingGateStartedAt) : _frameTimingGateWaitSeconds,
                    frameTimingSource = ToFrameTimingArtifact(_lastFrameTimingDiagnostic)
                },
                interactions = new HarnessInteractionArtifact
                {
                    logicalControlUpdatesPerSecond = HarnessInteractionContract.LogicalControlUpdatesPerSecond,
                    presetTriggerIntervalSeconds = HarnessInteractionContract.PresetTriggerIntervalSeconds,
                    measurementSeconds = _measureSeconds,
                    logicalControlUpdates = _controlEvents,
                    expectedLogicalControlUpdates = HarnessInteractionContract.ExpectedLogicalControlUpdates(_measureSeconds),
                    presetTriggerFires = _presetTicks,
                    expectedPresetTriggerFires = HarnessInteractionContract.ExpectedPresetTriggerFires(_measureSeconds)
                },
                operationSequence = _operationSequence.Concat(new[] { "Warmup:" + _warmupSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Measure:" + _measureSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
                ownership = SafeOwnershipArtifact(_lastOwnershipSnapshot),
                diagnostics = SafeDiagnosticsArtifact(null),
                diagnosticsExport = _diagnosticsExport ?? HarnessDiagnosticsExportArtifact.NotAttempted("Diagnostics export was not attempted."),
                failureCapture = HarnessFailureCaptureArtifact.PublicProgramReadbackUnavailable(),
                nativePluginProbe = mediaProbes.nativePlugin,
                codecProbe = mediaProbes.codec
            };
        }

        private ArtifactWriteResult TryWriteArtifact(HarnessArtifact artifact)
        {
            try
            {
                var directory = ResolveArtifactDirectory();
                var write = HarnessArtifactWriter.Write(directory, artifact);
                if (!write.Success)
                {
                    artifact.artifactWriteError = write.Error;
                    Debug.LogError("Standalone Harness artifact write failed: " + write.Error);
                }
                return write;
            }
            catch (Exception exception)
            {
                var error = exception.ToString();
                if (artifact != null) artifact.artifactWriteError = error;
                Debug.LogError("Standalone Harness artifact write failed: " + error);
                return new ArtifactWriteResult(false, null, null, error);
            }
        }

        private string ResolveArtifactDirectory()
        {
            return string.IsNullOrWhiteSpace(_artifactDirectory)
                ? Path.Combine(UnityApplication.persistentDataPath, "ShitDesigner", "HarnessArtifacts")
                : _artifactDirectory;
        }

        private HarnessDiagnosticsExportArtifact ExportDiagnosticsSafely()
        {
            var result = HarnessDiagnosticsExportArtifact.NotAttempted("Public Application diagnostics export was not available.");
            var application = _composition?.Application;
            if (application == null) return result;
            try
            {
                var directory = Path.Combine(ResolveArtifactDirectory(), "diagnostics");
                Directory.CreateDirectory(directory);
                result.attempted = true;
                result.textPath = Path.Combine(directory, _runId + "-diagnostics.txt");
                result.jsonPath = Path.Combine(directory, _runId + "-diagnostics.json");
                var text = application.ExportDiagnostics(result.textPath, false);
                result.textWritten = text.Status != ApplicationCommandStatus.Rejected && File.Exists(result.textPath);
                var json = application.ExportDiagnostics(result.jsonPath, true);
                result.jsonWritten = json.Status != ApplicationCommandStatus.Rejected && File.Exists(result.jsonPath);
                if (!result.textWritten || !result.jsonWritten)
                {
                    result.failure = "Public diagnostics export did not produce both text and JSON files: " +
                        DiagnosticText(!result.textWritten ? text.Diagnostic : json.Diagnostic);
                    Debug.LogError("Standalone Harness diagnostics export failed: " + result.failure);
                }
                else result.failure = string.Empty;
            }
            catch (Exception exception)
            {
                result.failure = exception.ToString();
                Debug.LogError("Standalone Harness diagnostics export failed: " + exception);
            }
            return result;
        }

        private ProductionOwnershipSnapshot SafeCaptureOwnership(ProductionCompositionRoot composition, string phase)
        {
            if (composition == null) return _lastOwnershipSnapshot;
            try { return composition.CaptureOwnershipSnapshot(); }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Ownership snapshot failed " + phase + ": " + exception);
                return _lastOwnershipSnapshot;
            }
        }

        private HarnessOwnershipSnapshotArtifact SafeOwnershipArtifact(ProductionOwnershipSnapshot snapshot)
        {
            try { return HarnessOwnershipSnapshotArtifact.From(snapshot); }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Ownership artifact projection failed: " + exception);
                return new HarnessOwnershipSnapshotArtifact { available = false, previews = Array.Empty<HarnessOwnershipSurfaceArtifact>() };
            }
        }

        private string SafeProjectRoot(ProjectApplication application)
        {
            if (!string.IsNullOrWhiteSpace(_projectRoot)) return _projectRoot;
            try { return application?.ReadProject()?.Model?.ProjectRoot ?? string.Empty; }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Project root snapshot failed: " + exception);
                return string.Empty;
            }
        }

        private ApplicationDiagnosticsReadModel SafeReadDiagnostics(ApplicationReadModel model)
        {
            try { return model?.DiagnosticModel?.Model; }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Diagnostics snapshot failed: " + exception);
                return null;
            }
        }

        private HarnessDiagnosticsArtifact SafeDiagnosticsArtifact(ApplicationDiagnosticsReadModel diagnostics)
        {
            var result = HarnessDiagnosticsArtifact.Empty();
            try
            {
                result.faultedFrames = _metrics?.FaultedFrames ?? 0;
                result.fatalFrames = _metrics?.FatalFrames ?? 0;
                result.holdingLastFrameFrames = _metrics?.HoldingFrames ?? 0;
                result.intervals = (_metrics?.Intervals ?? Array.Empty<HarnessDiagnosticInterval>()).ToArray();
                result.currentCodes = (diagnostics?.Current ?? Array.Empty<ApplicationDiagnosticReadModel>())
                    .Select(x => x == null ? string.Empty : x.Code ?? string.Empty).ToArray();
                result.historyCodes = (diagnostics?.History ?? Array.Empty<ApplicationDiagnosticReadModel>())
                    .Select(x => x == null ? string.Empty : x.Code ?? string.Empty).ToArray();
            }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Diagnostics artifact projection failed: " + exception);
                result.currentCodes = Array.Empty<string>();
                result.historyCodes = Array.Empty<string>();
            }
            return result;
        }

        private HarnessMediaProbeCapture SafeCaptureMediaProbes()
        {
            var capture = HarnessMediaProbeCapture.Empty(_codec);
            try
            {
                if (_corpus == null || _corpus.Entry == null || string.IsNullOrWhiteSpace(_corpus.Root))
                {
                    capture.codec.diagnostic = "Performance corpus is unavailable for codec probing.";
                }
                else
                {
                    var mediaPath = Path.GetFullPath(Path.Combine(_corpus.Root, _corpus.Entry.file));
                    var probe = new ExtensionVideoCapabilityProbe(new FileVideoMetadataProbe()).Probe(mediaPath);
                    capture.codec.path = "ExtensionVideoCapabilityProbe(FileVideoMetadataProbe)";
                    capture.codec.passed = probe.IsSuccess && probe.Value != null && probe.Value.Supported;
                    if (probe.IsSuccess && probe.Value != null)
                    {
                        var value = probe.Value;
                        capture.codec.supported = value.Supported;
                        capture.codec.container = value.Container.ToString();
                        capture.codec.codec = value.Codec.ToString();
                        capture.codec.hasAlpha = value.HasAlpha;
                        capture.codec.hasAudio = value.HasAudio;
                        capture.codec.durationSeconds = value.DurationSeconds;
                        capture.codec.diagnostic = value.DiagnosticMessage ?? string.Empty;
                    }
                    else
                    {
                        capture.codec.supported = false;
                        capture.codec.diagnostic = DiagnosticText(probe.Diagnostic);
                    }
                }

                if (_codec == HarnessCodec.Hap)
                {
                    capture.nativePlugin.path = "PInvokeHapNativeApi.ProbeInstalledBinary";
                    var nativeApi = new PInvokeHapNativeApi();
                    var native = nativeApi.ProbeInstalledBinary();
                    capture.nativePlugin.supportedPlatform = nativeApi.IsSupportedPlatform;
                    capture.nativePlugin.passed = native != null && native.IsAvailable;
                    capture.nativePlugin.abiVersion = native == null ? 0u : native.AbiVersion;
                    capture.nativePlugin.capabilities = native == null ? 0u : native.Capabilities;
                    capture.nativePlugin.diagnosticCode = native == null ? "media.hap.probe.empty" : native.DiagnosticCode;
                    capture.nativePlugin.diagnostic = native == null ? "Hap native probe returned no result." : native.Message;
                }
                else
                {
                    capture.nativePlugin.path = "UnityVideoBackend";
                    capture.nativePlugin.supportedPlatform = true;
                    capture.nativePlugin.passed = true;
                    capture.nativePlugin.diagnosticCode = string.Empty;
                    capture.nativePlugin.diagnostic = "H.264 uses UnityVideoBackend; Hap native plugin probe is not required.";
                }
            }
            catch (Exception exception)
            {
                PreserveFinalizationFailure("Media probe snapshot failed: " + exception);
                capture.codec.passed = false;
                capture.codec.supported = false;
                capture.codec.diagnostic = "Media probe snapshot failed: " + exception.Message;
                capture.nativePlugin.passed = false;
                capture.nativePlugin.diagnostic = "Native probe snapshot failed: " + exception.Message;
            }
            return capture;
        }

        private void DisposeGcRecorderSafely()
        {
            if (!_gcRecorderStarted) return;
            try { _gcAllocationRecorder.Dispose(); }
            catch (Exception exception) { PreserveFinalizationFailure("GC profiler teardown failed: " + exception); }
            finally { _gcRecorderStarted = false; }
        }

        private void DisposeCompositionOnce()
        {
            _compositionTeardown.Try(() => _composition?.Dispose(),
                exception => PreserveFinalizationFailure("Harness teardown failed: " + exception));
        }

        private void PreserveFinalizationFailure(string message)
        {
            if (string.IsNullOrEmpty(_failure)) _failure = message ?? "Harness finalization failed.";
            else Debug.LogError(message);
        }

        private static HarnessPreviewMetric ToPreviewMetric(ProductionSurfaceOwnershipSnapshot ownership,
            IReadOnlyList<ApplicationOutputSurfaceReadModel> outputPreviews)
        {
            var output = (outputPreviews ?? Array.Empty<ApplicationOutputSurfaceReadModel>()).FirstOrDefault(x => x.Id == ownership.Id);
            return new HarnessPreviewMetric
            {
                id = ownership.Id,
                width = ownership.Width,
                height = ownership.Height,
                format = ownership.GraphicsFormat,
                targetFramesPerSecond = ownership.TargetFramesPerSecond,
                frameNumber = ownership.FrameNumber,
                quality = output?.Quality ?? string.Empty,
                qualityStage = ParseQualityStage(output?.Quality)
            };
        }

        private static HarnessPreviewMetric ToPreviewMetric(ProductionPerformanceSurfaceSnapshot ownership,
            IReadOnlyList<ApplicationOutputSurfaceReadModel> outputPreviews)
        {
            ApplicationOutputSurfaceReadModel output = null;
            foreach (var candidate in outputPreviews ?? Array.Empty<ApplicationOutputSurfaceReadModel>())
                if (candidate.Id == ownership.Id) { output = candidate; break; }
            return new HarnessPreviewMetric
            {
                id = ownership.Id,
                width = ownership.Width,
                height = ownership.Height,
                format = ownership.GraphicsFormat,
                targetFramesPerSecond = ownership.TargetFramesPerSecond,
                frameNumber = ownership.FrameNumber,
                quality = output?.Quality ?? string.Empty,
                qualityStage = ParseQualityStage(output?.Quality)
            };
        }

        private static HarnessFrameTimingSourceArtifact ToFrameTimingArtifact(ProductionFrameTimingDiagnostic diagnostic)
        {
            diagnostic = diagnostic ?? ProductionFrameTimingDiagnostic.Unavailable;
            return new HarnessFrameTimingSourceArtifact
            {
                rawCount = diagnostic.RawCount,
                rawIdentity = diagnostic.RawIdentity,
                // Keep the legacy alias while making each Unity CPU timing
                // meaning explicit in the artifact. Public quality uses the
                // main/render critical-path workload; these are raw diagnostics.
                rawCpuMilliseconds = diagnostic.RawCpuFrameTimeMilliseconds,
                rawCpuFrameTimeMilliseconds = diagnostic.RawCpuFrameTimeMilliseconds,
                rawCpuMainThreadFrameTimeMilliseconds = diagnostic.RawCpuMainThreadFrameTimeMilliseconds,
                rawCpuRenderThreadFrameTimeMilliseconds = diagnostic.RawCpuRenderThreadFrameTimeMilliseconds,
                rawCpuMainThreadPresentWaitMilliseconds = diagnostic.RawCpuMainThreadPresentWaitMilliseconds,
                rawGpuMilliseconds = diagnostic.RawGpuMilliseconds,
                pendingBefore = diagnostic.PendingBefore,
                pendingAfter = diagnostic.PendingAfter,
                outcome = diagnostic.Outcome,
                candidateOutcome = diagnostic.CandidateOutcome,
                performanceFrameNumber = diagnostic.PerformanceFrameNumber,
                exceptionType = diagnostic.ExceptionType
            };
        }

        private static int ParseQualityStage(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality) || !quality.StartsWith("Stage", StringComparison.Ordinal)) return -1;
            return int.TryParse(quality.Substring("Stage".Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var stage) ? stage : -1;
        }

        private void Fail(string message) { if (string.IsNullOrEmpty(_failure)) _failure = message ?? "Harness failed."; _finished = true; }
        private void FailEnvironment(string message) { Fail("ENVIRONMENT: " + (message ?? "Corpus is unavailable.")); }
        private static bool IsEnvironmentFailure(string failure) => !string.IsNullOrEmpty(failure) && failure.StartsWith("ENVIRONMENT:", StringComparison.Ordinal);
        private string ResolveCorpusRoot()
        {
            if (!string.IsNullOrWhiteSpace(_corpusRoot)) return _corpusRoot;
            var commandRoot = _options?.CorpusRoot;
            if (!string.IsNullOrWhiteSpace(commandRoot)) return commandRoot;
            return Path.Combine(UnityApplication.streamingAssetsPath, "PerformanceCorpus");
        }
        private static string DiagnosticText(Diagnostic diagnostic) => diagnostic == null ? "unknown diagnostic" : diagnostic.Code.Value + ": " + diagnostic.Message;

        private sealed class HarnessNode
        {
            public string Name { get; }
            public string Type { get; }
            public string Id { get; }
            public float X { get; }
            public float Y { get; }
            public HarnessNode(string name, string type, out string id)
            { Name = name; Type = type; Id = Guid.NewGuid().ToString("D"); id = Id; X = 0; Y = 0; }
        }

        private sealed class HarnessMediaProbeCapture
        {
            public HarnessNativePluginProbeArtifact nativePlugin;
            public HarnessCodecProbeArtifact codec;

            public static HarnessMediaProbeCapture Empty(HarnessCodec codec)
            {
                return new HarnessMediaProbeCapture
                {
                    nativePlugin = new HarnessNativePluginProbeArtifact
                    {
                        path = codec == HarnessCodec.Hap ? "PInvokeHapNativeApi.ProbeInstalledBinary" : "UnityVideoBackend",
                        diagnosticCode = string.Empty,
                        diagnostic = string.Empty
                    },
                    codec = new HarnessCodecProbeArtifact
                    {
                        path = "ExtensionVideoCapabilityProbe(FileVideoMetadataProbe)",
                        backend = codec == HarnessCodec.H264 ? VideoBackendKind.UnityVideoBackend.ToString() : VideoBackendKind.HapVideoBackend.ToString(),
                        container = string.Empty,
                        codec = string.Empty,
                        diagnostic = string.Empty
                    }
                };
            }
        }

        private readonly struct HarnessConnection
        {
            public string Source { get; }
            public string SourcePort { get; }
            public string Destination { get; }
            public string DestinationPort { get; }
            public HarnessConnection(string source, string sourcePort, string destination, string destinationPort)
            { Source = source; SourcePort = sourcePort; Destination = destination; DestinationPort = destinationPort; }
        }
    }

    public sealed class HarnessOptions
    {
        public HarnessMode Mode { get; private set; } = HarnessMode.Performance;
        public HarnessAcceptanceStage AcceptanceStage { get; private set; } = HarnessAcceptanceStage.Initial;
        public string FixtureRoot { get; private set; }
        public string ProjectRoot { get; private set; }
        public string ExpectedFingerprint { get; private set; }
        public string ExpectedBackupFingerprint { get; private set; }
        public HarnessCodec Codec { get; private set; } = HarnessCodec.H264;
        public string CorpusRoot { get; private set; }
        public string ArtifactDirectory { get; private set; }
        public double WarmupSeconds { get; private set; } = 30d;
        public double MeasureSeconds { get; private set; } = 600d;
        public bool FixtureMode { get; private set; }
        public bool ShouldQuit { get; private set; } = true;
        public bool HasOverrides { get; private set; }
        public bool HasDurationOverrides { get; private set; }

        public static HarnessOptions Parse(IEnumerable<string> args)
        {
            var result = new HarnessOptions();
            var list = (args ?? Array.Empty<string>()).ToArray();
            for (var i = 0; i < list.Length; i++)
            {
                var key = list[i] ?? string.Empty;
                string Next() => i + 1 < list.Length ? list[++i] : string.Empty;
                if (string.Equals(key, "-sdHarnessMode", StringComparison.OrdinalIgnoreCase))
                {
                    Enum.TryParse(Next(), true, out HarnessMode mode); result.Mode = mode; result.HasOverrides = true;
                }
                else if (string.Equals(key, "-sdHarnessStage", StringComparison.OrdinalIgnoreCase))
                {
                    Enum.TryParse(Next(), true, out HarnessAcceptanceStage stage); result.AcceptanceStage = stage; result.HasOverrides = true;
                }
                else if (string.Equals(key, "-sdHarnessFixtureRoot", StringComparison.OrdinalIgnoreCase)) { result.FixtureRoot = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessProjectRoot", StringComparison.OrdinalIgnoreCase)) { result.ProjectRoot = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessExpectedFingerprint", StringComparison.OrdinalIgnoreCase)) { result.ExpectedFingerprint = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessExpectedBackupFingerprint", StringComparison.OrdinalIgnoreCase)) { result.ExpectedBackupFingerprint = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessCodec", StringComparison.OrdinalIgnoreCase))
                { Enum.TryParse(Next(), true, out HarnessCodec codec); result.Codec = codec; result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessCorpusRoot", StringComparison.OrdinalIgnoreCase)) { result.CorpusRoot = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessArtifactDir", StringComparison.OrdinalIgnoreCase)) { result.ArtifactDirectory = Next(); result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessWarmupSeconds", StringComparison.OrdinalIgnoreCase)) { double.TryParse(Next(), out var value); result.WarmupSeconds = value; result.HasOverrides = true; result.HasDurationOverrides = true; }
                else if (string.Equals(key, "-sdHarnessMeasureSeconds", StringComparison.OrdinalIgnoreCase)) { double.TryParse(Next(), out var value); result.MeasureSeconds = value; result.HasOverrides = true; result.HasDurationOverrides = true; }
                else if (string.Equals(key, "-sdHarnessFixtureMode", StringComparison.OrdinalIgnoreCase)) { result.FixtureMode = true; result.HasOverrides = true; }
                else if (string.Equals(key, "-sdHarnessNoQuit", StringComparison.OrdinalIgnoreCase)) result.ShouldQuit = false;
            }
            return result;
        }
    }
}
