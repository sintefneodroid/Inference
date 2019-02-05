using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GLTF;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.Cache;
using UnityGLTF.Extensions;
using UnityGLTF.Loader;
using Matrix4x4 = GLTF.Math.Matrix4x4;
using Object = UnityEngine.Object;
using WrapMode = UnityEngine.WrapMode;
using ThreadPriority = System.Threading.ThreadPriority;

#if WINDOWS_UWP
using System.Threading.Tasks;
#endif

namespace UnityGLTF
{
    public struct MeshConstructionData
    {
        public MeshPrimitive Primitive { get; set; }
        public Dictionary<string, AttributeAccessor> MeshAttributes { get; set; }
    }

    public class GLTFSceneImporter : IDisposable
    {
        public enum ColliderType
        {
            None,
            Box,
            Mesh,
            MeshConvex
        }

        /// <summary>
        /// Maximum LOD
        /// </summary>
        public int MaximumLod = 300;

        /// <summary>
        /// Timeout for certain threading operations
        /// </summary>
        public int Timeout = 8;

        /// <summary>
        /// Use Multithreading or not
        /// </summary>
        public bool isMultithreaded = false;

        /// <summary>
        /// The parent transform for the created GameObject
        /// </summary>
        public Transform SceneParent { get; set; }

        /// <summary>
        /// The last created object
        /// </summary>
        public GameObject CreatedObject { get; private set; }

        /// <summary>
        /// Adds colliders to primitive objects when created
        /// </summary>
        public ColliderType Collider { get; set; }

        /// <summary>
        /// Override for the shader to use on created materials
        /// </summary>
        public string CustomShaderName { get; set; }

        protected struct GLBStream
        {
            public Stream Stream;
            public long StartPosition;
        }

        protected GameObject _lastLoadedScene;
        protected readonly GLTFMaterial DefaultMaterial = new GLTFMaterial();
        protected MaterialCacheData _defaultLoadedMaterial = null;

        protected string _gltfFileName;
        protected GLBStream _gltfStream;
        protected GLTFRoot _gltfRoot;
        protected AssetCache _assetCache;
        protected AsyncAction _asyncAction;
        protected ILoader _loader;
        private bool _isRunning = false;

        /// <summary>
        /// Creates a GLTFSceneBuilder object which will be able to construct a scene based off a url
        /// </summary>
        /// <param name="gltfFileName">glTF file relative to data loader path</param>
        /// <param name="externalDataLoader"></param>
        public GLTFSceneImporter(string gltfFileName, ILoader externalDataLoader) : this(externalDataLoader)
        {
            this._gltfFileName = gltfFileName;
        }

        public GLTFSceneImporter(GLTFRoot rootNode, ILoader externalDataLoader, Stream glbStream = null) : this(
            externalDataLoader)
        {
            this._gltfRoot = rootNode;
            if (glbStream != null)
            {
                this._gltfStream = new GLBStream {Stream = glbStream, StartPosition = glbStream.Position};
            }
        }

        private GLTFSceneImporter(ILoader externalDataLoader)
        {
            this._loader = externalDataLoader;
            this._asyncAction = new AsyncAction();
        }

        public void Dispose()
        {
            if (this._assetCache != null)
            {
                this.Cleanup();
            }
        }

        public GameObject LastLoadedScene
        {
            get { return this._lastLoadedScene; }
        }

        /// <summary>
        /// Loads a glTF Scene into the LastLoadedScene field
        /// </summary>
        /// <param name="sceneIndex">The scene to load, If the index isn't specified, we use the default index in the file. Failing that we load index 0.</param>
        /// <param name="onLoadComplete">Callback function for when load is completed</param>
        /// <returns></returns>
        public IEnumerator LoadScene(int sceneIndex = -1, Action<GameObject> onLoadComplete = null)
        {
            try
            {
                lock (this)
                {
                    if (this._isRunning)
                    {
                        throw new GLTFLoadException("Cannot call LoadScene while GLTFSceneImporter is already running");
                    }

                    this._isRunning = true;
                }

                if (this._gltfRoot == null)
                {
                    yield return this.LoadJson(this._gltfFileName);
                }

                yield return this._LoadScene(sceneIndex);

                this.Cleanup();
            }
            finally
            {
                lock (this)
                {
                    this._isRunning = false;
                }
            }

            if (onLoadComplete != null)
            {
                onLoadComplete(this.LastLoadedScene);
            }
        }

        /// <summary>
        /// Loads a node tree from a glTF file into the LastLoadedScene field
        /// </summary>
        /// <param name="nodeIndex">The node index to load from the glTF</param>
        /// <returns></returns>
        public IEnumerator LoadNode(int nodeIndex)
        {
            if (this._gltfRoot == null)
            {
                throw new InvalidOperationException("GLTF root must first be loaded and parsed");
            }

            try
            {
                lock (this)
                {
                    if (this._isRunning)
                    {
                        throw new GLTFLoadException("Cannot call LoadNode while GLTFSceneImporter is already running");
                    }

                    this._isRunning = true;
                }

                if (this._assetCache == null)
                {
                    this.InitializeAssetCache();
                }

                yield return this._LoadNode(nodeIndex);
                this.CreatedObject = this._assetCache.NodeCache[nodeIndex];
                this.InitializeGltfTopLevelObject();

                // todo: optimially the asset cache can be reused between nodes
                this.Cleanup();
            }
            finally
            {
                lock (this)
                {
                    this._isRunning = false;
                }
            }
        }

        /// <summary>
        /// Initializes the top-level created node by adding an instantiated GLTF object component to it, 
        /// so that it can cleanup after itself properly when destroyed
        /// </summary>
        private void InitializeGltfTopLevelObject()
        {
            var instantiatedGltfObject = this.CreatedObject.AddComponent<InstantiatedGLTFObject>();
            instantiatedGltfObject.CachedData = new RefCountedCacheData
            {
                MaterialCache = this._assetCache.MaterialCache,
                MeshCache = this._assetCache.MeshCache,
                TextureCache = this._assetCache.TextureCache
            };
        }

        private IEnumerator ConstructBufferData(Node node)
        {
            var mesh = node.Mesh;
            if (mesh != null)
            {
                if (mesh.Value.Primitives != null)
                {
                    yield return this.ConstructMeshAttributes(mesh.Value, mesh);
                }
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    yield return this.ConstructBufferData(child.Value);
                }
            }
        }

