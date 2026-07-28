mergeInto(LibraryManager.library, {

  // Opens the browser's native file picker restricted to .mp3. When the
  // player picks a file, reads it as bytes, base64-encodes it, and sends it
  // back into Unity as a single string payload: "filename.mp3|<base64>",
  // via SendMessage(gameObjectName, callbackMethodName, payload). There is
  // no way to hand an async byte array straight back into a C# extern
  // return value, so a string round-trip through SendMessage is the
  // standard way to bridge this.
  JS_OpenMp3FileDialog: function (gameObjectNamePtr, callbackMethodPtr) {
    var goName = UTF8ToString(gameObjectNamePtr);
    var methodName = UTF8ToString(callbackMethodPtr);

    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.mp3,audio/mpeg';
    input.style.display = 'none';
    document.body.appendChild(input);

    input.onchange = function (event) {
      var file = event.target.files && event.target.files[0];
      if (!file) {
        document.body.removeChild(input);
        return;
      }

      var reader = new FileReader();
      reader.onload = function (loadEvent) {
        var bytes = new Uint8Array(loadEvent.target.result);

        // Chunked to avoid blowing the call stack on String.fromCharCode
        // for larger mp3 files.
        var binary = '';
        var chunkSize = 0x8000;
        for (var i = 0; i < bytes.length; i += chunkSize) {
          var chunk = bytes.subarray(i, i + chunkSize);
          binary += String.fromCharCode.apply(null, chunk);
        }
        var base64 = btoa(binary);
        var payload = file.name + '|' + base64;

        if (typeof unityInstance !== 'undefined' && unityInstance) {
          unityInstance.SendMessage(goName, methodName, payload);
        } else {
          // Older/loader-template builds expose SendMessage as a global.
          SendMessage(goName, methodName, payload);
        }

        document.body.removeChild(input);
      };
      reader.readAsArrayBuffer(file);
    };

    input.click();
  },

  // Wraps a byte array already in Unity's heap into a Blob and returns a
  // Blob URL string (as a malloc'd UTF8 C string Unity can marshal back to
  // a C# string). UnityWebRequestMultimedia can fetch a Blob URL the same
  // way it fetches an http(s) URL, which is how a byte[] read from
  // persistentDataPath gets turned back into a playable AudioClip on WebGL
  // (a plain "file://" path is not fetchable from inside the browser
  // sandbox the way it is on Standalone/Android).
  JS_CreateBlobUrl: function (bytesPtr, length, mimeTypePtr) {
    var mimeType = UTF8ToString(mimeTypePtr);
    var bytes = new Uint8Array(HEAPU8.buffer, bytesPtr, length);
    // Copy out of the heap view before handing to Blob, since the
    // underlying buffer can be detached/resized by later allocations.
    var copy = new Uint8Array(bytes);
    var blob = new Blob([copy], { type: mimeType });
    var url = URL.createObjectURL(blob);

    var bufferSize = lengthBytesUTF8(url) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(url, buffer, bufferSize);
    return buffer;
  },

  // Flushes Emscripten's IndexedDB-backed persistent filesystem so a file
  // just written into persistentDataPath (e.g. an uploaded song) is
  // actually durable across a page reload, not just sitting in the
  // in-memory FS layer.
  JS_SyncFileSystem: function () {
    try {
      if (typeof FS !== 'undefined' && FS.syncfs) {
        FS.syncfs(false, function (err) {
          if (err) console.error('WebGLAudioUpload: FS.syncfs failed', err);
        });
      }
    } catch (e) {
      console.error('WebGLAudioUpload: syncfs threw', e);
    }
  }

});
