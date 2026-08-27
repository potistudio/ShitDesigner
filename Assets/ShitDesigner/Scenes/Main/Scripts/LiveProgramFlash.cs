using System;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Tracks the short-lived intensity envelope for a live Program flash.</summary>
	public sealed class LiveProgramFlashState {
		private readonly double _durationSeconds;
		private double _visibleUntil;

		public LiveProgramFlashState(double durationSeconds = .12d) {
			if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds <= 0d)
				throw new ArgumentOutOfRangeException(nameof(durationSeconds));
			_durationSeconds = durationSeconds;
		}

		public void Trigger(double graphTime) {
			if (double.IsNaN(graphTime) || double.IsInfinity(graphTime)) throw new ArgumentOutOfRangeException(nameof(graphTime));
			_visibleUntil = graphTime + _durationSeconds;
		}

		public float Sample(double graphTime) {
			if (double.IsNaN(graphTime) || double.IsInfinity(graphTime)) throw new ArgumentOutOfRangeException(nameof(graphTime));
			var remaining = _visibleUntil - graphTime;
			return remaining <= 0d ? 0f : Mathf.Clamp01((float)(remaining / _durationSeconds));
		}
	}

	/// <summary>Applies an input-triggered white flash after the Program shader graph.</summary>
	internal sealed class LiveProgramFlash : IDisposable {
		private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
		private static readonly int AssetFlashTexture = Shader.PropertyToID("_AssetFlashTexture");
		private static readonly int AssetFlashAmount = Shader.PropertyToID("_AssetFlashAmount");
		private readonly LiveProgramFlashState _state = new LiveProgramFlashState();
		private Material _material;
		private LiveProgramFlashState _assetState;
		private Texture _assetTexture;

		public LiveProgramFlash(Shader shader) {
			if (shader == null) throw new ArgumentNullException(nameof(shader));
			_material = new Material(shader) { name = "ShitDesigner.Main.LiveProgramFlash" };
		}

		public void Trigger(double graphTime) => _state.Trigger(graphTime);

		public void TriggerAsset(double graphTime, Texture texture, double durationSeconds) {
			if (texture == null) return;
			_assetState = new LiveProgramFlashState(durationSeconds);
			_assetState.Trigger(graphTime);
			_assetTexture = texture;
		}

		public void Render(RenderTexture source, RenderTexture destination, double graphTime) {
			if (source == null || destination == null) throw new ArgumentNullException(source == null ? nameof(source) : nameof(destination));
			var amount = _state.Sample(graphTime);
			var assetAmount = _assetState == null ? 0f : _assetState.Sample(graphTime);
			if (amount <= 0f && assetAmount <= 0f) {
				_assetState = null;
				_assetTexture = null;
				Graphics.Blit(source, destination);
				return;
			}
			_material.SetFloat(FlashAmount, amount);
			_material.SetTexture(AssetFlashTexture, _assetTexture);
			_material.SetFloat(AssetFlashAmount, assetAmount);
			Graphics.Blit(source, destination, _material);
			if (assetAmount <= 0f) {
				_assetState = null;
				_assetTexture = null;
			}
		}

		public void Dispose() {
			if (_material == null) return;
			UnityEngine.Object.DestroyImmediate(_material);
			_material = null;
			_assetState = null;
			_assetTexture = null;
		}
	}
}
