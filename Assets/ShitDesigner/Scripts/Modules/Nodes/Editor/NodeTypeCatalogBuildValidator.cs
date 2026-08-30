using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using ShitDesigner.Core;
using ShitDesigner.Nodes;

namespace ShitDesigner.Nodes.Editor {
	/// <summary>Generates the catalog and validates all production asset and
	/// platform bindings before a player build. Every failure is a build
	/// failure; none of these checks are test skips.</summary>
	public sealed class NodeTypeCatalogBuildValidator : IPreprocessBuildWithReport {
		public int callbackOrder => 0;
		private const string AssetPath = "Assets/ShitDesigner/Scripts/Modules/Nodes/NodeTypeCatalog.asset";
		private const string GeneratorShaderPath = "Assets/ShitDesigner/Scripts/Modules/Media/Shaders/BuiltinGenerator.shader";
		private const string EffectShaderPath = "Assets/ShitDesigner/Scripts/Modules/Media/Shaders/BuiltinEffect.shader";
		private const string BlendShaderPath = "Assets/ShitDesigner/Scripts/Modules/Media/Shaders/BuiltinBlend2.shader";
		private const string WindowsNativePath = "Assets/Plugins/x86_64/ShitDesignerHapNative/shitdesigner_hap.dll";
		private const string MacNativePath = "Assets/Plugins/macOS/ShitDesignerHapNative/shitdesigner_hap.dylib";

		public void OnPreprocessBuild(BuildReport report) {
			var result = GenerateAndValidate(report == null ? BuildTarget.StandaloneWindows64 : report.summary.platform);
			if (result.IsFailure) throw new BuildFailedException(result.Error.Message);
		}

		[MenuItem("ShitDesigner/Nodes/Generate Node Type Catalog")]
		public static void GenerateMenu() {
			var result = GenerateAndValidate(EditorUserBuildSettings.activeBuildTarget);
			if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			Debug.Log("ShitDesigner node catalog generated and validated.");
		}

		private static UnitResult<Diagnostic> GenerateAndValidate(BuildTarget target) {
			var generated = ShaderNodeManifestAssetGenerator.GenerateAndValidate();
			if (generated.IsFailure) return UnitResult.Failure<Diagnostic>(generated.Error);
			var asset = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>(AssetPath);
			var runtime = asset == null ? null : asset.BuildRuntimeCatalog().Value;
			if (asset == null || runtime == null) return Failure("nodes.catalog.asset_missing", "Generated NodeTypeCatalog asset is missing.");
			var valid = runtime.Validate();
			if (valid.IsFailure) return valid;
			var manifest = asset.ValidateManifest();
			if (manifest.IsFailure) return manifest;
			var exact = asset.ValidateAgainst(runtime);
			if (exact.IsFailure) return exact;
			var shaderValidation = ValidateShaderBindings(asset, runtime);
			if (shaderValidation.IsFailure) return shaderValidation;
			var apiValidation = ValidateGraphicsApis(target);
			if (apiValidation.IsFailure) return apiValidation;
			var nativeValidation = ValidateNativePlugin(target);
			if (nativeValidation.IsFailure) return nativeValidation;
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> ValidateShaderBindings(NodeTypeCatalog asset, NodeDefinitionCatalog runtime) {
			foreach (var entry in runtime.Entries.Where(x => x.ShaderBinding != null)) {
				var record = asset.Entries.FirstOrDefault(x => x.TypeId == entry.TypeId.Value);
				if (record == null) return Failure("nodes.catalog.shader_record_missing", "Shader binding record is missing for " + entry.TypeId.Value + ".");
				var shader = record.Shader ?? record.TemplateMaterial?.shader;
				if (shader == null) return Failure("nodes.catalog.shader_missing", "Shader asset is missing for " + entry.TypeId.Value + ".");
				if (record.OutputPass < 0 || record.OutputPass >= shader.passCount) return Failure("nodes.catalog.shader_pass", "Shader output pass is not present for " + entry.TypeId.Value + ".");
				foreach (var property in entry.ShaderBinding.InputProperties.Values)
					if (!HasShaderProperty(shader, property) && (record.TemplateMaterial == null || !record.TemplateMaterial.HasProperty(property))) return Failure("nodes.catalog.shader_property", "Shader input property is missing: " + property + " for " + entry.TypeId.Value + " (" + shader.name + ").");
				foreach (var property in entry.ShaderBinding.ParameterProperties.Values)
					if (!HasShaderProperty(shader, property) && (record.TemplateMaterial == null || !record.TemplateMaterial.HasProperty(property))) return Failure("nodes.catalog.shader_property", "Shader parameter property is missing: " + property + " for " + entry.TypeId.Value + " (" + shader.name + ").");
				if (shader.passCount == 0) return Failure("nodes.catalog.shader_variant", "Shader has no compiled pass/variant for " + entry.TypeId.Value + ".");
			}
			return UnitResult.Success<Diagnostic>();
		}

		private static bool HasShaderProperty(Shader shader, string property) {
			if (shader == null || string.IsNullOrEmpty(property)) return false;
			for (var index = 0; index < shader.GetPropertyCount(); index++)
				if (string.Equals(shader.GetPropertyName(index), property, StringComparison.Ordinal)) return true;
			return false;
		}

		private static UnitResult<Diagnostic> ValidateGraphicsApis(BuildTarget target) {
			if (target != BuildTarget.StandaloneWindows64) return UnitResult.Success<Diagnostic>();
			if (PlayerSettings.GetUseDefaultGraphicsAPIs(target)) return Failure("nodes.graphics.api_default", "Windows Standalone must use explicit graphics APIs.");
			var apis = PlayerSettings.GetGraphicsAPIs(target) ?? Array.Empty<GraphicsDeviceType>();
			if (apis.Length < 2 || apis[0] != GraphicsDeviceType.Direct3D12 || apis[1] != GraphicsDeviceType.Vulkan)
				return Failure("nodes.graphics.api_order", "Windows Standalone must list Direct3D12 first and Vulkan second.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> ValidateNativePlugin(BuildTarget target) {
			if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneOSX) return UnitResult.Success<Diagnostic>();
			var path = target == BuildTarget.StandaloneWindows64 ? WindowsNativePath : MacNativePath;
			if (!File.Exists(path)) return Failure("nodes.native.missing", "Required Hap native plugin is missing for " + target + ": " + path + ".");
			var importer = AssetImporter.GetAtPath(path) as PluginImporter;
			if (importer == null) return Failure("nodes.native.importer", "Required Hap native plugin has no PluginImporter: " + path + ".");
			if (target == BuildTarget.StandaloneWindows64 && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)) return Failure("nodes.native.platform", "Windows Hap native plugin is not enabled for Standalone Windows.");
			if (target == BuildTarget.StandaloneOSX && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX)) return Failure("nodes.native.platform", "macOS Hap native plugin is not enabled for Standalone macOS.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
	}
}
