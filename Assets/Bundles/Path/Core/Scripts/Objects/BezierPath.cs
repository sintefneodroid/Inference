using System.Collections.Generic;
using UnityEngine;
using PathCreation.Utility;
using System.Linq;

namespace PathCreation {
  /// A bezier path is a path made by stitching together any number of (cubic) bezier curves.
  /// A single cubic bezier curve is defined by 4 points: anchor1, control1, control2, anchor2
  /// The curve moves between the 2 anchors, and the shape of the curve is affected by the positions of the 2 control points
  /// When two curves are stitched together, they share an anchor point (end anchor of curve 1 = start anchor of curve 2).
  /// So while one curve alone consists of 4 points, two curves are defined by 7 unique points.
  /// Apart from storing the points, this class also provides methods for working with the path.
  /// For example, adding, inserting, and deleting points.
  [System.Serializable]
  public class BezierPath {
    public event System.Action OnModified;

    public enum ControlMode {
      Aligned,
      Mirrored,
      Free,
      Automatic
    };

    #region Fields

    [SerializeField, HideInInspector] List<Vector3> points;
    [SerializeField, HideInInspector] bool isClosed;
    [SerializeField, HideInInspector] Vector3 localPosition;
    [SerializeField, HideInInspector] PathSpace space;
    [SerializeField, HideInInspector] ControlMode controlMode;
    [SerializeField, HideInInspector] float autoControlLength = .3f;
    [SerializeField, HideInInspector] bool boundsUpToDate;
    [SerializeField, HideInInspector] Vector3 pivot;
    [SerializeField, HideInInspector] Bounds bounds;
    [SerializeField, HideInInspector] Quaternion rotation = Quaternion.identity;
    [SerializeField, HideInInspector] Vector3 scale = Vector3.one;

    // Normals settings
    [SerializeField, HideInInspector] List<float> perAnchorNormalsAngle;
    [SerializeField, HideInInspector] float globalNormalsAngle;
    [SerializeField, HideInInspector] bool flipNormals;

    #endregion

    #region Constructors

    /// <summary> Creates a two-anchor path centred around the given centre point </summary>
    ///<param name="isClosed"> Should the end point connect back to the start point? </param>
    ///<param name="space"> Determines if the path is in 3d space, or clamped to the xy/xz plane </param>
    public BezierPath(Vector3 centre, bool isClosed = false, PathSpace space = PathSpace.xyz) {
      var dir = (space == PathSpace.xz) ? Vector3.forward : Vector3.up;
      float width = 2;
      var controlHeight = .5f;
      var controlWidth = 1f;
      this.points = new List<Vector3> {
          centre + Vector3.left * width,
          centre + Vector3.left * controlWidth + dir * controlHeight,
          centre + Vector3.right * controlWidth - dir * controlHeight,
          centre + Vector3.right * width
      };

      this.perAnchorNormalsAngle = new List<float>() {0, 0};

      this.Space = space;
      this.IsClosed = isClosed;
    }

    /// <summary> Creates a path from the supplied 3D points </summary>
    ///<param name="points"> List or array of points to create the path from. </param>
    ///<param name="isClosed"> Should the end point connect back to the start point? </param>
    ///<param name="space"> Determines if the path is in 3d space, or clamped to the xy/xz plane </param>
    public BezierPath(IEnumerable<Vector3> points, bool isClosed = false, PathSpace space = PathSpace.xyz) {
      var pointsArray = points.ToArray();

      if (pointsArray.Length < 2) {
        Debug.LogError("Path requires at least 2 anchor points.");
      } else {
        this.controlMode = ControlMode.Automatic;
        this.points = new List<Vector3> {pointsArray[0], Vector3.zero, Vector3.zero, pointsArray[1]};
        this.perAnchorNormalsAngle = new List<float>(new float[] {0, 0});

        for (var i = 2; i < pointsArray.Length; i++) {
          this.AddSegmentToEnd(pointsArray[i]);
          this.perAnchorNormalsAngle.Add(0);
        }
      }

      this.Space = space;
      this.IsClosed = isClosed;
    }

