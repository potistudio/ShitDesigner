using ShitDesigner.Core;

namespace ShitDesigner.Rendering {
	/// <summary>Publishes the application tempo to the automatic shader-uniform boundary.</summary>
	public static class ShaderBeatClock {
		private static BeatClockFrame m_Current;

		public static BeatClockFrame Current => m_Current;

		public static void Publish(BeatClockFrame frame) => m_Current = frame;
	}
}
