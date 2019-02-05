using System.IO;
using GLTF;
using UnityEngine;
using System;
using System.Collections;

#if WINDOWS_UWP
using System.Threading.Tasks;
#endif

namespace UnityGLTF.Loader {
  public class FileLoader : ILoader {
    private string _rootDirectoryPath;
    public Stream LoadedStream { get; private set; }

    public bool HasSyncLoadMethod { get; private set; }

    public FileLoader(string rootDirectoryPath) {
      this._rootDirectoryPath = rootDirectoryPath;
      this.HasSyncLoadMethod = true;
    }

    public IEnumerator LoadStream(string gltfFilePath) {
      if (gltfFilePath == null) {
        throw new ArgumentNullException("gltfFilePath");
      }

      yield return this.LoadFileStream(this._rootDirectoryPath, gltfFilePath);
    }

    private IEnumerator LoadFileStream(string rootPath, string fileToLoad) {
      var pathToLoad = Path.Combine(rootPath, fileToLoad);
      Debug.Log($"Loading path {pathToLoad}");
      if (!File.Exists(pathToLoad)) {
        throw new FileNotFoundException("Buffer file not found", fileToLoad);
      }

      yield return null;
      this.LoadedStream = File.OpenRead(pathToLoad);
    }

    public void LoadStreamSync(string gltfFilePath) {
      if (gltfFilePath == null) {
        throw new ArgumentNullException("gltfFilePath");
      }

      this.LoadFileStreamSync(this._rootDirectoryPath, gltfFilePath);
    }

    private void LoadFileStreamSync(string rootPath, string fileToLoad) {
      var pathToLoad = Path.Combine(rootPath, fileToLoad);
      if (!File.Exists(pathToLoad)) {
        throw new FileNotFoundException("Buffer file not found", fileToLoad);
      }

      this.LoadedStream = File.OpenRead(pathToLoad);
    }
  }
}
