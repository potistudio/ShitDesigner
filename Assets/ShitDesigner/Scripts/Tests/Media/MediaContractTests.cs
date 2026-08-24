using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShitDesigner.Tests.Media {
	public sealed class MediaContractTests {
		private static readonly MediaAssetId Asset = new MediaAssetId("11111111-1111-4111-8111-111111111111");
		private static readonly MediaAssetId HapAsset = new MediaAssetId("22222222-2222-4222-8222-222222222222");
		private static readonly MediaAssetId AlternateAsset = new MediaAssetId("33333333-3333-4333-8333-333333333333");

		[Test]
		public void Transport_ClampsSeekAndLoopsAtDuration() {
			var transport = new VideoTransportState();
			Assert.That(transport.SetMediaAsset(Asset).IsSuccess, Is.True);
			Assert.That(transport.SetDuration(2d).IsSuccess, Is.True);
			Assert.That(transport.SetPlaying(true).IsSuccess, Is.True);
			Assert.That(transport.Advance(1.5d).IsSuccess, Is.True);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(1.5d).Within(.0001));
			Assert.That(transport.Advance(1d).IsSuccess, Is.True);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(.5d).Within(.0001));
			Assert.That(transport.Playing, Is.True);
		}

		[Test]
		public void Transport_StopsAtEofWhenLoopDisabled() {
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(1d);
			transport.SetLoop(false);
			transport.SetPlaying(true);
			transport.Advance(2d);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(1d));
			Assert.That(transport.Playing, Is.False);
		}

		[Test]
		public void Transport_SpeedZeroHoldsPlayheadWithoutCreatingEof() {
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(1d);
			transport.SetSpeed(0d);
			transport.SetPlaying(true);
			transport.Advance(5d);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(0d));
			Assert.That(transport.Playing, Is.True);
		}

		[Test]
		public void TransportController_AnchorsGraphClockAndWrapsLoopingPosition() {
			var node = new NodeInstanceId("77777777-7777-4777-8777-777777777777");
			var backend = new UnityVideoBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var transport = new VideoTransportState();
			transport.SetDuration(2d);
			transport.SetPlaying(true);
			var controller = new VideoTransportController(session, transport);
			Assert.That(controller.LogicalPosition(0d), Is.EqualTo(0d).Within(.0001));
			Assert.That(controller.LogicalPosition(2.5d), Is.EqualTo(.5d).Within(.0001));
			session.Dispose();
		}

		[Test]
		public void Source_ResolvesContainedProjectRelativeFileOnly() {
			var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var file = VideoSource.FromProjectFile(root, "clip.mp4");
			Assert.That(file.Value, Does.StartWith(root));
			Assert.Throws<ArgumentException>(() => VideoSource.Parse("clip.mp4"));
			Assert.Throws<ArgumentException>(() => VideoSource.FromProjectFile(root, "../clip.mp4"));
		}

		[Test]
		public void Probe_UsesInjectedMetadataForMovAndDoesNotGuessCodec() {
			var expected = VideoProbeResult.SupportedVideo(VideoContainer.Mov, VideoCodec.HapM, hasAlpha: true);
			var metadata = new FakeMetadataProbe(expected);
			var probe = new ExtensionVideoCapabilityProbe(metadata).Probe("C:\\project\\clip.mov");
			Assert.That(probe.IsSuccess, Is.True);
			Assert.That(probe.Value.Codec, Is.EqualTo(VideoCodec.HapM));
			Assert.That(new ExtensionVideoCapabilityProbe().Probe("C:\\project\\clip.mov").IsFailure, Is.True);
		}

		[Test]
		public void VideoFixtures_ManifestVerifiesHashesMetadataAndNegativeHashArtifact() {
			var root = FixtureRoot();
			var manifestPath = Path.Combine(root, "manifest.json");
			Assert.That(File.Exists(manifestPath), Is.True, "The checked-in video fixture manifest is required; a missing fixture must fail, not skip.");
			var manifest = JsonUtility.FromJson<VideoFixtureManifest>(File.ReadAllText(manifestPath));
			Assert.That(manifest, Is.Not.Null);
			Assert.That(manifest.fixtures, Is.Not.Null.And.Not.Empty);
			foreach (var entry in manifest.fixtures) {
				var path = Path.Combine(root, entry.file);
				Assert.That(File.Exists(path), Is.True, entry.file);
				var bytes = File.ReadAllBytes(path);
				Assert.That(bytes.LongLength, Is.EqualTo(entry.bytes), entry.file);
				var actual = Hex(XxHash128.Hash(bytes));
				Assert.That(actual, Is.EqualTo(entry.xxh3_128), entry.file);
				Assert.That(entry.width, Is.GreaterThan(0), entry.file);
				Assert.That(entry.height, Is.GreaterThan(0), entry.file);
				Assert.That(entry.fps, Is.GreaterThan(0), entry.file);
				// Unity JsonUtility materializes a JSON null string as an
				// empty string. Both represent the intentional absence of a
				// decoded first-frame oracle for malformed/Hap metadata-only
				// fixtures; real pixel oracles remain exactly eight hex
				// characters.
				Assert.That(string.IsNullOrEmpty(entry.expectedFirstFrameRgba8) || entry.expectedFirstFrameRgba8.Length == 8, Is.True, entry.file);
				Assert.That(entry.expectedFrame, Is.EqualTo(entry.expectedFirstFrameRgba8), entry.file);
			}
			Assert.That(manifest.fixtures.Any(x => x.codec == "H264" && x.file == "h264.mp4" && !x.hasAudio), Is.True);
			Assert.That(manifest.fixtures.Any(x => x.codec == "VP8" && x.file == "vp8-alpha.webm" && x.hasAlpha), Is.True);
			Assert.That(manifest.fixtures.Any(x => x.codec == "VP8" && x.alphaEvidence == "Matroska BlockAdditional id 1"), Is.True, "VP8 alpha must be a real Matroska BlockAdditional payload, not only an extension/metadata claim.");
			Assert.That(manifest.fixtures.Any(x => x.codec == "H264" && x.file == "h264-audio.mp4" && x.hasAudio), Is.True);
			Assert.That(manifest.fixtures.Any(x => x.probe == "Unsupported" && x.codec == "VP9"), Is.True);
			Assert.That(manifest.fixtures.Any(x => x.probe == "Malformed" && x.file == "malformed-h264-truncated.mp4"), Is.True);

			var invalidManifest = JsonUtility.FromJson<InvalidHashFixtureManifest>(File.ReadAllText(Path.Combine(root, "manifest-invalid-hash.json")));
			var validBytes = File.ReadAllBytes(Path.Combine(root, invalidManifest.fixture));
			var validHash = Hex(XxHash128.Hash(validBytes));
			Assert.That(validHash, Is.Not.EqualTo(invalidManifest.xxh3_128), "The negative hash manifest must remain a rejected integrity case.");
		}

		[Test]
		public void VideoFixtures_FileProbeAcceptsH264AndVp8AlphaRejectsAudioUnsupportedAndMalformed() {
			var root = FixtureRoot();
			var probe = new FileVideoMetadataProbe();
			var h264 = probe.Probe(Path.Combine(root, "h264.mp4"));
			Assert.That(h264.IsSuccess && h264.Value.Supported, Is.True);
			Assert.That(h264.Value.Container, Is.EqualTo(VideoContainer.Mp4));
			Assert.That(h264.Value.Codec, Is.EqualTo(VideoCodec.H264));
			Assert.That(h264.Value.HasAudio, Is.False);

			var alpha = probe.Probe(Path.Combine(root, "vp8-alpha.webm"));
			Assert.That(alpha.IsSuccess && alpha.Value.Supported, Is.True);
			Assert.That(alpha.Value.Container, Is.EqualTo(VideoContainer.WebM));
			Assert.That(alpha.Value.Codec, Is.EqualTo(VideoCodec.VP8));
			Assert.That(alpha.Value.HasAlpha, Is.True);

			var audio = probe.Probe(Path.Combine(root, "h264-audio.mp4"));
			Assert.That(audio.IsSuccess && audio.Value.Supported, Is.True);
			Assert.That(audio.Value.HasAudio, Is.True, "Audio metadata is observed but UnityVideoBackend must still ignore audio output.");
			Assert.That(probe.Probe(Path.Combine(root, "unsupported-vp9.webm")).Value.Supported, Is.False);
			Assert.That(probe.Probe(Path.Combine(root, "malformed-h264-truncated.mp4")).Value.Supported, Is.False);
		}

		[Test]
		public void HapNativePluginProbe_LoadsInstalledBinaryAndChecksAbi() {
			var probe = new PInvokeHapNativeApi().ProbeInstalledBinary();
			var nativePlatform = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor
				|| UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer
				|| UnityEngine.Application.platform == RuntimePlatform.OSXEditor
				|| UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;
			Assert.That(probe.IsAvailable, Is.EqualTo(nativePlatform), probe.DiagnosticCode + ": " + probe.Message);
			if (nativePlatform) {
				Assert.That(probe.AbiVersion, Is.EqualTo(1u));
				Assert.That(probe.Capabilities & 0xFu, Is.EqualTo(0xFu));
			}
		}

		[Test]
		public void UnityBackend_RejectsCheckedInUnsupportedAndMalformedFixtures() {
			var metadata = new FileVideoMetadataProbe();
			var node = new NodeInstanceId("53535353-5353-4353-8353-535353535353");
			var backend = new UnityVideoBackend(node, 1);
			try {
				foreach (var file in new[] { "unsupported-vp9.webm", "malformed-h264-truncated.mp4" }) {
					var path = Path.Combine(FixtureRoot(), file);
					var result = metadata.Probe(path);
					Assert.That(result.IsSuccess && result.Value.Supported, Is.False, file + " must remain unsupported.");
					Assert.That(backend.Prepare(new VideoPrepareRequest(path, result.Value)).IsFailure, Is.True, file);
				}
			}
			finally { backend.Dispose(); }
		}

		[Test]
		public void BackendSelector_RecognizesAllGuaranteedHapVariants() {
			var graphics = new VideoGraphicsCapabilities(true, true, true);
			foreach (var codec in new[] { VideoCodec.Hap1, VideoCodec.Hap5, VideoCodec.HapY, VideoCodec.HapM }) {
				var probe = VideoProbeResult.SupportedVideo(VideoContainer.Mov, codec);
				var selected = VideoBackendSelector.Select(probe, graphics);
				Assert.That(selected.IsSuccess, Is.True, codec.ToString());
				Assert.That(selected.Value, Is.EqualTo(VideoBackendKind.HapVideoBackend));
			}
			var unsupported = VideoProbeResult.SupportedVideo(VideoContainer.Mov, VideoCodec.HapR);
			Assert.That(VideoBackendSelector.Select(unsupported, graphics).IsFailure, Is.True);
			Assert.That(VideoBackendSelector.Select(VideoProbeResult.SupportedVideo(VideoContainer.Mov, VideoCodec.Hap1), new VideoGraphicsCapabilities(false, false, false)).IsFailure, Is.True);
		}

		[UnityTest]
		public IEnumerator UnityBackend_UsesApiOnlyNoAudioAndDestroysOwnedHost() {
			var backend = new UnityVideoBackend(new NodeInstanceId("22222222-2222-4222-8222-222222222222"), 1);
			var host = backend.Host;
			Assert.That(backend.Player.renderMode, Is.EqualTo(UnityEngine.Video.VideoRenderMode.APIOnly));
			Assert.That(backend.Player.audioOutputMode, Is.EqualTo(UnityEngine.Video.VideoAudioOutputMode.None));
			Assert.That(backend.BorrowedTexture, Is.Null);
			backend.Dispose();
			Assert.That(backend.State, Is.EqualTo(VideoBackendState.Disposed));
			yield return null;
			Assert.That(host == null, Is.True, "The backend owns and destroys its private host GameObject.");
		}

		[Test]
		public void UnityBackend_ReflectsTransportSpeedLoopAndDemandTransfer() {
			var backend = new UnityVideoBackend(new NodeInstanceId("55555555-5555-4555-8555-555555555555"), 2);
			try {
				Assert.That(backend.SetSpeed(0d).IsSuccess, Is.True);
				Assert.That(backend.Player.playbackSpeed, Is.EqualTo(0f));
				Assert.That(backend.SetLoop(false).IsSuccess, Is.True);
				Assert.That(backend.Player.isLooping, Is.False);

				Assert.That(backend.SyncToGraphClock(3.25d, true).IsSuccess, Is.True);
				Assert.That(backend.Player.sendFrameReadyEvents, Is.True);
				Assert.That(backend.SyncToGraphClock(3.25d, false).IsSuccess, Is.True);
				Assert.That(backend.Player.sendFrameReadyEvents, Is.False);
				Assert.That(backend.Player.isPlaying, Is.False);
			}
			finally {
				backend.Dispose();
			}
		}

		[UnityTest]
		public IEnumerator UnityBackend_PreparesSeeksAndDecodesH264AudioFixture_WhileIgnoringAudio() {
			var path = Path.Combine(FixtureRoot(), "h264-audio.mp4");
			var probe = new FileVideoMetadataProbe().Probe(path);
			Assert.That(probe.IsSuccess && probe.Value.Supported && probe.Value.HasAudio, Is.True);
			var backend = new UnityVideoBackend(new NodeInstanceId("51515151-5151-4151-8151-515151515151"), 1);
			try {
				VideoBackendCompletion preparationError = null;
				backend.Completed += completion => {
					if (completion.Kind == VideoCompletionKind.Error) preparationError = completion;
				};
				Assert.That(backend.Prepare(new VideoPrepareRequest(path, probe.Value)).IsSuccess, Is.True);
				// Batch-mode test frames can advance far faster than Windows
				// Media Foundation's asynchronous decoder.  Wait on its
				// completion/error contract with a real-time deadline.
				var prepareDeadline = Time.realtimeSinceStartup + 10f;
				while (!backend.Player.isPrepared && preparationError == null && Time.realtimeSinceStartup < prepareDeadline) yield return null;
				Assert.That(preparationError, Is.Null, "Unity VideoPlayer reported a prepare error: " + preparationError?.ErrorMessage);
				Assert.That(backend.Player.isPrepared, Is.True, "The checked-in H.264 fixture must reach Unity VideoPlayer prepare completion within 10 real-time seconds.");
				Assert.That(backend.Seek(0.5d).IsSuccess, Is.True);
				Assert.That(backend.Play().IsSuccess, Is.True);
				var frameDeadline = Time.realtimeSinceStartup + 10f;
				while (backend.Player.texture == null && preparationError == null && Time.realtimeSinceStartup < frameDeadline) yield return null;
				Assert.That(preparationError, Is.Null, "Unity VideoPlayer reported an error while decoding H.264: " + preparationError?.ErrorMessage);
				Assert.That(backend.Player.texture, Is.Not.Null, "A prepared H.264 fixture must produce a decoded texture within 10 real-time seconds.");
				Assert.That(backend.Player.audioOutputMode, Is.EqualTo(UnityEngine.Video.VideoAudioOutputMode.None), "The audio track is intentionally ignored by the graph backend.");
			}
			finally { backend.Dispose(); }
		}

		[UnityTest]
		public IEnumerator UnityBackend_PreparesVp8AlphaFixtureWithExplicitAlphaMetadata() {
			var path = Path.Combine(FixtureRoot(), "vp8-alpha.webm");
			var probe = new FileVideoMetadataProbe().Probe(path);
			Assert.That(probe.IsSuccess && probe.Value.Supported && probe.Value.HasAlpha, Is.True);
			Assert.That(probe.Value.ConversionMetadata.AlphaMode, Is.EqualTo(VideoAlphaMode.Straight));
			var backend = new UnityVideoBackend(new NodeInstanceId("52525252-5252-4252-8252-525252525252"), 1);
			try {
				VideoBackendCompletion preparationError = null;
				backend.Completed += completion => {
					if (completion.Kind == VideoCompletionKind.Error) preparationError = completion;
				};
				Assert.That(backend.Prepare(new VideoPrepareRequest(path, probe.Value)).IsSuccess, Is.True);
				var prepareDeadline = Time.realtimeSinceStartup + 10f;
				while (!backend.Player.isPrepared && preparationError == null && Time.realtimeSinceStartup < prepareDeadline) yield return null;
				Assert.That(preparationError, Is.Null, "Unity VideoPlayer reported a VP8 prepare error: " + preparationError?.ErrorMessage);
				Assert.That(backend.Player.isPrepared, Is.True, "The checked-in VP8 alpha fixture must reach Unity VideoPlayer prepare completion within 10 real-time seconds.");
				Assert.That(backend.Player.audioOutputMode, Is.EqualTo(UnityEngine.Video.VideoAudioOutputMode.None));
			}
			finally { backend.Dispose(); }
		}

		[Test]
		public void HapNativeBackend_ClosesOpaqueHandleAndReportsUnsupportedPlatform() {
			var fake = new FakeHapApi();
			var backend = new HapVideoBackend(new NodeInstanceId("33333333-3333-4333-8333-333333333333"), 4, new HapNativeDecoder(fake));
			var request = new VideoPrepareRequest(VideoSource.FromProjectFile(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "clip.mov"), VideoProbeResult.SupportedVideo(VideoContainer.Mov, VideoCodec.HapM));
			Assert.That(backend.Prepare(request).IsSuccess, Is.True);
			Assert.That(backend.State, Is.EqualTo(VideoBackendState.Preparing), "Opening a native context is not a decode completion.");
			Assert.That(fake.Opened, Is.EqualTo(1));
			backend.Dispose();
			Assert.That(fake.Closed, Is.EqualTo(1));

			var unsupported = new HapVideoBackend(new NodeInstanceId("44444444-4444-4444-8444-444444444444"), 1, new HapNativeDecoder(new UnsupportedHapNativeApi()));
			var result = unsupported.Prepare(request);
			Assert.That(result.IsFailure, Is.True);
			Assert.That(result.Error.Code.Value, Is.EqualTo("media.hap.platform_unsupported"));
			unsupported.Dispose();
		}

		[Test]
		public void HapColorConversion_StraightAlphaBecomesLinearPremultiplied() {
			var straight = new byte[] { 255, 0, 255, 128 };
			var converted = HapColorConversion.ToLinearPremultipliedRgba8(straight);
			Assert.That(converted[3], Is.EqualTo(128));
			Assert.That(converted[0], Is.LessThanOrEqualTo(converted[3]));
			Assert.That(converted[2], Is.LessThanOrEqualTo(converted[3]));
			Assert.That(converted[1], Is.EqualTo(0));
			Assert.That(straight[0], Is.EqualTo(255), "the native straight-alpha copy is not mutated");
		}

		[Test]
		public void CompletionGate_DropsRetiredGenerationCallback() {
			var node = new NodeInstanceId("66666666-6666-4666-8666-666666666666");
			var gate = new VideoCompletionGate();
			gate.Register(node, 2);
			var applied = 0;
			Assert.That(gate.TryApply(new VideoBackendCompletion(node, 1, VideoCompletionKind.FrameReady), _ => applied++), Is.False);
			Assert.That(gate.TryApply(new VideoBackendCompletion(node, 2, VideoCompletionKind.FrameReady), _ => applied++), Is.True);
			Assert.That(applied, Is.EqualTo(1));
			gate.Unregister(node, 2);
			Assert.That(gate.TryApply(new VideoBackendCompletion(node, 2, VideoCompletionKind.Error), _ => applied++), Is.False);
		}

		[Test]
		public void TransportController_LatchesEofUntilExplicitFalseThenTrue() {
			var node = new NodeInstanceId("88888888-8888-4888-8888-888888888888");
			var backend = new RecordingBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(10d);
			transport.SetLoop(false);
			session.Prepare(new VideoPrepareRequest(VideoSource.FromFile(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fixture.mp4")), VideoProbeResult.SupportedVideo(VideoContainer.Mp4, VideoCodec.H264, durationSeconds: 10d)));
			var controller = new VideoTransportController(session, transport);
			var playing = Snapshot(node, 0d, true, 0d);

			Assert.That(controller.Apply(playing, node, true).IsSuccess, Is.True);
			backend.Emit(VideoCompletionKind.Ended, 10d);
			backend.ClearCalls();
			Assert.That(controller.Apply(Snapshot(node, 1d, true, 0d), node, true).IsSuccess, Is.True);
			Assert.That(controller.Apply(Snapshot(node, 2d, true, 0d), node, true).IsSuccess, Is.True);
			Assert.That(controller.Apply(Snapshot(node, 3d, true, 0d), node, true).IsSuccess, Is.True);
			Assert.That(backend.Calls.Count(x => x == "play"), Is.EqualTo(0), "A repeated persisted Playing=true must not restart after EOF.");
			Assert.That(controller.IsEofLatched, Is.True);

			Assert.That(controller.Apply(Snapshot(node, 2d, false, 0d), node, true).IsSuccess, Is.True);
			Assert.That(controller.IsEofLatched, Is.False);
			backend.ClearCalls();
			Assert.That(controller.Apply(Snapshot(node, 3d, true, 0d), node, true).IsSuccess, Is.True);
			Assert.That(backend.Calls.Count(x => x == "play"), Is.EqualTo(1), "Only an explicit false->true transition may restart playback.");
			Assert.That(backend.Calls.IndexOf("seek") < backend.Calls.IndexOf("play"), Is.True);
			session.Dispose();
		}

		[Test]
		public void TransportController_DemandFalsePausesAndResumeSeeksBeforePlay() {
			var node = new NodeInstanceId("99999999-9999-4999-8999-999999999999");
			var backend = new RecordingBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(10d);
			session.Prepare(new VideoPrepareRequest(VideoSource.FromFile(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fixture.mp4")), VideoProbeResult.SupportedVideo(VideoContainer.Mp4, VideoCodec.H264, durationSeconds: 10d)));
			var controller = new VideoTransportController(session, transport);
			Assert.That(controller.Apply(Snapshot(node, 0d, true, 0d), node, true).IsSuccess, Is.True);
			backend.ClearCalls();

			Assert.That(controller.Apply(Snapshot(node, 1d, true, 0d), node, false).IsSuccess, Is.True);
			Assert.That(backend.Calls, Does.Contain("sync:false"));
			Assert.That(backend.Calls, Does.Contain("pause"));
			backend.ClearCalls();
			Assert.That(controller.Apply(Snapshot(node, 4d, true, 0d), node, true).IsSuccess, Is.True);
			Assert.That(backend.Calls.IndexOf("seek") < backend.Calls.IndexOf("play"), Is.True);
			Assert.That(backend.Calls.Last(), Is.EqualTo("sync:true"));
			session.Dispose();
		}

		[Test]
		public void TransportController_DemandSeamAndEvaluateShareOneGraphClockSync() {
			var node = new NodeInstanceId("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
			var backend = new RecordingBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(10d);
			session.Prepare(new VideoPrepareRequest(VideoSource.FromFile(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fixture.mp4")), VideoProbeResult.SupportedVideo(VideoContainer.Mp4, VideoCodec.H264, durationSeconds: 10d)));
			var controller = new VideoTransportController(session, transport);
			var first = Snapshot(node, 0d, true, 0d);
			Assert.That(controller.Apply(first, node, true).IsSuccess, Is.True);
			backend.ClearCalls();

			var undemanded = Snapshot(node, 1d, true, 0d);
			controller.OnDemandChanged(false, Context(undemanded));
			Assert.That(controller.Apply(undemanded, node, false).IsSuccess, Is.True);
			Assert.That(backend.Calls.Count(x => x == "sync:false"), Is.EqualTo(1));
			backend.ClearCalls();

			var resumed = Snapshot(node, 4d, true, 0d);
			controller.OnDemandChanged(true, Context(resumed));
			Assert.That(controller.Apply(resumed, node, true).IsSuccess, Is.True);
			Assert.That(backend.Calls.Count(x => x == "sync:true"), Is.EqualTo(1));
			Assert.That(backend.Calls.IndexOf("seek") < backend.Calls.IndexOf("play"), Is.True);
			session.Dispose();
		}

		[Test]
		public void TransportController_SwitchesBackendOnLiveAssetChangeAndDropsOldCompletion() {
			var node = new NodeInstanceId("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
			var created = new List<RecordingBackend>();
			var factory = new RecordingFactory(created);
			var initial = factory.Create(node, 1, VideoBackendKind.UnityVideoBackend).Value;
			var session = new VideoPlaybackSession(node, 1, initial);
			var transport = new VideoTransportState();
			transport.SetMediaAsset(Asset);
			transport.SetDuration(10d);
			var controller = new VideoTransportController(session, transport, new SwitchingResolver(), factory, new VideoGraphicsCapabilities(true, true, true));

			Assert.That(controller.Apply(Snapshot(node, 0d, true, 0d, Asset), node, true).IsSuccess, Is.True);
			var unity = created[0];
			Assert.That(controller.Apply(Snapshot(node, 1d, true, 0d, HapAsset), node, true).IsSuccess, Is.True);
			Assert.That(session.Backend.BackendKind, Is.EqualTo(VideoBackendKind.HapVideoBackend));
			Assert.That(unity.State, Is.EqualTo(VideoBackendState.Disposed));
			unity.Emit(VideoCompletionKind.Error);
			Assert.That(session.Status, Is.Not.EqualTo(VideoPlaybackStatus.Faulted), "A stale completion from the retired backend must not fault the new backend.");

			Assert.That(controller.Apply(Snapshot(node, 2d, true, 0d, Asset), node, true).IsSuccess, Is.True);
			Assert.That(session.Backend.BackendKind, Is.EqualTo(VideoBackendKind.UnityVideoBackend));
			session.Dispose();
		}

		[Test]
		public void TransportController_FreshDemandAtPersistedStartPreparesAndPlaysWithoutRedundantSeek() {
			var node = new NodeInstanceId("cdcdcdcd-cdcd-4dcd-8dcd-cdcdcdcdcdcd");
			var backend = new RecordingBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var controller = new VideoTransportController(session, new VideoTransportState(), new TestPrepareResolver());
			try {
				var snapshot = Snapshot(node, 0d, true, 0d, Asset);

				Assert.That(controller.OnDemandChanged(true, Context(snapshot)).IsSuccess, Is.True);
				Assert.That(controller.Apply(snapshot, node, true).IsSuccess, Is.True);

				Assert.That(backend.Calls, Does.Contain("prepare"));
				Assert.That(backend.Calls, Does.Contain("play"));
				Assert.That(backend.Calls, Does.Not.Contain("seek"), "Prepare already positions a freshly opened asset at persisted playhead zero; a redundant seek may wait for an unavailable completion callback.");
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Playing));
			}
			finally { session.Dispose(); }
		}

		[Test]
		public void TransportController_FreshDemandAtPersistedNonzeroPositionSeeksAfterPrepare() {
			var node = new NodeInstanceId("dededede-dede-4ede-8ede-dededededede");
			var backend = new RecordingBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var controller = new VideoTransportController(session, new VideoTransportState(), new TestPrepareResolver());
			try {
				var snapshot = Snapshot(node, 0d, true, 2d, Asset);

				Assert.That(controller.OnDemandChanged(true, Context(snapshot)).IsSuccess, Is.True);
				Assert.That(controller.Apply(snapshot, node, true).IsSuccess, Is.True);

				Assert.That(backend.Calls, Does.Contain("seek"), "A nonzero persisted playhead remains a real transport seek after Prepare.");
				Assert.That(backend.Calls.IndexOf("seek"), Is.LessThan(backend.Calls.IndexOf("play")));
			}
			finally { session.Dispose(); }
		}

		[TestCase(2d, 0d, true)]
		[TestCase(0d, 0d, false)]
		[TestCase(2d, 2d, true)]
		public void TransportController_InitializedAssetChangeDuringPrepareRestoresTransport(double initialPlayhead, double replacementPlayhead, bool expectsSeek) {
			var node = new NodeInstanceId("efefefef-efef-4fef-8fef-efefefefefef");
			var backend = new DeferredPrepareBackend(node, 1);
			var session = new VideoPlaybackSession(node, 1, backend);
			var controller = new VideoTransportController(session, new VideoTransportState(), new TestPrepareResolver());
			try {
				var initial = Snapshot(node, 0d, true, initialPlayhead, Asset);
				Assert.That(controller.OnDemandChanged(true, Context(initial)).IsSuccess, Is.True);
				Assert.That(controller.Apply(initial, node, true).IsSuccess, Is.True);
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Preparing));

				backend.CompletePrepare();
				Assert.That(controller.Apply(initial, node, true).IsSuccess, Is.True);
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Playing));

				backend.ClearCalls();
				var replacement = Snapshot(node, 1d, true, replacementPlayhead, AlternateAsset, frameNumber: 2UL);
				Assert.That(controller.Apply(replacement, node, true).IsSuccess, Is.True);
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Preparing));
				Assert.That(backend.Calls, Does.Not.Contain("seek"), "Any replacement seek is deferred until this Prepare completes.");

				backend.CompletePrepare();
				Assert.That(controller.Apply(replacement, node, true).IsSuccess, Is.True);
				Assert.That(backend.Calls.Count(call => call == "seek"), Is.EqualTo(expectsSeek ? 1 : 0), "Only an initialized replacement with a changed playhead requires a seek after Prepare.");
				Assert.That(backend.Calls.Count(call => call == "play"), Is.EqualTo(1), "The requested playing transport resumes after the replacement asset Prepare.");
				if (expectsSeek) Assert.That(backend.Calls.IndexOf("seek"), Is.LessThan(backend.Calls.IndexOf("play")));
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Playing));
			}
			finally { session.Dispose(); }
		}

		[Test]
		public void RuntimeNode_PublishesSynchronousPrepareAsPreparingBeforeTheNextReadyEvaluation() {
			var nodeId = new NodeInstanceId("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
			var backend = new RecordingBackend(nodeId, 1);
			var session = new VideoPlaybackSession(nodeId, 1, backend);
			var node = new VideoPlayerRuntimeNode(nodeId, 1, session, new VideoTransportState(), new TestVideoFrameAdapter(), 16, 9,
				new TestPrepareResolver());
			try {
				var projection = VideoProjection(nodeId);
				var frame = 1UL;
				var output = default(NodeOutputResult);
				// Drain the initial Prepare publication before testing an
				// isolated asset change whose backend completes synchronously.
				for (; frame <= 8; frame++) {
					output = EvaluateVideoNode(node, VideoContext(Snapshot(nodeId, 0d, true, 0d, Asset, frame, projection), projection, nodeId));
					if (output.Status == NodeOutputStatus.Available) break;
					Assert.That(output.Status, Is.EqualTo(NodeOutputStatus.Preparing));
				}
				Assert.That(output.Status, Is.EqualTo(NodeOutputStatus.Available));
				Assert.That(node.State, Is.EqualTo(RuntimeNodeState.Ready));

				backend.ClearCalls();
				var prepareFrame = frame + 1;
				var synchronousPrepare = EvaluateVideoNode(node, VideoContext(Snapshot(nodeId, 0d, true, 0d, AlternateAsset, prepareFrame, projection), projection, nodeId));
				Assert.That(backend.Calls.Count(call => call == "prepare"), Is.EqualTo(1), "The alternate asset must issue its own Prepare.");
				// The preceding frame is deliberately held as Available;
				// FrameCoordinator must nevertheless publish the node's
				// Preparing state to the public graph for this evaluation.
				Assert.That(synchronousPrepare.Status, Is.EqualTo(NodeOutputStatus.Available));
				Assert.That(node.State, Is.EqualTo(RuntimeNodeState.Preparing));
				Assert.That(session.Status, Is.EqualTo(VideoPlaybackStatus.Playing), "The fake backend completed Prepare synchronously, then the requested playing transport resumed in the same evaluation.");

				var nextFrame = EvaluateVideoNode(node, VideoContext(Snapshot(nodeId, 0d, true, 0d, AlternateAsset, prepareFrame + 1, projection), projection, nodeId));
				Assert.That(nextFrame.Status, Is.EqualTo(NodeOutputStatus.Available));
				Assert.That(node.State, Is.EqualTo(RuntimeNodeState.Ready));
			}
			finally { node.Dispose(); }
		}

		private static FrameSnapshot Snapshot(NodeInstanceId node, double clock, bool playing, double playhead, MediaAssetId? asset = null,
			ulong frameNumber = 1UL, object projection = null) {
			var values = new Dictionary<ParameterKey, ParameterValue> {
				[new ParameterKey(node, new ParameterId(VideoPlayerContract.MediaAssetParameterId))] = ParameterValue.FromMediaAsset(asset ?? Asset),
				[new ParameterKey(node, new ParameterId(VideoPlayerContract.PlayingParameterId))] = ParameterValue.FromBool(playing),
				[new ParameterKey(node, new ParameterId(VideoPlayerContract.PlayheadParameterId))] = ParameterValue.FromFloat((float)playhead),
				[new ParameterKey(node, new ParameterId(VideoPlayerContract.SpeedParameterId))] = ParameterValue.FromFloat(1f),
				[new ParameterKey(node, new ParameterId(VideoPlayerContract.LoopParameterId))] = ParameterValue.FromBool(false)
			};
			var constructor = typeof(FrameSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.GetParameters().Length == 9 && candidate.GetParameters()[6].ParameterType == typeof(IDictionary<ParameterKey, ParameterValue>));
			return (FrameSnapshot)constructor.Invoke(new object[] { frameNumber, clock, false, 0L, 0L, projection, values, new Dictionary<LogicalControlId, float>(), Array.Empty<OutputDemand>() });
		}

		private static object VideoProjection(NodeInstanceId nodeId) {
			var image = new PortId(VideoPlayerContract.ImagePortId);
			var demand = new RuntimeOutputResolutionDemand(16, 9, 16d / 9d);
			var entry = new RuntimeOutputResolutionEntry(nodeId, image, demand);
			var type = typeof(FrameSnapshot).Assembly.GetType("ShitDesigner.Runtime.RuntimeOutputResolutionProjection", true);
			var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
			return constructor.Invoke(new object[] { new[] { entry }, new[] { entry } });
		}

		private static NodeExecutionContext VideoContext(FrameSnapshot snapshot, object projection, NodeInstanceId nodeId) {
			var constructor = typeof(NodeExecutionContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.GetParameters().Length == 9 && candidate.GetParameters()[1].ParameterType.Name == "RuntimeOutputResolutionProjection");
			return (NodeExecutionContext)constructor.Invoke(new object[]
			{
				snapshot, projection, Array.Empty<OutputDemand>(), nodeId, 0, new[] { new PortId(VideoPlayerContract.ImagePortId) },
				new Dictionary<PortId, ResolvedInput>(), new TestDiagnosticSink(), new TestOutputSurfacePort(nodeId, new PortId(VideoPlayerContract.ImagePortId))
			});
		}

		private static NodeOutputResult EvaluateVideoNode(VideoPlayerRuntimeNode node, NodeExecutionContext context) {
			var image = new PortId(VideoPlayerContract.ImagePortId);
			var writer = new NodeOutputWriter(new[] { image });
			node.Evaluate(context, writer);
			return writer.Outputs[image];
		}

		private static FrameEvaluationContext Context(FrameSnapshot snapshot) {
			var constructor = typeof(FrameEvaluationContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.GetParameters().Length == 3 && candidate.GetParameters()[1].ParameterType.Name == "RuntimeOutputResolutionProjection");
			return (FrameEvaluationContext)constructor.Invoke(new object[] { snapshot, null, Array.Empty<OutputDemand>() });
		}

		private sealed class FakeMetadataProbe : IVideoMetadataProbe {
			private readonly VideoProbeResult _result;
			public FakeMetadataProbe(VideoProbeResult result) { _result = result; }
			public CSharpFunctionalExtensions.Result<VideoProbeResult, Diagnostic> Probe(string absolutePath) => CSharpFunctionalExtensions.Result.Success<VideoProbeResult, Diagnostic>(_result);
		}

		private static string FixtureRoot() {
			var relative = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "ShitDesigner", "Scripts", "Tests", "Media", "Fixtures");
			if (Directory.Exists(relative)) return relative;
			return Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? Directory.GetCurrentDirectory(), "Assets", "ShitDesigner", "Scripts", "Tests", "Media", "Fixtures");
		}

		private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

