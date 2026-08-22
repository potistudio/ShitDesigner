using System;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Runtime;

namespace VisualNodeSmoke
{
    internal static class Program
    {
        private static void Main()
        {
            var catalog = NodeDefinitionCatalog.CreateInitial();
            var result = catalog.Validate();
            if (result.IsFailure) throw new InvalidOperationException(result.Diagnostic.Message);
            var bindings = NodeCatalogBootstrap.BuildProductionBindings(new IRuntimeVisualNodeBinding[]
            {
                Binding("shitdesigner.scene.3d"), Binding("shitdesigner.scene.2d"),
                Binding("shitdesigner.shader.generator"), Binding("shitdesigner.shader.effect"),
                Binding("shitdesigner.shader.blend2"), Binding("shitdesigner.video.player"), Binding("system.feedback")
            });
            if (bindings.IsFailure || !bindings.Value.IsProductionComplete) throw new InvalidOperationException(bindings.IsFailure ? bindings.Diagnostic.Message : "binding table incomplete");
            if (NodeCatalogBootstrap.CreateOutputFormatPolicy(new ShitDesigner.Project.ProjectOutputSettings()).ColorFormat != "R16G16B16A16_SFloat") throw new InvalidOperationException("HDR policy mismatch");
            Console.WriteLine("visual node source-direct compile=ok entries=" + catalog.Entries.Count + " bindings=" + bindings.Value.RegisteredTypeIds.Count);
        }

        private static IRuntimeVisualNodeBinding Binding(string id) => new FakeBinding(new NodeTypeId(id));

        private sealed class FakeBinding : IRuntimeVisualNodeBinding
        {
            public NodeTypeId TypeId { get; }
            public bool IsAvailable => true;
            public Diagnostic AvailabilityDiagnostic => null;
            public FakeBinding(NodeTypeId id) { TypeId = id; }
            public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId) => Result<IRuntimeNode>.Success(new FakeNode(node.Id, TypeId, generationId));
        }
        private sealed class FakeNode : IRuntimeNode
        {
            public NodeInstanceId NodeId { get; }
            public NodeTypeId TypeId { get; }
            public ulong GenerationId { get; }
            public RuntimeNodeState State => RuntimeNodeState.Ready;
            public FakeNode(NodeInstanceId id, NodeTypeId type, ulong generation) { NodeId = id; TypeId = type; GenerationId = generation; }
            public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) { }
            public void Dispose() { }
        }
    }
}
