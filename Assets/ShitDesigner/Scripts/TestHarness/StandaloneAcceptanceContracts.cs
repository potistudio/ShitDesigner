using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Persistence;

namespace ShitDesigner.TestHarness
{
    [Serializable]
    public sealed class AcceptanceFixtureManifest
    {
        public int version;
        public AcceptanceFixtureEntry[] fixtures;
    }

    [Serializable]
    public sealed class AcceptanceFixtureEntry
    {
        public string file;
        public string codec;
        public int width;
        public int height;
        public int fps;
        public bool hasAlpha;
        public bool hasAudio;
        public string xxh3_128;
        public long bytes;
        public string probe;
    }

    public sealed class AcceptanceFixtureValidationResult
    {
        public bool IsValid { get; }
        public string Root { get; }
        public string Error { get; }
        public IReadOnlyList<AcceptanceFixtureEntry> Entries { get; }

        private AcceptanceFixtureValidationResult(bool valid, string root, string error, IReadOnlyList<AcceptanceFixtureEntry> entries)
        {
            IsValid = valid;
            Root = root ?? string.Empty;
            Error = error ?? string.Empty;
            Entries = entries ?? Array.Empty<AcceptanceFixtureEntry>();
        }

        public static AcceptanceFixtureValidationResult Success(string root, IReadOnlyList<AcceptanceFixtureEntry> entries)
            => new AcceptanceFixtureValidationResult(true, root, null, entries);

        public static AcceptanceFixtureValidationResult Failure(string root, string error)
            => new AcceptanceFixtureValidationResult(false, root, error, null);
    }

    /// <summary>
    /// Validates the checked-in short fixtures before a Player is allowed to
    /// start an acceptance run. Missing or changed media is an environment
    /// failure; it is never converted to a skipped codec.
    /// </summary>
    public static class AcceptanceFixtureValidator
    {
        private static readonly string[] RequiredCodecs = { "H264", "VP8", "Hap1", "Hap5", "HapY", "HapM" };

        public static AcceptanceFixtureValidationResult Validate(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return AcceptanceFixtureValidationResult.Failure(root, "Acceptance fixture root is required.");
            string absoluteRoot;
            try { absoluteRoot = Path.GetFullPath(root); }
            catch (Exception exception) { return AcceptanceFixtureValidationResult.Failure(root, "Acceptance fixture root is invalid: " + exception.Message); }
            var manifestPath = Path.Combine(absoluteRoot, "manifest.json");
            if (!File.Exists(manifestPath)) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture manifest is missing: " + manifestPath);

            AcceptanceFixtureManifest manifest;
            try { manifest = UnityEngine.JsonUtility.FromJson<AcceptanceFixtureManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception exception) { return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture manifest is invalid: " + exception.Message); }
            if (manifest == null || manifest.version <= 0) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture manifest version is missing.");

            var entries = (manifest.fixtures ?? Array.Empty<AcceptanceFixtureEntry>()).Where(x => x != null).ToList();
            var selected = new List<AcceptanceFixtureEntry>();
            foreach (var codec in RequiredCodecs)
            {
                // The functional H.264 fixture is intentionally the audio-track
                // variant: the runtime must observe the track and ignore its
                // samples.  The manifest also contains a silent H.264 clip, so
                // selecting the first codec-only match makes the checked-in
                // fixture order change the acceptance contract.  Prefer the
                // required H.264 variant while retaining the first match below
                // so an audio-less manifest reports a metadata failure rather
                // than a misleading missing-codec failure.
                var codecEntries = entries.Where(x => string.Equals(x.codec, codec, StringComparison.OrdinalIgnoreCase)).ToList();
                var entry = string.Equals(codec, "H264", StringComparison.OrdinalIgnoreCase)
                    ? codecEntries.FirstOrDefault(x => x.hasAudio) ?? codecEntries.FirstOrDefault()
                    : codecEntries.FirstOrDefault();
                if (entry == null) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture is missing for codec " + codec + ".");
                if (string.IsNullOrWhiteSpace(entry.file) || !AssetIntegrity.IsDigest(entry.xxh3_128) || entry.bytes <= 0 ||
                    entry.width <= 0 || entry.height <= 0 || entry.fps <= 0 ||
                    !string.Equals(entry.probe, "Supported", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(codec, "H264", StringComparison.OrdinalIgnoreCase) && !entry.hasAudio) ||
                    (string.Equals(codec, "VP8", StringComparison.OrdinalIgnoreCase) && !entry.hasAlpha))
                    return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture metadata is invalid for codec " + codec + ".");
                var path = Path.GetFullPath(Path.Combine(absoluteRoot, entry.file));
                if (!IsContained(absoluteRoot, path)) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture escapes its root: " + entry.file);
                if (!File.Exists(path)) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture file is missing: " + path);
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length != entry.bytes) return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture byte count differs for " + entry.file + ".");
                    using (var stream = File.OpenRead(path))
                    {
                        var digest = AssetIntegrity.Hash(stream);
                        if (!string.Equals(digest, entry.xxh3_128, StringComparison.OrdinalIgnoreCase))
                            return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture XXH3-128 differs for " + entry.file + ".");
                    }
                }
                catch (Exception exception) { return AcceptanceFixtureValidationResult.Failure(absoluteRoot, "Acceptance fixture could not be verified: " + exception.Message); }
                selected.Add(entry);
            }
            return AcceptanceFixtureValidationResult.Success(absoluteRoot, selected);
        }