#pragma warning disable 0649
		[Serializable]
		private sealed class VideoFixtureManifest {
			public VideoFixtureEntry[] fixtures;
		}

		[Serializable]
		private sealed class VideoFixtureEntry {
			public string file;
			public string codec;
			public int width;
			public int height;
			public float fps;
			public int bytes;
			public bool hasAlpha;
			public string alphaEvidence;
			public bool hasAudio;
			public string expectedFirstFrameRgba8;
			public string expectedFrame;
			public string xxh3_128;
			public string probe;
		}

		[Serializable]
		private sealed class InvalidHashFixtureManifest {
			public string fixture;
			public string xxh3_128;
		}
#pragma warning restore 0649

		private sealed class SwitchingResolver : IVideoPrepareResolver {
			public CSharpFunctionalExtensions.Result<VideoPrepareRequest, Diagnostic> Resolve(MediaAssetId mediaAssetId) {
				var codec = mediaAssetId == HapAsset ? VideoCodec.HapM : VideoCodec.H264;
				var container = mediaAssetId == HapAsset ? VideoContainer.Mov : VideoContainer.Mp4;
				return CSharpFunctionalExtensions.Result.Success<VideoPrepareRequest, Diagnostic>(new VideoPrepareRequest(VideoSource.FromFile(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), mediaAssetId == HapAsset ? "hap.mov" : "clip.mp4")), VideoProbeResult.SupportedVideo(container, codec, durationSeconds: 10d)));
			}
		}

		private sealed class RecordingFactory : IVideoBackendFactory {
			private readonly List<RecordingBackend> _created;
			public RecordingFactory(List<RecordingBackend> created) { _created = created; }
			public CSharpFunctionalExtensions.Result<IVideoBackendHandle, Diagnostic> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind) {
				var backend = new RecordingBackend(nodeId, generationId, kind);
				_created.Add(backend);
				return CSharpFunctionalExtensions.Result.Success<IVideoBackendHandle, Diagnostic>(backend);
			}
		}

		private sealed class FakeHapApi : IHapNativeApi {
			public bool IsSupportedPlatform => true;
			public int Opened { get; private set; }
			public int Closed { get; private set; }
			public CSharpFunctionalExtensions.Result<IntPtr, Diagnostic> Open(VideoPrepareRequest request) { Opened++; return CSharpFunctionalExtensions.Result.Success<IntPtr, Diagnostic>(new IntPtr(42)); }
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Play(IntPtr handle) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Pause(IntPtr handle) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Stop(IntPtr handle) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> SetSpeed(IntPtr handle, double speed) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> SetLoop(IntPtr handle, bool loop) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> Seek(IntPtr handle, double seconds) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public CSharpFunctionalExtensions.UnitResult<Diagnostic> SyncToGraphClock(IntPtr handle, double logicalSeconds, bool demanded) => CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			public object GetBorrowedTexture(IntPtr handle) => null;
			public void Close(IntPtr handle) { Closed++; }
		}

		private sealed class RecordingBackend : VideoBackendHandleBase {
			public List<string> Calls { get; } = new List<string>();
			public RecordingBackend(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind = VideoBackendKind.UnityVideoBackend) : base(nodeId, generationId, kind) { }
			public override object BorrowedTexture => new object();
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Prepare(VideoPrepareRequest request) { Calls.Add("prepare"); State = VideoBackendState.Ready; Emit(VideoCompletionKind.Prepared); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Play() { Calls.Add("play"); State = VideoBackendState.Playing; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Pause() { Calls.Add("pause"); State = VideoBackendState.Paused; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Stop() { Calls.Add("stop"); State = VideoBackendState.Ready; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SetSpeed(double speed) { Calls.Add("speed"); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SetLoop(bool loop) { Calls.Add("loop"); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Seek(double seconds) { Calls.Add("seek"); State = VideoBackendState.Ready; Emit(VideoCompletionKind.SeekStarted, seconds); Emit(VideoCompletionKind.SeekCompleted, seconds); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SyncToGraphClock(double logicalSeconds, bool demanded) {
				Calls.Add("sync:" + demanded.ToString().ToLowerInvariant());
				if (!demanded && State == VideoBackendState.Playing) { Calls.Add("pause"); State = VideoBackendState.Paused; }
				return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			}
			public void Emit(VideoCompletionKind kind, double time = 0d) => base.Emit(kind, time);
			public void ClearCalls() => Calls.Clear();
			protected override void DisposeCore() { }
		}

		private sealed class DeferredPrepareBackend : VideoBackendHandleBase {
			public List<string> Calls { get; } = new List<string>();
			public DeferredPrepareBackend(NodeInstanceId nodeId, ulong generationId) : base(nodeId, generationId, VideoBackendKind.UnityVideoBackend) { }
			public override object BorrowedTexture => new object();
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Prepare(VideoPrepareRequest request) { Calls.Add("prepare"); State = VideoBackendState.Preparing; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Play() { Calls.Add("play"); State = VideoBackendState.Playing; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Pause() { Calls.Add("pause"); State = VideoBackendState.Paused; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Stop() { Calls.Add("stop"); State = VideoBackendState.Ready; return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SetSpeed(double speed) { Calls.Add("speed"); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SetLoop(bool loop) { Calls.Add("loop"); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> Seek(double seconds) { Calls.Add("seek"); State = VideoBackendState.Ready; Emit(VideoCompletionKind.SeekStarted, seconds); Emit(VideoCompletionKind.SeekCompleted, seconds); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public override CSharpFunctionalExtensions.UnitResult<Diagnostic> SyncToGraphClock(double logicalSeconds, bool demanded) { Calls.Add("sync:" + demanded.ToString().ToLowerInvariant()); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
			public void CompletePrepare() { State = VideoBackendState.Ready; Emit(VideoCompletionKind.Prepared); }
			public void ClearCalls() => Calls.Clear();
			protected override void DisposeCore() { }
		}

		private sealed class TestPrepareResolver : IVideoPrepareResolver {
			public CSharpFunctionalExtensions.Result<VideoPrepareRequest, Diagnostic> Resolve(MediaAssetId mediaAssetId) => CSharpFunctionalExtensions.Result.Success<VideoPrepareRequest, Diagnostic>(
				new VideoPrepareRequest(VideoSource.FromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fixture.mp4")),
					VideoProbeResult.SupportedVideo(VideoContainer.Mp4, VideoCodec.H264, durationSeconds: 10d)));
		}

		private sealed class TestVideoFrameAdapter : IVideoFrameAdapter {
			public CSharpFunctionalExtensions.Result<IRuntimeImageFrame, Diagnostic> Create(object borrowedTexture, int width, int height, ulong frameNumber, ulong leaseId)
				=> CSharpFunctionalExtensions.Result.Success<IRuntimeImageFrame, Diagnostic>(new TestVideoFrame(width, height, frameNumber, leaseId));
		}

		private sealed class TestVideoFrame : IRuntimeImageFrame {
			public int Width { get; }
			public int Height { get; }
			public string ColorFormat => "test";
			public ulong FrameNumber { get; }
			public ulong LeaseId { get; }
			public TestVideoFrame(int width, int height, ulong frameNumber, ulong leaseId) { Width = width; Height = height; FrameNumber = frameNumber; LeaseId = leaseId; }
		}

		private sealed class TestDiagnosticSink : IRuntimeDiagnosticSink {
			public void Report(Diagnostic diagnostic) { }
		}

		private sealed class TestOutputSurfacePort : IRuntimeOutputSurfacePort {
			private readonly IRuntimeOutputSurface _surface;
			public TestOutputSurfacePort(NodeInstanceId nodeId, PortId portId) { _surface = new TestOutputSurface(nodeId, portId); }
			public CSharpFunctionalExtensions.Result<IRuntimeOutputSurface, Diagnostic> TryGetPrepared(NodeInstanceId nodeId, PortId portId, int width, int height, ulong frameNumber)
				=> CSharpFunctionalExtensions.Result.Success<IRuntimeOutputSurface, Diagnostic>(_surface);
		}

		private sealed class TestOutputSurface : IRuntimeOutputSurface {
			public NodeInstanceId NodeId { get; }
			public PortId PortId { get; }
			public int Width => 16;
			public int Height => 9;
			public ulong LeaseId => 1;
			public ulong FrameNumber => 1;
			public object NativeSurface { get; } = new object();
			public TestOutputSurface(NodeInstanceId nodeId, PortId portId) { NodeId = nodeId; PortId = portId; }
		}
	}
}
