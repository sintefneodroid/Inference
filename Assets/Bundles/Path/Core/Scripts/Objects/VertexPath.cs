using System.Collections.Generic;
using UnityEngine;
using PathCreation.Utility;

namespace PathCreation {
  /// A vertex path is a collection of points (vertices) that lie along a bezier path.
  /// This allows one to do things like move at a constant speed along the path,
  /// which is not possible with a bezier path directly due to how they're constructed mathematically.
  /// This class also provides methods for getting the position along the path at a certain distance or time
  /// (where time = 0 is the start of the path, and time = 1 is the end of the path).
  /// Other info about the path (tangents, normals, rotation) can also be retrieved in this manner.
  public class VertexPath {
    #region Fields

    public readonly PathSpace space;
    public readonly bool isClosedLoop;
    public readonly Vector3[] vertices;
    public readonly Vector3[] tangents;
    public readonly Vector3[] normals;

    /// Percentage along the path at each vertex (0 being start of path, and 1 being the end)
    public readonly float[] times;

    /// Total distance between the vertices of the polyline
    public readonly float length;

    /// Total distance from the first vertex up to each vertex in the polyline
    public readonly float[] cumulativeLengthAtEachVertex;

    /// Bounding box of the path
    public readonly Bounds bounds;

    /// Equal to (0,0,-1) for 2D paths, and (0,1,0) for XZ paths
    public readonly Vector3 up;

    // Default values and constants:    
    const int accuracy = 10; // A scalar for how many times bezier path is divided when determining vertex positions
    const float minVertexSpacing = .01f;

    #endregion

    #region Constructors

    /// <summary> Splits bezier path into array of vertices along the path.</summary>
    ///<param name="maxAngleError">How much can the angle of the path change before a vertex is added. This allows fewer vertices to be generated in straighter sections.</param>
    ///<param name="minVertexDst">Vertices won't be added closer together than this distance, regardless of angle error.</param>
    public VertexPath(BezierPath bezierPath, float maxAngleError = 0.3f, float minVertexDst = 0) : this(
        bezierPath,
        VertexPathUtility.SplitBezierPathByAngleError(bezierPath, maxAngleError, minVertexDst, accuracy)) { }

    /// <summary> Splits bezier path into array of vertices along the path.</summary>
    ///<param name="maxAngleError">How much can the angle of the path change before a vertex is added. This allows fewer vertices to be generated in straighter sections.</param>
    ///<param name="minVertexDst">Vertices won't be added closer together than this distance, regardless of angle error.</param>
    ///<param name="accuracy">Higher value means the change in angle is checked more frequently.</param>
    public VertexPath(BezierPath bezierPath, float vertexSpacing) : this(
        bezierPath,
        VertexPathUtility.SplitBezierPathEvenly(bezierPath, Mathf.Max(vertexSpacing, minVertexSpacing), accuracy)) { }

