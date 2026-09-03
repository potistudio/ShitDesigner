using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Persistence;

namespace ShitDesigner.TestHarness.Tests
{
	[Category("docs/ARCHITECTURE/Testing.md/Standalone Acceptance")]
	public sealed class StandaloneAcceptanceContractTests
	{
		[Test]
		public void RequiredGraph_ContainsAllProgramPathNodesAndTwoPreviews()
		{
			var nodes = new[]
			{
				Node("3d", "shitdesigner.scene.3d"), Node("2d", "shitdesigner.scene.2d"),
				Node("effect", "shitdesigner.shader.effect"), Node("video", "shitdesigner.video.player"),
				Node("blend-a", "shitdesigner.shader.blend2"), Node("blend-b", "shitdesigner.shader.blend2"),
				Node("feedback", "system.feedback"), Node("program", GraphConstants.ProgramOutputTypeId),
				Node("preview-a", GraphConstants.PreviewTypeId), Node("preview-b", GraphConstants.PreviewTypeId)
			};
			var edges = new[]
			{
				Edge("3d", "blend-a", "a"), Edge("2d", "blend-a", "b"), Edge("blend-a", "blend-b", "a"),
				Edge("video", "blend-b", "b"), Edge("blend-b", "effect", "input"), Edge("effect", "feedback", "input"),
				Edge("feedback", "program", "image"), Edge("video", "preview-a", "image"), Edge("video", "preview-b", "image")
			};
			Assert.That(AcceptanceContract.ValidateRequiredGraph(new ApplicationGraphReadModel(nodes, connections: edges)), Is.Empty);
		}

		[Test]
		public void RequiredGraph_RejectsVideoThatOnlyFeedsPreviews()
		{
			var nodes = new[]
			{
				Node("3d", "shitdesigner.scene.3d"), Node("2d", "shitdesigner.scene.2d"),
				Node("effect", "shitdesigner.shader.effect"), Node("video", "shitdesigner.video.player"),
				Node("blend-a", "shitdesigner.shader.blend2"), Node("blend-b", "shitdesigner.shader.blend2"),
				Node("feedback", "system.feedback"), Node("program", GraphConstants.ProgramOutputTypeId),
				Node("preview-a", GraphConstants.PreviewTypeId), Node("preview-b", GraphConstants.PreviewTypeId)
			};
			var edges = new[]
			{
				Edge("3d", "blend-a", "a"), Edge("2d", "blend-a", "b"), Edge("blend-a", "blend-b", "a"),
				Edge("blend-b", "effect", "input"), Edge("effect", "feedback", "input"), Edge("feedback", "program", "image"),
				Edge("video", "preview-a", "image"), Edge("video", "preview-b", "image")
			};
			Assert.That(AcceptanceContract.ValidateRequiredGraph(new ApplicationGraphReadModel(nodes, connections: edges)), Does.Contain("video.player"));
		}

		[Test]
		public void ColorControl_SelectsOnlyThePublicWritableShaderGeneratorColorParameter()
		{
			var sceneColor = PublicColorParameter("scene", "shitdesigner.scene.3d");
			var shaderColor = PublicColorParameter("shader", "shitdesigner.shader.generator");
			var parameters = new[] { sceneColor, shaderColor };

			Assert.That(AcceptanceContract.FindWritableShaderGeneratorColorParameter(parameters, "scene"), Is.Null);
			Assert.That(AcceptanceContract.FindWritableShaderGeneratorColorParameter(parameters, "shader"), Is.SameAs(shaderColor));
		}