    /// <summary> Creates a path from the positions of the supplied 2D points </summary>
    ///<param name="transforms"> List or array of transforms to create the path from. </param>
    ///<param name="isClosed"> Should the end point connect back to the start point? </param>
    ///<param name="space"> Determines if the path is in 3d space, or clamped to the xy/xz plane </param>
    public BezierPath(IEnumerable<Vector2> transforms, bool isClosed = false, PathSpace space = PathSpace.xy) : this(
        transforms.Select(p => new Vector3(p.x, p.y)),
        isClosed,
        space) { }

    /// <summary> Creates a path from the positions of the supplied transforms </summary>
    ///<param name="transforms"> List or array of transforms to create the path from. </param>
    ///<param name="isClosed"> Should the end point connect back to the start point? </param>
    ///<param name="space"> Determines if the path is in 3d space, or clamped to the xy/xz plane </param>
    public BezierPath(IEnumerable<Transform> transforms, bool isClosed = false, PathSpace space = PathSpace.xy) : this(
        transforms.Select(t => t.position),
        isClosed,
        space) { }

    /// <summary> Creates a path from the supplied 2D points </summary>
    ///<param name="points"> List or array of 2d points to create the path from. </param>
    ///<param name="isClosed"> Should the end point connect back to the start point? </param>
    ///<param name="pathSpace"> Determines if the path is in 3d space, or clamped to the xy/xz plane </param>
    public BezierPath(IEnumerable<Vector2> points, PathSpace space = PathSpace.xyz, bool isClosed = false) : this(
        points.Select(p => new Vector3(p.x, p.y)),
        isClosed,
        space) { }

    #endregion

    #region Public methods and accessors

    /// Get world space position of point
    public Vector3 this[int i] { get { return this.GetPoint(i); } }

    /// Get world space position of point
    public Vector3 GetPoint(int i) { return this.points[i] + this.localPosition; }

    /// Total number of points in the path (anchors and controls)
    public int NumPoints { get { return this.points.Count; } }

    /// Number of anchor points making up the path
    public int NumAnchorPoints { get { return (this.IsClosed) ? this.points.Count / 3 : (this.points.Count + 2) / 3; } }

    /// Number of bezier curves making up this path
    public int NumSegments { get { return this.points.Count / 3; } }

    /// Path can exist in 3D (xyz), 2D (xy), or Top-Down (xz) space
    /// In xy or xz space, points will be clamped to that plane (so in a 2D path, for example, points will always be at 0 on z axis)
    public PathSpace Space {
      get { return this.space; }
      set {
        if (value != this.space) {
          var previousSpace = this.space;
          this.space = value;
          this.UpdateToNewPathSpace(previousSpace);
        }
      }
    }

    /// If closed, path will loop back from end point to start point
    public bool IsClosed {
      get { return this.isClosed; }
      set {
        if (this.isClosed != value) {
          this.isClosed = value;
          this.UpdateClosedState();
        }
      }
    }

    /// The control mode determines the behaviour of control points.
    /// Possible modes are:
    /// Aligned = controls stay in straight line around their anchor
    /// Mirrored = controls stay in straight, equidistant line around their anchor
    /// Free = no constraints (use this if sharp corners are needed)
    /// Automatic = controls placed automatically to try make the path smooth
    public ControlMode ControlPointMode {
      get { return this.controlMode; }
      set {
        if (this.controlMode != value) {
          this.controlMode = value;
          if (this.controlMode == ControlMode.Automatic) {
            this.AutoSetAllControlPoints();
            this.NotifyPathModified();
          }
        }
      }
    }

