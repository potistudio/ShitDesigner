using System;
using CSharpFunctionalExtensions;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ShitDesigner.Rendering.Tests {
	public sealed class RenderingContractTests {
		private static readonly GraphicsFormat ColorFormat = GraphicsFormat.R8G8B8A8_UNorm;

		private static TextureDescriptor Descriptor(int width = 4, int height = 4, GraphicsFormat format = GraphicsFormat.R8G8B8A8_UNorm) =>
			new TextureDescriptor(width, height, format, GraphicsFormat.None, 1, false, false, TextureDimension.Tex2D, 1, false);

		private static ResourceOwnerKey Owner(string id = "node", ulong generation = 1, LeaseRole role = LeaseRole.Output) =>
			new ResourceOwnerKey("session", ResourceOwnerKind.RuntimeNode, id, generation, "image", role);

		[Test, Category("ImageFrameRuntimeContract")]
		public void ImageFrame_RequiresCreatedTextureAndExactFields() {
			using (var pool = new RenderTexturePool()) {
				var lease = pool.Acquire(Descriptor(), Owner(), 1);
				Assert.That(lease.IsSuccess, Is.True, lease.IsFailure ? lease.Error.Message : string.Empty);
				var borrowed = lease.Value.Borrow(1);
				Assert.That(borrowed.IsSuccess, Is.True, borrowed.IsFailure ? borrowed.Error.Message : string.Empty);
				Assert.That(borrowed.Value.Frame.Size, Is.EqualTo(new Vector2Int(4, 4)));
				Assert.That(borrowed.Value.Frame.ColorFormat, Is.EqualTo(ColorFormat));
				Assert.That(borrowed.Value.Frame.LeaseId, Is.EqualTo(lease.Value.LeaseId));
				var runtimeFrame = (IRuntimeImageFrame)borrowed.Value.Frame;
				Assert.That(runtimeFrame.Width, Is.EqualTo(4));
				Assert.That(runtimeFrame.Height, Is.EqualTo(4));
				Assert.That(runtimeFrame.FrameNumber, Is.EqualTo(1));
				Assert.That(runtimeFrame.LeaseId, Is.EqualTo(lease.Value.LeaseId.Value));
				Assert.That(lease.Value.Release().IsSuccess, Is.True);
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TexturePool_DemandBeforeFirstAcquisition_HasNoLease() {
			using (var pool = new RenderTexturePool())
			using (var output = new OutputPortController(pool, Owner())) {
				Assert.That(output.HasActive, Is.False);
				Assert.That(output.BorrowActive(1).IsFailure, Is.True);
				Assert.That(output.EnsureDemand(Descriptor(), 1).IsSuccess, Is.True);
				Assert.That(output.HasActive, Is.False);
				Assert.That(output.HasCandidate, Is.True);
				var first = output.CandidateLease.Borrow(1).Value.Frame;
				Assert.That(output.CommitCandidate(first, 1).IsFailure, Is.True);
				Assert.That(output.MarkCandidateRendered(first).IsSuccess, Is.True);
				Assert.That(output.CommitCandidate(first, 1).IsSuccess, Is.True);
				Assert.That(output.HasActive, Is.True);
			}
		}

		[Test, Category("InternalDynamicRange")]
		public void DefaultImageProvider_UsesProjectInternalFormat() {
			using (var hdrPool = new RenderTexturePool())
			using (var hdr = new DefaultImageProvider(hdrPool, Owner("defaults-hdr"), RuntimeDynamicRange.Hdr))
			using (var ldrPool = new RenderTexturePool())
			using (var ldr = new DefaultImageProvider(ldrPool, Owner("defaults-ldr"), RuntimeDynamicRange.Ldr)) {
				var hdrValue = hdr.Get(RuntimeDefaultImageKind.TransparentBlack, 4, 4, 1);
				var ldrValue = ldr.Get(RuntimeDefaultImageKind.TransparentBlack, 4, 4, 1);
				Assert.That(hdrValue.IsSuccess, Is.True, hdrValue.IsFailure ? hdrValue.Error.Message : string.Empty);
				Assert.That(ldrValue.IsSuccess, Is.True, ldrValue.IsFailure ? ldrValue.Error.Message : string.Empty);
				Assert.That(hdrValue.Value.AsImageFrame().ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat.ToString()));
				Assert.That(ldrValue.Value.AsImageFrame().ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm.ToString()));
			}
		}

		[Test, Category("GraphicsFormat")]
		public void InternalFormatValidation_RequiresRenderSampleAndLoadStore() {
			var missingLoadStore = new FakeFormatCapabilities((format, usage) => usage != GraphicsFormatUsage.LoadStore);
			var rejected = RenderingFormatPolicy.ValidateInternalFormat(ProgramDynamicRange.Hdr, missingLoadStore);
			Assert.That(rejected.IsFailure, Is.True);
			Assert.That(rejected.Error.Code.Value, Is.EqualTo("rendering.format.unsupported"));

			var allUsages = new FakeFormatCapabilities((format, usage) => true);
			Assert.That(RenderingFormatPolicy.ValidateInternalFormat(ProgramDynamicRange.Hdr, allUsages).IsSuccess, Is.True);
			Assert.That(RenderingFormatPolicy.ValidateInternalFormat(ProgramDynamicRange.Ldr, allUsages).IsSuccess, Is.True);
		}

		[UnityTest, Category("ResourceOwnership"), Category("Phase5"), Category("Phase9")]
		public IEnumerator RuntimeOutputService_HideKeepsActiveLease_ReopenReuses_DeletionReleasesAtPhase9() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var session = CreateOutputServiceSession(out var previewSourceId, out var previewId, pool, RuntimeDynamicRange.Ldr))
			using (var service = new RuntimeOutputSurfaceService(session, pool, "service-test", new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Ldr))) {
				session.OutputSurfaces = service;
				session.ResourcePreparation = service;
				session.ResourceFinalization = service;
				var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);

				var firstReport = coordinator.Tick(0d);
				Assert.That(firstReport.Succeeded, Is.True, string.Join("; ", firstReport.Diagnostics.Select(x => x.Message)));
				Assert.That(service.RequiredOutputKeys.Any(x => x.NodeId == previewId), Is.False, "Preview's terminal input must not own an output lease.");
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(x => x.Owner.OwnerId == previewId.Value), Is.False);
				var first = service.TryGetPrepared(previewSourceId, new PortId("image"), 320, 180, firstReport.FrameNumber);
				Assert.That(first.IsSuccess, Is.True, first.IsFailure ? first.Error.Message : string.Empty);
				var firstLease = first.Value.LeaseId;
				var firstTexture = first.Value.NativeSurface;
				Assert.That(((IRuntimeOutputSurfaceFormat)first.Value).ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm.ToString()));
				var firstOwner = pool.CaptureOwnershipSnapshot().Entries.Single(x => x.LeaseId.Value == firstLease).Owner;

				// Preview visibility is not a lease release signal. The
				// source is omitted from evaluation while hidden, but its
				// Active lease must remain valid for reopening.
				Assert.That(session.SetOutputDemands(Array.Empty<OutputDemand>()).IsSuccess, Is.True);
				var hidden = coordinator.Tick(1d / 60d);
				Assert.That(hidden.Succeeded, Is.True, string.Join("; ", hidden.Diagnostics.Select(x => x.Message)));
				var hiddenSurface = service.TryGetPrepared(previewSourceId, new PortId("image"), 320, 180, hidden.FrameNumber);
				Assert.That(hiddenSurface.IsSuccess, Is.True, hiddenSurface.IsFailure ? hiddenSurface.Error.Message : string.Empty);
				Assert.That(hiddenSurface.Value.LeaseId, Is.EqualTo(firstLease));
				Assert.That(hiddenSurface.Value.NativeSurface, Is.SameAs(firstTexture));

				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);
				var reopened = coordinator.Tick(2d / 60d);
				var reopenedSurface = service.TryGetPrepared(previewSourceId, new PortId("image"), 320, 180, reopened.FrameNumber);
				Assert.That(reopenedSurface.IsSuccess, Is.True, reopenedSurface.IsFailure ? reopenedSurface.Error.Message : string.Empty);
				Assert.That(reopenedSurface.Value.LeaseId, Is.EqualTo(firstLease));
				Assert.That(reopenedSurface.Value.NativeSurface, Is.SameAs(firstTexture));
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Single(x => x.LeaseId.Value == firstLease).Owner, Is.EqualTo(firstOwner));

				// Deletion removes the runtime node first; the service only
				// returns its output at the Phase-9 finalization boundary.
				Assert.That(session.ApplyGraphCommand(new DeleteNodeEditCommand(previewSourceId)).IsSuccess, Is.True);
				var deleted = coordinator.Tick(3d / 60d);
				Assert.That(deleted.Succeeded, Is.True, string.Join("; ", deleted.Diagnostics.Select(x => x.Message)));
				Assert.That(service.TryGetPrepared(previewSourceId, new PortId("image"), 320, 180, deleted.FrameNumber).IsFailure, Is.True);
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(x => x.LeaseId.Value == firstLease), Is.False);
				yield return null;
			}
		}

		[Test, Category("ResolutionAndOutputPolicy"), Category("RuntimeAllocation")]
		public void RuntimeResolutionProjection_ReusesPlanOwnedEntriesUntilDemandPlanChanges() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var session = CreateOutputServiceSession(out var sourceId, out var previewId, pool, RuntimeDynamicRange.Ldr)) {
				var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);
				coordinator.Tick(0d);
				var report = coordinator.Tick(1d / 60d);

				var first = RuntimeOutputResolutionDemandAccess.GetAll(report.Snapshot);
				var second = RuntimeOutputResolutionDemandAccess.GetAll(report.Snapshot);
				Assert.That(second, Is.SameAs(first));
				var visualFirst = RuntimeOutputResolutionDemandAccess.GetVisualOutputs(session);
				var visualSecond = RuntimeOutputResolutionDemandAccess.GetVisualOutputs(session);
				Assert.That(visualSecond, Is.SameAs(visualFirst));
				Assert.That(RuntimeOutputResolutionDemandAccess.TryGet(report.Snapshot, sourceId, new PortId("image"), out var demandA), Is.True);
				Assert.That(RuntimeOutputResolutionDemandAccess.TryGet(report.Snapshot, sourceId, new PortId("image"), out var demandB), Is.True);
				Assert.That(demandB, Is.SameAs(demandA));

				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 640, 360) }).IsSuccess, Is.True);
				coordinator.Tick(2d / 60d);
				var changed = coordinator.Tick(3d / 60d);
				var rebuilt = RuntimeOutputResolutionDemandAccess.GetAll(changed.Snapshot);
				Assert.That(rebuilt, Is.Not.SameAs(first));
				Assert.That(RuntimeOutputResolutionDemandAccess.TryGet(changed.Snapshot, sourceId, new PortId("image"), out var changedDemand), Is.True);
				Assert.That(changedDemand.Width, Is.EqualTo(640));
				Assert.That(changedDemand.Height, Is.EqualTo(360));
			}
		}

		[UnityTest, Category("ResolutionAndOutputPolicy"), Category("HDR"), Category("LDR")]
		public IEnumerator RuntimeOutputService_UsesProjectFormatPolicyInSurfaceAndFrameMetadata() {
			foreach (var range in new[] { RuntimeDynamicRange.Hdr, RuntimeDynamicRange.Ldr }) {
				using (var pool = new RenderTexturePool(128L * 1024L * 1024L))
				using (var session = CreateOutputServiceSession(out var sourceId, out var previewId, pool, range))
				using (var service = new RuntimeOutputSurfaceService(session, pool, "format-test-" + range, new RuntimeOutputFormatPolicy(range))) {
					session.OutputSurfaces = service;
					session.ResourcePreparation = service;
					session.ResourceFinalization = service;
					var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
					Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 16, 8) }).IsSuccess, Is.True);
					var report = coordinator.Tick(0d);
					Assert.That(report.Succeeded, Is.True, string.Join("; ", report.Diagnostics.Select(x => x.Message)));
					var surface = service.TryGetPrepared(sourceId, new PortId("image"), 16, 8, report.FrameNumber);
					Assert.That(surface.IsSuccess, Is.True, surface.IsFailure ? surface.Error.Message : string.Empty);
					var expected = range == RuntimeDynamicRange.Hdr ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
					Assert.That(service.InternalFormat, Is.EqualTo(expected));
					Assert.That(((IRuntimeOutputSurfaceFormat)surface.Value).ColorFormat, Is.EqualTo(expected.ToString()));
					Assert.That(surface.Value.NativeSurface, Is.TypeOf<RenderTexture>());
					Assert.That(((RenderTexture)surface.Value.NativeSurface).graphicsFormat, Is.EqualTo(expected));
					yield return null;
				}
			}
		}

		[UnityTest, Category("RenderTexturePoolPolicy"), Category("SceneNodeRuntimeIsolation"), Category("URP"), Category("D3D12"), Category("Vulkan"), Category("Metal")]
		public IEnumerator RuntimeOutputService_SceneNodesAllocateDepthAttachedPoolOutputForCameraRenderRequests() {
			foreach (var sceneTypeId in new[] { "shitdesigner.scene.3d", "shitdesigner.scene.2d" }) {
				var pool = new RenderTexturePool(64L * 1024L * 1024L);
				try {
					NodeInstanceId sourceId;
					using (var session = CreateOutputServiceSession(out sourceId, out var previewId, pool, RuntimeDynamicRange.Ldr, sceneTypeId))
					using (var service = new RuntimeOutputSurfaceService(session, pool, "scene-depth-" + sceneTypeId, new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Ldr))) {
						session.OutputSurfaces = service;
						session.ResourcePreparation = service;
						session.ResourceFinalization = service;
						var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
						Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 32, 18) }).IsSuccess, Is.True);

						var report = coordinator.Tick(0d);
						Assert.That(report.Succeeded, Is.True, string.Join("; ", report.Diagnostics.Select(x => x.Message)));
						var prepared = service.TryGetPrepared(sourceId, new PortId("image"), 32, 18, report.FrameNumber);
						Assert.That(prepared.IsSuccess, Is.True, prepared.IsFailure ? prepared.Error.Message : string.Empty);
						var texture = prepared.Value.NativeSurface as RenderTexture;
						Assert.That(texture, Is.Not.Null);
						Assert.That(texture.descriptor.depthStencilFormat, Is.EqualTo(GraphicsFormat.D32_SFloat), sceneTypeId + " must provide URP SingleCameraRequest with a depth-attached destination.");

						var entry = pool.CaptureOwnershipSnapshot().Entries.Single(x => x.LeaseId.Value == prepared.Value.LeaseId);
						Assert.That(entry.Descriptor.DepthStencilFormat, Is.EqualTo(GraphicsFormat.D32_SFloat));
						Assert.That(entry.EstimatedBytes, Is.EqualTo(RenderTexturePool.EstimateBytes(entry.Descriptor)), "Depth attachment bytes must stay in the shared pool budget.");
					}

					var returned = pool.CaptureOwnershipSnapshot().Entries.Single(x => x.Descriptor.Width == 32 && x.Descriptor.Height == 18);
					Assert.That(returned.State, Is.EqualTo(TextureLeaseState.Free), "Scene output must return to the common pool when its session service is disposed.");
					Assert.That(pool.LeasedBytes, Is.EqualTo(0));
					Assert.That(returned.Descriptor.DepthStencilFormat, Is.EqualTo(GraphicsFormat.D32_SFloat));
				}
				finally {
					pool.Dispose();
				}
				yield return null;
			}
		}

		[UnityTest, Category("ResourceOwnership"), Category("Phase5"), Category("Phase9")]
		public IEnumerator RuntimeOutputService_DisabledAndUnreachableNodeKeepsActiveLease() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var session = CreateOutputServiceSession(out var sourceId, out var previewId, pool, RuntimeDynamicRange.Ldr))
			using (var service = new RuntimeOutputSurfaceService(session, pool, "service-retain-test", new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Ldr))) {
				session.OutputSurfaces = service;
				session.ResourcePreparation = service;
				session.ResourceFinalization = service;
				var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);
				var initial = coordinator.Tick(0d);
				var active = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, initial.FrameNumber);
				Assert.That(active.IsSuccess, Is.True, active.IsFailure ? active.Error.Message : string.Empty);
				var leaseId = active.Value.LeaseId;
				var texture = active.Value.NativeSurface;

				Assert.That(session.ApplyGraphCommand(new SetNodeEnabledEditCommand(sourceId, false)).IsSuccess, Is.True);
				var disabled = coordinator.Tick(1d / 60d);
				var disabledSurface = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, disabled.FrameNumber);
				Assert.That(disabledSurface.IsSuccess, Is.True, disabledSurface.IsFailure ? disabledSurface.Error.Message : string.Empty);
				Assert.That(disabledSurface.Value.LeaseId, Is.EqualTo(leaseId));
				Assert.That(disabledSurface.Value.NativeSurface, Is.SameAs(texture));

				Assert.That(session.ApplyGraphCommand(new SetNodeEnabledEditCommand(sourceId, true)).IsSuccess, Is.True);
				var reenabled = coordinator.Tick(2d / 60d);
				var reenabledSurface = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, reenabled.FrameNumber);
				Assert.That(reenabledSurface.IsSuccess, Is.True, reenabledSurface.IsFailure ? reenabledSurface.Error.Message : string.Empty);
				Assert.That(reenabledSurface.Value.LeaseId, Is.EqualTo(leaseId));

				var connection = session.Document.Connections.Single(x => x.SourceNodeId == sourceId && x.DestinationNodeId == previewId);
				Assert.That(session.ApplyGraphCommand(new DisconnectEditCommand(connection.Id)).IsSuccess, Is.True);
				var unreachable = coordinator.Tick(3d / 60d);
				var unreachableSurface = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, unreachable.FrameNumber);
				Assert.That(unreachableSurface.IsSuccess, Is.True, unreachableSurface.IsFailure ? unreachableSurface.Error.Message : string.Empty);
				Assert.That(unreachableSurface.Value.LeaseId, Is.EqualTo(leaseId));
				Assert.That(unreachableSurface.Value.NativeSurface, Is.SameAs(texture));
				yield return null;
			}
		}

		[UnityTest, Category("ResourceOwnership"), Category("Phase5"), Category("Phase9"), Category("FaultInjection")]
		public IEnumerator RuntimeOutputService_CandidateFailureHoldsActiveUntilLaterSuccess() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var session = CreateOutputServiceSession(out var sourceId, out var previewId, pool, RuntimeDynamicRange.Ldr))
			using (var service = new RuntimeOutputSurfaceService(session, pool, "service-candidate-test", new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Ldr))) {
				session.OutputSurfaces = service;
				session.ResourcePreparation = service;
				session.ResourceFinalization = service;
				var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);
				var initial = coordinator.Tick(0d);
				var active = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, initial.FrameNumber);
				Assert.That(active.IsSuccess, Is.True, active.IsFailure ? active.Error.Message : string.Empty);
				var oldLease = active.Value.LeaseId;
				var oldTexture = active.Value.NativeSurface;

				// Leave only the old lease budget available. Phase 5 cannot
				// acquire a larger candidate, so the old active must remain.
				Assert.That(pool.SetBudget(pool.LeasedBytes).IsSuccess, Is.True);
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 640, 360) }).IsSuccess, Is.True);
				var failed = coordinator.Tick(1d / 60d);
				Assert.That(failed.Succeeded, Is.False);
				var held = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, failed.FrameNumber);
				Assert.That(held.IsSuccess, Is.True, held.IsFailure ? held.Error.Message : string.Empty);
				Assert.That(held.Value.LeaseId, Is.EqualTo(oldLease));
				Assert.That(held.Value.NativeSurface, Is.SameAs(oldTexture));

				Assert.That(pool.SetBudget(64L * 1024L * 1024L).IsSuccess, Is.True);
				var recovered = coordinator.Tick(2d / 60d);
				var promoted = service.TryGetPrepared(sourceId, new PortId("image"), 640, 360, recovered.FrameNumber);
				Assert.That(promoted.IsSuccess, Is.True, promoted.IsFailure ? promoted.Error.Message : string.Empty);
				Assert.That(promoted.Value.LeaseId, Is.Not.EqualTo(oldLease));
				yield return null;
			}
		}

		[UnityTest, Category("ResourceOwnership"), Category("Generation"), Category("Phase5"), Category("Phase9")]
		public IEnumerator RuntimeOutputService_GenerationReplacementRetiresOldOnlyAtPhase9() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var session = CreateOutputServiceSession(out var sourceId, out var previewId, pool, RuntimeDynamicRange.Ldr))
			using (var service = new RuntimeOutputSurfaceService(session, pool, "service-generation-test", new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Ldr))) {
				session.OutputSurfaces = service;
				session.ResourcePreparation = service;
				session.ResourceFinalization = service;
				var coordinator = new FrameCoordinator(session, new GraphClock(new TestMonotonicSource()));
				Assert.That(session.SetOutputDemands(new[] { new OutputDemand(OutputTargetKind.Preview, previewId, new PortId("image"), 320, 180) }).IsSuccess, Is.True);
				var initial = coordinator.Tick(0d);
				var old = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, initial.FrameNumber);
				Assert.That(old.IsSuccess, Is.True, old.IsFailure ? old.Error.Message : string.Empty);
				var oldLease = old.Value.LeaseId;
				var oldTexture = old.Value.NativeSurface;
				var connection = session.Document.Connections.Single(x => x.SourceNodeId == sourceId && x.DestinationNodeId == previewId);
				var replacementType = new NodeTypeId("test.surface.output.replacement");
				var replacement = new NodeRecord(sourceId, replacementType, 1, "Replacement Source", true, new ProjectPosition(0, 0),
					ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Output, PortType.ImageFrame, false) });

				// Delete/add/connect in separate graph transactions to force a
				// new Runtime generation, then prepare before the coordinator
				// reaches Phase 9. The old controller must remain borrowable.
				Assert.That(session.ApplyGraphCommand(new DeleteNodeEditCommand(sourceId)).IsSuccess, Is.True);
				Assert.That(session.ApplyGraphCommand(new AddNodeEditCommand(replacement)).IsSuccess, Is.True);
				Assert.That(session.ApplyGraphCommand(new ConnectEditCommand(new ConnectionRecord(connection.Id, sourceId, new PortId("image"), previewId, new PortId("image")))).IsSuccess, Is.True);
				Assert.That(service.Prepare(initial.Snapshot).IsSuccess, Is.True);
				var replacementCandidate = service.TryGetPrepared(sourceId, new PortId("image"), 320, 180, initial.FrameNumber);
				Assert.That(replacementCandidate.IsSuccess, Is.True, replacementCandidate.IsFailure ? replacementCandidate.Error.Message : string.Empty);
				Assert.That(replacementCandidate.Value.LeaseId, Is.Not.EqualTo(oldLease));
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(x => x.LeaseId.Value == oldLease), Is.True, "Old generation must survive until Phase 9.");
				Assert.That(replacementCandidate.Value.NativeSurface, Is.Not.SameAs(oldTexture));

				var completed = coordinator.Tick(1d / 60d);
				Assert.That(completed.Succeeded, Is.True, string.Join("; ", completed.Diagnostics.Select(x => x.Message)));
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(x => x.LeaseId.Value == oldLease), Is.False, "Old generation must be released at Phase 9.");
				yield return null;
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TexturePool_DescriptorMismatch_DoesNotReuse() {
			using (var pool = new RenderTexturePool()) {
				var first = pool.Acquire(Descriptor(), Owner("a"), 1);
				var firstTexture = first.Value.Texture;
				Assert.That(first.Value.Release().IsSuccess, Is.True);
				var second = pool.Acquire(Descriptor(8, 4), Owner("b"), 2);
				Assert.That(second.IsSuccess, Is.True, second.IsFailure ? second.Error.Message : string.Empty);
				Assert.That(second.Value.Texture, Is.Not.EqualTo(firstTexture));
				Assert.That(second.Value.Release().IsSuccess, Is.True);
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TexturePool_BudgetEvictsFreeLruOnly() {
			using (var pool = new RenderTexturePool(128)) {
				var oldFree = pool.Acquire(Descriptor(), Owner("old"), 1);
				var leased = pool.Acquire(Descriptor(), Owner("leased"), 2);
				Assert.That(oldFree.Value.Release().IsSuccess, Is.True);
				var replacement = pool.Acquire(new TextureDescriptor(2, 2, ColorFormat), Owner("new"), 3);
				Assert.That(replacement.IsSuccess, Is.True, replacement.IsFailure ? replacement.Error.Message : string.Empty);
				Assert.That(leased.Value.IsReleased, Is.False);
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Any(entry => entry.Owner.OwnerId == "leased" && entry.State == TextureLeaseState.Leased), Is.True);
				Assert.That(replacement.Value.Release().IsSuccess, Is.True);
				Assert.That(leased.Value.Release().IsSuccess, Is.True);
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TexturePool_EstimateBytes_UsesBlockCountsMipsDepthAndMsaa() {
			// R_BC4 is Unity's available 4x4/8-byte block format (same block
			// arithmetic as BC1 for this pool accounting contract).
			var block8 = GraphicsFormat.R_BC4_UNorm;
			Assert.That(RenderTexturePool.EstimateLevelBytes(block8, 4, 4), Is.EqualTo(8));
			Assert.That(RenderTexturePool.EstimateLevelBytes(block8, 5, 4), Is.EqualTo(16));
			var mip = new TextureDescriptor(5, 4, block8, GraphicsFormat.None, 1, true);
			Assert.That(RenderTexturePool.EstimateBytes(mip), Is.EqualTo(32));

			var depthMsaa = new TextureDescriptor(3, 2, GraphicsFormat.R8G8B8A8_UNorm,
				GraphicsFormat.D24_UNorm_S8_UInt, 4, false);
			Assert.That(RenderTexturePool.EstimateBytes(depthMsaa), Is.EqualTo(192));

			var volume3d = new TextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm,
				GraphicsFormat.None, 1, true, false, TextureDimension.Tex3D, 4, false);
			Assert.That(RenderTexturePool.EstimateBytes(volume3d), Is.EqualTo(292));
			var array = new TextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm,
				GraphicsFormat.None, 1, true, false, TextureDimension.Tex2DArray, 4, false);
			Assert.That(RenderTexturePool.EstimateBytes(array), Is.EqualTo(336));
		}


		[Test, Category("ResourceOwnership")]
		public void TextureLease_DoubleReleaseAndOwnerGenerationMismatch_AreDetected() {
			using (var pool = new RenderTexturePool()) {
				var lease = pool.Acquire(Descriptor(), Owner("node", 2), 1);
				Assert.That(lease.IsSuccess, Is.True);
				Assert.That(lease.Value.Release(Owner("node", 1), 2).IsFailure, Is.True);
				Assert.That(lease.Value.Borrow(2).IsSuccess, Is.True);
				Assert.That(lease.Value.Release(Owner("node", 2), 2).IsSuccess, Is.True);
				Assert.That(lease.Value.Release(Owner("node", 2), 3).IsFailure, Is.True);
			}
		}

		[Test, Category("ResourceOwnership")]
		public void TextureLease_OwnershipSnapshot_IsReadOnlyAndTracksFreeAndLeased() {
			using (var pool = new RenderTexturePool()) {
				var lease = pool.Acquire(Descriptor(), Owner(), 4);
				var snapshot = pool.CaptureOwnershipSnapshot();
				Assert.That(snapshot.LeasedBytes, Is.GreaterThan(0));
				Assert.That(snapshot.Entries.Single().State, Is.EqualTo(TextureLeaseState.Leased));
				Assert.That(lease.Value.Release().IsSuccess, Is.True);
				Assert.That(pool.CaptureOwnershipSnapshot().Entries.Single().State, Is.EqualTo(TextureLeaseState.Free));
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TextureLease_CandidateRenderFails_KeepsActiveLease() {
			using (var pool = new RenderTexturePool())
			using (var output = new OutputPortController(pool, Owner())) {
				Assert.That(output.EnsureDemand(Descriptor(4, 4), 1).IsSuccess, Is.True);
				var initialFrame = output.CandidateLease.Borrow(1).Value.Frame;
				Assert.That(output.MarkCandidateRendered(initialFrame).IsSuccess, Is.True);
				Assert.That(output.CommitCandidate(initialFrame, 1).IsSuccess, Is.True);
				var active = output.ActiveLease;
				var candidate = output.BeginCandidate(Descriptor(8, 4), 2);
				Assert.That(candidate.IsSuccess, Is.True, candidate.IsFailure ? candidate.Error.Message : string.Empty);
				Assert.That(output.FailCandidate(3).IsSuccess, Is.True);
				Assert.That(output.ActiveLease, Is.SameAs(active));
				Assert.That(output.HasCandidate, Is.False);
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TextureLease_CandidateSuccess_SwapsAtCommitAndReleasesOld() {
			using (var pool = new RenderTexturePool())
			using (var output = new OutputPortController(pool, Owner())) {
				Assert.That(output.EnsureDemand(Descriptor(4, 4), 1).IsSuccess, Is.True);
				var initialFrame = output.CandidateLease.Borrow(1).Value.Frame;
				Assert.That(output.MarkCandidateRendered(initialFrame).IsSuccess, Is.True);
				Assert.That(output.CommitCandidate(initialFrame, 1).IsSuccess, Is.True);
				var old = output.ActiveLease;
				var candidate = output.BeginCandidate(Descriptor(8, 4), 2);
				var frame = candidate.Value.Borrow(2).Value.Frame;
				Assert.That(output.MarkCandidateRendered(frame).IsSuccess, Is.True);
				Assert.That(output.CommitCandidate(frame, 3).IsSuccess, Is.True);
				Assert.That(output.ActiveLease, Is.SameAs(candidate.Value));
				Assert.That(old.IsReleased, Is.True);
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void TextureLease_FirstCandidateRequiresRenderMarkAndExactMarkedFrame() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var output = new OutputPortController(pool, Owner())) {
				Assert.That(output.EnsureDemand(Descriptor(), 1).IsSuccess, Is.True);
				var marked = output.CandidateLease.Borrow(1).Value.Frame;
				Assert.That(output.CommitCandidate(marked, 1).IsFailure, Is.True);
				Assert.That(output.HasActive, Is.False);
				Assert.That(output.MarkCandidateRendered(marked).IsSuccess, Is.True);
				var changed = new ImageFrame(marked.Texture, marked.Size, marked.ColorFormat, 2, marked.LeaseId);
				Assert.That(output.CommitCandidate(changed, 2).IsFailure, Is.True);
				Assert.That(output.HasActive, Is.False);
				Assert.That(output.CommitCandidate(marked, 2).IsSuccess, Is.True);
				Assert.That(output.HasActive, Is.True);
			}
		}

		[Test, Category("FeedbackRuntimePolicy")]
		public void Feedback_DescriptorChangeSecondAcquireFailureKeepsOldPair() {
			using (var pool = new RenderTexturePool(72))
			using (var feedback = new FeedbackHistoryController(pool, Owner("feedback"))) {
				var oldDescriptor = Descriptor(1, 1);
				var changedDescriptor = Descriptor(4, 4);
				Assert.That(feedback.EnsureDescriptor(oldDescriptor, 1).IsSuccess, Is.True);
				var oldPrevious = feedback.PreviousLease;
				var oldNext = feedback.NextLease;
				Assert.That(feedback.EnsureDescriptor(changedDescriptor, 2).IsFailure, Is.True);
				Assert.That(feedback.HasHistory, Is.True);
				Assert.That(feedback.Descriptor, Is.EqualTo(oldDescriptor));
				Assert.That(feedback.PreviousLease, Is.SameAs(oldPrevious));
				Assert.That(feedback.NextLease, Is.SameAs(oldNext));
			}
		}

		[UnityTest, Category("FeedbackRuntimePolicy")]
		public IEnumerator Feedback_ResetClearsBothAndCommitFailurePreservesPrevious() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var feedback = new FeedbackHistoryController(pool, Owner("feedback"))) {
				var descriptor = Descriptor(4, 4);
				Assert.That(feedback.EnsureDescriptor(descriptor, 1).IsSuccess, Is.True);
				ClearTexture(feedback.PreviousLease.Texture, Color.red);
				ClearTexture(feedback.NextLease.Texture, Color.green);
				Assert.That(feedback.Reset(2).IsSuccess, Is.True);
				yield return null;
				Assert.That(ReadPixel(feedback.PreviousLease.Texture, 0, 0).a, Is.EqualTo(0).Within(0.01f));
				Assert.That(ReadPixel(feedback.NextLease.Texture, 0, 0).a, Is.EqualTo(0).Within(0.01f));
				Assert.That(feedback.LastResetFrame, Is.EqualTo(2));

				var wrongLease = pool.Acquire(Descriptor(2, 2), Owner("wrong"), 3);
				Assert.That(wrongLease.IsSuccess, Is.True);
				var previousBeforeFailure = feedback.PreviousLease;
				Assert.That(feedback.Commit(wrongLease.Value.Borrow(3).Value.Frame, 3).IsFailure, Is.True);
				Assert.That(feedback.PreviousLease, Is.SameAs(previousBeforeFailure));
				Assert.That(feedback.LastCommitFrame, Is.EqualTo(0));
				Assert.That(wrongLease.Value.Release().IsSuccess, Is.True);

				var currentLease = pool.Acquire(descriptor, Owner("current"), 4);
				Assert.That(currentLease.IsSuccess, Is.True);
				var beforeSwapPrevious = feedback.PreviousLease;
				var beforeSwapNext = feedback.NextLease;
				Assert.That(feedback.Commit(currentLease.Value.Borrow(4).Value.Frame, 4).IsSuccess, Is.True);
				Assert.That(feedback.PreviousLease, Is.SameAs(beforeSwapNext));
				Assert.That(feedback.NextLease, Is.SameAs(beforeSwapPrevious));
				Assert.That(feedback.LastCommitFrame, Is.EqualTo(4));
				Assert.That(currentLease.Value.Release().IsSuccess, Is.True);
			}
		}

		[Test, Category("ResolutionAndOutputPolicy")]
		public void ResolutionDemand_OrderingIsStable() {
			var program = new ResolutionDemand(ResolutionDemandKind.Program, new Vector2Int(1920, 1080), 0, new NodeInstanceId("program"));
			var previewA = new ResolutionDemand(ResolutionDemandKind.OtherPreview, new Vector2Int(800, 600), 1, new NodeInstanceId("a"));
			var previewB = new ResolutionDemand(ResolutionDemandKind.OtherPreview, new Vector2Int(1280, 720), 2, new NodeInstanceId("b"));
			var first = ResolutionDemandIntegrator.Merge(new[] { previewB, program, previewA });
			var second = ResolutionDemandIntegrator.Merge(new[] { previewA, previewB, program });
			Assert.That(first.IsSuccess, Is.True);
			Assert.That(second.IsSuccess, Is.True);
			Assert.That(first.Value.Size, Is.EqualTo(second.Value.Size));
			Assert.That(first.Value.Winner.NodeId, Is.EqualTo(program.NodeId));
			Assert.That(first.Value.Size, Is.EqualTo(new Vector2Int(1920, 1080)));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQuality_HighFrameTimeDropsAfterThirtyFrames() {
			var preview = new PreviewQualityController("preview");
			for (ulong frame = 1; frame <= 29; frame++) Assert.That(preview.ObserveFrameTime(16, 1, frame, true), Is.False);
			Assert.That(preview.ObserveFrameTime(16, 1, 30, true), Is.True);
			Assert.That(preview.QualityLevel, Is.EqualTo(1));
			Assert.That(preview.Stage.Size, Is.EqualTo(new Vector2Int(480, 270)));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQuality_LowFrameTimeRestoresAfter180FramesAndCooldown() {
			var preview = new PreviewQualityController("preview");
			for (ulong frame = 1; frame <= 30; frame++) preview.ObserveFrameTime(16, 16, frame, true);
			for (ulong frame = 31; frame <= 209; frame++) Assert.That(preview.ObserveFrameTime(13, 13, frame, true), Is.False);
			Assert.That(preview.ObserveFrameTime(13, 13, 210, true), Is.True);
			Assert.That(preview.QualityLevel, Is.EqualTo(0));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_RejectsNinthVisiblePreview() {
			var manager = new PreviewQualityManager();
			for (var i = 0; i < 8; i++) Assert.That(manager.Show("preview_" + i, false, i).IsSuccess, Is.True);
			Assert.That(manager.Show("preview_8", false, 8).IsFailure, Is.True);
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_RemoveReleasesClosedPreviewSlot() {
			var manager = new PreviewQualityManager();
			for (var i = 0; i < 8; i++) Assert.That(manager.Show("preview_" + i, false, i).IsSuccess, Is.True);
			manager.Remove(new NodeInstanceId("preview_3"));
			Assert.That(manager.Previews.Count, Is.EqualTo(7));
			Assert.That(manager.Show("replacement", false, 9).IsSuccess, Is.True);
			Assert.That(manager.Previews.Any(x => x.PreviewId == "preview_3"), Is.False);
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_HighThresholdDegradesOnlyOneStableCandidatePerThirtyFrames() {
			var manager = new PreviewQualityManager();
			Assert.That(manager.Show("old", false, 1).IsSuccess, Is.True);
			Assert.That(manager.Show("new", false, 2).IsSuccess, Is.True);
			for (ulong frame = 1; frame <= 30; frame++) manager.Observe(16, 16, frame);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "old").QualityLevel, Is.EqualTo(1));
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").QualityLevel, Is.EqualTo(0));
			for (ulong frame = 31; frame <= 59; frame++) manager.Observe(16, 16, frame);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").QualityLevel, Is.EqualTo(0));
			manager.Observe(16, 16, 60);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "old").QualityLevel, Is.EqualTo(2));
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").QualityLevel, Is.EqualTo(0));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_FocusOrderIsAllocatedOnlyOnFocusTransition() {
			var manager = new PreviewQualityManager();
			var oldId = new NodeInstanceId("old");
			var newId = new NodeInstanceId("new");
			manager.Ensure(oldId, true, 0);
			manager.Capture(oldId);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "old").IsFocused, Is.True);
			manager.Ensure(newId, true, 0);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "old").IsFocused, Is.False);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").IsFocused, Is.True);
			var focusOrder = manager.Previews.Single(x => x.PreviewId == "new").LastFocusOrder;
			manager.Ensure(newId, true, focusOrder + 100);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").LastFocusOrder, Is.EqualTo(focusOrder));
			manager.Ensure(newId, true, 0);
			for (ulong frame = 1; frame <= 30; frame++) manager.Observe(16, 16, frame);
			Assert.That(manager.Previews.Single(x => x.PreviewId == "old").QualityLevel, Is.EqualTo(1));
			Assert.That(manager.Previews.Single(x => x.PreviewId == "new").QualityLevel, Is.EqualTo(0));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_LowRecoveryKeepsQualificationAndRestoresEveryTwoSeconds() {
			var manager = new PreviewQualityManager();
			Assert.That(manager.Show("preview", false, 1).IsSuccess, Is.True);
			for (ulong frame = 1; frame <= 120; frame++) manager.Observe(16, 16, frame);
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(4));
			for (ulong frame = 121; frame <= 327; frame++) manager.Observe(13, 13, frame);
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(4));
			manager.Observe(13, 13, 328);
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(3));
			for (ulong frame = 329; frame <= 447; frame++) manager.Observe(13, 13, frame);
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(3));
			manager.Observe(13, 13, 448);
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(2));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void PreviewQualityManager_UnavailableTimingDoesNotInventQualityChanges() {
			var manager = new PreviewQualityManager();
			Assert.That(manager.Show("preview", false, 1).IsSuccess, Is.True);
			Assert.DoesNotThrow(() => manager.Observe(double.NaN, double.NaN, 1));
			Assert.That(manager.CpuFrameTimeAverage, Is.EqualTo(0d));
			Assert.That(manager.Previews.Single().QualityLevel, Is.EqualTo(0));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void DefaultPreviewQualityPolicy_UsesTheSameStableCandidateContract() {
			var policy = new DefaultPreviewQualityPolicy();
			var oldId = new NodeInstanceId("old");
			var newId = new NodeInstanceId("new");
			policy.Ensure(oldId, false, 1);
			policy.Ensure(newId, false, 2);
			for (ulong frame = 1; frame <= 60; frame++) policy.Observe(16, 16, frame);
			Assert.That(policy.Capture(oldId).QualityStage, Is.EqualTo(2));
			Assert.That(policy.Capture(newId).QualityStage, Is.EqualTo(0));
			policy.Observe(double.NaN, 16, 61);
			Assert.That(policy.Capture(oldId).QualityStage, Is.EqualTo(2));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void DefaultPreviewQualityPolicy_LowRecoveryKeepsQualificationAndRestoresEveryTwoSeconds() {
			var policy = new DefaultPreviewQualityPolicy();
			var id = new NodeInstanceId("preview");
			policy.Ensure(id, false, 1);
			for (ulong frame = 1; frame <= 120; frame++) policy.Observe(16, 16, frame);
			Assert.That(policy.Capture(id).QualityStage, Is.EqualTo(4));
			for (ulong frame = 121; frame <= 327; frame++) policy.Observe(13, 13, frame);
			Assert.That(policy.Capture(id).QualityStage, Is.EqualTo(4));
			policy.Observe(13, 13, 328);
			Assert.That(policy.Capture(id).QualityStage, Is.EqualTo(3));
			for (ulong frame = 329; frame <= 447; frame++) policy.Observe(13, 13, frame);
			policy.Observe(13, 13, 448);
			Assert.That(policy.Capture(id).QualityStage, Is.EqualTo(2));
		}

		[Test, Category("PreviewRuntimePolicy")]
		public void DefaultPreviewQualityPolicy_FocusOrderIsAllocatedOnlyOnFocusTransition() {
			var policy = new DefaultPreviewQualityPolicy();
			var oldId = new NodeInstanceId("old");
			var newId = new NodeInstanceId("new");
			policy.Ensure(oldId, true, 0);
			policy.Ensure(newId, true, 0);
			policy.Ensure(newId, true, 0);
			for (ulong frame = 1; frame <= 30; frame++) policy.Observe(16, 16, frame);
			Assert.That(policy.Capture(oldId).QualityStage, Is.EqualTo(1));
			Assert.That(policy.Capture(newId).QualityStage, Is.EqualTo(0));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void RuntimeFrameTimingSample_InvalidValuesAreUnavailableInsteadOfZero() {
			var sample = new RuntimeFrameTimingSample(12, 60, double.NaN, 4, true);
			Assert.That(sample.IsAvailable, Is.False);
			Assert.That(double.IsNaN(sample.CpuFrameMilliseconds), Is.True);
			Assert.That(double.IsNaN(sample.GpuFrameMilliseconds), Is.True);
			var explicitUnavailable = RuntimeFrameTimingSample.Unavailable(13);
			Assert.That(explicitUnavailable.IsAvailable, Is.False);
			Assert.That(double.IsNaN(explicitUnavailable.FramesPerSecond), Is.True);
			Assert.That(new RuntimeFrameTimingSample(14, 0, 1, 1, true).IsAvailable, Is.False);
		}

		[UnityTest, Category("ProgramRuntimePolicy")]
		public IEnumerator ProgramHold_UnavailableBeforeNormal_IsOpaqueBlack() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var hold = new ProgramHoldController(pool, new ResourceOwnerKey("session", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold))) {
				Assert.That(hold.SubmitUnavailable(1).IsSuccess, Is.True);
				yield return null;
				var frame = hold.GetFrame(1);
				Assert.That(frame.IsSuccess, Is.True, frame.IsFailure ? frame.Error.Message : string.Empty);
				var pixel = ReadPixel(frame.Value.Texture, 0, 0);
				Assert.That(pixel.r, Is.EqualTo(0).Within(1));
				Assert.That(pixel.g, Is.EqualTo(0).Within(1));
				Assert.That(pixel.b, Is.EqualTo(0).Within(1));
				Assert.That(pixel.a, Is.EqualTo(1).Within(0.01f));
			}
		}

		[UnityTest, Category("ProgramRuntimePolicy")]
		public IEnumerator ProgramHold_UnavailableAfterNormal_KeepsLastFrame() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var hold = new ProgramHoldController(pool, new ResourceOwnerKey("session", ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold))) {
				var programDescriptor = new TextureDescriptor(ProgramHoldController.ProgramSize.x,
					ProgramHoldController.ProgramSize.y, ProgramHoldController.DefaultColorFormat);
				var source = NewHdrTexture(ProgramHoldController.ProgramSize.x,
					ProgramHoldController.ProgramSize.y, new Color(0.25f, 0.5f, 0.75f, 1f));
				try {
					var sourceLease = pool.Acquire(programDescriptor, Owner("source"), 1);
					Assert.That(sourceLease.IsSuccess, Is.True, sourceLease.IsFailure ? sourceLease.Error.Message : string.Empty);
					Graphics.Blit(source, sourceLease.Value.Texture);
					var available = sourceLease.Value.Borrow(1).Value.Frame;
					Assert.That(hold.SubmitAvailable(available, 1).IsSuccess, Is.True);
					Assert.That(hold.SubmitUnavailable(2).IsSuccess, Is.True);
					yield return null;
					var frame = hold.GetFrame(2);
					Assert.That(frame.IsSuccess, Is.True);
					Assert.That(hold.State, Is.EqualTo(ProgramOutputState.HoldingLastFrame));
					Assert.That(ReadPixel(frame.Value.Texture, 0, 0).a, Is.EqualTo(1).Within(0.01f));
					Assert.That(sourceLease.Value.Release().IsSuccess, Is.True);
				}
				finally {
					UnityEngine.Object.DestroyImmediate(source);
				}
			}
		}

		[UnityTest, Category("ProgramRuntimePolicy"), Category("HDR"), Category("LDR")]
		public IEnumerator ProgramHold_NeutralFrameFormatMismatch_KeepsExistingHold() {
			using (var pool = new RenderTexturePool(128L * 1024L * 1024L))
			using (var hold = new ProgramHoldController(pool, Owner("program-format"), ProgramDynamicRange.Hdr)) {
				var hdr = NewHdrTexture(ProgramHoldController.ProgramSize.x, ProgramHoldController.ProgramSize.y, Color.white);
				var ldr = NewTexture(ProgramHoldController.ProgramSize.x, ProgramHoldController.ProgramSize.y, Color.black);
				try {
					var hdrFrame = new RuntimeSurfaceFrame(hdr, GraphicsFormat.R16G16B16A16_SFloat.ToString(), 1, 11);
					Assert.That(hold.SubmitAvailable(hdrFrame, 1).IsSuccess, Is.True);
					var lease = hold.HoldLease;
					Assert.That(lease, Is.Not.Null);
					Assert.That(hold.State, Is.EqualTo(ProgramOutputState.Available));
					var ldrFrame = new RuntimeSurfaceFrame(ldr, GraphicsFormat.R8G8B8A8_UNorm.ToString(), 2, 12);
					var mismatch = hold.SubmitAvailable(ldrFrame, 2);
					Assert.That(mismatch.IsFailure, Is.True);
					Assert.That(mismatch.Error.Code.Value, Is.EqualTo("rendering.program.format_mismatch"));
					Assert.That(hold.HoldLease, Is.SameAs(lease));
					Assert.That(hold.State, Is.EqualTo(ProgramOutputState.Available));
					yield return null;
				}
				finally {
					UnityEngine.Object.DestroyImmediate(hdr);
					UnityEngine.Object.DestroyImmediate(ldr);
				}
			}
		}

		[UnityTest, Category("DisplayTransformPolicy")]
		public IEnumerator DisplayTransform_LdrGpuReadback_UsesOpaqueBlack() {
			Assert.That(DisplayTransformPass.IsSupported(ColorFormat), Is.True,
				"RGBA8 render/sample support is required for the rendering contract.");
			using (var pass = new DisplayTransformPass(RequiredDisplayTransformShader())) {
				var source = NewTexture(4, 4, Color.clear);
				var destination = NewTexture(4, 4, Color.clear);
				try {
					pass.Blit(source, destination, DisplayTransformMode.Ldr);
					yield return null;
					var pixel = ReadPixel(destination, 1, 1);
					Assert.That(pixel.r, Is.EqualTo(0).Within(1));
					Assert.That(pixel.g, Is.EqualTo(0).Within(1));
					Assert.That(pixel.b, Is.EqualTo(0).Within(1));
					Assert.That(pixel.a, Is.EqualTo(1).Within(0.01f));
				}
				finally {
					UnityEngine.Object.DestroyImmediate(source);
					UnityEngine.Object.DestroyImmediate(destination);
				}
			}
		}

		[UnityTest, Category("DisplayTransformPolicy")]
		public IEnumerator DisplayTransform_ContentRectLeavesOpaqueBlackOutsideTheImage() {
			Assert.That(DisplayTransformPass.IsSupported(ColorFormat), Is.True,
				"RGBA8 render/sample support is required for the rendering contract.");
			using (var pass = new DisplayTransformPass(RequiredDisplayTransformShader())) {
				var source = NewTexture(4, 4, Color.white);
				var destination = NewTexture(4, 4, Color.clear);
				try {
					pass.Blit(source, destination, DisplayTransformMode.Ldr, new Vector4(0.25f, 0.25f, 0.5f, 0.5f));
					yield return null;
					var outside = ReadPixel(destination, 0, 0);
					var inside = ReadPixel(destination, 2, 2);
					Assert.That(outside.r, Is.EqualTo(0).Within(1));
					Assert.That(outside.g, Is.EqualTo(0).Within(1));
					Assert.That(outside.b, Is.EqualTo(0).Within(1));
					Assert.That(outside.a, Is.EqualTo(1).Within(0.01f));
					Assert.That(inside.r, Is.GreaterThan(0.9f));
					Assert.That(inside.g, Is.GreaterThan(0.9f));
					Assert.That(inside.b, Is.GreaterThan(0.9f));
				}
				finally {
					UnityEngine.Object.DestroyImmediate(source);
					UnityEngine.Object.DestroyImmediate(destination);
				}
			}
		}

		[UnityTest, Category("GPU"), Category("Probe"), Category("D3D12"), Category("Vulkan"), Category("Metal")]
		public IEnumerator PreviewDisplay_GpuReadback_ExercisesFitFillStretchAndBilinear() {
			Assert.That(DisplayTransformPass.IsSupported(ColorFormat), Is.True,
				"RGBA8 render/sample support is required for the preview contract.");

			using (var pass = new PreviewDisplayPass()) {
				var source = NewTexture(4, 2, Color.clear);
				var pattern = NewPatternTexture(4, 2,
					new Color(1f, 0f, 0f, 1f), new Color(0f, 0f, 1f, 1f));
				// Fill crops the 2:1 source to a 1:1 destination.  Use a
				// pattern whose pure edge colors occur only in the outer
				// texels; otherwise cropping one texel from each side still
				// produces the same pure red/blue edge as Stretch.
				var fillSource = NewTexture(4, 2, Color.clear);
				var fillPattern = NewColumnPatternTexture(4, 2,
					new Color(1f, 0f, 0f, 1f),
					new Color(0f, 1f, 0f, 1f),
					new Color(1f, 1f, 0f, 1f),
					new Color(0f, 0f, 1f, 1f));
				var fit = NewTexture(4, 4, Color.clear);
				var fill = NewTexture(4, 4, Color.clear);
				// A 4x4 Stretch is 1:1, so destination x=1 lands on source
				// texel 1 and cannot exercise a red/blue bilinear boundary.
				// Width 3 puts the destination center at uv=0.5, exactly
				// between source texels 1 and 2.
				var stretch = NewTexture(3, 4, Color.clear);
				try {
					var previousSrgbWrite = GL.sRGBWrite;
					GL.sRGBWrite = false;
					try { Graphics.Blit(pattern, source); }
					finally { GL.sRGBWrite = previousSrgbWrite; }
					previousSrgbWrite = GL.sRGBWrite;
					GL.sRGBWrite = false;
					try { Graphics.Blit(fillPattern, fillSource); }
					finally { GL.sRGBWrite = previousSrgbWrite; }
					pass.Blit(source, fit, PreviewDisplayMode.Fit);
					pass.Blit(fillSource, fill, PreviewDisplayMode.Fill);
					pass.Blit(source, stretch, PreviewDisplayMode.Stretch);
					yield return null;

					// Fit keeps the source aspect and leaves transparent bars.
					var fitPadding = ReadPixel(fit, 0, 0);
					var fitInterior = ReadPixel(fit, 1, 1);
					Assert.That(fitPadding.a, Is.LessThan(0.1f));
					Assert.That(fitInterior.a, Is.GreaterThan(0.9f), $"source={DescribeTexture(source)}; fit={DescribeTexture(fit)} padding={DescribePixel(fitPadding)} interior={DescribePixel(fitInterior)}");

					// Stretch covers the destination and the bilinear sample at
					// the center has both source edge colors.
					var stretchedLeft = ReadPixel(stretch, 0, 2);
					var stretchedRight = ReadPixel(stretch, 2, 2);
					var stretchDiagnostics = $"source={DescribeTexture(source)}; stretch={DescribeTexture(stretch)} left={DescribePixel(stretchedLeft)} right={DescribePixel(stretchedRight)}";
					Assert.That(stretchedLeft.r, Is.GreaterThan(0.7f), stretchDiagnostics);
					Assert.That(stretchedRight.b, Is.GreaterThan(0.7f), stretchDiagnostics);
					var bilinear = ReadPixel(stretch, 1, 2);
					Assert.That(bilinear.r, Is.GreaterThan(0.1f), $"{stretchDiagnostics}; bilinear={DescribePixel(bilinear)}");
					Assert.That(bilinear.b, Is.GreaterThan(0.1f), $"{stretchDiagnostics}; bilinear={DescribePixel(bilinear)}");

					// Fill center-crops the wide source; neither destination
					// edge is allowed to expose the original pure edge color.
					var filledLeft = ReadPixel(fill, 0, 2);
					var filledRight = ReadPixel(fill, 3, 2);
					var fillDiagnostics = $"source={DescribeTexture(fillSource)}; fill={DescribeTexture(fill)} left={DescribePixel(filledLeft)} right={DescribePixel(filledRight)}";
					Assert.That(filledLeft.r, Is.LessThan(0.95f), fillDiagnostics);
					Assert.That(filledRight.b, Is.LessThan(0.95f), fillDiagnostics);
				}
				finally {
					UnityEngine.Object.DestroyImmediate(pattern);
					UnityEngine.Object.DestroyImmediate(fillPattern);
					UnityEngine.Object.DestroyImmediate(source);
					UnityEngine.Object.DestroyImmediate(fillSource);
					UnityEngine.Object.DestroyImmediate(fit);
					UnityEngine.Object.DestroyImmediate(fill);
					UnityEngine.Object.DestroyImmediate(stretch);
				}
			}
		}

		[UnityTest, Category("GPU"), Category("Probe"), Category("D3D12"), Category("Vulkan"), Category("Metal")]
		public IEnumerator DisplayTransform_GpuReadback_ConvertsSrgbPremultipliesAndMapsHdr() {
			Assert.That(DisplayTransformPass.IsSupported(ColorFormat), Is.True,
				"RGBA8 render/sample support is required for the display contract.");

			using (var pass = new DisplayTransformPass(RequiredDisplayTransformShader())) {
				var source = NewLinearTexture(2, 2, new Color(0.5f, 0.5f, 0.5f, 0.5f));
				var linear = NewTexture(2, 2, Color.clear);
				var srgb = NewTexture(2, 2, Color.clear);
				var premultiplied = NewTexture(2, 2, Color.clear);
				try {
					pass.Blit(source, linear, DisplayTransformMode.Ldr, false, false);
					pass.Blit(source, srgb, DisplayTransformMode.Ldr, true, false);
					pass.Blit(source, premultiplied, DisplayTransformMode.Ldr, false, true);
					yield return null;
					var linearPixel = ReadPixel(linear, 0, 0);
					var srgbPixel = ReadPixel(srgb, 0, 0);
					var premultipliedPixel = ReadPixel(premultiplied, 0, 0);
					var ldrDiagnostics = $"source={DescribeTexture(source)}; linear={DescribeTexture(linear)} pixel={DescribePixel(linearPixel)}; srgb={DescribeTexture(srgb)} pixel={DescribePixel(srgbPixel)}; premultiplied={DescribeTexture(premultiplied)} pixel={DescribePixel(premultipliedPixel)}";
					Assert.That(linearPixel.a, Is.EqualTo(1f).Within(0.01f), ldrDiagnostics);
					Assert.That(srgbPixel.r, Is.LessThan(linearPixel.r));
					Assert.That(premultipliedPixel.r, Is.LessThan(linearPixel.r));

					Assert.That(DisplayTransformPass.IsSupported(GraphicsFormat.R16G16B16A16_SFloat), Is.True,
						"RGBA16F render/sample support is required for the HDR display contract.");
					{
						var hdrSource = NewHdrTexture(2, 2, new Color(4f, 0f, 0f, 1f));
						var hdrDestination = NewTexture(2, 2, Color.clear);
						try {
							pass.Blit(hdrSource, hdrDestination, DisplayTransformMode.HdrAces);
							yield return null;
							var hdrSourcePixel = ReadFloatPixel(hdrSource, 0, 0);
							var hdrPixel = ReadPixel(hdrDestination, 0, 0);
							var hdrDiagnostics = $"source={DescribeTexture(hdrSource)} expected=(4,0,0,1) readback={DescribePixel(hdrSourcePixel)}; destination={DescribeTexture(hdrDestination)} pixel={DescribePixel(hdrPixel)}";
							Assert.That(hdrPixel.a, Is.EqualTo(1f).Within(0.01f), hdrDiagnostics);
							Assert.That(hdrPixel.r, Is.GreaterThan(0.85f), hdrDiagnostics);
							Assert.That(hdrPixel.r, Is.LessThan(1.0f), hdrDiagnostics);
						}
						finally {
							UnityEngine.Object.DestroyImmediate(hdrSource);
							UnityEngine.Object.DestroyImmediate(hdrDestination);
						}
					}
					{
						var coloredHdrSource = NewHdrTexture(2, 2, new Color(4f, 1f, 0.25f, 1f));
						var coloredHdrDestination = NewTexture(2, 2, Color.clear);
						try {
							pass.Blit(coloredHdrSource, coloredHdrDestination, DisplayTransformMode.HdrAces);
							yield return null;
							var coloredHdrPixel = ReadPixel(coloredHdrDestination, 0, 0);
							var diagnostics = $"source={DescribeTexture(coloredHdrSource)} expected=(4,1,0.25,1); destination={DescribeTexture(coloredHdrDestination)} pixel={DescribePixel(coloredHdrPixel)}";
							Assert.That(coloredHdrPixel.r - coloredHdrPixel.g, Is.GreaterThan(0.25f), diagnostics);
							Assert.That(coloredHdrPixel.g, Is.GreaterThan(coloredHdrPixel.b), diagnostics);
						}
						finally {
							UnityEngine.Object.DestroyImmediate(coloredHdrSource);
							UnityEngine.Object.DestroyImmediate(coloredHdrDestination);
						}
					}
				}
				finally {
					UnityEngine.Object.DestroyImmediate(source);
					UnityEngine.Object.DestroyImmediate(linear);
					UnityEngine.Object.DestroyImmediate(srgb);
					UnityEngine.Object.DestroyImmediate(premultiplied);
				}
			}
		}

		[UnityTest, Category("GPU"), Category("ShaderBinding"), Category("D3D12"), Category("Vulkan"), Category("Metal")]
		public IEnumerator BuiltinGenerator_ColorParameterBinding_RendersMappedColor() {
			var shader = Shader.Find("Hidden/ShitDesigner/BuiltinGenerator");
			Assert.That(shader, Is.Not.Null, "BuiltinGenerator shader asset is required in the production player lane.");
			Assert.That(DisplayTransformPass.IsSupported(GraphicsFormat.R16G16B16A16_SFloat), Is.True,
				"RGBA16F render/sample support is required for the generator contract.");
			var material = new Material(shader);
			var target = NewHdrTexture(4, 4, Color.clear);
			try {
				var expected = new Color(0.25f, 0.5f, 0.75f, 1f);
				material.SetColor("_Color", expected);
				Graphics.Blit(Texture2D.whiteTexture, target, material);
				yield return null;
				var pixel = ReadPixel(target, 1, 1);
				Assert.That(pixel.r, Is.EqualTo(expected.r).Within(0.06f));
				Assert.That(pixel.g, Is.EqualTo(expected.g).Within(0.06f));
				Assert.That(pixel.b, Is.EqualTo(expected.b).Within(0.06f));
				Assert.That(pixel.a, Is.EqualTo(1f).Within(0.01f));
			}
			finally {
				UnityEngine.Object.DestroyImmediate(material);
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		[UnityTest, Category("GPU"), Category("ProgramRuntimePolicy"), Category("URP")]
		public IEnumerator UnityProgramDisplayPort_ProgramMonitorDoesNotRenderToPrimaryDisplay() {
			Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null,
				"The production display contract requires an active Scriptable Render Pipeline.");
			using (var port = new UnityProgramDisplayPort()) {
				var activated = port.Activate(ProgramDisplayPolicy.DefaultDisplay + 7);
				Assert.That(activated.IsSuccess, Is.True, activated.IsFailure ? activated.Error.Message : string.Empty);
				Assert.That(activated.Value.UsesProgramMonitor, Is.True);
				var surface = NewTexture(4, 4, Color.magenta);
				try {
					Assert.That(port.Present(surface, activated.Value).IsSuccess, Is.True);
					yield return null;
					Assert.That(GameObject.Find("ShitDesigner.ProgramDisplay"), Is.Null);
				}
				finally { UnityEngine.Object.DestroyImmediate(surface); }
			}
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramDisplayShader_IsAvailableToRuntime() {
			Assert.That(Resources.Load<Shader>("ProgramDisplay"), Is.Not.Null);
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void PoolBudget_DefaultsAndUserLimitsFollowPlatformCapability() {
			var dedicated = new RenderingPlatformCapabilities(RenderingMemoryKind.DedicatedGpu,
				8L * RenderingBudgetPolicy.GiB, 0, true, false);
			var budget = RenderingBudgetPolicy.DefaultBudget(dedicated, out var startup);
			Assert.That(startup, Is.Null);
			Assert.That(budget, Is.EqualTo(4L * RenderingBudgetPolicy.GiB));
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(dedicated, 8L * RenderingBudgetPolicy.GiB, 0).IsFailure, Is.True);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(dedicated, 6L * RenderingBudgetPolicy.GiB, 6L * RenderingBudgetPolicy.GiB).IsSuccess, Is.True);

			var unified = new RenderingPlatformCapabilities(RenderingMemoryKind.Unified,
				0, 16L * RenderingBudgetPolicy.GiB, false, true);
			Assert.That(RenderingBudgetPolicy.DefaultBudget(unified, out startup), Is.EqualTo(4L * RenderingBudgetPolicy.GiB));
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(unified, 7L * RenderingBudgetPolicy.GiB, 0).IsFailure, Is.True);
			var unknown = new RenderingPlatformCapabilities(RenderingMemoryKind.Unified);
			Assert.That(RenderingBudgetPolicy.DefaultBudget(unknown, out startup), Is.EqualTo(RenderingBudgetPolicy.UnknownMemoryBytes));
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void PoolBudget_DedicatedStartupDiagnosticAndExactUserBoundaries() {
			var low = new RenderingPlatformCapabilities(RenderingMemoryKind.DedicatedGpu,
				RenderingBudgetPolicy.GiB, 0, true, false);
			var lowBudget = RenderingBudgetPolicy.DefaultBudget(low, out var diagnostic);
			Assert.That(lowBudget, Is.LessThan(RenderingBudgetPolicy.MinimumStartupBudgetBytes));
			Assert.That(diagnostic, Is.Not.Null);
			Assert.That(diagnostic.Code.Value, Is.EqualTo("rendering.pool.startup_budget_low"));

			var dedicated = new RenderingPlatformCapabilities(RenderingMemoryKind.DedicatedGpu,
				8L * RenderingBudgetPolicy.GiB, 0, true, false);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(dedicated, 7L * RenderingBudgetPolicy.GiB, 0).IsSuccess, Is.True);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(dedicated, 7L * RenderingBudgetPolicy.GiB + 1, 0).IsFailure, Is.True);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(dedicated, 1, 2).IsFailure, Is.True);

			var unified = new RenderingPlatformCapabilities(RenderingMemoryKind.Unified, 0, 16L * RenderingBudgetPolicy.GiB, false, true);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(unified, (long)(16L * RenderingBudgetPolicy.GiB * 0.40d), 0).IsSuccess, Is.True);
			Assert.That(RenderingBudgetPolicy.ValidateUserBudget(unified, (long)(16L * RenderingBudgetPolicy.GiB * 0.40d) + 1, 0).IsFailure, Is.True);
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void PoolBudget_EightyFivePercentWarningIsEdgeTriggeredAndRecovers() {
			using (var pool = new RenderTexturePool(75)) {
				var lease = pool.Acquire(Descriptor(4, 4), Owner("warning"), 1);
				Assert.That(lease.IsSuccess, Is.True);
				Assert.That(pool.BudgetWarningActive, Is.True);
				Assert.That(pool.Diagnostics.Count(d => d.Code.Value == "rendering.pool.budget_warning"), Is.EqualTo(1));
				Assert.That(lease.Value.Release().IsSuccess, Is.True);
				Assert.That(pool.Diagnostics.Count(d => d.Code.Value == "rendering.pool.budget_warning"), Is.EqualTo(1));
				Assert.That(pool.TrimFree(601), Is.EqualTo(1));
				Assert.That(pool.BudgetWarningActive, Is.False);
				Assert.That(pool.Diagnostics.Count(d => d.Code.Value == "rendering.pool.budget_recovered"), Is.EqualTo(1));
				Assert.That(pool.TrimFree(1201), Is.EqualTo(0));
				Assert.That(pool.Diagnostics.Count(d => d.Code.Value == "rendering.pool.budget_recovered"), Is.EqualTo(1));
			}
		}

		[Test, Category("RenderTexturePoolPolicy")]
		public void PoolBudget_SetBudgetCannotDropBelowLeasedBytes() {
			using (var pool = new RenderTexturePool(1024)) {
				var lease = pool.Acquire(Descriptor(1, 1), Owner("budget"), 1);
				Assert.That(lease.IsSuccess, Is.True);
				Assert.That(pool.SetBudget(pool.LeasedBytes).IsSuccess, Is.True);
				Assert.That(pool.SetBudget(pool.LeasedBytes - 1).IsFailure, Is.True);
				Assert.That(lease.Value.Release().IsSuccess, Is.True);
			}
		}

		[Test, Category("ResourceOwnership")]
		public void TexturePool_DisposeMarksHandlesReleasedAndDestroysEntries() {
			var pool = new RenderTexturePool(64L * 1024L * 1024L);
			var lease = pool.Acquire(Descriptor(), Owner("dispose"), 1);
			Assert.That(lease.IsSuccess, Is.True);
			pool.Dispose();
			Assert.That(lease.Value.IsReleased, Is.True);
			Assert.That(pool.CurrentBytes, Is.EqualTo(0));
			Assert.That(pool.CaptureOwnershipSnapshot().Entries, Is.Empty);
		}

		[Test, Category("DisplayTransformPolicy")]
		public void PreviewDisplayTransform_FitFillAndStretchFollowContract() {
			var fit = PreviewDisplayTransform.Calculate(new Vector2Int(16, 9), new Vector2Int(4, 4), PreviewDisplayMode.Fit);
			Assert.That(fit.DestinationRect, Is.EqualTo(new RectInt(0, 1, 4, 2)));
			Assert.That(fit.HasTransparentPadding, Is.True);
			var fill = PreviewDisplayTransform.Calculate(new Vector2Int(16, 9), new Vector2Int(4, 4), PreviewDisplayMode.Fill);
			Assert.That(fill.DestinationRect, Is.EqualTo(new RectInt(0, 0, 4, 4)));
			Assert.That(fill.SourceRect.width, Is.LessThan(1f));
			var stretch = PreviewDisplayTransform.Calculate(new Vector2Int(16, 9), new Vector2Int(4, 4), PreviewDisplayMode.Stretch);
			Assert.That(stretch.SourceRect, Is.EqualTo(new Rect(0, 0, 1, 1)));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramPerformance_WarnsOnlyAfterSixtyConsecutiveBadFrames() {
			var monitor = new ProgramPerformanceMonitor();
			for (var i = 0; i < 59; i++) Assert.That(monitor.Observe(58, 17, 1), Is.False);
			Assert.That(monitor.Observe(58, 17, 1), Is.True);
			Assert.That(monitor.Observe(60, 10, 10), Is.False);
			Assert.That(monitor.Current.ConsecutiveBadFrames, Is.EqualTo(0));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramPerformance_UnavailableSampleResetsConsecutiveWarningState() {
			var monitor = new ProgramPerformanceMonitor();
			var sink = (IRuntimeProgramPerformanceSink)monitor;
			Assert.That(sink.Capture().IsAvailable, Is.False);
			for (var i = 0; i < ProgramPerformanceMonitor.WarningFrameCount; i++) sink.Observe(58, 17, 17);
			Assert.That(sink.Capture().WarningActive, Is.True);
			sink.Reset();
			Assert.That(sink.Capture().IsAvailable, Is.False);
			sink.Observe(60, 10, 10);
			Assert.That(sink.Capture().WarningActive, Is.False);
			Assert.That(sink.Capture().ConsecutiveBadFrames, Is.EqualTo(0));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramHold_DefaultsToFixedHdrAndSupportsExplicitLdr() {
			Assert.That(ProgramHoldFormatPolicy.FormatFor(ProgramDynamicRange.Hdr), Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
			Assert.That(ProgramHoldFormatPolicy.FormatFor(ProgramDynamicRange.Ldr), Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
			Assert.That(ProgramHoldController.ProgramSize, Is.EqualTo(new Vector2Int(1920, 1080)));
			Assert.That(ProgramHoldFormatPolicy.DisplayModeFor(ProgramDynamicRange.Hdr), Is.EqualTo(DisplayTransformMode.HdrAces));
			Assert.That(ProgramHoldFormatPolicy.DisplayModeFor(ProgramDynamicRange.Ldr), Is.EqualTo(DisplayTransformMode.Ldr));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramDisplay_DefaultsToDisplayTwoAndFallsBackToMonitor() {
			var external = ProgramDisplayPolicy.Resolve(displayCount: 2);
			Assert.That(external.ResolvedDisplay, Is.EqualTo(1));
			Assert.That(external.UsesProgramMonitor, Is.False);
			var fallback = ProgramDisplayPolicy.Resolve(displayCount: 1);
			Assert.That(fallback.ResolvedDisplay, Is.EqualTo(0));
			Assert.That(fallback.UsesProgramMonitor, Is.True);
		}

		[TestCase(16f / 9f, 16f / 9f, 16f / 9f, 1f)]
		[TestCase(16f / 9f, 4f / 3f, 16f / 9f, 1f)]
		[TestCase(16f / 9f, 16f / 10f, 16f / 9f, 1f)]
		[TestCase(16f / 9f, 21f / 9f, 21f / 9f, 21f / 16f)]
		[TestCase(16f / 9f, 9f / 16f, 16f / 9f, 1f)]
		public void ProgramDisplayFillLayout_PreservesAspectAndCropsOverflow(
			float sourceAspect, float targetAspect, float expectedWidth, float expectedHeight) {
			var scale = ProgramDisplayFillLayout.Scale(sourceAspect, targetAspect);

			Assert.That(scale.x, Is.EqualTo(expectedWidth).Within(0.0001f));
			Assert.That(scale.y, Is.EqualTo(expectedHeight).Within(0.0001f));
			Assert.That(scale.x / scale.y, Is.EqualTo(sourceAspect).Within(0.0001f));
			Assert.That(scale.x, Is.GreaterThanOrEqualTo(targetAspect));
			Assert.That(scale.y, Is.GreaterThanOrEqualTo(1f));
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramDisplay_ProjectNumbersConvertToZeroBasedUnityIndices() {
			Assert.That(ProgramDisplayPolicy.ToUnityIndex(1), Is.EqualTo(0));
			Assert.That(ProgramDisplayPolicy.ToUnityIndex(2), Is.EqualTo(1));
			Assert.That(ProgramDisplayPolicy.ToUnityIndex(3), Is.EqualTo(2));
			foreach (var count in new[] { 1, 2, 3 })
				foreach (var projectNumber in new[] { 1, 2, 3 }) {
					var unityIndex = ProgramDisplayPolicy.ToUnityIndex(projectNumber);
					var selection = ProgramDisplayPolicy.Resolve(unityIndex, count);
					Assert.That(selection.ResolvedDisplay, Is.EqualTo(projectNumber <= count ? unityIndex : 0));
					Assert.That(selection.UsesProgramMonitor, Is.EqualTo(projectNumber > count));
				}
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramDisplayPort_ActivatesRequestedDisplayOnlyWhenOutputStartsAndClosingMonitorKeepsEvaluation() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var hold = new ProgramHoldController(pool, Owner("program"))) {
				var port = new FakeDisplayPort(3);
				var presenter = new ProgramDisplayPresenter(hold, port, 1);
				Assert.That(port.LastRequestedDisplay, Is.EqualTo(-1));
				Assert.That(presenter.IsOutputActive, Is.False);
				Assert.That(presenter.SetRequestedDisplay(2).IsSuccess, Is.True);
				Assert.That(port.LastRequestedDisplay, Is.EqualTo(-1));
				Assert.That(presenter.SetOutputActive(true).IsSuccess, Is.True);
				Assert.That(port.LastRequestedDisplay, Is.EqualTo(2));
				Assert.That(presenter.Selection.ResolvedDisplay, Is.EqualTo(2));
				presenter.CloseMonitor();
				Assert.That(presenter.MonitorOpen, Is.False);
				Assert.That(presenter.EvaluationContinues, Is.True);
			}
		}

		[Test, Category("ProgramRuntimePolicy")]
		public void ProgramDisplayPresenter_DisablingOutputStopsPresentationButKeepsProgramAvailable() {
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L))
			using (var hold = new ProgramHoldController(pool, Owner("program-output-control"))) {
				var port = new FakeDisplayPort(2);
				var presenter = new ProgramDisplayPresenter(hold, port, 1);
				var surface = NewTexture(2, 2, Color.black);
				try {
					Assert.That(port.ActivateCount, Is.EqualTo(0));
					presenter.SetOutputActive(false);
					Assert.That(presenter.IsOutputActive, Is.False);
					Assert.That(port.IsOutputActive, Is.False);
					Assert.That(presenter.Present(surface).IsSuccess, Is.True);
					Assert.That(port.PresentCount, Is.EqualTo(0));
					Assert.That(presenter.SetOutputActive(true).IsSuccess, Is.True);
					Assert.That(port.ActivateCount, Is.EqualTo(1));
					Assert.That(port.IsOutputActive, Is.True);
					Assert.That(presenter.Present(surface).IsSuccess, Is.True);
					Assert.That(port.PresentCount, Is.EqualTo(1));
				}
				finally { UnityEngine.Object.DestroyImmediate(surface); }
			}
		}

		private static RuntimeSession CreateOutputServiceSession(out NodeInstanceId previewSourceId, out NodeInstanceId previewId, RenderTexturePool pool, RuntimeDynamicRange range, string sourceTypeId = "test.surface.output") {
			var document = new ProjectDocument("Output surface service test");
			var commands = new ProjectCommandProcessor(document);
			var surfaceType = new NodeTypeId(sourceTypeId);
			var replacementType = new NodeTypeId("test.surface.output.replacement");
			var imagePort = new PortId("image");
			var surfacePorts = new[] { new PortSnapshotRecord(imagePort, PortDirection.Output, PortType.ImageFrame, false) };
			previewSourceId = NodeInstanceId.New();
			var programSourceId = NodeInstanceId.New();
			previewId = NodeInstanceId.New();
			var programId = NodeInstanceId.New();
			var previewSource = new NodeRecord(previewSourceId, surfaceType, 1, "Preview Source", true, new ProjectPosition(0, 0), ports: surfacePorts);
			var programSource = new NodeRecord(programSourceId, surfaceType, 1, "Program Source", true, new ProjectPosition(0, 1), ports: surfacePorts);
			var preview = new NodeRecord(previewId, new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", true, new ProjectPosition(1, 0), ports: new[] { new PortSnapshotRecord(imagePort, PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false);
			var program = new NodeRecord(programId, new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", true, new ProjectPosition(1, 1), ports: new[] { new PortSnapshotRecord(imagePort, PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false);
			Assert.That(commands.AddNode(previewSource).IsSuccess, Is.True);
			Assert.That(commands.AddNode(programSource).IsSuccess, Is.True);
			Assert.That(commands.AddNode(preview).IsSuccess, Is.True);
			Assert.That(commands.AddNode(program).IsSuccess, Is.True);
			Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), previewSourceId, imagePort, previewId, imagePort)).IsSuccess, Is.True);
			Assert.That(commands.Connect(new ConnectionRecord(ConnectionId.New(), programSourceId, imagePort, programId, imagePort)).IsSuccess, Is.True);

			var registry = new NodeTypeRegistry();
			Assert.That(registry.Register(new NodeTypeDefinition(surfaceType, 1, "Surface", "Test", new[] { new PortDefinition(imagePort, "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(registry.Register(new NodeTypeDefinition(replacementType, 1, "Replacement", "Test", new[] { new PortDefinition(imagePort, "Image", PortDirection.Output, PortType.ImageFrame, false) })).IsSuccess, Is.True);
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.PreviewTypeId), 1, "Preview", "System", new[] { new PortDefinition(imagePort, "Image", PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false)).IsSuccess, Is.True);
			Assert.That(registry.Register(new NodeTypeDefinition(new NodeTypeId(GraphConstants.ProgramOutputTypeId), 1, "Program", "System", new[] { new PortDefinition(imagePort, "Image", PortDirection.Input, PortType.ImageFrame, true) }, systemOwned: true, userAddable: false)).IsSuccess, Is.True);
			var session = new RuntimeSession(document, registry);
			Assert.That(session.RegisterFactory(new SurfaceFactory(surfaceType)).IsSuccess, Is.True);
			Assert.That(session.RegisterFactory(new SurfaceFactory(replacementType)).IsSuccess, Is.True);
			return session;
		}

		private sealed class TestMonotonicSource : IMonotonicClock {
			public double Now => 0d;
		}

		private sealed class SurfaceFactory : IRuntimeNodeFactory {
			public NodeTypeId TypeId { get; }
			public SurfaceFactory(NodeTypeId typeId) { TypeId = typeId; }
			public Result<IRuntimeNode, Diagnostic> Create(RuntimeNodeCreateInfo node, ulong generationId) =>
				Result.Success<IRuntimeNode, Diagnostic>(new SurfaceNode(node.Id, TypeId, generationId));
		}

		private sealed class SurfaceNode : IRuntimeNode {
			public NodeInstanceId NodeId { get; }
			public NodeTypeId TypeId { get; }
			public ulong GenerationId { get; }
			public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Ready;
			public SurfaceNode(NodeInstanceId nodeId, NodeTypeId typeId, ulong generationId) { NodeId = nodeId; TypeId = typeId; GenerationId = generationId; }
			public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) {
				var image = new PortId("image");
				if (!context.RequestedOutputs.Contains(image)) return;
				if (!RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, image, out var demand)) {
					outputs.SetPreparing(image, new Diagnostic(new DiagnosticCode("test.surface.demand_missing"), Severity.Error, "Test surface demand is missing."));
					return;
				}
				var surface = context.OutputSurfaces?.TryGetPrepared(NodeId, image, demand.Width, demand.Height, context.Snapshot.FrameNumber);
				if (!surface.HasValue || surface.Value.IsFailure) {
					outputs.SetPreparing(image, surface.HasValue ? surface.Value.Error : new Diagnostic(new DiagnosticCode("test.surface.missing"), Severity.Error, "Test surface was not prepared."));
					return;
				}
				var target = surface.Value.Value.NativeSurface as RenderTexture;
				if (target == null) throw new InvalidOperationException("Test surface did not expose a RenderTexture.");
				Graphics.Blit(Texture2D.whiteTexture, target);
				var completion = surface.Value.Value as IRuntimeOutputSurfaceCompletion;
				Assert.That(completion, Is.Not.Null);
				Assert.That(completion.MarkRendered().IsSuccess, Is.True);
				outputs.SetAvailable(image, PortValue.FromImageFrame(new SurfaceFrame(surface.Value.Value, context.Snapshot.FrameNumber)));
				State = RuntimeNodeState.Ready;
			}
			public void Dispose() { State = RuntimeNodeState.Disposed; }
		}

		private sealed class SurfaceFrame : IRuntimeImageFrameSurface {
			private readonly IRuntimeOutputSurface _surface;
			public SurfaceFrame(IRuntimeOutputSurface surface, ulong frameNumber) { _surface = surface; FrameNumber = frameNumber; }
			public int Width => _surface.Width;
			public int Height => _surface.Height;
			public string ColorFormat => (_surface as IRuntimeOutputSurfaceFormat)?.ColorFormat ?? GraphicsFormat.R16G16B16A16_SFloat.ToString();
			public ulong FrameNumber { get; }
			public ulong LeaseId => _surface.LeaseId;
			public object NativeSurface => _surface.NativeSurface;
		}

		private static Shader RequiredDisplayTransformShader() {
			var shader = Shader.Find("Hidden/ShitDesigner/DisplayTransform");
			Assert.That(shader, Is.Not.Null, "DisplayTransform must be available to the rendering contract test asset set.");
			return shader;
		}

		private static RenderTexture NewTexture(int width, int height, Color clear) {
			// Rendering contract surfaces are linear, including the LDR
			// R8G8B8A8 probe targets.  The four-argument RenderTexture
			// constructor inherits project sRGB defaults; on a Linear D3D12
			// project that makes ReadPixels decode the shader's explicit
			// display transfer a second time (0.735 becomes 0.502).  Use an
			// explicit linear descriptor so readback observes the value
			// produced by the pass.
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None) {
				msaaSamples = 1,
				sRGB = false,
				useMipMap = false,
				autoGenerateMips = false
			};
			var texture = new RenderTexture(descriptor) { name = "ShitDesigner.TestTexture" };
			texture.Create();
			ClearTexture(texture, clear);
			return texture;
		}

		private static Texture2D NewPatternTexture(int width, int height, Color left, Color right) {
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) {
				name = "ShitDesigner.PatternTexture",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var pixels = new Color[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					pixels[y * width + x] = x < width / 2 ? left : right;
			texture.SetPixels(pixels);
			texture.Apply(false, false);
			return texture;
		}

		private static Texture2D NewColumnPatternTexture(int width, int height, params Color[] columns) {
			if (columns == null || columns.Length != width)
				throw new ArgumentException("A column pattern must provide one color per source texel.", nameof(columns));

			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) {
				name = "ShitDesigner.FillPatternTexture",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var pixels = new Color[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					pixels[y * width + x] = columns[x];
			texture.SetPixels(pixels);
			texture.Apply(false, false);
			return texture;
		}

		private static RenderTexture NewHdrTexture(int width, int height, Color clear) {
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None) {
				msaaSamples = 1,
				sRGB = false,
				useMipMap = false,
				autoGenerateMips = false
			};
			var texture = new RenderTexture(descriptor) { name = "ShitDesigner.HdrTestTexture" };
			texture.Create();
			if (clear.r > 1f || clear.g > 1f || clear.b > 1f || clear.a > 1f) {
				// GL.Clear clamps values above one on the D3D12 backend even
				// for an RGBA16F target.  Seed the probe from a linear float
				// texture instead: this preserves the exact HDR input while
				// still exercising the production fullscreen texture-sample
				// path used by DisplayTransform.
				var floatSource = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true) {
					name = "ShitDesigner.HdrProbeSource",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
				var pixels = new Color[width * height];
				for (var index = 0; index < pixels.Length; index++) pixels[index] = clear;
				floatSource.SetPixels(pixels);
				floatSource.Apply(false, false);
				var previousSrgbWrite = GL.sRGBWrite;
				GL.sRGBWrite = false;
				try {
					Graphics.Blit(floatSource, texture);
				}
				finally {
					GL.sRGBWrite = previousSrgbWrite;
					UnityEngine.Object.DestroyImmediate(floatSource);
				}
			}
			else ClearTexture(texture, clear);
			return texture;
		}

		private static RenderTexture NewLinearTexture(int width, int height, Color clear) {
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None) {
				msaaSamples = 1,
				sRGB = false,
				useMipMap = false,
				autoGenerateMips = false
			};
			var texture = new RenderTexture(descriptor) { name = "ShitDesigner.LinearTestTexture" };
			texture.Create();
			ClearTexture(texture, clear);
			return texture;
		}

		private static void ClearTexture(RenderTexture texture, Color color) {
			var previous = RenderTexture.active;
			var previousSrgbWrite = GL.sRGBWrite;
			GL.sRGBWrite = false;
			try {
				RenderTexture.active = texture;
				GL.Clear(true, true, color);
			}
			finally {
				RenderTexture.active = previous;
				GL.sRGBWrite = previousSrgbWrite;
			}
		}

		private static Color ReadPixel(RenderTexture texture, int x, int y) {
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
			readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
			readback.Apply(false, false);
			var pixel = readback.GetPixel(0, 0);
			UnityEngine.Object.DestroyImmediate(readback);
			RenderTexture.active = previous;
			return pixel;
		}

		private static Color ReadFloatPixel(RenderTexture texture, int x, int y) {
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			var readback = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
			readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
			readback.Apply(false, false);
			var pixel = readback.GetPixel(0, 0);
			UnityEngine.Object.DestroyImmediate(readback);
			RenderTexture.active = previous;
			return pixel;
		}

		private static string DescribePixel(Color pixel) =>
			$"rgba=({pixel.r:F6},{pixel.g:F6},{pixel.b:F6},{pixel.a:F6})";

		private static string DescribeTexture(RenderTexture texture) {
			if (texture == null) return "<null>";
			var descriptor = texture.descriptor;
			return $"name={texture.name}; size={texture.width}x{texture.height}; graphics={texture.graphicsFormat}; descriptorGraphics={descriptor.graphicsFormat}; sRGB={descriptor.sRGB}; format={texture.format}; created={texture.IsCreated()}";
		}

		private sealed class FakeDisplayPort : IProgramDisplayPort {
			public int DisplayCount { get; }
			public int LastRequestedDisplay { get; private set; } = -1;
			public int ActivateCount { get; private set; }
			public int PresentCount { get; private set; }
			public bool IsOutputActive { get; private set; }

			public FakeDisplayPort(int displayCount) { DisplayCount = displayCount; }

			public Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay) {
				LastRequestedDisplay = requestedDisplay;
				ActivateCount++;
				return Result.Success<ProgramDisplaySelection, Diagnostic>(ProgramDisplayPolicy.Resolve(requestedDisplay, DisplayCount));
			}

			public UnitResult<Diagnostic> Present(RenderTexture surface, ProgramDisplaySelection selection) {
				PresentCount++;
				return UnitResult.Success<Diagnostic>();
			}

			public void SetOutputActive(bool active) {
				IsOutputActive = active;
			}
		}

		private sealed class RuntimeSurfaceFrame : IRuntimeImageFrameSurface {
			private readonly RenderTexture _texture;
			public int Width => _texture.width;
			public int Height => _texture.height;
			public string ColorFormat { get; }
			public ulong FrameNumber { get; }
			public ulong LeaseId { get; }
			public object NativeSurface => _texture;
			public RuntimeSurfaceFrame(RenderTexture texture, string colorFormat, ulong frameNumber, ulong leaseId) { _texture = texture; ColorFormat = colorFormat; FrameNumber = frameNumber; LeaseId = leaseId; }
		}

		private sealed class FakeFormatCapabilities : IRenderingPlatformCapabilityPort {
			private readonly Func<GraphicsFormat, GraphicsFormatUsage, bool> _support;
			public FakeFormatCapabilities(Func<GraphicsFormat, GraphicsFormatUsage, bool> support) { _support = support; }
			public RenderingPlatformCapabilities Capabilities => default(RenderingPlatformCapabilities);
			public bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage) => _support(format, usage);
		}
	}
}
