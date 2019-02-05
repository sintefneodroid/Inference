using UnityEngine;
using UnityEngine.Serialization;

namespace Bundles.Path.Core.Scripts.Objects {
  /// Stores state data for the path creator editor
  [System.Serializable]
  public class PathCreatorData {
    public event System.Action BezierOrVertexPathModified;
    public event System.Action BezierCreated;

    [FormerlySerializedAs("_bezierPath")] [SerializeField] BezierPath bezierPath;
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
      if (this.bezierPath == null) {
        this.CreateBezier(centre, defaultIs2D);
      }

      this.vertexPathUpToDate = false;
      this.bezierPath.OnModified -= this.BezierPathEdited;
      this.bezierPath.OnModified += this.BezierPathEdited;
    }

    public void ResetBezierPath(Vector3 centre, bool defaultIs2D = false) { this.CreateBezier(centre, defaultIs2D); }

    void CreateBezier(Vector3 centre, bool defaultIs2D = false) {
      if (this.bezierPath != null) {
        this.bezierPath.OnModified -= this.BezierPathEdited;
      }

      var space = (defaultIs2D) ? PathSpace.Xy : PathSpace.Xyz;
      this.bezierPath = new BezierPath(centre, false, space);

      this.bezierPath.OnModified += this.BezierPathEdited;
      this.vertexPathUpToDate = false;

      if (this.BezierOrVertexPathModified != null) {
        this.BezierOrVertexPathModified();
      }

      if (this.BezierCreated != null) {
        this.BezierCreated();
      }
    }

    public BezierPath CBezierPath {
      get { return this.bezierPath; }
      set {
        this.bezierPath.OnModified -= this.BezierPathEdited;
        this.vertexPathUpToDate = false;
        this.bezierPath = value;
        this.bezierPath.OnModified += this.BezierPathEdited;

        if (this.BezierOrVertexPathModified != null) {
          this.BezierOrVertexPathModified();
        }

        if (this.BezierCreated != null) {
          this.BezierCreated();
        }
      }
    }

    // Get the current vertex path
    public VertexPath VertexPath {
      get {
        // create new vertex path if path was modified since this vertex path was created
        if (!this.vertexPathUpToDate || this._vertexPath == null) {
          this.vertexPathUpToDate = true;
          this._vertexPath = new VertexPath(
              this.CBezierPath,
              this.vertexPathMaxAngleError,
              this.vertexPathMinVertexSpacing);
        }

        return this._vertexPath;
      }
    }

    public void VertexPathSettingsChanged() {
      this.vertexPathUpToDate = false;
      if (this.BezierOrVertexPathModified != null) {
        this.BezierOrVertexPathModified();
      }
    }

    public void PathModifiedByUndo() {
      this.vertexPathUpToDate = false;
      if (this.BezierOrVertexPathModified != null) {
        this.BezierOrVertexPathModified();
      }
    }

    void BezierPathEdited() {
      this.vertexPathUpToDate = false;
      if (this.BezierOrVertexPathModified != null) {
        this.BezierOrVertexPathModified();
      }
    }
  }
}
