using System;
using System.Collections.Generic;
using Bundles.Path.Core.Editor.Helper;
using Bundles.Path.Core.Scripts.Objects;
using Bundles.Path.Core.Scripts.Utility;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bundles.Path.Core.Editor {
  /// Editor class for the creation of Bezier and Vertex paths
  [CustomEditor(typeof(PathCreator))]
  public class PathEditor : UnityEditor.Editor {
    #region Fields

    // Interaction:
    const float SegmentSelectDistanceThreshold = 10f;
    const float ScreenPolylineMaxAngleError = .3f;
    const float ScreenPolylineMinVertexDst = .01f;
    bool _shareTransformsWithPath = false; // Should changes to pathcreator's transform affect the path (and vice versa)

    // Help messages:
    const string HelpInfo =
        "Shift-click to add or insert new points. Control-click to delete points. For more detailed infomation, please refer to the documentation.";

    static readonly string[] SpaceNames = {"3D (xyz)", "2D (xy)", "Top-down (xz)"};
    static readonly string[] TabNames = {"Bézier Path", "Vertex Path"};

    const string ConstantSizeTooltip =
        "If true, anchor and control points will keep a constant size when zooming in the editor.";

    // Display
    const int InspectorSectionSpacing = 10;
    const float ConstantHandleScale = .01f;
    const float NormalsSpacing = .1f;
    GUIStyle _boldFoldoutStyle;

    // References:
    PathCreator _creator;
    UnityEditor.Editor _globalDisplaySettingsEditor;
    ScreenSpacePolyLine _screenSpaceLine;
    ScreenSpacePolyLine.MouseInfo _pathMouseInfo;
    GlobalDisplaySettings _globalDisplaySettings;
    PathHandle.HandleColours _splineAnchorColours;
    PathHandle.HandleColours _splineControlColours;
    Dictionary<GlobalDisplaySettings.HandleType, Handles.CapFunction> _capFunctions;
    ArcHandle _anchorAngleHandle = new ArcHandle();
    VertexPath _normalsVertexPath;

    // State variables:
    int _selectedSegmentIndex;
    int _draggingHandleIndex;
    int _mouseOverHandleIndex;
    int _handleIndexToDisplayAsTransform;

    bool _shiftLastFrame;
    bool _hasUpdatedScreenSpaceLine;
    bool _hasUpdatedNormalsVertexPath;
    bool _editingNormalsOld;

    Vector3 _positionOld;
    Quaternion _rotationOld;
    Vector3 _scaleOld;
    Quaternion _currentHandleRot = Quaternion.identity;
    Color _handlesStartCol;

    // Constants
    const int BezierPathTab = 0;
    const int VertexPathTab = 1;

    #endregion

    #region Inspectors

    public override void OnInspectorGUI() {
      // Initialize GUI styles
      if (_boldFoldoutStyle == null) {
        _boldFoldoutStyle = new GUIStyle(EditorStyles.foldout);
        _boldFoldoutStyle.fontStyle = FontStyle.Bold;
      }

      Undo.RecordObject(_creator, "Path settings changed");

      // Draw Bezier and Vertex tabs
      var tabIndex = GUILayout.Toolbar(Data.tabIndex, TabNames);
      if (tabIndex != Data.tabIndex) {
        Data.tabIndex = tabIndex;
        TabChanged();
      }

      // Draw inspector for active tab
      switch (Data.tabIndex) {
        case BezierPathTab:
          DrawBezierPathInspector();
          break;
        case VertexPathTab:
          DrawVertexPathInspector();
          break;
      }

      // Notify of undo/redo that might modify the path
      if (Event.current.type == EventType.ValidateCommand && Event.current.commandName == "UndoRedoPerformed") {
        Data.PathModifiedByUndo();
      }

      // Update visibility of default transform tool
      UpdateToolVisibility();
    }

    void DrawBezierPathInspector() {
      using (var check = new EditorGUI.ChangeCheckScope()) {
        // Path options:
        Data.showPathOptions = EditorGUILayout.Foldout(
            Data.showPathOptions,
            new GUIContent("Bézier Path Options"),
            true,
            _boldFoldoutStyle);
        if (Data.showPathOptions) {
          BezierPath.Space = (PathSpace)EditorGUILayout.Popup("Space", (int)BezierPath.Space, SpaceNames);
          BezierPath.ControlPointMode = (BezierPath.ControlMode)EditorGUILayout.EnumPopup(
              new GUIContent("Control Mode"),
              BezierPath.ControlPointMode);
          if (BezierPath.ControlPointMode == BezierPath.ControlMode.Automatic) {
            BezierPath.AutoControlLength = EditorGUILayout.Slider(
                new GUIContent("Control Spacing"),
                BezierPath.AutoControlLength,
                0,
                1);
          }

          BezierPath.IsClosed = EditorGUILayout.Toggle("Closed Path", BezierPath.IsClosed);
          Data.pathTransformationEnabled = EditorGUILayout.Toggle(
              new GUIContent("Enable Transforms"),
              Data.pathTransformationEnabled);

          if (GUILayout.Button("Reset Path")) {
            Undo.RecordObject(_creator, "Reset Path");
            var in2DEditorMode = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D;
            Data.ResetBezierPath(_creator.transform.position, in2DEditorMode);
          }

          GUILayout.Space(InspectorSectionSpacing);
        }

        Data.showNormals = EditorGUILayout.Foldout(
            Data.showNormals,
            new GUIContent("Normals Options"),
            true,
            _boldFoldoutStyle);
        if (Data.showNormals) {
          BezierPath.FlipNormals = EditorGUILayout.Toggle(new GUIContent("Flip Normals"), BezierPath.FlipNormals);
          if (BezierPath.Space == PathSpace.Xyz) {
            BezierPath.GlobalNormalsAngle = EditorGUILayout.Slider(
                new GUIContent("Global Angle"),
                BezierPath.GlobalNormalsAngle,
                0,
                360);

            if (GUILayout.Button("Reset Normals")) {
              Undo.RecordObject(_creator, "Reset Normals");
              BezierPath.FlipNormals = false;
              BezierPath.ResetNormalAngles();
            }
          }

          GUILayout.Space(InspectorSectionSpacing);
        }

        // Editor display options
        Data.showDisplayOptions = EditorGUILayout.Foldout(
            Data.showDisplayOptions,
            new GUIContent("Display Options"),
            true,
            _boldFoldoutStyle);
        if (Data.showDisplayOptions) {
          Data.showPathBounds = GUILayout.Toggle(Data.showPathBounds, new GUIContent("Show Path Bounds"));
          Data.showPerSegmentBounds = GUILayout.Toggle(
              Data.showPerSegmentBounds,
              new GUIContent("Show Segment Bounds"));
          Data.displayAnchorPoints = GUILayout.Toggle(Data.displayAnchorPoints, new GUIContent("Show Anchor Points"));
          if (!(BezierPath.ControlPointMode == BezierPath.ControlMode.Automatic
                && _globalDisplaySettings.hideAutoControls)) {
            Data.displayControlPoints = GUILayout.Toggle(
                Data.displayControlPoints,
                new GUIContent("Show Control Points"));
          }

          Data.keepConstantHandleSize = GUILayout.Toggle(
              Data.keepConstantHandleSize,
              new GUIContent("Constant Point Size", ConstantSizeTooltip));
          Data.bezierHandleScale = Mathf.Max(
              0,
              EditorGUILayout.FloatField(new GUIContent("Handle Scale"), Data.bezierHandleScale));
          DrawGlobalDisplaySettingsInspector();
        }

        if (check.changed) {
          SceneView.RepaintAll();
        }
      }
    }

    void DrawVertexPathInspector() {
      Data.showVertexPathOptions = EditorGUILayout.Foldout(
          Data.showVertexPathOptions,
          new GUIContent("Vertex Path Options"),
          true,
          _boldFoldoutStyle);
      if (Data.showVertexPathOptions) {
        using (var check = new EditorGUI.ChangeCheckScope()) {
          Data.vertexPathMaxAngleError = EditorGUILayout.Slider(
              new GUIContent("Max Angle Error"),
              Data.vertexPathMaxAngleError,
              0,
              45);
          Data.vertexPathMinVertexSpacing = EditorGUILayout.Slider(
              new GUIContent("Min Vertex Dst"),
              Data.vertexPathMinVertexSpacing,
              0,
              1);

          GUILayout.Space(InspectorSectionSpacing);
          if (check.changed) {
            Data.VertexPathSettingsChanged();
            SceneView.RepaintAll();
          }
        }
      }

      Data.showVertexPathDisplayOptions = EditorGUILayout.Foldout(
          Data.showVertexPathDisplayOptions,
          new GUIContent("Display Options"),
          true,
          _boldFoldoutStyle);
      if (Data.showVertexPathDisplayOptions) {
        using (var check = new EditorGUI.ChangeCheckScope()) {
          Data.vertexHandleSize = EditorGUILayout.Slider(new GUIContent("Vertex Scale"), Data.vertexHandleSize, 0, 1);
          Data.showNormalsInVertexMode = GUILayout.Toggle(Data.showNormalsInVertexMode, new GUIContent("Show Normals"));

          if (check.changed) {
            SceneView.RepaintAll();
          }
        }

        DrawGlobalDisplaySettingsInspector();
      }
    }

    void DrawGlobalDisplaySettingsInspector() {
      using (var check = new EditorGUI.ChangeCheckScope()) {
        Data.globalDisplaySettingsFoldout = EditorGUILayout.InspectorTitlebar(
            Data.globalDisplaySettingsFoldout,
            _globalDisplaySettings);
        if (Data.globalDisplaySettingsFoldout) {
          CreateCachedEditor(_globalDisplaySettings, null, ref _globalDisplaySettingsEditor);
          _globalDisplaySettingsEditor.OnInspectorGUI();
        }

        if (check.changed) {
          UpdateGlobalDisplaySettings();
          SceneView.RepaintAll();
        }
      }
    }

    #endregion

    #region Scene GUI

    void OnSceneGUI() {
      _handlesStartCol = Handles.color;
      switch (Data.tabIndex) {
        case BezierPathTab:
          ProcessBezierPathInput(Event.current);
          DrawBezierPathSceneEditor();
          break;
        case VertexPathTab:
          DrawVertexPathSceneEditor();
          break;
      }

      // Don't allow clicking over empty space to deselect the object
      if (Event.current.type == EventType.Layout) {
        HandleUtility.AddDefaultControl(0);
      }
    }

    void DrawVertexPathSceneEditor() {
      var bezierCol = _globalDisplaySettings.bezierPath;
      bezierCol.a *= .5f;

      for (var i = 0; i < BezierPath.NumSegments; i++) {
        var points = BezierPath.GetPointsInSegment(i);
        Handles.DrawBezier(points[0], points[3], points[1], points[2], bezierCol, null, 2);
      }

      Handles.color = _globalDisplaySettings.vertexPath;
      for (var i = 0; i < _creator.Path.NumVertices; i++) {
        var nextIndex = (i + 1) % _creator.Path.NumVertices;
        if (nextIndex != 0 || BezierPath.IsClosed) {
          Handles.DrawLine(_creator.Path.Vertices[i], _creator.Path.Vertices[nextIndex]);
        }
      }

      if (Data.showNormalsInVertexMode) {
        Handles.color = _globalDisplaySettings.normals;
        for (var i = 0; i < _creator.Path.NumVertices; i++) {
          Handles.DrawLine(
              _creator.Path.Vertices[i],
              _creator.Path.Vertices[i] + _creator.Path.Normals[i] * _globalDisplaySettings.normalsLength);
        }
      }

      Handles.color = _globalDisplaySettings.vertex;
      for (var i = 0; i < _creator.Path.NumVertices; i++) {
        Handles.SphereHandleCap(
            0,
            _creator.Path.Vertices[i],
            Quaternion.identity,
            Data.vertexHandleSize * .1f,
            EventType.Repaint);
      }
    }

    void ProcessBezierPathInput(Event e) {
      // Update path pivot point on mouse up
      if (e.type == EventType.MouseUp) {
        _currentHandleRot = Quaternion.identity;
        BezierPath.Pivot = BezierPath.PathBounds.center;
      }

      // Find which handle mouse is over. Start by looking at previous handle index first, as most likely to still be closest to mouse
      var previousMouseOverHandleIndex = (_mouseOverHandleIndex == -1) ? 0 : _mouseOverHandleIndex;
      _mouseOverHandleIndex = -1;
      for (var i = 0; i < BezierPath.NumPoints; i += 3) {
        var handleIndex = (previousMouseOverHandleIndex + i) % BezierPath.NumPoints;
        var handleRadius = GetHandleDiameter(
                                 _globalDisplaySettings.anchorSize * Data.bezierHandleScale,
                                 BezierPath[handleIndex])
                             / 2f;
        var dst = HandleUtility.DistanceToCircle(BezierPath[handleIndex], handleRadius);
        if (Math.Abs(dst) <= float.Epsilon) {
          _mouseOverHandleIndex = handleIndex;
          break;
        }
      }

      // Shift-left click (when mouse not over a handle) to split or add segment
      if (_mouseOverHandleIndex == -1) {
        if (e.type == EventType.MouseDown && e.button == 0 && e.shift) {
          UpdatePathMouseInfo();
          // Insert point along selected segment
          if (_selectedSegmentIndex != -1 && _selectedSegmentIndex < BezierPath.NumSegments) {
            var newPathPoint = _pathMouseInfo.ClosestWorldPointToMouse;
            Undo.RecordObject(_creator, "Split segment");
            BezierPath.SplitSegment(newPathPoint, _selectedSegmentIndex, _pathMouseInfo.TimeOnBezierSegment);
          }
          // If path is not a closed loop, add new point on to the end of the path
          else if (!BezierPath.IsClosed) {
            // insert new point at same dst from scene camera as the point that comes before it (for a 3d path)
            var dstCamToEndpoint =
                (Camera.current.transform.position - BezierPath[BezierPath.NumPoints - 1]).magnitude;
            var newPathPoint = MouseUtility.GetMouseWorldPosition(BezierPath.Space, dstCamToEndpoint);

            Undo.RecordObject(_creator, "Add segment");
            if (e.control || e.command) {
              BezierPath.AddSegmentToStart(newPathPoint);
            } else {
              BezierPath.AddSegmentToEnd(newPathPoint);
            }
          }
        }
      }

      // Control click or backspace/delete to remove point
      if (e.keyCode == KeyCode.Backspace
          || e.keyCode == KeyCode.Delete
          || ((e.control || e.command) && e.type == EventType.MouseDown && e.button == 0)) {
        if (_mouseOverHandleIndex != -1) {
          Undo.RecordObject(_creator, "Delete segment");
          BezierPath.DeleteSegment(_mouseOverHandleIndex);
          if (_mouseOverHandleIndex == _handleIndexToDisplayAsTransform) {
            _handleIndexToDisplayAsTransform = -1;
          }

          _mouseOverHandleIndex = -1;
        }
      }

      // Holding shift and moving mouse (but mouse not over a handle/dragging a handle)
      if (_draggingHandleIndex == -1 && _mouseOverHandleIndex == -1) {
        var shiftDown = e.shift && !_shiftLastFrame;
        if (shiftDown || ((e.type == EventType.MouseMove || e.type == EventType.MouseDrag) && e.shift)) {
          UpdatePathMouseInfo();

          if (_pathMouseInfo.MouseDstToLine < SegmentSelectDistanceThreshold) {
            if (_pathMouseInfo.ClosestSegmentIndex != _selectedSegmentIndex) {
              _selectedSegmentIndex = _pathMouseInfo.ClosestSegmentIndex;
              HandleUtility.Repaint();
            }
          } else {
            _selectedSegmentIndex = -1;
            HandleUtility.Repaint();
          }
        }
      }

      if (_shareTransformsWithPath) {
        // Move bezier path if creator's transform position has changed
        if (_creator.transform.position != _positionOld) {
          BezierPath.Position += (_creator.transform.position - _positionOld);
          _positionOld = _creator.transform.position;
        }

        // Rotate bezier path if creator's transform rotation has changed
        if (_creator.transform.rotation != _rotationOld) {
          BezierPath.Rotation = _creator.transform.rotation;
          _creator.transform.rotation = BezierPath.Rotation; // set to constrained value
          _rotationOld = _creator.transform.rotation;
        }

        // Scale bezier path if creator's transform scale has changed
        if (_creator.transform.localScale != _scaleOld) {
          BezierPath.Scale = _creator.transform.localScale;
          _creator.transform.localScale = BezierPath.Scale; // set to constrained value
          _scaleOld = _creator.transform.localScale;
        }
      }

      _shiftLastFrame = e.shift;
    }

    void DrawBezierPathSceneEditor() {
      var displayControlPoints = Data.displayControlPoints
                                  && (BezierPath.ControlPointMode != BezierPath.ControlMode.Automatic
                                      || !_globalDisplaySettings.hideAutoControls);
      var bounds = BezierPath.PathBounds;

      // Draw normals
      if (Data.showNormals) {
        if (!_hasUpdatedNormalsVertexPath) {
          _normalsVertexPath = new VertexPath(BezierPath, NormalsSpacing);
          _hasUpdatedNormalsVertexPath = true;
        }

        if (_editingNormalsOld != Data.showNormals) {
          _editingNormalsOld = Data.showNormals;
          Repaint();
        }

        Handles.color = _globalDisplaySettings.normals;
        for (var i = 0; i < _normalsVertexPath.NumVertices; i++) {
          Handles.DrawLine(
              _normalsVertexPath.Vertices[i],
              _normalsVertexPath.Vertices[i] + _normalsVertexPath.Normals[i] * _globalDisplaySettings.normalsLength);
        }
      }

      for (var i = 0; i < BezierPath.NumSegments; i++) {
        var points = BezierPath.GetPointsInSegment(i);

        if (Data.showPerSegmentBounds) {
          var segmentBounds = CubicBezierUtility.CalculateBounds(points);
          Handles.color = _globalDisplaySettings.segmentBounds;
          Handles.DrawWireCube(segmentBounds.center, segmentBounds.size);
        }

        // Draw lines between control points
        if (displayControlPoints) {
          Handles.color = (BezierPath.ControlPointMode == BezierPath.ControlMode.Automatic)
                              ? _globalDisplaySettings.handleDisabled
                              : _globalDisplaySettings.controlLine;
          Handles.DrawLine(points[1], points[0]);
          Handles.DrawLine(points[2], points[3]);
        }

        // Draw path
        var highlightSegment = (i == _selectedSegmentIndex
                                 && Event.current.shift
                                 && _draggingHandleIndex == -1
                                 && _mouseOverHandleIndex == -1);
        var segmentCol =
            (highlightSegment) ? _globalDisplaySettings.highlightedPath : _globalDisplaySettings.bezierPath;
        Handles.DrawBezier(points[0], points[3], points[1], points[2], segmentCol, null, 2);
      }

      // Draw rotate/scale/move tool
      if (Data.pathTransformationEnabled && !Event.current.alt && !Event.current.shift) {
        if (Tools.current == Tool.Rotate) {
          Undo.RecordObject(_creator, "Rotate Path");
          var newHandleRot = Handles.DoRotationHandle(_currentHandleRot, BezierPath.Pivot);
          var deltaRot = newHandleRot * Quaternion.Inverse(_currentHandleRot);
          _currentHandleRot = newHandleRot;

          var newRot = deltaRot * BezierPath.Rotation;
          BezierPath.Rotation = newRot;
          if (_shareTransformsWithPath) {
            _creator.transform.rotation = newRot;
            _rotationOld = newRot;
          }
        } else if (Tools.current == Tool.Scale) {
          Undo.RecordObject(_creator, "Scale Path");
          BezierPath.Scale = Handles.DoScaleHandle(
              BezierPath.Scale,
              BezierPath.Pivot,
              Quaternion.identity,
              HandleUtility.GetHandleSize(BezierPath.Pivot));
          if (_shareTransformsWithPath) {
            _creator.transform.localScale = BezierPath.Scale;
            _scaleOld = BezierPath.Scale;
          }
        } else {
          Undo.RecordObject(_creator, "Move Path");

          BezierPath.Pivot = bounds.center;
          var newCentre = Handles.DoPositionHandle(BezierPath.Pivot, Quaternion.identity);
          var deltaCentre = newCentre - BezierPath.Pivot;
          BezierPath.Position += deltaCentre;
          if (_shareTransformsWithPath) {
            _creator.transform.position = BezierPath.Position;
            _positionOld = BezierPath.Position;
          }
        }
      }

      if (Data.showPathBounds) {
        Handles.color = _globalDisplaySettings.bounds;
        Handles.DrawWireCube(bounds.center, bounds.size);
      }

      if (Data.displayAnchorPoints) {
        for (var i = 0; i < BezierPath.NumPoints; i += 3) {
          DrawHandle(i);
        }
      }

      if (displayControlPoints) {
        for (var i = 1; i < BezierPath.NumPoints - 1; i += 3) {
          DrawHandle(i);
          DrawHandle(i + 1);
        }
      }
    }

    void DrawHandle(int i) {
      var handlePosition = BezierPath[i];

      var anchorHandleSize = GetHandleDiameter(
          _globalDisplaySettings.anchorSize * Data.bezierHandleScale,
          BezierPath[i]);
      var controlHandleSize = GetHandleDiameter(
          _globalDisplaySettings.controlSize * Data.bezierHandleScale,
          BezierPath[i]);

      var isAnchorPoint = i % 3 == 0;
      var isInteractive = isAnchorPoint || BezierPath.ControlPointMode != BezierPath.ControlMode.Automatic;
      var handleSize = (isAnchorPoint) ? anchorHandleSize : controlHandleSize;
      var doTransformHandle = i == _handleIndexToDisplayAsTransform;

      var handleColours = (isAnchorPoint) ? _splineAnchorColours : _splineControlColours;
      var cap = _capFunctions[(isAnchorPoint) ? _globalDisplaySettings.anchorShape : _globalDisplaySettings.controlShape];
      PathHandle.HandleInputType handleInputType;
      handlePosition = PathHandle.DrawHandle(
          handlePosition,
          BezierPath.Space,
          isInteractive,
          handleSize,
          cap,
          handleColours,
          out handleInputType,
          i);

      if (doTransformHandle) {
        // Show normals rotate tool 
        if (Data.showNormals && Tools.current == Tool.Rotate && isAnchorPoint && BezierPath.Space == PathSpace.Xyz) {
          Handles.color = _handlesStartCol;

          var attachedControlIndex = (i == BezierPath.NumPoints - 1) ? i - 1 : i + 1;
          var dir = (BezierPath[attachedControlIndex] - handlePosition).normalized;
          var handleRotOffset = (360 + BezierPath.GlobalNormalsAngle) % 360;
          _anchorAngleHandle.radius = handleSize * 3;
          _anchorAngleHandle.angle = handleRotOffset + BezierPath.GetAnchorNormalAngle(i / 3);
          var handleDirection = Vector3.Cross(dir, Vector3.up);
          var handleMatrix = Matrix4x4.TRS(
              handlePosition,
              Quaternion.LookRotation(handleDirection, dir),
              Vector3.one);

          using (new Handles.DrawingScope(handleMatrix)) {
            // draw the handle
            EditorGUI.BeginChangeCheck();
            _anchorAngleHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck()) {
              Undo.RecordObject(_creator, "Set angle");
              BezierPath.SetAnchorNormalAngle(i / 3, _anchorAngleHandle.angle - handleRotOffset);
            }
          }
        } else {
          handlePosition = Handles.DoPositionHandle(handlePosition, Quaternion.identity);
        }
      }

      switch (handleInputType) {
        case PathHandle.HandleInputType.LmbDrag:
          _draggingHandleIndex = i;
          _handleIndexToDisplayAsTransform = -1;
          break;
        case PathHandle.HandleInputType.LmbRelease:
          _draggingHandleIndex = -1;
          _handleIndexToDisplayAsTransform = -1;
          break;
        case PathHandle.HandleInputType.LmbClick:
          if (Event.current.shift) {
            _handleIndexToDisplayAsTransform = -1; // disable move tool if new point added
          } else {
            if (_handleIndexToDisplayAsTransform == i) {
              _handleIndexToDisplayAsTransform = -1; // disable move tool if clicking on point under move tool
            } else {
              _handleIndexToDisplayAsTransform = i;
            }
          }

          break;
        case PathHandle.HandleInputType.LmbPress:
          if (_handleIndexToDisplayAsTransform != i) {
            _handleIndexToDisplayAsTransform = -1;
          }

          break;
      }

      if (BezierPath[i] != handlePosition) {
        Undo.RecordObject(_creator, "Move point");
        BezierPath.MovePoint(i, handlePosition);
      }
    }

    #endregion

    #region Internal methods

    void OnDisable() { Tools.hidden = false; }

    void OnEnable() {
      _creator = (PathCreator)target;
      var in2DEditorMode = EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D;
      _creator.InitializeEditorData(in2DEditorMode);
      var transform = _creator.transform;
      _positionOld = transform.position;
      _rotationOld = transform.rotation;
      _scaleOld = transform.localScale;

      Data.BezierCreated -= ResetState;
      Data.BezierCreated += ResetState;
      Undo.undoRedoPerformed -= OnUndoRedo;
      Undo.undoRedoPerformed += OnUndoRedo;

      LoadDisplaySettings();
      UpdateGlobalDisplaySettings();
      UpdateToolVisibility();
      ResetState();
    }

    void OnUndoRedo() {
      _hasUpdatedScreenSpaceLine = false;
      _hasUpdatedNormalsVertexPath = false;
      _selectedSegmentIndex = -1;

      Repaint();
    }

    void TabChanged() {
      SceneView.RepaintAll();
      RepaintUnfocusedSceneViews();
    }

    void LoadDisplaySettings() {
      // Global display settings:
      var guids = AssetDatabase.FindAssets("t:GlobalDisplaySettings");
      if (guids.Length == 0) {
        Debug.LogWarning("Could not find DisplaySettings asset. Will use default settings instead.");
        _globalDisplaySettings = ScriptableObject.CreateInstance<GlobalDisplaySettings>();
      } else {
        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
        _globalDisplaySettings = AssetDatabase.LoadAssetAtPath<GlobalDisplaySettings>(path);
      }

      _capFunctions = new Dictionary<GlobalDisplaySettings.HandleType, Handles.CapFunction>();
      _capFunctions.Add(GlobalDisplaySettings.HandleType.Circle, Handles.CylinderHandleCap);
      _capFunctions.Add(GlobalDisplaySettings.HandleType.Sphere, Handles.SphereHandleCap);
      _capFunctions.Add(GlobalDisplaySettings.HandleType.Square, Handles.CubeHandleCap);
    }

    void UpdateGlobalDisplaySettings() {
      var gds = _globalDisplaySettings;
      _splineAnchorColours = new PathHandle.HandleColours(
          gds.anchor,
          gds.anchorHighlighted,
          gds.anchorSelected,
          gds.handleDisabled);
      _splineControlColours = new PathHandle.HandleColours(
          gds.control,
          gds.controlHighlighted,
          gds.controlSelected,
          gds.handleDisabled);

      _anchorAngleHandle.fillColor = new Color(1, 1, 1, .05f);
      _anchorAngleHandle.wireframeColor = Color.grey;
      _anchorAngleHandle.radiusHandleColor = Color.clear;
      _anchorAngleHandle.angleHandleColor = Color.white;
    }

    void ResetState() {
      _selectedSegmentIndex = -1;
      _draggingHandleIndex = -1;
      _mouseOverHandleIndex = -1;
      _handleIndexToDisplayAsTransform = -1;
      _hasUpdatedScreenSpaceLine = false;
      _hasUpdatedNormalsVertexPath = false;
      BezierPath.Pivot = BezierPath.PathBounds.center;

      BezierPath.OnModified -= OnPathModifed;
      BezierPath.OnModified += OnPathModifed;

      SceneView.RepaintAll();
    }

    void OnPathModifed() {
      _hasUpdatedScreenSpaceLine = false;
      _hasUpdatedNormalsVertexPath = false;

      RepaintUnfocusedSceneViews();
    }

    void RepaintUnfocusedSceneViews() {
      // If multiple scene views are open, repaint those which do not have focus.
      if (SceneView.sceneViews.Count > 1) {
        foreach (SceneView sv in SceneView.sceneViews) {
          if (EditorWindow.focusedWindow != (EditorWindow)sv) {
            sv.Repaint();
          }
        }
      }
    }

    void UpdatePathMouseInfo() {
      if (!_hasUpdatedScreenSpaceLine) {
        _screenSpaceLine = new ScreenSpacePolyLine(BezierPath, ScreenPolylineMaxAngleError, ScreenPolylineMinVertexDst);
        _hasUpdatedScreenSpaceLine = true;
      }

      _pathMouseInfo = _screenSpaceLine.CalculateMouseInfo();
    }

    float GetHandleDiameter(float diameter, Vector3 handlePosition) {
      var scaledDiameter = diameter * ConstantHandleScale;
      if (Data.keepConstantHandleSize) {
        scaledDiameter *= HandleUtility.GetHandleSize(handlePosition) * 2.5f;
      }

      return scaledDiameter;
    }

    BezierPath BezierPath { get { return Data.CBezierPath; } }

    PathCreatorData Data { get { return _creator.EditorData; } }

    bool EditingNormals {
      get {
        return Tools.current == Tool.Rotate
               && _handleIndexToDisplayAsTransform % 3 == 0
               && BezierPath.Space == PathSpace.Xyz;
      }
    }

    void UpdateToolVisibility() {
      // Hide/unhide tools depending on if inspector is folded
      var hideTools = UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(_creator);
      if (Tools.hidden != hideTools) {
        Tools.hidden = hideTools;
      }
    }

    #endregion
  }
}
