using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Media {
	[Serializable]
	public sealed class AssetFlashPatchSlot {
		[SerializeField] private Texture2D _image;

		public Texture2D Image => _image;
	}

	/// <summary>Authored image slots and timing for a live Asset Flash patch.</summary>
	[CreateAssetMenu(fileName = "AssetFlashPatchDefinition", menuName = "ShitDesigner/Media/Asset Flash Patch Definition")]
	public sealed class AssetFlashPatchDefinition : ScriptableObject {
		public const int SlotCount = AssetFlashContract.SlotCount;

		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField, Min(.01f)] private float _durationSeconds = .25f;
		[SerializeField] private AssetFlashPatchSlot[] _slots = CreateSlots();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public float DurationSeconds => _durationSeconds;
		public IReadOnlyList<AssetFlashPatchSlot> Slots => _slots ?? Array.Empty<AssetFlashPatchSlot>();

		public bool TryGetImage(int slotNumber, out Texture2D image) {
			image = null;
			if (slotNumber < 1 || slotNumber > SlotCount || _slots == null || slotNumber > _slots.Length) return false;
			var slot = _slots[slotNumber - 1];
			image = slot?.Image;
			return image != null;
		}

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(Id)) return Failure("media.flash_patch.id", "An Asset Flash patch requires an ID.");
			if (string.IsNullOrWhiteSpace(DisplayName)) return Failure("media.flash_patch.name", "An Asset Flash patch requires a display name.");
			if (!IsFinite(_durationSeconds) || _durationSeconds <= 0f) return Failure("media.flash_patch.duration", "An Asset Flash patch requires a positive duration.");
			if (_slots == null || _slots.Length != SlotCount) return Failure("media.flash_patch.slots", "An Asset Flash patch requires exactly eight slots.");
			if (!_slots.Any(slot => slot != null && slot.Image != null)) return Failure("media.flash_patch.image", "An Asset Flash patch requires at least one image.");
			return UnitResult.Success<Diagnostic>();
		}

		private void OnValidate() {
			if (!IsFinite(_durationSeconds)) _durationSeconds = .25f;
			_durationSeconds = Mathf.Max(.01f, _durationSeconds);
			EnsureSlots();
		}

		private void EnsureSlots() {
			if (_slots != null && _slots.Length == SlotCount) {
				for (var index = 0; index < _slots.Length; index++)
					if (_slots[index] == null) _slots[index] = new AssetFlashPatchSlot();
				return;
			}
			var previous = _slots;
			_slots = CreateSlots();
			if (previous != null) Array.Copy(previous, _slots, Math.Min(previous.Length, _slots.Length));
		}

		private static AssetFlashPatchSlot[] CreateSlots() {
			var slots = new AssetFlashPatchSlot[SlotCount];
			for (var index = 0; index < slots.Length; index++) slots[index] = new AssetFlashPatchSlot();
			return slots;
		}

		private static UnitResult<Diagnostic> Failure(string code, string message)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
