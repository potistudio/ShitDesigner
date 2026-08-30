using System;
using ShitDesigner.Nodes;
using UnityEngine;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// A deliberately small, inspector-driven shader playground.  It resolves
	/// one generated manifest entry, renders that family shader into an owned
	/// texture, and puts the texture on the assigned display renderer.  This
	/// keeps the playground independent from the production composition root
	/// while still exercising the same direct Shader references and uniform
	/// names used by the VJ runtime.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class VJShaderPlayground : MonoBehaviour {
		public enum ClockMode {
			Automatic,
			Manual
		}

		[Header("Catalog and node")]
		[Tooltip("Generated catalog asset. It is a scene reference for validation and discoverability.")]
		[SerializeField] private NodeTypeCatalog nodeTypeCatalog;
		[Tooltip("Generated manifest used to resolve the selected Type ID to its strip-safe Shader reference.")]
		[SerializeField] private ShaderNodeManifestAsset shaderManifest;
		[Tooltip("A generated Type ID, for example shitdesigner.shader.generator.solid-color.")]
		[SerializeField] private string selectedTypeId = "shitdesigner.shader.generator.solid-color";
		[Tooltip("Optional escape hatch for experimenting with a shader that is not in the manifest.")]
		[SerializeField] private Shader shaderOverride;

		[Header("Input and output")]
		[Tooltip("Primary image input. Generators can leave this empty; black is used automatically.")]
		[SerializeField] private Texture inputTexture;
		[Tooltip("Secondary image input for blend/composite nodes.")]
		[SerializeField] private Texture secondaryTexture;
		[Tooltip("History slot 0 for temporal/stateful nodes.")]
		[SerializeField] private Texture historyTexture;
		[Tooltip("History slot 1 for temporal/stateful nodes.")]
		[SerializeField] private Texture historyTexture2;
		[Tooltip("History slot 2 for temporal/stateful nodes.")]
		[SerializeField] private Texture historyTexture3;
		[Tooltip("Renderer whose material receives the playground output texture.")]
		[SerializeField] private Renderer outputRenderer;
		[Tooltip("Optional display material. PreviewDisplay is used when empty.")]
		[SerializeField] private Material displayMaterial;
		[SerializeField] private int outputWidth = 640;
		[SerializeField] private int outputHeight = 360;
		[SerializeField] private FilterMode outputFilter = FilterMode.Bilinear;

		[Header("Clock and reset")]
		[SerializeField] private ClockMode clockMode = ClockMode.Automatic;
		[SerializeField] private float manualTime;
		[SerializeField] private float timeSpeed = 1f;
		[SerializeField] private bool paused;
		[Tooltip("Toggle this in the Inspector to clear the owned output and advance one clean frame.")]
		[SerializeField] private bool resetOnNextFrame;
		[SerializeField] private int seed;

		[Header("Common VJ parameters")]
		[Range(0f, 1f)][SerializeField] private float amount = 0.5f;
		[SerializeField] private float frequency = 4f;
		[SerializeField] private float detail = 4f;
		[SerializeField] private float speed = 1f;
		[SerializeField] private float phase;
		[SerializeField] private float scale = 1f;
		[SerializeField] private float radius = 1f;
		[SerializeField] private float softness = 0.05f;
		[SerializeField] private float threshold = 0.5f;
		[SerializeField] private float hue;
		[SerializeField] private float saturation = 1f;
		[SerializeField] private float contrast = 1f;
		[SerializeField] private Vector4 center = new Vector4(0.5f, 0.5f, 0f, 0f);
		[SerializeField] private Color colorA = Color.red;
		[SerializeField] private Color colorB = Color.blue;
		[SerializeField] private Color colorC = Color.green;

		private Material _shaderMaterial;
		private Material _displayMaterialInstance;
		private RenderTexture _outputTexture;
		private ShaderNodeManifestAssetEntry _entry;
		private Shader _resolvedShader;
		private string _resolvedTypeId;
		private Shader _resolvedOverride;
		private float _clock;
		private ulong _frame;
		private bool _resourcesReady;
		private string _lastError;

		public string SelectedTypeId => selectedTypeId ?? string.Empty;
		public RenderTexture OutputTexture => _outputTexture;
		public string LastError => _lastError ?? string.Empty;

		private void OnEnable() {
			EnsureResources();
			RenderFrame(true);
		}

		private void Update() {
			if (!UnityEngine.Application.isPlaying) return;
			RenderFrame(false);
		}

		private void OnDisable() => ReleaseResources();

		private void OnValidate() {
			_resourcesReady = false;
			outputWidth = Mathf.Clamp(outputWidth, 16, 4096);
			outputHeight = Mathf.Clamp(outputHeight, 16, 4096);
			timeSpeed = Mathf.Clamp(timeSpeed, -100f, 100f);
			frequency = Mathf.Max(0f, frequency);
			detail = Mathf.Max(0f, detail);
			scale = Mathf.Max(0.0001f, scale);
			radius = Mathf.Max(0f, radius);
			softness = Mathf.Max(0f, softness);
			if (isActiveAndEnabled && !UnityEngine.Application.isPlaying) RenderFrame(false);
		}

		[ContextMenu("Reset Playground Output")]
		public void ResetPlaygroundOutput() {
			resetOnNextFrame = true;
			if (isActiveAndEnabled) RenderFrame(true);
		}

		private void EnsureResources() {
			if (!string.Equals(_resolvedTypeId, selectedTypeId ?? string.Empty, StringComparison.Ordinal)) {
				ResolveEntryAndShader();
				_resourcesReady = false;
			}
			if (_resolvedOverride != shaderOverride) {
				ResolveEntryAndShader();
				_resourcesReady = false;
			}
			if (_resourcesReady && _outputTexture != null && _resolvedShader != null) return;
			ResolveEntryAndShader();
			if (_resolvedShader == null) return;

			var width = Mathf.Clamp(outputWidth, 16, 4096);
			var height = Mathf.Clamp(outputHeight, 16, 4096);
			if (_outputTexture == null || _outputTexture.width != width || _outputTexture.height != height) {
				ReleaseOutputTexture();
				_outputTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) {
					name = "VJShaderPlayground.Output",
					filterMode = outputFilter,
					wrapMode = TextureWrapMode.Clamp,
					useMipMap = false,
					autoGenerateMips = false
				};
				_outputTexture.Create();
			}

			if (_shaderMaterial == null || _shaderMaterial.shader != _resolvedShader) {
				DestroyMaterial(ref _shaderMaterial);
				_shaderMaterial = new Material(_resolvedShader) { name = "VJShaderPlayground.Shader" };
			}
			if (_displayMaterialInstance == null) {
				var source = displayMaterial;
				if (source == null) {
					var displayShader = Shader.Find("Hidden/ShitDesigner/PreviewDisplay");
					if (displayShader != null) source = new Material(displayShader);
				}
				if (source != null) _displayMaterialInstance = new Material(source) { name = "VJShaderPlayground.Display" };
			}
			if (outputRenderer == null) outputRenderer = GetComponent<Renderer>();
			if (outputRenderer != null && _displayMaterialInstance != null) outputRenderer.sharedMaterial = _displayMaterialInstance;
			_resourcesReady = _outputTexture != null && _shaderMaterial != null;
		}

		private void ResolveEntryAndShader() {
			var manifest = shaderManifest != null ? shaderManifest : nodeTypeCatalog == null ? null : nodeTypeCatalog.ShaderManifest;
			_entry = manifest == null ? null : manifest.Find(selectedTypeId);
			_resolvedShader = shaderOverride != null ? shaderOverride : (_entry == null ? null : _entry.Shader);
			_resolvedTypeId = selectedTypeId ?? string.Empty;
			_resolvedOverride = shaderOverride;
			_lastError = _resolvedShader == null
				? "Select a generated Type ID with a direct Shader reference, or assign Shader Override."
				: string.Empty;
		}

		private void RenderFrame(bool forceReset) {
			EnsureResources();
			if (!_resourcesReady || _shaderMaterial == null || _outputTexture == null) return;
			if (forceReset || resetOnNextFrame) {
				ClearOutput();
				_clock = clockMode == ClockMode.Manual ? manualTime : 0f;
				_frame = 0;
				resetOnNextFrame = false;
			}
			if (clockMode == ClockMode.Manual) _clock = manualTime;
			else if (!paused && UnityEngine.Application.isPlaying) _clock += Time.unscaledDeltaTime * timeSpeed;
			_frame++;

			ApplyUniforms();
			var source = inputTexture == null ? Texture2D.blackTexture : inputTexture;
			_shaderMaterial.SetTexture("_MainTex", source);
			_shaderMaterial.SetTexture("_TexA", source);
			_shaderMaterial.SetTexture("_TexB", secondaryTexture == null ? source : secondaryTexture);
			_shaderMaterial.SetTexture("_SD_SourceTex", source);
			_shaderMaterial.SetTexture("_HistoryTex", historyTexture == null ? Texture2D.blackTexture : historyTexture);
			_shaderMaterial.SetTexture("_HistoryTex2", historyTexture2 == null ? Texture2D.blackTexture : historyTexture2);
			_shaderMaterial.SetTexture("_HistoryTex3", historyTexture3 == null ? Texture2D.blackTexture : historyTexture3);
			var pass = _entry == null ? 0 : Mathf.Clamp(_entry.OutputPass, 0, Mathf.Max(0, _resolvedShader.passCount - 1));
			Graphics.Blit(source, _outputTexture, _shaderMaterial, pass);
			if (_displayMaterialInstance != null) {
				_displayMaterialInstance.SetTexture("_MainTex", _outputTexture);
				_displayMaterialInstance.SetVector("_SourceSize", new Vector4(_outputTexture.width, _outputTexture.height, 0f, 0f));
				_displayMaterialInstance.SetVector("_DestinationSize", new Vector4(_outputTexture.width, _outputTexture.height, 0f, 0f));
			}
			if (outputRenderer != null && _displayMaterialInstance != null) outputRenderer.sharedMaterial = _displayMaterialInstance;
		}

		private void ApplyUniforms() {
			var time = float.IsNaN(_clock) || float.IsInfinity(_clock) ? 0f : _clock;
			var resolution = new Vector4(_outputTexture.width, _outputTexture.height, 1f / _outputTexture.width, 1f / _outputTexture.height);
			_shaderMaterial.SetFloat("_SD_Time", time);
			_shaderMaterial.SetFloat("_SD_DeltaTime", paused ? 0f : (UnityEngine.Application.isPlaying ? Time.unscaledDeltaTime : 0f));
			_shaderMaterial.SetFloat("_SD_Frame", _frame);
			_shaderMaterial.SetVector("_SD_Resolution", resolution);
			_shaderMaterial.SetFloat("_SD_Seed", seed);
			_shaderMaterial.SetFloat("_SD_PassIndex", 0f);
			_shaderMaterial.SetFloat("_SD_PassCount", Mathf.Max(1, _resolvedShader.passCount));
			_shaderMaterial.SetFloat("_VJVariant", _entry == null ? 0f : _entry.SourceVariant);
			_shaderMaterial.SetFloat("_Variant", _entry == null ? 0f : _entry.SourceVariant);
			_shaderMaterial.SetFloat("_GraphTime", time);
			_shaderMaterial.SetFloat("_Frame", _frame);
			_shaderMaterial.SetVector("_Resolution", resolution);
			_shaderMaterial.SetFloat("_Seed", seed);
			SetFloat("_VJAmount", amount); SetFloat("_VJFrequency", frequency); SetFloat("_VJDetail", detail);
			SetFloat("_VJSpeed", speed); SetFloat("_VJPhase", phase); SetFloat("_VJScale", scale); SetFloat("_VJRadius", radius);
			SetFloat("_VJSoftness", softness); SetFloat("_VJThreshold", threshold); SetFloat("_VJHue", hue); SetFloat("_VJSaturation", saturation); SetFloat("_VJContrast", contrast);
			SetVector("_VJCenter", center); SetVector("_VJColorA", colorA); SetVector("_VJColorB", colorB); SetVector("_VJColorC", colorC);
		}

		private void SetFloat(string property, float value) { if (_shaderMaterial.HasProperty(property)) _shaderMaterial.SetFloat(property, value); }
		private void SetVector(string property, Vector4 value) { if (_shaderMaterial.HasProperty(property)) _shaderMaterial.SetVector(property, value); }
		private void SetVector(string property, Color value) { if (_shaderMaterial.HasProperty(property)) _shaderMaterial.SetVector(property, new Vector4(value.r, value.g, value.b, value.a)); }

		private void ClearOutput() {
			if (_outputTexture == null) return;
			var previous = RenderTexture.active;
			RenderTexture.active = _outputTexture;
			GL.Clear(false, true, Color.black);
			RenderTexture.active = previous;
		}

		private void ReleaseResources() {
			ReleaseOutputTexture();
			DestroyMaterial(ref _shaderMaterial);
			DestroyMaterial(ref _displayMaterialInstance);
			_resourcesReady = false;
		}

		private void ReleaseOutputTexture() {
			if (_outputTexture == null) return;
			if (RenderTexture.active == _outputTexture) RenderTexture.active = null;
			_outputTexture.Release();
			DestroyUnityObject(_outputTexture);
			_outputTexture = null;
		}

		private static void DestroyMaterial(ref Material material) {
			if (material == null) return;
			DestroyUnityObject(material);
			material = null;
		}

		private static void DestroyUnityObject(UnityEngine.Object value) {
			if (value == null) return;
			if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}
	}
}
