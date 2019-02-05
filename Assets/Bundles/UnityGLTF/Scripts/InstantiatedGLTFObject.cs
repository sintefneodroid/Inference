using UnityEngine;
using UnityGLTF.Cache;

namespace UnityGLTF {
  /// <summary>
  /// Instantiated GLTF Object component that gets added to the root of every GLTF game object created by a scene importer.
  /// </summary>
  public class InstantiatedGLTFObject : MonoBehaviour {
    /// <summary>
    /// Ref-counted cache data for this object.
    /// The same instance of this cached data will be used for all copies of this GLTF object,
    /// and the data gets cleaned up when the ref counts goes to 0.
    /// </summary>
    private RefCountedCacheData cachedData;

    public RefCountedCacheData CachedData {
      get { return this.cachedData; }
      set {
        if (this.cachedData != value) {
          if (this.cachedData != null) {
            this.cachedData.DecreaseRefCount();
          }

          this.cachedData = value;

          if (this.cachedData != null) {
            this.cachedData.IncreaseRefCount();
          }
        }
      }
    }

    /// <summary>
    /// Duplicates the instantiated GLTF object.
    /// Note that this should always be called if you intend to create a new instance of a GLTF object, 
    /// in order to properly preserve the ref count of the dynamically loaded mesh data, otherwise
    /// you will run into a memory leak due to non-destroyed meshes, textures and materials.
    /// </summary>
    /// <returns></returns>
    public InstantiatedGLTFObject Duplicate() {
      var duplicatedObject = Instantiate(this.gameObject);

      var newGltfObjectComponent = duplicatedObject.GetComponent<InstantiatedGLTFObject>();
      newGltfObjectComponent.CachedData = this.CachedData;

      return newGltfObjectComponent;
    }

    private void OnDestroy() { this.CachedData = null; }
  }
}
