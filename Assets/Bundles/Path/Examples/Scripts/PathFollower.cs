using UnityEngine;

namespace PathCreation.Examples {
  // Moves along a path at constant speed.
  // Depending on the end of path instruction, will either loop, reverse, or stop at the end of the path.
  public class PathFollower : MonoBehaviour {
    public PathCreator pathCreator;
    public EndOfPathInstruction endOfPathInstruction;
    public float speed = 5;
    float distanceTravelled;

    void Update() {
      if (this.pathCreator != null) {
        this.distanceTravelled += this.speed * Time.deltaTime;
        this.transform.position =
            this.pathCreator.path.GetPointAtDistance(this.distanceTravelled, this.endOfPathInstruction);
        this.transform.rotation = this.pathCreator.path.GetRotationAtDistance(
            this.distanceTravelled,
            this.endOfPathInstruction);
      }
    }
  }
}