    /// Position of the path in the world
    public Vector3 Position {
      get { return this.localPosition; }
      set {
        if (this.localPosition != value) {
          if (this.space == PathSpace.xy) {
            value.z = 0;
          } else if (this.space == PathSpace.xz) {
            value.y = 0;
          }

          this.localPosition = value;
          this.NotifyPathModified();
        }
      }
    }

    /// When using automatic control point placement, this value scales how far apart controls are placed
    public float AutoControlLength {
      get { return this.autoControlLength; }
      set {
        value = Mathf.Max(value, .01f);
        if (this.autoControlLength != value) {
          this.autoControlLength = value;
          this.AutoSetAllControlPoints();
          this.NotifyPathModified();
        }
      }
    }

    /// Add new anchor point to end of the path
    public void AddSegmentToEnd(Vector3 anchorPos) {
      if (this.isClosed) {
        return;
      }

      anchorPos -= this.localPosition;

      var lastAnchorIndex = this.points.Count - 1;
      // Set position for new control to be mirror of its counterpart
      var secondControlForOldLastAnchorOffset = (this.points[lastAnchorIndex] - this.points[lastAnchorIndex - 1]);
      if (this.controlMode != ControlMode.Mirrored && this.controlMode != ControlMode.Automatic) {
        // Set position for new control to be aligned with its counterpart, but with a length of half the distance from prev to new anchor
        var dstPrevToNewAnchor = (this.points[lastAnchorIndex] - anchorPos).magnitude;
        secondControlForOldLastAnchorOffset =
            (this.points[lastAnchorIndex] - this.points[lastAnchorIndex - 1]).normalized * dstPrevToNewAnchor * .5f;
      }

      var secondControlForOldLastAnchor = this.points[lastAnchorIndex] + secondControlForOldLastAnchorOffset;
      var controlForNewAnchor = (anchorPos + secondControlForOldLastAnchor) * .5f;

      this.points.Add(secondControlForOldLastAnchor);
      this.points.Add(controlForNewAnchor);
      this.points.Add(anchorPos);
      this.perAnchorNormalsAngle.Add(this.perAnchorNormalsAngle[this.perAnchorNormalsAngle.Count - 1]);

      if (this.controlMode == ControlMode.Automatic) {
        this.AutoSetAllAffectedControlPoints(this.points.Count - 1);
      }

      this.NotifyPathModified();
    }

    /// Add new anchor point to start of the path
    public void AddSegmentToStart(Vector3 anchorPos) {
      if (this.isClosed) {
        return;
      }

      anchorPos -= this.localPosition;

      // Set position for new control to be mirror of its counterpart
      var secondControlForOldFirstAnchorOffset = (this.points[0] - this.points[1]);
      if (this.controlMode != ControlMode.Mirrored && this.controlMode != ControlMode.Automatic) {
        // Set position for new control to be aligned with its counterpart, but with a length of half the distance from prev to new anchor
        var dstPrevToNewAnchor = (this.points[0] - anchorPos).magnitude;
        secondControlForOldFirstAnchorOffset =
            secondControlForOldFirstAnchorOffset.normalized * dstPrevToNewAnchor * .5f;
      }

      var secondControlForOldFirstAnchor = this.points[0] + secondControlForOldFirstAnchorOffset;
      var controlForNewAnchor = (anchorPos + secondControlForOldFirstAnchor) * .5f;
      this.points.Insert(0, anchorPos);
      this.points.Insert(1, controlForNewAnchor);
      this.points.Insert(2, secondControlForOldFirstAnchor);
      this.perAnchorNormalsAngle.Insert(0, this.perAnchorNormalsAngle[0]);

      if (this.controlMode == ControlMode.Automatic) {
        this.AutoSetAllAffectedControlPoints(0);
      }

      this.NotifyPathModified();
    }

