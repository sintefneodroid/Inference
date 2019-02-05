﻿using Bundles.Path.Core.Scripts.Objects;
using UnityEngine;

namespace Bundles.Path.Examples.Scripts {
  public class RoadMeshCreator : PathSceneTool {
    [Header("Road settings")] public float roadWidth = .4f;
    [Range(0, .5f)] public float thickness = .15f;
    public bool flattenSurface;

    [Header("Material settings")] public Material roadMaterial;
    public Material undersideMaterial;
    public float textureTiling = 1;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;

    protected override void PathUpdated() {
      if (this.pathCreator != null) {
        this.AssignMeshComponents();
        this.AssignMaterials();
        this.meshFilter.mesh = this.CreateRoadMesh();
        ;
      }
    }

    Mesh CreateRoadMesh() {
      var verts = new Vector3[this.path.NumVertices * 8];
      var uvs = new Vector2[verts.Length];
      var normals = new Vector3[verts.Length];

      var numTris = 2 * (this.path.NumVertices - 1) + ((this.path.IsClosedLoop) ? 2 : 0);
      var roadTriangles = new int[numTris * 3];
      var underRoadTriangles = new int[numTris * 3];
      var sideOfRoadTriangles = new int[numTris * 2 * 3];

      var vertIndex = 0;
      var triIndex = 0;

      // Vertices for the top of the road are layed out:
      // 0  1
      // 8  9
      // and so on... So the triangle map 0,8,1 for example, defines a triangle from top left to bottom left to bottom right.
      int[] triangleMap = {0, 8, 1, 1, 8, 9};
      int[] sidesTriangleMap = {4, 6, 14, 12, 4, 14, 5, 15, 7, 13, 15, 5};

      var usePathNormals = !(this.path.Space == PathSpace.Xyz && this.flattenSurface);

      for (var i = 0; i < this.path.NumVertices; i++) {
        var localUp = (usePathNormals) ? Vector3.Cross(this.path.Tangents[i], this.path.Normals[i]) : this.path.Up;
        var localRight = (usePathNormals) ? this.path.Normals[i] : Vector3.Cross(localUp, this.path.Tangents[i]);

        // Find position to left and right of current path vertex
        var vertSideA = this.path.Vertices[i] - localRight * Mathf.Abs(this.roadWidth) - this.transform.position;
        var vertSideB = this.path.Vertices[i] + localRight * Mathf.Abs(this.roadWidth) - this.transform.position;

        // Add top of road vertices
        verts[vertIndex + 0] = vertSideA;
        verts[vertIndex + 1] = vertSideB;
        // Add bottom of road vertices
        verts[vertIndex + 2] = vertSideA - localUp * this.thickness;
        verts[vertIndex + 3] = vertSideB - localUp * this.thickness;

        // Duplicate vertices to get flat shading for sides of road
        verts[vertIndex + 4] = verts[vertIndex + 0];
        verts[vertIndex + 5] = verts[vertIndex + 1];
        verts[vertIndex + 6] = verts[vertIndex + 2];
        verts[vertIndex + 7] = verts[vertIndex + 3];

        // Set uv on y axis to path time (0 at start of path, up to 1 at end of path)
        uvs[vertIndex + 0] = new Vector2(0, this.path.Times[i]);
        uvs[vertIndex + 1] = new Vector2(1, this.path.Times[i]);

        // Top of road normals
        normals[vertIndex + 0] = localUp;
        normals[vertIndex + 1] = localUp;
        // Bottom of road normals
        normals[vertIndex + 2] = -localUp;
        normals[vertIndex + 3] = -localUp;
        // Sides of road normals
        normals[vertIndex + 4] = -localRight;
        normals[vertIndex + 5] = localRight;
        normals[vertIndex + 6] = -localRight;
        normals[vertIndex + 7] = localRight;

        // Set triangle indices
        if (i < this.path.NumVertices - 1 || this.path.IsClosedLoop) {
          for (var j = 0; j < triangleMap.Length; j++) {
            roadTriangles[triIndex + j] = (vertIndex + triangleMap[j]) % verts.Length;
            // reverse triangle map for under road so that triangles wind the other way and are visible from underneath
            underRoadTriangles[triIndex + j] = (vertIndex + triangleMap[triangleMap.Length - 1 - j] + 2) % verts.Length;
          }

          for (var j = 0; j < sidesTriangleMap.Length; j++) {
            sideOfRoadTriangles[triIndex * 2 + j] = (vertIndex + sidesTriangleMap[j]) % verts.Length;
          }
        }

        vertIndex += 8;
        triIndex += 6;
      }

      var mesh = new Mesh();
      mesh.vertices = verts;
      mesh.uv = uvs;
      mesh.normals = normals;
      mesh.subMeshCount = 3;
      mesh.SetTriangles(roadTriangles, 0);
      mesh.SetTriangles(underRoadTriangles, 1);
      mesh.SetTriangles(sideOfRoadTriangles, 2);
      mesh.RecalculateBounds();

      return mesh;
    }

    // Add MeshRenderer and MeshFilter components to this gameobject if not already attached
    void AssignMeshComponents() {
      // Find/creator mesh holder object in children
      var meshHolderName = "Mesh Holder";
      var meshHolder = this.transform.Find(meshHolderName);
      if (meshHolder == null) {
        meshHolder = new GameObject(meshHolderName).transform;
        meshHolder.transform.parent = this.transform;
        meshHolder.transform.localPosition = Vector3.zero;
      }

      //meshHolder.transform.position = Vector3.zero;
      meshHolder.transform.rotation = Quaternion.identity;

      // Ensure mesh renderer and filter components are assigned
      if (!meshHolder.gameObject.GetComponent<MeshFilter>()) {
        meshHolder.gameObject.AddComponent<MeshFilter>();
      }

      if (!meshHolder.GetComponent<MeshRenderer>()) {
        meshHolder.gameObject.AddComponent<MeshRenderer>();
      }

      this.meshRenderer = meshHolder.GetComponent<MeshRenderer>();
      this.meshFilter = meshHolder.GetComponent<MeshFilter>();
    }

    void AssignMaterials() {
      if (this.roadMaterial != null && this.undersideMaterial != null) {
        this.meshRenderer.sharedMaterials =
            new Material[] {this.roadMaterial, this.undersideMaterial, this.undersideMaterial};
        this.meshRenderer.sharedMaterials[0].mainTextureScale = new Vector3(1, this.textureTiling);
      }
    }
  }
}