        private IEnumerator ConstructMeshAttributes(GLTFMesh mesh, MeshId meshId)
        {
            var meshIdIndex = meshId.Id;

            if (this._assetCache.MeshCache[meshIdIndex] == null)
            {
                this._assetCache.MeshCache[meshIdIndex] = new MeshCacheData[mesh.Primitives.Count];
            }

            for (var i = 0; i < mesh.Primitives.Count; ++i)
            {
                var primitive = mesh.Primitives[i];

                if (this._assetCache.MeshCache[meshIdIndex][i] == null)
                {
                    this._assetCache.MeshCache[meshIdIndex][i] = new MeshCacheData();
                }

                if (this._assetCache.MeshCache[meshIdIndex][i].MeshAttributes.Count == 0)
                {
                    yield return this.ConstructMeshAttributes(primitive, meshIdIndex, i);
                    if (primitive.Material != null)
                    {
                        yield return this.ConstructMaterialImageBuffers(primitive.Material.Value);
                    }
                }
            }
        }

        protected IEnumerator ConstructImageBuffer(GLTFTexture texture, int textureIndex)
        {
            var sourceId = this.GetTextureSourceId(texture);
            if (this._assetCache.ImageStreamCache[sourceId] == null)
            {
                var image = this._gltfRoot.Images[sourceId];

                // we only load the streams if not a base64 uri, meaning the data is in the uri
                if (image.Uri != null && !URIHelper.IsBase64Uri(image.Uri))
                {
                    yield return this._loader.LoadStream(image.Uri);
                    this._assetCache.ImageStreamCache[sourceId] = this._loader.LoadedStream;
                }
            }

            this._assetCache.TextureCache[textureIndex] = new TextureCacheData {TextureDefinition = texture};
        }

        private IEnumerator LoadJson(string jsonFilePath)
        {
            if (this.isMultithreaded && this._loader.HasSyncLoadMethod)
            {
                var loadThread = new Thread(() => this._loader.LoadStreamSync(jsonFilePath));
                loadThread.Priority = ThreadPriority.Highest;
                loadThread.Start();
                yield return new WaitUntil(() => !loadThread.IsAlive);
            }
            else
            {
                yield return this._loader.LoadStream(jsonFilePath);
            }

            this._gltfStream.Stream = this._loader.LoadedStream;
            this._gltfStream.StartPosition = 0;

            if (this.isMultithreaded)
            {
                var parseJsonThread = new Thread(
                    () => GLTFParser.ParseJson(this._gltfStream.Stream, out this._gltfRoot,
                        this._gltfStream.StartPosition));
                parseJsonThread.Priority = ThreadPriority.Highest;
                parseJsonThread.Start();
                yield return new WaitUntil(() => !parseJsonThread.IsAlive);
            }
            else
            {
                GLTFParser.ParseJson(this._gltfStream.Stream, out this._gltfRoot, this._gltfStream.StartPosition);
                yield return null;
            }
        }

        private IEnumerator _LoadNode(int nodeIndex)
        {
            if (nodeIndex >= this._gltfRoot.Nodes.Count)
            {
                throw new ArgumentException("nodeIndex is out of range");
            }

            var nodeToLoad = this._gltfRoot.Nodes[nodeIndex];
            yield return this.ConstructBufferData(nodeToLoad);
            yield return this.ConstructNode(nodeToLoad, nodeIndex);
        }

        protected void InitializeAssetCache()
        {
            this._assetCache = new AssetCache(
                this._gltfRoot.Images != null ? this._gltfRoot.Images.Count : 0,
                this._gltfRoot.Textures != null ? this._gltfRoot.Textures.Count : 0,
                this._gltfRoot.Materials != null ? this._gltfRoot.Materials.Count : 0,
                this._gltfRoot.Buffers != null ? this._gltfRoot.Buffers.Count : 0,
                this._gltfRoot.Meshes != null ? this._gltfRoot.Meshes.Count : 0,
                this._gltfRoot.Nodes != null ? this._gltfRoot.Nodes.Count : 0,
                this._gltfRoot.Animations != null ? this._gltfRoot.Animations.Count : 0);
        }

        /// <summary>
        /// Creates a scene based off loaded JSON. Includes loading in binary and image data to construct the meshes required.
        /// </summary>
        /// <param name="sceneIndex">The bufferIndex of scene in gltf file to load</param>
        /// <returns></returns>
        protected IEnumerator _LoadScene(int sceneIndex = -1)
        {
            GLTFScene scene;
            this.InitializeAssetCache(); // asset cache currently needs initialized every time due to cleanup logic

            if (sceneIndex >= 0 && sceneIndex < this._gltfRoot.Scenes.Count)
            {
                scene = this._gltfRoot.Scenes[sceneIndex];
            }
            else
            {
                scene = this._gltfRoot.GetDefaultScene();
            }

            if (scene == null)
            {
                throw new GLTFLoadException("No default scene in gltf file.");
            }

            if (this._lastLoadedScene == null)
            {
                if (this._gltfRoot.Buffers != null)
                {
                    // todo add fuzzing to verify that buffers are before uri
                    for (var i = 0; i < this._gltfRoot.Buffers.Count; ++i)
                    {
                        var buffer = this._gltfRoot.Buffers[i];
                        if (this._assetCache.BufferCache[i] == null)
                        {
                            yield return this.ConstructBuffer(buffer, i);
                        }
                    }
                }

                if (this._gltfRoot.Textures != null)
                {
                    for (var i = 0; i < this._gltfRoot.Textures.Count; ++i)
                    {
                        if (this._assetCache.TextureCache[i] == null)
                        {
                            var texture = this._gltfRoot.Textures[i];
                            yield return this.ConstructImageBuffer(texture, i);
                            yield return this.ConstructImage(texture.Source.Value, texture.Source.Id);
                        }
                    }
                }

                yield return this.ConstructAttributesForMeshes();
            }

            yield return this.ConstructScene(scene);

            if (this.SceneParent != null)
            {
                this.CreatedObject.transform.SetParent(this.SceneParent, false);
            }

            this._lastLoadedScene = this.CreatedObject;
        }

        protected IEnumerator ConstructBuffer(GLTFBuffer buffer, int bufferIndex)
        {
            if (buffer.Uri == null)
            {
                this._assetCache.BufferCache[bufferIndex] = this.ConstructBufferFromGLB(bufferIndex);
            }
            else
            {
                Stream bufferDataStream = null;
                var uri = buffer.Uri;

                byte[] bufferData;
                URIHelper.TryParseBase64(uri, out bufferData);
                if (bufferData != null)
                {
                    bufferDataStream = new MemoryStream(bufferData, 0, bufferData.Length, false, true);
                }
                else
                {
                    yield return this._loader.LoadStream(buffer.Uri);
                    bufferDataStream = this._loader.LoadedStream;
                }

                this._assetCache.BufferCache[bufferIndex] = new BufferCacheData {Stream = bufferDataStream};
            }
        }

