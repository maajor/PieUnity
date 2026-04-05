using UnityEditor;
using UnityEngine;

namespace Pie.Editor
{
    [InitializeOnLoad]
    internal static class PieDevRpcEditorBootstrap
    {
        private static bool _registeredUpdate;
        [System.Serializable]
        private class ThreadInfoPayload
        {
            public int mainThreadId;
            public int currentThreadId;
        }

        static PieDevRpcEditorBootstrap()
        {
            if (!_registeredUpdate)
            {
                EditorApplication.update += PieDevRpcDispatcher.Tick;
                _registeredUpdate = true;
            }
            EditorApplication.delayCall += PieDevRpcDispatcher.InitializeMainThread;
            PieDevRpcServer.Start();
            PieHostBridge.Register("pie.dev_rpc", PieDevRpc.InvokeHostCall);
            PieDevRpc.Register("rpc.thread_info", _ => JsonUtility.ToJson(new ThreadInfoPayload
            {
                mainThreadId = PieDevRpcDispatcher.MainThreadId,
                currentThreadId = PieDevRpcDispatcher.CurrentThreadId,
            }));

            PieChatWindow.EnsureDevRpcReady();
        }
    }
}
