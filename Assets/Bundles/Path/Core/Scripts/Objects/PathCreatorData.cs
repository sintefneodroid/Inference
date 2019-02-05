using UnityEngine;
using System.Collections.Generic;

namespace PathCreation {
  /// Stores state data for the path creator editor
  [System.Serializable]
  public class PathCreatorData {
    public event System.Action bezierOrVertexPathModified;
    public event System.Action bezierCreated;

    [SerializeField] BezierPath _bezierPath;
    VertexPath _vertexPath;

    [SerializeField] bool vertexPathUpToDate;

    // vertex path settings
    public float vertexPathMaxAngleError = .3f;
    public float vertexPathMinVertexSpacing = 0.01f;

    // bezier display settings
    public bool pathTransformationEnabled;
    public bool showPathBounds;
    public bool showPerSegmentBounds;
    public bool displayAnchorPoints = true;
    public bool displayControlPoints = true;
    public float bezierHandleScale = 1;
    public bool globalDisplaySettingsFoldout;
    public bool keepConstantHandleSize;

    // vertex display settings
    public float vertexHandleSize = .2f;
    public bool showNormalsInVertexMode;

    // Editor display states
    public bool showDisplayOptions;
    public bool showPathOptions = true;
    public bool showVertexPathDisplayOptions;
    public bool showVertexPathOptions = true;
    public bool showNormals;
    public bool showNormalsHelpInfo;
    public int tabIndex;

    public void Initialize(Vector3 centre, bool defaultIs2D) {
      if (this._bezierPath == null) {
        this.CreateBezier(centre, defaultIs2D);
      }

      this.vertexPathUpToDate = false;
      this._bezierPath.OnModified -= this.BezierPathEdited;
      this._bezierPath.OnModified += this.BezierPathEdited;
    }

    public void ResetBezierPath(Vector3 centre, bool defaultIs2D = false) { this.CreateBezier(centre, defaultIs2D); }

    void CreateBezier(Vector3 centre, bool defaultIs2D = false) {
      if (this._bezierPath != null) {
        this._bezierPath.OnModified -= this.BezierPathEdited;
      }

      var space = (defaultIs2D) ? PathSpace.xy : PathSpace.xyz;
      this._bezierPath = new BezierPath(centre, false, space);

      this._bezierPath.OnModified += this.BezierPathEdited;
      this.vertexPathUpToDate = false;

      if (this.bezierOrVertexPathModified != null) {
        this.bezierOrVertexPathModified();
      }

      if (this.bezierCreated != null) {
        this.bezierCreated();
      }
    }

    public BezierPath bezierPath {
      get { return this._bezierPath; }
      set {
        this._bezierPath.OnModified -= this.BezierPathEdited;
        this.vertexPathUpToDate = false;
        this._bezierPath = value;
        this._bezierPath.OnModified += this.BezierPathEdited;

        if (this.bezierOrVertexPathModified != null) {
          this.bezierOrVertexPathModified();
        }

        if (this.bezierCreated != null) {
          this.bezierCreated();
        }
      }
    }

    // Get the current vertex path
    public VertexPath vertexPath {
      get {
        // create new vertex path if path was modified since this vertex path was created
        if (!this.vertexPathUpToDate || this._vertexPath == null) {
          this.vertexPathUpToDate = true;
          this._vertexPath = new VertexPath(
              this.bezierPath,
              this.vertexPathMaxAngleError,
              this.vertexPathMinVertexSpacing);
        }

        return this._vertexPath;
      }
    }

    public void VertexPathSettingsChanged() {
      this.vertexPathUpToDate = false;
      if (this.bezierOrVertexPathModified != null) {
        this.bezierOrVertexPathModified();
      }
    }

    public void PathModifiedByUndo() {
      this.vertexPathUpToDate = false;
      if (this.bezierOrVertexPathModified != null) {
        this.bezierOrVertexPathModified();
      }
    }

    void BezierPathEdited() {
      this.vertexPathUpToDate = false;
      if (this.bezierOrVertexPathModified != null) {
        this.bezierOrVertexPathModified();
      }
    }
  }
}
