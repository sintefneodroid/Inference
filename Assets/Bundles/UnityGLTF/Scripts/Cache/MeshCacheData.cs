﻿using GLTF;
using System.Collections.Generic;
using UnityEngine;

namespace UnityGLTF.Cache {
  public class MeshCacheData {
    public Mesh LoadedMesh { get; set; }
    public Dictionary<string, AttributeAccessor> MeshAttributes { get; set; }

    public MeshCacheData() { this.MeshAttributes = new Dictionary<string, AttributeAccessor>(); }

    /// <summary>
    /// Unloads the meshes in this cache.
    /// </summary>
    public void Unload() { Object.Destroy(this.LoadedMesh); }
  }
}