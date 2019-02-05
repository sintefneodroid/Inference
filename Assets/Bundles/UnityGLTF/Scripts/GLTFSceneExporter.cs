using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.Extensions;
using CameraType = GLTF.Schema.CameraType;
using Object = UnityEngine.Object;
using WrapMode = GLTF.Schema.WrapMode;

namespace UnityGLTF {
  public class GLTFSceneExporter {
    public delegate string RetrieveTexturePathDelegate(Texture texture);

    private enum IMAGETYPE {
      RGB,
      RGBA,
      R,
      G,
      B,
      A,
      G_INVERT
    }

    private enum TextureMapType {
      Main,
      Bump,
      SpecGloss,
      Emission,
      MetallicGloss,
      Light,
      Occlusion
    }

    private struct ImageInfo {
      public Texture2D texture;
      public TextureMapType textureMapType;
    }

    private Transform[] _rootTransforms;
    private GLTFRoot _root;
    private BufferId _bufferId;
    private GLTFBuffer _buffer;
    private BinaryWriter _bufferWriter;
    private List<ImageInfo> _imageInfos;
    private List<Texture> _textures;
    private List<Material> _materials;

    private RetrieveTexturePathDelegate _retrieveTexturePathDelegate;

    private Material _metalGlossChannelSwapMaterial;
    private Material _normalChannelMaterial;

    private const uint MagicGLTF = 0x46546C67;
    private const uint Version = 2;
    private const uint MagicJson = 0x4E4F534A;
    private const uint MagicBin = 0x004E4942;
    private const int GLTFHeaderSize = 12;
    private const int SectionHeaderSize = 8;

    protected struct PrimKey {
      public Mesh Mesh;
      public Material Material;
    }

    private readonly Dictionary<PrimKey, MeshId> _primOwner = new Dictionary<PrimKey, MeshId>();
    private readonly Dictionary<Mesh, MeshPrimitive[]> _meshToPrims = new Dictionary<Mesh, MeshPrimitive[]>();

    // Settings
    public static bool ExportNames = true;
    public static bool ExportFullPath = true;
    public static bool RequireExtensions = false;

    /// <summary>
    /// Create a GLTFExporter that exports out a transform
    /// </summary>
    /// <param name="rootTransforms">Root transform of object to export</param>
    public GLTFSceneExporter(Transform[] rootTransforms, RetrieveTexturePathDelegate retrieveTexturePathDelegate) {
      this._retrieveTexturePathDelegate = retrieveTexturePathDelegate;

      var metalGlossChannelSwapShader = Resources.Load("MetalGlossChannelSwap", typeof(Shader)) as Shader;
      this._metalGlossChannelSwapMaterial = new Material(metalGlossChannelSwapShader);

      var normalChannelShader = Resources.Load("NormalChannel", typeof(Shader)) as Shader;
      this._normalChannelMaterial = new Material(normalChannelShader);

      this._rootTransforms = rootTransforms;
      this._root = new GLTFRoot {
          Accessors = new List<Accessor>(),
          Asset = new Asset {Version = "2.0"},
          Buffers = new List<GLTFBuffer>(),
          BufferViews = new List<BufferView>(),
          Cameras = new List<GLTFCamera>(),
          Images = new List<GLTFImage>(),
          Materials = new List<GLTFMaterial>(),
          Meshes = new List<GLTFMesh>(),
          Nodes = new List<Node>(),
          Samplers = new List<Sampler>(),
          Scenes = new List<GLTFScene>(),
          Textures = new List<GLTFTexture>()
      };

      this._imageInfos = new List<ImageInfo>();
      this._materials = new List<Material>();
      this._textures = new List<Texture>();

      this._buffer = new GLTFBuffer();
      this._bufferId = new BufferId {Id = this._root.Buffers.Count, Root = this._root};
      this._root.Buffers.Add(this._buffer);
    }

    /// <summary>
    /// Gets the root object of the exported GLTF
    /// </summary>
    /// <returns>Root parsed GLTF Json</returns>
    public GLTFRoot GetRoot() { return this._root; }

    /// <summary>
    /// Writes a binary GLB file with filename at path.
    /// </summary>
    /// <param name="path">File path for saving the binary file</param>
    /// <param name="fileName">The name of the GLTF file</param>
    public void SaveGLB(string path, string fileName) {
      Stream binStream = new MemoryStream();
      Stream jsonStream = new MemoryStream();

      this._bufferWriter = new BinaryWriter(binStream);

      TextWriter jsonWriter = new StreamWriter(jsonStream, Encoding.ASCII);

      this._root.Scene = this.ExportScene(fileName, this._rootTransforms);

      this._buffer.ByteLength = (UInt32)this._bufferWriter.BaseStream.Length;

      this._root.Serialize(jsonWriter);

      this._bufferWriter.Flush();
      jsonWriter.Flush();

      // align to 4-byte boundary to comply with spec.
      AlignToBoundary(jsonStream);
      AlignToBoundary(binStream, 0x00);

      var glbLength =
          (int)(GLTFHeaderSize + SectionHeaderSize + jsonStream.Length + SectionHeaderSize + binStream.Length);

      var fullPath = Path.Combine(path, Path.ChangeExtension(fileName, "glb"));

      using (var glbFile = new FileStream(fullPath, FileMode.Create)) {
        var writer = new BinaryWriter(glbFile);

        // write header
        writer.Write(MagicGLTF);
        writer.Write(Version);
        writer.Write(glbLength);

        // write JSON chunk header.
        writer.Write((int)jsonStream.Length);
        writer.Write(MagicJson);

        jsonStream.Position = 0;
        CopyStream(jsonStream, writer);

        writer.Write((int)binStream.Length);
        writer.Write(MagicBin);

        binStream.Position = 0;
        CopyStream(binStream, writer);

        writer.Flush();
      }

      this.ExportImages(path);
    }

