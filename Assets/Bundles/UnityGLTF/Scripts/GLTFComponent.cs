using System;
using System.Collections;
using System.IO;
using GLTF;
using GLTF.Schema;
using UnityEngine;
using UnityGLTF.Loader;

namespace UnityGLTF {
  /// <summary>
  /// Component to load a GLTF scene with
  /// </summary>
  public class GLTFComponent : MonoBehaviour {
    public string GLTFUri = null;
    public bool Multithreaded = true;
    public bool UseStream = false;

    [SerializeField] private bool loadOnStart = true;

    public int MaximumLod = 300;
    public int Timeout = 8;
    public GLTFSceneImporter.ColliderType Collider = GLTFSceneImporter.ColliderType.None;

    [SerializeField] private string base_uri_path = Application.streamingAssetsPath;
    [SerializeField] private Shader shaderOverride = null;

    IEnumerator Start() {
      if (this.loadOnStart) {
        yield return this.Load();
      }
    }

    public IEnumerator Load() {
      GLTFSceneImporter sceneImporter = null;
      ILoader loader = null;
      try {
        if (this.UseStream) {
          // Path.Combine treats paths that start with the separator character
          // as absolute paths, ignoring the first path passed in. This removes
          // that character to properly handle a filename written with it.
          this.GLTFUri = this.GLTFUri.TrimStart(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar});
          var fullPath = Path.Combine(base_uri_path, this.GLTFUri);
          var directoryPath = URIHelper.GetDirectoryName(fullPath);
          loader = new FileLoader(directoryPath);
          sceneImporter = new GLTFSceneImporter(Path.GetFileName(this.GLTFUri), loader);
        } else {
          var directoryPath = URIHelper.GetDirectoryName(this.GLTFUri);
          loader = new WebRequestLoader(directoryPath);
          sceneImporter = new GLTFSceneImporter(URIHelper.GetFileFromUri(new Uri(this.GLTFUri)), loader);
        }

        sceneImporter.SceneParent = this.gameObject.transform;
        sceneImporter.Collider = this.Collider;
        sceneImporter.MaximumLod = this.MaximumLod;
        sceneImporter.Timeout = this.Timeout;
        sceneImporter.isMultithreaded = this.Multithreaded;
        sceneImporter.CustomShaderName = this.shaderOverride ? this.shaderOverride.name : null;
        yield return sceneImporter.LoadScene(-1);

        // Override the shaders on all materials if a shader is provided
        if (this.shaderOverride != null) {
          var renderers = this.gameObject.GetComponentsInChildren<Renderer>();
          foreach (var renderer_ in renderers) {
            renderer_.sharedMaterial.shader = this.shaderOverride;
          }
        }
      } finally {
        if (loader != null) {
          sceneImporter?.Dispose();
          sceneImporter = null;
          loader = null;
        }
      }
    }
  }
}
