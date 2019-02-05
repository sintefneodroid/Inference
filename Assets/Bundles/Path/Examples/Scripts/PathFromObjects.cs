using Bundles.Path.Core.Scripts.Objects;
using UnityEngine;

namespace Bundles.Path.Examples.Scripts {
  // Creates a path from an array of transforms and moves along it

  [RequireComponent(typeof(TrailRenderer))]
  public class PathFromObjects : MonoBehaviour {
    public Transform[] waypoints;
    public float speed = 8;

    float dstTravelled;
    VertexPath path;

    void Start() {
      if (this.waypoints.Length > 0) {
        // Create a new bezier path from the waypoints.
        // The 'true' argument specifies that the path should be a closed loop
        var bezierPath = new BezierPath(this.waypoints, true, PathSpace.Xyz);
        // Create a vertex path from the bezier path
        this.path = new VertexPath(bezierPath);
      } else {
        Debug.Log("No waypoints assigned");
      }
    }

    void Update() {
      if (this.path != null) {
        this.dstTravelled += this.speed * Time.deltaTime;
        this.transform.position = this.path.GetPointAtDistance(this.dstTravelled);
      }
    }
  }
}
