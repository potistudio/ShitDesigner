using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Nodes;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;

namespace ShitDesigner.Bootstrap.Tests {
	public sealed class ProductionOutputSurfaceBridgePlayModeTests {
		[UnityTest, Category("GPU"), Category("PreviewPresentation")]
		public IEnumerator OutputSurfaceBridge_RetiresBorrowedSurfacesAcrossClearWithoutAffectingTheNewBinding() {
			var previewId = new NodeInstanceId("e0000000-0000-4000-8000-000000000123");
			var previousActive = RenderTexture.active;
			var source = new RenderTexture(1920, 1080, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "BridgePreviewSource" };
			source.Create();
			try {
				using (var pool = new RenderTexturePool())
				using (var program = new ProgramHoldController(pool,
					new ResourceOwnerKey("bridge-test", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), ProgramDynamicRange.Hdr))
				using (var session = CreatePreviewSession(previewId, 640, 360))
				using (var bridge = new ProductionOutputSurfaceBridge(RequiredDisplayTransformShader())) {
					Assert.That(program.Ensure(1).IsSuccess, Is.True);
					session.LastPresentation = PresentedPreview(previewId, source, 41);
					bridge.Bind(session, program, pool);

					bridge.Sync(1);
					Assert.That(bridge.PreviewDisplayBlitCount, Is.EqualTo(1));
					Assert.That(bridge.TryDescribe(previewId.Value, out var described), Is.True);
					Assert.That(described.Generation, Is.Not.EqualTo(0UL));
					Assert.That(described.FrameNumber, Is.EqualTo(41UL));
					Assert.That(described.Width, Is.EqualTo(640));
					Assert.That(described.Height, Is.EqualTo(360));
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0), "Descriptor reads must not borrow a view-owned output lease.");
					Assert.That(bridge.TryAcquire("program", out var oldProgram), Is.True);
					Assert.That(bridge.TryAcquire(previewId.Value, out var first), Is.True);
					Assert.That(first.Width, Is.EqualTo(640));
					Assert.That(first.Height, Is.EqualTo(360));
					Assert.That(first.FrameNumber, Is.EqualTo(41));

					// The quality policy's non-due frame keeps its held
					// presentation. It must not run a second 60 Hz GPU copy.
					bridge.Sync(2);
					Assert.That(bridge.PreviewDisplayBlitCount, Is.EqualTo(1));
					Assert.That(bridge.TryAcquire(previewId.Value, out var held), Is.True);
					Assert.That(held.Generation, Is.EqualTo(first.Generation));
					held.Dispose();

					Assert.That(session.ApplyDemandRequest(Array.Empty<OutputDemand>()), Is.True);
					bridge.Sync(3);
					Assert.That(bridge.TryAcquire(previewId.Value, out _), Is.False, "A hidden Preview must stop host presentation demand.");
					Assert.That(session.ApplyDemandRequest(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 640, 360) }), Is.True);
					bridge.Sync(4);
					Assert.That(bridge.TryAcquire(previewId.Value, out var reopened), Is.True, "Showing the Preview again must reuse its retained display surface.");
					Assert.That(reopened.Generation, Is.EqualTo(first.Generation));
					reopened.Dispose();

