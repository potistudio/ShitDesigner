using UnityEngine;

public class AvatarShowcase : MonoBehaviour {
	[SerializeField] private Vector3 m_RotationSpeed = Vector3.up * 10f;

	private void Update() {
		transform.Rotate(m_RotationSpeed * Time.deltaTime, Space.World);
	}
}
