using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;

namespace ShitDesigner.Graph.Tests {
	[TestFixture]
	public sealed class GraphRuntimeTests {
		private static readonly PortId Image = new PortId(GraphConstants.ImagePortId);
		private static readonly PortId Input = new PortId("input");

		private static NodeRecord Node(string id, string type, params PortSnapshotRecord[] ports) {
			return new NodeRecord(new NodeInstanceId(id), new NodeTypeId(type), 1, type, true, new ProjectPosition(0, 0), ports: ports, systemOwned: type.StartsWith("system.", StringComparison.Ordinal), userAddable: !type.StartsWith("system.", StringComparison.Ordinal));
		}

		private static PortSnapshotRecord Out(string id, PortType type) => new PortSnapshotRecord(new PortId(id), PortDirection.Output, type, false);
		private static PortSnapshotRecord RequiredIn(string id, PortType type) => new PortSnapshotRecord(new PortId(id), PortDirection.Input, type, true);
		private static PortSnapshotRecord OptionalIn(string id, PortType type) => new PortSnapshotRecord(new PortId(id), PortDirection.Input, type, false);
		private static ConnectionRecord Connection(string id, string source, string sourcePort, string destination, string destinationPort, string conversion = null) {
			return new ConnectionRecord(new ConnectionId(id), new NodeInstanceId(source), new PortId(sourcePort), new NodeInstanceId(destination), new PortId(destinationPort), conversion);
		}

		private static NodeTypeRegistry RegistryWith(params string[] types) {
			var registry = new NodeTypeRegistry();
			foreach (var type in types) {
				var result = registry.Register(new NodeTypeDefinition(new NodeTypeId(type), 1, type, "Test", new[]
				{
					new PortDefinition(Image, "Image", PortDirection.Output, PortType.ImageFrame, false)
				}));
				Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
			}
			return registry;
		}

		[Test]
		public void NodeTypeRegistry_RevisionAdvancesOnlyForAcceptedDefinitions() {
			var registry = new NodeTypeRegistry();
			Assert.That(registry.Revision, Is.EqualTo(0));
			Assert.That(registry.Register(null).IsFailure, Is.True);
			Assert.That(registry.Revision, Is.EqualTo(0));
			var definition = new NodeTypeDefinition(new NodeTypeId("test.catalog.revision"), 1, "Catalog", "Test", Array.Empty<PortDefinition>());
			Assert.That(registry.Register(definition).IsSuccess, Is.True);
			Assert.That(registry.Revision, Is.EqualTo(1));
			Assert.That(registry.Register(definition).IsFailure, Is.True);
			Assert.That(registry.Revision, Is.EqualTo(1));
		}

		private static GraphState ProgramGraph(params NodeRecord[] extra) {
			var program = Node(GraphConstants.ProgramOutputTypeId, GraphConstants.ProgramOutputTypeId, RequiredIn(GraphConstants.ImagePortId, PortType.ImageFrame));
			return new GraphState(new[] { program }.Concat(extra));
		}

