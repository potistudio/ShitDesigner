using System;
using System.Collections.Generic;
using System.Linq;

namespace ShitDesigner.Presentation {
	/// <summary>Presentation-side port for the transient Display identify
	/// action. The Bootstrap adapter owns the Unity Display API; the view only
	/// asks for one-based display numbers and renders the returned state.</summary>
	public interface IDisplayIdentifyPort {
		int DisplayCount { get; }
		bool TryIdentify(int displayNumber, out string error);
	}

	/// <summary>Rendering/Application bridge for opaque output leases. Presentation
	/// never owns a RenderTexture directly; the lease releases it back to the
	/// producing module when the generation is replaced or the adapter closes.</summary>
	public interface IOutputSurfacePort {
		bool TryAcquire(string surfaceId, out OutputSurfaceLease lease);
	}

	/// <summary>Non-borrowing surface state used by presentation adapters to
	/// decide whether their existing lease is still current. Implementations
	/// must not increment pool/bridge borrower counts here.</summary>
	public interface IOutputSurfaceDescriptorPort : IOutputSurfacePort {
		bool TryDescribe(string surfaceId, out OutputSurfaceDescriptor descriptor);
	}

	/// <summary>Allocation-free snapshot returned by descriptor-aware surface
	/// ports.  The default value is explicitly unbound.</summary>
	public readonly struct OutputSurfaceDescriptor {
		public string SurfaceId { get; }
		public ulong Generation { get; }
		public int Width { get; }
		public int Height { get; }
		public ulong FrameNumber { get; }
		public object Texture { get; }
		public bool IsBound { get; }
		public OutputSurfaceDescriptor(string surfaceId, ulong generation, int width, int height, ulong frameNumber, object texture, bool isBound) {
			SurfaceId = surfaceId ?? string.Empty;
			Generation = generation;
			Width = width;
			Height = height;
			FrameNumber = frameNumber;
			Texture = texture;
			IsBound = isBound;
		}
	}

	public interface IProgramPresenterPort {
		void SetVisible(bool visible);
	}

	/// <summary>Transient control surface for the Program's external output.
	/// Presentation owns the interaction contract; the platform adapter owns
	/// Unity Display activation and presentation.</summary>
	public interface IProgramOutputControlPort {
		int DisplayNumber { get; }
		int ConnectedDisplayCount { get; }
		bool IsOutputActive { get; }
		string LastError { get; }
		event Action<bool> OutputActiveChanged;
		bool CanActivate(int displayNumber, out string error);
		bool SelectDisplay(int displayNumber);
		bool SetOutputActive(bool active);
	}

	public sealed class OutputSurfaceLease : IDisposable {
		private readonly Action _release;
		private bool _released;
		public string SurfaceId { get; }
		public ulong Generation { get; }
		public int Width { get; }
		public int Height { get; }
		public ulong FrameNumber { get; }
		public object Texture { get; }
		public OutputSurfaceLease(string surfaceId, ulong generation, int width, int height, ulong frameNumber, object texture, Action release = null) {
			SurfaceId = surfaceId ?? string.Empty;
			Generation = generation;
			Width = width;
			Height = height;
			FrameNumber = frameNumber;
			Texture = texture;
			_release = release;
		}
		public void Dispose() {
			if (_released) return;
			_released = true;
			_release?.Invoke();
		}
	}

	public interface IPreviewDemandPort {
		void SetDemand(string previewNodeId, bool demanded);
	}

	public sealed class SurfaceBinding {
		public string SurfaceId { get; private set; }
		public ulong Generation { get; private set; }
		public object Texture { get; private set; }
		public bool IsBound => !string.IsNullOrEmpty(SurfaceId);
		public bool Bind(OutputSurfaceReadModel surface) {
			if (surface == null || string.IsNullOrEmpty(surface.SurfaceId)) return false;
			if (IsBound && string.Equals(SurfaceId, surface.SurfaceId, StringComparison.Ordinal) && Generation == surface.Generation) return false;
			SurfaceId = surface.SurfaceId;
			Generation = surface.Generation;
			Texture = surface.Texture;
			return true;
		}
		public SurfaceRelease Unbind() {
			var release = new SurfaceRelease(SurfaceId, Generation);
			SurfaceId = null;
			Generation = 0;
			Texture = null;
			return release;
		}
	}

