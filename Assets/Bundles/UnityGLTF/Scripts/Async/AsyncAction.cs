using System;
using System.Collections;
using System.Threading;
#if WINDOWS_UWP
using System.Threading.Tasks;
#else

#endif

namespace Bundles.UnityGLTF.Scripts.Async {
  /// <summary>
  /// Creates a thread to run multithreaded operations on
  /// </summary>
  public class AsyncAction {
    private bool _workerThreadRunning = false;
    private Exception _savedException;

    public IEnumerator RunOnWorkerThread(Action action) {
      this._workerThreadRunning = true;

      #if WINDOWS_UWP
			Task.Factory.StartNew(() =>
       {
            try {
              action();
            } catch (Exception e) {
              this._savedException = e;
            }

            this._workerThreadRunning = false;
          });
      #else
      ThreadPool.QueueUserWorkItem(
          (_) => {
            try {
              action();
            } catch (Exception e) {
              this._savedException = e;
            }

            this._workerThreadRunning = false;
          });
      #endif

      yield return this.Wait();

      if (this._savedException != null) {
        throw this._savedException;
      }
    }

    private IEnumerator Wait() {
      while (this._workerThreadRunning) {
        yield return null;
      }
    }
  }
}