    /// <summary>
    /// Convenience function to copy from a stream to a binary writer, for
    /// compatibility with pre-.NET 4.0.
    /// Note: Does not set position/seek in either stream. After executing,
    /// the input buffer's position should be the end of the stream.
    /// </summary>
    /// <param name="input">Stream to copy from</param>
    /// <param name="output">Stream to copy to.</param>
    private static void CopyStream(Stream input, BinaryWriter output) {
      var buffer = new byte[8 * 1024];
      int length;
      while ((length = input.Read(buffer, 0, buffer.Length)) > 0) {
        output.Write(buffer, 0, length);
      }
    }

    /// <summary>
    /// Pads a stream with additional bytes.
    /// </summary>
    /// <param name="stream">The stream to be modified.</param>
    /// <param name="pad">The padding byte to append. Defaults to ASCII
    /// space (' ').</param>
    /// <param name="boundary">The boundary to align with, in bytes.
    /// </param>
    private static void AlignToBoundary(Stream stream, byte pad = (byte)' ', uint boundary = 4) {
      var currentLength = (uint)stream.Length;
      var newLength = CalculateAlignment(currentLength, boundary);
      for (var i = 0; i < newLength - currentLength; i++) {
        stream.WriteByte(pad);
      }
    }

    /// <summary>
    /// Calculates the number of bytes of padding required to align the
    /// size of a buffer with some multiple of byteAllignment.
    /// </summary>
    /// <param name="currentSize">The current size of the buffer.</param>
    /// <param name="byteAlignment">The number of bytes to align with.</param>
    /// <returns></returns>
    public static uint CalculateAlignment(uint currentSize, uint byteAlignment) {
      return (currentSize + byteAlignment - 1) / byteAlignment * byteAlignment;
    }

    /// <summary>
    /// Specifies the path and filename for the GLTF Json and binary
    /// </summary>
    /// <param name="path">File path for saving the GLTF and binary files</param>
    /// <param name="fileName">The name of the GLTF file</param>
    public void SaveGLTFandBin(string path, string fileName) {
      var binFile = File.Create(Path.Combine(path, fileName + ".bin"));
      this._bufferWriter = new BinaryWriter(binFile);

      this._root.Scene = this.ExportScene(fileName, this._rootTransforms);

      this._buffer.Uri = fileName + ".bin";
      this._buffer.ByteLength = (UInt32)this._bufferWriter.BaseStream.Length;

      var gltfFile = File.CreateText(Path.Combine(path, fileName + ".gltf"));
      this._root.Serialize(gltfFile);

      #if WINDOWS_UWP
			gltfFile.Dispose();
			binFile.Dispose();
      #else
      gltfFile.Close();
      binFile.Close();
      #endif
      this.ExportImages(path);
    }

    private void ExportImages(string outputPath) {
      for (var t = 0; t < this._imageInfos.Count; ++t) {
        var image = this._imageInfos[t].texture;
        var height = image.height;
        var width = image.width;

        switch (this._imageInfos[t].textureMapType) {
          case TextureMapType.MetallicGloss:
            this.ExportMetallicGlossTexture(image, outputPath);
            break;
          case TextureMapType.Bump:
            this.ExportNormalTexture(image, outputPath);
            break;
          default:
            this.ExportTexture(image, outputPath);
            break;
        }
      }
    }

    /// <summary>
    /// This converts Unity's metallic-gloss texture representation into GLTF's metallic-roughness specifications. 
    /// Unity's metallic-gloss A channel (glossiness) is inverted and goes into GLTF's metallic-roughness G channel (roughness).
    /// Unity's metallic-gloss R channel (metallic) goes into GLTF's metallic-roughess B channel.
    /// </summary>
    /// <param name="texture">Unity's metallic-gloss texture to be exported</param>
    /// <param name="outputPath">The location to export the texture</param>
    private void ExportMetallicGlossTexture(Texture2D texture, string outputPath) {
      var destRenderTexture = RenderTexture.GetTemporary(
          texture.width,
          texture.height,
          0,
          RenderTextureFormat.ARGB32,
          RenderTextureReadWrite.Linear);

      Graphics.Blit(texture, destRenderTexture, this._metalGlossChannelSwapMaterial);

      var exportTexture = new Texture2D(texture.width, texture.height);
      exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
      exportTexture.Apply();

      var finalFilenamePath = this.ConstructImageFilenamePath(texture, outputPath);
      File.WriteAllBytes(finalFilenamePath, exportTexture.EncodeToPNG());

      destRenderTexture.Release();
      if (Application.isEditor) {
        Object.DestroyImmediate(exportTexture);
      } else {
        Object.Destroy(exportTexture);
      }
    }

    /// <summary>
    /// This export's the normal texture. If a texture is marked as a normal map, the values are stored in the A and G channel.
    /// To output the correct normal texture, the A channel is put into the R channel.
    /// </summary>
    /// <param name="texture">Unity's normal texture to be exported</param>
    /// <param name="outputPath">The location to export the texture</param>
    private void ExportNormalTexture(Texture2D texture, string outputPath) {
      var destRenderTexture = RenderTexture.GetTemporary(
          texture.width,
          texture.height,
          0,
          RenderTextureFormat.ARGB32,
          RenderTextureReadWrite.Linear);

      Graphics.Blit(texture, destRenderTexture, this._normalChannelMaterial);

      var exportTexture = new Texture2D(texture.width, texture.height);
      exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
      exportTexture.Apply();

      var finalFilenamePath = this.ConstructImageFilenamePath(texture, outputPath);
      File.WriteAllBytes(finalFilenamePath, exportTexture.EncodeToPNG());

      destRenderTexture.Release();

      if (Application.isEditor) {
        Object.DestroyImmediate(exportTexture);
      } else {
        Object.Destroy(exportTexture);
      }
    }

