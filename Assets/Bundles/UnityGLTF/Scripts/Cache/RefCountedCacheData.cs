using System;
using System.Collections.Generic;

namespace Bundles.UnityGLTF.Scripts.Cache {
  /// <summary>
  /// A ref-counted cache data object containing lists of Unity objects that were created for the sake of a GLTF scene/node.
  /// This supports counting the amount of refcounts that will dispose of itself
  /// </summary>
  public class RefCountedCacheData {
    private bool _isDisposed = false;

    /// <summary>
    /// Ref count for this cache data.
    /// </summary>
    /// <remarks>
    /// Initialized to 0. When assigning the cache data to an instantiated GLTF
    /// object the count will increase.
    /// </remarks>
    private int _refCount = 0;

    private readonly object _refCountLock = new object();

    /// <summary>
    /// Meshes used by this GLTF node.
    /// </summary>
    public List<MeshCacheData[]> MeshCache { get; set; }

    /// <summary>
    /// Materials used by this GLTF node.
    /// </summary>
    public MaterialCacheData[] MaterialCache { get; set; }

    /// <summary>
    /// Textures used by this GLTF node.
    /// </summary>
    public TextureCacheData[] TextureCache { get; set; }

    public void IncreaseRefCount() {
      if (this._isDisposed) {
        throw new InvalidOperationException("Cannot inscrease the ref count on disposed cache data.");
      }

      lock (this._refCountLock) {
        this._refCount++;
      }
    }

    public void DecreaseRefCount() {
      if (this._isDisposed) {
        throw new InvalidOperationException("Cannot decrease the ref count on disposed cache data.");
      }

      lock (this._refCountLock) {
        if (this._refCount <= 0) {
          throw new InvalidOperationException("Cannot decrease the cache data ref count below zero.");
        }

        this._refCount--;
      }

      if (this._refCount <= 0) {
        this.DestroyCachedData();
      }
    }

    private void DestroyCachedData() {
      // Destroy the cached meshes
      for (var i = 0; i < this.MeshCache.Count; i++) {
        for (var j = 0; j < this.MeshCache[i].Length; j++) {
          if (this.MeshCache[i][j] != null) {
            this.MeshCache[i][j].Unload();
          }
        }
      }

      // Destroy the cached textures
      for (var i = 0; i < this.TextureCache.Length; i++) {
        if (this.TextureCache[i] != null) {
          this.TextureCache[i].Unload();
        }
      }

      // Destroy the cached materials
      for (var i = 0; i < this.MaterialCache.Length; i++) {
        if (this.MaterialCache[i] != null) {
          this.MaterialCache[i].Unload();
        }
      }

      this._isDisposed = true;
    }
  }
}
