using GLTF;

namespace Bundles.UnityGLTF.Scripts.Cache {
  public struct AnimationSamplerCacheData {
    public AttributeAccessor Input;
    public AttributeAccessor Output;
  }

  public class AnimationCacheData {
    public UnityEngine.AnimationClip LoadedAnimationClip { get; set; }
    public AnimationSamplerCacheData[] Samplers { get; set; }

    public AnimationCacheData(int samplerCount) { this.Samplers = new AnimationSamplerCacheData[samplerCount]; }
  }
}