        private static bool IsContained(string root, string path)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Read-model component hashes used only to diagnose a canonical
    /// Project hash mismatch. The acceptance equality gate itself uses
    /// ProjectSerializer's exact canonical output through ProjectApplication.
    /// Workspace and runtime output state are intentionally excluded.</summary>
    public static class AcceptanceFingerprint
    {
        public sealed class Components
        {
            public string Project { get; }
            public string Graph { get; }
            public string Parameters { get; }
            public string Controls { get; }
            public string Presets { get; }
            public string Dashboard { get; }
            public string Previews { get; }
            public string Media { get; }

            public Components(string project, string graph, string parameters, string controls, string presets, string dashboard, string previews, string media)
            {
                Project = project ?? string.Empty;
                Graph = graph ?? string.Empty;
                Parameters = parameters ?? string.Empty;
                Controls = controls ?? string.Empty;
                Presets = presets ?? string.Empty;
                Dashboard = dashboard ?? string.Empty;
                Previews = previews ?? string.Empty;
                Media = media ?? string.Empty;
            }

            public string Fingerprint => Hash("project=" + Project + "|graph=" + Graph + "|parameters=" + Parameters + "|controls=" + Controls + "|presets=" + Presets + "|dashboard=" + Dashboard + "|previews=" + Previews + "|media=" + Media);

            /// <summary>Stable marker/artifact representation. Component
            /// hashes make a restart mismatch actionable without serializing
            /// project content into the acceptance artifact.</summary>
            public string Describe() => "project=" + Project + ";graph=" + Graph + ";parameters=" + Parameters + ";controls=" + Controls + ";presets=" + Presets + ";dashboard=" + Dashboard + ";previews=" + Previews + ";media=" + Media;

            public static bool TryParse(string value, out Components components)
            {
                components = null;
                if (string.IsNullOrWhiteSpace(value)) return false;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = item.IndexOf('=');
                    if (separator <= 0 || separator == item.Length - 1) return false;
                    values[item.Substring(0, separator)] = item.Substring(separator + 1);
                }
                string project, graph, parameters, controls, presets, dashboard, previews, media;
                if (!values.TryGetValue("project", out project) || !values.TryGetValue("graph", out graph) ||
                    !values.TryGetValue("parameters", out parameters) || !values.TryGetValue("controls", out controls) ||
                    !values.TryGetValue("presets", out presets) || !values.TryGetValue("dashboard", out dashboard) || !values.TryGetValue("previews", out previews) ||
                    !values.TryGetValue("media", out media)) return false;
                components = new Components(project, graph, parameters, controls, presets, dashboard, previews, media);
                return true;
            }
        }

        public static string Compute(ApplicationReadModel model)
            => ComputeComponents(model)?.Fingerprint ?? string.Empty;

