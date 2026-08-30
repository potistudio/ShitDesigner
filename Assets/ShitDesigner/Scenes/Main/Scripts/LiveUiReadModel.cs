using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Main {
	public enum LiveCatalogRole {
		Main,
		Overlay,
		Effect
	}

	public enum LivePatchRole {
		Main,
		Overlay
	}

	public readonly struct LivePatchReadModel {
		public string Id { get; }
		public string Name { get; }
		public LivePatchRole Role { get; }

		public LivePatchReadModel(string id, string name, LivePatchRole role) {
			Id = id;
			Name = name;
			Role = role;
		}
	}

	public readonly struct LiveEffectNodeReadModel {
		public string TypeId { get; }
		public string Name { get; }
		public string Category { get; }

		public LiveEffectNodeReadModel(string typeId, string name, string category) {
			TypeId = typeId;
			Name = name;
			Category = category;
		}
	}

	public sealed class LiveUiReadModel {
		public ulong FrameNumber { get; }
		public IReadOnlyList<LivePatchReadModel> Patches { get; }
		public IReadOnlyList<LiveEffectNodeReadModel> EffectNodes { get; }
		public LiveCatalogRole SelectedCatalogRole { get; }
		public string SelectedCatalogItemId { get; }
		public string LoadedPatchId { get; }
		public IReadOnlyList<RenderTexture> OverlayLanePreviews { get; }
		public LiveParameterDefinition Bpm { get; }
		public IReadOnlyList<LiveParameterDefinition> Parameters { get; }
		public IReadOnlyList<LiveSequencerReadModel> Sequencers { get; }
		public IReadOnlyList<LiveProgramFrame> ProgramFrames { get; }
		public RenderTexture ProgramTexture { get; }
		public ulong ProgramFrameNumber { get; }
		public int ConnectedDisplayCount { get; }
		public IReadOnlyList<int> ExternalDisplayNumbers { get; }
		public bool IsDisplayOutputActive { get; }
		public string DisplayError { get; }
		public LiveCapabilitySnapshot Capabilities { get; }
		public string Diagnostic { get; }
		public IReadOnlyList<LiveParameterApplicationResult> RequestResults { get; }

		public LiveUiReadModel(ulong frameNumber, IReadOnlyList<LivePatchReadModel> patches, IReadOnlyList<LiveEffectNodeReadModel> effectNodes,
			LiveCatalogRole selectedCatalogRole, string selectedCatalogItemId,
			string loadedPatchId, IReadOnlyList<RenderTexture> overlayLanePreviews,
			LiveParameterDefinition bpm, IReadOnlyList<LiveParameterDefinition> parameters, IReadOnlyList<LiveSequencerReadModel> sequencers, LiveProgramFrames programFrames, LiveExternalDisplayOutput output,
			LiveCapabilitySnapshot capabilities, string diagnostic, IReadOnlyList<LiveParameterApplicationResult> requestResults) {
			FrameNumber = frameNumber;
			Patches = patches ?? Array.Empty<LivePatchReadModel>();
			EffectNodes = effectNodes ?? Array.Empty<LiveEffectNodeReadModel>();
			SelectedCatalogRole = selectedCatalogRole;
			SelectedCatalogItemId = selectedCatalogItemId ?? string.Empty;
			LoadedPatchId = loadedPatchId ?? string.Empty;
			OverlayLanePreviews = overlayLanePreviews ?? Array.Empty<RenderTexture>();
			Bpm = bpm;
			Parameters = parameters ?? Array.Empty<LiveParameterDefinition>();
			Sequencers = sequencers ?? Array.Empty<LiveSequencerReadModel>();
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