        protected IEnumerator ConstructImage(GLTFImage image, int imageCacheIndex, bool markGpuOnly = true)
        {
            if (this._assetCache.ImageCache[imageCacheIndex] == null)
            {
                if (image.BufferView != null)
                {
                    yield return this.ConstructImageFromGLB(image, imageCacheIndex);
                }
                else
                {
                    var uri = image.Uri;

                    byte[] bufferData;
                    URIHelper.TryParseBase64(uri, out bufferData);
                    if (bufferData != null)
                    {
                        var loadedTexture = new Texture2D(0, 0);
                        loadedTexture.LoadImage(bufferData, true);

                        this._assetCache.ImageCache[imageCacheIndex] = loadedTexture;
                        yield return null;
                    }
                    else
                    {
                        var stream = this._assetCache.ImageStreamCache[imageCacheIndex];
                        yield return this.ConstructUnityTexture(stream, markGpuOnly, image, imageCacheIndex);
                    }
                }
            }
        }

        /// <summary>
        /// Loads texture from a stream. Is responsible for stream clean up
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="markGpuOnly">Non-readable textures are saved only on the GPU and take up half as much memory.</param>
        /// <param name="imageCacheIndex"></param>
        /// <returns></returns>
        protected virtual IEnumerator ConstructUnityTexture(
            Stream stream,
            bool markGpuOnly,
            GLTFImage image,
            int imageCacheIndex)
        {
            var texture = new Texture2D(0, 0);

            if (stream is MemoryStream)
            {
                using (var memoryStream = stream as MemoryStream)
                {
                    //	NOTE: the second parameter of LoadImage() marks non-readable, but we can't mark it until after we call Apply()
                    texture.LoadImage(memoryStream.ToArray(), false);
                }

                yield return null;
            }
            else
            {
                var buffer = new byte[stream.Length];

                // todo: potential optimization is to split stream read into multiple frames (or put it on a thread?)
                using (stream)
                {
                    if (stream.Length > int.MaxValue)
                    {
                        throw new Exception("Stream is larger than can be copied into byte array");
                    }

                    if (this.isMultithreaded)
                    {
                        var readThread = new Thread(() => stream.Read(buffer, 0, (int) stream.Length));
                        readThread.Priority = ThreadPriority.Highest;
                        readThread.Start();
                        yield return new WaitUntil(() => !readThread.IsAlive);
                    }
                    else
                    {
                        stream.Read(buffer, 0, (int) stream.Length);
                        yield return null;
                    }
                }

                //	NOTE: the second parameter of LoadImage() marks non-readable, but we can't mark it until after we call Apply()
                texture.LoadImage(buffer, false);
                yield return null;
            }

            // After we conduct the Apply(), then we can make the texture non-readable and never create a CPU copy
            texture.Apply(true, markGpuOnly);

            this._assetCache.ImageCache[imageCacheIndex] = texture;
            yield return null;
        }

        protected virtual IEnumerator ConstructAttributesForMeshes()
        {
            for (var i = 0; i < this._gltfRoot.Meshes.Count; ++i)
            {
                var mesh = this._gltfRoot.Meshes[i];
                if (this._assetCache.MeshCache[i] == null)
                {
                    this._assetCache.MeshCache[i] = new MeshCacheData[mesh.Primitives.Count];
                }

                for (var j = 0; j < mesh.Primitives.Count; ++j)
                {
                    this._assetCache.MeshCache[i][j] = new MeshCacheData();
                    var primitive = mesh.Primitives[j];
                    yield return this.ConstructMeshAttributes(primitive, i, j);
                    if (primitive.Material != null)
                    {
                        yield return this.ConstructMaterialImageBuffers(primitive.Material.Value);
                    }
                }
            }
        }

        protected virtual IEnumerator ConstructMeshAttributes(MeshPrimitive primitive, int meshID, int primitiveIndex)
        {
            if (this._assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes.Count == 0)
            {
                var attributeAccessors = new Dictionary<string, AttributeAccessor>(primitive.Attributes.Count + 1);
                foreach (var attributePair in primitive.Attributes)
                {
                    var bufferIdPair = attributePair.Value.Value.BufferView.Value.Buffer;
                    var buffer = bufferIdPair.Value;
                    var bufferId = bufferIdPair.Id;

                    // on cache miss, load the buffer
                    if (this._assetCache.BufferCache[bufferId] == null)
                    {
                        yield return this.ConstructBuffer(buffer, bufferId);
                    }

                    var attributeAccessor = new AttributeAccessor
                    {
                        AccessorId = attributePair.Value,
                        Stream = this._assetCache.BufferCache[bufferId].Stream,
                        Offset = (UInt32) this._assetCache.BufferCache[bufferId].ChunkOffset
                    };

                    attributeAccessors[attributePair.Key] = attributeAccessor;
                }

                if (primitive.Indices != null)
                {
                    var bufferId = primitive.Indices.Value.BufferView.Value.Buffer.Id;
                    var indexBuilder = new AttributeAccessor
                    {
                        AccessorId = primitive.Indices,
                        Stream = this._assetCache.BufferCache[bufferId].Stream,
                        Offset = (UInt32) this._assetCache.BufferCache[bufferId].ChunkOffset
                    };

                    attributeAccessors[SemanticProperties.INDICES] = indexBuilder;
                }

                if (this.isMultithreaded)
                {
                    var buildMeshAttributesThread =
                        new Thread(() => GLTFHelpers.BuildMeshAttributes(ref attributeAccessors));
                    buildMeshAttributesThread.Priority = ThreadPriority.Highest;
                    buildMeshAttributesThread.Start();
                    while (!buildMeshAttributesThread.Join(this.Timeout))
                    {
                        yield return null;
                    }
                }
                else
                {
                    GLTFHelpers.BuildMeshAttributes(ref attributeAccessors);
                }

                this.TransformAttributes(ref attributeAccessors);
                this._assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes = attributeAccessors;
            }
        }

        protected void TransformAttributes(ref Dictionary<string, AttributeAccessor> attributeAccessors)
        {
            // Flip vectors and triangles to the Unity coordinate system.
            if (attributeAccessors.ContainsKey(SemanticProperties.POSITION))
            {
                var attributeAccessor = attributeAccessors[SemanticProperties.POSITION];
                SchemaExtensions.ConvertVector3CoordinateSpace(
                    ref attributeAccessor,
                    SchemaExtensions.CoordinateSpaceConversionScale);
            }

            if (attributeAccessors.ContainsKey(SemanticProperties.INDICES))
            {
                var attributeAccessor = attributeAccessors[SemanticProperties.INDICES];
                SchemaExtensions.FlipFaces(ref attributeAccessor);
            }

            if (attributeAccessors.ContainsKey(SemanticProperties.NORMAL))
            {
                var attributeAccessor = attributeAccessors[SemanticProperties.NORMAL];
                SchemaExtensions.ConvertVector3CoordinateSpace(
                    ref attributeAccessor,
                    SchemaExtensions.CoordinateSpaceConversionScale);
            }

            // TexCoord goes from 0 to 3 to match GLTFHelpers.BuildMeshAttributes
            for (var i = 0; i < 4; i++)
            {
                if (attributeAccessors.ContainsKey(SemanticProperties.TexCoord(i)))
                {
                    var attributeAccessor = attributeAccessors[SemanticProperties.TexCoord(i)];
                    SchemaExtensions.FlipTexCoordArrayV(ref attributeAccessor);
                }
            }

            if (attributeAccessors.ContainsKey(SemanticProperties.TANGENT))
            {
                var attributeAccessor = attributeAccessors[SemanticProperties.TANGENT];
                SchemaExtensions.ConvertVector4CoordinateSpace(
                    ref attributeAccessor,
                    SchemaExtensions.TangentSpaceConversionScale);
            }
        }