    /// Internal contructor
    VertexPath(BezierPath bezierPath, VertexPathUtility.PathSplitData pathSplitData) {
      this.space = bezierPath.Space;
      this.isClosedLoop = bezierPath.IsClosed;
      var numVerts = pathSplitData.vertices.Count;
      this.length = pathSplitData.cumulativeLength[numVerts - 1];

      this.vertices = new Vector3[numVerts];
      this.normals = new Vector3[numVerts];
      this.tangents = new Vector3[numVerts];
      this.cumulativeLengthAtEachVertex = new float[numVerts];
      this.times = new float[numVerts];
      this.bounds = new Bounds(
          (pathSplitData.minMax.Min + pathSplitData.minMax.Max) / 2,
          pathSplitData.minMax.Max - pathSplitData.minMax.Min);

      // Figure out up direction for path
      this.up = (this.bounds.size.z > this.bounds.size.y) ? Vector3.up : -Vector3.forward;
      var lastRotationAxis = this.up;

      // Loop through the data and assign to arrays.
      for (var i = 0; i < this.vertices.Length; i++) {
        this.vertices[i] = pathSplitData.vertices[i];
        this.tangents[i] = pathSplitData.tangents[i];
        this.cumulativeLengthAtEachVertex[i] = pathSplitData.cumulativeLength[i];
        this.times[i] = this.cumulativeLengthAtEachVertex[i] / this.length;

        // Calculate normals
        if (this.space == PathSpace.xyz) {
          if (i == 0) {
            this.normals[0] = Vector3.Cross(lastRotationAxis, pathSplitData.tangents[0]).normalized;
          } else {
            // First reflection
            var offset = (this.vertices[i] - this.vertices[i - 1]);
            var sqrDst = offset.sqrMagnitude;
            var r = lastRotationAxis - offset * 2 / sqrDst * Vector3.Dot(offset, lastRotationAxis);
            var t = this.tangents[i - 1] - offset * 2 / sqrDst * Vector3.Dot(offset, this.tangents[i - 1]);

            // Second reflection
            var v2 = this.tangents[i] - t;
            var c2 = Vector3.Dot(v2, v2);

            var finalRot = r - v2 * 2 / c2 * Vector3.Dot(v2, r);
            var n = Vector3.Cross(finalRot, this.tangents[i]).normalized;
            this.normals[i] = n;
            lastRotationAxis = finalRot;
          }
        } else {
          this.normals[i] = Vector3.Cross(this.tangents[i], this.up) * ((bezierPath.FlipNormals) ? 1 : -1);
        }
      }

      // Apply correction for 3d normals along a closed path
      if (this.space == PathSpace.xyz && this.isClosedLoop) {
        // Get angle between first and last normal (if zero, they're already lined up, otherwise we need to correct)
        var normalsAngleErrorAcrossJoin = Vector3.SignedAngle(
            this.normals[this.normals.Length - 1],
            this.normals[0],
            this.tangents[0]);
        // Gradually rotate the normals along the path to ensure start and end normals line up correctly
        if (Mathf.Abs(normalsAngleErrorAcrossJoin) > 0.1f) // don't bother correcting if very nearly correct
        {
          for (var i = 1; i < this.normals.Length; i++) {
            var t = (i / (this.normals.Length - 1f));
            var angle = normalsAngleErrorAcrossJoin * t;
            var rot = Quaternion.AngleAxis(angle, this.tangents[i]);
            this.normals[i] = rot * this.normals[i] * ((bezierPath.FlipNormals) ? -1 : 1);
          }
        }
      }

      // Rotate normals to match up with user-defined anchor angles
      if (this.space == PathSpace.xyz) {
        for (var anchorIndex = 0; anchorIndex < pathSplitData.anchorVertexMap.Count - 1; anchorIndex++) {
          var nextAnchorIndex = (this.isClosedLoop) ? (anchorIndex + 1) % bezierPath.NumSegments : anchorIndex + 1;

          var startAngle = bezierPath.GetAnchorNormalAngle(anchorIndex) + bezierPath.GlobalNormalsAngle;
          var endAngle = bezierPath.GetAnchorNormalAngle(nextAnchorIndex) + bezierPath.GlobalNormalsAngle;
          var deltaAngle = Mathf.DeltaAngle(startAngle, endAngle);

          var startVertIndex = pathSplitData.anchorVertexMap[anchorIndex];
          var endVertIndex = pathSplitData.anchorVertexMap[anchorIndex + 1];

          var num = endVertIndex - startVertIndex;
          if (anchorIndex == pathSplitData.anchorVertexMap.Count - 2) {
            num += 1;
          }

          for (var i = 0; i < num; i++) {
            var vertIndex = startVertIndex + i;
            var t = i / (num - 1f);
            var angle = startAngle + deltaAngle * t;
            var rot = Quaternion.AngleAxis(angle, this.tangents[vertIndex]);
            this.normals[vertIndex] = (rot * this.normals[vertIndex]) * ((bezierPath.FlipNormals) ? -1 : 1);
          }
        }
      }
    }