    private void ExportTexture(Texture2D texture, string outputPath) {
      var destRenderTexture = RenderTexture.GetTemporary(
          texture.width,
          texture.height,
          0,
          RenderTextureFormat.ARGB32,
          RenderTextureReadWrite.sRGB);

      Graphics.Blit(texture, destRenderTexture);

      var exportTexture = new Texture2D(texture.width, texture.height);
      exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
      exportTexture.Apply();

      var finalFilenamePath = this.ConstructImageFilenamePath(texture, outputPath);
      File.WriteAllBytes(finalFilenamePath, exportTexture.EncodeToPNG());

      destRenderTexture.Release();
      if (Application.isEditor) {
        Object.DestroyImmediate(exportTexture);
      } else {
        Object.Destroy(exportTexture);
      }
    }

    private string ConstructImageFilenamePath(Texture2D texture, string outputPath) {
      var imagePath = this._retrieveTexturePathDelegate(texture);
      var filenamePath = Path.Combine(outputPath, imagePath);
      if (!ExportFullPath) {
        filenamePath = outputPath + "/" + texture.name;
      }

      var file = new FileInfo(filenamePath);
      file.Directory.Create();
      return Path.ChangeExtension(filenamePath, ".png");
    }

    private SceneId ExportScene(string name, Transform[] rootObjTransforms) {
      var scene = new GLTFScene();

      if (ExportNames) {
        scene.Name = name;
      }

      scene.Nodes = new List<NodeId>(rootObjTransforms.Length);
      foreach (var transform in rootObjTransforms) {
        scene.Nodes.Add(this.ExportNode(transform));
      }

      this._root.Scenes.Add(scene);

      return new SceneId {Id = this._root.Scenes.Count - 1, Root = this._root};
    }

    private NodeId ExportNode(Transform nodeTransform) {
      var node = new Node();

      if (ExportNames) {
        node.Name = nodeTransform.name;
      }

      //export camera attached to node
      var unityCamera = nodeTransform.GetComponent<Camera>();
      if (unityCamera != null) {
        node.Camera = this.ExportCamera(unityCamera);
      }

      node.SetUnityTransform(nodeTransform);

      var id = new NodeId {Id = this._root.Nodes.Count, Root = this._root};
      this._root.Nodes.Add(node);

      // children that are primitives get put in a mesh
      GameObject[] primitives, nonPrimitives;
      this.FilterPrimitives(nodeTransform, out primitives, out nonPrimitives);
      if (primitives.Length > 0) {
        node.Mesh = this.ExportMesh(nodeTransform.name, primitives);

        // associate unity meshes with gltf mesh id
        foreach (var prim in primitives) {
          var filter = prim.GetComponent<MeshFilter>();
          var renderer = prim.GetComponent<MeshRenderer>();
          this._primOwner[new PrimKey {Mesh = filter.sharedMesh, Material = renderer.sharedMaterial}] = node.Mesh;
        }
      }

      // children that are not primitives get added as child nodes
      if (nonPrimitives.Length > 0) {
        node.Children = new List<NodeId>(nonPrimitives.Length);
        foreach (var child in nonPrimitives) {
          node.Children.Add(this.ExportNode(child.transform));
        }
      }

      return id;
    }

    private CameraId ExportCamera(Camera unityCamera) {
      var camera = new GLTFCamera();
      //name
      camera.Name = unityCamera.name;

      //type
      var isOrthographic = unityCamera.orthographic;
      camera.Type = isOrthographic ? CameraType.orthographic : CameraType.perspective;
      var matrix = unityCamera.projectionMatrix;

      //matrix properties: compute the fields from the projection matrix
      if (isOrthographic) {
        var ortho = new CameraOrthographic();

        ortho.XMag = 1 / matrix[0, 0];
        ortho.YMag = 1 / matrix[1, 1];

        var farClip = (matrix[2, 3] / matrix[2, 2]) - (1 / matrix[2, 2]);
        var nearClip = farClip + (2 / matrix[2, 2]);
        ortho.ZFar = farClip;
        ortho.ZNear = nearClip;

        camera.Orthographic = ortho;
      } else {
        var perspective = new CameraPerspective();
        var fov = 2 * Mathf.Atan(1 / matrix[1, 1]);
        var aspectRatio = matrix[1, 1] / matrix[0, 0];
        perspective.YFov = fov;
        perspective.AspectRatio = aspectRatio;

        if (matrix[2, 2] == -1) {
          //infinite projection matrix
          var nearClip = matrix[2, 3] * -0.5f;
          perspective.ZNear = nearClip;
        } else {
          //finite projection matrix
          var farClip = matrix[2, 3] / (matrix[2, 2] + 1);
          var nearClip = farClip * (matrix[2, 2] + 1) / (matrix[2, 2] - 1);
          perspective.ZFar = farClip;
          perspective.ZNear = nearClip;
        }

        camera.Perspective = perspective;
      }

      var id = new CameraId {Id = this._root.Cameras.Count, Root = this._root};

      this._root.Cameras.Add(camera);

      return id;
    }

    private void FilterPrimitives(Transform transform, out GameObject[] primitives, out GameObject[] nonPrimitives) {
      var childCount = transform.childCount;
      var prims = new List<GameObject>(childCount + 1);
      var nonPrims = new List<GameObject>(childCount);

      // add another primitive if the root object also has a mesh
      if (transform.gameObject.GetComponent<MeshFilter>() != null
          && transform.gameObject.GetComponent<MeshRenderer>() != null) {
        prims.Add(transform.gameObject);
      }

      for (var i = 0; i < childCount; i++) {
        var go = transform.GetChild(i).gameObject;
        if (IsPrimitive(go))
          prims.Add(go);
        else
          nonPrims.Add(go);
      }

      primitives = prims.ToArray();
      nonPrimitives = nonPrims.ToArray();
    }

