using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Moves a wordmark within the orthographic camera bounds and changes its color at each impact.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceScene : MonoBehaviour {
		[Header("Logo")]
		[SerializeField] private string m_Label = "DVD";
		[Min(1)][SerializeField] private int m_FontSize = 120;
		[Min(0.01f)][SerializeField] private float m_CharacterSize = 0.12f;

		[Header("Motion")]
		[Min(0.01f)][SerializeField] private float m_Speed = 4.5f;
		[SerializeField] private Vector2 m_InitialDirection = new Vector2(1f, 0.63f);
		[SerializeField] private Vector2 m_InitialPosition = Vector2.zero;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color[] m_Colors = {
			new Color(1f, 0.22f, 0.36f, 1f),
			new Color(0.2f, 0.9f, 1f, 1f),
			new Color(1f, 0.78f, 0.16f, 1f),
			new Color(0.5f, 1f, 0.35f, 1f),
			new Color(0.82f, 0.36f, 1f, 1f)
		};

		private Camera m_Camera;
		private TextMesh m_TextMesh;
		private MeshRenderer m_Renderer;
		private Vector2 m_Velocity;
		private int m_ColorIndex;

		private void Awake() {
			m_Camera = Camera.main;
			if (m_Camera == null)
				m_Camera = FindFirstObjectByType<Camera>();
			CreateLogo();
		}

		private void Start() {
			transform.position = new Vector3(m_InitialPosition.x, m_InitialPosition.y, 0f);
			m_Velocity = GetInitialVelocity();
			ApplyColor();
			KeepWithinBounds();
		}

		private void Update() {
			if (m_Camera == null || m_Renderer == null)
				return;

			var position = (Vector2)transform.position + m_Velocity * Time.unscaledDeltaTime;
			var bounds = GetMovementBounds();
			var impacted = ReflectWithinBounds(ref position, ref m_Velocity, bounds);
			transform.position = new Vector3(position.x, position.y, 0f);
			if (impacted)
				AdvanceColor();
		}

		private void OnValidate() {
			m_FontSize = Mathf.Max(1, m_FontSize);
			m_CharacterSize = Mathf.Max(0.01f, m_CharacterSize);
			m_Speed = Mathf.Max(0.01f, m_Speed);
			if (m_InitialDirection.sqrMagnitude < 0.0001f)
				m_InitialDirection = new Vector2(1f, 0.63f);
			if (m_TextMesh != null) {
				m_TextMesh.text = m_Label;
				m_TextMesh.fontSize = m_FontSize;
				m_TextMesh.characterSize = m_CharacterSize;
				ApplyColor();
			}
		}

		private void CreateLogo() {
			m_TextMesh = GetComponent<TextMesh>();
			if (m_TextMesh == null)
				m_TextMesh = gameObject.AddComponent<TextMesh>();
			m_TextMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			m_TextMesh.text = m_Label;
			m_TextMesh.fontSize = m_FontSize;
			m_TextMesh.characterSize = m_CharacterSize;
			m_TextMesh.anchor = TextAnchor.MiddleCenter;
			m_TextMesh.alignment = TextAlignment.Center;
			m_TextMesh.fontStyle = FontStyle.Bold;
			m_Renderer = GetComponent<MeshRenderer>();
			m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
			m_Renderer.receiveShadows = false;
			m_Renderer.allowOcclusionWhenDynamic = false;
			m_Renderer.sortingOrder = 1;
		}

		private Vector2 GetInitialVelocity() {
			return m_InitialDirection.normalized * m_Speed;
		}

		private Bounds2D GetMovementBounds() {
			var cameraHalfHeight = m_Camera.orthographicSize;
			var cameraHalfWidth = cameraHalfHeight * m_Camera.aspect;
			var logoExtents = m_Renderer.bounds.extents;
			return new Bounds2D(
				new Vector2(-cameraHalfWidth + logoExtents.x, -cameraHalfHeight + logoExtents.y),
				new Vector2(cameraHalfWidth - logoExtents.x, cameraHalfHeight - logoExtents.y));
		}

		private void KeepWithinBounds() {
			if (m_Camera == null || m_Renderer == null)
				return;

			var position = (Vector2)transform.position;
			var bounds = GetMovementBounds();
			ReflectWithinBounds(ref position, ref m_Velocity, bounds);
			transform.position = new Vector3(position.x, position.y, 0f);
		}

		private static bool ReflectWithinBounds(ref Vector2 position, ref Vector2 velocity, Bounds2D bounds) {
			var impacted = false;
			if (position.x < bounds.Minimum.x || position.x > bounds.Maximum.x) {
				position.x = Mathf.Clamp(position.x, bounds.Minimum.x, bounds.Maximum.x);
				velocity.x = -velocity.x;
				impacted = true;
			}
			if (position.y < bounds.Minimum.y || position.y > bounds.Maximum.y) {
				position.y = Mathf.Clamp(position.y, bounds.Minimum.y, bounds.Maximum.y);
				velocity.y = -velocity.y;
				impacted = true;
			}
			return impacted;
		}

		private void AdvanceColor() {
			if (m_Colors == null || m_Colors.Length == 0)
				return;

			m_ColorIndex = (m_ColorIndex + 1) % m_Colors.Length;
			ApplyColor();
		}

		private void ApplyColor() {
			if (m_TextMesh == null || m_Colors == null || m_Colors.Length == 0)
				return;

			m_ColorIndex = Mathf.Clamp(m_ColorIndex, 0, m_Colors.Length - 1);
			m_TextMesh.color = m_Colors[m_ColorIndex];
		}

		private readonly struct Bounds2D {
			public readonly Vector2 Minimum;
			public readonly Vector2 Maximum;

			public Bounds2D(Vector2 minimum, Vector2 maximum) {
				Minimum = minimum;
				Maximum = maximum;
			}
		}
	}
}
