using Bundles.Path.Core.Scripts.Utility;
using UnityEngine;

namespace Bundles.Path.Core.Scripts.Objects {
  /// A vertex path is a collection of points (vertices) that lie along a bezier path.
  /// This allows one to do things like move at a constant speed along the path,
  /// which is not possible with a bezier path directly due to how they're constructed mathematically.
  /// This class also provides methods for getting the position along the path at a certain distance or time
  /// (where time = 0 is the start of the path, and time = 1 is the end of the path).
  /// Other info about the path (tangents, normals, rotation) can also be retrieved in this manner.
  public class VertexPath {
    #region Fields

    public readonly PathSpace Space;
    public readonly bool IsClosedLoop;
    public readonly Vector3[] Vertices;
    public readonly Vector3[] Tangents;
    public readonly Vector3[] Normals;

    /// Percentage along the path at each vertex (0 being start of path, and 1 being the end)
    public readonly float[] Times;

    /// Total distance between the vertices of the polyline
    public readonly float Length;

    /// Total distance from the first vertex up to each vertex in the polyline
    public readonly float[] CumulativeLengthAtEachVertex;

    /// Bounding box of the path
    public readonly Bounds Bounds;

    /// Equal to (0,0,-1) for 2D paths, and (0,1,0) for XZ paths
    public readonly Vector3 Up;

    // Default values and constants:    
    const int Accuracy = 10; // A scalar for how many times bezier path is divided when determining vertex positions
    const float MinVertexSpacing = .01f;

    #endregion

    #region Constructors

    /// <summary> Splits bezier path into array of vertices along the path.</summary>
    ///<param name="maxAngleError">How much can the angle of the path change before a vertex is added. This allows fewer vertices to be generated in straighter sections.</param>
    ///<param name="minVertexDst">Vertices won't be added closer together than this distance, regardless of angle error.</param>
    public VertexPath(BezierPath bezierPath, float maxAngleError = 0.3f, float minVertexDst = 0) : this(
        bezierPath,
        VertexPathUtility.SplitBezierPathByAngleError(bezierPath, maxAngleError, minVertexDst, Accuracy)) { }

    /// <summary> Splits bezier path into array of vertices along the path.</summary>
    ///<param name="maxAngleError">How much can the angle of the path change before a vertex is added. This allows fewer vertices to be generated in straighter sections.</param>
    ///<param name="minVertexDst">Vertices won't be added closer together than this distance, regardless of angle error.</param>
    ///<param name="accuracy">Higher value means the change in angle is checked more frequently.</param>
    public VertexPath(BezierPath bezierPath, float vertexSpacing) : this(
        bezierPath,
        VertexPathUtility.SplitBezierPathEvenly(bezierPath, Mathf.Max(vertexSpacing, MinVertexSpacing), Accuracy)) { }

