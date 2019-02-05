using System.IO;
using UnityEngine;

namespace UnityGLTF.Examples {
  public class GLTFExporterTest : MonoBehaviour {
    public string RetrieveTexturePath(Texture texture) { return texture.name; }

    // Use this for initialization
    void Awake() {
      var exporter = new GLTFSceneExporter(new[] {this.transform}, this.RetrieveTexturePath);
      var appPath = Application.dataPath;
      var wwwPath = appPath.Substring(0, appPath.LastIndexOf("Assets")) + "www";
      exporter.SaveGLTFandBin(Path.Combine(wwwPath, "TestScene"), "TestScene");
    }
  }
}