    /// Insert new anchor point at given position. Automatically place control points around it so as to keep shape of curve the same
    public void SplitSegment(Vector3 anchorPos, int segmentIndex, float splitTime) {
      anchorPos -= this.localPosition;

      if (this.controlMode == ControlMode.Automatic) {
        this.points.InsertRange(segmentIndex * 3 + 2, new Vector3[] {Vector3.zero, anchorPos, Vector3.zero});
        this.AutoSetAllAffectedControlPoints(segmentIndex * 3 + 3);
      } else {
        // Split the curve to find where control points can be inserted to least affect shape of curve
        // Curve will probably be deformed slightly since splitTime is only an estimate (for performance reasons, and so doesn't correspond exactly with anchorPos)
        var splitSegment = CubicBezierUtility.SplitCurve(this.GetPointsInSegment(segmentIndex), splitTime);
        this.points.InsertRange(
            segmentIndex * 3 + 2,
            new Vector3[] {splitSegment[0][2], splitSegment[1][0], splitSegment[1][1]});
        var newAnchorIndex = segmentIndex * 3 + 3;
        this.MovePoint(newAnchorIndex - 2, splitSegment[0][1], true);
        this.MovePoint(newAnchorIndex + 2, splitSegment[1][2], true);
        this.MovePoint(newAnchorIndex, anchorPos, true);

        if (this.controlMode == ControlMode.Mirrored) {
          var avgDst = ((splitSegment[0][2] - anchorPos).magnitude + (splitSegment[1][1] - anchorPos).magnitude) / 2;
          this.MovePoint(newAnchorIndex + 1, anchorPos + (splitSegment[1][1] - anchorPos).normalized * avgDst, true);
        }
      }

      // Insert angle for new anchor (value should be set inbetween neighbour anchor angles)
      var newAnchorAngleIndex = segmentIndex + 1;
      var numAngles = this.perAnchorNormalsAngle.Count;
      var anglePrev = (newAnchorAngleIndex > 0 || this.isClosed)
                            ? this.perAnchorNormalsAngle[(newAnchorAngleIndex - 1 + numAngles) % numAngles]
                            : 0;
      var angleNext = (newAnchorAngleIndex < numAngles || this.isClosed)
                            ? this.perAnchorNormalsAngle[(newAnchorAngleIndex + 1) % numAngles]
                            : 0;
      this.perAnchorNormalsAngle.Insert(newAnchorAngleIndex, (anglePrev + angleNext) / 2f);

      this.NotifyPathModified();
    }

    /// Delete the anchor point at given index, as well as its associated control points
    public void DeleteSegment(int anchorIndex) {
      // Don't delete segment if its the last one remaining (or if only two segments in a closed path)
      if (this.NumSegments > 2 || !this.isClosed && this.NumSegments > 1) {
        if (anchorIndex == 0) {
          if (this.isClosed) {
            this.points[this.points.Count - 1] = this.points[2];
          }

          this.points.RemoveRange(0, 3);
        } else if (anchorIndex == this.points.Count - 1 && !this.isClosed) {
          this.points.RemoveRange(anchorIndex - 2, 3);
        } else {
          this.points.RemoveRange(anchorIndex - 1, 3);
        }

        this.perAnchorNormalsAngle.RemoveAt(anchorIndex / 3);

        if (this.controlMode == ControlMode.Automatic) {
          this.AutoSetAllControlPoints();
        }

        this.NotifyPathModified();
      }
    }

    /// Returns an array of the 4 points making up the segment (anchor1, control1, control2, anchor2)
    public Vector3[] GetPointsInSegment(int segmentIndex) {
      segmentIndex = Mathf.Clamp(segmentIndex, 0, this.NumSegments - 1);
      return new Vector3[] {
          this[segmentIndex * 3],
          this[segmentIndex * 3 + 1],
          this[segmentIndex * 3 + 2],
          this[this.LoopIndex(segmentIndex * 3 + 3)]
      };
    }

