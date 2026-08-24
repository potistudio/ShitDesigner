using System;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;

namespace ShitDesigner.Rendering.Tests {
	public sealed class RenderingAssemblyBoundaryTests {
		[Test, Category("ModuleBoundaries")]
		public void Rendering_DoesNotReferenceProjectAssembly() {
			var references = typeof(DefaultImageProvider).Assembly.GetReferencedAssemblies();
			Assert.That(references.Any(x => string.Equals(x.Name, "ShitDesigner.Project", StringComparison.Ordinal)), Is.False);
			Assert.That(references.Any(x => string.Equals(x.Name, "ShitDesigner.Graph", StringComparison.Ordinal)), Is.False);
		}

		[Test, Category("ModuleBoundaries")]
		public void DefaultImageProvider_UsesRuntimeNeutralContract() {
			var method = typeof(DefaultImageProvider).GetMethod("Get");
			Assert.That(method, Is.Not.Null);
			Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(RuntimeDefaultImageKind)));
		}
	}
}
