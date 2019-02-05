using System;

namespace UnityGLTF.Cache {
  public class BufferCacheData : IDisposable {
    public Int64 ChunkOffset { get; set; }
    public System.IO.Stream Stream { get; set; }

    public void Dispose() {
      if (this.Stream != null) {
        this.Stream.Dispose();
        this.Stream = null;
      }
    }
  }
}
