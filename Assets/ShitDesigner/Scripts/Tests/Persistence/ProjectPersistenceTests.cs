using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Persistence;
using ShitDesigner.Project;

namespace ShitDesigner.Persistence.Tests
{
    [TestFixture]
    public sealed class ProjectPersistenceTests
    {
        [Test]
        [Category("PERSIST_HASH")]
        public void Xxh3_128_EmptyPayload_MatchesOfficialVector()
        {
            Assert.That(AssetIntegrity.Hash(Array.Empty<byte>()), Is.EqualTo("99aa06d3014798d86001c324468d497f"));
        }

        [Test]
        [Category("PERSIST_HASH")]
        public void Xxh3_128_SpanAndStream_ReturnSameDigest()
        {
            var payload = Encoding.UTF8.GetBytes("portable project asset");
            using (var stream = new MemoryStream(payload))
                Assert.That(AssetIntegrity.Hash(stream), Is.EqualTo(AssetIntegrity.Hash(payload)));
        }

        [TestCase(1, "a6cd5e9392000f6ac44bdff4074eecdb")]
        [TestCase(6, "082afe0b8162d12a3e7039bdda43cfc6")]
        [TestCase(48, "a002ac4e5478227ef942219aed80f67b")]
        [TestCase(81, "4952f58181ab00425e8bafb9f95fb803")]
        [TestCase(222, "337e09641b948717f1aebd597cec6b3a")]
        [TestCase(403, "1b6de21e332dd73dcdeb804d65c6dea4")]
        [TestCase(512, "18d2d110dcc9bca1617e49599013cb6b")]
        [TestCase(2048, "f736557fd47073a5dd59e2c3a5f038e0")]
        [TestCase(2367, "e89c0f6ff369b427cb37aeb9e5d361ed")]
        [Category("PERSIST_HASH")]
        public void Xxh3_128_OfficialSanityVectors_AreCanonicalForSpanAndFragmentedStream(int length, string expected)
        {
            var fixture = CreateOfficialXxh3SanityFixture(length);
            var spanDigest = AssetIntegrity.Hash(fixture.AsSpan());
            using (var stream = new FragmentedReadStream(fixture, 7))
            {
                var streamDigest = AssetIntegrity.Hash(stream);
                Assert.That(streamDigest, Is.EqualTo(expected), "The streaming digest must match the official XXH3-128 vector independent of read fragmentation.");
                Assert.That(streamDigest, Is.EqualTo(spanDigest), "Span and Stream hashing must use the same canonical byte order.");
            }
            Assert.That(spanDigest, Is.EqualTo(expected), "Canonical XXH3-128 hexadecimal order must not depend on host endianness.");
            Assert.That(AssetIntegrity.IsDigest(spanDigest), Is.True);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void MediaPathRules_OnlyAcceptsAssetRelativeSourcePath()
        {
            var id = MediaAssetId.New();
            Assert.That(MediaPathRules.Normalize(id, "Assets/" + id.Value + "/source.png").IsSuccess, Is.True);
            Assert.That(MediaPathRules.Normalize(id, "../source.png").IsFailure, Is.True);
            Assert.That(MediaPathRules.Normalize(id, "C:/source.png").IsFailure, Is.True);
            Assert.That(MediaPathRules.Normalize(id, "Assets/" + id.Value + "/../source.png").IsFailure, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_UsesCanonicalTopLevelOrder_NoBomAndOneTrailingLf()
        {
            var document = new ProjectDocument("Canonical");
            document.BeginSave();
            var snapshot = document.TryCreateSaveSnapshot();
            Assert.That(snapshot.IsSuccess, Is.True, snapshot.Diagnostic?.Message);
            var json = ProjectSerializer.Serialize(snapshot.Value);

            Assert.That(json.IsSuccess, Is.True, json.Diagnostic?.Message);
            Assert.That(json.Value[0], Is.EqualTo('{'));
            Assert.That(json.Value.EndsWith("\n", StringComparison.Ordinal), Is.True);
            // NUnit's ContainsConstraint treats the BOM as an empty string on
            // some bundled runners; use the direct ordinal search instead.
            Assert.That(json.Value.IndexOf('\uFEFF'), Is.EqualTo(-1));
            var topLevel = new[] { "projectFormatVersion", "projectName", "settings", "graph", "logicalControls", "controlMappings", "presets", "mediaAssets", "ui" };
            var previous = -1;
            foreach (var property in topLevel)
            {
                var current = json.Value.IndexOf("\"" + property + "\"", StringComparison.Ordinal);
                Assert.That(current, Is.GreaterThan(previous), property + " must follow the canonical top-level order.");
                previous = current;
            }
            Assert.That(json.Value.IndexOf("\"expressions\"", StringComparison.Ordinal), Is.EqualTo(-1));
            Assert.That(ProjectSerializer.Deserialize(Encoding.UTF8.GetBytes(json.Value)).IsSuccess, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_PreservesPreviewTabAssignmentOrder()
        {
            var first = NodeInstanceId.New().Value;
            var second = NodeInstanceId.New().Value;
            var document = new ProjectDocument("Preview tab order", ui: new ProjectUiStateRecord(previewNodeIds: new[] { second, first }));
            document.BeginSave();
            var snapshot = document.TryCreateSaveSnapshot();
            Assert.That(snapshot.IsSuccess, Is.True, snapshot.Diagnostic?.Message);

            var json = ProjectSerializer.Serialize(snapshot.Value);
            Assert.That(json.IsSuccess, Is.True, json.Diagnostic?.Message);
            Assert.That(json.Value.IndexOf(second, StringComparison.Ordinal), Is.LessThan(json.Value.IndexOf(first, StringComparison.Ordinal)),
                "Preview tab assignment and order are Project UI State, not a dictionary-like canonical collection.");

            var readback = ProjectSerializer.Deserialize(json.Value);
            Assert.That(readback.IsSuccess, Is.True, readback.Diagnostic?.Message);
            Assert.That(readback.Value.Ui.PreviewNodeIds, Is.EqualTo(new[] { second, first }));
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void PreviewDisplayModeAndInstanceTitle_SurviveSaveLoadAndCanonicalResave()
        {
            var id = NodeInstanceId.New();
            var mode = new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
                enumOptionIds: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
            var preview = new NodeRecord(id, new NodeTypeId("system.preview"), 1, "Acceptance Preview 1", true, new ProjectPosition(4, 5),
                parameters: new[] { new ParameterRecord(mode, ParameterValue.FromEnum("fill")) },
                ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
                systemOwned: false, userAddable: true);
            var created = ProjectDocumentFactory.TryCreate("Preview persistence", 1, new[] { preview }, Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(), Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>());
            Assert.That(created.IsSuccess, Is.True, created.Diagnostic?.Message);
            var fileSystem = new FakeFileSystem();
            Assert.That(new ProjectSaver().Save(created.Value, "source", fileSystem).IsSuccess, Is.True);
            var sourceBytes = fileSystem.ReadAllBytes("source/project.json");

            var loaded = new ProjectLoader().Load("source", fileSystem);

            Assert.That(loaded.IsSuccess, Is.True, loaded.Diagnostic?.Message);
            var reloadedPreview = loaded.Value.Document.FindNode(id);
            Assert.That(reloadedPreview.DisplayName, Is.EqualTo("Acceptance Preview 1"));
            Assert.That(reloadedPreview.FindParameter(new ParameterId("display.mode")).BaseValue.AsString(), Is.EqualTo("fill"));
            Assert.That(new ProjectSaver().Save(loaded.Value.Document, "resaved", fileSystem).IsSuccess, Is.True);
            Assert.That(fileSystem.ReadAllBytes("resaved/project.json"), Is.EqualTo(sourceBytes));
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_PreservesFloatMaximumAcrossLoadAndCanonicalResave()
        {
            var range = new ParameterRange(ParameterValue.FromFloat(0f), ParameterValue.FromFloat(float.MaxValue));
            var definition = new ParameterDefinition(new ParameterId("transport.playhead_seconds"), "Playhead", ParameterType.Float, ParameterValue.FromFloat(0f), range, runtimeStateful: true);
            var node = new NodeRecord(NodeInstanceId.New(), new NodeTypeId("shitdesigner.video.player"), 1, "Video", true, new ProjectPosition(0, 0),
                parameters: new[] { new ParameterRecord(definition, definition.DefaultValue) }, rawState: "{}");
            var created = ProjectDocumentFactory.CreateNew("Float maximum");
            Assert.That(created.IsSuccess, Is.True, created.Diagnostic?.Message);
            var document = created.Value;
            Assert.That(new ProjectCommandProcessor(document).AddNode(node).IsSuccess, Is.True);
            var fileSystem = new FakeFileSystem();
            Assert.That(new ProjectSaver().Save(document, "source", fileSystem).IsSuccess, Is.True);
            var sourceBytes = fileSystem.ReadAllBytes("source/project.json");
            Assert.That(Encoding.UTF8.GetString(sourceBytes), Does.Contain("3.40282347E+38"));

            var loaded = new ProjectLoader().Load("source", fileSystem);

            Assert.That(loaded.IsSuccess, Is.True, loaded.Diagnostic?.Message);
            Assert.That(new ProjectSaver().Save(loaded.Value.Document, "resaved", fileSystem).IsSuccess, Is.True);
            Assert.That(fileSystem.ReadAllBytes("resaved/project.json"), Is.EqualTo(sourceBytes));
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RoundTripsUnboundMediaAssetReference_AndRejectsNonCanonicalAlternatives()
        {
            var media = new ParameterDefinition(new ParameterId("transport.media_asset"), "Media", ParameterType.MediaAssetReference, ParameterValue.FromMediaAsset(null));
            var node = new NodeRecord(NodeInstanceId.New(), new NodeTypeId("shitdesigner.video.player"), 1, "Video", true, new ProjectPosition(0, 0),
                new[] { new ParameterRecord(media, media.DefaultValue) }, rawState: "{}");
            var document = new ProjectDocument("Unbound media");
            Assert.That(new ProjectCommandProcessor(document).AddNode(node).IsSuccess, Is.True);
            var fileSystem = new FakeFileSystem();
            var saved = new ProjectSaver().Save(document, "root", fileSystem);
            Assert.That(saved.IsSuccess, Is.True, saved.Diagnostic?.Message);
            Assert.That(fileSystem.Exists("root/project.json.tmp"), Is.False, "A successfully read-back manifest must be atomically promoted from tmp.");
            var json = Encoding.UTF8.GetString(fileSystem.ReadAllBytes("root/project.json"));
            Assert.That(json, Does.Contain("\"type\":\"MediaAssetReference\",\"value\":null"));

            var readback = ProjectSerializer.Deserialize(Encoding.UTF8.GetBytes(json));
            Assert.That(readback.IsSuccess, Is.True, readback.Diagnostic?.Message);
            Assert.That(readback.Value.Nodes.Single().Parameters.Single().BaseValue.Type, Is.EqualTo(ParameterType.MediaAssetReference.ToString()));
            Assert.That(readback.Value.Nodes.Single().Parameters.Single().BaseValue.TextValue, Is.Null);
            Assert.That(ProjectSerializer.Deserialize(json.Replace(",\"value\":null", string.Empty)).IsFailure, Is.True, "MediaAssetReference must still require an explicit value property.");
            Assert.That(ProjectSerializer.Deserialize(json.Replace("\"value\":null", "\"value\":\"\"")).IsFailure, Is.True, "Unselected MediaAssetReference must use explicit null, not an empty string.");

            var selectedAsset = MediaAssetId.New();
            var selectedJson = SerializeSingleParameter(ParameterType.MediaAssetReference, ParameterValue.FromMediaAsset(selectedAsset));
            var selectedReadback = ProjectSerializer.Deserialize(selectedJson);
            Assert.That(selectedReadback.IsSuccess, Is.True, selectedReadback.Diagnostic?.Message);
            Assert.That(selectedReadback.Value.Nodes.Single().Parameters.Single().BaseValue.TextValue, Is.EqualTo(selectedAsset.Value));
            Assert.That(ProjectSerializer.Deserialize(selectedJson.Replace(selectedAsset.Value, "not-a-uuid")).IsFailure, Is.True, "Selected media values must remain UUID v4 IDs.");

            var nonMediaNullCases = new[]
            {
                (json: SerializeSingleParameter(ParameterType.String, ParameterValue.FromString("assigned")), serializedValue: "\"value\":\"assigned\"", type: "String"),
                (json: SerializeSingleParameter(ParameterType.Float, ParameterValue.FromFloat(1f)), serializedValue: "\"value\":1", type: "Float")
            };
            foreach (var item in nonMediaNullCases)
            {
                var nullValue = item.json.Replace(item.serializedValue, "\"value\":null");
                Assert.That(ProjectSerializer.Deserialize(nullValue).IsFailure, Is.True, item.type + " value:null must be rejected by the value parser.");
            }
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RejectsDuplicatePropertiesAndTrailingCommas()
        {
            var duplicate = "{\"projectFormatVersion\":1,\"projectFormatVersion\":1,\"projectName\":\"x\",\"graph\":{\"nodes\":[],\"connections\":[]}}";
            var trailing = "{\"projectFormatVersion\":1,\"projectName\":\"x\",\"graph\":{\"nodes\":[],\"connections\":[],},\"logicalControls\":[],\"presets\":[],\"mediaAssets\":[],\"ui\":{}}";
            Assert.That(ProjectSerializer.Deserialize(duplicate).IsFailure, Is.True);
            Assert.That(ProjectSerializer.Deserialize(trailing).IsFailure, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_AllowsUtf8BomOnRead()
        {
            Assert.That(ProjectSerializer.Deserialize("\uFEFF" + EmptyManifest("Bom")).IsSuccess, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RejectsUnknownTopLevelAndMissingRequiredProperties()
        {
            var unknown = EmptyManifest("Unknown").Replace("\"ui\":{}", "\"ui\":{},\"future\":1");
            var legacyExpressions = EmptyManifest("LegacyExpressions").Replace("\"ui\":{}", "\"ui\":{},\"expressions\":[]");
            var missing = "{\"projectFormatVersion\":1,\"projectName\":\"x\",\"graph\":{\"nodes\":[],\"connections\":[]}}";
            Assert.That(ProjectSerializer.Deserialize(unknown).IsFailure, Is.True);
            Assert.That(ProjectSerializer.Deserialize(legacyExpressions).IsFailure, Is.True);
            Assert.That(ProjectSerializer.Deserialize(missing).IsFailure, Is.True);
        }

        [Test]
        [Category("PERSIST_UNKNOWN")]
        public void UnknownNode_RoundTripRetainsOriginalTypeSchemaAndRawState()
        {
            var original = new NodeTypeId("vendor.future.node");
            var unknown = new UnknownNodeRecord(original, 7, "{\"z\":1, \"a\": [true]}");
            var node = new NodeRecord(NodeInstanceId.New(), new NodeTypeId("system.unknown_node"), 1, "Unknown", true, new ProjectPosition(1, 2), rawState: unknown.RawJsonValue, unknown: unknown);
            var document = new ProjectDocument("Unknown");
            Assert.That(new ProjectCommandProcessor(document).AddNode(node).IsSuccess, Is.True);
            document.BeginSave();
            var snapshot = document.TryCreateSaveSnapshot();
            var json = ProjectSerializer.Serialize(snapshot.Value);
            var dto = ProjectSerializer.Deserialize(json.Value);

            Assert.That(dto.IsSuccess, Is.True, dto.Diagnostic?.Message);
            Assert.That(json.Value, Does.Contain("\"nodeTypeId\":\"" + original.Value + "\""));
            Assert.That(json.Value, Does.Contain("\"schemaVersion\":7"));
            Assert.That(json.Value, Does.Contain("\"state\":{" + "\"z\":1, \"a\": [true]" + "}"));
            Assert.That(json.Value, Does.Not.Contain("\"unknown\""));
            Assert.That(dto.Value.Nodes[0].TypeId, Is.EqualTo(original.Value));
            Assert.That(dto.Value.Nodes[0].SchemaVersion, Is.EqualTo(7));
            Assert.That(dto.Value.Nodes[0].RawState, Is.EqualTo(unknown.RawJsonValue));

            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes(json.Value));
            var unknownLoad = new ProjectLoader().Load("root", fs, catalog: new TestNodeCatalog());
            Assert.That(unknownLoad.IsSuccess, Is.True, unknownLoad.Diagnostic?.Message);
            Assert.That(unknownLoad.Value.Document.Nodes[0].IsUnknown, Is.True);
            Assert.That(unknownLoad.Value.Document.Nodes[0].Unknown.OriginalNodeTypeId, Is.EqualTo(original));
            Assert.That(unknownLoad.Value.Document.Nodes[0].Unknown.OriginalSchemaVersion, Is.EqualTo(7));
            Assert.That(unknownLoad.Value.Document.Nodes[0].Unknown.RawJsonValue, Is.EqualTo(unknown.RawJsonValue));

            var restored = new ProjectLoader().Load("root", fs, catalog: new TestNodeCatalog(original, 7));
            Assert.That(restored.IsSuccess, Is.True, restored.Diagnostic?.Message);
            Assert.That(restored.Value.Document.Nodes[0].IsUnknown, Is.False);
            Assert.That(restored.Value.Document.Nodes[0].TypeId, Is.EqualTo(original));

            var migration = new NodeMigrationRegistry();
            Assert.That(migration.Register(new TestNodeMigrator(original, 7, "{\"migrated\":true}")).IsSuccess, Is.True);
            var migrated = new ProjectLoader().Load("root", fs, catalog: new TestNodeCatalog(original, 8), migrations: migration);
            Assert.That(migrated.IsSuccess, Is.True, migrated.Diagnostic?.Message);
            Assert.That(migrated.Value.Document.Nodes[0].IsUnknown, Is.False);
            Assert.That(migrated.Value.Document.Nodes[0].SchemaVersion, Is.EqualTo(8));
            Assert.That(migrated.Value.Document.Nodes[0].RawState, Is.EqualTo("{\"migrated\":true}"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_WriteFailure_PreservesMainAndSavedToken()
        {
            var fs = new FakeFileSystem { FailWrite = true };
            var document = new ProjectDocument("SaveFailure");
            var before = document.SavedToken;
            var result = new ProjectSaver().Save(document, "root", fs);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(fs.Exists("root/project.json"), Is.False);
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'tmp.write'"));
            Assert.That(result.Diagnostic.Message, Does.Contain("project.json.tmp"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_RequiresExplicitAtomicManifestWriterPort()
        {
            var fs = new NonAtomicFileSystem();
            var result = new ProjectSaver().Save(new ProjectDocument("NoAtomicPort"), "root", fs);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'manifest.adapter'"));
            Assert.That(result.Diagnostic.Message, Does.Contain("project.json"));
            Assert.That(fs.Exists("root/project.json"), Is.False);
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void WindowsManifestAdapter_UsesOneReplaceOperation()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes("old"));
            fs.WriteAllBytes("root/project.json.tmp", Encoding.UTF8.GetBytes("new"));

            new WindowsAtomicManifestWriter().Replace(fs, "root/project.json.tmp", "root/project.json", "root/project.json.bak", true);

            Assert.That(fs.Operations.Count, Is.EqualTo(1));
            Assert.That(fs.Operations[0], Is.EqualTo("replace:root/project.json.tmp->root/project.json"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void MacManifestAdapter_VerifiesBackupThenRenamesWithoutPreviousSidecar()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes("old"));
            fs.WriteAllBytes("root/project.json.bak", Encoding.UTF8.GetBytes("older"));
            fs.WriteAllBytes("root/project.json.tmp", Encoding.UTF8.GetBytes("new"));

            new MacOsAtomicManifestWriter().Replace(fs, "root/project.json.tmp", "root/project.json", "root/project.json.bak", true);

            Assert.That(fs.Operations.Count, Is.EqualTo(4));
            Assert.That(fs.Operations[0], Does.StartWith("copy:root/project.json->root/project.json.bak.macos-copy-"));
            Assert.That(fs.Operations[1], Does.StartWith("flush:root/project.json.bak.macos-copy-"));
            Assert.That(fs.Operations[2], Does.StartWith("replace:root/project.json.bak.macos-copy-"));
            Assert.That(fs.Operations[3], Is.EqualTo("replace:root/project.json.tmp->root/project.json"));
            Assert.That(fs.EnumerateFiles("root").Any(x => x.Contains("previous", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_EditingDuringSave_KeepsProjectDirtyAfterCompletion()
        {
            var fs = new FakeFileSystem();
            var document = new ProjectDocument("SaveEdit");
            var result = new ProjectSaver().Save(document, "root", fs, () => Assert.That(new ProjectCommandProcessor(document).AddLogicalControl(new LogicalControlRecord(LogicalControlId.New(), "Edited", LogicalControlKind.Value)).IsSuccess, Is.True));

            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            Assert.That(document.IsDirty, Is.True);
        }

        [Test]
        [Category("PERSIST_RECOVERY")]
        public void Loader_InvalidMainAndValidBackup_OpensRecoveredDirtyCandidate()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes("not json"));
            fs.WriteAllBytes("root/project.json.bak", Encoding.UTF8.GetBytes(EmptyManifest("Recovered")));
            var result = new ProjectLoader().Load("root", fs, new ProjectDocument("Current"));

            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            Assert.That(result.Value.IsRecovered, Is.True);
            Assert.That(result.Value.Document.IsDirty, Is.True);
        }

        [Test]
        [Category("PERSIST_RECOVERY")]
        public void Loader_InvalidMainAndBackup_DoesNotReplaceCurrentProject()
        {
            var current = new ProjectDocument("Current");
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes("bad"));
            fs.WriteAllBytes("root/project.json.bak", Encoding.UTF8.GetBytes("also bad"));

            var result = new ProjectLoader().Load("root", fs, current);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(current.ProjectName, Is.EqualTo("Current"));
            Assert.That(current.IsDirty, Is.False);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void Loader_RejectsResolvedMediaOutsideProjectRoot()
        {
            var id = MediaAssetId.New();
            var fs = new FakeFileSystem { EscapeMediaPath = true };
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes(MediaManifest("Outside", id, Array.Empty<byte>())));
            var result = new ProjectLoader().Load("root", fs, new ProjectDocument("Current"));
            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            Assert.That(result.Value.Diagnostics.Any(x => x.Code.Value == "persistence.media_outside_project"), Is.True);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void Loader_RejectsReparsePointInManagedMediaPath()
        {
            var id = MediaAssetId.New();
            var payload = Encoding.UTF8.GetBytes("asset");
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes(MediaManifest("Reparse", id, payload)));
            fs.WriteAllBytes("root/Assets/" + id.Value + "/source.png", payload);
            fs.MarkReparse("root/Assets/" + id.Value);
            var result = new ProjectLoader().Load("root", fs, new ProjectDocument("Current"));
            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            Assert.That(result.Value.Diagnostics.Any(x => x.Code.Value == "persistence.media_reparse_point"), Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RejectsUnknownPropertyOnNestedDto()
        {
            var unknownUi = EmptyManifest("Nested").Replace("\"ui\":{}", "\"ui\":{\"future\":1}");
            Assert.That(ProjectSerializer.Deserialize(unknownUi).IsFailure, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RejectsDuplicatedLogicalControlMappingsInNestedShape()
        {
            var manifest = EmptyManifest("NestedMapping").Replace("\"logicalControls\":[]", "\"logicalControls\":[{\"logicalControlId\":\"00000000-0000-4000-8000-000000000001\",\"name\":\"Value\",\"kind\":\"Value\",\"targets\":[],\"mappings\":[]}]");
            Assert.That(ProjectSerializer.Deserialize(manifest).IsFailure, Is.True);
        }

        [Test]
        [Category("PERSIST_DTO")]
        public void Serializer_RoundTripsParameterExpressionInsideGraphParameter()
        {
            var nodeId = NodeInstanceId.New();
            var parameterId = new ParameterId("gain");
            var definition = new ParameterDefinition(parameterId, "Gain", ParameterType.Float, ParameterValue.FromFloat(0));
            var node = new NodeRecord(nodeId, new NodeTypeId("vendor.expression.node"), 1, "Expression", true, new ProjectPosition(0, 0), new[] { new ParameterRecord(definition, ParameterValue.FromFloat(0)) }, rawState: "{\"opaque\":true}");
            var document = new ProjectDocument("Expression");
            Assert.That(new ProjectCommandProcessor(document).AddNode(node).IsSuccess, Is.True);
            var controlId = LogicalControlId.New();
            var target = new LogicalControlTargetRecord(nodeId, parameterId, ParameterType.Float, ParameterValue.FromFloat(-1), ParameterValue.FromFloat(1));
            Assert.That(new ProjectCommandProcessor(document).AddLogicalControl(new LogicalControlRecord(controlId, "Input", LogicalControlKind.Value, targets: new[] { target })).IsSuccess, Is.True);
            var expressionResult = new ProjectCommandProcessor(document).AddExpression(new ParameterExpressionRecord(nodeId, parameterId, new LogicalControlLeaf(controlId), new ParameterRange(ParameterValue.FromFloat(-1), ParameterValue.FromFloat(1))));
            Assert.That(expressionResult.IsSuccess, Is.True, expressionResult.Diagnostic?.Message);
            document.BeginSave();
            var snapshot = document.TryCreateSaveSnapshot();
            var canonical = ProjectSerializer.Serialize(snapshot.Value);
            Assert.That(canonical.IsSuccess, Is.True, canonical.Diagnostic?.Message);
            Assert.That(canonical.Value.IndexOf("\"expressions\"", StringComparison.Ordinal), Is.EqualTo(-1));
            var parsed = ProjectSerializer.Deserialize(canonical.Value);
            Assert.That(parsed.IsSuccess, Is.True, parsed.Diagnostic?.Message);
            Assert.That(parsed.Value.Nodes[0].Parameters[0].Expression.Kind, Is.EqualTo("Control"));
            Assert.That(parsed.Value.Nodes[0].Parameters[0].OutputMinimum.FloatValue, Is.EqualTo(-1));
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes(canonical.Value));
            var loaded = new ProjectLoader().Load("root", fs);
            Assert.That(loaded.IsSuccess, Is.True, loaded.Diagnostic?.Message);
            Assert.That(loaded.Value.Document.FindExpression(nodeId, parameterId), Is.Not.Null);
        }

        [Test]
        [Category("PERSIST_MIGRATION")]
        public void NodeMigrationRegistry_RequiresAndAppliesSequentialVersions()
        {
            var registry = new NodeMigrationRegistry();
            var type = new NodeTypeId("vendor.test.node");
            Assert.That(registry.Register(new TestNodeMigrator(type, 1, "v2")).IsSuccess, Is.True);
            Assert.That(registry.Register(new TestNodeMigrator(type, 2, "v3")).IsSuccess, Is.True);
            var migrated = registry.Migrate(type, 1, 3, "{}");
            Assert.That(migrated.IsSuccess, Is.True);
            Assert.That(migrated.Value, Is.EqualTo("v3"));
        }

        [Test]
        [Category("PERSIST_MIGRATION")]
        public void ProjectMigrationRegistry_RequiresContinuousVersionsAndCopiesInput()
        {
            var registry = new ProjectFormatMigrationRegistry();
            Assert.That(registry.Register(new TestProjectMigrator(1, 2)).IsSuccess, Is.True);
            Assert.That(registry.Register(new TestProjectMigrator(2, 3)).IsSuccess, Is.True);
            var source = new ProjectDocumentDto { ProjectFormatVersion = 1, ProjectName = "v1", Settings = new SettingsDto(), Nodes = new List<NodeDto>(), Connections = new List<ConnectionDto>(), LogicalControls = new List<LogicalControlDto>(), ControlMappings = new List<ControlMappingDto>(), Presets = new List<PresetDto>(), MediaAssets = new List<MediaAssetDto>(), Ui = new UiDto() };
            var migrated = registry.Migrate(source, 3);
            Assert.That(migrated.IsSuccess, Is.True);
            Assert.That(migrated.Value.ProjectFormatVersion, Is.EqualTo(3));
            Assert.That(source.ProjectFormatVersion, Is.EqualTo(1));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_InvalidMainNeverReplacesExistingValidBackup()
        {
            var fs = new FakeFileSystem();
            var backup = Encoding.UTF8.GetBytes(EmptyManifest("Backup"));
            fs.WriteAllBytes("root/project.json", Encoding.UTF8.GetBytes("broken"));
            fs.WriteAllBytes("root/project.json.bak", backup);
            var result = new ProjectSaver().Save(new ProjectDocument("New"), "root", fs);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fs.ReadAllBytes("root/project.json.bak"), Is.EqualTo(backup));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_FlushFailurePreservesSavedTokenAndMain()
        {
            var fs = new FakeFileSystem { FailFlush = true };
            var document = new ProjectDocument("FlushFailure");
            var before = document.SavedToken;
            var result = new ProjectSaver().Save(document, "root", fs);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(fs.Exists("root/project.json"), Is.False);
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'tmp.flush'"));
            Assert.That(result.Diagnostic.Message, Does.Contain("project.json.tmp"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_ReadbackFailurePreservesSavedTokenAndMain()
        {
            var fs = new FakeFileSystem { FailReadback = true };
            var document = new ProjectDocument("ReadbackFailure");
            var before = document.SavedToken;
            var result = new ProjectSaver().Save(document, "root", fs);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(fs.Exists("root/project.json"), Is.False);
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'tmp.readback'"));
            Assert.That(result.Diagnostic.Message, Does.Contain("project.json.tmp"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_ReplaceFailurePreservesExistingMainAndSavedToken()
        {
            var fs = new FakeFileSystem { FailReplace = true };
            var original = Encoding.UTF8.GetBytes(EmptyManifest("Original"));
            fs.WriteAllBytes("root/project.json", original);
            var document = new ProjectDocument("ReplaceFailure");
            var before = document.SavedToken;
            var result = new ProjectSaver().Save(document, "root", fs);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(fs.ReadAllBytes("root/project.json"), Is.EqualTo(original));
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'manifest.replace'"));
            Assert.That(result.Diagnostic.Message, Does.Contain("project.json"));
        }

        [Test]
        [Category("PERSIST_MIGRATION")]
        public void MigrationBackup_FlushFailureStopsBeforeMigration()
        {
            var fs = new FakeFileSystem { FailFlush = true };
            var result = ProjectMigrationBackup.Write(fs, "root", Encoding.UTF8.GetBytes(EmptyManifest("Old")), 1);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(fs.EnumerateFiles("root/Backups").Any(), Is.False);
        }

        [Test]
        [Category("PERSIST_MIGRATION")]
        public void MigrationBackup_RetainsOnlyTheNewestFiveVerifiedBackups()
        {
            var fs = new FakeFileSystem();
            for (var index = 0; index < 5; index++)
                fs.WriteAllBytes("root/Backups/pre-migration-2024010100000000" + index + "-v1-old" + index + ".json", Encoding.UTF8.GetBytes("old-" + index));

            var result = ProjectMigrationBackup.Write(fs, "root", Encoding.UTF8.GetBytes(EmptyManifest("Current")), 1);

            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            var backups = fs.EnumerateFiles("root/Backups")
                .Where(x => Path.GetFileName(x).StartsWith("pre-migration-", StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            Assert.That(backups, Has.Length.EqualTo(5));
            Assert.That(backups.Any(x => x.EndsWith("old0.json", StringComparison.Ordinal)), Is.False);
            Assert.That(backups.Any(x => string.Equals(x.Replace('\\', '/'), result.Value.Replace('\\', '/'), StringComparison.Ordinal)), Is.True, "The verified backup returned by Write must be one of the retained five backups.");
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void Saver_InterruptionBeforeReplacePreservesLastSuccessfulMain()
        {
            var fs = new FakeFileSystem { FailFlush = true };
            var original = Encoding.UTF8.GetBytes(EmptyManifest("LastGood"));
            fs.WriteAllBytes("root/project.json", original);
            var document = new ProjectDocument("Interrupted");
            var before = document.SavedToken;

            var result = new ProjectSaver().Save(document, "root", fs);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(fs.ReadAllBytes("root/project.json"), Is.EqualTo(original));
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(result.Diagnostic.Message, Does.Contain("stage 'tmp.flush'"));
        }

        [Test]
        [Category("PERSIST_SAVE")]
        public void PortableSaveAs_MoveFailureCleansOnlyStagingAndKeepsDocumentState()
        {
            var fs = new FakeFileSystem { FailMoveDirectory = true };
            var document = new ProjectDocument("Portable");
            var before = document.SavedToken;
            var result = new PortableProjectSaver().SaveAs(document, "source", "target", fs);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(document.SavedToken, Is.EqualTo(before));
            Assert.That(document.IsDirty, Is.False);
            Assert.That(fs.DirectoryExists("target"), Is.False);
            Assert.That(fs.EnumerateFiles("source").Any(), Is.False);
            Assert.That(fs.EnumerateFiles(".").Any(x => x.Contains(".staging-", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void MediaAssetStore_ImportFailureLeavesNoStagedAsset()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("source.png", Encoding.UTF8.GetBytes("asset"));
            fs.FailWrite = true;
            var result = MediaAssetStore.Import("source.png", "root", fs, "asset");
            Assert.That(result.IsFailure, Is.True);
            Assert.That(fs.EnumerateFiles("root/Assets").Any(), Is.False);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void MediaAssetStore_ImportFlushesAndAtomicallyRenamesBeforeReturning()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("source.png", Encoding.UTF8.GetBytes("asset"));
            var result = MediaAssetStore.Import("source.png", "root", fs, "asset");
            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            var finalPath = "root/" + result.Value.Asset.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            Assert.That(fs.Exists(finalPath), Is.True);
            Assert.That(fs.EnumerateFiles("root/Assets").Any(x => x.EndsWith(".importing", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void MediaAssetStore_ImportUsesStreamingPortWithoutReadAllBytes()
        {
            var fs = new FakeFileSystem { FailMediaReadAllBytes = true };
            fs.WriteAllBytes("source.png", Encoding.UTF8.GetBytes("large-enough-for-stream-boundary"));

            var result = MediaAssetStore.Import("source.png", "root", fs, "asset");

            Assert.That(result.IsSuccess, Is.True, result.Diagnostic?.Message);
            Assert.That(fs.Operations.Any(x => x.StartsWith("flush:root/Assets/", StringComparison.Ordinal) && x.EndsWith(".importing", StringComparison.Ordinal)), Is.True);
            Assert.That(fs.Operations.Any(x => x.StartsWith("move:root/Assets/", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        [Category("PERSIST_ASSET")]
        public void MediaAssetStore_ProbeFailureCleansStagedTransaction()
        {
            var fs = new FakeFileSystem();
            fs.WriteAllBytes("source.png", Encoding.UTF8.GetBytes("asset"));
            var result = MediaAssetStore.Import("source.png", "root", fs, "asset", MediaAssetKind.Experimental, MediaColorSpace.SRgb, MediaAlphaMode.Opaque, new RejectingProbe());
            Assert.That(result.IsFailure, Is.True);
            Assert.That(fs.EnumerateFiles("root/Assets").Any(), Is.False);
        }

        private static string EmptyManifest(string name) => "{\"projectFormatVersion\":1,\"projectName\":\"" + name + "\",\"settings\":{},\"graph\":{\"nodes\":[],\"connections\":[]},\"logicalControls\":[],\"controlMappings\":[],\"presets\":[],\"mediaAssets\":[],\"ui\":{}}";
        private static string SerializeSingleParameter(ParameterType type, ParameterValue value)
        {
            var definition = new ParameterDefinition(new ParameterId("parameter.value"), "Value", type, value);
            var node = new NodeRecord(NodeInstanceId.New(), new NodeTypeId("shitdesigner.test.node"), 1, "Node", true, new ProjectPosition(0, 0),
                new[] { new ParameterRecord(definition, value) }, rawState: "{}");
            var document = new ProjectDocument("Parameter");
            Assert.That(new ProjectCommandProcessor(document).AddNode(node).IsSuccess, Is.True);
            document.BeginSave();
            var snapshot = document.TryCreateSaveSnapshot();
            Assert.That(snapshot.IsSuccess, Is.True, snapshot.Diagnostic?.Message);
            var json = ProjectSerializer.Serialize(snapshot.Value);
            Assert.That(json.IsSuccess, Is.True, json.Diagnostic?.Message);
            return json.Value;
        }
        private static string MediaManifest(string name, MediaAssetId id, byte[] payload)
        {
            return "{\"projectFormatVersion\":1,\"projectName\":\"" + name + "\",\"settings\":{},\"graph\":{\"nodes\":[],\"connections\":[]},\"logicalControls\":[],\"controlMappings\":[],\"presets\":[],\"mediaAssets\":[{\"mediaAssetId\":\"" + id.Value + "\",\"displayName\":\"asset\",\"relativePath\":\"Assets/" + id.Value + "/source.png\",\"byteSize\":" + payload.LongLength + ",\"integrityAlgorithm\":\"xxh3_128\",\"integrityHash\":\"" + AssetIntegrity.Hash(payload) + "\",\"kind\":\"Image\",\"colorSpace\":\"sRGB\",\"alphaMode\":\"Straight\"}],\"ui\":{}}";
        }

        private sealed class TestNodeMigrator : INodeStateMigrator
        {
            private readonly string _result;
            public NodeTypeId NodeTypeId { get; }
            public int FromVersion { get; }
            public int ToVersion => FromVersion + 1;
            public TestNodeMigrator(NodeTypeId nodeTypeId, int fromVersion, string result) { NodeTypeId = nodeTypeId; FromVersion = fromVersion; _result = result; }
            public Result<string> Migrate(string rawJson) => Result<string>.Success(_result);
        }

        private static byte[] CreateOfficialXxh3SanityFixture(int length)
        {
            if (length < 0 || length > 2367) throw new ArgumentOutOfRangeException(nameof(length));
            const ulong prime64 = 11400714785074694797UL;
            var byteGenerator = (ulong)2654435761U;
            var bytes = new byte[length];
            unchecked
            {
                for (var index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = (byte)(byteGenerator >> 56);
                    byteGenerator *= prime64;
                }
            }
            return bytes;
        }

        private sealed class FragmentedReadStream : MemoryStream
        {
            private readonly int _maximumRead;

            public FragmentedReadStream(byte[] bytes, int maximumRead) : base(bytes, false)
            {
                if (maximumRead <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRead));
                _maximumRead = maximumRead;
            }

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, _maximumRead));
        }

        private sealed class TestProjectMigrator : IProjectFormatMigrator
        {
            public int FromVersion { get; }
            public int ToVersion => FromVersion + 1;
            public TestProjectMigrator(int fromVersion, int toVersion) { FromVersion = fromVersion; }
            public Result<ProjectDocumentDto> Migrate(ProjectDocumentDto sourceCopy) { sourceCopy.ProjectFormatVersion = ToVersion; sourceCopy.ProjectName += "-migrated"; return Result<ProjectDocumentDto>.Success(sourceCopy); }
        }

        private sealed class TestNodeCatalog : INodeSchemaCatalog
        {
            private readonly NodeTypeId _type;
            private readonly int _version;
            public TestNodeCatalog() { _type = new NodeTypeId("vendor.catalog.unavailable"); _version = 1; }
            public TestNodeCatalog(NodeTypeId type, int version) { _type = type; _version = version; }
            public bool TryGetCurrentSchema(NodeTypeId nodeTypeId, out int currentSchemaVersion)
            {
                if (nodeTypeId == _type) { currentSchemaVersion = _version; return true; }
                currentSchemaVersion = 0;
                return false;
            }
        }

        private sealed class RejectingProbe : IMediaAssetProbe
        {
            public Result Probe(Stream stagedStream, string extension) => Result.Failure(new Diagnostic(new DiagnosticCode("test.probe.rejected"), Severity.Error, "rejected"));
        }

        private sealed class NonAtomicFileSystem : IProjectFileSystem
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public bool Exists(string path) => _files.ContainsKey(Key(path));
            public string GetFullPath(string path) => Key(path);
            public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
            public byte[] ReadAllBytes(string path) => _files[Key(path)].ToArray();
            public void WriteAllBytes(string path, byte[] bytes) => _files[Key(path)] = bytes.ToArray();
            public void EnsureDirectory(string path) { }
            public void AtomicReplace(string temporaryPath, string mainPath, string backupPath, bool backupMain) => throw new IOException("atomic replacement is unavailable");
            public IEnumerable<string> EnumerateFiles(string directory) => _files.Keys.Where(x => x.StartsWith(Key(directory), StringComparison.Ordinal));
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => _files[Key(destinationPath)] = _files[Key(sourcePath)].ToArray();
            public void Delete(string path) => _files.Remove(Key(path));
            private static string Key(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        private sealed class FakeFileSystem : IProjectFileSystem, IProjectDurableFileSystem, IProjectAtomicFileOperations, IProjectStreamingFileOperations, IAtomicManifestWriter, IProjectDirectoryOperations, IProjectDirectoryCleanup
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly HashSet<string> _reparsePoints = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal);
            public bool FailWrite { get; set; }
            public bool FailReplace { get; set; }
            public bool FailFlush { get; set; }
            public bool FailReadback { get; set; }
            public bool FailMoveDirectory { get; set; }
            public bool EscapeMediaPath { get; set; }
            public bool FailMediaReadAllBytes { get; set; }
            public List<string> Operations { get; } = new List<string>();
            public bool Exists(string path) => _files.ContainsKey(Key(path));
            public string GetFullPath(string path)
            {
                var key = Key(path).TrimEnd('/');
                return EscapeMediaPath && key.Contains("/Assets/", StringComparison.Ordinal) ? "/outside/" + key.Substring(key.IndexOf("/Assets/", StringComparison.Ordinal) + 1) : key;
            }
            public FileAttributes GetAttributes(string path) => _reparsePoints.Contains(Key(path)) ? FileAttributes.ReparsePoint : FileAttributes.Normal;
            public byte[] ReadAllBytes(string path) { if (FailMediaReadAllBytes && !Key(path).Contains("project.json", StringComparison.Ordinal)) throw new IOException("media ReadAllBytes is forbidden in this test"); if (FailReadback && Key(path).EndsWith("project.json.tmp", StringComparison.Ordinal)) throw new IOException("injected readback failure"); return _files[Key(path)].ToArray(); }
            public void WriteAllBytes(string path, byte[] bytes) { if (FailWrite) throw new IOException("injected write failure"); _files[Key(path)] = bytes.ToArray(); }
            public Stream OpenRead(string path) => new MemoryStream(_files[Key(path)].ToArray(), false);
            public Stream OpenWrite(string path, bool overwrite)
            {
                if (FailWrite) throw new IOException("injected write failure");
                var key = Key(path);
                if (!overwrite && _files.ContainsKey(key)) throw new IOException("exists");
                return new MemoryFileWriteStream(bytes => _files[key] = bytes);
            }
            public void Flush(string path) { if (FailFlush) throw new IOException("injected flush failure"); Operations.Add("flush:" + Key(path)); }
            public void EnsureDirectory(string path) { _directories.Add(Key(path)); }
            public void AtomicReplace(string temporaryPath, string mainPath, string backupPath, bool backupMain) { if (FailReplace) throw new IOException("injected replace failure"); var temp = Key(temporaryPath); var main = Key(mainPath); var backup = Key(backupPath); Operations.Add("replace:" + temp + "->" + main); if (backupMain && _files.ContainsKey(main) && backup != string.Empty) _files[backup] = _files[main].ToArray(); _files[main] = _files[temp].ToArray(); _files.Remove(temp); }
            public void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain) => AtomicReplace(temporaryPath, mainPath, backupPath, backupMain);
            public void AtomicMove(string sourcePath, string destinationPath) { var source = Key(sourcePath); var destination = Key(destinationPath); Operations.Add("move:" + source + "->" + destination); if (_files.ContainsKey(destination)) throw new IOException("exists"); _files[destination] = _files[source].ToArray(); _files.Remove(source); }
            public IEnumerable<string> EnumerateFiles(string directory) => _files.Keys.Where(x => x.StartsWith(Key(directory), StringComparison.Ordinal));
            public void CopyFile(string sourcePath, string destinationPath, bool overwrite) { var source = Key(sourcePath); var destination = Key(destinationPath); Operations.Add("copy:" + source + "->" + destination); if (!overwrite && _files.ContainsKey(destination)) throw new IOException("exists"); _files[destination] = _files[source].ToArray(); }
            public void Delete(string path) => _files.Remove(Key(path));
            public bool DirectoryExists(string path) { var key = Key(path); return _directories.Contains(key) || _files.Keys.Any(x => x.StartsWith(key + "/", StringComparison.Ordinal)); }
            public void MoveDirectory(string sourcePath, string destinationPath) { if (FailMoveDirectory) throw new IOException("injected directory move failure"); var source = Key(sourcePath); var destination = Key(destinationPath); foreach (var file in _files.Keys.Where(x => x == source || x.StartsWith(source + "/", StringComparison.Ordinal)).ToList()) { var value = _files[file]; _files.Remove(file); _files[destination + file.Substring(source.Length)] = value; } _directories.Remove(source); _directories.Add(destination); }
            public void DeleteDirectory(string path) { var key = Key(path); foreach (var file in _files.Keys.Where(x => x == key || x.StartsWith(key + "/", StringComparison.Ordinal)).ToList()) _files.Remove(file); foreach (var directory in _directories.Where(x => x == key || x.StartsWith(key + "/", StringComparison.Ordinal)).ToList()) _directories.Remove(directory); }
            public void MarkReparse(string path) => _reparsePoints.Add(Key(path));
            private static string Key(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');

            private sealed class MemoryFileWriteStream : MemoryStream
            {
                private readonly Action<byte[]> _commit;
                public MemoryFileWriteStream(Action<byte[]> commit) { _commit = commit; }
                protected override void Dispose(bool disposing)
                {
                    if (disposing) _commit(ToArray());
                    base.Dispose(disposing);
                }
            }
        }
    }
}
