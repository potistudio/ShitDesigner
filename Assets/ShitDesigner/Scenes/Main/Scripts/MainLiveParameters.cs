using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public enum MainLiveParameterId {
		Scene,
		Motion,
		Scale
	}

	public readonly struct MainLiveParameterEvent {
		public ulong SequenceNumber { get; }
		public MainLiveParameterId ParameterId { get; }
		public float NormalizedValue { get; }

		public MainLiveParameterEvent(ulong sequenceNumber, MainLiveParameterId parameterId, float normalizedValue) {
			if (sequenceNumber == 0) throw new ArgumentOutOfRangeException(nameof(sequenceNumber));
			SequenceNumber = sequenceNumber;
			ParameterId = parameterId;
			NormalizedValue = normalizedValue;
		}
	}

	/// <summary>Immutable parameter values shared by scene selection, scene update, and rendering for one Main frame.</summary>
	public sealed class MainLiveParameterFrame {
		public ulong FrameNumber { get; }
		public int SceneIndex { get; }
		public float Motion { get; }
		public float Scale { get; }

		internal MainLiveParameterFrame(ulong frameNumber, int sceneIndex, float motion, float scale) {
			FrameNumber = frameNumber;
			SceneIndex = sceneIndex;
			Motion = motion;
			Scale = scale;
		}
	}

	/// <summary>Commits queued live input once at the Main frame boundary.</summary>
	public sealed class MainLiveParameterBuffer {
		private readonly List<MainLiveParameterEvent> _pending = new List<MainLiveParameterEvent>();
		private ulong _nextSequence = 1;
		private float _scene;
		private float _motion = 0.5f;
		private float _scale = 0.5f;

		public int PendingCount => _pending.Count;

		public void Enqueue(MainLiveParameterId parameterId, float normalizedValue) {
			_pending.Add(new MainLiveParameterEvent(_nextSequence++, parameterId, normalizedValue));
		}

		public MainLiveParameterFrame Commit(ulong frameNumber, int sceneCount) {
			if (frameNumber == 0) throw new ArgumentOutOfRangeException(nameof(frameNumber));
			if (sceneCount <= 0) throw new ArgumentOutOfRangeException(nameof(sceneCount));

			_pending.Sort((left, right) => left.SequenceNumber.CompareTo(right.SequenceNumber));
			foreach (var input in _pending) {
				var value = Normalize(input.NormalizedValue);
				switch (input.ParameterId) {
					case MainLiveParameterId.Scene: _scene = value; break;
					case MainLiveParameterId.Motion: _motion = value; break;
					case MainLiveParameterId.Scale: _scale = value; break;
			}
			}
			_pending.Clear();

			var sceneIndex = sceneCount == 1 ? 0 : (int)Math.Round(_scene * (sceneCount - 1), MidpointRounding.AwayFromZero);
			return new MainLiveParameterFrame(frameNumber, sceneIndex, _motion, _scale);
		}

		private static float Normalize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Min(1f, Math.Max(0f, value));
	}
}
