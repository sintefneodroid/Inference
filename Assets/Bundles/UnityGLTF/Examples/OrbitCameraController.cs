using UnityEngine;

// Taken from http://wiki.unity3d.com/index.php?title=MouseOrbitImproved
namespace Bundles.UnityGLTF.Examples {
  [AddComponentMenu("Camera-Control/Mouse Orbit with zoom")]
  public class OrbitCameraController : MonoBehaviour {
    /// <summary>
    ///
    /// </summary>
    [SerializeField] Transform target;
    [SerializeField] float distance = 5.0f;
    [SerializeField] float xSpeed = 120.0f;
    [SerializeField] float ySpeed = 120.0f;

    [SerializeField] float yMinLimit = -20f;
    [SerializeField] float yMaxLimit = 80f;

    [SerializeField] float distanceMin = .5f;
    [SerializeField] float distanceMax = 50f;

    Rigidbody cameraRigidBody;

    float x = 0.0f;
    float y = 0.0f;

    // Use this for initialization
    void Start() {
      var angles = this.transform.eulerAngles;
      this.x = angles.y;
      this.y = angles.x;

      this.cameraRigidBody = this.GetComponent<Rigidbody>();

      if (this.cameraRigidBody != null) { // Make the rigid body not change rotation
        this.cameraRigidBody.freezeRotation = true;
      }
    }

    void LateUpdate() {
      if (this.target) {
        this.x += Input.GetAxis("Mouse X") * this.xSpeed * this.distance * 0.02f;
        this.y -= Input.GetAxis("Mouse Y") * this.ySpeed * 0.02f;

        this.y = ClampAngle(this.y, this.yMinLimit, this.yMaxLimit);

        var rotation = Quaternion.Euler(this.y, this.x, 0);

        this.distance = Mathf.Clamp(
            this.distance - Input.GetAxis("Mouse ScrollWheel") * 5,
            this.distanceMin,
            this.distanceMax);

        RaycastHit hit;
        if (Physics.Linecast(this.target.position, this.transform.position, out hit)) {
          this.distance -= hit.distance;
        }

        var negDistance = new Vector3(0.0f, 0.0f, -this.distance);
        var position = rotation * negDistance + this.target.position;

        this.transform.rotation = rotation;
        this.transform.position = position;
      }
    }

    public static float ClampAngle(float angle, float min, float max) {
      if (angle < -360F)
        angle += 360F;
      if (angle > 360F)
        angle -= 360F;
      return Mathf.Clamp(angle, min, max);
    }
  }
}