    /// Move an existing point to a new position
    public void MovePoint(int i, Vector3 pointPos, bool suppressPathModifiedEvent = false) {
      pointPos -= this.localPosition;

      if (this.space == PathSpace.xy) {
        pointPos.z = 0;
      } else if (this.space == PathSpace.xz) {
        pointPos.y = 0;
      }

      var deltaMove = pointPos - this.points[i];
      var isAnchorPoint = i % 3 == 0;

      // Don't process control point if control mode is set to automatic
      if (isAnchorPoint || this.controlMode != ControlMode.Automatic) {
        this.points[i] = pointPos;

        if (this.controlMode == ControlMode.Automatic) {
          this.AutoSetAllAffectedControlPoints(i);
        } else {
          // Move control points with anchor point
          if (isAnchorPoint) {
            if (i + 1 < this.points.Count || this.isClosed) {
              this.points[this.LoopIndex(i + 1)] += deltaMove;
            }

            if (i - 1 >= 0 || this.isClosed) {
              this.points[this.LoopIndex(i - 1)] += deltaMove;
            }
          }
          // If not in free control mode, then move attached control point to be aligned/mirrored (depending on mode)
          else if (this.controlMode != ControlMode.Free) {
            var nextPointIsAnchor = (i + 1) % 3 == 0;
            var attachedControlIndex = (nextPointIsAnchor) ? i + 2 : i - 2;
            var anchorIndex = (nextPointIsAnchor) ? i + 1 : i - 1;

            if (attachedControlIndex >= 0 && attachedControlIndex < this.points.Count || this.isClosed) {
              float distanceFromAnchor = 0;
              // If in aligned mode, then attached control's current distance from anchor point should be maintained
              if (this.controlMode == ControlMode.Aligned) {
                distanceFromAnchor =
                    (this.points[this.LoopIndex(anchorIndex)] - this.points[this.LoopIndex(attachedControlIndex)])
                    .magnitude;
              }
              // If in mirrored mode, then both control points should have the same distance from the anchor point
              else if (this.controlMode == ControlMode.Mirrored) {
                distanceFromAnchor = (this.points[this.LoopIndex(anchorIndex)] - this.points[i]).magnitude;
              }

              var dir = (this.points[this.LoopIndex(anchorIndex)] - pointPos).normalized;
              this.points[this.LoopIndex(attachedControlIndex)] =
                  this.points[this.LoopIndex(anchorIndex)] + dir * distanceFromAnchor;
            }
          }
        }

        if (!suppressPathModifiedEvent) {
          this.NotifyPathModified();
        }
      }
    }

    /// Rotation of the path around current pivot
    public Quaternion Rotation {
      get { return this.rotation; }
      set {
        if (this.space != PathSpace.xyz) {
          var axis = (this.space == PathSpace.xy) ? Vector3.forward : Vector3.up;
          var angle = (this.space == PathSpace.xy) ? value.eulerAngles.z : value.eulerAngles.y;
          value = Quaternion.AngleAxis(angle, axis);
        }

        if (this.rotation != value) {
          // Inverse of rotation takes us back to when there was no rotation applied, then multiply by new rotation
          var rotFromOrigin = value * Quaternion.Inverse(this.rotation);
          var localPivot = this.pivot - this.localPosition;
          // Apply rotation to all points
          for (var i = 0; i < this.points.Count; i++) {
            this.points[i] = rotFromOrigin * (this.points[i] - localPivot) + localPivot;
          }

          this.rotation = value;
          this.NotifyPathModified();
        }
      }
    }

