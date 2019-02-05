using System.Collections.Generic;
using Bundles.Path.Core.Scripts.Objects;
using UnityEditor;
using UnityEngine;

namespace Bundles.Path.Core.Editor.Helper {
  public static class PathHandle {
    public const float ExtraInputRadius = .005f;

    static Vector2 _handleDragMouseStart;
    static Vector2 _handleDragMouseEnd;
    static Vector3 _handleDragWorldStart;

    static int _selectedHandleId;
    static bool _mouseIsOverAHandle;

    public enum HandleInputType {
      None,
      LmbPress,
      LmbClick,
      LmbDrag,
      LmbRelease,
    };

    static readonly int Hint;
    static float _dstMouseToDragPointStart;

    static List<int> _ids;

    static PathHandle() {
      Hint = 109264;
      _ids = new List<int>();

      _dstMouseToDragPointStart = float.MaxValue;
    }

    public static Vector3 DrawHandle(
        Vector3 position,
        PathSpace space,
        bool isInteractive,
        float handleDiameter,
        Handles.CapFunction capFunc,
        HandleColours colours,
        out HandleInputType inputType,
        int handleIndex) {
      var id = GetId(handleIndex);
      var screenPosition = Handles.matrix.MultiplyPoint(position);
      var cachedMatrix = Handles.matrix;

      inputType = HandleInputType.None;

      var eventType = Event.current.GetTypeForControl(id);
      var handleRadius = handleDiameter / 2f;
      var dstToHandle = HandleUtility.DistanceToCircle(position, handleRadius + ExtraInputRadius);
      var dstToMouse = HandleUtility.DistanceToCircle(position, 0);

      // Handle input events
      if (isInteractive) {
        // Repaint if mouse is entering/exiting handle (for highlight colour)
        if (dstToHandle == 0) {
          if (!_mouseIsOverAHandle) {
            HandleUtility.Repaint();
            _mouseIsOverAHandle = true;
          }
        } else {
          if (_mouseIsOverAHandle) {
            HandleUtility.Repaint();
            _mouseIsOverAHandle = false;
          }
        }

        switch (eventType) {
          case EventType.MouseDown:
            if (Event.current.button == 0 && Event.current.modifiers != EventModifiers.Alt) {
              if (dstToHandle == 0 && dstToMouse < _dstMouseToDragPointStart) {
                _dstMouseToDragPointStart = dstToMouse;
                GUIUtility.hotControl = id;
                _handleDragMouseEnd = _handleDragMouseStart = Event.current.mousePosition;
                _handleDragWorldStart = position;
                _selectedHandleId = id;
                inputType = HandleInputType.LmbPress;
              }
            }

            break;

          case EventType.MouseUp:
            _dstMouseToDragPointStart = float.MaxValue;
            if (GUIUtility.hotControl == id && Event.current.button == 0) {
              GUIUtility.hotControl = 0;
              _selectedHandleId = -1;
              Event.current.Use();

              inputType = HandleInputType.LmbRelease;

              if (Event.current.mousePosition == _handleDragMouseStart) {
                inputType = HandleInputType.LmbClick;
              }
            }

            break;

          case EventType.MouseDrag:
            if (GUIUtility.hotControl == id && Event.current.button == 0) {
              _handleDragMouseEnd += new Vector2(Event.current.delta.x, -Event.current.delta.y);
              var position2 = Camera.current.WorldToScreenPoint(Handles.matrix.MultiplyPoint(_handleDragWorldStart))
                                  + (Vector3)(_handleDragMouseEnd - _handleDragMouseStart);
              inputType = HandleInputType.LmbDrag;
              // Handle can move freely in 3d space
              if (space == PathSpace.Xyz) {
                position = Handles.matrix.inverse.MultiplyPoint(Camera.current.ScreenToWorldPoint(position2));
              }
              // Handle is clamped to xy or xz plane
              else {
                position = MouseUtility.GetMouseWorldPosition(space);
              }

              GUI.changed = true;
              Event.current.Use();
            }

            break;
        }
      }

      switch (eventType) {
        case EventType.Repaint:
          var originalColour = Handles.color;
          Handles.color = (isInteractive) ? colours.DefaultColour : colours.DisabledColour;

          if (id == GUIUtility.hotControl) {
            Handles.color = colours.SelectedColour;
          } else if (dstToHandle == 0 && _selectedHandleId == -1 && isInteractive) {
            Handles.color = colours.HighlightedColour;
          }

          Handles.matrix = Matrix4x4.identity;
          var lookForward = Vector3.up;
          var cam = Camera.current;
          if (cam != null) {
            if (cam.orthographic) {
              lookForward = -cam.transform.forward;
            } else {
              lookForward = (cam.transform.position - position);
            }
          }

          capFunc(id, screenPosition, Quaternion.LookRotation(lookForward), handleDiameter, EventType.Repaint);
          Handles.matrix = cachedMatrix;

          Handles.color = originalColour;
          break;

        case EventType.Layout:
          Handles.matrix = Matrix4x4.identity;
          HandleUtility.AddControl(id, HandleUtility.DistanceToCircle(screenPosition, handleDiameter / 2f));
          Handles.matrix = cachedMatrix;
          break;
      }

      return position;
    }

    public struct HandleColours {
      public Color DefaultColour;
      public Color HighlightedColour;
      public Color SelectedColour;
      public Color DisabledColour;

      public HandleColours(Color defaultColour, Color highlightedColour, Color selectedColour, Color disabledColour) {
        this.DefaultColour = defaultColour;
        this.HighlightedColour = highlightedColour;
        this.SelectedColour = selectedColour;
        this.DisabledColour = disabledColour;
      }
    }

    static void AddIDs(int upToIndex) {
      var numIdAtStart = _ids.Count;
      var numToAdd = (upToIndex - numIdAtStart) + 1;
      for (var i = 0; i < numToAdd; i++) {
        _ids.Add(GUIUtility.GetControlID(Hint + numIdAtStart + i, FocusType.Passive)); //
      }
    }

    static int GetId(int handleIndex) {
      if (handleIndex >= _ids.Count) {
        AddIDs(handleIndex);
      }

      return _ids[handleIndex];
    }
  }
}
