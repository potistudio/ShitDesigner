using System;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// Monotonic identifier for an output lease. It is diagnostic data only and
	/// does not grant access to the underlying Unity object.
	/// </summary>
	public readonly struct OutputLeaseId : IEquatable<OutputLeaseId>, IComparable<OutputLeaseId> {
		public ulong Value { get; }
		public bool IsEmpty => Value == 0;

		public OutputLeaseId(ulong value) {
			if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
			Value = value;
		}

		public bool Equals(OutputLeaseId other) => Value == other.Value;
		public override bool Equals(object obj) => obj is OutputLeaseId other && Equals(other);
		public override int GetHashCode() => Value.GetHashCode();
		public int CompareTo(OutputLeaseId other) => Value.CompareTo(other.Value);
		public override string ToString() => Value.ToString();
		public static bool operator ==(OutputLeaseId left, OutputLeaseId right) => left.Equals(right);
		public static bool operator !=(OutputLeaseId left, OutputLeaseId right) => !left.Equals(right);
	}

	public enum ResourceOwnerKind {
		RuntimeNode,
		ProgramPresenter,
		DefaultImageProvider,
		Feedback,
		Other
	}

	public enum LeaseRole {
		Output,
		Depth,
		FeedbackPrevious,
		FeedbackNext,
		ProgramHold,
		DefaultImage
	}

	/// <summary>Stable owner identity used for every acquire and release check.</summary>
	public readonly struct ResourceOwnerKey : IEquatable<ResourceOwnerKey> {
		public string SessionId { get; }
		public ResourceOwnerKind OwnerKind { get; }
		public string OwnerId { get; }
		public ulong GenerationId { get; }
		public string SlotId { get; }
		public LeaseRole Role { get; }
		public bool IsValid => !string.IsNullOrWhiteSpace(SessionId) && !string.IsNullOrWhiteSpace(OwnerId) && !string.IsNullOrWhiteSpace(SlotId) && GenerationId != 0;

		public ResourceOwnerKey(string sessionId, ResourceOwnerKind ownerKind, string ownerId, ulong generationId, string slotId, LeaseRole role) {
			if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID is required.", nameof(sessionId));
			if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner ID is required.", nameof(ownerId));
			if (string.IsNullOrWhiteSpace(slotId)) throw new ArgumentException("Slot ID is required.", nameof(slotId));
			if (generationId == 0) throw new ArgumentOutOfRangeException(nameof(generationId));
			SessionId = sessionId.Trim();
			OwnerKind = ownerKind;
			OwnerId = ownerId.Trim();
			GenerationId = generationId;
			SlotId = slotId.Trim();
			Role = role;
		}

		public bool Equals(ResourceOwnerKey other) =>
			OwnerKind == other.OwnerKind && GenerationId == other.GenerationId && Role == other.Role &&
			string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
			string.Equals(OwnerId, other.OwnerId, StringComparison.Ordinal) &&
			string.Equals(SlotId, other.SlotId, StringComparison.Ordinal);

		public override bool Equals(object obj) => obj is ResourceOwnerKey other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(SessionId, OwnerKind, OwnerId, GenerationId, SlotId, Role);
		public static bool operator ==(ResourceOwnerKey left, ResourceOwnerKey right) => left.Equals(right);
		public static bool operator !=(ResourceOwnerKey left, ResourceOwnerKey right) => !left.Equals(right);
		public override string ToString() => $"{SessionId}/{OwnerKind}/{OwnerId}/{GenerationId}/{SlotId}/{Role}";
	}

	/// <summary>
	/// Complete immutable RenderTexture reuse key. No descriptor field is
	/// inferred or omitted during equality comparison.
	/// </summary>
	public readonly struct TextureDescriptor : IEquatable<TextureDescriptor> {
		public int Width { get; }
		public int Height { get; }
		public GraphicsFormat GraphicsFormat { get; }
		public GraphicsFormat DepthStencilFormat { get; }
		public int MsaaSamples { get; }
		public bool MipMap { get; }
		public bool RandomWrite { get; }
		public TextureDimension Dimension { get; }
		public int VolumeDepth { get; }
		public bool SRgb { get; }

		public TextureDescriptor(int width, int height, GraphicsFormat graphicsFormat,
			GraphicsFormat depthStencilFormat = GraphicsFormat.None, int msaaSamples = 1,
			bool mipMap = false, bool randomWrite = false, TextureDimension dimension = TextureDimension.Tex2D,
			int volumeDepth = 1, bool sRgb = false) {
			if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
			if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
			if (graphicsFormat == GraphicsFormat.None) throw new ArgumentException("A color GraphicsFormat is required.", nameof(graphicsFormat));
			if (msaaSamples < 1) throw new ArgumentOutOfRangeException(nameof(msaaSamples));
			if (volumeDepth < 1) throw new ArgumentOutOfRangeException(nameof(volumeDepth));
			if (dimension != TextureDimension.Tex2D && dimension != TextureDimension.Tex2DArray && dimension != TextureDimension.Tex3D)
				throw new ArgumentException("Only 2D, 2D array and 3D textures are supported.", nameof(dimension));
			Width = width;
			Height = height;
			GraphicsFormat = graphicsFormat;
			DepthStencilFormat = depthStencilFormat;
			MsaaSamples = msaaSamples;
			MipMap = mipMap;
			RandomWrite = randomWrite;
			Dimension = dimension;
			VolumeDepth = volumeDepth;
			SRgb = sRgb;
		}

		public RenderTextureDescriptor ToUnityDescriptor() {
			var descriptor = new RenderTextureDescriptor(Width, Height, GraphicsFormat, DepthStencilFormat) {
				msaaSamples = MsaaSamples,
				useMipMap = MipMap,
				autoGenerateMips = MipMap,
				enableRandomWrite = RandomWrite,
				dimension = Dimension,
				volumeDepth = VolumeDepth,
				sRGB = SRgb
			};
			return descriptor;
		}

		public bool Equals(TextureDescriptor other) =>
			Width == other.Width && Height == other.Height && GraphicsFormat == other.GraphicsFormat &&
			DepthStencilFormat == other.DepthStencilFormat && MsaaSamples == other.MsaaSamples &&
			MipMap == other.MipMap && RandomWrite == other.RandomWrite && Dimension == other.Dimension &&
			VolumeDepth == other.VolumeDepth && SRgb == other.SRgb;

		public override bool Equals(object obj) => obj is TextureDescriptor other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(
			HashCode.Combine(Width, Height, GraphicsFormat, DepthStencilFormat, MsaaSamples, MipMap, RandomWrite, Dimension),
			HashCode.Combine(VolumeDepth, SRgb));
		public static bool operator ==(TextureDescriptor left, TextureDescriptor right) => left.Equals(right);
		public static bool operator !=(TextureDescriptor left, TextureDescriptor right) => !left.Equals(right);
		public override string ToString() => $"{Width}x{Height} {GraphicsFormat} depth={DepthStencilFormat} msaa={MsaaSamples} mip={MipMap} rw={RandomWrite} dim={Dimension} volume={VolumeDepth} srgb={SRgb}";
	}

	/// <summary>Read-only frame value. It intentionally has no release or pool API.</summary>
	public readonly struct ImageFrame : IEquatable<ImageFrame>, IRuntimeImageFrameSurface {
		public RenderTexture Texture { get; }
		public Vector2Int Size { get; }
		public GraphicsFormat ColorFormat { get; }
		public ulong FrameNumber { get; }
		public OutputLeaseId LeaseId { get; }

		// Runtime consumes only this small, Unity-free metadata seam. Keep the
		// public Rendering API strongly typed while exposing the neutral view
		// explicitly to avoid a GraphicsFormat dependency in Runtime.
		int IRuntimeImageFrame.Width => Size.x;
		int IRuntimeImageFrame.Height => Size.y;
		string IRuntimeImageFrame.ColorFormat => ColorFormat.ToString();
		ulong IRuntimeImageFrame.FrameNumber => FrameNumber;
		ulong IRuntimeImageFrame.LeaseId => LeaseId.Value;
		object IRuntimeImageFrameSurface.NativeSurface => Texture;

		public ImageFrame(RenderTexture texture, Vector2Int size, GraphicsFormat colorFormat, ulong frameNumber, OutputLeaseId leaseId) {
			if (texture == null) throw new ArgumentNullException(nameof(texture));
			if (!texture.IsCreated()) throw new ArgumentException("Texture must be created.", nameof(texture));
			if (size.x < 1 || size.y < 1) throw new ArgumentOutOfRangeException(nameof(size));
			if (texture.width != size.x || texture.height != size.y) throw new ArgumentException("Frame size must match the texture.", nameof(size));
			if (colorFormat == GraphicsFormat.None || texture.graphicsFormat != colorFormat) throw new ArgumentException("Frame color format must match the texture.", nameof(colorFormat));
			if (frameNumber == 0) throw new ArgumentOutOfRangeException(nameof(frameNumber));
			if (leaseId.IsEmpty) throw new ArgumentException("A frame requires a valid output lease ID.", nameof(leaseId));
			Texture = texture;
			Size = size;
			ColorFormat = colorFormat;
			FrameNumber = frameNumber;
			LeaseId = leaseId;
		}

		public bool Equals(ImageFrame other) => ReferenceEquals(Texture, other.Texture) && Size == other.Size && ColorFormat == other.ColorFormat && FrameNumber == other.FrameNumber && LeaseId == other.LeaseId;
		public override bool Equals(object obj) => obj is ImageFrame other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Texture, Size, ColorFormat, FrameNumber, LeaseId);
		public static bool operator ==(ImageFrame left, ImageFrame right) => left.Equals(right);
		public static bool operator !=(ImageFrame left, ImageFrame right) => !left.Equals(right);
	}

	/// <summary>
	/// Borrowed view handed to nodes and Presentation. No ownership operation is
	/// exposed on this type.
	/// </summary>
	public readonly struct BorrowedOutputSurface {
		public ImageFrame Frame { get; }
		public RenderTexture Texture => Frame.Texture;
		public Vector2Int Size => Frame.Size;
		public GraphicsFormat ColorFormat => Frame.ColorFormat;
		public OutputLeaseId LeaseId => Frame.LeaseId;

		internal BorrowedOutputSurface(ImageFrame frame) { Frame = frame; }
	}

	internal static class RenderingDiagnostics {
		public static Diagnostic Error(string code, string message) =>
			new Diagnostic(new DiagnosticCode(code), Severity.Error, message);
	}
}