    /// Scale of the path around current pivot
    public Vector3 Scale {
      get { return this.scale; }
      set {
        var minVal = 0.01f;
        // Ensure scale is never exactly zero since information would be lost when scale is applied
        if (value.x == 0) {
          value.x = minVal;
        }

        if (value.y == 0) {
          value.y = minVal;
        }

        if (value.z == 0) {
          value.z = minVal;
        }

        // Set unused axis to zero
        if (this.space == PathSpace.xy) {
          value.z = 0;
        } else if (this.space == PathSpace.xz) {
          value.y = 0;
        }

        if (this.scale != value) {
          // Find scale required to go from current applied scale to new scale
          var deltaScale = value;
          if (this.scale.x != 0) {
            deltaScale.x /= this.scale.x;
          }

          if (this.scale.y != 0) {
            deltaScale.y /= this.scale.y;
          }

          if (this.scale.z != 0) {
            deltaScale.z /= this.scale.z;
          }

          var localPivot = this.pivot - this.localPosition;
          // Apply the scale to all points
          for (var i = 0; i < this.points.Count; i++) {
            this.points[i] = Vector3.Scale(this.points[i] - localPivot, deltaScale) + localPivot;
          }

          this.scale = value;
          this.NotifyPathModified();
        }
      }
    }

    /// Current pivot point around which transformations occur
    public Vector3 Pivot { get { return this.pivot; } set { this.pivot = value; } }

    /// Flip the normal vectors 180 degrees
    public bool FlipNormals {
      get { return this.flipNormals; }
      set {
        if (this.flipNormals != value) {
          this.flipNormals = value;
          this.NotifyPathModified();
        }
      }
    }

    /// Global angle that all normal vectors are rotated by (only relevant for paths in 3D space)
    public float GlobalNormalsAngle {
      get { return this.globalNormalsAngle; }
      set {
        if (value != this.globalNormalsAngle) {
          this.globalNormalsAngle = value;
          this.NotifyPathModified();
        }
      }
    }

    /// Get the desired angle of the normal vector at a particular anchor (only relevant for paths in 3D space)
    public float GetAnchorNormalAngle(int anchorIndex) { return this.perAnchorNormalsAngle[anchorIndex] % 360; }

    /// Set the desired angle of the normal vector at a particular anchor (only relevant for paths in 3D space)
    public void SetAnchorNormalAngle(int anchorIndex, float angle) {
      angle = (angle + 360) % 360;
      if (this.perAnchorNormalsAngle[anchorIndex] != angle) {
        this.perAnchorNormalsAngle[anchorIndex] = angle;
        this.NotifyPathModified();
      }
    }

    /// Reset global and anchor normal angles to 0
    public void ResetNormalAngles() {
      for (var i = 0; i < this.perAnchorNormalsAngle.Count; i++) {
        this.perAnchorNormalsAngle[i] = 0;
      }

      this.globalNormalsAngle = 0;
      this.NotifyPathModified();
    }

    /// Bounding box containing the path
    public Bounds PathBounds {
      get {
        if (!this.boundsUpToDate) {
          this.UpdateBounds();
        }

        return this.bounds;
      }
    }

    #endregion

    #region Internal methods and accessors

    /// Update the bounding box of the path
    void UpdateBounds() {
      if (this.boundsUpToDate) {
        return;
      }

      // Loop through all segments and keep track of the minmax points of all their bounding boxes
      var minMax = new MinMax3D();

      for (var i = 0; i < this.NumSegments; i++) {
        var p = this.GetPointsInSegment(i);
        minMax.AddValue(p[0]);
        minMax.AddValue(p[3]);

        var extremePointTimes = CubicBezierUtility.ExtremePointTimes(p[0], p[1], p[2], p[3]);
        foreach (var t in extremePointTimes) {
          minMax.AddValue(CubicBezierUtility.EvaluateCurve(p, t));
        }
      }

      this.boundsUpToDate = true;
      this.bounds = new Bounds((minMax.Min + minMax.Max) / 2, minMax.Max - minMax.Min);
    }

    /// Determines good positions (for a smooth path) for the control points affected by a moved/inserted anchor point
    void AutoSetAllAffectedControlPoints(int updatedAnchorIndex) {
      for (var i = updatedAnchorIndex - 3; i <= updatedAnchorIndex + 3; i += 3) {
        if (i >= 0 && i < this.points.Count || this.isClosed) {
          this.AutoSetAnchorControlPoints(this.LoopIndex(i));
        }
      }

      this.AutoSetStartAndEndControls();
    }

