using System;
using System.Collections.Generic;
using System.Linq;

namespace ShitDesigner.Presentation {
	public readonly struct PresentationPoint {
		public float X { get; }
		public float Y { get; }
		public PresentationPoint(float x, float y) { X = x; Y = y; }
		public static PresentationPoint operator +(PresentationPoint a, PresentationPoint b) => new PresentationPoint(a.X + b.X, a.Y + b.Y);
		public static PresentationPoint operator -(PresentationPoint a, PresentationPoint b) => new PresentationPoint(a.X - b.X, a.Y - b.Y);
		public static PresentationPoint operator *(PresentationPoint a, float scale) => new PresentationPoint(a.X * scale, a.Y * scale);
	}

	public readonly struct PresentationRect {
		public float X { get; }
		public float Y { get; }
		public float Width { get; }
		public float Height { get; }
		public float Right => X + Width;
		public float Bottom => Y + Height;
		public PresentationRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
		public bool Contains(PresentationPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
		public bool Overlaps(PresentationRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
	}

	public sealed class GraphCoordinateMapper {
		public const float MinZoom = 0.25f;
		public const float MaxZoom = 2f;
		public const float GridSize = 8f;
		public float Zoom { get; private set; }
		public PresentationPoint Pan { get; private set; }

		public GraphCoordinateMapper(float zoom = 1f, PresentationPoint pan = default(PresentationPoint)) {
			Zoom = ClampZoom(zoom);
			Pan = pan;
		}

		public PresentationPoint ScreenToCanvas(PresentationPoint screen) => (screen - Pan) * (1f / Zoom);
		public PresentationPoint CanvasToScreen(PresentationPoint canvas) => canvas * Zoom + Pan;
		public void PanBy(PresentationPoint delta) { Pan = Pan + delta; }
		public void ZoomAt(float zoom, PresentationPoint screenPoint) {
			var before = ScreenToCanvas(screenPoint);
			Zoom = ClampZoom(zoom);
			var after = CanvasToScreen(before);
			Pan = Pan + (screenPoint - after);
		}

		public static float ClampZoom(float zoom) {
			if (float.IsNaN(zoom) || float.IsInfinity(zoom)) return 1f;
			return zoom < MinZoom ? MinZoom : zoom > MaxZoom ? MaxZoom : zoom;
		}

		public static float Snap(float coordinate) => (float)Math.Round(coordinate / GridSize, MidpointRounding.AwayFromZero) * GridSize;
	}

	public sealed class GraphSelectionState {
		private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
		public IReadOnlyCollection<string> Selected => _selected;
		public string Primary { get; private set; }
		public void Replace(IEnumerable<string> ids, string primary = null) {
			_selected.Clear();
			foreach (var id in ids ?? Enumerable.Empty<string>()) if (!string.IsNullOrWhiteSpace(id)) _selected.Add(id);
			Primary = primary != null && _selected.Contains(primary) ? primary : _selected.FirstOrDefault();
		}
		public void Toggle(string id) {
			if (string.IsNullOrWhiteSpace(id)) return;
			if (!_selected.Add(id)) _selected.Remove(id);
			Primary = _selected.Contains(id) ? id : _selected.FirstOrDefault();
		}
		public void Clear() { _selected.Clear(); Primary = null; }
		public void SelectAll(IEnumerable<string> ids) => Replace(ids);
	}

	public sealed class GraphGestureState {
		private readonly Dictionary<string, PresentationPoint> _candidatePositions = new Dictionary<string, PresentationPoint>(StringComparer.Ordinal);
		public bool IsDragging { get; private set; }
		public bool IsMarquee { get; private set; }
		public PresentationRect Marquee { get; private set; }
		public IReadOnlyDictionary<string, PresentationPoint> CandidatePositions => _candidatePositions;
		public void BeginNodeDrag(IEnumerable<GraphNodeReadModel> nodes) {
			IsDragging = true;
			_candidatePositions.Clear();
			foreach (var node in nodes ?? Enumerable.Empty<GraphNodeReadModel>()) _candidatePositions[node.Id] = new PresentationPoint(node.X, node.Y);
		}
		public void MoveBy(PresentationPoint delta, bool snap) {
			if (!IsDragging) return;
			var keys = _candidatePositions.Keys.ToList();
			foreach (var id in keys) {
				var next = _candidatePositions[id] + delta;
				_candidatePositions[id] = snap ? new PresentationPoint(GraphCoordinateMapper.Snap(next.X), GraphCoordinateMapper.Snap(next.Y)) : next;
			}
		}
		public void BeginMarquee(PresentationPoint start) { IsMarquee = true; Marquee = new PresentationRect(start.X, start.Y, 0, 0); }
		public void UpdateMarquee(PresentationPoint current) {
			if (!IsMarquee) return;
			var x = Math.Min(Marquee.X, current.X);
			var y = Math.Min(Marquee.Y, current.Y);
			Marquee = new PresentationRect(x, y, Math.Abs(current.X - Marquee.X), Math.Abs(current.Y - Marquee.Y));
		}
		public IReadOnlyDictionary<string, PresentationPoint> CommitNodeDrag() {
			var result = new Dictionary<string, PresentationPoint>(_candidatePositions, StringComparer.Ordinal);
			IsDragging = false;
			_candidatePositions.Clear();
			return result;
		}
		public void Cancel() { IsDragging = false; IsMarquee = false; _candidatePositions.Clear(); }
	}

	public sealed class NodeSearchResult {
		public string NodeTypeId { get; }
		public string DisplayName { get; }
		public string Category { get; }
		public bool IsFavorite { get; }
		public bool IsRecent { get; }
		public bool IsDisabled { get; }
		public string DisabledReason { get; }
		public int Score { get; }
		public NodeSearchResult(string nodeTypeId, string displayName, string category, int score,
			bool isFavorite = false, bool isRecent = false, bool isDisabled = false, string disabledReason = null) {
			NodeTypeId = nodeTypeId ?? string.Empty;
			DisplayName = displayName ?? nodeTypeId ?? string.Empty;
			Category = category ?? string.Empty;
			Score = score;
			IsFavorite = isFavorite;
			IsRecent = isRecent;
			IsDisabled = isDisabled;
			DisabledReason = disabledReason ?? string.Empty;
		}
	}

	public static class NodeSearch {
		public static IReadOnlyList<NodeSearchResult> Fuzzy(string query, IEnumerable<NodeSearchResult> candidates) {
			query = (query ?? string.Empty).Trim();
			var result = new List<NodeSearchResult>();
			foreach (var candidate in candidates ?? Enumerable.Empty<NodeSearchResult>()) {
				var score = Score(query, candidate.DisplayName, candidate.Category, candidate.NodeTypeId);
				if (query.Length == 0 || score >= 0) result.Add(new NodeSearchResult(candidate.NodeTypeId, candidate.DisplayName, candidate.Category, score,
					candidate.IsFavorite, candidate.IsRecent, candidate.IsDisabled, candidate.DisabledReason));
			}
			return result.OrderByDescending(x => x.Score).ThenByDescending(x => x.IsFavorite).ThenByDescending(x => x.IsRecent).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static int Score(string query, string name, string category, string id) {
			if (query.Length == 0) return 0;
			var lower = query.ToLowerInvariant();
			var n = (name ?? string.Empty).ToLowerInvariant();
			var c = (category ?? string.Empty).ToLowerInvariant();
			var i = (id ?? string.Empty).ToLowerInvariant();
			if (n == lower || i == lower) return 1000;
			if (n.StartsWith(lower, StringComparison.Ordinal)) return 700 - (n.Length - lower.Length);
			if (n.Contains(lower)) return 500 - (n.Length - lower.Length);
			if (c.Contains(lower) || i.Contains(lower)) return 300;
			var cursor = 0;
			foreach (var ch in lower) {
				cursor = n.IndexOf(ch, cursor);
				if (cursor < 0) return -1;
				cursor++;
			}
			return 100 - lower.Length;
		}
	}

	/// <summary>Session-only state for the graph add-node popup.  Keeping
	/// recent/favorite/category ordering here makes the empty query useful and
	/// prevents the UI from inventing a second catalog.</summary>
	public sealed class NodeSearchPopupState {
		private readonly List<NodeSearchResult> _entries = new List<NodeSearchResult>();
		public IReadOnlyList<NodeSearchResult> Entries => _entries;
		public int SelectedIndex { get; private set; }
		public bool IsOpen { get; private set; }
		public PresentationPoint CanvasPosition { get; private set; }
		public void Open(IEnumerable<NodeSearchResult> entries, PresentationPoint position) {
			_entries.Clear();
			_entries.AddRange((entries ?? Enumerable.Empty<NodeSearchResult>()).Where(x => x != null));
			SelectedIndex = 0;
			CanvasPosition = position;
			IsOpen = true;
		}
		public void ReplaceEntries(IEnumerable<NodeSearchResult> entries) {
			var selected = Current?.NodeTypeId;
			_entries.Clear();
			_entries.AddRange((entries ?? Enumerable.Empty<NodeSearchResult>()).Where(x => x != null));
			var index = selected == null ? -1 : _entries.FindIndex(x => string.Equals(x.NodeTypeId, selected, StringComparison.Ordinal));
			SelectedIndex = index >= 0 ? index : 0;
		}
		public void MoveSelection(int delta) {
			if (_entries.Count == 0) return;
			SelectedIndex = (SelectedIndex + delta) % _entries.Count;
			if (SelectedIndex < 0) SelectedIndex += _entries.Count;
		}
		public NodeSearchResult Current => _entries.Count == 0 ? null : _entries[SelectedIndex];
		public void Close() { IsOpen = false; _entries.Clear(); SelectedIndex = 0; }
	}

	public sealed class GraphCommandComposer {
		private readonly Guid _sessionId;
		private readonly long _documentRevision;
		public GraphCommandComposer(Guid sessionId, long documentRevision) { _sessionId = sessionId; _documentRevision = documentRevision; }
		public PresentationCommandRequest AddNode(string typeId, float x, float y, Guid interactionId = default(Guid)) => Request("graph.add_node", typeId, interactionId, new { nodeTypeId = typeId, x, y });
		public PresentationCommandRequest DeleteNodes(IEnumerable<string> nodeIds, Guid interactionId = default(Guid)) => Request("graph.delete_nodes", string.Join(",", nodeIds ?? Enumerable.Empty<string>()), interactionId);
		public PresentationCommandRequest MoveNodes(IReadOnlyDictionary<string, PresentationPoint> positions, Guid interactionId = default(Guid)) => Request("graph.move_nodes", string.Join(";", positions.Select(x => x.Key + "=" + x.Value.X + "," + x.Value.Y)), interactionId);
		public PresentationCommandRequest Connect(string fromNode, string fromPort, string toNode, string toPort, Guid interactionId = default(Guid)) => Request("graph.connect", Guid.NewGuid().ToString("D"), interactionId, new { sourceNodeId = fromNode, sourcePortId = fromPort, destinationNodeId = toNode, destinationPortId = toPort });
		public PresentationCommandRequest Disconnect(string connectionId, Guid interactionId = default(Guid)) => Request("graph.disconnect", connectionId, interactionId);
		public PresentationCommandRequest Replace(string connectionId, string fromNode, string fromPort, string toNode, string toPort, Guid interactionId = default(Guid)) => Request("graph.replace_connection", connectionId, interactionId, new { sourceNodeId = fromNode, sourcePortId = fromPort, destinationNodeId = toNode, destinationPortId = toPort });
		public PresentationCommandRequest Copy(IEnumerable<string> nodeIds, Guid interactionId = default(Guid)) => Request("graph.copy", string.Join(",", nodeIds ?? Enumerable.Empty<string>()), interactionId);
		public PresentationCommandRequest Paste(float x, float y, Guid interactionId = default(Guid)) => Request("graph.paste", string.Empty, interactionId, new { x, y });
		public PresentationCommandRequest Duplicate(IEnumerable<string> nodeIds, Guid interactionId = default(Guid)) => Request("graph.duplicate", string.Join(",", nodeIds ?? Enumerable.Empty<string>()), interactionId);
		public PresentationCommandRequest FocusSelection(Guid interactionId = default(Guid)) => Request("graph.focus_selection", string.Empty, interactionId);
		public PresentationCommandRequest FocusAll(Guid interactionId = default(Guid)) => Request("graph.focus_all", string.Empty, interactionId);
		private PresentationCommandRequest Request(string id, string target, Guid interaction, object payload = null) {
			var values = new List<KeyValuePair<string, string>>();
			if (payload != null) foreach (var property in payload.GetType().GetProperties()) values.Add(new KeyValuePair<string, string>(property.Name, Convert.ToString(property.GetValue(payload), System.Globalization.CultureInfo.InvariantCulture)));
			return new PresentationCommandRequest(_sessionId, Guid.NewGuid(), interaction, _documentRevision, target, id, values);
		}
	}
}