    private static bool IsPrimitive(GameObject gameObject) {
      /*
       * Primitives have the following properties:
       * - have no children
       * - have no non-default local transform properties
       * - have MeshFilter and MeshRenderer components
       */
      return gameObject.transform.childCount == 0
             && gameObject.transform.localPosition == Vector3.zero
             && gameObject.transform.localRotation == Quaternion.identity
             && gameObject.transform.localScale == Vector3.one
             && gameObject.GetComponent<MeshFilter>() != null
             && gameObject.GetComponent<MeshRenderer>() != null;
    }

    private MeshId ExportMesh(string name, GameObject[] primitives) {
      // check if this set of primitives is already a mesh
      MeshId existingMeshId = null;
      var key = new PrimKey();
      foreach (var prim in primitives) {
        var filter = prim.GetComponent<MeshFilter>();
        var renderer = prim.GetComponent<MeshRenderer>();
        key.Mesh = filter.sharedMesh;
        key.Material = renderer.sharedMaterial;

        MeshId tempMeshId;
        if (this._primOwner.TryGetValue(key, out tempMeshId)
            && (existingMeshId == null || tempMeshId == existingMeshId)) {
          existingMeshId = tempMeshId;
        } else {
          existingMeshId = null;
          break;
        }
      }

      // if so, return that mesh id
      if (existingMeshId != null) {
        return existingMeshId;
      }

      // if not, create new mesh and return its id
      var mesh = new GLTFMesh();

      if (ExportNames) {
        mesh.Name = name;
      }

      mesh.Primitives = new List<MeshPrimitive>(primitives.Length);
      foreach (var prim in primitives) {
        mesh.Primitives.AddRange(this.ExportPrimitive(prim));
      }

      var id = new MeshId {Id = this._root.Meshes.Count, Root = this._root};
      this._root.Meshes.Add(mesh);

      return id;
    }

    // a mesh *might* decode to multiple prims if there are submeshes
    private MeshPrimitive[] ExportPrimitive(GameObject gameObject) {
      var filter = gameObject.GetComponent<MeshFilter>();
      var meshObj = filter.sharedMesh;

      var renderer = gameObject.GetComponent<MeshRenderer>();
      var materialsObj = renderer.sharedMaterials;

      var prims = new MeshPrimitive[meshObj.subMeshCount];

      // don't export any more accessors if this mesh is already exported
      MeshPrimitive[] primVariations;
      if (this._meshToPrims.TryGetValue(meshObj, out primVariations) && meshObj.subMeshCount == primVariations.Length) {
        for (var i = 0; i < primVariations.Length; i++) {
          prims[i] = new MeshPrimitive(primVariations[i], this._root) {Material = this.ExportMaterial(materialsObj[i])};
        }

        return prims;
      }

      AccessorId aPosition = null,
                 aNormal = null,
                 aTangent = null,
                 aTexcoord0 = null,
                 aTexcoord1 = null,
                 aColor0 = null;

      aPosition = this.ExportAccessor(
          SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(
              meshObj.vertices,
              SchemaExtensions.CoordinateSpaceConversionScale));

      if (meshObj.normals.Length != 0)
        aNormal = this.ExportAccessor(
            SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(
                meshObj.normals,
                SchemaExtensions.CoordinateSpaceConversionScale));

      if (meshObj.tangents.Length != 0)
        aTangent = this.ExportAccessor(
            SchemaExtensions.ConvertVector4CoordinateSpaceAndCopy(
                meshObj.tangents,
                SchemaExtensions.TangentSpaceConversionScale));

      if (meshObj.uv.Length != 0)
        aTexcoord0 = this.ExportAccessor(SchemaExtensions.FlipTexCoordArrayVAndCopy(meshObj.uv));

      if (meshObj.uv2.Length != 0)
        aTexcoord1 = this.ExportAccessor(SchemaExtensions.FlipTexCoordArrayVAndCopy(meshObj.uv2));

      if (meshObj.colors.Length != 0)
        aColor0 = this.ExportAccessor(meshObj.colors);

      MaterialId lastMaterialId = null;

      for (var submesh = 0; submesh < meshObj.subMeshCount; submesh++) {
        var primitive = new MeshPrimitive();

        var triangles = meshObj.GetTriangles(submesh);
        primitive.Indices = this.ExportAccessor(SchemaExtensions.FlipFacesAndCopy(triangles), true);

        primitive.Attributes = new Dictionary<string, AccessorId>();
        primitive.Attributes.Add(SemanticProperties.POSITION, aPosition);

        if (aNormal != null)
          primitive.Attributes.Add(SemanticProperties.NORMAL, aNormal);
        if (aTangent != null)
          primitive.Attributes.Add(SemanticProperties.TANGENT, aTangent);
        if (aTexcoord0 != null)
          primitive.Attributes.Add(SemanticProperties.TexCoord(0), aTexcoord0);
        if (aTexcoord1 != null)
          primitive.Attributes.Add(SemanticProperties.TexCoord(1), aTexcoord1);
        if (aColor0 != null)
          primitive.Attributes.Add(SemanticProperties.Color(0), aColor0);

        if (submesh < materialsObj.Length) {
          primitive.Material = this.ExportMaterial(materialsObj[submesh]);
          lastMaterialId = primitive.Material;
        } else {
          primitive.Material = lastMaterialId;
        }

        prims[submesh] = primitive;
      }

      this._meshToPrims[meshObj] = prims;

      return prims;
    }