    /// Determines good positions (for a smooth path) for all control points
    void AutoSetAllControlPoints() {
      if (this.NumAnchorPoints > 2) {
        for (var i = 0; i < this.points.Count; i += 3) {
          this.AutoSetAnchorControlPoints(i);
        }
      }

      this.AutoSetStartAndEndControls();
    }

    /// Calculates good positions (to result in smooth path) for the controls around specified anchor
    void AutoSetAnchorControlPoints(int anchorIndex) {
      // Calculate a vector that is perpendicular to the vector bisecting the angle between this anchor and its two immediate neighbours
      // The control points will be placed along that vector
      var anchorPos = this.points[anchorIndex];
      var dir = Vector3.zero;
      var neighbourDistances = new float[2];

      if (anchorIndex - 3 >= 0 || this.isClosed) {
        var offset = this.points[this.LoopIndex(anchorIndex - 3)] - anchorPos;
        dir += offset.normalized;
        neighbourDistances[0] = offset.magnitude;
      }

      if (anchorIndex + 3 >= 0 || this.isClosed) {
        var offset = this.points[this.LoopIndex(anchorIndex + 3)] - anchorPos;
        dir -= offset.normalized;
        neighbourDistances[1] = -offset.magnitude;
      }

      dir.Normalize();

      // Set the control points along the calculated direction, with a distance proportional to the distance to the neighbouring control point
      for (var i = 0; i < 2; i++) {
        var controlIndex = anchorIndex + i * 2 - 1;
        if (controlIndex >= 0 && controlIndex < this.points.Count || this.isClosed) {
          this.points[this.LoopIndex(controlIndex)] = anchorPos + dir * neighbourDistances[i] * this.autoControlLength;
        }
      }
    }

    /// Determines good positions (for a smooth path) for the control points at the start and end of a path
    void AutoSetStartAndEndControls() {
      if (this.isClosed) {
        // Handle case with only 2 anchor points separately, as will otherwise result in straight line ()
        if (this.NumAnchorPoints == 2) {
          var dirAnchorAToB = (this.points[3] - this.points[0]).normalized;
          var dstBetweenAnchors = (this.points[0] - this.points[3]).magnitude;
          var perp = Vector3.Cross(dirAnchorAToB, (this.space == PathSpace.xy) ? Vector3.forward : Vector3.up);
          this.points[1] = this.points[0] + perp * dstBetweenAnchors / 2f;
          this.points[5] = this.points[0] - perp * dstBetweenAnchors / 2f;
          this.points[2] = this.points[3] + perp * dstBetweenAnchors / 2f;
          this.points[4] = this.points[3] - perp * dstBetweenAnchors / 2f;
        } else {
          this.AutoSetAnchorControlPoints(0);
          this.AutoSetAnchorControlPoints(this.points.Count - 3);
        }
      } else {
        // Handle case with 2 anchor points separately, as otherwise minor adjustments cause path to constantly flip
        if (this.NumAnchorPoints == 2) {
          this.points[1] = this.points[0] + (this.points[3] - this.points[0]) * .25f;
          this.points[2] = this.points[3] + (this.points[0] - this.points[3]) * .25f;
        } else {
          this.points[1] = (this.points[0] + this.points[2]) * .5f;
          this.points[this.points.Count - 2] =
              (this.points[this.points.Count - 1] + this.points[this.points.Count - 3]) * .5f;
        }
      }
    }