					// A real PreviewQualityManager transition, rather than a
					// test-only demand mutation, replaces the descriptor.
					for (var sample = 1UL; sample <= 30UL; sample++)
						session.ObservePreviewTiming(previewId, 16d, 16d, sample);
					var degraded = session.CapturePreviewOutputSnapshots().Single();
					Assert.That(degraded.Width, Is.EqualTo(480));
					Assert.That(degraded.Height, Is.EqualTo(270));
					Assert.That(degraded.QualityStage, Is.EqualTo(1));
					bridge.Sync(31);
					Assert.That(bridge.PreviewDisplayBlitCount, Is.EqualTo(2));
					Assert.That(bridge.TryAcquire(previewId.Value, out var replacement), Is.True);
					Assert.That(replacement.Width, Is.EqualTo(480));
					Assert.That(replacement.Height, Is.EqualTo(270));
					Assert.That(replacement.Generation, Is.Not.EqualTo(first.Generation));
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == first.Generation && entry.State == TextureLeaseState.Leased), Is.True,
						"A borrowed old Preview generation must remain leased until its view releases it.");

					RenderTexture.active = null;
					var priorBinding = bridge.BindingGeneration;
					bridge.Clear();
					Assert.That(bridge.BindingGeneration, Is.Not.EqualTo(priorBinding));
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(3), "Clear must retain every view-owned OutputSurfaceLease until that view disposes it.");
					Assert.That(bridge.TryAcquire(previewId.Value, out _), Is.False, "A cleared binding must not issue new surfaces.");
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == first.Generation && entry.State == TextureLeaseState.Leased), Is.True);
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == replacement.Generation && entry.State == TextureLeaseState.Leased), Is.True);
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == oldProgram.Generation && entry.State == TextureLeaseState.Leased), Is.True);

					bridge.Bind(session, program, pool);
					bridge.Sync(32);
					Assert.That(bridge.TryAcquire("program", out var currentProgram), Is.True);
					Assert.That(bridge.TryAcquire(previewId.Value, out var currentPreview), Is.True);
					Assert.That(currentProgram.Generation, Is.Not.EqualTo(oldProgram.Generation));
					Assert.That(currentPreview.Generation, Is.Not.EqualTo(replacement.Generation));
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(5));

					first.Dispose();
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(4));
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == first.Generation && entry.State == TextureLeaseState.Leased), Is.False,
						"A stale Preview callback must retire only its own old generation.");
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == currentPreview.Generation && entry.State == TextureLeaseState.Leased), Is.True,
						"A stale callback must not release the new Preview binding.");
					replacement.Dispose();
					oldProgram.Dispose();
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(2));
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == oldProgram.Generation && entry.State == TextureLeaseState.Leased), Is.False,
						"A cleared Program surface must remain leased only until its old view releases it.");
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == currentProgram.Generation && entry.State == TextureLeaseState.Leased), Is.True,
						"A stale Program callback must not release the new Program binding.");
					currentPreview.Dispose();
					currentProgram.Dispose();
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0));

					bridge.Clear();
					Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.State == TextureLeaseState.Leased &&
						(entry.Owner.OwnerId == previewId.Value || entry.Owner.OwnerId == "program-display")), Is.False,
						"Final teardown must leave no bridge-owned display lease borrowed.");
				}
			}
			finally {
				RenderTexture.active = null;
				if (source != null) UnityEngine.Object.DestroyImmediate(source);
				RenderTexture.active = previousActive;
			}
			yield break;
		}

		[UnityTest, Category("GPU"), Category("PreviewPresentation")]
		public IEnumerator OutputSurfaceBridge_DescribesLatestSameGenerationFrameWithoutBorrowing() {
			var previewId = new NodeInstanceId("e0000000-0000-4000-8000-000000000127");
			var previousActive = RenderTexture.active;
			var source = new RenderTexture(1920, 1080, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "BridgeDescriptorSource" };
			source.Create();
			try {
				using (var pool = new RenderTexturePool())
				using (var program = new ProgramHoldController(pool,
					new ResourceOwnerKey("bridge-descriptor", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), ProgramDynamicRange.Hdr))
				using (var session = CreatePreviewSession(previewId, 640, 360))
				using (var bridge = new ProductionOutputSurfaceBridge(RequiredDisplayTransformShader())) {
					Assert.That(program.Ensure(1).IsSuccess, Is.True);
					bridge.Bind(session, program, pool);
					session.LastPresentation = PresentedPreview(previewId, source, 81);
					bridge.Sync(1);
					Assert.That(bridge.TryDescribe(previewId.Value, out var first), Is.True);
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0));

					session.LastPresentation = PresentedPreview(previewId, source, 82);
					bridge.Sync(2);
					Assert.That(bridge.TryDescribe(previewId.Value, out var latest), Is.True);
					Assert.That(latest.Generation, Is.EqualTo(first.Generation));
					Assert.That(latest.FrameNumber, Is.EqualTo(82UL));
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0), "Reading a descriptor must not borrow a view lease, even as the held frame advances.");
					RenderTexture.active = null;
				}
			}
			finally {
				RenderTexture.active = null;
				if (source != null) UnityEngine.Object.DestroyImmediate(source);
				RenderTexture.active = previousActive;
			}
			yield break;
		}

		[UnityTest, Category("GPU"), Category("PreviewPresentation")]
		public IEnumerator OutputSurfaceBridge_RetiresBorrowedPreviewWhenProjectCommandRemovesItsNode() {
			var previewId = new NodeInstanceId("e0000000-0000-4000-8000-000000000124");
			var previousActive = RenderTexture.active;
			var source = new RenderTexture(1920, 1080, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "BridgeRemovedBorrowedPreviewSource" };
			source.Create();
			try {
				using (var pool = new RenderTexturePool())
				using (var program = new ProgramHoldController(pool,
					new ResourceOwnerKey("bridge-remove-borrowed", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), ProgramDynamicRange.Hdr))
				using (var session = CreatePreviewSession(previewId, 640, 360))
				using (var bridge = new ProductionOutputSurfaceBridge(RequiredDisplayTransformShader())) {
					Assert.That(program.Ensure(1).IsSuccess, Is.True);
					session.LastPresentation = PresentedPreview(previewId, source, 51);
					bridge.Bind(session, program, pool);
					bridge.Sync(1);
					Assert.That(bridge.TryAcquire(previewId.Value, out var oldLease), Is.True);

					Assert.That(new ProjectCommandProcessor(session.Document).DeleteNode(previewId).IsSuccess, Is.True,
						"The production Project command must remove the Preview before bridge retirement is evaluated.");
					bridge.Sync(2);
					Assert.That(bridge.TryAcquire(previewId.Value, out _), Is.False, "A removed Preview must not be acquired from the current bridge binding.");
					Assert.That(IsLeased(pool, oldLease.Generation), Is.True,
						"The removed Preview texture remains owned by its already-bound view until that view releases it.");

					oldLease.Dispose();
					Assert.That(IsLeased(pool, oldLease.Generation), Is.False);
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0));
					RenderTexture.active = null;
				}
			}
			finally {
				RenderTexture.active = null;
				if (source != null) UnityEngine.Object.DestroyImmediate(source);
				RenderTexture.active = previousActive;
			}
			yield break;
		}

		[UnityTest, Category("GPU"), Category("PreviewPresentation")]
		public IEnumerator OutputSurfaceBridge_ReleasesUnborrowedPreviewImmediatelyWhenProjectCommandRemovesItsNode() {
			var previewId = new NodeInstanceId("e0000000-0000-4000-8000-000000000125");
			var previousActive = RenderTexture.active;
			var source = new RenderTexture(1920, 1080, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "BridgeRemovedUnborrowedPreviewSource" };
			source.Create();
			try {
				using (var pool = new RenderTexturePool())
				using (var program = new ProgramHoldController(pool,
					new ResourceOwnerKey("bridge-remove-unborrowed", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), ProgramDynamicRange.Hdr))
				using (var session = CreatePreviewSession(previewId, 640, 360))
				using (var bridge = new ProductionOutputSurfaceBridge(RequiredDisplayTransformShader())) {
					Assert.That(program.Ensure(1).IsSuccess, Is.True);
					session.LastPresentation = PresentedPreview(previewId, source, 61);
					bridge.Bind(session, program, pool);
					bridge.Sync(1);
					Assert.That(IsOwnerLeased(pool, previewId.Value), Is.True);

					Assert.That(new ProjectCommandProcessor(session.Document).DeleteNode(previewId).IsSuccess, Is.True);
					bridge.Sync(2);
					Assert.That(bridge.TryAcquire(previewId.Value, out _), Is.False);
					Assert.That(IsOwnerLeased(pool, previewId.Value), Is.False,
						"No view borrowed this Preview, so node removal must return its display surface immediately.");
					Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0));
					RenderTexture.active = null;
				}
			}
			finally {
				RenderTexture.active = null;
				if (source != null) UnityEngine.Object.DestroyImmediate(source);
				RenderTexture.active = previousActive;
			}
			yield break;
		}

		[UnityTest, Category("GPU"), Category("PreviewPresentation")]
		public IEnumerator OutputSurfaceBridge_DisposeRetainsBorrowedProgramAndPreviewUntilTheirViewsRelease() {
			var previewId = new NodeInstanceId("e0000000-0000-4000-8000-000000000126");
			var previousActive = RenderTexture.active;
			var source = new RenderTexture(1920, 1080, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "BridgeDisposedBorrowedSurfaceSource" };
			source.Create();
			try {
				using (var pool = new RenderTexturePool())
				using (var program = new ProgramHoldController(pool,
					new ResourceOwnerKey("bridge-dispose-borrowed", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), ProgramDynamicRange.Hdr))
				using (var session = CreatePreviewSession(previewId, 640, 360)) {
					var bridge = new ProductionOutputSurfaceBridge(RequiredDisplayTransformShader());
					try {
						Assert.That(program.Ensure(1).IsSuccess, Is.True);
						session.LastPresentation = PresentedPreview(previewId, source, 71);
						bridge.Bind(session, program, pool);
						bridge.Sync(1);
						Assert.That(bridge.TryAcquire("program", out var programLease), Is.True);
						Assert.That(bridge.TryAcquire(previewId.Value, out var previewLease), Is.True);

						RenderTexture.active = null;
						bridge.Dispose();
						Assert.That(bridge.TryAcquire("program", out _), Is.False);
						Assert.That(bridge.TryAcquire(previewId.Value, out _), Is.False);
						Assert.That(IsLeased(pool, programLease.Generation), Is.True);
						Assert.That(IsLeased(pool, previewLease.Generation), Is.True);
						Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(2));

						previewLease.Dispose();
						Assert.That(IsLeased(pool, previewLease.Generation), Is.False);
						Assert.That(IsLeased(pool, programLease.Generation), Is.True);
						programLease.Dispose();
						Assert.That(IsLeased(pool, programLease.Generation), Is.False);
						Assert.That(bridge.ActiveLeaseCount, Is.EqualTo(0));
					}
					finally { bridge.Dispose(); }
				}
			}
			finally {
				RenderTexture.active = null;
				if (source != null) UnityEngine.Object.DestroyImmediate(source);
				RenderTexture.active = previousActive;
			}
			yield break;
		}

		private static RuntimeSession CreatePreviewSession(NodeInstanceId previewId, int width, int height) {
			var document = new ProjectDocument("Bridge Preview");
			var preview = new NodeRecord(previewId, new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", false, new ProjectPosition(0, 0),
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) });
			Assert.That(new ProjectCommandProcessor(document).AddNode(preview).IsSuccess, Is.True);
			var session = new RuntimeSession(document, new NodeTypeRegistry());
			session.PreviewQualityPolicy = new PreviewQualityManager();
			Assert.That(session.ApplyDemandRequest(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), width, height) }), Is.True);
			return session;
		}

		private static Shader RequiredDisplayTransformShader() {
			var shader = Shader.Find("Hidden/ShitDesigner/DisplayTransform");
			Assert.That(shader, Is.Not.Null, "The PlayMode bridge contract requires the serialized DisplayTransform shader.");
			return shader;
		}

		private static OutputPresentation PresentedPreview(NodeInstanceId previewId, RenderTexture source, ulong frameNumber) {
			var frame = new SurfaceImageFrame(new RenderTextureOutputSurface(previewId, source, frameNumber));
			return new OutputPresentation(default(NodeOutputResult), new Dictionary<NodeInstanceId, NodeOutputResult>
			{
				{ previewId, NodeOutputResult.Available(PortValue.FromImageFrame(frame)) }
			});
		}

		private sealed class RenderTextureOutputSurface : IRuntimeOutputSurface, IRuntimeOutputSurfaceFormat {
			private readonly RenderTexture _texture;
			public NodeInstanceId NodeId { get; }
			public PortId PortId { get; } = new PortId("image");
			public int Width => _texture.width;
			public int Height => _texture.height;
			public ulong LeaseId { get; }
			public ulong FrameNumber { get; }
			public object NativeSurface => _texture;
			public string ColorFormat => _texture.graphicsFormat.ToString();

			public RenderTextureOutputSurface(NodeInstanceId nodeId, RenderTexture texture, ulong frameNumber) {
				NodeId = nodeId;
				_texture = texture ?? throw new ArgumentNullException(nameof(texture));
				LeaseId = frameNumber;
				FrameNumber = frameNumber;
			}
		}

		private static bool IsLeased(RenderTexturePool pool, ulong generation) =>
			pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.LeaseId.Value == generation && entry.State == TextureLeaseState.Leased);

		private static bool IsOwnerLeased(RenderTexturePool pool, string ownerId) =>
			pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.Owner.OwnerId == ownerId && entry.State == TextureLeaseState.Leased);
	}
}
