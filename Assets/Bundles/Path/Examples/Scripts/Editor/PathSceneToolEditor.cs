using Bundles.Path.Core.Scripts.Objects;
using UnityEditor;
using UnityEngine;

namespace Bundles.Path.Examples.Scripts.Editor {
  [CustomEditor(typeof(PathSceneTool), true)]
  public class PathSceneToolEditor : UnityEditor.Editor {
    protected PathSceneTool pathTool;
    bool isSubscribed;

    public override void OnInspectorGUI() {
      using (var check = new EditorGUI.ChangeCheckScope()) {
        DrawDefaultInspector();

        if (check.changed) {
          if (!isSubscribed) {
            TryFindPathCreator();
            Subscribe();
          }

          if (pathTool.autoUpdate) {
            pathTool.CreatePath();
          }
        }
      }

      if (GUILayout.Button("Manual Update")) {
        if (TryFindPathCreator()) {
          pathTool.CreatePath();
          SceneView.RepaintAll();
        }
      }
    }

    protected virtual void OnPathModified() {
      if (pathTool.autoUpdate) {
        pathTool.CreatePath();
      }
    }

    protected virtual void OnEnable() {
      pathTool = (PathSceneTool)target;
      pathTool.onDestroyed += OnToolDestroyed;

      if (TryFindPathCreator()) {
        Subscribe();
        pathTool.CreatePath();
      }
    }

    void OnToolDestroyed() {
      if (pathTool != null) {
        pathTool.pathCreator.PathUpdated -= OnPathModified;
      }
    }

    protected virtual void Subscribe() {
      if (pathTool.pathCreator != null) {
        isSubscribed = true;
        pathTool.pathCreator.PathUpdated -= OnPathModified;
        pathTool.pathCreator.PathUpdated += OnPathModified;
      }
    }

    bool TryFindPathCreator() {
      // Try find a path creator in the scene, if one is not already assigned
      if (pathTool.pathCreator == null) {
        if (pathTool.GetComponent<PathCreator>() != null) {
          pathTool.pathCreator = pathTool.GetComponent<PathCreator>();
        } else if (FindObjectOfType<PathCreator>()) {
          pathTool.pathCreator = FindObjectOfType<PathCreator>();
        }
      }

      return pathTool.pathCreator != null;
    }
  }
}
