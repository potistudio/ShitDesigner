using System;
using CSharpFunctionalExtensions;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Runtime;

namespace ShitDesigner.Nodes.Tests {
	public sealed class NodeCatalogContractTests {
		[Test]
		public void InitialCatalog_ContainsFixedVisualVideoFeedbackAndLossyNodes() {
			var catalog = NodeDefinitionCatalog.CreateInitial();
			Assert.That(catalog.Validate().IsSuccess, Is.True);
			var ids = catalog.Entries.Select(x => x.TypeId.Value).ToArray();
			Assert.That(ids, Does.Contain("system.program_output"));
			Assert.That(ids, Does.Contain("system.preview"));
			Assert.That(ids, Does.Contain("system.feedback"));
			Assert.That(ids, Does.Contain("shitdesigner.scene.3d"));
			Assert.That(ids, Does.Contain("shitdesigner.scene.2d"));
			Assert.That(ids, Does.Contain("shitdesigner.shader.generator"));
			Assert.That(ids, Does.Contain("shitdesigner.shader.effect"));
			Assert.That(ids, Does.Contain("shitdesigner.shader.blend2"));
			Assert.That(ids, Does.Contain("shitdesigner.video.player"));
			Assert.That(ids, Does.Contain("shitdesigner.media.asset_flash"));
			Assert.That(ids.Count(x => x.StartsWith("shitdesigner.convert.", StringComparison.Ordinal)), Is.EqualTo(13));
		}

		[Test]
		public void AssetFlashDefinition_HasEightOptionalTriggersEightAssetsAndDuration() {
			var flash = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == "shitdesigner.media.asset_flash");
			Assert.That(flash.Ports.Count(x => x.Direction == NodePortDirection.Input && x.Type == NodePortType.Bool && !x.Required), Is.EqualTo(8));
			Assert.That(flash.Ports.Single(x => x.Id.Value == "image").Type, Is.EqualTo(NodePortType.ImageFrame));
			Assert.That(flash.Parameters.Count(x => x.Type == ParameterType.MediaAssetReference), Is.EqualTo(8));
			var duration = flash.Parameters.Single(x => x.Id.Value == "flash.duration_seconds");
			Assert.That(duration.DefaultValue.AsFloat(), Is.EqualTo(.25f));
			Assert.That(duration.Minimum.Value.AsFloat(), Is.EqualTo(.01f));
			Assert.That(duration.Maximum.Value.AsFloat(), Is.EqualTo(60f));
		}

		[Test]
		public void PixelSortDefinition_ExposesOnlyPortedControls() {
			var pixelSort = NodeDefinitionCatalog.CreateInitial().Entries.Single(
				x => x.TypeId.Value == BitonicPixelSortContract.NodeTypeId);
			Assert.That(pixelSort.Category, Is.EqualTo("Effect/Glitch"));
			Assert.That(pixelSort.Ports.Select(x => x.Id.Value), Is.EqualTo(new[] { "input", "image" }));
			Assert.That(pixelSort.Ports.Single(x => x.Id.Value == "input").Required, Is.True);
			Assert.That(pixelSort.Parameters.Select(x => x.Id.Value), Is.EqualTo(new[] {
				"direction", "ascending", "threshold_min", "threshold_max"
			}));
			Assert.That(pixelSort.Parameters.Single(x => x.Id.Value == "direction").EnumOptions,
				Is.EqualTo(new[] { "horizontal", "vertical" }));
			Assert.That(pixelSort.Parameters.Single(x => x.Id.Value == "threshold_min").DefaultValue.AsFloat(), Is.EqualTo(.4f));
			Assert.That(pixelSort.Parameters.Single(x => x.Id.Value == "threshold_max").DefaultValue.AsFloat(), Is.EqualTo(.6f));
		}

