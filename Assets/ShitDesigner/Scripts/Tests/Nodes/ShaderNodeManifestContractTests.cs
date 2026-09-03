using System;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Nodes;

namespace ShitDesigner.Nodes.Tests {
	public sealed class ShaderNodeManifestContractTests {
		[Test]
		public void BuiltinManifest_IsValidAndContainsRecursiveRectangles() {
			var manifest = ShaderNodeManifest.CreateBuiltIn();
			var valid = ShaderNodeManifestValidator.Validate(manifest);

			Assert.That(valid.IsSuccess, Is.True, valid.IsFailure ? valid.Error.Message : string.Empty);
			Assert.That(manifest.Entries.Count, Is.EqualTo(4));
			Assert.That(manifest.Entries.Select(x => x.TypeId.Value), Is.EqualTo(new[]
			{
				"shitdesigner.shader.generator",
				"shitdesigner.shader.effect",
				"shitdesigner.shader.blend2",
				"shitdesigner.shader.generator.recursive-rectangles"
			}));
			var recursive = manifest.Find("shitdesigner.shader.generator.recursive-rectangles");
			Assert.That(recursive.Inputs, Is.Empty);
			Assert.That(recursive.Parameters.Select(x => x.Id.Value), Is.EqualTo(new[]
			{
				"max_depth", "min_leaf_size", "split_probability", "axis_mode", "ratio_min", "ratio_max", "seed", "beat_sync",
				"reveal_progress", "split_duration", "split_stagger", "easing", "color_a", "gutter", "line_color"
			}));
			Assert.That(recursive.Parameters.Single(x => x.Id.Value == "axis_mode").EnumMapping["random"], Is.EqualTo(3));
			Assert.That(recursive.Parameters.Single(x => x.Id.Value == "min_leaf_size").DefaultValue.AsFloat(), Is.EqualTo(.001f));
			Assert.That(recursive.Parameters.Single(x => x.Id.Value == "easing").EnumMapping["ease_in_out"], Is.EqualTo(4));
		}

		[Test]
		public void ManifestGenerator_ProducesCatalogBindingsWithTypedRoles() {
			var result = ShaderNodeManifestGenerator.GenerateCatalog(ShaderNodeManifest.CreateBuiltIn());

			Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
			var generator = result.Value.Entries.Single(x => x.TypeId.Value == "shitdesigner.shader.generator");
			var blend = result.Value.Entries.Single(x => x.TypeId.Value == "shitdesigner.shader.blend2");
			Assert.That(generator.ShaderBinding.Family, Is.EqualTo(ShaderNodeFamily.Generator));
			Assert.That(generator.ShaderBinding.Parameters.Single().Type, Is.EqualTo(ParameterType.Color));
			Assert.That(blend.ShaderBinding.Inputs.Single(x => x.PortId.Value == "b").Role, Is.EqualTo(ShaderInputRole.Secondary));
			Assert.That(blend.ShaderBinding.FindPass(0).VariantId, Is.EqualTo("default"));
		}

		[Test]
		public void ManifestValidator_RejectsUndeclaredOutputPass() {
			var entry = new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.invalid_pass"), "Invalid", "Shader/Utility",
				ShaderNodeFamily.Utility, "family.utility", passes: new[] { new ShaderNodeManifestPass("main", 0) }, outputPass: 1);

			var result = ShaderNodeManifestValidator.Validate(new ShaderNodeManifest(new[] { entry }));

			Assert.That(result.IsFailure, Is.True);
			Assert.That(result.Error.Code.Value, Is.EqualTo("nodes.shader_manifest_pass_range"));
		}

		[Test]
		public void ManifestEnumMapping_IsTypedAndDeterministic() {
			var parameter = new ShaderNodeManifestParameter(
				new ParameterId("mode"), "Mode", ParameterType.Enum, ParameterValue.FromEnum("high"),
				"_Mode", enumOptions: new[] { "low", "high" });
			var entry = new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.enum_test"), "Enum Test", "Shader/Utility",
				ShaderNodeFamily.Utility, "family.utility", parameters: new[] { parameter });

			var result = ShaderNodeManifestValidator.Validate(new ShaderNodeManifest(new[] { entry }));

			Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
			Assert.That(entry.ToShaderBinding().Parameters.Single().EnumMapping["low"], Is.EqualTo(0));
			Assert.That(entry.ToShaderBinding().Parameters.Single().EnumMapping["high"], Is.EqualTo(1));
		}
	}
}
