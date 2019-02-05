using System.Collections.Generic;
using Bundles.Path.Core.Scripts.Objects;
using Bundles.Path.Core.Scripts.Utility;
using UnityEditor;
using UnityEngine;

namespace Bundles.Path.Core.Editor.Helper {
  public class ScreenSpacePolyLine {
    const int AccuracyMultiplier = 10;

    // dont allow vertices to be spaced too far apart, as screenspace-worldspace conversion can then be noticeably off
    const float IntermediaryThreshold = .2f;

    public readonly List<Vector3> VerticesWorld;

    // For each point in the polyline, says which bezier segment it belongs to
    readonly List<int> _vertexToPathSegmentMap;

    // Stores the index in the vertices list where the start point of each segment is
    readonly int[] _segmentStartIndices;

    readonly float _pathLengthWorld;
    readonly float[] _cumululativeLengthWorld;

    Vector2[] _points;

    Vector3 _prevCamPos;
    Quaternion _prevCamRot;
    bool _premCamIsOrtho;

    public ScreenSpacePolyLine(BezierPath bezierPath, float maxAngleError, float minVertexDst, float accuracy = 1) {
      // Split path in vertices based on angle error
      VerticesWorld = new List<Vector3>();
      _vertexToPathSegmentMap = new List<int>();
      _segmentStartIndices = new int[bezierPath.NumSegments + 1];

      VerticesWorld.Add(bezierPath[0]);
      _vertexToPathSegmentMap.Add(0);
      var prevPointOnPath = bezierPath[0];
      float dstSinceLastVertex = 0;
      var lastAddedPoint = prevPointOnPath;
      float dstSinceLastIntermediary = 0;

      for (var segmentIndex = 0; segmentIndex < bezierPath.NumSegments; segmentIndex++) {
        var segmentPoints = bezierPath.GetPointsInSegment(segmentIndex);
        VerticesWorld.Add(segmentPoints[0]);
        _vertexToPathSegmentMap.Add(segmentIndex);
        _segmentStartIndices[segmentIndex] = VerticesWorld.Count - 1;

        prevPointOnPath = segmentPoints[0];
        lastAddedPoint = prevPointOnPath;
        dstSinceLastVertex = 0;
        dstSinceLastIntermediary = 0;

        var estimatedSegmentLength = CubicBezierUtility.EstimateCurveLength(
            segmentPoints[0],
            segmentPoints[1],
            segmentPoints[2],
            segmentPoints[3]);
        var divisions = Mathf.CeilToInt(estimatedSegmentLength * accuracy * AccuracyMultiplier);
        var increment = 1f / divisions;

        for (var t = increment; t <= 1; t += increment) {
          var pointOnPath = CubicBezierUtility.EvaluateCurve(
              segmentPoints[0],
              segmentPoints[1],
              segmentPoints[2],
              segmentPoints[3],
              t);
          var nextPointOnPath = CubicBezierUtility.EvaluateCurve(
              segmentPoints[0],
              segmentPoints[1],
              segmentPoints[2],
              segmentPoints[3],
              t + increment);

          // angle at current point on path
          var localAngle = 180 - MathUtility.MinAngle(prevPointOnPath, pointOnPath, nextPointOnPath);
          // angle between the last added vertex, the current point on the path, and the next point on the path
          var angleFromPrevVertex = 180 - MathUtility.MinAngle(lastAddedPoint, pointOnPath, nextPointOnPath);
          var angleError = Mathf.Max(localAngle, angleFromPrevVertex);

          if (angleError > maxAngleError && dstSinceLastVertex >= minVertexDst) {
            dstSinceLastVertex = 0;
            dstSinceLastIntermediary = 0;
            VerticesWorld.Add(pointOnPath);
            _vertexToPathSegmentMap.Add(segmentIndex);
            lastAddedPoint = pointOnPath;
          } else {
            if (dstSinceLastIntermediary > IntermediaryThreshold) {
              VerticesWorld.Add(pointOnPath);
              _vertexToPathSegmentMap.Add(segmentIndex);
              dstSinceLastIntermediary = 0;
            } else {
              dstSinceLastIntermediary += (pointOnPath - prevPointOnPath).magnitude;
            }

            dstSinceLastVertex += (pointOnPath - prevPointOnPath).magnitude;
          }

          prevPointOnPath = pointOnPath;
        }
      }

      _segmentStartIndices[bezierPath.NumSegments] = VerticesWorld.Count;

      // ensure final point gets added (unless path is closed loop)
      if (!bezierPath.IsClosed) {
        VerticesWorld.Add(bezierPath[bezierPath.NumPoints - 1]);
      } else {
        VerticesWorld.Add(bezierPath[0]);
      }

      // Calculate length
      _cumululativeLengthWorld = new float[VerticesWorld.Count];
      for (var i = 1; i < VerticesWorld.Count; i++) {
        _pathLengthWorld += (VerticesWorld[i - 1] - VerticesWorld[i]).magnitude;
        _cumululativeLengthWorld[i] = _pathLengthWorld;
      }
    }

