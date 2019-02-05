using UnityEngine;
using System.Collections.Generic;

namespace PathCreation {
  public class PathCreator : MonoBehaviour {
    /// This class stores data for the path editor, and provides accessors to get the current vertex and bezier path.
    /// Attach to a GameObject to create a new path editor.
    public event System.Action pathUpdated;

    [SerializeField, HideInInspector] PathCreatorData editorData;
    [SerializeField, HideInInspector] bool initialized;

    // Vertex path created from the current bezier path
    public VertexPath path {
      get {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        return this.editorData.vertexPath;
      }
    }

    // The bezier path created in the editor
    public BezierPath bezierPath {
      get {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        return this.editorData.bezierPath;
      }
      set {
        if (!this.initialized) {
          this.InitializeEditorData(false);
        }

        this.editorData.bezierPath = value;
      }
    }

    #region Internal methods

    /// Used by the path editor to initialise some data
    public void InitializeEditorData(bool in2DMode) {
      if (this.editorData == null) {
        this.editorData = new PathCreatorData();
      }

      this.editorData.bezierOrVertexPathModified -= this.OnPathUpdated;
      this.editorData.bezierOrVertexPathModified += this.OnPathUpdated;

      this.editorData.Initialize(this.transform.position, in2DMode);
      this.initialized = true;
    }

    public PathCreatorData EditorData { get { return this.editorData; } }

    void OnPathUpdated() {
      if (this.pathUpdated != null) {
        this.pathUpdated();
      }
    }

    #endregion
  }
}