		[Test]
		[Category("GRAPH_10")]
		[Category("GRAPH_11")]
		public void Connect_ExactAndInitialImplicitTypes_StoreExpectedConversion() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame), Out("color", PortType.Color));
			var destination = Node("destination", "test.nodes.destination", RequiredIn("image", PortType.ImageFrame), RequiredIn("vector", PortType.Vector4));
			var state = ProgramGraph(source, destination);
			var editor = new GraphEditor(state, RegistryWith("test.nodes.source", "test.nodes.destination"));

			var exact = editor.ApplyBatch(new[] { new ConnectEditCommand(Connection("exact", "source", "image", "destination", "image")) });
			Assert.That(exact.IsSuccess, Is.True, exact.Diagnostic?.Message);

			var implicitConnection = Connection("implicit", "source", "color", "destination", "vector");
			var implicitResult = editor.ApplyBatch(new[] { new ConnectEditCommand(implicitConnection) });
			Assert.That(implicitResult.IsSuccess, Is.True, implicitResult.Diagnostic?.Message);
			Assert.That(editor.State.FindConnection(new ConnectionId("implicit")).ConversionId, Is.EqualTo(GraphConstants.ColorToVector4ConversionId));
		}

		[Test]
		[Category("GRAPH_10")]
		[Category("GRAPH_12")]
		public void Connect_UnsupportedType_IsRejected() {
			var source = Node("source", "test.nodes.source", Out("value", PortType.Float));
			var destination = Node("destination", "test.nodes.destination", RequiredIn("value", PortType.Int));
			var editor = new GraphEditor(ProgramGraph(source, destination), RegistryWith("test.nodes.source", "test.nodes.destination"));

			var result = editor.ApplyBatch(new[] { new ConnectEditCommand(Connection("bad", "source", "value", "destination", "value")) });

			Assert.That(result.IsFailure, Is.True);
			Assert.That(editor.State.Connections, Is.Empty);
		}

		[Test]
		[Category("GRAPH_08")]
		[Category("GRAPH_07")]
		public void ReplaceConnection_InvalidCycle_KeepsExistingConnection() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var target = Node("target", "test.nodes.target", RequiredIn("input", PortType.ImageFrame), Out("image", PortType.ImageFrame));
			var state = ProgramGraph(source, target, Node("other", "test.nodes.other", Out("image", PortType.ImageFrame)));
			state = new GraphState(state.Nodes, new[] { Connection("old", "source", "image", "target", "input"), Connection("to-program", "target", "image", GraphConstants.ProgramOutputTypeId, "image") });
			var editor = new GraphEditor(state, RegistryWith("test.nodes.source", "test.nodes.target", "test.nodes.other"));

			var replacement = editor.ApplyBatch(new[] { new ReplaceInputConnectionEditCommand(Connection("cycle", "target", "image", "target", "input")) });

			Assert.That(replacement.IsFailure, Is.True);
			Assert.That(editor.State.FindConnection(new ConnectionId("old")), Is.Not.Null);
			Assert.That(editor.State.FindConnection(new ConnectionId("cycle")), Is.Null);
		}

		[Test]
		[Category("GRAPH_13")]
		public void Connect_4097thConnection_IsRejected() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var nodes = new List<NodeRecord> { Node(GraphConstants.ProgramOutputTypeId, GraphConstants.ProgramOutputTypeId, RequiredIn("image", PortType.ImageFrame)), source };
			var connections = new List<ConnectionRecord>();
			for (var i = 0; i < GraphConstants.MaxConnections; i++) {
				var destinationId = $"destination-{i:D4}";
				nodes.Add(Node(destinationId, "test.nodes.destination", RequiredIn("input", PortType.ImageFrame)));
				connections.Add(Connection($"connection-{i:D4}", "source", "image", destinationId, "input"));
			}
			nodes.Add(Node("last-destination", "test.nodes.destination", RequiredIn("input", PortType.ImageFrame)));
			var workspace = new GraphBatchWorkspace(new GraphState(nodes, connections), RegistryWith("test.nodes.source", "test.nodes.destination"));

			var result = workspace.Apply(new ConnectEditCommand(Connection("connection-last", "source", "image", "last-destination", "input")));

			Assert.That(result.IsFailure, Is.True);
			Assert.That(workspace.State.Connections, Has.Count.EqualTo(GraphConstants.MaxConnections));
		}

		[Test]
		[Category("GRAPH_07")]
		public void FeedbackCycle_IsAcceptedAsFrameBoundaryAndProducesDagPlan() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var feedback = Node("feedback", GraphConstants.FeedbackTypeId, RequiredIn("input", PortType.ImageFrame), Out("image", PortType.ImageFrame));
			var mix = Node("mix", "test.nodes.mix", RequiredIn("source", PortType.ImageFrame), RequiredIn("feedback", PortType.ImageFrame), Out("image", PortType.ImageFrame));
			var state = ProgramGraph(source, feedback, mix);
			state = new GraphState(state.Nodes, new[]
			{
				Connection("source-mix", "source", "image", "mix", "source"),
				Connection("feedback-mix", "feedback", "image", "mix", "feedback"),
				Connection("mix-feedback", "mix", "image", "feedback", "input"),
				Connection("mix-program", "mix", "image", GraphConstants.ProgramOutputTypeId, "image")
			});

			var success = EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source", "test.nodes.mix"), null, out var plan, out var diagnostic);

			Assert.That(success, Is.True, diagnostic?.Message);
			Assert.That(plan.EvaluationOrder, Does.Contain(new NodeInstanceId("mix")));
			Assert.That(plan.FeedbackCommitNodeIds, Does.Contain(new NodeInstanceId("feedback")));
		}

		[Test]
		[Category("GRAPH_07")]
		public void FeedbackOutput_PrecedesProgramEvenWhenItsIdSortsAfterProgram() {
			const string programId = "10000000-0000-4000-8000-000000000000";
			const string feedbackId = "f0000000-0000-4000-8000-000000000000";
			var source = Node("e0000000-0000-4000-8000-000000000000", "test.nodes.source", Out("image", PortType.ImageFrame));
			var feedback = Node(feedbackId, GraphConstants.FeedbackTypeId, RequiredIn("input", PortType.ImageFrame), Out("image", PortType.ImageFrame));
			var program = Node(programId, GraphConstants.ProgramOutputTypeId, RequiredIn("image", PortType.ImageFrame));
			var state = new GraphState(new[] { source, feedback, program }, new[]
			{
				Connection("source-feedback", source.Id.Value, "image", feedbackId, "input"),
				Connection("feedback-program", feedbackId, "image", programId, "image")
			});

			var success = EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"), null, out var plan, out var diagnostic);

			Assert.That(success, Is.True, diagnostic?.Message);
			Assert.That(plan.EvaluationOrder.TakeWhile(x => x != new NodeInstanceId(feedbackId)).Count(), Is.LessThan(plan.EvaluationOrder.TakeWhile(x => x != new NodeInstanceId(programId)).Count()));
		}

		[Test]
		[Category("GRAPH_06")]
		public void BrokenConnection_IsPreservedButExcludedFromActivePlan() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var state = ProgramGraph(source);
			state = new GraphState(state.Nodes, new[]
			{
				new ConnectionRecord(new ConnectionId("broken"), new NodeInstanceId("source"), Image,
					new NodeInstanceId(GraphConstants.ProgramOutputTypeId), Image,
					"missing.converter", true, "Converter was removed from the catalog.")
			});

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"), null, out var plan, out var diagnostic), Is.True, diagnostic?.Message);
			Assert.That(plan.RequiredNodeIds, Does.Contain(new NodeInstanceId(GraphConstants.ProgramOutputTypeId)));
			Assert.That(plan.RequiredNodeIds.Any(x => x == new NodeInstanceId("source")), Is.False);
			Assert.That(state.FindConnection(new ConnectionId("broken")).IsBroken, Is.True);
		}

		[Test]
		[Category("GRAPH_02")]
		[Category("GRAPH_04")]
		public void Plan_SharedSource_IsRequiredAndOrderedOnceForProgramAndPreview() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var preview = Node("preview", GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame));
			var state = ProgramGraph(source, preview);
			state = new GraphState(state.Nodes, new[]
			{
				Connection("source-program", "source", "image", GraphConstants.ProgramOutputTypeId, "image"),
				Connection("source-preview", "source", "image", "preview", "image")
			});
			var demands = new[]
			{
				new OutputDemand(OutputTargetKind.Program, new NodeInstanceId(GraphConstants.ProgramOutputTypeId), Image, 1920, 1080),
				new OutputDemand(OutputTargetKind.Preview, new NodeInstanceId("preview"), Image, 640, 360)
			};

			var success = EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"), demands, out var plan, out var diagnostic);

			Assert.That(success, Is.True, diagnostic?.Message);
			Assert.That(plan.RequiredNodeIds.Count(x => x == new NodeInstanceId("source")), Is.EqualTo(1));
			Assert.That(plan.EvaluationOrder.Count(x => x == new NodeInstanceId("source")), Is.EqualTo(1));
			var ordered = plan.EvaluationOrder.ToList();
			Assert.That(ordered.IndexOf(new NodeInstanceId("source")), Is.LessThan(ordered.IndexOf(new NodeInstanceId(GraphConstants.ProgramOutputTypeId))));
		}

		[Test]
		[Category("GRAPH_01")]
		[Category("GRAPH_09")]
		public void Plan_FanOutDoesNotDuplicateSourceAndIgnoresUnrequestedBranch() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var unused = Node("unused", "test.nodes.unused", RequiredIn("image", PortType.ImageFrame), Out("processed", PortType.ImageFrame));
			var state = ProgramGraph(source, unused);
			state = new GraphState(state.Nodes, new[]
			{
				Connection("source-program", "source", "image", GraphConstants.ProgramOutputTypeId, "image"),
				Connection("source-unused", "source", "image", "unused", "image")
			});

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source", "test.nodes.unused"), null, out var plan, out var diagnostic), Is.True, diagnostic?.Message);
			Assert.That(plan.RequiredNodeIds.Any(x => x == new NodeInstanceId("unused")), Is.False);
			Assert.That(plan.EvaluationOrder.Count(x => x == new NodeInstanceId("source")), Is.EqualTo(1));
		}

		[Test]
		[Category("GRAPH_04")]
		public void ApplyBatch_PlanFailure_DoesNotMutateCurrentState() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var editor = new GraphEditor(ProgramGraph(source), RegistryWith("test.nodes.source", "test.nodes.extra"));
			var before = editor.State;

			var result = editor.ApplyBatch(new[] { new ConnectEditCommand(Connection("bad", "source", "image", "missing", "image")) });

			Assert.That(result.IsFailure, Is.True);
			Assert.That(editor.State.Nodes.Select(x => x.Id), Is.EqualTo(before.Nodes.Select(x => x.Id)));
			Assert.That(editor.State.Connections, Is.Empty);
			Assert.That(editor.UndoCount, Is.EqualTo(0));
		}

		[Test]
		[Category("GRAPH_01")]
		public void Plan_CanonicalOrder_IsIndependentOfConnectionInsertionOrder() {
			var sourceA = Node("a", "test.nodes.source", Out("image", PortType.ImageFrame));
			var sourceB = Node("b", "test.nodes.source", Out("image", PortType.ImageFrame));
			var mix = Node("mix", "test.nodes.mix", RequiredIn("a", PortType.ImageFrame), RequiredIn("b", PortType.ImageFrame), Out("image", PortType.ImageFrame));
			var program = Node(GraphConstants.ProgramOutputTypeId, GraphConstants.ProgramOutputTypeId, RequiredIn("image", PortType.ImageFrame));
			var nodes = new[] { sourceA, sourceB, mix, program };
			var edgesA = new[] { Connection("ab", "a", "image", "mix", "a"), Connection("bb", "b", "image", "mix", "b"), Connection("mp", "mix", "image", GraphConstants.ProgramOutputTypeId, "image") };
			var edgesB = edgesA.Reverse().ToArray();

			Assert.That(EvaluationPlan.TryBuild(new GraphState(nodes, edgesA), RegistryWith("test.nodes.source", "test.nodes.mix"), null, out var planA, out var diagnosticA), Is.True, diagnosticA?.Message);
			Assert.That(EvaluationPlan.TryBuild(new GraphState(nodes, edgesB), RegistryWith("test.nodes.source", "test.nodes.mix"), null, out var planB, out var diagnosticB), Is.True, diagnosticB?.Message);
			Assert.That(planA.EvaluationOrder, Is.EqualTo(planB.EvaluationOrder));
		}

		[Test]
		[Category("GRAPH_08")]
		public void GraphEditor_UndoRedo_RestoresAtomicPatches() {
			var source = Node("source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var editor = new GraphEditor(ProgramGraph(source), RegistryWith("test.nodes.source", "test.nodes.extra"));

			Assert.That(editor.ApplyBatch(new[] { new AddNodeEditCommand(Node("extra", "test.nodes.extra", Out("image", PortType.ImageFrame))) }).IsSuccess, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId("extra")), Is.Not.Null);
			var appliedRevision = editor.State.Revision;
			Assert.That(editor.Undo().IsSuccess, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId("extra")), Is.Null);
			Assert.That(appliedRevision, Is.LessThan(editor.State.Revision));
			var undoneRevision = editor.State.Revision;
			Assert.That(editor.Redo().IsSuccess, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId("extra")), Is.Not.Null);
			Assert.That(undoneRevision, Is.LessThan(editor.State.Revision));
		}

		[Test]
		[Category("GRAPH_06")]
		public void RestoreUnknown_UsesCurrentCatalogAndPreservesStableNodeId() {
			var unknownType = new NodeTypeId("test.nodes.restored");
			var metadata = new UnknownNodeRecord(unknownType, 1, "{\"future\":true}");
			var placeholder = new NodeRecord(new NodeInstanceId("future"), new NodeTypeId(GraphConstants.UnknownNodeTypeId), 1, "Unknown", true,
				new ProjectPosition(3, 4), ports: new[] { Out("image", PortType.ImageFrame) }, rawState: metadata.RawJsonValue, unknown: metadata);
			var registry = RegistryWith("test.nodes.restored");
			var editor = new GraphEditor(ProgramGraph(placeholder), registry);

			var result = editor.ApplyBatch(new[] { new RestoreUnknownNodeEditCommand(placeholder.Id, metadata) });

			Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
			var restored = editor.State.FindNode(placeholder.Id);
			Assert.That(restored.TypeId, Is.EqualTo(unknownType));
			Assert.That(restored.IsUnknown, Is.False);
			Assert.That(restored.RawState, Is.EqualTo(metadata.RawJsonValue));
		}

		[Test]
		[Category("GRAPH_10")]
		[Category("GRAPH_06")]
		public void SavedTypeMismatchWithoutRegisteredConversion_IsReclassifiedBroken() {
			var source = Node("source", "test.nodes.source", Out("color", PortType.Color));
			var destination = Node("destination", "test.nodes.destination", RequiredIn("vector", PortType.Vector4));
			var state = ProgramGraph(source, destination);
			var edge = Connection("saved", "source", "color", "destination", "vector");
			state = new GraphState(state.Nodes, new[] { edge });

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source", "test.nodes.destination"), null, out var plan, out var diagnostic, out var normalized), Is.True, diagnostic?.Message);
			Assert.That(state.FindConnection(edge.Id).IsBroken, Is.False);
			Assert.That(normalized.FindConnection(edge.Id).IsBroken, Is.True);
			Assert.That(normalized.FindConnection(edge.Id).BrokenReason, Does.Contain("conversion"));
		}

		[Test]
		[Category("GRAPH_10")]
		public void SavedTypeMismatchWithUnregisteredConversion_IsReclassifiedBroken() {
			var source = Node("source", "test.nodes.source", Out("color", PortType.Color));
			var destination = Node("destination", "test.nodes.destination", RequiredIn("vector", PortType.Vector4));
			var state = ProgramGraph(source, destination);
			var edge = Connection("saved", "source", "color", "destination", "vector", "vendor.color_to_vector4.v9");
			state = new GraphState(state.Nodes, new[] { edge });

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source", "test.nodes.destination"), null, out _, out var diagnostic, out var normalized), Is.True, diagnostic?.Message);
			Assert.That(state.FindConnection(edge.Id).IsBroken, Is.False);
			Assert.That(normalized.FindConnection(edge.Id).IsBroken, Is.True);
		}

		[Test]
		[Category("GRAPH_08")]
		public void RestoreBatch_DuplicateNodeAndConnectionIds_IsRejectedAtomically() {
			var registry = RegistryWith("test.nodes.extra");
			var workspace = new GraphBatchWorkspace(ProgramGraph(), registry);
			var node = Node("duplicate", "test.nodes.extra", Out("image", PortType.ImageFrame));
			var result = workspace.Apply(new RestoreNodesEditCommand(
				new[] { node, node },
				Array.Empty<ConnectionRecord>()));

			Assert.That(result.IsFailure, Is.True);
			Assert.That(workspace.State.FindNode(node.Id), Is.Null);
		}

		[Test]
		[Category("GRAPH_06")]
		public void RestoreUnknown_MetadataMismatch_IsRejectedAndPlaceholderRetained() {
			var originalType = new NodeTypeId("test.nodes.restored");
			var preserved = new UnknownNodeRecord(originalType, 1, "{\"future\":true}");
			var mismatched = new UnknownNodeRecord(originalType, 2, "{\"future\":true}");
			var placeholder = new NodeRecord(new NodeInstanceId("future"), new NodeTypeId(GraphConstants.UnknownNodeTypeId), 1, "Unknown", true,
				new ProjectPosition(3, 4), ports: new[] { Out("image", PortType.ImageFrame) }, rawState: preserved.RawJsonValue, unknown: preserved);
			var editor = new GraphEditor(ProgramGraph(placeholder), RegistryWith("test.nodes.restored"));

			var result = editor.ApplyBatch(new[] { new RestoreUnknownNodeEditCommand(placeholder.Id, mismatched) });

			Assert.That(result.IsFailure, Is.True);
			Assert.That(editor.State.FindNode(placeholder.Id).IsUnknown, Is.True);
			Assert.That(editor.UndoCount, Is.EqualTo(0));
		}

		[Test]
		[Category("GRAPH_09")]
		public void ProgramOutput_CannotBeDisabled() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith());
			var result = editor.ApplyBatch(new[] { new SetNodeEnabledEditCommand(new NodeInstanceId(GraphConstants.ProgramOutputTypeId), false) });

			Assert.That(result.IsFailure, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId(GraphConstants.ProgramOutputTypeId)).Enabled, Is.True);
		}

		[Test]
		[Category("GRAPH_02")]
		[Category("GRAPH_04")]
		public void Demand_MergesResolutionAndUsesProgramAspectPriority() {
			var preview = Node("preview", GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame));
			var state = ProgramGraph(preview);
			var demands = new[]
			{
				new OutputDemand(OutputTargetKind.Program, new NodeInstanceId(GraphConstants.ProgramOutputTypeId), Image, 1920, 1080),
				new OutputDemand(OutputTargetKind.Preview, new NodeInstanceId("preview"), Image, 640, 360, focused: true, focusTimestamp: 12)
			};

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith(), demands, out var plan, out var diagnostic), Is.True, diagnostic?.Message);
			Assert.That(plan.MergedDemands.Count, Is.EqualTo(2));
			Assert.That(plan.ProgramAspectRatio, Is.EqualTo(16d / 9d).Within(0.0001));
			Assert.That(plan.MergedDemands.First(x => x.TargetKind == OutputTargetKind.Program).Width, Is.EqualTo(1920));
		}

		[Test]
		[Category("GRAPH_03")]
		public void Demand_RejectsNinthPreview() {
			var nodes = new List<NodeRecord>();
			var demands = new List<OutputDemand>();
			for (var i = 0; i < 9; i++) {
				var id = $"preview-{i:D2}";
				nodes.Add(Node(id, GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame)));
				demands.Add(new OutputDemand(OutputTargetKind.Preview, new NodeInstanceId(id), Image, 320, 180));
			}
			demands.Add(new OutputDemand(OutputTargetKind.Program, new NodeInstanceId(GraphConstants.ProgramOutputTypeId), Image, 1920, 1080));

			var state = ProgramGraph(nodes.ToArray());
			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith(), demands, out _, out var diagnostic), Is.False);
			Assert.That(diagnostic.Code.Value, Is.EqualTo("graph.plan.preview_limit"));
		}

		[Test]
		[Category("GRAPH_02")]
		public void Demand_ResolutionPropagation_MergesSharedUpstreamDeterministically() {
			var source = Node("shared-source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var first = Node("preview-a", GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame));
			var second = Node("preview-b", GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame));
			var state = new GraphState(
				new[] { Node(GraphConstants.ProgramOutputTypeId, GraphConstants.ProgramOutputTypeId, RequiredIn("image", PortType.ImageFrame)), source, first, second },
				new[] { Connection("a", "shared-source", "image", "preview-a", "image"), Connection("b", "shared-source", "image", "preview-b", "image") });
			var demands = new[]
			{
				new OutputDemand(OutputTargetKind.Preview, new NodeInstanceId("preview-a"), Image, 320, 180, focused: false),
				new OutputDemand(OutputTargetKind.Preview, new NodeInstanceId("preview-b"), Image, 640, 360, focused: true, focusTimestamp: 4)
			};
			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"), demands, out var planA, out var diagnosticA), Is.True, diagnosticA?.Message);
			Assert.That(planA.RequiredOutputResolutions[new NodeInstanceId("shared-source")][Image].Width, Is.EqualTo(640));
			Assert.That(planA.RequiredOutputResolutions[new NodeInstanceId("shared-source")][Image].Height, Is.EqualTo(360));

			var shuffled = demands.Reverse().ToArray();
			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"), shuffled, out var planB, out var diagnosticB), Is.True, diagnosticB?.Message);
			var a = planA.RequiredOutputResolutions[new NodeInstanceId("shared-source")][Image];
			var b = planB.RequiredOutputResolutions[new NodeInstanceId("shared-source")][Image];
			Assert.That(new[] { (double)a.Width, (double)a.Height, a.AspectRatio, a.Focused ? 1d : 0d, (double)a.FocusTimestamp }, Is.EqualTo(new[] { (double)b.Width, (double)b.Height, b.AspectRatio, b.Focused ? 1d : 0d, (double)b.FocusTimestamp }));
		}

		[Test]
		[Category("GRAPH_02")]
		public void EvaluationPlan_ExposesStableExecutionIndexesForEveryOrderedNode() {
			var source = Node("indexed-source", "test.nodes.source", Out("image", PortType.ImageFrame));
			var preview = Node("indexed-preview", GraphConstants.PreviewTypeId, RequiredIn("image", PortType.ImageFrame));
			var state = new GraphState(
				new[] { Node(GraphConstants.ProgramOutputTypeId, GraphConstants.ProgramOutputTypeId, RequiredIn("image", PortType.ImageFrame)), source, preview },
				new[] { Connection("indexed-link", "indexed-source", "image", "indexed-preview", "image") });

			Assert.That(EvaluationPlan.TryBuild(state, RegistryWith("test.nodes.source"),
				new[] { new OutputDemand(OutputTargetKind.Preview, preview.Id, Image, 320, 180) }, out var plan, out var diagnostic), Is.True, diagnostic?.Message);

			for (var index = 0; index < plan.RequiredNodeIds.Count; index++) {
				Assert.That(plan.TryGetEvaluationIndex(plan.RequiredNodeIds[index], out var actual), Is.True);
				Assert.That(actual, Is.EqualTo(index));
				Assert.That(plan.EvaluationIndices[plan.RequiredNodeIds[index]], Is.EqualTo(index));
			}
			Assert.That(plan.TryGetEvaluationIndex(new NodeInstanceId("absent"), out _), Is.False);
		}

		[Test]
		[Category("GRAPH_08")]
		public void UndoHistory_IsCappedAtTwoHundredGraphPatches() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith("test.nodes.extra"));
			for (var i = 0; i < GraphConstants.MaxUndoEntries + 1; i++) {
				var result = editor.ApplyBatch(new[] { new AddNodeEditCommand(Node($"extra-{i:D3}", "test.nodes.extra", Out("image", PortType.ImageFrame))) });
				Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
			}

			Assert.That(editor.UndoCount, Is.EqualTo(GraphConstants.MaxUndoEntries));
			Assert.That(editor.Undo().IsSuccess, Is.True);
			Assert.That(editor.Redo().IsSuccess, Is.True);
		}

		[Test]
		[Category("GRAPH_08")]
		public void PrepareBatch_PersistenceFailureLeavesStateRevisionAndFullHistoryUnchanged() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith("test.nodes.extra"));
			for (var i = 0; i < GraphConstants.MaxUndoEntries + 1; i++)
				Assert.That(editor.ApplyBatch(new[] { new AddNodeEditCommand(Node($"history-{i:D3}", "test.nodes.extra", Out("image", PortType.ImageFrame))) }).IsSuccess, Is.True);

			var before = editor.State;
			var beforeIds = before.Nodes.Select(x => x.Id).ToList();
			var beforeUndo = editor.UndoCount;
			var beforeRedo = editor.RedoCount;
			var candidate = editor.PrepareBatchDetailed(new[] { new AddNodeEditCommand(Node("persisted-later", "test.nodes.extra", Out("image", PortType.ImageFrame))) });

			// Simulate the failed persistence half by deliberately not calling
			// CommitCandidate. Preparation must be entirely non-destructive,
			// including the oldest evicted-history boundary.
			Assert.That(candidate.Patch, Is.Not.Null);
			Assert.That(candidate.IsCommitted, Is.False);
			Assert.That(editor.State.Revision, Is.EqualTo(before.Revision));
			Assert.That(editor.State.Nodes.Select(x => x.Id), Is.EqualTo(beforeIds));
			Assert.That(editor.UndoCount, Is.EqualTo(beforeUndo));
			Assert.That(editor.RedoCount, Is.EqualTo(beforeRedo));

			var redoEditor = new GraphEditor(ProgramGraph(), RegistryWith("test.nodes.extra"));
			Assert.That(redoEditor.ApplyBatch(new[] { new AddNodeEditCommand(Node("redo-source", "test.nodes.extra", Out("image", PortType.ImageFrame))) }).IsSuccess, Is.True);
			Assert.That(redoEditor.Undo().IsSuccess, Is.True);
			var redoBefore = redoEditor.RedoCount;
			var redoCandidate = redoEditor.PrepareBatchDetailed(new[] { new AddNodeEditCommand(Node("redo-later", "test.nodes.extra", Out("image", PortType.ImageFrame))) });
			Assert.That(redoCandidate.Patch, Is.Not.Null);
			Assert.That(redoEditor.RedoCount, Is.EqualTo(redoBefore));
		}

		[Test]
		[Category("GRAPH_04")]
		public void ApplyBatchDetailed_RejectsOneCommandAndContinuesIndependentCommands() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith("test.nodes.extra"));
			var first = new AddNodeEditCommand(Node("extra-a", "test.nodes.extra", Out("image", PortType.ImageFrame)));
			var rejected = new AddNodeEditCommand(Node(GraphConstants.ProgramOutputTypeId, "test.nodes.extra", Out("image", PortType.ImageFrame)));
			var last = new AddNodeEditCommand(Node("extra-b", "test.nodes.extra", Out("image", PortType.ImageFrame)));

			var result = editor.ApplyBatchDetailed(new GraphEditCommand[] { first, rejected, last });

			Assert.That(result.IsCommitted, Is.True);
			Assert.That(result.CommandResults.Count, Is.EqualTo(3));
			Assert.That(result.CommandResults[0].IsSuccess, Is.True);
			Assert.That(result.CommandResults[1].IsFailure, Is.True);
			Assert.That(result.CommandResults[2].IsSuccess, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId("extra-a")), Is.Not.Null);
			Assert.That(editor.State.FindNode(new NodeInstanceId("extra-b")), Is.Not.Null);
			Assert.That(editor.UndoCount, Is.EqualTo(1));
		}

		[Test]
		[Category("GRAPH_04")]
		public void ApplyBatchDetailed_AllCommandsRejected_LeavesStateRevisionAndHistoryUnchanged() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith());
			var before = editor.State;

			var result = editor.ApplyBatchDetailed(new GraphEditCommand[]
			{
				new SetNodeEnabledEditCommand(new NodeInstanceId("missing"), false),
				new DeleteNodeEditCommand(new NodeInstanceId("missing"))
			});

			Assert.That(result.IsCommitted, Is.False);
			Assert.That(result.CommandResults.All(x => x.IsFailure), Is.True);
			Assert.That(editor.State.Revision, Is.EqualTo(before.Revision));
			Assert.That(editor.State.Nodes.Select(x => x.Id), Is.EqualTo(before.Nodes.Select(x => x.Id)));
			Assert.That(editor.UndoCount, Is.EqualTo(0));
		}

		[Test]
		[Category("GRAPH_04")]
		public void ApplyBatchDetailed_FailureBetweenSuccesses_CommitsOnlySuccessfulCommandsInOrder() {
			var editor = new GraphEditor(ProgramGraph(), RegistryWith("test.nodes.extra"));
			var result = editor.ApplyBatchDetailed(new GraphEditCommand[]
			{
				new AddNodeEditCommand(Node("before-failure", "test.nodes.extra", Out("image", PortType.ImageFrame))),
				new ConnectEditCommand(Connection("missing-endpoint", "missing", "image", GraphConstants.ProgramOutputTypeId, "image")),
				new AddNodeEditCommand(Node("after-failure", "test.nodes.extra", Out("image", PortType.ImageFrame)))
			});

			Assert.That(result.IsCommitted, Is.True);
			Assert.That(result.CommandResults[1].IsFailure, Is.True);
			Assert.That(editor.State.FindNode(new NodeInstanceId("before-failure")), Is.Not.Null);
			Assert.That(editor.State.FindNode(new NodeInstanceId("after-failure")), Is.Not.Null);
			Assert.That(editor.State.FindConnection(new ConnectionId("missing-endpoint")), Is.Null);
		}
	}
}
