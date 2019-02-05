using System;
using System.Collections;
using UnityEngine;
using UnityGLTF.Loader;

namespace UnityGLTF.Examples {
  public class MultiSceneComponent : MonoBehaviour {
    public int SceneIndex = 0;
    public string Url;

    private GLTFSceneImporter _importer;
    private ILoader _loader;
    private string _fileName;

    void Start() {
      Debug.Log("Hit spacebar to change the scene.");

      var uri = new Uri(this.Url);
      var directoryPath = URIHelper.AbsoluteUriPath(uri);
      this._loader = new WebRequestLoader(directoryPath);
      this._fileName = URIHelper.GetFileFromUri(uri);

      this.StartCoroutine(this.LoadScene(this.SceneIndex));
    }

    void Update() {
      if (Input.GetKeyDown("space")) {
        this.SceneIndex = this.SceneIndex == 0 ? 1 : 0;
        Debug.LogFormat("Loading scene {0}", this.SceneIndex);
        this.StartCoroutine(this.LoadScene(this.SceneIndex));
      }
    }

    IEnumerator LoadScene(int SceneIndex) {
      foreach (Transform child in this.transform) {
        Destroy(child.gameObject);
      }

      this._importer = new GLTFSceneImporter(this._fileName, this._loader);

      this._importer.SceneParent = this.gameObject.transform;
      yield return this._importer.LoadScene(SceneIndex);
    }
  }
}
