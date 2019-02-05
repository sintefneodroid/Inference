using System.Collections;
using System.IO;

#if WINDOWS_UWP
using System.Threading.Tasks;
#endif

namespace Bundles.UnityGLTF.Scripts.Loader {
  public interface ILoader {
    IEnumerator LoadStream(string relativeFilePath);

    void LoadStreamSync(string jsonFilePath);

    Stream LoadedStream { get; }

    bool HasSyncLoadMethod { get; }
  }
}
