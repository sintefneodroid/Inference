using UnityEngine;

namespace UnityGLTF {
  class MetalRough2StandardMap : StandardMap,
                                 IMetalRoughUniformMap {
    public MetalRough2StandardMap(int MaxLOD = 1000) : base("Standard", MaxLOD) { }
    protected MetalRough2StandardMap(string shaderName, int MaxLOD = 1000) : base(shaderName, MaxLOD) { }
    protected MetalRough2StandardMap(Material m, int MaxLOD = 1000) : base(m, MaxLOD) { }

    public virtual Texture BaseColorTexture {
      get { return this._material.GetTexture("_MainTex"); }
      set { this._material.SetTexture("_MainTex", value); }
    }

    // not implemented by the Standard shader
    public virtual int BaseColorTexCoord { get { return 0; } set { return; } }

    public virtual Color BaseColorFactor {
      get { return this._material.GetColor("_Color"); }
      set { this._material.SetColor("_Color", value); }
    }

    public virtual Texture MetallicRoughnessTexture {
      get { return null; }
      set {
        // cap metalness at 0.5 to compensate for lack of texture
        this.MetallicFactor = Mathf.Min(0.5f, (float)this.MetallicFactor);
      }
    }

    // not implemented by the Standard shader
    public virtual int MetallicRoughnessTexCoord { get { return 0; } set { return; } }

    public virtual double MetallicFactor {
      get { return this._material.GetFloat("_Metallic"); }
      set { this._material.SetFloat("_Metallic", (float)value); }
    }

    // not supported by the Standard shader
    public virtual double RoughnessFactor { get { return 0.5; } set { return; } }

    public override IUniformMap Clone() {
      var copy = new MetalRough2StandardMap(new Material(this._material));
      base.Copy(copy);
      return copy;
    }
  }
}
