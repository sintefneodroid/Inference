﻿using UnityEngine;

namespace Bundles.Path.Core.Scripts.Utility {
  public static class MathUtility {
    public static bool LineSegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2) {
      var d = (b2.x - b1.x) * (a1.y - a2.y) - (a1.x - a2.x) * (b2.y - b1.y);
      if (d == 0)
        return false;
      var t = ((b1.y - b2.y) * (a1.x - b1.x) + (b2.x - b1.x) * (a1.y - b1.y)) / d;
      var u = ((a1.y - a2.y) * (a1.x - b1.x) + (a2.x - a1.x) * (a1.y - b1.y)) / d;

      return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    public static bool LinesIntersect(Vector2 a1, Vector2 a2, Vector2 a3, Vector2 a4) {
      return (a1.x - a2.x) * (a3.y - a4.y) - (a1.y - a2.y) * (a3.x - a4.x) != 0;
    }

    public static Vector2 PointOfLineLineIntersection(Vector2 a1, Vector2 a2, Vector2 a3, Vector2 a4) {
      var d = (a1.x - a2.x) * (a3.y - a4.y) - (a1.y - a2.y) * (a3.x - a4.x);
      if (d == 0) {
        Debug.LogError(
            "Lines are parallel, please check that this is not the case before calling line intersection method");
        return Vector2.zero;
      } else {
        var n = (a1.x - a3.x) * (a3.y - a4.y) - (a1.y - a3.y) * (a3.x - a4.x);
        var t = n / d;
        return a1 + (a2 - a1) * t;
      }
    }

    public static Vector2 ClosestPointOnLineSegment(Vector2 p, Vector2 a, Vector2 b) {
      var aB = b - a;
      var aP = p - a;
      var sqrLenAB = aB.sqrMagnitude;

      if (sqrLenAB == 0)
        return a;

      var t = Mathf.Clamp01(Vector2.Dot(aP, aB) / sqrLenAB);
      return a + aB * t;
    }

    public static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b) {
      var aB = b - a;
      var aP = p - a;
      var sqrLenAB = aB.sqrMagnitude;

      if (sqrLenAB == 0)
        return aP.magnitude;

      var t = Mathf.Clamp01(Vector2.Dot(aP, aB) / sqrLenAB);
      return Vector2.Distance(p, a + aB * t);
    }

    public static int SideOfLine(Vector2 a, Vector2 b, Vector2 c) {
      return (int)Mathf.Sign((c.x - a.x) * (-b.y + a.y) + (c.y - a.y) * (b.x - a.x));
    }

    /// returns the smallest angle between ABC. Never greater than 180
    public static float MinAngle(Vector3 a, Vector3 b, Vector3 c) { return Vector3.Angle((a - b), (c - b)); }

    public static bool PointInTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 p) {
      var area = 0.5f * (-b.y * c.x + a.y * (-b.x + c.x) + a.x * (b.y - c.y) + b.x * c.y);
      var s = 1 / (2 * area) * (a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y);
      var t = 1 / (2 * area) * (a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y);
      return s >= 0 && t >= 0 && (s + t) <= 1;
    }

    public static bool PointsAreClockwise(Vector2[] points) {
      float signedArea = 0;
      for (var i = 0; i < points.Length; i++) {
        var nextIndex = (i + 1) % points.Length;
        signedArea += (points[nextIndex].x - points[i].x) * (points[nextIndex].y + points[i].y);
      }

      return signedArea >= 0;
    }
  }
}