    void ComputeScreenSpace() {
      if (Camera.current.transform.position != _prevCamPos
          || Camera.current.transform.rotation != _prevCamRot
          || Camera.current.orthographic != _premCamIsOrtho) {
        _points = new Vector2[VerticesWorld.Count];
        for (var i = 0; i < VerticesWorld.Count; i++) {
          _points[i] = HandleUtility.WorldToGUIPoint(VerticesWorld[i]);
        }

        _prevCamPos = Camera.current.transform.position;
        _prevCamRot = Camera.current.transform.rotation;
        _premCamIsOrtho = Camera.current.orthographic;
      }
    }

    public MouseInfo CalculateMouseInfo() {
      ComputeScreenSpace();

      var mousePos = Event.current.mousePosition;
      var minDst = float.MaxValue;
      var closestPolyLineSegmentIndex = 0;
      var closestBezierSegmentIndex = 0;

      for (var i = 0; i < _points.Length - 1; i++) {
        var dst = HandleUtility.DistancePointToLineSegment(mousePos, _points[i], _points[i + 1]);

        if (dst < minDst) {
          minDst = dst;
          closestPolyLineSegmentIndex = i;
          closestBezierSegmentIndex = _vertexToPathSegmentMap[i];
        }
      }

      var closestPointOnLine = MathUtility.ClosestPointOnLineSegment(
          mousePos,
          _points[closestPolyLineSegmentIndex],
          _points[closestPolyLineSegmentIndex + 1]);
      var dstToPointOnLine = (_points[closestPolyLineSegmentIndex] - closestPointOnLine).magnitude;
      var percentBetweenVertices = dstToPointOnLine
                                     / (_points[closestPolyLineSegmentIndex] - _points[closestPolyLineSegmentIndex + 1])
                                     .magnitude;
      var closestPoint3D = Vector3.Lerp(
          VerticesWorld[closestPolyLineSegmentIndex],
          VerticesWorld[closestPolyLineSegmentIndex + 1],
          percentBetweenVertices);

      var distanceAlongPathWorld = _cumululativeLengthWorld[closestPolyLineSegmentIndex]
                                     + Vector3.Distance(VerticesWorld[closestPolyLineSegmentIndex], closestPoint3D);
      var timeAlongPath = distanceAlongPathWorld / _pathLengthWorld;

      // Calculate how far between the current bezier segment the closest point on the line is

      var bezierSegmentStartIndex = _segmentStartIndices[closestBezierSegmentIndex];
      var bezierSegmentEndIndex = _segmentStartIndices[closestBezierSegmentIndex + 1];
      var bezierSegmentLength = _cumululativeLengthWorld[bezierSegmentEndIndex]
                                  - _cumululativeLengthWorld[bezierSegmentStartIndex];
      var distanceAlongBezierSegment = distanceAlongPathWorld - _cumululativeLengthWorld[bezierSegmentStartIndex];
      var timeAlongBezierSegment = distanceAlongBezierSegment / bezierSegmentLength;

      return new MouseInfo(
          minDst,
          closestPoint3D,
          distanceAlongPathWorld,
          timeAlongPath,
          timeAlongBezierSegment,
          closestBezierSegmentIndex);
    }

    public struct MouseInfo {
      public readonly float MouseDstToLine;
      public readonly Vector3 ClosestWorldPointToMouse;
      public readonly float DistanceAlongPathWorld;
      public readonly float TimeOnPath;
      public readonly float TimeOnBezierSegment;
      public readonly int ClosestSegmentIndex;

      public MouseInfo(
          float mouseDstToLine,
          Vector3 closestWorldPointToMouse,
          float distanceAlongPathWorld,
          float timeOnPath,
          float timeOnBezierSegment,
          int closestSegmentIndex) {
        this.MouseDstToLine = mouseDstToLine;
        this.ClosestWorldPointToMouse = closestWorldPointToMouse;
        this.DistanceAlongPathWorld = distanceAlongPathWorld;
        this.TimeOnPath = timeOnPath;
        this.TimeOnBezierSegment = timeOnBezierSegment;
        this.ClosestSegmentIndex = closestSegmentIndex;
      }
    }
  }
}
