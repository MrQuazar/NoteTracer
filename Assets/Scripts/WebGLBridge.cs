using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Thin wrapper around WebGLAudioUpload.jslib (Assets/Plugins/WebGL). Opens
// the browser's native mp3 picker (result comes back via SendMessage as
// "filename.mp3|<base64>"), and turns an in-memory byte[] into a fetchable
// Blob URL since WebGL can't fetch a "file://" path. No-ops on every other
// platform so callers don't need #if guards.
public static class WebGLBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JS_OpenMp3FileDialog(string gameObjectName, string callbackMethodName);

    [DllImport("__Internal")]
    private static extern IntPtr JS_CreateBlobUrl(byte[] bytes, int length, string mimeType);

    [DllImport("__Internal")]
    private static extern void JS_SyncFileSystem();
#endif

    public static bool IsAvailable
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    public static void OpenMp3FileDialog(string targetGameObjectName, string callbackMethodName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JS_OpenMp3FileDialog(targetGameObjectName, callbackMethodName);
#else
        Debug.LogWarning("WebGLBridge: OpenMp3FileDialog called outside a WebGL build; nothing happens here.");
#endif
    }

    public static string CreateBlobUrl(byte[] bytes, string mimeType)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        IntPtr ptr = JS_CreateBlobUrl(bytes, bytes.Length, mimeType);
        return Marshal.PtrToStringUTF8(ptr);
#else
        Debug.LogWarning("WebGLBridge: CreateBlobUrl called outside a WebGL build; returning null.");
        return null;
#endif
    }

    // Flushes the IndexedDB-backed persistent data path so newly written
    // files survive a page reload. No-op outside WebGL (real disk elsewhere).
    public static void SyncFileSystem()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JS_SyncFileSystem();
#endif
    }
}
