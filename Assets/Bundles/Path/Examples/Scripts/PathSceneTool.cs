using Bundles.Path.Core.Scripts.Objects;
using UnityEngine;

namespace Bundles.Path.Examples.Scripts {
  [ExecuteInEditMode]
  public abstract class PathSceneTool : MonoBehaviour {
    public event System.Action onDestroyed;
    public PathCreator pathCreator;
    public bool autoUpdate = true;

    protected VertexPath path { get { return this.pathCreator.Path; } }

    public void CreatePath() {
      if (this.pathCreator != null) {
        this.PathUpdated();
      }
    }

    protected virtual void OnDestroy() {
      if (this.onDestroyed != null) {
        this.onDestroyed();
      }
    }

    protected abstract void PathUpdated();
  }
}