    private MaterialId ExportMaterial(Material materialObj) {
      var id = this.GetMaterialId(this._root, materialObj);
      if (id != null) {
        return id;
      }

      var material = new GLTFMaterial();

      if (ExportNames) {
        material.Name = materialObj.name;
      }

      if (materialObj.HasProperty("_Cutoff")) {
        material.AlphaCutoff = materialObj.GetFloat("_Cutoff");
      }

      switch (materialObj.GetTag("RenderType", false, "")) {
        case "TransparentCutout":
          material.AlphaMode = AlphaMode.MASK;
          break;
        case "Transparent":
          material.AlphaMode = AlphaMode.BLEND;
          break;
        default:
          material.AlphaMode = AlphaMode.OPAQUE;
          break;
      }

      material.DoubleSided = materialObj.HasProperty("_Cull") && materialObj.GetInt("_Cull") == (float)CullMode.Off;

      if (materialObj.HasProperty("_EmissionColor")) {
        material.EmissiveFactor = materialObj.GetColor("_EmissionColor").ToNumericsColorRaw();
      }

      if (materialObj.HasProperty("_EmissionMap")) {
        var emissionTex = materialObj.GetTexture("_EmissionMap");

        if (emissionTex != null) {
          material.EmissiveTexture = this.ExportTextureInfo(emissionTex, TextureMapType.Emission);

          this.ExportTextureTransform(material.EmissiveTexture, materialObj, "_EmissionMap");
        }
      }

      if (materialObj.HasProperty("_BumpMap")) {
        var normalTex = materialObj.GetTexture("_BumpMap");

        if (normalTex != null) {
          material.NormalTexture = this.ExportNormalTextureInfo(normalTex, TextureMapType.Bump, materialObj);
          this.ExportTextureTransform(material.NormalTexture, materialObj, "_BumpMap");
        }
      }

      if (materialObj.HasProperty("_OcclusionMap")) {
        var occTex = materialObj.GetTexture("_OcclusionMap");
        if (occTex != null) {
          material.OcclusionTexture = this.ExportOcclusionTextureInfo(occTex, TextureMapType.Occlusion, materialObj);
          this.ExportTextureTransform(material.OcclusionTexture, materialObj, "_OcclusionMap");
        }
      }

      if (this.IsPBRMetallicRoughness(materialObj)) {
        material.PbrMetallicRoughness = this.ExportPBRMetallicRoughness(materialObj);
      } else if (this.IsCommonConstant(materialObj)) {
        material.CommonConstant = this.ExportCommonConstant(materialObj);
      }

      this._materials.Add(materialObj);

      id = new MaterialId {Id = this._root.Materials.Count, Root = this._root};
      this._root.Materials.Add(material);

      return id;
    }

    private bool IsPBRMetallicRoughness(Material material) {
      return material.HasProperty("_Metallic") && material.HasProperty("_MetallicGlossMap");
    }

    private bool IsCommonConstant(Material material) {
      return material.HasProperty("_AmbientFactor")
             && material.HasProperty("_LightMap")
             && material.HasProperty("_LightFactor");
    }

    private void ExportTextureTransform(TextureInfo def, Material mat, string texName) {
      var offset = mat.GetTextureOffset(texName);
      var scale = mat.GetTextureScale(texName);

      if (offset == Vector2.zero && scale == Vector2.one) return;

      if (this._root.ExtensionsUsed == null) {
        this._root.ExtensionsUsed = new List<string>(new[] {ExtTextureTransformExtensionFactory.EXTENSION_NAME});
      } else if (!this._root.ExtensionsUsed.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME)) {
        this._root.ExtensionsUsed.Add(ExtTextureTransformExtensionFactory.EXTENSION_NAME);
      }

      if (RequireExtensions) {
        if (this._root.ExtensionsRequired == null) {
          this._root.ExtensionsRequired = new List<string>(new[] {ExtTextureTransformExtensionFactory.EXTENSION_NAME});
        } else if (!this._root.ExtensionsRequired.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME)) {
          this._root.ExtensionsRequired.Add(ExtTextureTransformExtensionFactory.EXTENSION_NAME);
        }
      }

      if (def.Extensions == null)
        def.Extensions = new Dictionary<string, IExtension>();