		[Test]
		public void PortableMedia_RequiresProjectRelativeCopiedFiles()
		{
			var root = Path.Combine(Path.GetTempPath(), "ShitDesignerAcceptanceContract-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(root, "media"));
			var file = Path.Combine(root, "media", "clip.mp4");
			File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
			try
			{
				var valid = new[] { new ApplicationMediaReadModel("asset", "media/clip.mp4", 3, "hash") };
				Assert.That(AcceptanceContract.ValidatePortableMediaPaths(valid, root, Path.Combine(root, "fixtures")), Is.Empty);
				Assert.That(AcceptanceContract.ValidatePortableMediaPaths(new[] { new ApplicationMediaReadModel("asset", file, 3, "hash") }, root), Does.Contain("relative"));
				Assert.That(AcceptanceContract.ValidatePortableMediaPaths(new[] { new ApplicationMediaReadModel("asset", "../clip.mp4", 3, "hash") }, root), Does.Contain("escapes"));
				Assert.That(AcceptanceContract.ValidatePortableMediaPaths(new[] { new ApplicationMediaReadModel("asset", "media/missing.mp4", 3, "hash") }, root), Does.Contain("missing"));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void AcceptanceFixtures_SelectTheAudioH264EntryWhenItFollowsSilentH264()
		{
			var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
			var fixtureRoot = Path.Combine(projectRoot, "Assets", "ShitDesigner", "Scripts", "Tests", "Media", "Fixtures");
			var result = AcceptanceFixtureValidator.Validate(fixtureRoot);

			Assert.That(result.IsValid, Is.True, result.Error);
			var h264 = result.Entries.Single(x => string.Equals(x.codec, "H264", StringComparison.OrdinalIgnoreCase));
			Assert.That(h264.file, Is.EqualTo("h264-audio.mp4"));
			Assert.That(h264.hasAudio, Is.True);
		}

		[Test]
		public void ArtifactV2_ReopenRequiresPersistedAcceptanceState()
		{
			var artifact = ReopenArtifact().acceptance;
			artifact.logicalControlStateObserved = false;
			Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Reopen, artifact), Does.Contain("logical control"));
			artifact.logicalControlStateObserved = true;
			Assert.That(AcceptanceContract.ValidateStage(HarnessAcceptanceStage.Reopen, artifact), Is.Empty);
		}

		[Test]
		public void FailedSaveTask_IsPublishedAndRetainsPublicFailureDiagnostics()
		{
			var priorTaskId = Guid.NewGuid();
			var exception = new DiagnosticExceptionInfo("System.IO.IOException", "disk full", "stack");
			var diagnostic = new Diagnostic(new DiagnosticCode("persistence.json_invalid"), Severity.Error, "Readback rejected the temporary manifest.", module: "persistence", exception: exception);
			var failed = new ApplicationTaskReadModel(Guid.NewGuid(), "Save", "Failed", "Failed", "C:/acceptance/project", diagnostic);

			Assert.That(AcceptanceContract.SaveTaskPublished(failed, priorTaskId), Is.True, "A terminal failed Save is still a newly published Save task.");
			Assert.That(AcceptanceContract.SaveTaskFailed(failed), Is.True);
			var description = AcceptanceContract.DescribeSaveTaskFailure(failed);
			Assert.That(description, Does.Contain("stage=Failed"));
			Assert.That(description, Does.Contain("path=C:/acceptance/project"));
			Assert.That(description, Does.Contain("diagnosticCode=persistence.json_invalid"));
			Assert.That(description, Does.Contain("exceptionType=System.IO.IOException"));
		}

		[Test]
		public void Fingerprint_PersistsPreviewDescriptorButExcludesRuntimeQualityAndDemand()
		{
			var saved = new[] { Preview("preview1", "Fit", "Black", "Project", false) };
			var runtimeChanged = new[] { Preview("preview1", "Fit", "Black", "Low", true, true) };
			var fitChanged = new[] { Preview("preview1", "Fill", "Black", "Project", false) };
			var backgroundChanged = new[] { Preview("preview1", "Fit", "Checker", "Project", false) };
			var idChanged = new[] { Preview("preview2", "Fit", "Black", "Project", false) };

			Assert.That(AcceptanceFingerprint.ComputePersistedPreviewComponent(runtimeChanged),
				Is.EqualTo(AcceptanceFingerprint.ComputePersistedPreviewComponent(saved)),
				"Quality and demand are runtime output negotiation, not Canonical Project content.");
			Assert.That(AcceptanceFingerprint.ComputePersistedPreviewComponent(fitChanged),
				Is.Not.EqualTo(AcceptanceFingerprint.ComputePersistedPreviewComponent(saved)),
				"The persisted Preview tab fit descriptor must survive reopen equality.");
			Assert.That(AcceptanceFingerprint.ComputePersistedPreviewComponent(backgroundChanged),
				Is.Not.EqualTo(AcceptanceFingerprint.ComputePersistedPreviewComponent(saved)),
				"The persisted Preview tab background descriptor must survive reopen equality.");
			Assert.That(AcceptanceFingerprint.ComputePersistedPreviewComponent(idChanged),
				Is.Not.EqualTo(AcceptanceFingerprint.ComputePersistedPreviewComponent(saved)),
				"The persisted Preview tab id must survive reopen equality.");
		}

		[Test]
		public void Fingerprint_PersistsPreviewTabOrder()
		{
			var firstThenSecond = new[] { Preview("preview1", "Fit", "Black", "Project", false), Preview("preview2", "Fit", "Black", "Project", false) };
			var secondThenFirst = new[] { Preview("preview2", "Fit", "Black", "Project", false), Preview("preview1", "Fit", "Black", "Project", false) };

			Assert.That(AcceptanceFingerprint.ComputePersistedPreviewComponent(secondThenFirst),
				Is.Not.EqualTo(AcceptanceFingerprint.ComputePersistedPreviewComponent(firstThenSecond)),
				"Preview tab assignment and order are persisted Project UI State.");
		}

		[Test]
		public void Fingerprint_ComputeExcludesWorkspaceSessionState()
		{
			var root = Path.Combine(Path.GetTempPath(), "ShitDesignerAcceptanceFingerprint-" + Guid.NewGuid().ToString("N"));
			try
			{
				using (var application = new ProjectApplication(new LocalProjectFileSystem()))
				{
					Assert.That(application.NewProject("Fingerprint", root).IsSuccess, Is.True);
					var initial = application.ReadModel;
					var initialFingerprint = AcceptanceFingerprint.Compute(initial);
					Assert.That(initial.Workspace?.Model?.LayoutId, Is.EqualTo("default"));

					Assert.That(application.SetWorkspaceLayout("alternate-layout", true).IsSuccess, Is.True);
					var changedWorkspace = application.ReadModel;
					Assert.That(changedWorkspace.Workspace?.Model?.LayoutId, Is.EqualTo("alternate-layout"));
					Assert.That(changedWorkspace.Workspace?.Model?.IsDirty, Is.True);
					Assert.That(AcceptanceFingerprint.Compute(changedWorkspace), Is.EqualTo(initialFingerprint),
						"Workspace layout/dirty state is per-user session state and cannot alter Canonical Project equality.");
				}
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}

		[Test]
		public void CanonicalProjectFingerprint_MatchesSavedBytesAndFreshOpenWithoutSideEffects()
		{
			var root = Path.Combine(Path.GetTempPath(), "ShitDesignerCanonicalFingerprint-" + Guid.NewGuid().ToString("N"));
			try
			{
				string fingerprint;
				using (var application = new ProjectApplication(new LocalProjectFileSystem()))
				{
					Assert.That(application.NewProject("Canonical Fingerprint", root).IsSuccess, Is.True);
					Assert.That(application.SetProgramDisplay(3).IsSuccess, Is.True);
					var dirtyBytesBefore = File.ReadAllBytes(Path.Combine(root, PersistenceConstants.MainFileName));
					var dirtyReadBefore = application.ReadModel;
					var dirtyTaskBefore = dirtyReadBefore.Task?.Model;
					var dirtyCaptured = application.CaptureCanonicalProjectFingerprint();
					Assert.That(dirtyCaptured.IsSuccess, Is.True, dirtyCaptured.Diagnostic?.Message);
					Assert.That(application.ReadModel, Is.SameAs(dirtyReadBefore), "Fingerprint capture must not publish a read-model/task update while the Project is dirty.");
					Assert.That(application.ReadModel.Project.Model.IsDirty, Is.True, "Fingerprint capture must not begin a Save or change the dirty/saving token state.");
					Assert.That(application.ReadModel.Task?.Model?.TaskId, Is.EqualTo(dirtyTaskBefore?.TaskId));
					Assert.That(File.ReadAllBytes(Path.Combine(root, PersistenceConstants.MainFileName)), Is.EqualTo(dirtyBytesBefore), "Fingerprint capture must not write a file while the Project is dirty.");
					Assert.That(application.SaveProject().IsSuccess, Is.True);
					var mainPath = Path.Combine(root, PersistenceConstants.MainFileName);
					var bytesBefore = File.ReadAllBytes(mainPath);
					var readBefore = application.ReadModel;
					var taskBefore = readBefore.Task?.Model;

					var captured = application.CaptureCanonicalProjectFingerprint();

					Assert.That(captured.IsSuccess, Is.True, captured.Diagnostic?.Message);
					fingerprint = captured.Value;
					Assert.That(fingerprint, Is.EqualTo(AssetIntegrity.Hash(bytesBefore)), "The query must hash ProjectSerializer's exact canonical bytes.");
					Assert.That(File.ReadAllBytes(mainPath), Is.EqualTo(bytesBefore), "Fingerprint capture must not write a file.");
					Assert.That(application.ReadModel, Is.SameAs(readBefore), "Fingerprint capture must not publish a read-model/task update.");
					Assert.That(application.ReadModel.Project.Model.IsDirty, Is.False, "Fingerprint capture must not begin a Save or dirty the document.");
					Assert.That(application.ReadModel.Task?.Model?.TaskId, Is.EqualTo(taskBefore?.TaskId));
					Assert.That(application.ReadModel.Task?.Model?.Status, Is.EqualTo(taskBefore?.Status));
				}

				using (var reopened = new ProjectApplication(new LocalProjectFileSystem()))
				{
					Assert.That(reopened.OpenProject(root).IsSuccess, Is.True);
					var captured = reopened.CaptureCanonicalProjectFingerprint();
					Assert.That(captured.IsSuccess, Is.True, captured.Diagnostic?.Message);
					Assert.That(captured.Value, Is.EqualTo(fingerprint), "A freshly opened saved Project must retain its canonical persistence identity.");
				}
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}

		[Test]
		public void CanonicalProjectFingerprint_RejectsMissingProject()
		{
			using (var application = new ProjectApplication(new LocalProjectFileSystem()))
			{
				var captured = application.CaptureCanonicalProjectFingerprint();
				Assert.That(captured.IsFailure, Is.True);
				Assert.That(captured.Diagnostic.Code.Value, Is.EqualTo("application.fingerprint.project_missing"));
			}
		}

		[Test]
		public void Fingerprint_ComponentDiagnosticIdentifiesOnlyChangedPersistedComponent()
		{
			var expected = new AcceptanceFingerprint.Components("project", "graph", "parameters", "controls", "presets", "dashboard", "previews", "media");
			var actual = new AcceptanceFingerprint.Components("project", "other-graph", "parameters", "controls", "presets", "dashboard", "previews", "media");

			var diagnostic = AcceptanceFingerprint.DescribeDifference(expected.Describe(), actual);

			Assert.That(diagnostic, Does.Contain("changed=graph"));
			Assert.That(diagnostic, Does.Not.Contain("changed=workspace"));
			Assert.That(diagnostic, Does.Not.Contain("changed=previews,graph"));
		}

		private static HarnessArtifact ReopenArtifact()
		{
			return new HarnessArtifact
			{
				mode = "acceptance",
				stage = "Reopen",
				acceptance = new HarnessAcceptanceArtifact
				{
					mode = "acceptance",
					stage = "Reopen",
					acceptanceContractVersion = AcceptanceContract.CurrentArtifactContractVersion,
					productionCompositionUsed = true,
					productionCatalogUsed = true,
					editorAssemblyExcluded = true,
					presentationRootAvailable = true,
					programAndPreviewsReady = true,
					requiredGraphObserved = true,
					realFrameObserved = true,
					mediaPortable = true,
					valueControlId = "value",
					presetTriggerId = "trigger",
					presetId = "preset",
					fileProjectReadable = true,
					fileProjectWritable = true,
					persistence = new HarnessAcceptancePersistenceArtifact
					{
						reopened = true,
						fingerprint = "same",
						expectedFingerprint = "same"
					}
				}
			};
		}

		private static ApplicationGraphNodeReadModel Node(string id, string type)
			=> new ApplicationGraphNodeReadModel(id, type, type, 0, 0);

		private static ApplicationParameterReadModel PublicColorParameter(string nodeId, string nodeTypeId)
			=> new ApplicationParameterReadModel(nodeId + ":color", nodeId, "color", "Color", "color:0,0,0,1", "color:0,0,0,1",
				false, false, false, false, null, ParameterType.Color.ToString(), null, null, null, null, "Shader", 0, null, null, 0d,
				null, null, null, null, nodeTypeId, true);

		private static ApplicationGraphConnectionReadModel Edge(string source, string destination, string destinationPort)
			=> new ApplicationGraphConnectionReadModel(Guid.NewGuid().ToString("D"), source, "image", destination, destinationPort);

		private static ApplicationOutputSurfaceReadModel Preview(string id, string fit, string background, string quality, bool demanded, bool holdingRuntime = false)
			=> new ApplicationOutputSurfaceReadModel(id, "Preview", "Available", 640, 360, fit, background, quality, demanded, holdingRuntime);
	}
}
