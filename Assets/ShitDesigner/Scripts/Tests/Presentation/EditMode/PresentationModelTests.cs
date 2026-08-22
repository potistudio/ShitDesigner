using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Presentation;

namespace ShitDesigner.Presentation.Tests.EditMode
{
    public sealed class PresentationModelTests
    {
        [Test]
        public void DockCandidateFailure_PreservesCurrentTree()
        {
            var current = new DockTree(new DockTabGroup(new[] { "graph" }, "graph"));
            var session = new DockLayoutSession(current);
            session.BeginDrag();
            session.SetCandidate(new DockTree(new DockSplit(DockAxis.Horizontal, 2f, new DockEmpty(), new DockEmpty())));
            Assert.That(session.TryCommitCandidate(new HashSet<string> { "graph" }, out var validation), Is.False);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(session.Current.Root.Kind, Is.EqualTo("TabGroup"));
        }

        [Test]
        public void GraphMapper_ConvertsScreenAndCanvasWithoutMutatingNodePositions()
        {
            var mapper = new GraphCoordinateMapper();
            var point = new PresentationPoint(24, 32);
            Assert.That(mapper.CanvasToScreen(mapper.ScreenToCanvas(point)).X, Is.EqualTo(point.X).Within(.001));
            Assert.That(GraphCoordinateMapper.ClampZoom(.1f), Is.EqualTo(GraphCoordinateMapper.MinZoom));
        }

        [Test]
        public void ShortcutRouter_SuppressesGraphKeyInTextInput()
        {
            var router = new ShortcutRouter();
            router.Register(new ShortcutBinding(PresentationKey.G, "graph.grid"));
            Assert.That(router.Resolve(PresentationKey.G, false, false, false, true, true, false), Is.Null);
        }

        [Test]
        public void PresentationAssembly_ReferencesOnlyCoreAndApplicationDirectly()
        {
            var names = typeof(PresentationReadModel).Assembly.GetReferencedAssemblies();
            Assert.That(Array.Exists(names, x => x.Name == "ShitDesigner.Project"), Is.False);
            Assert.That(Array.Exists(names, x => x.Name == "ShitDesigner.Graph"), Is.False);
            Assert.That(Array.Exists(names, x => x.Name == "ShitDesigner.Runtime"), Is.False);
            Assert.That(Array.Exists(names, x => x.Name == "ShitDesigner.Rendering"), Is.False);
        }
    }
}