        #region Animation

        static string RelativePathFrom(Transform self, Transform root)
        {
            var path = new List<String>();
            for (var current = self; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return String.Join("/", path.ToArray());
                }

                path.Insert(0, current.name);
            }

            throw new Exception("no RelativePath");
        }

        protected virtual void BuildAnimationSamplers(GLTFAnimation animation, int animationId)
        {
            // look up expected data types
            var typeMap = new Dictionary<int, string>();
            foreach (var channel in animation.Channels)
            {
                typeMap[channel.Sampler.Id] = channel.Target.Path.ToString();
            }

            var samplers = this._assetCache.AnimationCache[animationId].Samplers;
            var samplersByType =
                new Dictionary<string, List<AttributeAccessor>>
                {
                    {"time", new List<AttributeAccessor>(animation.Samplers.Count)}
                };

            for (var i = 0; i < animation.Samplers.Count; i++)
            {
                // no sense generating unused samplers
                if (!typeMap.ContainsKey(i))
                {
                    continue;
                }

                var samplerDef = animation.Samplers[i];

                // set up input accessors
                var bufferCacheData = this._assetCache.BufferCache[samplerDef.Input.Value.BufferView.Value.Buffer.Id];
                var attributeAccessor = new AttributeAccessor
                {
                    AccessorId = samplerDef.Input, Stream = bufferCacheData.Stream,
                    Offset = (UInt32) bufferCacheData.ChunkOffset
                };

                samplers[i].Input = attributeAccessor;
                samplersByType["time"].Add(attributeAccessor);

                // set up output accessors
                bufferCacheData = this._assetCache.BufferCache[samplerDef.Output.Value.BufferView.Value.Buffer.Id];
                attributeAccessor = new AttributeAccessor
                {
                    AccessorId = samplerDef.Output,
                    Stream = bufferCacheData.Stream,
                    Offset = (UInt32) bufferCacheData.ChunkOffset
                };

                samplers[i].Output = attributeAccessor;

                if (!samplersByType.ContainsKey(typeMap[i]))
                {
                    samplersByType[typeMap[i]] = new List<AttributeAccessor>();
                }

                samplersByType[typeMap[i]].Add(attributeAccessor);
            }

            // populate attributeAccessors with buffer data
            GLTFHelpers.BuildAnimationSamplers(ref samplersByType);
        }

        AnimationClip ConstructClip(Transform root, Transform[] nodes, int animationId)
        {
            var animation = this._gltfRoot.Animations[animationId];

            var animationCache = this._assetCache.AnimationCache[animationId];
            if (animationCache == null)
            {
                animationCache = new AnimationCacheData(animation.Samplers.Count);
                this._assetCache.AnimationCache[animationId] = animationCache;
            }
            else if (animationCache.LoadedAnimationClip != null)
                return animationCache.LoadedAnimationClip;

            // unpack accessors
            this.BuildAnimationSamplers(animation, animationId);

            // init clip
            var clip = new AnimationClip {name = animation.Name ?? String.Format("animation:{0}", animationId)};
            this._assetCache.AnimationCache[animationId].LoadedAnimationClip = clip;

            // needed because Animator component is unavailable at runtime
            clip.legacy = true;

            foreach (var channel in animation.Channels)
            {
                var samplerCache = animationCache.Samplers[channel.Sampler.Id];
                var node = nodes[channel.Target.Node.Id];
                var relativePath = RelativePathFrom(node, root);
                AnimationCurve curveX = new AnimationCurve(),
                    curveY = new AnimationCurve(),
                    curveZ = new AnimationCurve(),
                    curveW = new AnimationCurve();
                NumericArray input = samplerCache.Input.AccessorContent, output = samplerCache.Output.AccessorContent;

                switch (channel.Target.Path)
                {
                    case GLTFAnimationChannelPath.translation:
                        for (var i = 0; i < input.AsFloats.Length; ++i)
                        {
                            var time = input.AsFloats[i];
                            var position = output.AsVec3s[i].ToUnityVector3Convert();
                            curveX.AddKey(time, position.x);
                            curveY.AddKey(time, position.y);
                            curveZ.AddKey(time, position.z);
                        }

                        clip.SetCurve(relativePath, typeof(Transform), "localPosition.x", curveX);
                        clip.SetCurve(relativePath, typeof(Transform), "localPosition.y", curveY);
                        clip.SetCurve(relativePath, typeof(Transform), "localPosition.z", curveZ);
                        break;

                    case GLTFAnimationChannelPath.rotation:
                        for (var i = 0; i < input.AsFloats.Length; ++i)
                        {
                            var time = input.AsFloats[i];
                            var rotation = output.AsVec4s[i];

                            var rot = new GLTF.Math.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W)
                                .ToUnityQuaternionConvert();
                            curveX.AddKey(time, rot.x);
                            curveY.AddKey(time, rot.y);
                            curveZ.AddKey(time, rot.z);
                            curveW.AddKey(time, rot.w);
                        }

                        clip.SetCurve(relativePath, typeof(Transform), "localRotation.x", curveX);
                        clip.SetCurve(relativePath, typeof(Transform), "localRotation.y", curveY);
                        clip.SetCurve(relativePath, typeof(Transform), "localRotation.z", curveZ);
                        clip.SetCurve(relativePath, typeof(Transform), "localRotation.w", curveW);
                        break;

                    case GLTFAnimationChannelPath.scale:
                        for (var i = 0; i < input.AsFloats.Length; ++i)
                        {
                            var time = input.AsFloats[i];
                            var scale = output.AsVec3s[i].ToUnityVector3Raw();
                            curveX.AddKey(time, scale.x);
                            curveY.AddKey(time, scale.y);
                            curveZ.AddKey(time, scale.z);
                        }

                        clip.SetCurve(relativePath, typeof(Transform), "localScale.x", curveX);
                        clip.SetCurve(relativePath, typeof(Transform), "localScale.y", curveY);
                        clip.SetCurve(relativePath, typeof(Transform), "localScale.z", curveZ);
                        break;

                    case GLTFAnimationChannelPath.weights:
                        var primitives = channel.Target.Node.Value.Mesh.Value.Primitives;
                        var targetCount = primitives[0].Targets.Count;
                        for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
                        {
                            for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
                            {
                                // TODO: add support for blend shapes/morph targets
                                //clip.SetCurve(primitiveObjPath, typeof(SkinnedMeshRenderer), "blendShape." + targetIndex, curves[targetIndex]);
                            }
                        }

                        break;

                    default:
                        Debug.LogWarning("Cannot read GLTF animation path");
                        break;
                } // switch target type
            } // foreach channel

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        #endregion

        protected virtual IEnumerator ConstructScene(GLTFScene scene)
        {
            var sceneObj = new GameObject(string.IsNullOrEmpty(scene.Name) ? ("GLTFScene") : scene.Name);

            var nodeTransforms = new Transform[scene.Nodes.Count];
            for (var i = 0; i < scene.Nodes.Count; ++i)
            {
                var node = scene.Nodes[i];
                yield return this.ConstructNode(node.Value, node.Id);
                var nodeObj = this._assetCache.NodeCache[node.Id];
                nodeObj.transform.SetParent(sceneObj.transform, false);
                nodeTransforms[i] = nodeObj.transform;
            }

            if (this._gltfRoot.Animations != null && this._gltfRoot.Animations.Count > 0)
            {
                // create the AnimationClip that will contain animation data
                var animation = sceneObj.AddComponent<Animation>();
                for (var i = 0; i < this._gltfRoot.Animations.Count; ++i)
                {
                    var clip = this.ConstructClip(
                        sceneObj.transform,
                        this._assetCache.NodeCache.Select(x => x.transform).ToArray(),
                        i);

                    clip.wrapMode = WrapMode.Loop;

                    animation.AddClip(clip, clip.name);
                    if (i == 0)
                    {
                        animation.clip = clip;
                    }
                }
            }

            this.CreatedObject = sceneObj;
            this.InitializeGltfTopLevelObject();
        }

        protected virtual IEnumerator ConstructNode(Node node, int nodeIndex)
        {
            if (this._assetCache.NodeCache[nodeIndex] != null)
            {
                yield break;
            }

            var nodeObj = new GameObject(string.IsNullOrEmpty(node.Name) ? ("GLTFNode" + nodeIndex) : node.Name);
            // If we're creating a really large node, we need it to not be visible in partial stages. So we hide it while we create it
            nodeObj.SetActive(false);

            Vector3 position;
            Quaternion rotation;
            Vector3 scale;
            node.GetUnityTRSProperties(out position, out rotation, out scale);
            nodeObj.transform.localPosition = position;
            nodeObj.transform.localRotation = rotation;
            nodeObj.transform.localScale = scale;

            if (node.Mesh != null)
            {
                yield return this.ConstructMesh(
                    node.Mesh.Value,
                    nodeObj.transform,
                    node.Mesh.Id,
                    node.Skin != null ? node.Skin.Value : null);
            }
            /* TODO: implement camera (probably a flag to disable for VR as well)
            if (camera != null)
            {
              GameObject cameraObj = camera.Value.Create();
              cameraObj.transform.parent = nodeObj.transform;
            }
            */

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    // todo blgross: replace with an iterartive solution
                    yield return this.ConstructNode(child.Value, child.Id);
                    var childObj = this._assetCache.NodeCache[child.Id];
                    childObj.transform.SetParent(nodeObj.transform, false);
                }
            }

            nodeObj.SetActive(true);
            this._assetCache.NodeCache[nodeIndex] = nodeObj;
        }

        private bool NeedsSkinnedMeshRenderer(MeshPrimitive primitive, Skin skin)
        {
            return this.HasBones(skin) || this.HasBlendShapes(primitive);
        }

        private bool HasBones(Skin skin)
        {
            return skin != null;
        }

        private bool HasBlendShapes(MeshPrimitive primitive)
        {
            return primitive.Targets != null;
        }

        protected virtual IEnumerator SetupBones(
            Skin skin,
            MeshPrimitive primitive,
            SkinnedMeshRenderer renderer,
            GameObject primitiveObj,
            Mesh curMesh)
        {
            var boneCount = skin.Joints.Count;
            var bones = new Transform[boneCount];

            var bufferId = skin.InverseBindMatrices.Value.BufferView.Value.Buffer.Id;
            var attributeAccessor = new AttributeAccessor
            {
                AccessorId = skin.InverseBindMatrices,
                Stream = this._assetCache.BufferCache[bufferId].Stream,
                Offset = (UInt32) this._assetCache.BufferCache[bufferId].ChunkOffset
            };

            GLTFHelpers.BuildBindPoseSamplers(ref attributeAccessor);

            var gltfBindPoses = attributeAccessor.AccessorContent.AsMatrix4x4s;
            var bindPoses = new UnityEngine.Matrix4x4[skin.Joints.Count];

            for (var i = 0; i < boneCount; i++)
            {
                if (this._assetCache.NodeCache[skin.Joints[i].Id] == null)
                {
                    yield return this.ConstructNode(this._gltfRoot.Nodes[skin.Joints[i].Id], skin.Joints[i].Id);
                }

                bones[i] = this._assetCache.NodeCache[skin.Joints[i].Id].transform;
                bindPoses[i] = gltfBindPoses[i].ToUnityMatrix4x4Convert();
            }

            renderer.rootBone = this._assetCache.NodeCache[skin.Skeleton.Id].transform;
            curMesh.bindposes = bindPoses;
            renderer.bones = bones;

            yield return null;
        }

        private BoneWeight[] CreateBoneWeightArray(Vector4[] joints, Vector4[] weights, int vertCount)
        {
            this.NormalizeBoneWeightArray(weights);

            var boneWeights = new BoneWeight[vertCount];
            for (var i = 0; i < vertCount; i++)
            {
                boneWeights[i].boneIndex0 = (int) joints[i].x;
                boneWeights[i].boneIndex1 = (int) joints[i].y;
                boneWeights[i].boneIndex2 = (int) joints[i].z;
                boneWeights[i].boneIndex3 = (int) joints[i].w;

                boneWeights[i].weight0 = weights[i].x;
                boneWeights[i].weight1 = weights[i].y;
                boneWeights[i].weight2 = weights[i].z;
                boneWeights[i].weight3 = weights[i].w;
            }

            return boneWeights;
        }

        /// <summary>
        /// Ensures each bone weight influences applied to the vertices add up to 1
        /// </summary>
        /// <param name="weights">Bone weight array</param>
        private void NormalizeBoneWeightArray(Vector4[] weights)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                var weightSum = (weights[i].x + weights[i].y + weights[i].z + weights[i].w);

                if (!Mathf.Approximately(weightSum, 0))
                {
                    weights[i] /= weightSum;
                }
            }
        }

        protected virtual IEnumerator ConstructMesh(GLTFMesh mesh, Transform parent, int meshId, Skin skin)
        {
            if (this._assetCache.MeshCache[meshId] == null)
            {
                this._assetCache.MeshCache[meshId] = new MeshCacheData[mesh.Primitives.Count];
            }

            for (var i = 0; i < mesh.Primitives.Count; ++i)
            {
                var primitive = mesh.Primitives[i];
                var materialIndex = primitive.Material != null ? primitive.Material.Id : -1;

                yield return this.ConstructMeshPrimitive(primitive, meshId, i, materialIndex);

                var primitiveObj = new GameObject("Primitive");

                var materialCacheData = materialIndex >= 0
                    ? this._assetCache.MaterialCache[materialIndex]
                    : this._defaultLoadedMaterial;

                var material =
                    materialCacheData.GetContents(primitive.Attributes.ContainsKey(SemanticProperties.Color(0)));

                var curMesh = this._assetCache.MeshCache[meshId][i].LoadedMesh;
                if (this.NeedsSkinnedMeshRenderer(primitive, skin))
                {
                    var skinnedMeshRenderer = primitiveObj.AddComponent<SkinnedMeshRenderer>();
                    skinnedMeshRenderer.material = material;
                    skinnedMeshRenderer.quality = SkinQuality.Auto;
                    // TODO: add support for blend shapes/morph targets
                    //if (HasBlendShapes(primitive))
                    //	SetupBlendShapes(primitive);
                    if (this.HasBones(skin))
                    {
                        yield return this.SetupBones(skin, primitive, skinnedMeshRenderer, primitiveObj, curMesh);
                    }

                    skinnedMeshRenderer.sharedMesh = curMesh;
                }
                else
                {
                    var meshRenderer = primitiveObj.AddComponent<MeshRenderer>();
                    meshRenderer.material = material;
                }

                var meshFilter = primitiveObj.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = curMesh;

                switch (this.Collider)
                {
                    case ColliderType.Box:
                        var boxCollider = primitiveObj.AddComponent<BoxCollider>();
                        boxCollider.center = curMesh.bounds.center;
                        boxCollider.size = curMesh.bounds.size;
                        break;
                    case ColliderType.Mesh:
                        var meshCollider = primitiveObj.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = curMesh;
                        break;
                    case ColliderType.MeshConvex:
                        var meshConvexCollider = primitiveObj.AddComponent<MeshCollider>();
                        meshConvexCollider.sharedMesh = curMesh;
                        meshConvexCollider.convex = true;
                        break;
                }

                primitiveObj.transform.SetParent(parent, false);
                primitiveObj.SetActive(true);
            }
        }

        protected virtual IEnumerator ConstructMeshPrimitive(
            MeshPrimitive primitive,
            int meshID,
            int primitiveIndex,
            int materialIndex)
        {
            if (this._assetCache.MeshCache[meshID][primitiveIndex] == null)
            {
                this._assetCache.MeshCache[meshID][primitiveIndex] = new MeshCacheData();
            }

            if (this._assetCache.MeshCache[meshID][primitiveIndex].LoadedMesh == null)
            {
                var meshAttributes = this._assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes;
                var meshConstructionData = new MeshConstructionData
                    {Primitive = primitive, MeshAttributes = meshAttributes};

                yield return null;
                yield return this.ConstructUnityMesh(meshConstructionData, meshID, primitiveIndex);
            }

            var shouldUseDefaultMaterial = primitive.Material == null;

            var materialToLoad = shouldUseDefaultMaterial ? this.DefaultMaterial : primitive.Material.Value;
            if ((shouldUseDefaultMaterial && this._defaultLoadedMaterial == null)
                || (!shouldUseDefaultMaterial && this._assetCache.MaterialCache[materialIndex] == null))
            {
                yield return this.ConstructMaterialTextures(materialToLoad);
                this.ConstructMaterial(materialToLoad, materialIndex);
            }
        }

        protected virtual IEnumerator ConstructMaterialImageBuffers(GLTFMaterial def)
        {
            if (def.PbrMetallicRoughness != null)
            {
                var pbr = def.PbrMetallicRoughness;

                if (pbr.BaseColorTexture != null)
                {
                    var textureId = pbr.BaseColorTexture.Index;
                    yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
                }

                if (pbr.MetallicRoughnessTexture != null)
                {
                    var textureId = pbr.MetallicRoughnessTexture.Index;

                    yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
                }
            }

            if (def.CommonConstant != null)
            {
                if (def.CommonConstant.LightmapTexture != null)
                {
                    var textureId = def.CommonConstant.LightmapTexture.Index;

                    yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
                }
            }

            if (def.NormalTexture != null)
            {
                var textureId = def.NormalTexture.Index;
                yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
            }

            if (def.OcclusionTexture != null)
            {
                var textureId = def.OcclusionTexture.Index;

                if (!(def.PbrMetallicRoughness != null
                      && def.PbrMetallicRoughness.MetallicRoughnessTexture != null
                      && def.PbrMetallicRoughness.MetallicRoughnessTexture.Index.Id == textureId.Id))
                {
                    yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
                }
            }

            if (def.EmissiveTexture != null)
            {
                var textureId = def.EmissiveTexture.Index;
                yield return this.ConstructImageBuffer(textureId.Value, textureId.Id);
            }
        }

        protected virtual IEnumerator ConstructMaterialTextures(GLTFMaterial def)
        {
            for (var i = 0; i < this._assetCache.TextureCache.Length; ++i)
            {
                var textureCacheData = this._assetCache.TextureCache[i];
                if (textureCacheData != null && textureCacheData.Texture == null)
                {
                    yield return this.ConstructTexture(textureCacheData.TextureDefinition, i, true);
                }
            }
        }

        protected IEnumerator ConstructUnityMesh(
            MeshConstructionData meshConstructionData,
            int meshId,
            int primitiveIndex)
        {
            var primitive = meshConstructionData.Primitive;
            var meshAttributes = meshConstructionData.MeshAttributes;
            var vertexCount = (Int32) primitive.Attributes[SemanticProperties.POSITION].Value.Count;

            // todo optimize: There are multiple copies being performed to turn the buffer data into mesh data. Look into reducing them
            var mesh = new Mesh
            {
#if UNITY_2017_3_OR_NEWER
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
#endif
                vertices =
                    primitive.Attributes.ContainsKey(SemanticProperties.POSITION)
                        ? meshAttributes[SemanticProperties.POSITION].AccessorContent.AsVertices.ToUnityVector3Raw()
                        : null,
                normals =
                    primitive.Attributes.ContainsKey(SemanticProperties.NORMAL)
                        ? meshAttributes[SemanticProperties.NORMAL].AccessorContent.AsNormals.ToUnityVector3Raw()
                        : null,
                uv =
                    primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(0))
                        ? meshAttributes[SemanticProperties.TexCoord(0)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                        : null,
                uv2 =
                    primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(1))
                        ? meshAttributes[SemanticProperties.TexCoord(1)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                        : null,
                uv3 =
                    primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(2))
                        ? meshAttributes[SemanticProperties.TexCoord(2)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                        : null,
                uv4 =
                    primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(3))
                        ? meshAttributes[SemanticProperties.TexCoord(3)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                        : null,
                colors =
                    primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
                        ? meshAttributes[SemanticProperties.Color(0)].AccessorContent.AsColors.ToUnityColorRaw()
                        : null,
                triangles =
                    primitive.Indices != null
                        ? meshAttributes[SemanticProperties.INDICES].AccessorContent.AsUInts.ToIntArrayRaw()
                        : MeshPrimitive.GenerateTriangles((Int32) vertexCount),
                tangents =
                    primitive.Attributes.ContainsKey(SemanticProperties.TANGENT)
                        ? meshAttributes[SemanticProperties.TANGENT].AccessorContent.AsTangents.ToUnityVector4Raw()
                        : null,
                boneWeights =
                    meshAttributes.ContainsKey(SemanticProperties.Weight(0))
                    && meshAttributes.ContainsKey(SemanticProperties.Joint(0))
                        ? this.CreateBoneWeightArray(
                            meshAttributes[SemanticProperties.Joint(0)].AccessorContent.AsVec4s.ToUnityVector4Raw(),
                            meshAttributes[SemanticProperties.Weight(0)].AccessorContent.AsVec4s.ToUnityVector4Raw(),
                            vertexCount)
                        : null
            };

            this._assetCache.MeshCache[meshId][primitiveIndex].LoadedMesh = mesh;

            yield return null;
        }

        protected virtual void ConstructMaterial(GLTFMaterial def, int materialIndex)
        {
            IUniformMap mapper;
            const string specGlossExtName = KHR_materials_pbrSpecularGlossinessExtensionFactory.EXTENSION_NAME;
            if (this._gltfRoot.ExtensionsUsed != null
                && this._gltfRoot.ExtensionsUsed.Contains(specGlossExtName)
                && def.Extensions != null
                && def.Extensions.ContainsKey(specGlossExtName))
            {
                if (!string.IsNullOrEmpty(this.CustomShaderName))
                {
                    mapper = new SpecGlossMap(this.CustomShaderName, this.MaximumLod);
                }
                else
                {
                    mapper = new SpecGlossMap(this.MaximumLod);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(this.CustomShaderName))
                {
                    mapper = new MetalRoughMap(this.CustomShaderName, this.MaximumLod);
                }
                else
                {
                    mapper = new MetalRoughMap(this.MaximumLod);
                }
            }

            mapper.AlphaMode = def.AlphaMode;
            mapper.DoubleSided = def.DoubleSided;

            var mrMapper = mapper as IMetalRoughUniformMap;
            if (def.PbrMetallicRoughness != null && mrMapper != null)
            {
                var pbr = def.PbrMetallicRoughness;

                mrMapper.BaseColorFactor = pbr.BaseColorFactor.ToUnityColorRaw();

                if (pbr.BaseColorTexture != null)
                {
                    var textureId = pbr.BaseColorTexture.Index.Id;
                    mrMapper.BaseColorTexture = this._assetCache.TextureCache[textureId].Texture;
                    mrMapper.BaseColorTexCoord = pbr.BaseColorTexture.TexCoord;

                    //ApplyTextureTransform(pbr.BaseColorTexture, material, "_MainTex");
                }

                mrMapper.MetallicFactor = pbr.MetallicFactor;

                if (pbr.MetallicRoughnessTexture != null)
                {
                    var textureId = pbr.MetallicRoughnessTexture.Index.Id;
                    mrMapper.MetallicRoughnessTexture = this._assetCache.TextureCache[textureId].Texture;
                    mrMapper.MetallicRoughnessTexCoord = pbr.MetallicRoughnessTexture.TexCoord;

                    //ApplyTextureTransform(pbr.MetallicRoughnessTexture, material, "_MetallicRoughnessMap");
                }

                mrMapper.RoughnessFactor = pbr.RoughnessFactor;
            }

            var sgMapper = mapper as ISpecGlossUniformMap;
            if (sgMapper != null)
            {
                var specGloss = def.Extensions[specGlossExtName] as KHR_materials_pbrSpecularGlossinessExtension;

                sgMapper.DiffuseFactor = specGloss.DiffuseFactor.ToUnityColorRaw();

                if (specGloss.DiffuseTexture != null)
                {
                    var textureId = specGloss.DiffuseTexture.Index.Id;
                    sgMapper.DiffuseTexture = this._assetCache.TextureCache[textureId].Texture;
                    sgMapper.DiffuseTexCoord = specGloss.DiffuseTexture.TexCoord;

                    //ApplyTextureTransform(specGloss.DiffuseTexture, material, "_MainTex");
                }

                sgMapper.SpecularFactor = specGloss.SpecularFactor.ToUnityVector3Raw();
                sgMapper.GlossinessFactor = specGloss.GlossinessFactor;

                if (specGloss.SpecularGlossinessTexture != null)
                {
                    var textureId = specGloss.SpecularGlossinessTexture.Index.Id;
                    sgMapper.SpecularGlossinessTexture = this._assetCache.TextureCache[textureId].Texture;
                }
            }

            if (def.NormalTexture != null)
            {
                var textureId = def.NormalTexture.Index.Id;
                mapper.NormalTexture = this._assetCache.TextureCache[textureId].Texture;
                mapper.NormalTexCoord = def.NormalTexture.TexCoord;
                mapper.NormalTexScale = def.NormalTexture.Scale;
            }

            if (def.OcclusionTexture != null)
            {
                mapper.OcclusionTexStrength = def.OcclusionTexture.Strength;
                var textureId = def.OcclusionTexture.Index.Id;
                mapper.OcclusionTexture = this._assetCache.TextureCache[textureId].Texture;
            }

            if (def.EmissiveTexture != null)
            {
                var textureId = def.EmissiveTexture.Index.Id;
                mapper.EmissiveTexture = this._assetCache.TextureCache[textureId].Texture;
                mapper.EmissiveTexCoord = def.EmissiveTexture.TexCoord;
            }

            mapper.EmissiveFactor = def.EmissiveFactor.ToUnityColorRaw();

            var vertColorMapper = mapper.Clone();
            vertColorMapper.VertexColorsEnabled = true;

            var materialWrapper = new MaterialCacheData
            {
                UnityMaterial = mapper.Material, UnityMaterialWithVertexColor = vertColorMapper.Material,
                GLTFMaterial = def
            };

            if (materialIndex >= 0)
            {
                this._assetCache.MaterialCache[materialIndex] = materialWrapper;
            }
            else
            {
                this._defaultLoadedMaterial = materialWrapper;
            }
        }

        protected virtual int GetTextureSourceId(GLTFTexture texture)
        {
            return texture.Source.Id;
        }

        /// <summary>
        /// Creates a texture from a glTF texture
        /// </summary>
        /// <param name="texture">The texture to load</param>
        /// <returns>The loaded unity texture</returns>
        public virtual IEnumerator LoadTexture(GLTFTexture texture, int textureIndex, bool markGpuOnly = true)
        {
            try
            {
                lock (this)
                {
                    if (this._isRunning)
                    {
                        throw new GLTFLoadException("Cannot CreateTexture while GLTFSceneImporter is already running");
                    }

                    this._isRunning = true;
                }

                if (this._assetCache == null)
                {
                    this.InitializeAssetCache();
                }

                yield return this.ConstructImageBuffer(texture, this.GetTextureSourceId(texture));
                yield return this.ConstructTexture(texture, textureIndex, markGpuOnly);
            }
            finally
            {
                lock (this)
                {
                    this._isRunning = false;
                }
            }
        }

        /// <summary>
        /// Gets texture that has been loaded from CreateTexture
        /// </summary>
        /// <param name="textureIndex">The texture to get</param>
        /// <returns>Created texture</returns>
        public virtual Texture GetTexture(int textureIndex)
        {
            if (this._assetCache == null)
            {
                throw new GLTFLoadException("Asset cache needs initialized before calling GetTexture");
            }

            if (this._assetCache.TextureCache[textureIndex] == null)
            {
                return null;
            }

            return this._assetCache.TextureCache[textureIndex].Texture;
        }

        protected virtual IEnumerator ConstructTexture(GLTFTexture texture, int textureIndex, bool markGpuOnly = true)
        {
            if (this._assetCache.TextureCache[textureIndex].Texture == null)
            {
                var sourceId = this.GetTextureSourceId(texture);
                var image = this._gltfRoot.Images[sourceId];
                yield return this.ConstructImage(image, sourceId, markGpuOnly);

                var source = this._assetCache.ImageCache[sourceId];
                var desiredFilterMode = FilterMode.Bilinear;
                var desiredWrapMode = TextureWrapMode.Repeat;

                if (texture.Sampler != null)
                {
                    var sampler = texture.Sampler.Value;
                    switch (sampler.MinFilter)
                    {
                        case MinFilterMode.Nearest:
                        case MinFilterMode.NearestMipmapNearest:
                        case MinFilterMode.NearestMipmapLinear:
                            desiredFilterMode = FilterMode.Point;
                            break;
                        case MinFilterMode.Linear:
                        case MinFilterMode.LinearMipmapNearest:
                        case MinFilterMode.LinearMipmapLinear:
                            desiredFilterMode = FilterMode.Bilinear;
                            break;
                        default:
                            Debug.LogWarning("Unsupported Sampler.MinFilter: " + sampler.MinFilter);
                            break;
                    }

                    switch (sampler.WrapS)
                    {
                        case GLTF.Schema.WrapMode.ClampToEdge:
                            desiredWrapMode = TextureWrapMode.Clamp;
                            break;
                        case GLTF.Schema.WrapMode.Repeat:
                        default:
                            desiredWrapMode = TextureWrapMode.Repeat;
                            break;
                    }
                }

                if (source.filterMode == desiredFilterMode && source.wrapMode == desiredWrapMode)
                {
                    this._assetCache.TextureCache[textureIndex].Texture = source;
                }
                else
                {
                    var unityTexture = Object.Instantiate(source);
                    unityTexture.filterMode = desiredFilterMode;
                    unityTexture.wrapMode = desiredWrapMode;

                    this._assetCache.TextureCache[textureIndex].Texture = unityTexture;
                }

                yield return null;
            }
        }

        protected virtual IEnumerator ConstructImageFromGLB(GLTFImage image, int imageCacheIndex)
        {
            var texture = new Texture2D(0, 0);
            var bufferView = image.BufferView.Value;
            var data = new byte[bufferView.ByteLength];

            var bufferContents = this._assetCache.BufferCache[bufferView.Buffer.Id];
            bufferContents.Stream.Position = bufferView.ByteOffset + bufferContents.ChunkOffset;
            bufferContents.Stream.Read(data, 0, data.Length);
            texture.LoadImage(data);

            this._assetCache.ImageCache[imageCacheIndex] = texture;
            yield return null;
        }

        protected virtual BufferCacheData ConstructBufferFromGLB(int bufferIndex)
        {
            GLTFParser.SeekToBinaryChunk(
                this._gltfStream.Stream,
                bufferIndex,
                this._gltfStream.StartPosition); // sets stream to correct start position
            return new BufferCacheData
                {Stream = this._gltfStream.Stream, ChunkOffset = this._gltfStream.Stream.Position};
        }

        protected virtual void ApplyTextureTransform(TextureInfo def, Material mat, string texName)
        {
            IExtension extension;
            if (this._gltfRoot.ExtensionsUsed != null
                && this._gltfRoot.ExtensionsUsed.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME)
                && def.Extensions != null
                && def.Extensions.TryGetValue(ExtTextureTransformExtensionFactory.EXTENSION_NAME, out extension))
            {
                var ext = (ExtTextureTransformExtension) extension;

                var temp = ext.Offset.ToUnityVector2Raw();
                temp = new Vector2(temp.x, -temp.y);
                mat.SetTextureOffset(texName, temp);

                mat.SetTextureScale(texName, ext.Scale.ToUnityVector2Raw());
            }
        }

        /// <summary>
        ///	 Get the absolute path to a gltf uri reference.
        /// </summary>
        /// <param name="gltfPath">The path to the gltf file</param>
        /// <returns>A path without the filename or extension</returns>
        protected static string AbsoluteUriPath(string gltfPath)
        {
            var uri = new Uri(gltfPath);
            var partialPath = uri.AbsoluteUri.Remove(
                uri.AbsoluteUri.Length - uri.Query.Length - uri.Segments[uri.Segments.Length - 1].Length);
            return partialPath;
        }

        /// <summary>
        /// Get the absolute path a gltf file directory
        /// </summary>
        /// <param name="gltfPath">The path to the gltf file</param>
        /// <returns>A path without the filename or extension</returns>
        protected static string AbsoluteFilePath(string gltfPath)
        {
            var fileName = Path.GetFileName(gltfPath);
            var lastIndex = gltfPath.IndexOf(fileName);
            var partialPath = gltfPath.Substring(0, lastIndex);
            return partialPath;
        }

        /// <summary>
        /// Cleans up any undisposed streams after loading a scene or a node.
        /// </summary>
        private void Cleanup()
        {
            this._assetCache.Dispose();
            this._assetCache = null;
        }
    }
}