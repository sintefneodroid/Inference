using System.Collections;
using Bundles.UnityGLTF.Scripts;
using Bundles.UnityGLTF.Scripts.Loader;
using GLTF;
using GLTF.Schema;
using UnityEngine;

namespace Bundles.UnityGLTF.Examples {
  public class RootMergeComponent : MonoBehaviour {
    public string asset0Path;
    public string asset1Path;
    public bool Multithreaded = true;

    public int MaximumLod = 300;

    // todo undo
    #if !WINDOWS_UWP
    IEnumerator Start() {
      var fullPath0 = Application.streamingAssetsPath + System.IO.Path.DirectorySeparatorChar + this.asset0Path;
      ILoader loader0 = new FileLoader(URIHelper.GetDirectoryName(fullPath0));

      var fullPath1 = Application.streamingAssetsPath + System.IO.Path.DirectorySeparatorChar + this.asset1Path;
      ILoader loader1 = new FileLoader(URIHelper.GetDirectoryName(fullPath1));

      yield return loader0.LoadStream(System.IO.Path.GetFileName(this.asset0Path));
      var asset0Stream = loader0.LoadedStream;
      GLTFRoot asset0Root;
      GLTFParser.ParseJson(asset0Stream, out asset0Root);

      yield return loader1.LoadStream(System.IO.Path.GetFileName(this.asset1Path));
      var asset1Stream = loader1.LoadedStream;
      GLTFRoot asset1Root;
      GLTFParser.ParseJson(asset0Stream, out asset1Root);

      var newPath = "../../" + URIHelper.GetDirectoryName(this.asset0Path);

      var previousBufferCount = asset1Root.Buffers.Count;
      var previousImageCount = asset1Root.Images.Count;
      var previousSceneCounter = asset1Root.Scenes.Count;
      GLTFHelpers.MergeGLTF(asset1Root, asset0Root);

      for (var i = previousBufferCount; i < asset1Root.Buffers.Count; ++i) {
        var buffer = asset1Root.Buffers[i];
        if (!URIHelper.IsBase64Uri(buffer.Uri)) {
          buffer.Uri = newPath + buffer.Uri;
        }
      }

      for (var i = previousImageCount; i < asset1Root.Images.Count; ++i) {
        var image = asset1Root.Images[i];
        if (!URIHelper.IsBase64Uri(image.Uri)) {
          image.Uri = newPath + image.Uri;
        }
      }

      foreach (var node in asset1Root.Scenes[asset0Root.Scene.Id + previousSceneCounter].Nodes) {
        node.Value.Translation.X += 5f;
        asset1Root.Scene.Value.Nodes.Add(node);
      }

      var importer = new GLTFSceneImporter(asset1Root, loader1);

      importer.MaximumLod = this.MaximumLod;
      importer.isMultithreaded = this.Multithreaded;
      yield return importer.LoadScene(-1);
    }
    #endif
  }
}
