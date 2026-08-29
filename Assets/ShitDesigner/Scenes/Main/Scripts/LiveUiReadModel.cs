using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LivePatchReadModel {
		public string Id { get; }
		public string Name { get; }

		public LivePatchReadModel(string id, string name) {
			Id = id;
			Name = name;
		}
	}

	public sealed class LiveUiReadModel {
		public ulong FrameNumber { get; }
		public IReadOnlyList<LivePatchReadModel> Patches { get; }
		public IReadOnlyList<LivePatchSlotReadModel> PatchSlots { get; }
		public int SelectedPatchSlotIndex { get; }
		public string SelectedCatalogPatchId { get; }
		public string LoadedPatchId { get; }
		public string PreloadedPatchId { get; }
		public LiveParameterDefinition Bpm { get; }
		public IReadOnlyList<LiveProgramFrame> ProgramFrames { get; }
		public IReadOnlyList<LiveParameterDefinition> Parameters { get; }
		public RenderTexture ProgramTexture { get; }
		public ulong ProgramFrameNumber { get; }
		public int ConnectedDisplayCount { get; }
		public IReadOnlyList<int> ExternalDisplayNumbers { get; }
		public bool IsDisplayOutputActive { get; }
		public string DisplayError { get; }
		public LiveCapabilitySnapshot Capabilities { get; }
		public string Diagnostic { get; }
		public IReadOnlyList<LiveParameterApplicationResult> RequestResults { get; }

		public LiveUiReadModel(ulong frameNumber, IReadOnlyList<LivePatchReadModel> patches, IReadOnlyList<LivePatchSlotReadModel> patchSlots, int selectedPatchSlotIndex, string selectedCatalogPatchId,
			string loadedPatchId, string preloadedPatchId,
			LiveParameterDefinition bpm, IReadOnlyList<LiveParameterDefinition> parameters, LiveProgramFrames programFrames, LiveExternalDisplayOutput output,
			LiveCapabilitySnapshot capabilities, string diagnostic, IReadOnlyList<LiveParameterApplicationResult> requestResults) {
			FrameNumber = frameNumber;
			Patches = patches ?? Array.Empty<LivePatchReadModel>();
			PatchSlots = patchSlots ?? Array.Empty<LivePatchSlotReadModel>();
			SelectedPatchSlotIndex = selectedPatchSlotIndex;
			SelectedCatalogPatchId = selectedCatalogPatchId ?? string.Empty;
			LoadedPatchId = loadedPatchId ?? string.Empty;
			PreloadedPatchId = preloadedPatchId ?? string.Empty;
			Bpm = bpm;
			Parameters = parameters ?? Array.Empty<LiveParameterDefinition>();
			ProgramFrames = programFrames.Frames;
			ProgramTexture = programFrames.Primary.Texture;
			ProgramFrameNumber = programFrames.Primary.FrameNumber;
			ConnectedDisplayCount = output?.ConnectedDisplayCount ?? 0;
			ExternalDisplayNumbers = output?.ConnectedExternalDisplayNumbers ?? Array.Empty<int>();
			IsDisplayOutputActive = output != null && output.IsOutputActive;
			DisplayError = output?.LastError ?? string.Empty;
			Capabilities = capabilities;
			Diagnostic = diagnostic ?? string.Empty;
			RequestResults = requestResults ?? Array.Empty<LiveParameterApplicationResult>();
		}
	}
}
