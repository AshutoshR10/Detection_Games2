// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Mediapipe.Unity
{
  public class WebCamSource : ImageSource
  {
    private readonly int _preferableDefaultWidth = 1280;

    private const string _TAG = nameof(WebCamSource);

    private readonly ResolutionStruct[] _defaultAvailableResolutions;

    public WebCamSource(int preferableDefaultWidth, ResolutionStruct[] defaultAvailableResolutions)
    {
      _preferableDefaultWidth = preferableDefaultWidth;
      _defaultAvailableResolutions = defaultAvailableResolutions;
    }

    private static readonly object _PermissionLock = new object();
    private static bool _IsPermitted = false;

    private WebCamTexture _webCamTexture;
    private WebCamTexture webCamTexture
    {
      get => _webCamTexture;
      set
      {
        if (_webCamTexture != null)
        {
          _webCamTexture.Stop();
        }
        _webCamTexture = value;
      }
    }

    public override int textureWidth => !isPrepared ? 0 : webCamTexture.width;
    public override int textureHeight => !isPrepared ? 0 : webCamTexture.height;

    public override bool isVerticallyFlipped => isPrepared && webCamTexture.videoVerticallyMirrored;
    public override bool isFrontFacing => isPrepared && (webCamDevice is WebCamDevice valueOfWebCamDevice) && valueOfWebCamDevice.isFrontFacing;
    public override RotationAngle rotation => !isPrepared ? RotationAngle.Rotation0 : (RotationAngle)webCamTexture.videoRotationAngle;

    private WebCamDevice? _webCamDevice;
    private WebCamDevice? webCamDevice
    {
      get => _webCamDevice;
      set
      {
        if (_webCamDevice is WebCamDevice valueOfWebCamDevice)
        {
          if (value is WebCamDevice valueOfValue && valueOfValue.name == valueOfWebCamDevice.name)
          {
            // not changed
            return;
          }
        }
        else if (value == null)
        {
          // not changed
          return;
        }
        _webCamDevice = value;
        resolution = GetDefaultResolution();
      }
    }
    public override string sourceName => (webCamDevice is WebCamDevice valueOfWebCamDevice) ? valueOfWebCamDevice.name : null;

    private WebCamDevice[] _availableSources;
    private WebCamDevice[] availableSources
    {
      get
      {
        if (_availableSources == null)
        {
#if UNITY_ANDROID
          // On Android, camera enumeration requires permission first
          if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
          {
            Debug.LogWarning("Camera permission not granted yet. Cannot enumerate cameras.");
            return new WebCamDevice[0]; // Return empty array, will be populated after permission granted
          }
#endif
          _availableSources = WebCamTexture.devices;
        }

        return _availableSources;
      }
      set => _availableSources = value;
    }

    public override string[] sourceCandidateNames => availableSources?.Select(device => device.name).ToArray();

#pragma warning disable IDE0025
    public override ResolutionStruct[] availableResolutions
    {
      get
      {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (webCamDevice is WebCamDevice valueOfWebCamDevice) {
          return valueOfWebCamDevice.availableResolutions.Select(resolution => new ResolutionStruct(resolution)).ToArray();
        }
#endif
        return webCamDevice == null ? null : _defaultAvailableResolutions;
      }
    }
#pragma warning restore IDE0025

    public override bool isPrepared => webCamTexture != null;
    public override bool isPlaying => webCamTexture != null && webCamTexture.isPlaying;

    private IEnumerator Initialize()
    {
      yield return GetPermission();

      if (!_IsPermitted)
      {
        yield break;
      }

      if (webCamDevice != null)
      {
        yield break;
      }

#if UNITY_ANDROID
      // On Android, wait a moment after permission is granted for camera list to refresh
      Debug.Log("Waiting for camera devices to be available...");
      yield return new WaitForSeconds(0.5f);

      // Force re-enumeration of cameras now that permission is granted
      _availableSources = null;
#endif

      availableSources = WebCamTexture.devices;
      Debug.Log($"Found {availableSources?.Length ?? 0} camera device(s)");

      if (availableSources != null && availableSources.Length > 0)
      {
        // Prefer front-facing camera (for selfie mode on phones)
        var frontCamera = availableSources.FirstOrDefault(cam => cam.isFrontFacing);

        if (frontCamera.name != null)
        {
          webCamDevice = frontCamera;
          Debug.Log($"Selected FRONT camera: {frontCamera.name}");
        }
        else
        {
          // Fallback to first available camera if no front camera found
          webCamDevice = availableSources[0];
          Debug.Log($"No front camera found, using: {availableSources[0].name}");
        }
      }
      else
      {
        Debug.LogWarning("No camera devices found!");
      }
    }

    private IEnumerator GetPermission()
    {
      lock (_PermissionLock)
      {
        if (_IsPermitted)
        {
          yield break;
        }

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
          Debug.Log("Requesting camera permission...");
          Permission.RequestUserPermission(Permission.Camera);

          // Wait for user to respond to permission dialog (max 10 seconds)
          var waitTime = 0f;
          var maxWaitTime = 10f;
          while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && waitTime < maxWaitTime)
          {
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
          }

          Debug.Log($"Camera permission status after {waitTime}s: {Permission.HasUserAuthorizedPermission(Permission.Camera)}");
        }
#elif UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) {
          yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }
#endif

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
          Debug.LogWarning("Not permitted to use Camera - User denied or timeout");
          yield break;
        }