    /// Update point positions for new path space
    /// (for example, if changing from xy to xz path, y and z axes will be swapped so the path keeps its shape in the new space)
    void UpdateToNewPathSpace(PathSpace previousSpace) {
      // If changing from 3d to 2d space, first find the bounds of the 3d path.
      // The axis with the smallest bounds will be discarded.
      if (previousSpace == PathSpace.xyz) {
        var boundsSize = this.PathBounds.size;
        var minBoundsSize = Mathf.Min(boundsSize.x, boundsSize.y, boundsSize.z);

        for (var i = 0; i < this.NumPoints; i++) {
          if (this.space == PathSpace.xy) {
            this.localPosition = new Vector3(this.localPosition.x, this.localPosition.y, 0);
            var x = (minBoundsSize == boundsSize.x) ? this.points[i].z : this.points[i].x;
            var y = (minBoundsSize == boundsSize.y) ? this.points[i].z : this.points[i].y;
            this.points[i] = new Vector3(x, y, 0);
          } else if (this.space == PathSpace.xz) {
            this.localPosition = new Vector3(this.localPosition.x, 0, this.localPosition.z);
            var x = (minBoundsSize == boundsSize.x) ? this.points[i].y : this.points[i].x;
            var z = (minBoundsSize == boundsSize.z) ? this.points[i].y : this.points[i].z;
            this.points[i] = new Vector3(x, 0, z);
          }
        }
      } else {
        // Nothing needs to change when going to 3d space
        if (this.space != PathSpace.xyz) {
          for (var i = 0; i < this.NumPoints; i++) {
            // from xz to xy
            if (this.space == PathSpace.xy) {
              this.localPosition = new Vector3(this.localPosition.x, this.localPosition.z);
              this.points[i] = new Vector3(this.points[i].x, this.points[i].z, 0);
            }
            // from xy to xz
            else if (this.space == PathSpace.xz) {
              this.localPosition = new Vector3(this.localPosition.x, 0, this.localPosition.y);
              this.points[i] = new Vector3(this.points[i].x, 0, this.points[i].y);
            }
          }
        }
      }

      if (this.space != PathSpace.xyz) {
        var axis = (this.space == PathSpace.xy) ? Vector3.forward : Vector3.up;
        var angle = (this.space == PathSpace.xy) ? this.rotation.eulerAngles.z : this.rotation.eulerAngles.y;
        this.rotation = Quaternion.AngleAxis(angle, axis);
      }

      this.NotifyPathModified();
    }

    /// Add/remove the extra 2 controls required for a closed path
    void UpdateClosedState() {
      if (this.isClosed) {
        // Set positions for new controls to mirror their counterparts
        var lastAnchorSecondControl = this.points[this.points.Count - 1] * 2 - this.points[this.points.Count - 2];
        var firstAnchorSecondControl = this.points[0] * 2 - this.points[1];
        if (this.controlMode != ControlMode.Mirrored && this.controlMode != ControlMode.Automatic) {
          // Set positions for new controls to be aligned with their counterparts, but with a length of half the distance between start/end anchor
          var dstBetweenStartAndEndAnchors = (this.points[this.points.Count - 1] - this.points[0]).magnitude;
          lastAnchorSecondControl = this.points[this.points.Count - 1]
                                    + (this.points[this.points.Count - 1] - this.points[this.points.Count - 2])
                                    .normalized
                                    * dstBetweenStartAndEndAnchors
                                    * .5f;
          firstAnchorSecondControl = this.points[0]
                                     + (this.points[0] - this.points[1]).normalized
                                     * dstBetweenStartAndEndAnchors
                                     * .5f;
        }

        this.points.Add(lastAnchorSecondControl);
        this.points.Add(firstAnchorSecondControl);
      } else {
        this.points.RemoveRange(this.points.Count - 2, 2);
      }

      if (this.controlMode == ControlMode.Automatic) {
        this.AutoSetStartAndEndControls();
      }

      if (this.OnModified != null) {
        this.OnModified();
      }
    }

    /// Loop index around to start/end of points array if out of bounds (useful when working with closed paths)
    int LoopIndex(int i) { return (i + this.points.Count) % this.points.Count; }

    // Called internally when the path is modified
    void NotifyPathModified() {
      this.boundsUpToDate = false;
      if (this.OnModified != null) {
        this.OnModified();
      }
    }

    #endregion
  }
}
