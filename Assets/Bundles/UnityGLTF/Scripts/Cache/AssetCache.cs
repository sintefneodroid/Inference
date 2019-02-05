using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bundles.UnityGLTF.Scripts.Cache {
  /// <summary>
  /// Caches data in order to construct a unity object
  /// </summary>
  public class AssetCache : IDisposable {
    /// <summary>
    /// Streams to the images to be loaded
    /// </summary>
    public Stream[] ImageStreamCache { get; private set; }

    /// <summary>
    /// Loaded raw texture data
    /// </summary>
    public Texture2D[] ImageCache { get; private set; }

    /// <summary>
    /// Textures to be used for assets. Textures from image cache with samplers applied
    /// </summary>
    public TextureCacheData[] TextureCache { get; private set; }

    /// <summary>
    /// Cache for materials to be applied to the meshes
    /// </summary>
    public MaterialCacheData[] MaterialCache { get; private set; }

    /// <summary>
    /// Byte buffers that represent the binary contents that get parsed
    /// </summary>
    public BufferCacheData[] BufferCache { get; private set; }

    /// <summary>
    /// Cache of loaded meshes
    /// </summary>
    public List<MeshCacheData[]> MeshCache { get; private set; }

    /// <summary>
    /// Cache of loaded animations
    /// </summary>
    public AnimationCacheData[] AnimationCache { get; private set; }

    /// <summary>
    /// Cache of loaded node objects
    /// </summary>
    public GameObject[] NodeCache { get; private set; }

    /// <summary>
    /// Creates an asset cache which caches objects used in scene
    /// </summary>
    /// <param name="imageCacheSize"></param>
    /// <param name="textureCacheSize"></param>
    /// <param name="materialCacheSize"></param>
    /// <param name="bufferCacheSize"></param>
    /// <param name="meshCacheSize"></param>
    /// <param name="nodeCacheSize"></param>
    public AssetCache(
        int imageCacheSize,
        int textureCacheSize,
        int materialCacheSize,
        int bufferCacheSize,
        int meshCacheSize,
        int nodeCacheSize,
        int animationCacheSize) {
      this.ImageCache = new Texture2D[imageCacheSize];
      this.ImageStreamCache = new Stream[imageCacheSize];
      this.TextureCache = new TextureCacheData[textureCacheSize];
      this.MaterialCache = new MaterialCacheData[materialCacheSize];
      this.BufferCache = new BufferCacheData[bufferCacheSize];
      this.MeshCache = new List<MeshCacheData[]>(meshCacheSize);
      for (var i = 0; i < meshCacheSize; ++i) {
        this.MeshCache.Add(null);
      }

      this.NodeCache = new GameObject[nodeCacheSize];
      this.AnimationCache = new AnimationCacheData[animationCacheSize];
    }

    public void Dispose() {
      this.ImageCache = null;
      this.ImageStreamCache = null;
      this.TextureCache = null;
      this.MaterialCache = null;
      if (this.BufferCache != null) {
        foreach (var bufferCacheData in this.BufferCache) {
          if (bufferCacheData != null) {
            if (bufferCacheData.Stream != null) {
              #if !WINDOWS_UWP
              bufferCacheData.Stream.Close();
              #else
							buffer.Stream.Dispose();
              #endif
            }

            bufferCacheData.Dispose();
          }
        }

        this.BufferCache = null;
      }

      this.MeshCache = null;
      this.AnimationCache = null;
    }
  }
}
