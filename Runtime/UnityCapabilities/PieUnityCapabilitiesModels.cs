using System;
using UnityEngine;

namespace Pie
{
    [Serializable]
    public sealed class PieUnityEnvelope
    {
        public bool ok = true;
        public string service = "pie-unity";
        public string version = PieUnityCapabilitiesConstants.Version;
        public string instanceId = "";
        public string projectPath = "";
        public string mode = "";
        public string kind = "";
        public string name = "";
        public string result = "null";
        public string error = "";
        public string errorCode = "";
        public string serverAvailability = "";
    }

    [Serializable]
    public sealed class PieUnityParameterDescriptor
    {
        public string name;
        public string type;
        public bool required;
    }

    [Serializable]
    public sealed class PieUnityCapabilityDescriptor
    {
        public string kind;
        public string name;
        public string ns;
        public string description;
        public string mode;
        public string availableIn;
        public string capabilityKind;
        public bool readOnly;
        public bool deprecated;
        public bool convenience;
        public bool requiresMainThread;
        public string[] aliases;
        public PieUnityParameterDescriptor[] parameters;
    }

    [Serializable]
    public sealed class PieUnityManifestSummary
    {
        public string service = "pie-unity";
        public string version = PieUnityCapabilitiesConstants.Version;
        public string instanceId = "";
        public string projectPath = "";
        public string mode = "";
        public string[] namespaces;
        public int stableTools;
        public int deprecatedAliases;
    }

    [Serializable]
    public sealed class PieUnityManifestDetail
    {
        public string service = "pie-unity";
        public string version = PieUnityCapabilitiesConstants.Version;
        public string instanceId = "";
        public string projectPath = "";
        public string mode = "";
        public string filterNamespace = "";
        public string filterName = "";
        public PieUnityCapabilityDescriptor[] capabilities;
    }

    [Serializable]
    public sealed class PieUnityHealthPayload
    {
        public string service = "pie-unity";
        public string version = PieUnityCapabilitiesConstants.Version;
        public string instanceId = "";
        public string projectPath = "";
        public string mode = "";
        public int port;
        public bool running = true;
        public bool ready = true;
        public bool domainReloadPending = false;
        public bool bridgeReady = false;
        public string bridgeLastError = "";
        public string bridgeDiagnostic = "";
        public bool mainThreadResponsive = true;
        public int activeHttpRequests = 0;
        public int activeFileRequests = 0;
        public int activeScriptRuns = 0;
        public string availability = "";
    }

    [Serializable]
    public sealed class PieUnityInstance
    {
        public string instanceId;
        public string projectPath;
        public string projectName;
        public string mode;
        public int port;
        public int pid;
        public long lastSeenUnix;
        public string version;
    }

    [Serializable]
    public sealed class PieUnityInstancesPayload
    {
        public string service = "pie-unity";
        public PieUnityInstance[] instances;
    }

    [Serializable]
    public sealed class PieUnityRef
    {
        public string scope = "";
        public string mode = "";
        public string id = "";
        public string path = "";
        public string name = "";
        public string type = "";
        public string scene = "";
        public string parentPath = "";
    }

    [Serializable]
    public sealed class PieUnityQueryPayload
    {
        public string scope = "scene_object";
        public string name = "";
        public string path = "";
        public string type = "";
        public int limit = 20;
    }

    [Serializable]
    public sealed class PieUnityQueryResult
    {
        public string scope = "scene_object";
        public string summary = "";
        public PieUnityRef[] results;
    }

    [Serializable]
    public sealed class PieUnityInspectPayload
    {
        public PieUnityRef target;
    }

    [Serializable]
    public sealed class PieUnityInspectResult
    {
        public string summary = "";
        public PieUnityRef target;
        public bool found;
        public bool activeSelf = true;
        public bool activeInHierarchy = true;
        public int childCount;
        public string[] componentTypes;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
    }

    [Serializable]
    public sealed class PieUnityApplyPayload
    {
        public string action = "";
        public PieUnityRef target;
        public string name = "";
        public string primitiveType = "";
        public PieUnityRef parentRef;
        public string parentName = "";
        public bool active = true;
        public string tag = "";
        public int layer = -1;
        public string componentType = "";
        public bool enabled = true;
        public string propertyName = "";
        public bool applyPosition;
        public float x;
        public float y;
        public float z;
        public bool applyRotation;
        public float rotX;
        public float rotY;
        public float rotZ;
        public bool applyScale;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float scaleZ = 1f;
    }

    [Serializable]
    public sealed class PieUnityApplyResult
    {
        public string action = "";
        public string summary = "";
        public PieUnityRef target;
        public PieUnityRef[] createdRefs;
    }
}
