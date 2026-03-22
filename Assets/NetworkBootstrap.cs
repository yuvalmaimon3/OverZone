using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Minimal local multiplayer bootstrap for Unity 6 hobby workflow.
/// Uses reflection so project compiles even before NGO package is installed.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    [Header("Debug UI")]
    public bool showDebugOnGui = false;

    private object networkManagerInstance;
    private Type networkManagerType;

    private string status = "Not initialized";

    void Awake()
    {
        TryBindNetworkManager();
    }

    void Update()
    {
        if (!EnsureNetworkManagerReady())
            return;

        if (IsNetworkListening())
        {
            bool isHost = ReadBoolProperty("IsHost");
            bool isServer = ReadBoolProperty("IsServer");
            bool isClient = ReadBoolProperty("IsClient");
            status = $"Running ({(isHost ? "Host" : isServer ? "Server" : isClient ? "Client" : "Unknown")})";
        }
        else if (status.StartsWith("Running", StringComparison.Ordinal))
        {
            status = "Ready";
        }
    }

    void OnGUI()
    {
        if (!showDebugOnGui)
            return;

        const int w = 220;
        const int h = 34;
        int x = 12;
        int y = 12;

        GUI.Label(new Rect(x, y, 520, 24), $"Networking: {status}");
        y += 28;

        bool isListening = IsNetworkListening();

        GUI.enabled = !isListening;
        if (GUI.Button(new Rect(x, y, w, h), "Start Host"))
            StartHost();
        y += h + 8;

        if (GUI.Button(new Rect(x, y, w, h), "Start Client"))
            StartClient();
        y += h + 8;

        GUI.enabled = isListening && ReadBoolProperty("IsHost");
        if (GUI.Button(new Rect(x, y, w, h), "Restart Match (Host)"))
            RestartMatchAsHost();
        y += h + 8;

        GUI.enabled = isListening;
        if (GUI.Button(new Rect(x, y, w, h), "Shutdown"))
            Shutdown();
        y += h + 8;

        GUI.enabled = true;
        if (GUI.Button(new Rect(x, y, w, h), "Exit Game"))
            ExitGame();
        GUI.enabled = true;
    }

    public void StartHost()
    {
        if (!EnsureNetworkManagerReady())
            return;

        if (IsNetworkListening())
        {
            status = "Already running";
            return;
        }

        // Prevent noisy transport error when another process already hosts on this port.
        const int defaultPort = 7777;
        if (!IsUdpPortAvailable(defaultPort))
        {
            status = $"Port {defaultPort} is in use. Close other host or press Start Client.";
            return;
        }

        if (InvokeNetworkManagerBoolMethod("StartHost"))
        {
            status = "Host started";
            SetupCamera(isHost: true);
        }
        else
            status = "Failed to start host";
    }

    public void StartClient()
    {
        if (!EnsureNetworkManagerReady())
            return;

        if (IsNetworkListening())
        {
            status = "Already running";
            return;
        }

        if (InvokeNetworkManagerBoolMethod("StartClient"))
        {
            status = "Client started";
            SetupCamera(isHost: false);
        }
        else
            status = "Failed to start client";
    }

    private void SetupCamera(bool isHost)
    {
        var cam = FindFirstObjectByType<FieldCameraController>();
        if (cam == null) return;

        if (isHost)
            cam.SetupForHost();
        else
            cam.SetupForClient();
    }

    public void Shutdown()
    {
        if (!EnsureNetworkManagerReady())
            return;

        if (!IsNetworkListening())
        {
            status = "Already stopped";
            return;
        }

        InvokeNetworkManagerVoidMethod("Shutdown");
        status = "Shutdown";
    }

    public void RestartMatchAsHost()
    {
        if (!EnsureNetworkManagerReady())
            return;

        if (!IsNetworkListening() || !ReadBoolProperty("IsHost"))
        {
            status = "Only host can restart";
            return;
        }

        // Best sync path: host reloads scene via NGO scene manager.
        if (TryReloadSceneThroughNetwork())
            status = "Restart requested by host";
        else
            status = "Failed to restart match";
    }

    public void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private bool EnsureNetworkManagerReady()
    {
        if (networkManagerInstance != null)
            return true;

        TryBindNetworkManager();
        return networkManagerInstance != null;
    }

    private void TryBindNetworkManager()
    {
        // NGO package type name:
        networkManagerType = Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        if (networkManagerType == null)
        {
            status = "NGO package missing. Install com.unity.netcode.gameobjects.";
            return;
        }

        UnityEngine.Object managerObj = FindFirstObjectByType(networkManagerType);
        if (managerObj == null)
        {
            status = "Add NetworkManager object (Component: NetworkManager).";
            return;
        }

        networkManagerInstance = managerObj;
        status = "Ready";
    }

    private bool InvokeNetworkManagerBoolMethod(string methodName)
    {
        MethodInfo method = networkManagerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        if (method == null)
            return false;

        object result = method.Invoke(networkManagerInstance, null);
        return result is bool ok && ok;
    }

    private void InvokeNetworkManagerVoidMethod(string methodName)
    {
        MethodInfo method = networkManagerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method?.Invoke(networkManagerInstance, null);
    }

    private bool IsNetworkListening()
    {
        return ReadBoolProperty("IsListening");
    }

    private bool ReadBoolProperty(string propertyName)
    {
        if (networkManagerType == null || networkManagerInstance == null)
            return false;

        PropertyInfo prop = networkManagerType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
            return false;

        object value = prop.GetValue(networkManagerInstance);
        return value is bool b && b;
    }

    private bool TryReloadSceneThroughNetwork()
    {
        PropertyInfo sceneManagerProp = networkManagerType.GetProperty("SceneManager", BindingFlags.Public | BindingFlags.Instance);
        if (sceneManagerProp == null)
            return false;

        object netSceneManager = sceneManagerProp.GetValue(networkManagerInstance);
        if (netSceneManager == null)
            return false;

        Type netSceneManagerType = netSceneManager.GetType();
        MethodInfo loadSceneMethod = netSceneManagerType.GetMethod("LoadScene", new[] { typeof(string), typeof(LoadSceneMode) });
        if (loadSceneMethod == null)
            return false;

        string currentSceneName = SceneManager.GetActiveScene().name;
        object result = loadSceneMethod.Invoke(netSceneManager, new object[] { currentSceneName, LoadSceneMode.Single });
        return result != null;
    }

    private static bool IsUdpPortAvailable(int port)
    {
        UdpClient udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            udp?.Close();
        }
    }
}

