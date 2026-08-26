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
		private readonly LiveProgramFlashState _state = new LiveProgramFlashState();
		private Material _material;

		public LiveProgramFlash(Shader shader) {
			if (shader == null) throw new ArgumentNullException(nameof(shader));
			_material = new Material(shader) { name = "ShitDesigner.Main.LiveProgramFlash" };
		}

		public void Trigger(double graphTime) => _state.Trigger(graphTime);

		public void Render(RenderTexture source, RenderTexture destination, double graphTime) {
			if (source == null || destination == null) throw new ArgumentNullException(source == null ? nameof(source) : nameof(destination));
			var amount = _state.Sample(graphTime);
			if (amount <= 0f) {
				Graphics.Blit(source, destination);
				return;
			}
			_material.SetFloat(FlashAmount, amount);
			Graphics.Blit(source, destination, _material);
		}

		public void Dispose() {
			if (_material == null) return;
			UnityEngine.Object.DestroyImmediate(_material);
			_material = null;
		}
	}
}
