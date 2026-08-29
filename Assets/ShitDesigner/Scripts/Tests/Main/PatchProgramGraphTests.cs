using NUnit.Framework;
using ShitDesigner.Scene;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class PatchProgramGraphTests {
		[Test]
		public void Validate_AllowsGraphWithoutA3DSceneNode() {
			var graph = new PatchProgramGraph(
				"generator",
				new[] { new PatchGraphNode("generator", "shitdesigner.shader.generator") },
				new PatchGraphConnection[0]);

			var result = graph.Validate();

			Assert.That(result.IsSuccess, Is.True);
		}
	}
}
