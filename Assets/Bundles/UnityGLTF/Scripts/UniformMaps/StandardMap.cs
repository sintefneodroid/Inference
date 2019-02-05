using System;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using Material = UnityEngine.Material;
using Texture = UnityEngine.Texture;

namespace UnityGLTF {
  class StandardMap : IUniformMap {
    protected Material _material;
    private AlphaMode _alphaMode = AlphaMode.OPAQUE;
    private double _alphaCutoff = 0.5;

    protected StandardMap(string shaderName, int MaxLOD = 1000) {
      var s = Shader.Find(shaderName);
      if (s == null) {
        throw new ShaderNotFoundException(shaderName + " not found. Did you forget to add it to the build?");
      }

      s.maximumLOD = MaxLOD;
      this._material = new Material(s);
    }

    protected StandardMap(Material mat, int MaxLOD = 1000) {
      mat.shader.maximumLOD = MaxLOD;
      this._material = mat;

      if (mat.HasProperty("_Cutoff")) {
        this._alphaCutoff = mat.GetFloat("_Cutoff");
      }

      switch (mat.renderQueue) {
        case (int)RenderQueue.AlphaTest:
          this._alphaMode = AlphaMode.MASK;
          break;
        case (int)RenderQueue.Transparent:
          this._alphaMode = AlphaMode.BLEND;
          break;
        case (int)RenderQueue.Geometry:
        default:
          this._alphaMode = AlphaMode.OPAQUE;
          break;
      }
    }

    public Material Material { get { return this._material; } }

    public virtual Texture NormalTexture {
      get { return this._material.HasProperty("_BumpMap") ? this._material.GetTexture("_BumpMap") : null; }
      set {
        if (this._material.HasProperty("_BumpMap")) {
          this._material.SetTexture("_BumpMap", value);
          this._material.EnableKeyword("_NORMALMAP");
        } else {
          Debug.LogWarning("Tried to set a normal map texture to a material that does not support it.");
        }
      }
    }

    // not implemented by the Standard shader
    public virtual int NormalTexCoord { get { return 0; } set { return; } }

    public virtual double NormalTexScale {
      get { return this._material.HasProperty("_BumpScale") ? this._material.GetFloat("_BumpScale") : 1; }
      set {
        if (this._material.HasProperty("_BumpScale")) {
          this._material.SetFloat("_BumpScale", (float)value);
        } else {
          Debug.LogWarning("Tried to set a normal map scale to a material that does not support it.");
        }
      }
    }

    public virtual Texture OcclusionTexture {
      get { return this._material.HasProperty("_OcclusionMap") ? this._material.GetTexture("_OcclusionMap") : null; }
      set {
        if (this._material.HasProperty("_OcclusionMap")) {
          this._material.SetTexture("_OcclusionMap", value);
        } else {
          Debug.LogWarning("Tried to set an occlusion map to a material that does not support it.");
        }
      }
    }

    // not implemented by the Standard shader
    public virtual int OcclusionTexCoord { get { return 0; } set { return; } }

    public virtual double OcclusionTexStrength {
      get {
        return this._material.HasProperty("_OcclusionStrength") ? this._material.GetFloat("_OcclusionStrength") : 1;
      }
      set {
        if (this._material.HasProperty("_OcclusionStrength")) {
          this._material.SetFloat("_OcclusionStrength", (float)value);
        } else {
          Debug.LogWarning("Tried to set occlusion strength to a material that does not support it.");
        }
      }
    }

    public virtual Texture EmissiveTexture {
      get { return this._material.HasProperty("_EmissionMap") ? this._material.GetTexture("_EmissionMap") : null; }
      set {
        if (this._material.HasProperty("_EmissionMap")) {
          this._material.SetTexture("_EmissionMap", value);
          this._material.EnableKeyword("_EMISSION");
        } else {
          Debug.LogWarning("Tried to set an emission map to a material that does not support it.");
        }
      }
    }

    // not implemented by the Standard shader
    public virtual int EmissiveTexCoord { get { return 0; } set { return; } }

    public virtual Color EmissiveFactor {
      get {
        return this._material.HasProperty("_EmissionColor") ? this._material.GetColor("_EmissionColor") : Color.white;
      }
      set {
        if (this._material.HasProperty("_EmissionColor")) {
          this._material.SetColor("_EmissionColor", value);
        } else {
          Debug.LogWarning("Tried to set an emission factor to a material that does not support it.");
        }
      }
    }

    public virtual AlphaMode AlphaMode {
      get { return this._alphaMode; }
      set {
        if (value == AlphaMode.MASK) {
          this._material.SetOverrideTag("RenderType", "TransparentCutout");
          this._material.SetInt("_SrcBlend", (int)BlendMode.One);
          this._material.SetInt("_DstBlend", (int)BlendMode.Zero);
          this._material.SetInt("_ZWrite", 1);
          this._material.EnableKeyword("_ALPHATEST_ON");
          this._material.DisableKeyword("_ALPHABLEND_ON");
          this._material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
          this._material.renderQueue = (int)RenderQueue.AlphaTest;
          if (this._material.HasProperty("_Cutoff")) {
            this._material.SetFloat("_Cutoff", (float)this._alphaCutoff);
          }
        } else if (value == AlphaMode.BLEND) {
          this._material.SetOverrideTag("RenderType", "Transparent");
          this._material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
          this._material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
          this._material.SetInt("_ZWrite", 0);
          this._material.DisableKeyword("_ALPHATEST_ON");
          this._material.EnableKeyword("_ALPHABLEND_ON");
          this._material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
          this._material.renderQueue = (int)RenderQueue.Transparent;
        } else {
          this._material.SetOverrideTag("RenderType", "Opaque");
          this._material.SetInt("_SrcBlend", (int)BlendMode.One);
          this._material.SetInt("_DstBlend", (int)BlendMode.Zero);
          this._material.SetInt("_ZWrite", 1);
          this._material.DisableKeyword("_ALPHATEST_ON");
          this._material.DisableKeyword("_ALPHABLEND_ON");
          this._material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
          this._material.renderQueue = (int)RenderQueue.Geometry;
        }

        this._alphaMode = value;
      }
    }

    public virtual double AlphaCutoff {
      get { return this._alphaCutoff; }
      set {
        if ((this._alphaMode == AlphaMode.MASK) && this._material.HasProperty("_Cutoff")) {
          this._material.SetFloat("_Cutoff", (float)value);
        }

        this._alphaCutoff = value;
      }
    }

    public virtual bool DoubleSided {
      get { return this._material.GetInt("_Cull") == (int)CullMode.Off; }
      set {
        if (value)
          this._material.SetInt("_Cull", (int)CullMode.Off);
        else
          this._material.SetInt("_Cull", (int)CullMode.Back);
      }
    }

    public virtual bool VertexColorsEnabled {
      get { return this._material.IsKeywordEnabled("VERTEX_COLOR_ON"); }
      set {
        if (value)
          this._material.EnableKeyword("VERTEX_COLOR_ON");
        else
          this._material.DisableKeyword("VERTEX_COLOR_ON");
      }
    }

    public virtual IUniformMap Clone() {
      var ret = new StandardMap(new Material(this._material));
      ret._alphaMode = this._alphaMode;
      ret._alphaCutoff = this._alphaCutoff;
      return ret;
    }

    protected virtual void Copy(IUniformMap o) {
      var other = (StandardMap)o;
      other._material = this._material;
      other._alphaCutoff = this._alphaCutoff;
      other._alphaMode = this._alphaMode;
    }
  }
}
