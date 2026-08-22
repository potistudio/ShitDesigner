using System;
using NUnit.Framework;
using ShitDesigner.Core;

namespace ShitDesigner.Tests.Core
{
    public sealed class CoreContractTests
    {
        [Test]
        public void StableIds_NewValuesAreUuidV4()
        {
            Assert.That(NodeInstanceId.New().IsUuidV4, Is.True);
            Assert.That(MediaAssetId.New().IsUuidV4, Is.True);
            Assert.That(PresetId.New().IsUuidV4, Is.True);
            Assert.That(LogicalControlId.New().IsUuidV4, Is.True);
            Assert.That(NodeInstanceId.TryParseUuidV4("not-a-uuid", out _), Is.False);
        }

        [Test]
        public void StableStringIds_UseSpecificationNamingRules()
        {
            Assert.Throws<ArgumentException>(() => new NodeTypeId("Vendor.Category.Name"));
            Assert.Throws<ArgumentException>(() => new NodeTypeId("vendor.category"));
            Assert.Throws<ArgumentException>(() => new ParameterId("transport.Playhead"));
            Assert.Throws<ArgumentException>(() => new PortId("input.port"));
            Assert.That(new NodeTypeId("shitdesigner.shader.crossfade").Value, Is.EqualTo("shitdesigner.shader.crossfade"));
            Assert.That(new ParameterId("transport.playhead_seconds").Value, Is.EqualTo("transport.playhead_seconds"));
            Assert.That(new PortId("input_port").Value, Is.EqualTo("input_port"));
        }

        [Test]
        public void NodeTypeId_AllowsSpecifiedSystemTypesAndRejectsOtherTwoSegmentIds()
        {
            var systemTypes = new[]
            {
                "system.program_output",
                "system.preview",
                "system.feedback",
                "system.unknown_node"
            };
            foreach (var value in systemTypes)
                Assert.That(new NodeTypeId(value).Value, Is.EqualTo(value));

            var invalidTwoSegmentTypes = new[]
            {
                "system.other",
                "vendor.category",
                "shitdesigner.shader"
            };
            foreach (var value in invalidTwoSegmentTypes)
                Assert.Throws<ArgumentException>(() => new NodeTypeId(value));
        }

        [Test]
        public void ParameterValue_StringRejectsNulAnd4097Characters()
        {
            Assert.Throws<ArgumentException>(() => ParameterValue.FromString("a\0b"));
            Assert.Throws<ArgumentException>(() => ParameterValue.FromString(new string('a', 4097)));
            Assert.That(ParameterValue.FromString(new string('a', 4096)).AsString().Length, Is.EqualTo(4096));
        }

        [Test]
        public void ParameterValue_EnumDefaultAllowsEmptyButOptionIdsAreValidated()
        {
            Assert.That(ParameterValue.Default(ParameterType.Enum).AsString(), Is.Empty);
            Assert.Throws<ArgumentException>(() => ParameterValue.FromEnum("Bad Option"));
            Assert.That(ParameterValue.FromEnum("preset_a").AsString(), Is.EqualTo("preset_a"));
        }

        [Test]
        public void ParameterValue_ClampAndLerpValidateRangeAndClampComponents()
        {
            var value = ParameterValue.FromVector3(new Vector3Value(-1, .5f, 2));
            var clamped = ParameterValue.Clamp(value, ParameterValue.FromVector3(new Vector3Value(0, 0, 0)), ParameterValue.FromVector3(new Vector3Value(1, 1, 1)));
            Assert.That(clamped.IsSuccess, Is.True);
            Assert.That(clamped.Value.AsVector3(), Is.EqualTo(new Vector3Value(0, .5f, 1)));
            Assert.That(ParameterValue.Clamp(ParameterValue.FromFloat(.5f), ParameterValue.FromFloat(1), ParameterValue.FromFloat(0)).IsFailure, Is.True);
            Assert.That(ParameterValue.Lerp(ParameterValue.FromInt(0), ParameterValue.FromInt(3), .5f).Value.AsInt(), Is.EqualTo(2));
            Assert.That(ParameterValue.Lerp(ParameterValue.FromFloat(1), ParameterValue.FromFloat(0), .5f).IsFailure, Is.True);
        }

        [Test]
        public void DiagnosticCode_UsesLowerAsciiCodeAndExceptionIsCopied()
        {
            Assert.Throws<ArgumentException>(() => new DiagnosticCode("Module.Error"));
            var info = DiagnosticExceptionInfo.FromException(new InvalidOperationException("broken"));
            Assert.That(info.TypeName, Does.Contain("InvalidOperationException"));
            Assert.That(info.Message, Is.EqualTo("broken"));
        }
    }
}
