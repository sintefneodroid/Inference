using GLTF.Schema;
using UnityEngine;

namespace Bundles.UnityGLTF.Scripts.Cache {
  public class TextureCacheData {
    public GLTFTexture TextureDefinition;
    public Texture Texture;

    /// <summary>
    /// Unloads the textures in this cache.
    /// </summary>
    public void Unload() { Object.Destroy(this.Texture); }
  }
}
