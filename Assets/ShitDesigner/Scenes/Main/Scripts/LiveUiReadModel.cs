using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LiveSceneReadModel {
		public string Id { get; }
		public string Name { get; }

		public LiveSceneReadModel(string id, string name) {
			Id = id;
			Name = name;
		}
	}

	public sealed class LiveUiReadModel {
		public ulong FrameNumber { get; }
		public IReadOnlyList<LiveSceneReadModel> Scenes { get; }
		public string SelectedSceneId { get; }
		public IReadOnlyList<LiveParameterDefinition> Parameters { get; }
		public RenderTexture ProgramTexture { get; }
		public ulong ProgramFrameNumber { get; }
		public int ConnectedDisplayCount { get; }
		public int SelectedDisplayNumber { get; }
		public bool IsDisplayOutputActive { get; }
		public string DisplayError { get; }
		public LiveCapabilitySnapshot Capabilities { get; }
		public string Diagnostic { get; }
		public IReadOnlyList<LiveParameterApplicationResult> RequestResults { get; }

		public LiveUiReadModel(ulong frameNumber, IReadOnlyList<LiveSceneReadModel> scenes, string selectedSceneId,
			IReadOnlyList<LiveParameterDefinition> parameters, LiveProgramFrame programFrame, LiveExternalDisplayOutput output,
			LiveCapabilitySnapshot capabilities, string diagnostic, IReadOnlyList<LiveParameterApplicationResult> requestResults) {
			FrameNumber = frameNumber;
			Scenes = scenes ?? Array.Empty<LiveSceneReadModel>();
			SelectedSceneId = selectedSceneId ?? string.Empty;
			Parameters = parameters ?? Array.Empty<LiveParameterDefinition>();
			ProgramTexture = programFrame.Texture;
			ProgramFrameNumber = programFrame.FrameNumber;
			ConnectedDisplayCount = output?.ConnectedDisplayCount ?? 0;
			SelectedDisplayNumber = output?.DisplayNumber ?? 0;
			IsDisplayOutputActive = output != null && output.IsOutputActive;
			DisplayError = output?.LastError ?? string.Empty;
			Capabilities = capabilities;
			Diagnostic = diagnostic ?? string.Empty;
			RequestResults = requestResults ?? Array.Empty<LiveParameterApplicationResult>();
		}
	}
}
