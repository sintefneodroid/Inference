using Bundles.Path.Core.Scripts.Objects;
using UnityEditor;
using UnityEngine;

namespace Bundles.Path.Core.Editor.Helper {
  public static class MouseUtility {
    /// <summary>
    /// Determines mouse position in world. If PathSpace is xy/xz, the position will be locked to that plane.
    /// If PathSpace is xyz, then depthFor3DSpace will be used as distance from scene camera.
    /// </summary>
    public static Vector3 GetMouseWorldPosition(PathSpace space, float depthFor3DSpace = 10) {
      var mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
      var worldMouse = mouseRay.GetPoint(depthFor3DSpace);

      // Mouse can only move on XY plane
      if (space == PathSpace.Xy) {
        var zDir = mouseRay.direction.z;
        if (zDir != 0) {
          var dstToXyPlane = Mathf.Abs(mouseRay.origin.z / zDir);
          worldMouse = mouseRay.GetPoint(dstToXyPlane);
        }
      }
      // Mouse can only move on XZ plane 
      else if (space == PathSpace.Xz) {
        var yDir = mouseRay.direction.y;
        if (yDir != 0) {
          var dstToXzPlane = Mathf.Abs(mouseRay.origin.y / yDir);
          worldMouse = mouseRay.GetPoint(dstToXzPlane);
        }
      }

      return worldMouse;
    }
  }
}
