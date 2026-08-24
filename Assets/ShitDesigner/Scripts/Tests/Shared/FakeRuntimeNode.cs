using System;

namespace ShitDesigner.Tests.Shared {
	public enum FakeRuntimeNodeState {
		Preparing,
		Ready,
		Faulted,
		Disposed
	}

	/// <summary>
	/// Minimal stateful node double. It deliberately has no dependency on production node contracts.
	/// </summary>
	public class FakeRuntimeNode : IDisposable {
		public FakeRuntimeNode(string nodeId) {
			if (string.IsNullOrWhiteSpace(nodeId)) {
				throw new ArgumentException("A node id is required.", nameof(nodeId));
			}

			NodeId = nodeId;
			State = FakeRuntimeNodeState.Preparing;
		}

		public string NodeId { get; }
		public FakeRuntimeNodeState State { get; private set; }
		public int EvaluateCount { get; private set; }
		public bool ThrowOnEvaluate { get; set; }

		public void MarkReady() {
			EnsureNotDisposed();
			State = FakeRuntimeNodeState.Ready;
		}

		public virtual void Evaluate() {
			EnsureNotDisposed();
			EvaluateCount++;
			if (ThrowOnEvaluate) {
				State = FakeRuntimeNodeState.Faulted;
				throw new InvalidOperationException($"Injected failure for {NodeId}.");
			}

			State = FakeRuntimeNodeState.Ready;
		}

		public void Dispose() {
			State = FakeRuntimeNodeState.Disposed;
		}

		private void EnsureNotDisposed() {
			if (State == FakeRuntimeNodeState.Disposed) {
				throw new ObjectDisposedException(NodeId);
			}
		}
	}
}
