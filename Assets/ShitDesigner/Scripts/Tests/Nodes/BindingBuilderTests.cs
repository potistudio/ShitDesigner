using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Runtime;

namespace ShitDesigner.Nodes.Tests {
	public sealed class BindingBuilderTests {
		[Test]
		public void Builder_RequiresAllSevenAndExposesReadOnlyAvailability() {
			var missing = NodeCatalogBootstrap.BuildProductionBindings(new IRuntimeVisualNodeBinding[0]);
			Assert.That(missing.IsFailure, Is.True);

			var complete = NodeCatalogBootstrap.BuildProductionBindings(new[]
			{
				Binding("shitdesigner.scene.3d"), Binding("shitdesigner.scene.2d"),
				Binding("shitdesigner.shader.generator"), Binding("shitdesigner.shader.effect"),
				Binding("shitdesigner.shader.blend2"), Binding("shitdesigner.video.player"),
				Binding("system.feedback")
			});
			Assert.That(complete.IsSuccess, Is.True, complete.IsFailure ? complete.Diagnostic.Message : string.Empty);
			Assert.That(complete.Value.IsProductionComplete, Is.True);
			Assert.That(complete.Value.Availability.MissingSpecializedTypes, Is.Empty);
			Assert.That(complete.Value.Availability.RegisteredTypeIds, Is.Not.SameAs(complete.Value.RegisteredTypeIds));
		}

		[Test]
		public void Builder_RejectsUnavailableBindingBeforeRegistration() {
			var binding = Binding("system.feedback", false);
			var result = NodeCatalogBootstrap.BuildProductionBindings(
				Binding("shitdesigner.scene.3d"), Binding("shitdesigner.scene.2d"),
				Binding("shitdesigner.shader.generator"), Binding("shitdesigner.shader.effect"),
				Binding("shitdesigner.shader.blend2"), Binding("shitdesigner.video.player"), binding);
			Assert.That(result.IsFailure, Is.True);
			Assert.That(result.Diagnostic.Code.Value, Is.EqualTo("nodes.factory.unavailable"));
		}

		private static IRuntimeVisualNodeBinding Binding(string id, bool available = true) => new FakeBinding(new NodeTypeId(id), available);

		private sealed class FakeBinding : IRuntimeVisualNodeBinding {
			public NodeTypeId TypeId { get; }
			public bool IsAvailable { get; }
			public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("nodes.factory.unavailable"), Severity.Error, "fake unavailable");
			public FakeBinding(NodeTypeId typeId, bool available) { TypeId = typeId; IsAvailable = available; }
			public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId) => Result<IRuntimeNode>.Success(new FakeNode(node.Id, TypeId, generationId));
		}

		private sealed class FakeNode : IRuntimeNode {
			public NodeInstanceId NodeId { get; }
			public NodeTypeId TypeId { get; }
			public ulong GenerationId { get; }
			public RuntimeNodeState State => RuntimeNodeState.Ready;
			public FakeNode(NodeInstanceId nodeId, NodeTypeId typeId, ulong generationId) { NodeId = nodeId; TypeId = typeId; GenerationId = generationId; }
			public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) { }
			public void Dispose() { }
		}
	}
}