		[Test]
		public void InstantEffectTriggerDefinition_ExposesTenBooleanOutputsInKeyboardOrder() {
			var triggers = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == InstantEffectTriggerContract.NodeTypeId);
			Assert.That(triggers.Category, Is.EqualTo("Input"));
			Assert.That(triggers.Parameters, Is.Empty);
			Assert.That(triggers.Ports.Select(port => port.Id.Value),
				Is.EqualTo(Enumerable.Range(1, InstantEffectTriggerContract.TriggerCount).Select(InstantEffectTriggerContract.PortId)));
			Assert.That(triggers.Ports.All(port => port.Direction == NodePortDirection.Output && port.Type == NodePortType.Bool), Is.True);
			Assert.That(triggers.Ports.Select(port => port.DisplayName),
				Is.EqualTo(new[] { "Trigger 1 (Q)", "Trigger 2 (W)", "Trigger 3 (E)", "Trigger 4 (R)", "Trigger 5 (T)", "Trigger 6 (Y)", "Trigger 7 (U)", "Trigger 8 (I)", "Trigger 9 (O)", "Trigger 10 (P)" }));
		}

		[Test]
		public void FixedOutputs_ProgramIsSingleSystemOwnedPreviewIsAddable() {
			var catalog = NodeDefinitionCatalog.CreateInitial();
			var program = catalog.Entries.Single(x => x.TypeId.Value == "system.program_output");
			var preview = catalog.Entries.Single(x => x.TypeId.Value == "system.preview");
			Assert.That(program.SystemOwned, Is.True);
			Assert.That(program.UserAddable, Is.False);
			Assert.That(program.Ports.Single().Id.Value, Is.EqualTo("image"));
			Assert.That(program.Ports.Single().Direction, Is.EqualTo(NodePortDirection.Input));
			Assert.That(program.Ports.Single().Required, Is.True);
			Assert.That(preview.SystemOwned, Is.False);
			Assert.That(preview.UserAddable, Is.True);
			Assert.That(preview.Ports.Count, Is.EqualTo(1));
			Assert.That(preview.Parameters.Single().DefaultValue.AsString(), Is.EqualTo("fit"));
		}

		[Test]
		public void VideoDefinition_UsesStatefulPlayheadAndContractRanges() {
			var video = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == "shitdesigner.video.player");
			Assert.That(video.Ports.Count(x => x.Direction == NodePortDirection.Input), Is.EqualTo(0));
			Assert.That(video.Ports.Single(x => x.Id.Value == "image").Direction, Is.EqualTo(NodePortDirection.Output));
			var playhead = video.Parameters.Single(x => x.Id.Value == "transport.playhead_seconds");
			var speed = video.Parameters.Single(x => x.Id.Value == "transport.speed");
			Assert.That(playhead.RuntimeStateful, Is.True);
			Assert.That(playhead.DefaultValue.AsFloat(), Is.EqualTo(0f));
			Assert.That(speed.Minimum.Value.AsFloat(), Is.EqualTo(0f));
			Assert.That(speed.Maximum.Value.AsFloat(), Is.EqualTo(4f));
			Assert.That(video.Parameters.Single(x => x.Id.Value == "transport.playing").DefaultValue.AsBool(), Is.True);
			Assert.That(video.Parameters.Single(x => x.Id.Value == "transport.loop").DefaultValue.AsBool(), Is.True);
		}

		[Test]
		public void Catalog_RejectsDuplicateTypeIdsAndAssetManifestRoundTrips() {
			var initial = NodeDefinitionCatalog.CreateInitial();
			var duplicate = new NodeDefinitionCatalog(initial.Entries.Concat(new[] { initial.Entries[0] }));
			Assert.That(duplicate.Validate().IsFailure, Is.True);

			var asset = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>("Assets/ShitDesigner/Scripts/Modules/Nodes/NodeTypeCatalog.asset");
			Assert.That(asset, Is.Not.Null);
			Assert.That(asset.BitonicPixelSorter, Is.Not.Null);
			Assert.That(asset.ValidateManifest().IsSuccess, Is.True);
			var runtime = asset.BuildRuntimeCatalog();
			Assert.That(runtime.IsSuccess, Is.True);
			Assert.That(runtime.Value.Entries.Count, Is.EqualTo(asset.Entries.Count));
			Assert.That(runtime.Value.Entries.Count, Is.GreaterThan(initial.Entries.Count));
		}

		[Test]
		public void CatalogAsset_MatchesCompletePortAndParameterDescriptors() {
			var asset = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>("Assets/ShitDesigner/Scripts/Modules/Nodes/NodeTypeCatalog.asset");
			Assert.That(asset, Is.Not.Null);
			var runtime = asset.BuildRuntimeCatalog();
			Assert.That(runtime.IsSuccess, Is.True, runtime.IsFailure ? runtime.Error.Message : string.Empty);
			var result = asset.ValidateAgainst(runtime.Value);
			Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
			Assert.That(asset.Entries.All(x => x.Ports.Count == x.PortIds.Count && x.Parameters.Count == x.ParameterIds.Count), Is.True);
		}

		[Test]
		public void CatalogAsset_DoesNotSerializeScenePrefabReferences() {
			var asset = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>("Assets/ShitDesigner/Scripts/Modules/Nodes/NodeTypeCatalog.asset");
			Assert.That(asset, Is.Not.Null);
			var serialized = new SerializedObject(asset);
			var entries = serialized.FindProperty("entries");
			Assert.That(entries, Is.Not.Null);
			for (var index = 0; index < entries.arraySize; index++) {
				var entry = entries.GetArrayElementAtIndex(index);
				Assert.That(entry.FindPropertyRelative("scenePrefab"), Is.Null);
				Assert.That(entry.FindPropertyRelative("prefabKey"), Is.Null);
			}
		}

		[Test]
		public void WindowsStandaloneGraphicsApis_AreExplicitD3D12ThenVulkan() {
			var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
			var settings = File.ReadAllText(Path.Combine(projectRoot, "ProjectSettings", "ProjectSettings.asset"));
			Assert.That(settings, Does.Contain("m_BuildTarget: WindowsStandaloneSupport"));
			Assert.That(settings, Does.Contain("m_APIs: 1200000015000000"));
		}

		[Test]
		public void ProductPlayer_EnablesFrameTimingStatistics() {
			Assert.That(PlayerSettings.enableFrameTimingStats, Is.True);
		}

		[Test]
		public void CatalogFactory_IsExplicitAndCreatesRegisteredRuntimeNode() {
			var entry = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == "shitdesigner.shader.generator");
			var node = new RuntimeNodeCreateInfo(new NodeInstanceId("00000000-0000-4000-8000-000000000001"), entry.TypeId, 1, entry.DisplayName, true, 0, 0);
			var result = entry.Factory.Create(node, 1);
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.Value.TypeId, Is.EqualTo(entry.TypeId));
			result.Value.Dispose();
		}

		[Test]
		public void ShaderGenerator_ExposesColorParameterAndExplicitColorBinding() {
			var entry = NodeDefinitionCatalog.CreateInitial().Entries.Single(x => x.TypeId.Value == "shitdesigner.shader.generator");
			var color = entry.Definition.FindParameter(new ParameterId("color"));
			Assert.That(color, Is.Not.Null);
			Assert.That(color.Type, Is.EqualTo(ParameterType.Color));
			Assert.That(color.DefaultValue.AsColor().A, Is.EqualTo(1f).Within(0.0001f));
			Assert.That(entry.ShaderBinding.ParameterProperties[new ParameterId("color")], Is.EqualTo("_Color"));
		}

		[Test]
		public void SurfaceImageFrame_ExposesItsRuntimeOutputSurfaceAtTheNativeSurfaceBoundary() {
			var nativeSurface = new object();
			var output = new TestOutputSurface(nativeSurface);
			IRuntimeImageFrame frame = new SurfaceImageFrame(output);

			Assert.That(frame, Is.InstanceOf<IRuntimeImageFrameSurface>());
			var surfaceFrame = (IRuntimeImageFrameSurface)frame;
			Assert.That(surfaceFrame.NativeSurface, Is.SameAs(nativeSurface));
			Assert.That(frame.Width, Is.EqualTo(output.Width));
			Assert.That(frame.Height, Is.EqualTo(output.Height));
			Assert.That(frame.FrameNumber, Is.EqualTo(output.FrameNumber));
			Assert.That(frame.LeaseId, Is.EqualTo(output.LeaseId));
		}

		[Test]
		public void NodesAssembly_EnforcesCoreRuntimeOnlyBoundary() {
			var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
			var asmdef = File.ReadAllText(Path.Combine(projectRoot, "Assets", "ShitDesigner", "Scripts", "Nodes", "ShitDesigner.Nodes.asmdef"));
			Assert.That(asmdef, Does.Contain("ShitDesigner.Core"));
			Assert.That(asmdef, Does.Contain("ShitDesigner.Runtime"));
			Assert.That(asmdef, Does.Not.Contain("ShitDesigner.Project"));
			Assert.That(asmdef, Does.Not.Contain("ShitDesigner.Graph"));
			Assert.That(asmdef, Does.Not.Contain("ShitDesigner.Rendering"));
			Assert.That(asmdef, Does.Not.Contain("ShitDesigner.Scene"));
			Assert.That(asmdef, Does.Not.Contain("ShitDesigner.Media"));
		}

		[Test]
		public void Catalog_RequiresExplicitVisualServiceBindings() {
			var missing = NodeDefinitionCatalog.CreateProduction(new NodeFactoryBindings());
			Assert.That(missing.IsFailure, Is.True);
			var bindings = new NodeFactoryBindings();
			foreach (var type in NodeDefinitionCatalog.SpecializedNodeTypeIds) {
				var id = new NodeTypeId(type);
				Assert.That(bindings.Register(id, (info, generation) => Result.Success<IRuntimeNode, Diagnostic>(new StubNode(info.Id, info.TypeId, generation))).IsSuccess, Is.True);
			}
			var production = NodeDefinitionCatalog.CreateProduction(bindings);
			Assert.That(production.IsSuccess, Is.True);
			var entry = production.Value.Entries.Single(x => x.TypeId.Value == "shitdesigner.video.player");
			var info = new RuntimeNodeCreateInfo(new NodeInstanceId("00000000-0000-4000-8000-000000000002"), entry.TypeId, 1, entry.DisplayName, true, 0, 0);
			var created = entry.Factory.Create(info, 1);
			Assert.That(created.IsSuccess, Is.True);
			Assert.That(created.Value, Is.TypeOf<StubNode>());
			created.Value.Dispose();
		}

		private sealed class StubNode : IRuntimeNode {
			public NodeInstanceId NodeId { get; }
			public NodeTypeId TypeId { get; }
			public ulong GenerationId { get; }
			public RuntimeNodeState State => RuntimeNodeState.Ready;
			public StubNode(NodeInstanceId nodeId, NodeTypeId typeId, ulong generationId) { NodeId = nodeId; TypeId = typeId; GenerationId = generationId; }
			public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) { }
			public void Dispose() { }
		}

		private sealed class TestOutputSurface : IRuntimeOutputSurface, IRuntimeOutputSurfaceFormat {
			public NodeInstanceId NodeId { get; } = new NodeInstanceId("31000000-0000-4000-8000-000000000001");
			public PortId PortId { get; } = new PortId("image");
			public int Width => 640;
			public int Height => 360;
			public ulong LeaseId => 71;
			public ulong FrameNumber => 19;
			public object NativeSurface { get; }
			public string ColorFormat => "R16G16B16A16_SFloat";

			public TestOutputSurface(object nativeSurface) { NativeSurface = nativeSurface; }
		}
	}
}
