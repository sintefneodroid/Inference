using GLTF.Schema;
using UnityEngine;

namespace Bundles.UnityGLTF.Scripts.Cache {
  public class MaterialCacheData {
    public Material UnityMaterial { get; set; }
    public Material UnityMaterialWithVertexColor { get; set; }
    public GLTFMaterial GLTFMaterial { get; set; }

    public Material GetContents(bool useVertexColors) {
      return useVertexColors ? this.UnityMaterialWithVertexColor : this.UnityMaterial;
    }

    /// <summary>
    /// Unloads the materials in this cache.
    /// </summary>
    public void Unload() {
      if (this.UnityMaterial != null) {
        Object.Destroy(this.UnityMaterial);
      }

      if (this.UnityMaterialWithVertexColor != null) {
        Object.Destroy(this.UnityMaterialWithVertexColor);
      }
    }
  }
}
