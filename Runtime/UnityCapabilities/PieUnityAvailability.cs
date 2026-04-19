#if PIE_UNITY_SPLIT_SOURCES
namespace Pie
{
    public static class PieUnityAvailability
    {
        public static string GetAvailabilityNoticeJson()
        {
#if UNITY_EDITOR
            if (PieDevRpcDispatcher.MainThreadId >= 0 && PieDevRpcDispatcher.CurrentThreadId != PieDevRpcDispatcher.MainThreadId)
            {
                return "";
            }

            if (UnityEditor.EditorApplication.isCompiling)
            {
                return "{\"status\":\"temporarily_unavailable\",\"reason\":\"compiling\",\"message\":\"Unity is compiling scripts. Wait for compilation to finish and retry.\"}";
            }

            if (UnityEditor.EditorApplication.isUpdating)
            {
                return "{\"status\":\"temporarily_unavailable\",\"reason\":\"updating\",\"message\":\"Unity is refreshing assets or updating the editor state. Retry shortly.\"}";
            }
#endif
            return "";
        }
    }
}

#endif
