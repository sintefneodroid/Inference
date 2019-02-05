using UnityEngine;

namespace Bundles.Path.Core.Scripts.Objects {
  public class PathCreator : MonoBehaviour {
    /// This class stores data for the path editor, and provides accessors to get the current vertex and bezier path.
    /// Attach to a GameObject to create a new path editor.
    public event System.Action PathUpdated;

    [SerializeField, HideInInspector] PathCreatorData editorData;
    [SerializeField, HideInInspector] bool initialized;

    // Vertex path created from the current bezier path
    public VertexPath Path {
      get {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        return this.editorData.VertexPath;
      }
    }

    // The bezier path created in the editor
    public BezierPath BezierPath {
      get {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        return this.editorData.CBezierPath;
      }
      set {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        this.editorData.CBezierPath = value;
      }
    }

    #region Internal methods

    /// Used by the path editor to initialise some data
    public void InitializeEditorData(bool in2DMode) {
      if (this.editorData == null) {
        this.editorData = new PathCreatorData();
      }

      this.editorData.BezierOrVertexPathModified -= this.OnPathUpdated;
      this.editorData.BezierOrVertexPathModified += this.OnPathUpdated;

      this.editorData.Initialize(this.transform.position, in2DMode);
      this.initialized = true;
    }

    public PathCreatorData EditorData { get { return this.editorData; } }

    void OnPathUpdated() {
      if (this.PathUpdated != null) {
        this.PathUpdated();
      }
    }

    #endregion
  }
}