        public static Components ComputeComponents(ApplicationReadModel model)
        {
            if (model == null || model.Project?.Model == null || model.Graph?.Model == null) return null;
            var project = model.Project.Model;
            var projectContent = new StringBuilder(256);
            projectContent.Append("project:").Append(project.ProjectName).Append('|');
            var output = model.Output?.Model;
            var graph = new StringBuilder(2048);
            foreach (var node in model.Graph.Model.Nodes.OrderBy(x => x.Id, StringComparer.Ordinal))
                graph.Append("node:").Append(node.Id).Append(':').Append(node.TypeId).Append(':').Append(node.DisplayName).Append(':').Append(node.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(node.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(node.Enabled).Append('|');
            foreach (var connection in model.Graph.Model.Connections.OrderBy(x => x.Id, StringComparer.Ordinal))
                graph.Append("edge:").Append(connection.Id).Append(':').Append(connection.FromNodeId).Append(':').Append(connection.FromPortId).Append(':').Append(connection.ToNodeId).Append(':').Append(connection.ToPortId).Append('|');
            var parameters = new StringBuilder(1024);
            foreach (var parameter in (model.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>()).OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.ParameterId, StringComparer.Ordinal))
                // EffectiveValue can be a transient preset/control result;
                // the persisted contract is the authored BaseValue.
                parameters.Append("param:").Append(parameter.NodeId).Append(':').Append(parameter.ParameterId).Append(':').Append(parameter.BaseValue).Append(':').Append(parameter.LogicalTargets).Append(':').Append(parameter.Expression).Append(':').Append(parameter.OutputClamp).Append('|');
            var controls = new StringBuilder(1024);
            foreach (var control in project.LogicalControls.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                controls.Append("control:").Append(control.Id).Append(':').Append(control.Name).Append(':').Append(control.Kind).Append(':').Append(control.PresetId).Append('|');
                foreach (var mapping in (control.Mappings ?? Array.Empty<ControlMappingReadModel>()).OrderBy(x => x.PhysicalId, StringComparer.Ordinal).ThenBy(x => x.ControlPath, StringComparer.Ordinal))
                    controls.Append("mapping:").Append(mapping.PhysicalId).Append(':').Append(mapping.ControlPath).Append(':').Append(mapping.RawMin.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(mapping.RawMax.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(mapping.Invert).Append('|');
            }
            var presets = new StringBuilder(1024);
            foreach (var preset in (model.Presets?.Model ?? Array.Empty<ApplicationPresetReadModel>()).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                presets.Append("preset:").Append(preset.Id).Append(':').Append(preset.Name).Append(':').Append(preset.Category).Append(':').Append(preset.SortIndex).Append('|');
                foreach (var entry in (preset.Entries ?? Array.Empty<ApplicationPresetEntryReadModel>()).OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.ParameterId, StringComparer.Ordinal))
                    presets.Append("preset-entry:").Append(entry.NodeId).Append(':').Append(entry.ParameterId).Append(':').Append(entry.ValueType).Append(':').Append(entry.Value).Append('|');
            }
            var dashboardContent = new StringBuilder(1024);
            foreach (var dashboard in (model.Dashboard?.Model ?? Array.Empty<ApplicationDashboardReadModel>()).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                dashboardContent.Append("dashboard:").Append(dashboard.Id).Append(':').Append(dashboard.Name).Append('|');
                foreach (var widget in (dashboard.Widgets ?? Array.Empty<ApplicationDashboardWidgetReadModel>()).OrderBy(x => x.Id, StringComparer.Ordinal))
                    dashboardContent.Append("dashboard-widget:").Append(widget.Id).Append(':').Append(widget.NodeId).Append(':').Append(widget.ParameterId).Append(':').Append(widget.Column).Append(':').Append(widget.Row).Append(':').Append(widget.Width).Append(':').Append(widget.Height).Append(':').Append(widget.Label).Append(':').Append(widget.IsBroken).Append('|');
            }
            var previews = ComputePersistedPreviewComponent(output?.Previews);
            var mediaContent = new StringBuilder(1024);
            foreach (var media in (model.Media?.Model ?? Array.Empty<ApplicationMediaReadModel>()).OrderBy(x => x.Id, StringComparer.Ordinal))
                mediaContent.Append("media:").Append(media.Id).Append(':').Append(media.RelativePath).Append(':').Append(media.Size).Append(':').Append(media.IntegrityHash).Append(':').Append(media.Kind).Append(':').Append(media.ColorSpace).Append(':').Append(media.AlphaMode).Append('|');
            return new Components(Hash(projectContent.ToString()), Hash(graph.ToString()), Hash(parameters.ToString()), Hash(controls.ToString()), Hash(presets.ToString()), Hash(dashboardContent.ToString()), previews, Hash(mediaContent.ToString()));
        }

        /// <summary>Only the persisted Preview tab descriptor participates in
        /// Canonical Project equality. Runtime quality and demand negotiation
        /// must never change a saved Project fingerprint.</summary>
        public static string ComputePersistedPreviewComponent(IEnumerable<ApplicationOutputSurfaceReadModel> previews)
        {
            var content = new StringBuilder(512);
            // Preview tab order is Project UI State. Do not sort it: [A,B]
            // and [B,A] are different canonical project payloads.
            foreach (var preview in previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>())
                // TargetKind is derived from the persisted surface id; quality
                // and demand are runtime negotiation, not Project content.
                content.Append("preview:").Append(preview.Id).Append(':').Append(preview.FitMode).Append(':').Append(preview.BackgroundMode).Append('|');
            return Hash(content.ToString());
        }

        public static string DescribeDifference(string expectedComponents, Components actual)
        {
            Components expected;
            if (actual == null) return "actual persistent components unavailable";
            if (!Components.TryParse(expectedComponents, out expected)) return "expected persistent component evidence unavailable; actual=" + actual.Describe();
            var changed = new List<string>();
            if (!string.Equals(expected.Project, actual.Project, StringComparison.OrdinalIgnoreCase)) changed.Add("project");
            if (!string.Equals(expected.Graph, actual.Graph, StringComparison.OrdinalIgnoreCase)) changed.Add("graph");
            if (!string.Equals(expected.Parameters, actual.Parameters, StringComparison.OrdinalIgnoreCase)) changed.Add("parameters");
            if (!string.Equals(expected.Controls, actual.Controls, StringComparison.OrdinalIgnoreCase)) changed.Add("controls");
            if (!string.Equals(expected.Presets, actual.Presets, StringComparison.OrdinalIgnoreCase)) changed.Add("presets");
            if (!string.Equals(expected.Dashboard, actual.Dashboard, StringComparison.OrdinalIgnoreCase)) changed.Add("dashboard");
            if (!string.Equals(expected.Previews, actual.Previews, StringComparison.OrdinalIgnoreCase)) changed.Add("previews");
            if (!string.Equals(expected.Media, actual.Media, StringComparison.OrdinalIgnoreCase)) changed.Add("media");
            return changed.Count == 0 ? "persistent components matched; expected fingerprint source differs" : "changed=" + string.Join(",", changed) + "; expected=" + expected.Describe() + "; actual=" + actual.Describe();
        }

        private static string Hash(string value) => AssetIntegrity.Hash(Encoding.UTF8.GetBytes(value ?? string.Empty));

        public static bool Matches(ApplicationReadModel model, string expected)
            => !string.IsNullOrWhiteSpace(expected) && string.Equals(Compute(model), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static class AcceptanceContract
    {
        public const string CurrentArtifactContractVersion = "2";

        /// <summary>A Save was published when the public Task read model has
        /// a new id and Save kind. Terminal status is evidence of publication,
        /// not evidence that no task was created.</summary>
        public static bool SaveTaskPublished(ApplicationTaskReadModel task, Guid priorTaskId)
            => task != null && task.TaskId != priorTaskId &&
                string.Equals(task.Kind, "Save", StringComparison.OrdinalIgnoreCase);

        public static bool SaveTaskFailed(ApplicationTaskReadModel task)
            => task != null && string.Equals(task.Status, "Failed", StringComparison.OrdinalIgnoreCase);

        public static string DescribeSaveTaskFailure(ApplicationTaskReadModel task)
        {
            if (task == null) return "Save task read model is unavailable.";
            var diagnostic = task.Diagnostic;
            var exception = diagnostic?.Exception;
            return "id=" + task.TaskId + "; kind=" + task.Kind + "; stage=" + task.Stage + "; status=" + task.Status +
                "; path=" + task.Path + "; diagnosticCode=" + (diagnostic?.Code.Value ?? string.Empty) +
                "; diagnosticMessage=" + (diagnostic?.Message ?? string.Empty) + "; exceptionType=" + (exception?.TypeName ?? string.Empty) +
                "; exceptionMessage=" + (exception?.Message ?? string.Empty) + "; exceptionStack=" + (exception?.StackTrace ?? string.Empty);
        }

        public static bool OutputsReady(ApplicationOutputReadModel output)
        {
            if (output == null || output.FrameNumber == 0 || output.Program == null || output.Program.Width != 1920 || output.Program.Height != 1080) return false;
            if (!string.Equals(output.Program.State, "Available", StringComparison.OrdinalIgnoreCase) && !string.Equals(output.Program.State, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase)) return false;
            var previews = output.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>();
            return previews.Count == 2 && previews.All(preview => preview != null && preview.IsDemanded && preview.Width == 640 && preview.Height == 360 &&
                (string.Equals(preview.State, "Available", StringComparison.OrdinalIgnoreCase) || string.Equals(preview.State, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The required graph routes VideoPlayer through Program and
        /// both Previews.  Therefore output readiness is only meaningful
        /// after the public media/playing/loop binding is applied.</summary>
        public static bool OutputsReadyAfterVideoBinding(ApplicationOutputReadModel output,
            IReadOnlyList<ApplicationParameterReadModel> parameters, string videoNodeId)
            => OutputsReady(output) && HarnessVideoTransportContract.HasActiveBinding(parameters, videoNodeId);

        /// <summary>Output readiness is historical acceptance evidence just
        /// like real-frame evidence. A later transition (for example, the
        /// next fixture replacing a short video) must not erase a public
        /// snapshot that has already proved the required bound outputs.</summary>
        public static bool ObserveOutputsReadyAfterVideoBinding(bool alreadyObserved, ApplicationOutputReadModel output,
            IReadOnlyList<ApplicationParameterReadModel> parameters, string videoNodeId)
            => alreadyObserved || OutputsReadyAfterVideoBinding(output, parameters, videoNodeId);

        /// <summary>A real presentation is stricter than output readiness:
        /// Program must be presenting an available frame, not merely holding
        /// an earlier valid one.  The two demanded Preview descriptors remain
        /// part of the same public read-model observation.</summary>
        public static bool RealPresentedFrame(ApplicationOutputReadModel output)
        {
            if (output == null || output.FrameNumber == 0 || output.Program == null ||
                !string.Equals(output.Program.State, "Available", StringComparison.OrdinalIgnoreCase) ||
                output.Program.Width != 1920 || output.Program.Height != 1080) return false;
            var previews = output.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>();
            return previews.Count == 2 && previews.All(preview => preview != null && preview.IsDemanded &&
                preview.Width == 640 && preview.Height == 360 &&
                (string.Equals(preview.State, "Available", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(preview.State, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Acceptance evidence is historical: once a single public
        /// read-model evaluation has proved a real presentation, a later
        /// transitional snapshot must not erase that proof.</summary>
        public static bool ObserveRealPresentedFrame(bool alreadyObserved, ApplicationOutputReadModel output)
            => alreadyObserved || RealPresentedFrame(output);

        /// <summary>A fixture completes only after its active binding has
        /// separately produced concrete Program/Preview frame increments and
        /// a public read-model snapshot has proved bound output readiness and
        /// a real Program frame. Those observations may arrive on adjacent
        /// evaluations, but both must precede the next fixture binding.</summary>
        public static bool FixtureFrameEvidenceObserved(bool ownershipFramesObserved, bool outputsObserved, bool realFrameObserved)
            => ownershipFramesObserved && outputsObserved && realFrameObserved;

        /// <summary>A fixture has proved Prepare only while its public graph
        /// node actually reports Preparing. Ready from an earlier or an
        /// instantaneous completion is not evidence of that transition.</summary>
        public static bool VideoPrepareObserved(ApplicationGraphReadModel graph, string videoNodeId)
            => !string.IsNullOrWhiteSpace(videoNodeId) && graph?.Nodes?.Any(node => node != null &&
                string.Equals(node.Id, videoNodeId, StringComparison.Ordinal) &&
                string.Equals(node.Status, "Preparing", StringComparison.OrdinalIgnoreCase)) == true;

        /// <summary>Playback commands are queued only after the new MediaAsset
        /// is public and its Prepare transition has been observed.</summary>
        public static bool CanStartVideoPlaybackAfterPrepare(bool mediaAssetPublished, bool prepareObserved)
            => mediaAssetPublished && prepareObserved;

        /// <summary>
        /// The acceptance control must target the writable, visible Color
        /// parameter exposed by the Shader Generator node.  Scene generators
        /// deliberately have no such parameter in the production catalog.
        /// </summary>
        public static ApplicationParameterReadModel FindWritableShaderGeneratorColorParameter(IEnumerable<ApplicationParameterReadModel> parameters, string generatorNodeId)
        {
            if (string.IsNullOrWhiteSpace(generatorNodeId)) return null;
            return (parameters ?? Array.Empty<ApplicationParameterReadModel>()).FirstOrDefault(parameter => parameter != null &&
                string.Equals(parameter.NodeId, generatorNodeId, StringComparison.Ordinal) &&
                string.Equals(parameter.NodeTypeId, "shitdesigner.shader.generator", StringComparison.Ordinal) &&
                string.Equals(parameter.ParameterId, "color", StringComparison.Ordinal) &&
                string.Equals(parameter.ValueType, ParameterType.Color.ToString(), StringComparison.Ordinal) &&
                parameter.IsVisible && !parameter.IsReadOnly && !parameter.IsBroken);
        }

        /// <summary>
        /// Imported media is part of the project payload.  The public
        /// read-model must therefore expose a project-relative path which is
        /// contained by the project root and resolves to a copied file.  A
        /// fixture/source path is never a valid persisted reference.
        /// </summary>
        public static string ValidatePortableMedia(ApplicationReadModel model, string projectRoot, string fixtureRoot = null)
        {
            if (model == null || model.Project?.Model == null) return "Acceptance project read model is missing.";
            if (string.IsNullOrWhiteSpace(projectRoot)) return "Acceptance project root is missing for portable media validation.";
            return ValidatePortableMediaPaths(model.Media?.Model, projectRoot, fixtureRoot);
        }

        public static string ValidatePortableMediaPaths(IEnumerable<ApplicationMediaReadModel> importedMedia, string projectRoot, string fixtureRoot = null)
        {
            var media = (importedMedia ?? Array.Empty<ApplicationMediaReadModel>()).ToList();
            if (media.Count == 0) return "Acceptance project contains no imported media.";

            string root;
            try { root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception exception) { return "Acceptance project root is invalid: " + exception.Message; }
            string fixture = null;
            if (!string.IsNullOrWhiteSpace(fixtureRoot))
            {
                try { fixture = Path.GetFullPath(fixtureRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch (Exception exception) { return "Acceptance fixture root is invalid: " + exception.Message; }
            }

            foreach (var asset in media)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.RelativePath) || Path.IsPathRooted(asset.RelativePath))
                    return "Imported media must expose a non-empty project-relative path.";
                var relative = asset.RelativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return "Imported media escapes the project root: " + asset.RelativePath;
                string resolved;
                try { resolved = Path.GetFullPath(Path.Combine(root, relative)); }
                catch (Exception exception) { return "Imported media path is invalid: " + exception.Message; }
                if (!IsContained(root, resolved)) return "Imported media escapes the project root: " + asset.RelativePath;
                if (fixture != null && (resolved.StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(resolved, fixture, StringComparison.OrdinalIgnoreCase)))
                    return "Imported media still points at the acceptance fixture root: " + asset.RelativePath;
                if (!File.Exists(resolved)) return "Imported media copy is missing from the project root: " + asset.RelativePath;
            }
            return string.Empty;
        }

        public static string ValidateRequiredGraph(ApplicationGraphReadModel graph)
        {
            if (graph == null) return "Acceptance graph read model is missing.";
            var nodes = graph.Nodes ?? Array.Empty<ApplicationGraphNodeReadModel>();
            var edges = graph.Connections ?? Array.Empty<ApplicationGraphConnectionReadModel>();
            var requiredTypes = new[]
            {
                "shitdesigner.scene.3d", "shitdesigner.scene.2d", "shitdesigner.shader.effect",
                "shitdesigner.video.player", "shitdesigner.shader.blend2", "system.feedback", "system.program_output"
            };
            foreach (var type in requiredTypes)
                if (!nodes.Any(x => x != null && string.Equals(x.TypeId, type, StringComparison.Ordinal)))
                    return "Acceptance graph is missing required node type: " + type;
            if (nodes.Count(x => x != null && string.Equals(x.TypeId, "shitdesigner.shader.blend2", StringComparison.Ordinal)) < 2)
                return "Acceptance graph must contain two 2-input Blend nodes.";
            if (nodes.Count(x => x != null && string.Equals(x.TypeId, GraphConstants.PreviewTypeId, StringComparison.Ordinal)) != 2)
                return "Acceptance graph must contain exactly two Preview nodes.";

            var topology = HarnessScenarioTopology.Validate(
                nodes.Where(x => x != null).Select(x => new HarnessTopologyNode(x.Id, x.TypeId)),
                edges.Where(x => x != null).Select(x => new HarnessTopologyEdge(x.FromNodeId, x.ToNodeId)));
            return string.IsNullOrEmpty(topology) ? string.Empty : topology;
        }

        public static string ValidateLogicalControlContract(ApplicationReadModel model, string valueControlId, string triggerControlId,
            string presetId, string nodeId, string parameterId, string expectedValuePhysicalId)
        {
            var project = model?.Project?.Model;
            var controls = project?.LogicalControls ?? Array.Empty<LogicalControlReadModel>();
            var value = controls.FirstOrDefault(x => x != null && x.Id == valueControlId && x.Kind == ApplicationLogicalControlKind.Value);
            var trigger = controls.FirstOrDefault(x => x != null && x.Id == triggerControlId && x.Kind == ApplicationLogicalControlKind.PresetTrigger);
            if (value == null || trigger == null) return "Acceptance logical controls were not persisted in the public project read model.";
            if (value.Mappings == null || !value.Mappings.Any(x => x != null && x.PhysicalId == expectedValuePhysicalId))
                return "Acceptance Value control mapping was not persisted for the remapped physical key.";
            if (trigger.PresetId != presetId || trigger.PresetIsBroken) return "Acceptance PresetTrigger binding is missing or broken.";
            var parameter = (model.Parameters?.Model ?? Array.Empty<ApplicationParameterReadModel>()).FirstOrDefault(x => x != null && x.NodeId == nodeId && x.ParameterId == parameterId);
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.LogicalTargets) || !parameter.LogicalTargets.Split(',').Contains(valueControlId))
                return "Acceptance Value control no longer targets the original parameter.";
            return string.Empty;
        }

        private static bool IsContained(string root, string path)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static string ValidateStage(HarnessAcceptanceStage stage, HarnessAcceptanceArtifact artifact)
        {
            if (artifact == null) return "Acceptance artifact is missing.";
            if (!string.Equals(artifact.mode, "acceptance", StringComparison.OrdinalIgnoreCase)) return "Acceptance artifact mode is invalid.";
            if (string.IsNullOrWhiteSpace(artifact.stage) || !string.Equals(artifact.stage, stage.ToString(), StringComparison.OrdinalIgnoreCase)) return "Acceptance artifact stage is invalid.";
            if (!artifact.productionCompositionUsed || !artifact.productionCatalogUsed) return "Acceptance did not use the production composition and catalog.";
            if (!artifact.editorAssemblyExcluded) return "A UnityEditor assembly was loaded by the Player.";
            if (!artifact.presentationRootAvailable || !artifact.programAndPreviewsReady) return "Acceptance did not observe the production PresentationRoot and Program/Preview outputs.";
            if (!artifact.fileProjectWritable) return "Acceptance project write probe failed.";
            if (stage != HarnessAcceptanceStage.Recovery && !artifact.fileProjectReadable) return "Acceptance project read probe failed.";
            if (stage == HarnessAcceptanceStage.Recovery && (!artifact.backupFileReadable || artifact.fileProjectReadable)) return "Recovery file-access state is invalid: the damaged main must not be canonical-readable and the backup must be readable.";
            if (string.Equals(artifact.acceptanceContractVersion, CurrentArtifactContractVersion, StringComparison.Ordinal))
            {
                if (!artifact.requiredGraphObserved || !artifact.realFrameObserved) return "Acceptance did not observe the required Program graph and a real presented frame.";
                if (stage == HarnessAcceptanceStage.Initial && (!artifact.valueControlUpdated || !artifact.valueControlRemapped || !artifact.presetTriggerFired)) return "Acceptance logical control input/remapping/PresetTrigger was not observed through the public input path.";
                if (stage != HarnessAcceptanceStage.Initial && !artifact.logicalControlStateObserved) return "Acceptance logical control and preset state was not observed after reopen/recovery.";
                if (!artifact.mediaPortable) return "Acceptance media portability was not proven from the public read model.";
                if (string.IsNullOrWhiteSpace(artifact.valueControlId) || string.IsNullOrWhiteSpace(artifact.presetTriggerId) || string.IsNullOrWhiteSpace(artifact.presetId)) return "Acceptance logical control identifiers are missing from the artifact.";
            }
            if (stage == HarnessAcceptanceStage.Initial && (artifact.fixtures == null || artifact.fixtures.Length != 6 || artifact.fixtures.Any(x => x == null || !x.probePassed || !x.prepareObserved || !x.mediaBindingApplied || x.frameAfter <= x.frameBefore || x.preview1FrameAfter <= x.preview1FrameBefore || x.preview2FrameAfter <= x.preview2FrameBefore || x.previewFrameAfter <= x.previewFrameBefore || !x.ownershipFramesObserved || !x.outputReadyObserved || !x.realFrameObserved || !x.frameReady))) return "Acceptance fixture probe/prepare/binding/public-output/frame contract is incomplete.";
            if (artifact.persistence == null) return "Acceptance persistence artifact is missing.";
            if (stage == HarnessAcceptanceStage.Initial && (!artifact.persistence.saved || string.IsNullOrWhiteSpace(artifact.persistence.fingerprint) || string.IsNullOrWhiteSpace(artifact.persistence.backupFingerprint) || string.IsNullOrWhiteSpace(artifact.persistence.expectedBackupFingerprint) || !string.Equals(artifact.persistence.backupFingerprint, artifact.persistence.expectedBackupFingerprint, StringComparison.OrdinalIgnoreCase) || !artifact.persistence.backupReadable)) return "Initial acceptance did not save a known backup fingerprint.";
            if (stage == HarnessAcceptanceStage.Reopen && (!artifact.persistence.reopened || !string.Equals(artifact.persistence.fingerprint, artifact.persistence.expectedFingerprint, StringComparison.OrdinalIgnoreCase))) return "Reopen acceptance fingerprint did not match.";
            if (stage == HarnessAcceptanceStage.Recovery && (!artifact.persistence.recovered || !artifact.persistence.dirtyAfterRecovery || !artifact.persistence.mainFilePreservedAfterRecovery || string.IsNullOrWhiteSpace(artifact.persistence.expectedBackupFingerprint) || !string.Equals(artifact.persistence.expectedFingerprint, artifact.persistence.expectedBackupFingerprint, StringComparison.OrdinalIgnoreCase) || !string.Equals(artifact.persistence.backupFingerprint, artifact.persistence.expectedBackupFingerprint, StringComparison.OrdinalIgnoreCase) || !string.Equals(artifact.persistence.fingerprint, artifact.persistence.expectedBackupFingerprint, StringComparison.OrdinalIgnoreCase))) return "Recovery acceptance did not preserve the damaged main file, dirty recovered state, and expected backup fingerprint.";
            return string.Empty;
        }
    }
}
