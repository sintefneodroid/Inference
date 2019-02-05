using UnityEngine;

namespace PathCreation {
  public class MinMax3D {
    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }

    public MinMax3D() {
      this.Min = Vector3.one * float.MaxValue;
      this.Max = Vector3.one * float.MinValue;
    }

    public void AddValue(Vector3 v) {
      this.Min = new Vector3(Mathf.Min(this.Min.x, v.x), Mathf.Min(this.Min.y, v.y), Mathf.Min(this.Min.z, v.z));
      this.Max = new Vector3(Mathf.Max(this.Max.x, v.x), Mathf.Max(this.Max.y, v.y), Mathf.Max(this.Max.z, v.z));
    }
  }
}