#elif UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) {
          Debug.LogWarning("Not permitted to use WebCam");
          yield break;
        }
#endif
        _IsPermitted = true;

        yield return new WaitForEndOfFrame();
      }
    }

    public override void SelectSource(int sourceId)
    {
      if (sourceId < 0 || sourceId >= availableSources.Length)
      {
        throw new ArgumentException($"Invalid source ID: {sourceId}");
      }

      webCamDevice = availableSources[sourceId];
    }

    public override IEnumerator Play()
    {
      yield return Initialize();
      if (!_IsPermitted)
      {
        throw new InvalidOperationException("Not permitted to access cameras");
      }

      InitializeWebCamTexture();
      webCamTexture.Play();
      yield return WaitForWebCamTexture();
    }

    public override IEnumerator Resume()
    {
      if (!isPrepared)
      {
        throw new InvalidOperationException("WebCamTexture is not prepared yet");
      }
      if (!webCamTexture.isPlaying)
      {
        webCamTexture.Play();
      }
      yield return WaitForWebCamTexture();
    }

    public override void Pause()
    {
      if (isPlaying)
      {
        webCamTexture.Pause();
      }
    }

    public override void Stop()
    {
      if (webCamTexture != null)
      {
        webCamTexture.Stop();
      }
      webCamTexture = null;
    }

    public override Texture GetCurrentTexture() => webCamTexture;

    private ResolutionStruct GetDefaultResolution()
    {
      var resolutions = availableResolutions;
      return resolutions == null || resolutions.Length == 0 ? new ResolutionStruct() : resolutions.OrderBy(resolution => resolution, new ResolutionStructComparer(_preferableDefaultWidth)).First();
    }

    private void InitializeWebCamTexture()
    {
      Stop();
      if (webCamDevice is WebCamDevice valueOfWebCamDevice)
      {
        webCamTexture = new WebCamTexture(valueOfWebCamDevice.name, resolution.width, resolution.height, (int)resolution.frameRate);
        return;
      }
      throw new InvalidOperationException("Cannot initialize WebCamTexture because WebCamDevice is not selected");
    }

    private IEnumerator WaitForWebCamTexture()
    {
      const int timeoutFrame = 2000;
      var count = 0;
      Debug.Log("Waiting for WebCamTexture to start");
      yield return new WaitUntil(() => count++ > timeoutFrame || webCamTexture.width > 16);

      if (webCamTexture.width <= 16)
      {
        throw new TimeoutException("Failed to start WebCam");
      }
    }

    private class ResolutionStructComparer : IComparer<ResolutionStruct>
    {
      private readonly int _preferableDefaultWidth;

      public ResolutionStructComparer(int preferableDefaultWidth)
      {
        _preferableDefaultWidth = preferableDefaultWidth;
      }

      public int Compare(ResolutionStruct a, ResolutionStruct b)
      {
        var aDiff = Mathf.Abs(a.width - _preferableDefaultWidth);
        var bDiff = Mathf.Abs(b.width - _preferableDefaultWidth);
        if (aDiff != bDiff)
        {
          return aDiff - bDiff;
        }
        if (a.height != b.height)
        {
          // prefer smaller height
          return a.height - b.height;
        }
        // prefer smaller frame rate
        return (int)(a.frameRate - b.frameRate);
      }
    }
  }
}