    /// Internal contructor
    VertexPath(BezierPath bezierPath, VertexPathUtility.PathSplitData pathSplitData) {
      this.Space = bezierPath.Space;
      this.IsClosedLoop = bezierPath.IsClosed;
      var numVerts = pathSplitData.vertices.Count;
      this.Length = pathSplitData.cumulativeLength[numVerts - 1];

      this.Vertices = new Vector3[numVerts];
      this.Normals = new Vector3[numVerts];
      this.Tangents = new Vector3[numVerts];
      this.CumulativeLengthAtEachVertex = new float[numVerts];
      this.Times = new float[numVerts];
      this.Bounds = new Bounds(
          (pathSplitData.minMax.Min + pathSplitData.minMax.Max) / 2,
          pathSplitData.minMax.Max - pathSplitData.minMax.Min);

      // Figure out up direction for path
      this.Up = (this.Bounds.size.z > this.Bounds.size.y) ? Vector3.up : -Vector3.forward;
      var lastRotationAxis = this.Up;

      // Loop through the data and assign to arrays.
      for (var i = 0; i < this.Vertices.Length; i++) {
        this.Vertices[i] = pathSplitData.vertices[i];
        this.Tangents[i] = pathSplitData.tangents[i];
        this.CumulativeLengthAtEachVertex[i] = pathSplitData.cumulativeLength[i];
        this.Times[i] = this.CumulativeLengthAtEachVertex[i] / this.Length;

        // Calculate normals
        if (this.Space == PathSpace.Xyz) {
          if (i == 0) {
            this.Normals[0] = Vector3.Cross(lastRotationAxis, pathSplitData.tangents[0]).normalized;
          } else {
            // First reflection
            var offset = (this.Vertices[i] - this.Vertices[i - 1]);
            var sqrDst = offset.sqrMagnitude;
            var r = lastRotationAxis - offset * 2 / sqrDst * Vector3.Dot(offset, lastRotationAxis);
            var t = this.Tangents[i - 1] - offset * 2 / sqrDst * Vector3.Dot(offset, this.Tangents[i - 1]);

            // Second reflection
            var v2 = this.Tangents[i] - t;
            var c2 = Vector3.Dot(v2, v2);

            var finalRot = r - v2 * 2 / c2 * Vector3.Dot(v2, r);
            var n = Vector3.Cross(finalRot, this.Tangents[i]).normalized;
            this.Normals[i] = n;
            lastRotationAxis = finalRot;
          }
        } else {
          this.Normals[i] = Vector3.Cross(this.Tangents[i], this.Up) * ((bezierPath.FlipNormals) ? 1 : -1);
        }
      }

      // Apply correction for 3d normals along a closed path
      if (this.Space == PathSpace.Xyz && this.IsClosedLoop) {
        // Get angle between first and last normal (if zero, they're already lined up, otherwise we need to correct)
        var normalsAngleErrorAcrossJoin = Vector3.SignedAngle(
            this.Normals[this.Normals.Length - 1],
            this.Normals[0],
            this.Tangents[0]);
        // Gradually rotate the normals along the path to ensure start and end normals line up correctly
        if (Mathf.Abs(normalsAngleErrorAcrossJoin) > 0.1f) // don't bother correcting if very nearly correct
        {
          for (var i = 1; i < this.Normals.Length; i++) {
            var t = (i / (this.Normals.Length - 1f));
            var angle = normalsAngleErrorAcrossJoin * t;
            var rot = Quaternion.AngleAxis(angle, this.Tangents[i]);
            this.Normals[i] = rot * this.Normals[i] * ((bezierPath.FlipNormals) ? -1 : 1);
          }
        }
      }

      // Rotate normals to match up with user-defined anchor angles
      if (this.Space == PathSpace.Xyz) {
        for (var anchorIndex = 0; anchorIndex < pathSplitData.anchorVertexMap.Count - 1; anchorIndex++) {
          var nextAnchorIndex = (this.IsClosedLoop) ? (anchorIndex + 1) % bezierPath.NumSegments : anchorIndex + 1;

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
            var rot = Quaternion.AngleAxis(angle, this.Tangents[vertIndex]);
            this.Normals[vertIndex] = (rot * this.Normals[vertIndex]) * ((bezierPath.FlipNormals) ? -1 : 1);
          }
        }
      }
    }

    #endregion

    #region Public methods and accessors

    public int NumVertices { get { return this.Vertices.Length; } }

    /// Gets point on path based on distance travelled.
    public Vector3 GetPointAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.Length;
      return this.GetPoint(t, endOfPathInstruction);
    }

    /// Gets forward direction on path based on distance travelled.
    public Vector3 GetDirectionAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.Length;
      return this.GetDirection(t, endOfPathInstruction);
    }

    /// Gets normal vector on path based on distance travelled.
    public Vector3 GetNormalAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.Length;
      return this.GetNormal(t, endOfPathInstruction);
    }

    /// Gets a rotation that will orient an object in the direction of the path at this point, with local up point along the path's normal
    public Quaternion GetRotationAtDistance(
        float dst,
        EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var t = dst / this.Length;
      return this.GetRotation(t, endOfPathInstruction);
    }

    /// Gets point on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetPoint(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.Vertices[data.PreviousIndex], this.Vertices[data.NextIndex], data.PercentBetweenIndices);
    }

    /// Gets forward direction on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetDirection(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.Tangents[data.PreviousIndex], this.Tangents[data.NextIndex], data.PercentBetweenIndices);
    }

    /// Gets normal vector on path based on 'time' (where 0 is start, and 1 is end of path).
    public Vector3 GetNormal(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      return Vector3.Lerp(this.Normals[data.PreviousIndex], this.Normals[data.NextIndex], data.PercentBetweenIndices);
    }

    /// Gets a rotation that will orient an object in the direction of the path at this point, with local up point along the path's normal
    public Quaternion GetRotation(float t, EndOfPathInstruction endOfPathInstruction = EndOfPathInstruction.Loop) {
      var data = this.CalculatePercentOnPathData(t, endOfPathInstruction);
      var direction = Vector3.Lerp(
          this.Tangents[data.PreviousIndex],
          this.Tangents[data.NextIndex],
          data.PercentBetweenIndices);
      var normal = Vector3.Lerp(
          this.Normals[data.PreviousIndex],
          this.Normals[data.NextIndex],
          data.PercentBetweenIndices);
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
        if (t <= this.Times[i]) {
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

      var abPercent = Mathf.InverseLerp(this.Times[prevIndex], this.Times[nextIndex], t);
      return new TimeOnPathData(prevIndex, nextIndex, abPercent);
    }

    struct TimeOnPathData {
      public readonly int PreviousIndex;
      public readonly int NextIndex;
      public readonly float PercentBetweenIndices;

      public TimeOnPathData(int prev, int next, float percentBetweenIndices) {
        this.PreviousIndex = prev;
        this.NextIndex = next;
        this.PercentBetweenIndices = percentBetweenIndices;
      }
    }

    #endregion
  }
}
