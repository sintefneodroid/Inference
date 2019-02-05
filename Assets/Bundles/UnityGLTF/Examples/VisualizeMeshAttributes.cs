using UnityEngine;

namespace Bundles.UnityGLTF.Examples {
  public class VisualizeMeshAttributes : MonoBehaviour {
    [SerializeField] private MeshFilter Mesh;
    [SerializeField] private float NormalScale = 0.1f;
    [SerializeField] private float TangentScale = 0.1f;
    [SerializeField] private bool VisualizeTangents = false;
    [SerializeField] private bool VisualizeNormals = false;

    private Vector3[] vertices;
    private Vector3[] normals;
    private Vector4[] tangents;

    void OnEnable() {
      if (this.Mesh != null && this.Mesh.mesh != null) {
        this.vertices = this.Mesh.mesh.vertices;
        this.normals = this.Mesh.mesh.normals;
        this.tangents = this.Mesh.mesh.tangents;
      }
    }

    // Update is called once per frame
    void Update() {
      if (this.vertices != null) {
        var numVerts = this.vertices.Length;
        for (var vertexIndex = 0; vertexIndex < numVerts; vertexIndex++) {
          var vertexTransformed = this.transform.TransformPoint(this.vertices[vertexIndex]);

          if (this.VisualizeNormals && this.normals != null) {
            var normalTransformed = this.transform.InverseTransformVector(this.normals[vertexIndex]);
            Debug.DrawLine(
                vertexTransformed,
                vertexTransformed + normalTransformed * this.NormalScale * 0.5f,
                Color.green);
            Debug.DrawLine(
                vertexTransformed + normalTransformed * this.NormalScale * 0.5f,
                vertexTransformed + normalTransformed * this.NormalScale * 1.0f,
                Color.blue);
          }

          if (this.VisualizeTangents && this.tangents != null) {
            var tangentTransformed = this.transform.TransformVector(
                this.tangents[vertexIndex].w
                * new Vector3(
                    this.tangents[vertexIndex].x,
                    this.tangents[vertexIndex].y,
                    this.tangents[vertexIndex].z));
            Debug.DrawLine(
                vertexTransformed,
                vertexTransformed + tangentTransformed * this.TangentScale * 0.5f,
                Color.black);
            Debug.DrawLine(
                vertexTransformed + tangentTransformed * this.TangentScale * 0.5f,
                vertexTransformed + tangentTransformed * this.TangentScale * 1.0f,
                Color.white);
          }
        }
      }
    }
  }
}