	public readonly struct SurfaceRelease {
		public string SurfaceId { get; }
		public ulong Generation { get; }
		public SurfaceRelease(string surfaceId, ulong generation) { SurfaceId = surfaceId ?? string.Empty; Generation = generation; }
	}

	public sealed class ProgramMonitorController {
		private readonly SurfaceBinding _binding = new SurfaceBinding();
		public bool IsOpen { get; private set; }
		public SurfaceBinding Binding => _binding;
		public void Open() { IsOpen = true; }
		public SurfaceRelease Close() {
			IsOpen = false;
			// Closing the monitor unbinds only the view. Program demand and
			// external display remain owned by Application/Rendering.
			return _binding.Unbind();
		}
		public bool Apply(OutputSurfaceReadModel program) => _binding.Bind(program);
	}

	public sealed class PreviewHostController {
		public const int MaxHosts = 8;
		private readonly Dictionary<string, PreviewTabState> _tabs = new Dictionary<string, PreviewTabState>(StringComparer.Ordinal);
		private readonly IPreviewDemandPort _demands;
		public bool IsVisible { get; private set; }
		public IReadOnlyCollection<PreviewTabState> Tabs => _tabs.Values.ToList();
		public string LastRejectionReason { get; private set; }
		public PreviewHostController(IPreviewDemandPort demands = null) { _demands = demands; }
		public void SetVisible(bool visible) {
			IsVisible = visible;
			foreach (var tab in _tabs.Values) _demands?.SetDemand(tab.NodeId, visible && tab.IsVisible);
		}
		public bool Open(PreviewReadModel preview) {
			if (preview == null || string.IsNullOrWhiteSpace(preview.TabId)) { LastRejectionReason = "Preview tab identity is required."; return false; }
			if (_tabs.ContainsKey(preview.TabId)) { _tabs[preview.TabId].IsVisible = true; _demands?.SetDemand(preview.NodeId, IsVisible); return true; }
			if (_tabs.Count >= MaxHosts) { LastRejectionReason = "Preview Viewer Host is limited to eight visible previews."; return false; }
			_tabs.Add(preview.TabId, new PreviewTabState(preview.NodeId, preview.TabId, preview.Fit, preview.Background, true));
			_demands?.SetDemand(preview.NodeId, IsVisible);
			return true;
		}
		public bool Close(string tabId) {
			if (!_tabs.TryGetValue(tabId ?? string.Empty, out var tab)) return false;
			_demands?.SetDemand(tab.NodeId, false);
			return _tabs.Remove(tab.TabId);
		}
		public bool ApplySettings(string tabId, PresentationOutputFit fit, PresentationOutputBackground background) {
			if (!_tabs.TryGetValue(tabId ?? string.Empty, out var tab)) return false;
			tab.Fit = fit;
			tab.Background = background;
			return true;
		}
	}

	public sealed class PreviewTabState {
		public string NodeId { get; }
		public string TabId { get; }
		public PresentationOutputFit Fit { get; set; }
		public PresentationOutputBackground Background { get; set; }
		public bool IsVisible { get; set; }
		public PreviewTabState(string nodeId, string tabId, PresentationOutputFit fit, PresentationOutputBackground background, bool isVisible) { NodeId = nodeId ?? string.Empty; TabId = tabId ?? string.Empty; Fit = fit; Background = background; IsVisible = isVisible; }
	}

	public static class PreviewOverlayPolicy {
		public static bool CanOverlayProgram => false;
		public static bool CanOverlayPreview => true;
	}
}
