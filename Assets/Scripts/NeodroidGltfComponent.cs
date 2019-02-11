/*using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Bundles.UnityGLTF.Scripts;
using Bundles.UnityGLTF.Scripts.Loader;
using GLTF;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Component to load a GLTF scene with
/// </summary>
public class NeodroidGltfComponent : MonoBehaviour
{
    [FormerlySerializedAs("GLTFUri")] public string gltfUri = null;
    public bool Multithreaded = true;
    public bool UseStream = false;
    public GameObject loaderNotication;
    public GameObject _loaded_ui;
    public Text _loader_text;

    [SerializeField] private bool loadOnStart = true;

    public int MaximumLod = 300;
    public int Timeout = 8;
    public GLTFSceneImporter.ColliderType c_Collider = GLTFSceneImporter.ColliderType.None;

    [SerializeField] private Shader shaderOverride = null;

    IEnumerator Start()
    {
        if (this.loadOnStart || Application.isEditor)
        {
            yield return this.Load();
        }
    }

    public IEnumerator Load()
    {
        GLTFSceneImporter sceneImporter = null;
        ILoader loader = null;

        if (loaderNotication)
            loaderNotication.SetActive(true);
        if (_loaded_ui)
            _loaded_ui.SetActive(false);

        try
        {
            if (this.UseStream){
                var directoryPath = URIHelper.GetDirectoryName(gltfUri);
                loader = new FileLoader(directoryPath);
                var file_name = Path.GetFileName(this.gltfUri);
                file_name = Regex.Replace(file_name , "[^\\w\\._]", "");
                Debug.Log($"Loading {file_name} from {directoryPath}");
                sceneImporter = new GLTFSceneImporter(file_name, loader);
            }else{
                var directoryPath = URIHelper.GetDirectoryName(this.gltfUri);
                loader = new WebRequestLoader(directoryPath);
                sceneImporter = new GLTFSceneImporter(URIHelper.GetFileFromUri(new Uri(this.gltfUri)), loader);
            }

            sceneImporter.SceneParent = this.gameObject.transform;
            sceneImporter.Collider = this.c_Collider;
            sceneImporter.MaximumLod = this.MaximumLod;
            sceneImporter.Timeout = this.Timeout;
            sceneImporter.isMultithreaded = this.Multithreaded;
            sceneImporter.CustomShaderName = this.shaderOverride ? this.shaderOverride.name : null;
            yield return sceneImporter.LoadScene(-1);

            // Override the shaders on all materials if a shader is provided
            if (this.shaderOverride != null)
            {
                var renderers = this.gameObject.GetComponentsInChildren<Renderer>();
                foreach (var renderer_ in renderers)
                {
                    renderer_.sharedMaterial.shader = this.shaderOverride;
                }
            }
        }
        catch (Exception e)
            {
                if (_loader_text)
                    _loader_text.text = $"Error: {e}";
            }
        finally
        {
            if (loader != null)
            {
                sceneImporter?.Dispose();
                sceneImporter = null;
                loader = null;
            }
        }

        if (loaderNotication)
            loaderNotication.SetActive(false);
        if (_loaded_ui)
            _loaded_ui.SetActive(true);
    }
}*/