      def.Extensions[ExtTextureTransformExtensionFactory.EXTENSION_NAME] = new ExtTextureTransformExtension(
          new GLTF.Math.Vector2(offset.x, -offset.y),
          new GLTF.Math.Vector2(scale.x, scale.y),
          0 // TODO: support UV channels
      );
    }

    private NormalTextureInfo ExportNormalTextureInfo(
        Texture texture,
        TextureMapType textureMapType,
        Material material) {
      var info = new NormalTextureInfo();

      info.Index = this.ExportTexture(texture, textureMapType);

      if (material.HasProperty("_BumpScale")) {
        info.Scale = material.GetFloat("_BumpScale");
      }

      return info;
    }

    private OcclusionTextureInfo ExportOcclusionTextureInfo(
        Texture texture,
        TextureMapType textureMapType,
        Material material) {
      var info = new OcclusionTextureInfo();

      info.Index = this.ExportTexture(texture, textureMapType);

      if (material.HasProperty("_OcclusionStrength")) {
        info.Strength = material.GetFloat("_OcclusionStrength");
      }

      return info;
    }

    private PbrMetallicRoughness ExportPBRMetallicRoughness(Material material) {
      var pbr = new PbrMetallicRoughness();

      if (material.HasProperty("_Color")) {
        pbr.BaseColorFactor = material.GetColor("_Color").ToNumericsColorRaw();
      }

      if (material.HasProperty("_MainTex")) {
        var mainTex = material.GetTexture("_MainTex");

        if (mainTex != null) {
          pbr.BaseColorTexture = this.ExportTextureInfo(mainTex, TextureMapType.Main);
          this.ExportTextureTransform(pbr.BaseColorTexture, material, "_MainTex");
        }
      }

      if (material.HasProperty("_Metallic")) {
        var metallicGlossMap = material.GetTexture("_MetallicGlossMap");
        pbr.MetallicFactor = (metallicGlossMap != null) ? 1.0 : material.GetFloat("_Metallic");
      }

      if (material.HasProperty("_Glossiness")) {
        var metallicGlossMap = material.GetTexture("_MetallicGlossMap");
        pbr.RoughnessFactor = (metallicGlossMap != null) ? 1.0 : material.GetFloat("_Glossiness");
      }

      if (material.HasProperty("_MetallicGlossMap")) {
        var mrTex = material.GetTexture("_MetallicGlossMap");

        if (mrTex != null) {
          pbr.MetallicRoughnessTexture = this.ExportTextureInfo(mrTex, TextureMapType.MetallicGloss);
          this.ExportTextureTransform(pbr.MetallicRoughnessTexture, material, "_MetallicGlossMap");
        }
      } else if (material.HasProperty("_SpecGlossMap")) {
        var mgTex = material.GetTexture("_SpecGlossMap");

        if (mgTex != null) {
          pbr.MetallicRoughnessTexture = this.ExportTextureInfo(mgTex, TextureMapType.SpecGloss);
          this.ExportTextureTransform(pbr.MetallicRoughnessTexture, material, "_SpecGlossMap");
        }
      }

      return pbr;
    }

    private MaterialCommonConstant ExportCommonConstant(Material materialObj) {
      if (this._root.ExtensionsUsed == null) {
        this._root.ExtensionsUsed = new List<string>(new[] {"KHR_materials_common"});
      } else if (!this._root.ExtensionsUsed.Contains("KHR_materials_common")) {
        this._root.ExtensionsUsed.Add("KHR_materials_common");
      }

      if (RequireExtensions) {
        if (this._root.ExtensionsRequired == null) {
          this._root.ExtensionsRequired = new List<string>(new[] {"KHR_materials_common"});
        } else if (!this._root.ExtensionsRequired.Contains("KHR_materials_common")) {
          this._root.ExtensionsRequired.Add("KHR_materials_common");
        }
      }

      var constant = new MaterialCommonConstant();

      if (materialObj.HasProperty("_AmbientFactor")) {
        constant.AmbientFactor = materialObj.GetColor("_AmbientFactor").ToNumericsColorRaw();
      }

      if (materialObj.HasProperty("_LightMap")) {
        var lmTex = materialObj.GetTexture("_LightMap");

        if (lmTex != null) {
          constant.LightmapTexture = this.ExportTextureInfo(lmTex, TextureMapType.Light);
          this.ExportTextureTransform(constant.LightmapTexture, materialObj, "_LightMap");
        }
      }

      if (materialObj.HasProperty("_LightFactor")) {
        constant.LightmapFactor = materialObj.GetColor("_LightFactor").ToNumericsColorRaw();
      }

      return constant;
    }

    private TextureInfo ExportTextureInfo(Texture texture, TextureMapType textureMapType) {
      var info = new TextureInfo();

      info.Index = this.ExportTexture(texture, textureMapType);

      return info;
    }

    private TextureId ExportTexture(Texture textureObj, TextureMapType textureMapType) {
      var id = this.GetTextureId(this._root, textureObj);
      if (id != null) {
        return id;
      }

      var texture = new GLTFTexture();

      //If texture name not set give it a unique name using count
      if (textureObj.name == "") {
        textureObj.name = (this._root.Textures.Count + 1).ToString();
      }

      if (ExportNames) {
        texture.Name = textureObj.name;
      }

      texture.Source = this.ExportImage(textureObj, textureMapType);
      texture.Sampler = this.ExportSampler(textureObj);

      this._textures.Add(textureObj);

      id = new TextureId {Id = this._root.Textures.Count, Root = this._root};

      this._root.Textures.Add(texture);

      return id;
    }

    private ImageId ExportImage(Texture texture, TextureMapType texturMapType) {
      var id = this.GetImageId(this._root, texture);
      if (id != null) {
        return id;
      }

      var image = new GLTFImage();

      if (ExportNames) {
        image.Name = texture.name;
      }

      this._imageInfos.Add(new ImageInfo {texture = texture as Texture2D, textureMapType = texturMapType});

      var imagePath = this._retrieveTexturePathDelegate(texture);
      var filenamePath = Path.ChangeExtension(imagePath, ".png");
      if (!ExportFullPath) {
        filenamePath = Path.ChangeExtension(texture.name, ".png");
      }

      image.Uri = Uri.EscapeUriString(filenamePath);

      id = new ImageId {Id = this._root.Images.Count, Root = this._root};

      this._root.Images.Add(image);

      return id;
    }

    private SamplerId ExportSampler(Texture texture) {
      var samplerId = this.GetSamplerId(this._root, texture);
      if (samplerId != null)
        return samplerId;

      var sampler = new Sampler();

      if (texture.wrapMode == TextureWrapMode.Clamp) {
        sampler.WrapS = WrapMode.ClampToEdge;
        sampler.WrapT = WrapMode.ClampToEdge;
      } else {
        sampler.WrapS = WrapMode.Repeat;
        sampler.WrapT = WrapMode.Repeat;
      }

      if (texture.filterMode == FilterMode.Point) {
        sampler.MinFilter = MinFilterMode.NearestMipmapNearest;
        sampler.MagFilter = MagFilterMode.Nearest;
      } else if (texture.filterMode == FilterMode.Bilinear) {
        sampler.MinFilter = MinFilterMode.NearestMipmapLinear;
        sampler.MagFilter = MagFilterMode.Linear;
      } else {
        sampler.MinFilter = MinFilterMode.LinearMipmapLinear;
        sampler.MagFilter = MagFilterMode.Linear;
      }

      samplerId = new SamplerId {Id = this._root.Samplers.Count, Root = this._root};

      this._root.Samplers.Add(sampler);

      return samplerId;
    }

    private AccessorId ExportAccessor(int[] arr, bool isIndices = false) {
      var count = arr.Length;

      if (count == 0) {
        throw new Exception("Accessors can not have a count of 0.");
      }

      var accessor = new Accessor();
      accessor.Count = (UInt32)count;
      accessor.Type = GLTFAccessorAttributeType.SCALAR;

      var min = arr[0];
      var max = arr[0];

      for (var i = 1; i < count; i++) {
        var cur = arr[i];

        if (cur < min) {
          min = cur;
        }

        if (cur > max) {
          max = cur;
        }
      }

      var byteOffset = this._bufferWriter.BaseStream.Position;

      if (max <= byte.MaxValue && min >= byte.MinValue) {
        accessor.ComponentType = GLTFComponentType.UnsignedByte;

        foreach (var v in arr) {
          this._bufferWriter.Write((byte)v);
        }
      } else if (max <= sbyte.MaxValue && min >= sbyte.MinValue && !isIndices) {
        accessor.ComponentType = GLTFComponentType.Byte;

        foreach (var v in arr) {
          this._bufferWriter.Write((sbyte)v);
        }
      } else if (max <= short.MaxValue && min >= short.MinValue && !isIndices) {
        accessor.ComponentType = GLTFComponentType.Short;

        foreach (var v in arr) {
          this._bufferWriter.Write((short)v);
        }
      } else if (max <= ushort.MaxValue && min >= ushort.MinValue) {
        accessor.ComponentType = GLTFComponentType.UnsignedShort;

        foreach (var v in arr) {
          this._bufferWriter.Write((ushort)v);
        }
      } else if (min >= uint.MinValue) {
        accessor.ComponentType = GLTFComponentType.UnsignedInt;

        foreach (var v in arr) {
          this._bufferWriter.Write((uint)v);
        }
      } else {
        accessor.ComponentType = GLTFComponentType.Float;

        foreach (var v in arr) {
          this._bufferWriter.Write((float)v);
        }
      }

      accessor.Min = new List<double> {min};
      accessor.Max = new List<double> {max};

      var byteLength = this._bufferWriter.BaseStream.Position - byteOffset;

      accessor.BufferView = this.ExportBufferView((int)byteOffset, (int)byteLength);

      var id = new AccessorId {Id = this._root.Accessors.Count, Root = this._root};
      this._root.Accessors.Add(accessor);

      return id;
    }

    private AccessorId ExportAccessor(Vector2[] arr) {
      var count = arr.Length;

      if (count == 0) {
        throw new Exception("Accessors can not have a count of 0.");
      }

      var accessor = new Accessor();
      accessor.ComponentType = GLTFComponentType.Float;
      accessor.Count = (UInt32)count;
      accessor.Type = GLTFAccessorAttributeType.VEC2;

      var minX = arr[0].x;
      var minY = arr[0].y;
      var maxX = arr[0].x;
      var maxY = arr[0].y;

      for (var i = 1; i < count; i++) {
        var cur = arr[i];

        if (cur.x < minX) {
          minX = cur.x;
        }

        if (cur.y < minY) {
          minY = cur.y;
        }

        if (cur.x > maxX) {
          maxX = cur.x;
        }

        if (cur.y > maxY) {
          maxY = cur.y;
        }
      }

      accessor.Min = new List<double> {minX, minY};
      accessor.Max = new List<double> {maxX, maxY};

      var byteOffset = this._bufferWriter.BaseStream.Position;

      foreach (var vec in arr) {
        this._bufferWriter.Write(vec.x);
        this._bufferWriter.Write(vec.y);
      }

      var byteLength = this._bufferWriter.BaseStream.Position - byteOffset;

      accessor.BufferView = this.ExportBufferView((int)byteOffset, (int)byteLength);

      var id = new AccessorId {Id = this._root.Accessors.Count, Root = this._root};
      this._root.Accessors.Add(accessor);

      return id;
    }

    private AccessorId ExportAccessor(Vector3[] arr) {
      var count = arr.Length;

      if (count == 0) {
        throw new Exception("Accessors can not have a count of 0.");
      }

      var accessor = new Accessor();
      accessor.ComponentType = GLTFComponentType.Float;
      accessor.Count = (UInt32)count;
      accessor.Type = GLTFAccessorAttributeType.VEC3;

      var minX = arr[0].x;
      var minY = arr[0].y;
      var minZ = arr[0].z;
      var maxX = arr[0].x;
      var maxY = arr[0].y;
      var maxZ = arr[0].z;

      for (var i = 1; i < count; i++) {
        var cur = arr[i];

        if (cur.x < minX) {
          minX = cur.x;
        }

        if (cur.y < minY) {
          minY = cur.y;
        }

        if (cur.z < minZ) {
          minZ = cur.z;
        }

        if (cur.x > maxX) {
          maxX = cur.x;
        }

        if (cur.y > maxY) {
          maxY = cur.y;
        }

        if (cur.z > maxZ) {
          maxZ = cur.z;
        }
      }

      accessor.Min = new List<double> {minX, minY, minZ};
      accessor.Max = new List<double> {maxX, maxY, maxZ};

      var byteOffset = this._bufferWriter.BaseStream.Position;

      foreach (var vec in arr) {
        this._bufferWriter.Write(vec.x);
        this._bufferWriter.Write(vec.y);
        this._bufferWriter.Write(vec.z);
      }

      var byteLength = this._bufferWriter.BaseStream.Position - byteOffset;

      accessor.BufferView = this.ExportBufferView((int)byteOffset, (int)byteLength);

      var id = new AccessorId {Id = this._root.Accessors.Count, Root = this._root};
      this._root.Accessors.Add(accessor);

      return id;
    }

    private AccessorId ExportAccessor(Vector4[] arr) {
      var count = arr.Length;

      if (count == 0) {
        throw new Exception("Accessors can not have a count of 0.");
      }

      var accessor = new Accessor();
      accessor.ComponentType = GLTFComponentType.Float;
      accessor.Count = (UInt32)count;
      accessor.Type = GLTFAccessorAttributeType.VEC4;

      var minX = arr[0].x;
      var minY = arr[0].y;
      var minZ = arr[0].z;
      var minW = arr[0].w;
      var maxX = arr[0].x;
      var maxY = arr[0].y;
      var maxZ = arr[0].z;
      var maxW = arr[0].w;

      for (var i = 1; i < count; i++) {
        var cur = arr[i];

        if (cur.x < minX) {
          minX = cur.x;
        }

        if (cur.y < minY) {
          minY = cur.y;
        }

        if (cur.z < minZ) {
          minZ = cur.z;
        }

        if (cur.w < minW) {
          minW = cur.w;
        }

        if (cur.x > maxX) {
          maxX = cur.x;
        }

        if (cur.y > maxY) {
          maxY = cur.y;
        }

        if (cur.z > maxZ) {
          maxZ = cur.z;
        }

        if (cur.w > maxW) {
          maxW = cur.w;
        }
      }

      accessor.Min = new List<double> {minX, minY, minZ, minW};
      accessor.Max = new List<double> {maxX, maxY, maxZ, maxW};

      var byteOffset = this._bufferWriter.BaseStream.Position;

      foreach (var vec in arr) {
        this._bufferWriter.Write(vec.x);
        this._bufferWriter.Write(vec.y);
        this._bufferWriter.Write(vec.z);
        this._bufferWriter.Write(vec.w);
      }

      var byteLength = this._bufferWriter.BaseStream.Position - byteOffset;

      accessor.BufferView = this.ExportBufferView((int)byteOffset, (int)byteLength);

      var id = new AccessorId {Id = this._root.Accessors.Count, Root = this._root};
      this._root.Accessors.Add(accessor);

      return id;
    }

    private AccessorId ExportAccessor(Color[] arr) {
      var count = arr.Length;

      if (count == 0) {
        throw new Exception("Accessors can not have a count of 0.");
      }

      var accessor = new Accessor();
      accessor.ComponentType = GLTFComponentType.Float;
      accessor.Count = (UInt32)count;
      accessor.Type = GLTFAccessorAttributeType.VEC4;

      var minR = arr[0].r;
      var minG = arr[0].g;
      var minB = arr[0].b;
      var minA = arr[0].a;
      var maxR = arr[0].r;
      var maxG = arr[0].g;
      var maxB = arr[0].b;
      var maxA = arr[0].a;

      for (var i = 1; i < count; i++) {
        var cur = arr[i];

        if (cur.r < minR) {
          minR = cur.r;
        }

        if (cur.g < minG) {
          minG = cur.g;
        }

        if (cur.b < minB) {
          minB = cur.b;
        }

        if (cur.a < minA) {
          minA = cur.a;
        }

        if (cur.r > maxR) {
          maxR = cur.r;
        }

        if (cur.g > maxG) {
          maxG = cur.g;
        }

        if (cur.b > maxB) {
          maxB = cur.b;
        }

        if (cur.a > maxA) {
          maxA = cur.a;
        }
      }

      accessor.Min = new List<double> {minR, minG, minB, minA};
      accessor.Max = new List<double> {maxR, maxG, maxB, maxA};

      var byteOffset = this._bufferWriter.BaseStream.Position;

      foreach (var color in arr) {
        this._bufferWriter.Write(color.r);
        this._bufferWriter.Write(color.g);
        this._bufferWriter.Write(color.b);
        this._bufferWriter.Write(color.a);
      }

      var byteLength = this._bufferWriter.BaseStream.Position - byteOffset;

      accessor.BufferView = this.ExportBufferView((int)byteOffset, (int)byteLength);

      var id = new AccessorId {Id = this._root.Accessors.Count, Root = this._root};
      this._root.Accessors.Add(accessor);

      return id;
    }

    private BufferViewId ExportBufferView(int byteOffset, int byteLength) {
      var bufferView = new BufferView {
          Buffer = this._bufferId, ByteOffset = (UInt32)byteOffset, ByteLength = (UInt32)byteLength
      };

      var id = new BufferViewId {Id = this._root.BufferViews.Count, Root = this._root};

      this._root.BufferViews.Add(bufferView);

      return id;
    }

    public MaterialId GetMaterialId(GLTFRoot root, Material materialObj) {
      for (var i = 0; i < this._materials.Count; i++) {
        if (this._materials[i] == materialObj) {
          return new MaterialId {Id = i, Root = root};
        }
      }

      return null;
    }

    public TextureId GetTextureId(GLTFRoot root, Texture textureObj) {
      for (var i = 0; i < this._textures.Count; i++) {
        if (this._textures[i] == textureObj) {
          return new TextureId {Id = i, Root = root};
        }
      }

      return null;
    }

    public ImageId GetImageId(GLTFRoot root, Texture imageObj) {
      for (var i = 0; i < this._imageInfos.Count; i++) {
        if (this._imageInfos[i].texture == imageObj) {
          return new ImageId {Id = i, Root = root};
        }
      }

      return null;
    }

    public SamplerId GetSamplerId(GLTFRoot root, Texture textureObj) {
      for (var i = 0; i < root.Samplers.Count; i++) {
        var filterIsNearest = root.Samplers[i].MinFilter == MinFilterMode.Nearest
                               || root.Samplers[i].MinFilter == MinFilterMode.NearestMipmapNearest
                               || root.Samplers[i].MinFilter == MinFilterMode.LinearMipmapNearest;

        var filterIsLinear = root.Samplers[i].MinFilter == MinFilterMode.Linear
                              || root.Samplers[i].MinFilter == MinFilterMode.NearestMipmapLinear;

        var filterMatched = textureObj.filterMode == FilterMode.Point && filterIsNearest
                             || textureObj.filterMode == FilterMode.Bilinear && filterIsLinear
                             || textureObj.filterMode == FilterMode.Trilinear
                             && root.Samplers[i].MinFilter == MinFilterMode.LinearMipmapLinear;

        var wrapMatched =
            textureObj.wrapMode == TextureWrapMode.Clamp && root.Samplers[i].WrapS == WrapMode.ClampToEdge
            || textureObj.wrapMode == TextureWrapMode.Repeat && root.Samplers[i].WrapS != WrapMode.ClampToEdge;

        if (filterMatched && wrapMatched) {
          return new SamplerId {Id = i, Root = root};
        }
      }

      return null;
    }
  }
}
