using System;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Project;

namespace ShitDesigner.Tests.Project {
	public sealed class ProjectContractTests {
		private sealed class FixedProjectIdFactory : IProjectIdFactory {
			private readonly NodeInstanceId _id;
			public FixedProjectIdFactory(NodeInstanceId id) { _id = id; }
			public NodeInstanceId NewNodeInstanceId() => _id;
		}

		private static readonly string NodeAValue = "11111111-1111-4111-8111-111111111111";
		private static readonly string NodeBValue = "22222222-2222-4222-8222-222222222222";
		private static readonly string SourceValue = "33333333-3333-4333-8333-333333333333";
		private static readonly string DestinationValue = "44444444-4444-4444-8444-444444444444";
		private static readonly string UnknownValue = "55555555-5555-4555-8555-555555555555";
		private static readonly string MissingValue = "66666666-6666-4666-8666-666666666666";
		private static NodeInstanceId NodeId(string value) {
			if (value == "node_a") return new NodeInstanceId(NodeAValue);
			if (value == "node_b") return new NodeInstanceId(NodeBValue);
			if (value == "source") return new NodeInstanceId(SourceValue);
			if (value == "dest") return new NodeInstanceId(DestinationValue);
			if (value == "unknown") return new NodeInstanceId(UnknownValue);
			if (value == "missing") return new NodeInstanceId(MissingValue);
			if (value.StartsWith("node_", StringComparison.Ordinal) && int.TryParse(value.Substring(5), out var index)) return new NodeInstanceId($"{index + 1:00000000}-0000-4000-8000-000000000000");
			return new NodeInstanceId(value);
		}
		private static NodeRecord Node(string id = "node_a", bool systemOwned = false) {
			var definition = new ParameterDefinition(new ParameterId("mix.amount"), "Amount", ParameterType.Float, ParameterValue.FromFloat(.5f), new ParameterRange(ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)));
			return new NodeRecord(NodeId(id), new NodeTypeId("shitdesigner.test.node"), 1, id, true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, definition.DefaultValue) }, new[] { new PortSnapshotRecord(new PortId("output"), PortDirection.Output, PortType.Float, false), new PortSnapshotRecord(new PortId("input"), PortDirection.Input, PortType.Float, true) }, "{\"state\":1}", systemOwned, !systemOwned);
		}

		private static NodeRecord MediaNode(string id, MediaAssetId assetId) {
			var definition = new ParameterDefinition(new ParameterId("media.clip"), "Clip", ParameterType.MediaAssetReference, ParameterValue.FromMediaAsset(assetId));
			return new NodeRecord(NodeId(id), new NodeTypeId("shitdesigner.test.media.node"), 1, id, true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, definition.DefaultValue) });
		}

		private static NodeRecord ProgramNode(string id, string displayName = "Image", bool enabled = true, bool systemOwned = true, bool userAddable = false, string rawState = "{}") {
			return new NodeRecord(NodeId(id), new NodeTypeId("system.program_output"), 1, displayName, enabled, new ProjectPosition(8, 9),
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				rawState: rawState, systemOwned: systemOwned, userAddable: userAddable);
		}

		[Test]
		public void PreviewNormalization_PreservesInstanceTitleAndPersistedDisplayMode() {
			var mode = new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
				enumOptionIds: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
			var preview = new NodeRecord(NodeId("node_a"), new NodeTypeId("system.preview"), 1, "Camera A", true, new ProjectPosition(2, 3),
				parameters: new[] { new ParameterRecord(mode, ParameterValue.FromEnum("fill")) },
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				systemOwned: false, userAddable: true);

			var created = ProjectDocumentFactory.TryCreateDetailed("Preview", 1, new[] { preview, ProgramNode("node_b") }, Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(), Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>(), new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") }));

			Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
			Assert.That(created.Value.WasRepaired, Is.False);
			var preserved = created.Value.Document.FindNode(preview.Id);
			Assert.That(preserved.DisplayName, Is.EqualTo("Camera A"));
			Assert.That(preserved.FindParameter(new ParameterId("display.mode")).BaseValue.AsString(), Is.EqualTo("fill"));
		}

		[Test]
		public void PreviewNormalization_RepairsMalformedShapeButRetainsValidDisplayModeAndTitle() {
			var mode = new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
				enumOptionIds: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
			var extra = new ParameterDefinition(new ParameterId("unexpected"), "Unexpected", ParameterType.Bool, ParameterValue.FromBool(false));
			var preview = new NodeRecord(NodeId("node_a"), new NodeTypeId("system.preview"), 1, "Camera A", true, new ProjectPosition(2, 3),
				parameters: new[] { new ParameterRecord(mode, ParameterValue.FromEnum("stretch")), new ParameterRecord(extra, extra.DefaultValue) },
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				systemOwned: false, userAddable: true);

			var created = ProjectDocumentFactory.TryCreateDetailed("Preview", 1, new[] { preview }, Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(), Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>());

			Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
			Assert.That(created.Value.WasRepaired, Is.True);
			var repaired = created.Value.Document.FindNode(preview.Id);
			Assert.That(repaired.DisplayName, Is.EqualTo("Camera A"));
			Assert.That(repaired.Parameters.Select(x => x.Definition.Id.Value), Is.EqualTo(new[] { "display.mode" }));
			Assert.That(repaired.FindParameter(new ParameterId("display.mode")).BaseValue.AsString(), Is.EqualTo("stretch"));
		}

		[TestCase("")]
		[TestCase("unknown")]
		public void PreviewNormalization_RepairsInvalidDisplayModeToFit(string invalidMode) {
			var mode = new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
				enumOptionIds: invalidMode == "unknown"
					? new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch"), new ParameterId("unknown") }
					: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
			var preview = new NodeRecord(NodeId("node_a"), new NodeTypeId("system.preview"), 1, "Camera A", true, new ProjectPosition(2, 3),
				parameters: new[] { new ParameterRecord(mode, ParameterValue.FromEnum(invalidMode)) },
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				systemOwned: false, userAddable: true);

			var created = ProjectDocumentFactory.TryCreateDetailed("Preview", 1, new[] { preview }, Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(), Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>());

			Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
			Assert.That(created.Value.WasRepaired, Is.True);
			var repaired = created.Value.Document.FindNode(preview.Id);
			Assert.That(repaired.DisplayName, Is.EqualTo("Camera A"));
			Assert.That(repaired.FindParameter(new ParameterId("display.mode")).BaseValue.AsString(), Is.EqualTo("fit"));
		}

		[Test]
		public void CommandProcessor_ChangesOnlyThroughCommandsAndRevisionIsMonotonic() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			var initialRevision = document.DocumentRevision;
			Assert.That(processor.AddNode(Node()).IsSuccess, Is.True);
			Assert.That(document.DocumentRevision, Is.GreaterThan(initialRevision));
			Assert.That(document.Nodes.Count, Is.EqualTo(1));
			Assert.That(document.IsDirty, Is.True);
		}

		[Test]
		public void SaveAndUndo_ToSavedStateClearsDirty() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			processor.AddNode(Node());
			document.BeginSave();
			document.MarkSaved();
			Assert.That(document.IsDirty, Is.False);
			processor.DeleteNode(NodeId("node_a"));
			Assert.That(document.IsDirty, Is.True);
			processor.Undo();
			Assert.That(document.IsDirty, Is.False);
		}

		[Test]
		public void Undo_ToStateBeforeLastSave_DoesNotRollbackSavedTokenAndRemainsDirty() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			processor.AddNode(Node());
			document.BeginSave();
			document.MarkSaved();
			var savedToken = document.SavedToken;
			processor.SetBaseValue(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.8f));
			processor.Undo();
			Assert.That(document.SavedToken, Is.EqualTo(savedToken));
			Assert.That(document.IsDirty, Is.False);
			processor.SetBaseValue(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.8f));
			processor.Undo();
			processor.Undo();
			Assert.That(document.SavedToken, Is.EqualTo(savedToken));
			Assert.That(document.IsDirty, Is.True);
		}

		[Test]
		public void SaveInFlight_AdditionalEditKeepsDirtyAfterCompletion() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			processor.AddNode(Node());
			document.BeginSave();
			processor.SetBaseValue(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.9f));
			document.MarkSaved();
			Assert.That(document.IsDirty, Is.True);
		}

		[Test]
		public void UndoRedo_AreCappedAt200Entries() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			for (var i = 0; i < 205; i++) processor.AddNode(Node("node_" + i));
			Assert.That(processor.UndoCount, Is.EqualTo(200));
			for (var i = 0; i < 200; i++) Assert.That(processor.Undo().IsSuccess, Is.True);
			Assert.That(document.Nodes.Count, Is.EqualTo(5));
			Assert.That(processor.Undo().IsFailure, Is.True);
		}

		[Test]
		public void Connect_InvalidReplacementKeepsExistingConnection() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document);
			processor.AddNode(Node("source")); processor.AddNode(Node("dest"));
			var first = new ConnectionRecord(new ConnectionId("connection_a"), NodeId("source"), new PortId("output"), NodeId("dest"), new PortId("input"));
			Assert.That(processor.Connect(first).IsSuccess, Is.True);
			var invalid = new ConnectionRecord(new ConnectionId("connection_b"), NodeId("source"), new PortId("output"), NodeId("dest"), new PortId("missing"));
			Assert.That(processor.Connect(invalid).IsFailure, Is.True);
			Assert.That(document.Connections.Single().Id, Is.EqualTo(new ConnectionId("connection_a")));
		}

		[Test]
		public void Preset_BrokenItemRejectsWholeTransactionAndRangeOutOfBoundsClamps() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); processor.AddNode(Node());
			var broken = new PresetRecord(PresetId.New(), "Broken", entries: new[] { new PresetEntryRecord(NodeId("missing"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(.2f), true, "missing") });
			processor.AddPreset(broken);
			Assert.That(processor.ApplyPreset(broken.Id).IsFailure, Is.True);
			Assert.That(document.FindNode(NodeId("node_a")).FindParameter(new ParameterId("mix.amount")).BaseValue.AsFloat(), Is.EqualTo(.5f));
			var clamped = new PresetRecord(PresetId.New(), "Clamped", entries: new[] { new PresetEntryRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(2)) });
			processor.AddPreset(clamped);
			Assert.That(processor.ApplyPreset(clamped.Id).IsSuccess, Is.True);
			Assert.That(document.FindNode(NodeId("node_a")).FindParameter(new ParameterId("mix.amount")).BaseValue.AsFloat(), Is.EqualTo(1));
		}

		[Test]
		public void LogicalControl_TargetMappingNormalizesAndRejectsUnsupportedTypes() {
			var target = new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(2), ParameterValue.FromFloat(4), true);
			Assert.That(target.Map(0).Value.AsFloat(), Is.EqualTo(4));
			Assert.That(target.Map(.5f).Value.AsFloat(), Is.EqualTo(3));
			Assert.Throws<ArgumentException>(() => new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("label"), ParameterType.String, ParameterValue.FromString("a"), ParameterValue.FromString("z")));
		}

		[Test]
		public void UnknownNode_RawStateAndBrokenReferencesAreRetained() {
			var unknown = new UnknownNodeRecord(new NodeTypeId("vendor.future.node"), 3, "{  \"future\":  true }\n");
			var node = new NodeRecord(NodeId("unknown"), new NodeTypeId("system.unknown_node"), 1, "Unknown", true, new ProjectPosition(12, 34), ports: new[] { new PortSnapshotRecord(new PortId("future"), PortDirection.Output, PortType.ImageFrame, false) }, rawState: unknown.RawJsonValue, unknown: unknown);
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); Assert.That(processor.AddNode(node).IsSuccess, Is.True);
			Assert.That(document.Nodes[0].Unknown.RawJsonValue, Is.EqualTo(unknown.RawJsonValue));
			Assert.That(document.Nodes[0].Unknown.OriginalNodeTypeId, Is.EqualTo(new NodeTypeId("vendor.future.node")));
			Assert.That(document.Nodes[0].Position, Is.EqualTo(new ProjectPosition(12, 34)));
			Assert.That(document.Nodes[0].Ports.Count, Is.EqualTo(1));
		}

		[Test]
		public void CommandBoundary_RejectsNonUuidDomainIds() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document);
			var nonUuidNode = new NodeRecord(new NodeInstanceId("node_non_uuid"), new NodeTypeId("shitdesigner.test.node"), 1, "Bad", true, new ProjectPosition(0, 0));
			Assert.That(processor.AddNode(nonUuidNode).IsFailure, Is.True);
			Assert.That(processor.AddLogicalControl(new LogicalControlRecord(new LogicalControlId("control_non_uuid"), "Bad", LogicalControlKind.Value)).IsFailure, Is.True);
			Assert.That(processor.AddPreset(new PresetRecord(new PresetId("preset_non_uuid"), "Bad")).IsFailure, Is.True);
		}

		[Test]
		public void NodeAndUnknownContracts_RejectDuplicatesAndInvalidPlaceholderData() {
			var definition = new ParameterDefinition(new ParameterId("mix.amount"), "Amount", ParameterType.Float, ParameterValue.FromFloat(.5f));
			Assert.Throws<ArgumentException>(() => new NodeRecord(NodeId("node_b"), new NodeTypeId("shitdesigner.test.node"), 1, "Duplicate", true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, definition.DefaultValue), new ParameterRecord(definition, definition.DefaultValue) }));
			Assert.Throws<ArgumentException>(() => new NodeRecord(NodeId("node_b"), new NodeTypeId("shitdesigner.test.node"), 1, "Duplicate", true, new ProjectPosition(0, 0), ports: new[] { new PortSnapshotRecord(new PortId("input"), PortDirection.Input, PortType.Float, false), new PortSnapshotRecord(new PortId("input"), PortDirection.Input, PortType.Float, false) }));
			Assert.Throws<ArgumentException>(() => new UnknownNodeRecord(new NodeTypeId("system.unknown_node"), 1, "{}"));
			Assert.Throws<ArgumentException>(() => new NodeRecord(NodeId("node_b"), new NodeTypeId("system.unknown_node"), 1, "Missing", true, new ProjectPosition(0, 0)));
		}

		[Test]
		public void EnumDefinition_AllowsUnselectedAndRejectsUnregisteredOptions() {
			var definition = new ParameterDefinition(new ParameterId("mode.kind"), "Mode", ParameterType.Enum, ParameterValue.Default(ParameterType.Enum), enumOptionIds: new[] { new ParameterId("mode_a"), new ParameterId("mode_b") });
			Assert.That(definition.Clamp(ParameterValue.FromEnum(string.Empty)).IsSuccess, Is.True);
			Assert.That(definition.Clamp(ParameterValue.FromEnum("mode_a")).IsSuccess, Is.True);
			Assert.That(definition.Clamp(ParameterValue.FromEnum("mode_c")).IsFailure, Is.True);
			Assert.Throws<ArgumentException>(() => new ParameterDefinition(new ParameterId("mode.kind"), "Mode", ParameterType.Enum, ParameterValue.FromEnum("mode_c"), enumOptionIds: new[] { new ParameterId("mode_a") }));
		}

		[Test]
		[Category("P_ParameterDefinition")]
		public void ParameterDefinition_ClampsDefaultIntoHardRange() {
			var definition = new ParameterDefinition(new ParameterId("mix.amount"), "Amount", ParameterType.Float, ParameterValue.FromFloat(2), new ParameterRange(ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)));
			Assert.That(definition.DefaultValue.AsFloat(), Is.EqualTo(1));
			Assert.That(new ParameterRecord(definition, definition.DefaultValue).BaseValue.AsFloat(), Is.EqualTo(1));
		}

		[Test]
		[Category("P_ParameterDefinition")]
		public void EnumOptionCatalog_PreservesStableIdsAndDisplayNames() {
			var definition = new ParameterDefinition(new ParameterId("mode.kind"), "Mode", ParameterType.Enum, ParameterValue.FromEnum("mode_a"), null, false,
				new[] { new EnumOptionDefinition(new ParameterId("mode_a"), "Mode A"), new EnumOptionDefinition(new ParameterId("mode_b"), "Mode B") });
			Assert.That(definition.EnumOptionIds.Select(x => x.Value), Is.EqualTo(new[] { "mode_a", "mode_b" }));
			Assert.That(definition.EnumOptions[0].DisplayName, Is.EqualTo("Mode A"));
			Assert.That(definition.Clamp(ParameterValue.FromEnum("mode_b")).IsSuccess, Is.True);
			Assert.That(definition.Clamp(ParameterValue.FromEnum("mode_c")).IsFailure, Is.True);
		}

		[Test]
		[Category("P_LogicalControl")]
		public void ControlMapping_RejectsNonFinitePhysicalInput() {
			var mapping = new ControlMappingRecord(PhysicalInputKind.Keyboard, "keyboard", "space");
			Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Normalize(float.NaN));
			Assert.Throws<ArgumentOutOfRangeException>(() => mapping.Normalize(float.PositiveInfinity));
		}

		[Test]
		[Category("P_LogicalControl")]
		public void BrokenExpression_PreservesBinaryTreeForRebind() {
			var controlId = new LogicalControlId("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
			var original = new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"),
				new BinaryLogicalExpression(LogicalOperator.Max, new BaseValueLeaf(), new BinaryLogicalExpression(LogicalOperator.Min, new LogicalControlLeaf(controlId), new LogicalControlLeaf(controlId))));
			var broken = original.AsBroken("node deleted");
			var root = broken.Expression as BinaryLogicalExpression;
			Assert.That(root, Is.Not.Null);
			Assert.That(root.Operator, Is.EqualTo(LogicalOperator.Max));
			Assert.That(root.Left, Is.TypeOf<BaseValueLeaf>());
			Assert.That(root.Right, Is.TypeOf<BinaryLogicalExpression>());
			Assert.That(broken.IsBroken, Is.True);
			var repaired = broken.Revalidate(id => id == controlId);
			Assert.That(repaired.Expression, Is.TypeOf<BinaryLogicalExpression>());
			Assert.That(repaired.IsBroken, Is.False);
		}

		[Test]
		[Category("P_LogicalControl")]
		public void Expression_RejectsUnknownBinaryOperator() {
			var controlId = new LogicalControlId("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
			var expression = new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"),
				new BinaryLogicalExpression((LogicalOperator)999, new LogicalControlLeaf(controlId), new LogicalControlLeaf(controlId)));
			Assert.That(expression.Expression.IsComplete, Is.True);
			Assert.That(expression.IsValid, Is.False);
		}

		[Test]
		[Category("P_MediaAsset")]
		public void MediaAsset_RequiresDisplayName() {
			var id = MediaAssetId.New();
			Assert.Throws<ArgumentException>(() => new MediaAssetRecord(id, " ", "Assets/" + id.Value + "/source.png", 1, "0123456789abcdef0123456789abcdef"));
		}

		[Test]
		public void DeleteNode_RetainsBrokenReferencesAndRestoresThemByStableId() {
			var page = new DashboardPageRecord("page", "Page", new[] { new DashboardWidgetRecord("widget", NodeId("source"), new ParameterId("mix.amount")) });
			var document = new ProjectDocument("Test", 1, new ProjectUiStateRecord(new[] { page })); var processor = new ProjectCommandProcessor(document);
			Assert.That(processor.AddNode(Node("source")).IsSuccess, Is.True); Assert.That(processor.AddNode(Node("dest")).IsSuccess, Is.True);
			var connection = new ConnectionRecord(new ConnectionId("connection_a"), NodeId("source"), new PortId("output"), NodeId("dest"), new PortId("input")); Assert.That(processor.Connect(connection).IsSuccess, Is.True);
			var control = new LogicalControlRecord(new LogicalControlId("77777777-7777-4777-8777-777777777777"), "Control", LogicalControlKind.Value, targets: new[] { new LogicalControlTargetRecord(NodeId("source"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)) });
			Assert.That(processor.AddLogicalControl(control).IsSuccess, Is.True);
			var expression = new ParameterExpressionRecord(NodeId("source"), new ParameterId("mix.amount"), new LogicalControlLeaf(control.Id)); Assert.That(processor.AddExpression(expression).IsSuccess, Is.True);
			var preset = new PresetRecord(PresetId.New(), "Source", entries: new[] { new PresetEntryRecord(NodeId("source"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(.7f)) }); Assert.That(processor.AddPreset(preset).IsSuccess, Is.True);
			Assert.That(processor.DeleteNode(NodeId("source")).IsSuccess, Is.True);
			Assert.That(document.Connections[0].IsBroken, Is.True); Assert.That(document.LogicalControls[0].Targets[0].IsBroken, Is.True); Assert.That(document.Expressions[0].IsBroken, Is.True); Assert.That(document.Presets[0].Entries[0].IsBroken, Is.True); Assert.That(document.Ui.DashboardPages[0].Widgets[0].IsBroken, Is.True);
			Assert.That(processor.AddNode(Node("source")).IsSuccess, Is.True);
			Assert.That(document.Connections[0].IsBroken, Is.False); Assert.That(document.LogicalControls[0].Targets[0].IsBroken, Is.False); Assert.That(document.Expressions[0].IsBroken, Is.False); Assert.That(document.Presets[0].Entries[0].IsBroken, Is.False); Assert.That(document.Ui.DashboardPages[0].Widgets[0].IsBroken, Is.False);
		}

		[Test]
		public void DeleteLogicalControl_LeavesBrokenExpressionAndRevalidatesOnRestore() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); processor.AddNode(Node());
			var control = new LogicalControlRecord(new LogicalControlId("88888888-8888-4888-8888-888888888888"), "Control", LogicalControlKind.Value, targets: new[] { new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)) }); processor.AddLogicalControl(control);
			processor.AddExpression(new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), new LogicalControlLeaf(control.Id)));
			Assert.That(processor.DeleteLogicalControl(control.Id).IsSuccess, Is.True); Assert.That(document.Expressions[0].IsBroken, Is.True);
			Assert.That(processor.AddLogicalControl(control).IsSuccess, Is.True); Assert.That(document.Expressions[0].IsBroken, Is.False);
		}

		[Test]
		public void DeletePreset_LeavesBrokenTriggerAndRevalidatesOnRestore() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); var presetId = PresetId.New(); var preset = new PresetRecord(presetId, "One"); processor.AddPreset(preset);
			var trigger = new LogicalControlRecord(new LogicalControlId("99999999-9999-4999-8999-999999999999"), "Trigger", LogicalControlKind.PresetTrigger, presetId: presetId); Assert.That(processor.AddLogicalControl(trigger).IsSuccess, Is.True);
			Assert.That(processor.DeletePreset(presetId).IsSuccess, Is.True); Assert.That(document.LogicalControls[0].PresetIsBroken, Is.True);
			Assert.That(processor.AddPreset(new PresetRecord(presetId, "One")).IsSuccess, Is.True); Assert.That(document.LogicalControls[0].PresetIsBroken, Is.False);
		}

		[Test]
		public void DeleteMediaAsset_RetainsBrokenParameterAndPresetEntry() {
			var assetId = MediaAssetId.New(); var asset = new MediaAssetRecord(assetId, "Clip", "Assets/" + assetId.Value + "/source.png", 12, "0123456789abcdef0123456789abcdef", MediaAssetKind.Image, MediaColorSpace.SRgb, MediaAlphaMode.Straight);
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); Assert.That(processor.AddMediaAsset(asset).IsSuccess, Is.True); Assert.That(processor.AddNode(MediaNode("node_a", assetId)).IsSuccess, Is.True);
			var preset = new PresetRecord(PresetId.New(), "Clip", entries: new[] { new PresetEntryRecord(NodeId("node_a"), new ParameterId("media.clip"), ParameterType.MediaAssetReference, ParameterValue.FromMediaAsset(assetId)) }); Assert.That(processor.AddPreset(preset).IsSuccess, Is.True);
			Assert.That(processor.DeleteMediaAsset(assetId).IsSuccess, Is.True); Assert.That(document.Nodes[0].Parameters[0].IsBroken, Is.True); Assert.That(document.Presets[0].Entries[0].IsBroken, Is.True);
			Assert.That(processor.AddMediaAsset(asset).IsSuccess, Is.True); Assert.That(document.Nodes[0].Parameters[0].IsBroken, Is.False); Assert.That(document.Presets[0].Entries[0].IsBroken, Is.False);
		}

		[Test]
		public void LogicalControlKind_InvariantsRejectPresetOnValueAndNumbersOnTrigger() {
			var presetId = PresetId.New(); var target = new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(1));
			Assert.Throws<ArgumentException>(() => new LogicalControlRecord(new LogicalControlId("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "Value", LogicalControlKind.Value, presetId: presetId));
			Assert.Throws<ArgumentException>(() => new LogicalControlRecord(new LogicalControlId("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "Trigger", LogicalControlKind.PresetTrigger, initialValue: .1f));
			Assert.Throws<ArgumentException>(() => new LogicalControlRecord(new LogicalControlId("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), "Trigger", LogicalControlKind.PresetTrigger, targets: new[] { target }));
		}

		[Test]
		public void ExpressionValidation_RejectsInvalidDraftAndPreservesOldExpression() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); processor.AddNode(Node());
			var control = new LogicalControlRecord(new LogicalControlId("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), "Control", LogicalControlKind.Value, targets: new[] { new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)) }); processor.AddLogicalControl(control);
			var valid = new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), new LogicalControlLeaf(control.Id)); Assert.That(processor.AddExpression(valid).IsSuccess, Is.True);
			Assert.That(processor.AddExpression(new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), new BaseValueLeaf())).IsFailure, Is.True);
			Assert.That(document.Expressions.Count, Is.EqualTo(1));
			var tooManyBase = new BinaryLogicalExpression(LogicalOperator.Min, new BaseValueLeaf(), new BinaryLogicalExpression(LogicalOperator.Max, new BaseValueLeaf(), new LogicalControlLeaf(control.Id)));
			Assert.That(processor.AddExpression(new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), tooManyBase)).IsFailure, Is.True);
			var outputRange = new ParameterRange(ParameterValue.FromFloat(0), ParameterValue.FromFloat(2));
			Assert.That(processor.AddExpression(new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), new LogicalControlLeaf(control.Id), outputRange)).IsFailure, Is.True);
			Assert.That(document.Expressions.Count, Is.EqualTo(1));
		}

		[Test]
		public void PresetAndMediaContracts_RejectDuplicateEntriesAndUnsafeMetadata() {
			var nodeId = NodeId("node_a"); var entry = new PresetEntryRecord(nodeId, new ParameterId("mix.amount"), ParameterType.Float, ParameterValue.FromFloat(.1f));
			Assert.Throws<ArgumentException>(() => new PresetRecord(PresetId.New(), "Duplicate", entries: new[] { entry, entry }));
			var badId = new MediaAssetId("not-a-uuid"); Assert.Throws<ArgumentException>(() => new MediaAssetRecord(badId, "Bad", "Assets/not-a-uuid/source.png", 1, "0123456789abcdef0123456789abcdef"));
			var goodId = MediaAssetId.New(); Assert.Throws<ArgumentException>(() => new MediaAssetRecord(goodId, "Bad", "Assets/" + goodId.Value + "/../source.png", 1, "0123456789abcdef0123456789abcdef"));
			Assert.Throws<ArgumentException>(() => new MediaAssetRecord(goodId, "Bad", "Assets/" + goodId.Value + "/source.png", 1, "0123456789ABCDEF0123456789abcdef"));
		}

		[Test]
		public void SaveCompletion_RejectsStaleSavingGeneration() {
			var document = new ProjectDocument("Test"); var processor = new ProjectCommandProcessor(document); processor.AddNode(Node()); var first = document.BeginSave(); processor.SetBaseValue(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.9f)); var second = document.BeginSave();
			Assert.That(document.TryMarkSaved(first), Is.False); Assert.That(document.SavedToken == first, Is.False); Assert.That(document.TryMarkSaved(second), Is.True); Assert.That(document.SavedToken, Is.EqualTo(second));
		}

		[Test]
		[Category("P_ParameterUpdate")]
		public void BaseValueBatch_IsAtomicAndCreatesOneHistoryEntry() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			processor.AddNode(Node());
			var beforeRevision = document.DocumentRevision;
			var beforeValue = document.FindNode(NodeId("node_a")).FindParameter(new ParameterId("mix.amount")).BaseValue;
			var invalid = processor.ApplyBaseValues(new[]
			{
				new BaseValueUpdate(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.9f)),
				new BaseValueUpdate(NodeId("missing"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.2f))
			});
			Assert.That(invalid.IsFailure, Is.True);
			Assert.That(document.DocumentRevision, Is.EqualTo(beforeRevision));
			Assert.That(document.FindNode(NodeId("node_a")).FindParameter(new ParameterId("mix.amount")).BaseValue, Is.EqualTo(beforeValue));
			var valid = processor.ApplyBaseValues(new[] { new BaseValueUpdate(NodeId("node_a"), new ParameterId("mix.amount"), ParameterValue.FromFloat(.9f)) });
			Assert.That(valid.IsSuccess, Is.True);
			Assert.That(document.DocumentRevision, Is.EqualTo(beforeRevision + 1));
			Assert.That(processor.UndoCount, Is.EqualTo(2));
			Assert.That(document.FindNode(NodeId("node_a")).FindParameter(new ParameterId("mix.amount")).BaseValue.AsFloat(), Is.EqualTo(.9f));
		}

		[Test]
		[Category("P_GraphCommit")]
		public void GraphStateCommit_IsAtomicAndRetainsOneProjectHistoryEntry() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			var connection = new ConnectionRecord(new ConnectionId("connection_a"), NodeId("source"), new PortId("output"), NodeId("dest"), new PortId("input"));
			var committed = processor.CommitGraphState(new[] { Node("source"), Node("dest") }, new[] { connection });
			Assert.That(committed.IsSuccess, Is.True);
			Assert.That(document.Nodes.Count, Is.EqualTo(2));
			Assert.That(document.Connections.Count, Is.EqualTo(1));
			Assert.That(processor.UndoCount, Is.EqualTo(1));
			var revision = document.DocumentRevision;
			var rejected = processor.CommitGraphState(new[] { Node("source"), Node("source") }, new ConnectionRecord[0]);
			Assert.That(rejected.IsFailure, Is.True);
			Assert.That(document.DocumentRevision, Is.EqualTo(revision));
			Assert.That(document.Connections.Count, Is.EqualTo(1));
		}

		[Test]
		[Category("P_GraphRepair")]
		public void GraphRepair_AdvancesRevisionWithoutEnteringUserUndoHistory() {
			var document = new ProjectDocument("Test");
			var processor = new ProjectCommandProcessor(document);
			var connection = new ConnectionRecord(new ConnectionId("connection_broken"), NodeId("source"), new PortId("output"), NodeId("dest"), new PortId("input"), "vendor.removed.v1", true, "Saved conversion is no longer registered.");
			var repaired = processor.CommitGraphRepair(new[] { Node("source"), Node("dest") }, new[] { connection });

			Assert.That(repaired.IsSuccess, Is.True);
			Assert.That(document.DocumentRevision, Is.EqualTo(1));
			Assert.That(document.IsDirty, Is.True);
			Assert.That(processor.UndoCount, Is.EqualTo(0));
			Assert.That(document.Connections.Single().IsBroken, Is.True);
		}

		[Test]
		[Category("P_ProjectSettings")]
		public void ProjectSettings_DefaultsToHdrAndDisplayTwo() {
			var document = new ProjectDocument("Test");
			Assert.That(document.Settings.DynamicRange, Is.EqualTo(ProjectDynamicRange.Hdr));
			Assert.That(document.Settings.ProgramDisplay, Is.EqualTo(ProjectOutputSettings.DefaultProgramDisplay));
			Assert.That(document.Settings.InternalGraphicsFormat, Is.EqualTo(ProjectOutputSettings.HdrGraphicsFormat));
		}

		[Test]
		[Category("P_ProjectFactory")]
		public void ProjectDocumentFactory_RoundTripsSettingsExpressionsAndMappings() {
			var controlId = new LogicalControlId("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
			var mapping = new ControlMappingRecord(PhysicalInputKind.Keyboard, "keyboard", "space");
			var control = new LogicalControlRecord(controlId, "Control", LogicalControlKind.Value, targets: new[]
			{
				new LogicalControlTargetRecord(NodeId("node_a"), new ParameterId("mix.amount"), ParameterType.Float,
					ParameterValue.FromFloat(0), ParameterValue.FromFloat(1))
			}, mappings: new[] { mapping });
			var expression = new ParameterExpressionRecord(NodeId("node_a"), new ParameterId("mix.amount"), new LogicalControlLeaf(controlId));
			var settings = new ProjectOutputSettings(ProjectDynamicRange.Ldr, 3);

			var result = ProjectDocumentFactory.TryCreate("Factory", 2, new[] { Node() }, Array.Empty<ConnectionRecord>(),
				new[] { control }, new[] { expression }, Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>(),
				settings: settings);

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.Settings, Is.EqualTo(settings));
			Assert.That(result.Value.Expressions.Count, Is.EqualTo(1));
			Assert.That(result.Value.Expressions[0].Expression, Is.TypeOf<LogicalControlLeaf>());
			Assert.That(result.Value.Expressions[0].IsBroken, Is.False);
			Assert.That(result.Value.ControlMappings.Count, Is.EqualTo(1));
			var snapshot = result.Value.TryCreateSaveSnapshot();
			Assert.That(snapshot.IsSuccess, Is.True);
			Assert.That(snapshot.Value.Settings, Is.EqualTo(settings));
			Assert.That(snapshot.Value.Expressions.Count, Is.EqualTo(1));
			Assert.That(snapshot.Value.ControlMappings.Count, Is.EqualTo(1));
		}

		[Test]
		[Category("P_ProjectFactory")]
		public void ProjectDocumentFactory_FailureLeavesExistingDocumentUnchanged() {
			var current = new ProjectDocument("Existing");
			var result = ProjectDocumentFactory.Rehydrate(current, "Replacement", 1,
				new[] { Node(), Node() }, Array.Empty<ConnectionRecord>(), Array.Empty<LogicalControlRecord>(),
				Array.Empty<ParameterExpressionRecord>(), Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsFailure, Is.True);
			Assert.That(current.ProjectName, Is.EqualTo("Existing"));
			Assert.That(current.Nodes.Count, Is.EqualTo(0));
			Assert.That(current.DocumentRevision, Is.EqualTo(0));
		}

		[Test]
		[Category("P_FixedProgramOutput")]
		public void ProjectDocumentFactory_CreatesMissingProgramOutputAndMarksRepair() {
			var result = ProjectDocumentFactory.TryCreateDetailed("New", 1, Array.Empty<NodeRecord>(), Array.Empty<ConnectionRecord>(),
				Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(), Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.WasRepaired, Is.True);
			Assert.That(result.Value.Document.DocumentRevision, Is.EqualTo(1));
			var output = result.Value.Document.Nodes.Single();
			Assert.That(output.Id.IsUuidV4, Is.True);
			Assert.That(output.TypeId.Value, Is.EqualTo("system.program_output"));
			Assert.That(output.SystemOwned, Is.True);
			Assert.That(output.UserAddable, Is.False);
			Assert.That(output.Enabled, Is.True);
			Assert.That(output.DisplayName, Is.EqualTo("Image"));
			Assert.That(output.Ports.Single().Id.Value, Is.EqualTo("image"));
			Assert.That(output.Ports.Single().Type, Is.EqualTo(PortType.ImageFrame));
			Assert.That(output.Ports.Single().Required, Is.True);
		}

		[Test]
		[Category("P_FixedProgramOutput")]
		public void ProjectDocumentFactory_UsesInjectedIdFactoryForDeterministicRepair() {
			var fixedId = NodeId("node_b");
			var result = ProjectDocumentFactory.TryCreateDetailed("New", 1, Array.Empty<NodeRecord>(), Array.Empty<ConnectionRecord>(),
				Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(), Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>(),
				idFactory: new FixedProjectIdFactory(fixedId));

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.Document.Nodes.Single().Id, Is.EqualTo(fixedId));
			Assert.That(result.Value.Document.Nodes.Single().Id.IsUuidV4, Is.True);
		}

		[Test]
		[Category("P_FixedProgramOutput")]
		public void ProjectDocumentFactory_PreservesOneValidProgramOutput() {
			var result = ProjectDocumentFactory.TryCreateDetailed("Existing", 1, new[] { ProgramNode("node_a") },
				Array.Empty<ConnectionRecord>(), Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(),
				Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.WasRepaired, Is.False);
			Assert.That(result.Value.Document.Nodes.Count, Is.EqualTo(1));
			Assert.That(result.Value.Document.DocumentRevision, Is.EqualTo(0));
			Assert.That(result.Value.Document.Nodes[0].Position, Is.EqualTo(new ProjectPosition(8, 9)));
		}

		[Test]
		[Category("P_FixedProgramOutput")]
		public void ProjectDocumentFactory_ConvertsDuplicateProgramOutputsToUnknownNodes() {
			var duplicate = ProgramNode("node_b", rawState: "{ \"duplicate\": true }");
			var result = ProjectDocumentFactory.TryCreateDetailed("Duplicate", 1, new[] { ProgramNode("node_a"), duplicate },
				Array.Empty<ConnectionRecord>(), Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(),
				Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.Document.Nodes.Count, Is.EqualTo(2));
			Assert.That(result.Value.Document.Nodes[0].IsUnknown, Is.False);
			Assert.That(result.Value.Document.Nodes[1].IsUnknown, Is.True);
			Assert.That(result.Value.Document.Nodes[1].Unknown.OriginalNodeTypeId.Value, Is.EqualTo("system.program_output"));
			Assert.That(result.Value.Document.Nodes[1].Unknown.RawJsonValue, Is.EqualTo("{ \"duplicate\": true }"));
			Assert.That(result.Value.Document.Nodes[1].Ports.Single().Id.Value, Is.EqualTo("image"));
		}

		[Test]
		[Category("P_FixedProgramOutput")]
		public void ProjectDocumentFactory_RepairsMalformedProgramOutputShape() {
			var malformed = new NodeRecord(NodeId("node_a"), new NodeTypeId("system.program_output"), 1, "Wrong", false,
				new ProjectPosition(4, 5), rawState: "{ \"keep\": 1 }");
			var result = ProjectDocumentFactory.TryCreateDetailed("Malformed", 1, new[] { malformed },
				Array.Empty<ConnectionRecord>(), Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(),
				Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.WasRepaired, Is.True);
			var output = result.Value.Document.Nodes.Single();
			Assert.That(output.DisplayName, Is.EqualTo("Image"));
			Assert.That(output.Enabled, Is.True);
			Assert.That(output.SystemOwned, Is.True);
			Assert.That(output.UserAddable, Is.False);
			Assert.That(output.Ports.Single().Type, Is.EqualTo(PortType.ImageFrame));
			Assert.That(output.RawState, Is.EqualTo("{ \"keep\": 1 }"));
		}

		[Test]
		[Category("P_FixedPreview")]
		public void ProjectDocumentFactory_RepairsMalformedPreviewShape() {
			var malformed = new NodeRecord(NodeId("node_a"), new NodeTypeId("system.preview"), 1, "Wrong", false,
				new ProjectPosition(4, 5), parameters: new[] { new ParameterRecord(new ParameterDefinition(new ParameterId("mode"), "Mode", ParameterType.String, ParameterValue.FromString("bad")), ParameterValue.FromString("bad")) });
			var result = ProjectDocumentFactory.TryCreateDetailed("Preview", 1, new[] { malformed },
				Array.Empty<ConnectionRecord>(), Array.Empty<LogicalControlRecord>(), Array.Empty<ParameterExpressionRecord>(),
				Array.Empty<PresetRecord>(), Array.Empty<MediaAssetRecord>());

			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.WasRepaired, Is.True);
			var preview = result.Value.Document.Nodes.Single(x => x.TypeId.Value == "system.preview");
			Assert.That(preview.DisplayName, Is.EqualTo("Wrong"), "Preview instance titles are persisted tab titles; only an empty title is repaired to Preview.");
			Assert.That(preview.Enabled, Is.True);
			Assert.That(preview.SystemOwned, Is.False);
			Assert.That(preview.UserAddable, Is.True);
			Assert.That(preview.Parameters.Select(x => x.Definition.Id.Value), Is.EqualTo(new[] { "display.mode" }));
			Assert.That(preview.FindParameter(new ParameterId("display.mode")).BaseValue.AsString(), Is.EqualTo("fit"));
			Assert.That(preview.Ports.Single().Id.Value, Is.EqualTo("image"));
			Assert.That(preview.Ports.Single().Required, Is.True);
		}
	}
}
