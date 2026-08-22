using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;

namespace ShitDesigner.Bootstrap.Tests
{
    [TestFixture]
    public sealed class UserSettingsPersistenceTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ShitDesigner-UserSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Test]
        public void Read_ReusesSnapshotUntilASuccessfulSettingsChange()
        {
            var port = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(new LocalProjectFileSystem(), _root));
            var first = port.Read();
            Assert.That(port.Read(), Is.SameAs(first));
            Assert.That(port.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.25f)).IsSuccess, Is.True);
            var changed = port.Read();
            Assert.That(changed, Is.Not.SameAs(first));
            Assert.That(changed.UiScale, Is.EqualTo(1.25f));
            Assert.That(port.Read(), Is.SameAs(changed));
        }

        [Test]
        public void SettingsAndLayoutsAreSeparateAtomicDocumentsAndRoundTrip()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            var port = new ProjectUserSettingsPort(storage);

            Assert.That(File.Exists(storage.SettingsPath), Is.True);
            Assert.That(File.Exists(storage.LayoutsPath), Is.True);
            Assert.That(File.ReadAllText(storage.SettingsPath), Does.Contain("formatVersion"));
            Assert.That(File.ReadAllText(storage.LayoutsPath), Does.Contain("currentLayoutId"));

            Assert.That(port.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.5f)).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("reduce-motion", reduceMotion: true)).IsSuccess, Is.True);
            var settings = storage.ReadSettings();
            settings.Theme = "Light";
            settings.TooltipDelaySeconds = .25f;
            settings.MediaLibraryView = "List";
            settings.DiagnosticsExportFolder = Path.Combine(_root, "Diagnostics");
            storage.SaveSettings(settings);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("rename", "Edit", "Editing")).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("select", "Live")).IsSuccess, Is.True);

            var settingsJson = File.ReadAllText(storage.SettingsPath);
            var layoutsJson = File.ReadAllText(storage.LayoutsPath);
            Assert.That(settingsJson, Does.Contain("uiScale"));
            Assert.That(settingsJson, Does.Not.Contain("presets"));
            Assert.That(layoutsJson, Does.Contain("presets"));
            Assert.That(layoutsJson, Does.Not.Contain("uiScale"));
            Assert.That(File.Exists(storage.SettingsPath + ".tmp"), Is.False);
            Assert.That(File.Exists(storage.LayoutsPath + ".tmp"), Is.False);

            var restored = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(fileSystem, _root));
            Assert.That(restored.Read().UiScale, Is.EqualTo(1.5f));
            Assert.That(restored.Read().ReduceMotion, Is.True);
            var restoredSettings = new ProjectUserSettingsStorage(fileSystem, _root).ReadSettings();
            Assert.That(restoredSettings.Theme, Is.EqualTo("Light"));
            Assert.That(restoredSettings.TooltipDelaySeconds, Is.EqualTo(.25f));
            Assert.That(restoredSettings.MediaLibraryView, Is.EqualTo("List"));
            Assert.That(restoredSettings.DiagnosticsExportFolder, Is.EqualTo(Path.Combine(_root, "Diagnostics")));
            Assert.That(restored.Read().ActivePresetId, Is.EqualTo("Live"));
            Assert.That(restored.Read().Presets.Single(x => x.Id == "Edit").Name, Is.EqualTo("Editing"));
        }

        [Test]
        public void CorruptDocumentsRecoverIndependentlyWithoutDestroyingTheOtherDocument()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            Directory.CreateDirectory(_root);
            File.WriteAllText(storage.SettingsPath, "{\"formatVersion\":1,", new System.Text.UTF8Encoding(false));
            File.WriteAllText(storage.LayoutsPath, "{\"formatVersion\":1,\"currentLayoutId\":\"Keep\",\"presets\":[{\"id\":\"Keep\",\"name\":\"Keep\",\"tree\":{\"kind\":\"tabs\",\"panels\":[\"node-graph-panel\"],\"activePanel\":\"node-graph-panel\"}}]}", new System.Text.UTF8Encoding(false));

            var port = new ProjectUserSettingsPort(storage);
            Assert.That(port.Read().UiScale, Is.EqualTo(1f));
            Assert.That(port.Read().Presets.Single().Id, Is.EqualTo("Keep"));
            Assert.That(File.ReadAllText(storage.SettingsPath), Does.Contain("recentProjectRoots"));
            Assert.That(File.ReadAllText(storage.LayoutsPath), Does.Contain("Keep"));
        }

        [Test]
        public void LayoutEditIsDraftOnlyAndExplicitOverwriteIsTheOnlyTreeSave()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            var port = new ProjectUserSettingsPort(storage);
            var draft = new DockTree(new DockTabGroup(new[] { "node-graph-panel" }, "node-graph-panel"));
            var persistedBeforeEdit = File.ReadAllText(storage.LayoutsPath);

            var edit = port.Apply(new WorkspaceSettingsCommand("edit", "Edit", tree: draft, isDirty: true));
            Assert.That(edit.IsSuccess, Is.True);
            Assert.That(edit.Snapshot.IsDirty, Is.True);
            Assert.That(DockTreeCodec.Encode(edit.Snapshot.CurrentTree), Is.EqualTo(DockTreeCodec.Encode(draft)));
            Assert.That(File.ReadAllText(storage.LayoutsPath), Is.EqualTo(persistedBeforeEdit));

            var restartedBeforeSave = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(fileSystem, _root));
            Assert.That(DockTreeCodec.Encode(restartedBeforeSave.Read().CurrentTree), Is.EqualTo(DockTreeCodec.Encode(LayoutPresetStore.EditDefaultTree())));

            var overwrite = port.Apply(new WorkspaceSettingsCommand("overwrite", "Edit"));
            Assert.That(overwrite.IsSuccess, Is.True);
            Assert.That(overwrite.Snapshot.IsDirty, Is.False);
            var restartedAfterSave = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(fileSystem, _root));
            Assert.That(DockTreeCodec.Encode(restartedAfterSave.Read().CurrentTree), Is.EqualTo(DockTreeCodec.Encode(draft)));
        }

        [Test]
        public void FailedAtomicLayoutSavePreservesPersistedLayoutAndInMemoryDraft()
        {
            var fileSystem = new FailingAtomicFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            var port = new ProjectUserSettingsPort(storage);
            var draft = new DockTree(new DockTabGroup(new[] { "node-graph-panel" }, "node-graph-panel"));
            Assert.That(port.Apply(new WorkspaceSettingsCommand("edit", "Edit", tree: draft, isDirty: true)).IsSuccess, Is.True);
            var persistedBeforeFailure = File.ReadAllText(storage.LayoutsPath);
            fileSystem.FailPromotion = true;

            var result = port.Apply(new WorkspaceSettingsCommand("overwrite", "Edit"));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Snapshot.IsDirty, Is.True);
            Assert.That(DockTreeCodec.Encode(result.Snapshot.CurrentTree), Is.EqualTo(DockTreeCodec.Encode(draft)));
            Assert.That(File.ReadAllText(storage.LayoutsPath), Is.EqualTo(persistedBeforeFailure));
        }

        [Test]
        public void UnknownPanelRawPayloadSurvivesLoadEditSelectRenameAndSave()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            var raw = "{\"futureSetting\": 7, \"opaque\": [1, 2, 3]}";
            File.WriteAllText(storage.LayoutsPath,
                "{\"formatVersion\":1,\"currentLayoutId\":\"Edit\",\"presets\":[{\"id\":\"Edit\",\"name\":\"Edit\",\"tree\":{\"kind\":\"tabs\",\"panels\":[{\"panelTypeId\":\"future.panel\",\"panelInstanceId\":\"future-instance\",\"rawPayload\":\"{\\\"futureSetting\\\": 7, \\\"opaque\\\": [1, 2, 3]}\",\"originalLocation\":\"root.first\"}],\"activePanel\":\"future-instance\"}}]}",
                new System.Text.UTF8Encoding(false));

            var port = new ProjectUserSettingsPort(storage);
            var loaded = (DockTabGroup)port.Read().CurrentTree.Root;
            Assert.That(loaded.UnknownPanels.Count, Is.EqualTo(1));
            Assert.That(loaded.UnknownPanels[0].RawPayload, Is.EqualTo(raw));
            Assert.That(DockTreeCodec.TryDecode(DockTreeCodec.Encode(port.Read().CurrentTree), out var boundaryTree), Is.True);
            var boundaryUnknown = ((DockTabGroup)boundaryTree.Root).UnknownPanels.Single();
            Assert.That(boundaryUnknown.RawPayload, Is.EqualTo(raw));
            Assert.That(port.Apply(new WorkspaceSettingsCommand("rename", "Edit", "Future Preserved")).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("select", "Edit")).IsSuccess, Is.True);

            var restored = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(fileSystem, _root));
            var restoredGroup = (DockTabGroup)restored.Read().CurrentTree.Root;
            Assert.That(restoredGroup.UnknownPanels[0].PanelTypeId, Is.EqualTo("future.panel"));
            Assert.That(restoredGroup.UnknownPanels[0].PanelInstanceId, Is.EqualTo("future-instance"));
            Assert.That(restoredGroup.UnknownPanels[0].RawPayload, Is.EqualTo(raw));
            Assert.That(restoredGroup.UnknownPanels[0].OriginalLocation, Is.EqualTo("root.first"));
            Assert.That(restored.Read().Presets.Single(x => x.Id == "Edit").Name, Is.EqualTo("Future Preserved"));
        }

        [Test]
        public void RecentProjectsAreMoveToFrontPersistedAndCappedAtTenAcrossApplicationRestart()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            using (var application = new ProjectApplication(fileSystem, recentProjectStore: storage))
            {
                for (var i = 0; i < 11; i++)
                {
                    var projectRoot = Path.Combine(_root, "Project" + i);
                    Assert.That(application.NewProject("Project" + i, projectRoot).IsSuccess, Is.True);
                    Assert.That(application.OpenProject(projectRoot, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
                }

                Assert.That(application.ReadModel.RecentProjectRoots.Count, Is.EqualTo(10));
                Assert.That(application.ReadModel.RecentProjectRoots[0], Is.EqualTo(Path.GetFullPath(Path.Combine(_root, "Project10"))));
                Assert.That(application.OpenProject(Path.Combine(_root, "Project3"), UnsavedChangesDecision.Discard).IsSuccess, Is.True);
                Assert.That(application.ReadModel.RecentProjectRoots[0], Is.EqualTo(Path.GetFullPath(Path.Combine(_root, "Project3"))));
                Assert.That(application.ReadModel.RecentProjectRoots.Count, Is.EqualTo(10));
            }

            using (var restarted = new ProjectApplication(fileSystem, recentProjectStore: new ProjectUserSettingsStorage(fileSystem, _root)))
            {
                Assert.That(restarted.ReadModel.RecentProjectRoots.Count, Is.EqualTo(10));
                Assert.That(restarted.ReadModel.RecentProjectRoots[0], Is.EqualTo(Path.GetFullPath(Path.Combine(_root, "Project3"))));
                Assert.That(restarted.ReadModel.RecentProjectRoots.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(10));
            }
        }

        [Test]
        public void SettingsCommandsExposeAndPersistAllUserPreferences()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            var port = new ProjectUserSettingsPort(storage);

            Assert.That(port.Apply(new WorkspaceSettingsCommand("theme", theme: "Dark")).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("tooltip-delay", tooltipDelaySeconds: .25f)).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("media-view", mediaLibraryView: "List")).IsSuccess, Is.True);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("diagnostics-folder", diagnosticsExportFolder: Path.Combine(_root, "Diagnostics"))).IsSuccess, Is.True);

            var snapshot = port.Read();
            Assert.That(snapshot.Theme, Is.EqualTo("Dark"));
            Assert.That(snapshot.TooltipDelaySeconds, Is.EqualTo(.25f));
            Assert.That(snapshot.MediaLibraryView, Is.EqualTo("List"));
            Assert.That(snapshot.DiagnosticsExportFolder, Is.EqualTo(Path.Combine(_root, "Diagnostics")));

            var restarted = new ProjectUserSettingsPort(new ProjectUserSettingsStorage(fileSystem, _root));
            Assert.That(restarted.Read().TooltipDelaySeconds, Is.EqualTo(.25f));
            Assert.That(restarted.Read().MediaLibraryView, Is.EqualTo("List"));
            Assert.That(restarted.Read().DiagnosticsExportFolder, Is.EqualTo(Path.Combine(_root, "Diagnostics")));
            Assert.That(port.Apply(new WorkspaceSettingsCommand("tooltip-delay", tooltipDelaySeconds: .3f)).IsSuccess, Is.False);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("media-view", mediaLibraryView: "Tiles")).IsSuccess, Is.False);
        }

        [Test]
        public void MissingRecentProjectCanBeRemovedFromUserSettings()
        {
            var fileSystem = new LocalProjectFileSystem();
            var storage = new ProjectUserSettingsStorage(fileSystem, _root);
            storage.WriteRecentProjectRoots(new[] { Path.Combine(_root, "Missing"), Path.Combine(_root, "Keep") });
            var port = new ProjectUserSettingsPort(storage);
            Assert.That(port.Apply(new WorkspaceSettingsCommand("recent-remove", recentProjectRoot: Path.Combine(_root, "Missing"))).IsSuccess, Is.True);
            Assert.That(storage.ReadRecentProjectRoots().Any(x => x.EndsWith("Missing", System.StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(storage.ReadRecentProjectRoots().Any(x => x.EndsWith("Keep", System.StringComparison.OrdinalIgnoreCase)), Is.True);
        }

        private sealed class FailingAtomicFileSystem : IProjectFileSystem, IProjectDurableFileSystem, IAtomicManifestWriter
        {
            private readonly LocalProjectFileSystem _inner = new LocalProjectFileSystem();
            public bool FailPromotion { get; set; }
            public bool Exists(string path) => _inner.Exists(path);
            public string GetFullPath(string path) => _inner.GetFullPath(path);
            public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
            public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
            public void WriteAllBytes(string path, byte[] bytes) => _inner.WriteAllBytes(path, bytes);
            public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);
            public void AtomicReplace(string temporaryPath, string mainPath, string backupPath, bool backupMain) => _inner.AtomicReplace(temporaryPath, mainPath, backupPath, backupMain);
            public System.Collections.Generic.IEnumerable<string> EnumerateFiles(string directory) => _inner.EnumerateFiles(directory);
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => _inner.CopyFile(sourcePath, destinationPath, overwrite);
            public void Delete(string path) => _inner.Delete(path);
            public void Flush(string path) => _inner.Flush(path);
            public void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain)
            {
                if (FailPromotion) throw new IOException("Injected atomic promotion failure.");
                _inner.Replace(fileSystem, temporaryPath, mainPath, backupPath, backupMain);
            }
        }
    }
}