    #endregion

    #region Public methods and accessors

    public int NumVertices { get { return this.vertices.Length; } }

    /// Gets point on path based on distance travelled.
    public Vector3 GetPointAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.length;
      return this.GetPoint(t, endOfPathInstruction);
    }

    /// Gets forward direction on path based on distance travelled.
    public Vector3 GetDirectionAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.length;
      return this.GetDirection(t, endOfPathInstruction);
    }

    /// Gets normal vector on path based on distance travelled.
    public Vector3 GetNormalAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.length;
      return this.GetNormal(t, endOfPathInstruction);
    }

    /// Gets a rotation that will orient an object in the direction of the path at this point, with local up point along the path's normal
    public Quaternion GetRotationAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.length;
      return this.GetRotation(t, endOfPathInstruction);
    }

    /// Gets point on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetPoint(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.vertices[data.previousIndex], this.vertices[data.nextIndex], data.percentBetweenIndices);
    }

    /// Gets forward direction on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetDirection(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.tangents[data.previousIndex], this.tangents[data.nextIndex], data.percentBetweenIndices);
    }

    /// Gets normal vector on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetNormal(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.normals[data.previousIndex], this.normals[data.nextIndex], data.percentBetweenIndices);
    }

    /// Gets a rotation that will orient an object in the direction of the path at this point, with local up point along the path's normal
    public Quaternion GetRotation(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      var direction = Vector3.Lerp(
          this.tangents[data.previousIndex],
          this.tangents[data.nextIndex],
          data.percentBetweenIndices);
      var normal = Vector3.Lerp(
          this.normals[data.previousIndex],
          this.normals[data.nextIndex],
          data.percentBetweenIndices);
      return Quaternion.LookRotation(direction, normal);
    }

    #endregion

    #region Internal methods

    /// For a given value 't' between 0 and 1, calculate the indices of the two vertices before and after t. 
    /// Also calculate how far t is between those two vertices as a percentage between 0 and 1.
    TimeOnPathData CalculatePercentOnPathData(float t, EndOfPathInstruction endOfPathInstruction) {
      // Constrain t based on the end of path instruction
      switch (endOfPathInstruction) {
        case EndOfPathInstruction.Loop:
          // If t is negative, make it the equivalent value between 0 and 1
          if (t < 0) {
            t += Mathf.CeilToInt(Mathf.Abs(t));
          }

          t %= 1;
          break;
        case EndOfPathInstruction.Reverse:
          t = Mathf.PingPong(t, 1);
          break;
        case EndOfPathInstruction.Stop:
          t = Mathf.Clamp01(t);
          break;
      }

      var prevIndex = 0;
      var nextIndex = this.NumVertices - 1;
      var i = Mathf.RoundToInt(t * (this.NumVertices - 1)); // starting guess

      // Starts by looking at middle vertex and determines if t lies to the left or to the right of that vertex.
      // Continues dividing in half until closest surrounding vertices have been found.
      while (true) {
        // t lies to left
        if (t <= this.times[i]) {
          nextIndex = i;
        }
        // t lies to right
        else {
          prevIndex = i;
        }

        i = (nextIndex + prevIndex) / 2;

        if (nextIndex - prevIndex <= 1) {
          break;
        }
      }

      var abPercent = Mathf.InverseLerp(this.times[prevIndex], this.times[nextIndex], t);
      return new TimeOnPathData(prevIndex, nextIndex, abPercent);
    }

    struct TimeOnPathData {
      public readonly int previousIndex;
      public readonly int nextIndex;
      public readonly float percentBetweenIndices;

      public TimeOnPathData(int prev, int next, float percentBetweenIndices) {
        this.previousIndex = prev;
        this.nextIndex = next;
        this.percentBetweenIndices = percentBetweenIndices;
      }
    }

    #endregion
  }
}
