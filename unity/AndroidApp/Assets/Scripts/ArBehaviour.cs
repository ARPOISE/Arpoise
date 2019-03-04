﻿/*
ArBehaviour.cs - MonoBehaviour for Arpoise.

Copyright (C) 2018, Tamiko Thiel and Peter Graf - All Rights Reserved

ARPOISE - Augmented Reality Point Of Interest Service 

This file is part of Arpoise.

    Arpoise is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Arpoise is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Arpoise.  If not, see <https://www.gnu.org/licenses/>.

For more information on 

Tamiko Thiel, see www.TamikoThiel.com/
Peter Graf, see www.mission-base.com/peter/
Arpoise, see www.Arpoise.com/

*/

//#define DEVEL

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Vuforia;

namespace com.arpoise.arpoiseapp
{
    public class RefreshRequest
    {
        public string url;
        public string layerName;
        public float? latitude;
        public float? longitude;
    }

    public class ArBehaviour : MonoBehaviour
    {
        private static readonly string _selectingText = "Please select a layer.";
        private static readonly string _loadingText = "Loading data, please wait";
#if DEVEL
        private static readonly string _arpoiseDirectoryLayer = "Arpoise-Directory";
        private static readonly string _arpoiseDirectoryUrl = "http://www.arpoise.com/cgi-bin/ArpoiseDirectory.cgi";
#else
        private static readonly string _arpoiseDirectoryLayer = "Arpoise-Directory";
        private static readonly string _arpoiseDirectoryUrl = "http://www.arpoise.com/cgi-bin/ArpoiseDirectory.cgi";
#endif
        private double _locationTimestamp = 0;
        private float _locationHorizontalAccuracy = 0;
        private float _locationLongitude = 0;
        private float _locationLatitude = 0;

        protected ArObject LastObject = null;
        protected float OriginalLatitude = 0;
        protected float OriginalLongitude = 0;
        protected float InitialHeading = 0;
        protected float InitialCameraAngle = 0;

        private float _filteredLongitude = 0;
        private float _filteredLatitude = 0;
        private float _currentHeading = 0;
        private float _headingShown = 0;

        private bool _cameraIsInitializing = true;

        protected DeviceOrientation InitialDeviceOrientation = DeviceOrientation.LandscapeLeft;
        private Transform _cameraTransform = null;

        private string _informationMessage = null;
        private bool _showInfo = false;
        private string _error = null;

        private readonly Dictionary<string, List<ArLayer>> _innerLayers = new Dictionary<string, List<ArLayer>>();
        private readonly Dictionary<string, AssetBundle> _assetBundles = new Dictionary<string, AssetBundle>();
        private ArObjectState _arObjectState = null;

        private long _startTicks = 0;
        private static readonly long _initialSecond = DateTime.Now.Ticks / 10000000L;
        private long _currentSecond = _initialSecond;
        private int _framesPerSecond = 30;
        private int _framesPerCurrentSecond = 1;

        private bool _applyKalmanFilter = true;
        private volatile RefreshRequest _refreshRequest = null;
        private float _refreshInterval = 0;
        private int _bleachingValue = -1;
        private int _areaSize = 0;
        private int _areaWidth = 0;

        // Not string is null or white space
        public bool IsEmpty(string s)
        {
            return s == null || string.IsNullOrEmpty(s.Trim());
        }

        private bool _headerButtonActivated = false;

        #region Globals

        public GameObject SceneAnchor = null;
        public GameObject InfoText = null;
        public GameObject Wrapper = null;
        public GameObject HeaderText = null;
        public GameObject LayerPanel = null;
        public GameObject HeaderButton = null;
        public GameObject MenuButton = null;
        public GameObject PanelHeaderButton = null;
        public Transform ContentPanel;
        public SimpleObjectPool ButtonObjectPool;
        public GameObject InputPanel;

        #endregion

        protected bool InputPanelEnabled = true;
        protected bool? MenuEnabled = null;
        private List<Item> _layerItemList = null;
        private ArLayerScrollList _layerScrollList = null;
        private bool _waitingForLayerSelection = false;

        #region Buttons

        public void HandleInputPanelClosed(float? latitude, float? longitude)
        {
            Debug.Log("HandleInputPanelClosed lat " + latitude + " lon " + longitude);

            _refreshRequest = new RefreshRequest
            {
                url = _arpoiseDirectoryUrl,
                layerName = _arpoiseDirectoryLayer,
                latitude = latitude,
                longitude = longitude
            };
        }

        private long _lastButtonSecond = 0;

        public void HandleMenuButtonClick()
        {
            if (InputPanelEnabled)
            {
                var second = DateTime.Now.Ticks / 10000000L;
                if (_lastButtonSecond == second)
                {
                    InputPanel.SetActive(true);
                    var inputPanel = InputPanel.GetComponent<InputPanel>();
                    inputPanel.Activate(this);
                    return;
                }
                _lastButtonSecond = second;
            }

            List<Item> itemList = _layerItemList;
            if (MenuEnabled.HasValue && MenuEnabled.Value && itemList != null && itemList.Any())
            {
                if (_layerScrollList != null)
                {
                    _layerScrollList.RemoveButtons();
                }

                _layerScrollList = new ArLayerScrollList(ContentPanel, ButtonObjectPool);
                _layerScrollList.AddButtons(itemList, this);

                InputPanel.SetActive(false);
                HeaderButton.SetActive(false);
                MenuButton.SetActive(false);
                LayerPanel.SetActive(true);
            }
        }

        public void HandlePanelHeaderButtonClick()
        {
            if (MenuEnabled.HasValue && MenuEnabled.Value)
            {
                HeaderButton.SetActive(_headerButtonActivated);
                MenuButton.SetActive(MenuEnabled.HasValue && MenuEnabled.Value);
                LayerPanel.SetActive(false);
                if (_layerScrollList != null)
                {
                    _layerScrollList.RemoveButtons();
                }
            }
            if (InputPanelEnabled)
            {
                var second = DateTime.Now.Ticks / 10000000L;
                if (_lastButtonSecond == second)
                {
                    InputPanel.SetActive(true);
                    InputPanel inputPanel = InputPanel.GetComponent<InputPanel>();
                    inputPanel.Activate(this);
                }
                _lastButtonSecond = second;
            }
        }

        public void HandleLayerButtonClick(Item item)
        {
            if (item != null && !IsEmpty(item.layerName) && !IsEmpty(item.url))
            {
                Debug.Log("HandleLayerButtonClick " + item.itemName);

                _refreshRequest = new RefreshRequest
                {
                    url = item.url,
                    layerName = item.layerName,
                    latitude = _fixedDeviceLatitude,
                    longitude = _fixedDeviceLongitude
                };
            }
            if (MenuEnabled.HasValue && MenuEnabled.Value)
            {
                HeaderButton.SetActive(_headerButtonActivated);
                MenuButton.SetActive(MenuEnabled.HasValue && MenuEnabled.Value);
                LayerPanel.SetActive(false);
                if (_layerScrollList != null)
                {
                    _layerScrollList.RemoveButtons();
                }
            }
        }

        #endregion

        #region GetData

        // Link ar object to ar object state or to parent object
        private string LinkArObject(ArObjectState arObjectState, ArObject parentObject, Transform parentTransform, ArObject arObject, GameObject arGameObject, Poi poi)
        {
            if (parentObject == null)
            {
                // Add to ar object state
                arObjectState.ArObjects.Add(arObject);

                List<ArLayer> innerLayers = null;
                if (!IsEmpty(poi.InnerLayerName) && _innerLayers.TryGetValue(poi.InnerLayerName, out innerLayers))
                {
                    foreach (var layer in innerLayers.Where(x => x.hotspots != null))
                    {
                        var result = CreateArObjects(arObjectState, arObject, parentTransform, layer.hotspots);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }
            else
            {
                // Add to parent object
                parentObject.GameObjects.Add(arGameObject);
                parentObject.ArObjects.Add(arObject);
            }
            return null;
        }

        // Create ar objects for the pois and link them
        private string CreateArObjects(ArObjectState arObjectState, ArObject parentObject, Transform parentObjectTransform, IEnumerable<Poi> pois)
        {
            foreach (var poi in pois.Where(x => x.isVisible && !IsEmpty(x.GameObjectName)))
            {
                long poiId = poi.id;
                if (parentObject != null)
                {
                    poiId = -1000000 * parentObject.Id - poiId;
                }

                string assetBundleUrl = poi.BaseUrl;
                if (IsEmpty(assetBundleUrl))
                {
                    return "Poi with id " + poiId + ", empty asset bundle url'";
                }

                AssetBundle assetBundle = null;
                if (!_assetBundles.TryGetValue(assetBundleUrl, out assetBundle))
                {
                    return "?: '" + assetBundleUrl + "'";
                }

                string objectName = poi.GameObjectName;
                if (IsEmpty(objectName))
                {
                    continue;
                }

                var objectToAdd = assetBundle.LoadAsset<GameObject>(objectName);
                if (objectToAdd == null)
                {
                    return "Poi with id " + poiId + ", unknown game object: '" + objectName + "'";
                }

                // All objects are below the scene anchor or the parent
                var parentTransform = parentObjectTransform;

                // Create a copy of the object
                objectToAdd = Instantiate(objectToAdd);
                if (objectToAdd == null)
                {
                    return "Instantiate(" + objectName + ") failed";
                }

                // Wrap the object into a wrapper, so it can be moved around when the device moves
                var wrapper = Instantiate(Wrapper);
                if (wrapper == null)
                {
                    return "Instantiate(_wrapper) failed";
                }
                wrapper.transform.parent = parentTransform;
                parentTransform = wrapper.transform;

                // Add a wrapper for scaling
                var scaleWrapper = Instantiate(Wrapper);
                if (scaleWrapper == null)
                {
                    return "Instantiate(_wrapper) failed";
                }
                scaleWrapper.transform.parent = parentTransform;
                parentTransform = scaleWrapper.transform;

                // Prepare the relative rotation of the object - billboard handling
                if (poi.transform != null && poi.transform.rel)
                {
                    var billboardWrapper = Instantiate(Wrapper);
                    if (billboardWrapper == null)
                    {
                        return "Instantiate(_wrapper) failed";
                    }
                    billboardWrapper.transform.parent = parentTransform;
                    parentTransform = billboardWrapper.transform;
                    arObjectState.BillboardAnimations.Add(new ArAnimation(poiId, billboardWrapper, objectToAdd, null, true));
                }

                // Prepare the rotation of the object
                GameObject rotationWrapper = null;
                if (poi.transform != null && poi.transform.angle != 0)
                {
                    rotationWrapper = Instantiate(Wrapper);
                    if (rotationWrapper == null)
                    {
                        return "Instantiate(_wrapper) failed";
                    }
                    rotationWrapper.transform.parent = parentTransform;
                    parentTransform = rotationWrapper.transform;
                }

                // Look at the animations present for the object
                if (poi.animations != null)
                {
                    if (poi.animations.onCreate != null)
                    {
                        foreach (var poiAnimation in poi.animations.onCreate)
                        {
                            // Put the animation into a wrapper
                            var animationWrapper = Instantiate(Wrapper);
                            if (animationWrapper == null)
                            {
                                return "Instantiate(_wrapper) failed";
                            }
                            arObjectState.OnCreateAnimations.Add(new ArAnimation(poiId, animationWrapper, objectToAdd, poiAnimation, true));
                            animationWrapper.transform.parent = parentTransform;
                            parentTransform = animationWrapper.transform;
                        }
                    }

                    if (poi.animations.onFocus != null)
                    {
                        foreach (var poiAnimation in poi.animations.onFocus)
                        {
                            SphereCollider sc = objectToAdd.AddComponent<SphereCollider>() as SphereCollider;
                            sc.radius = .71f; // 2 ** -2 / 2

                            // Put the animation into a wrapper
                            var animationWrapper = Instantiate(Wrapper);
                            if (animationWrapper == null)
                            {
                                return "Instantiate(_wrapper) failed";
                            }
                            arObjectState.OnFocusAnimations.Add(new ArAnimation(poiId, animationWrapper, objectToAdd, poiAnimation, false));
                            animationWrapper.transform.parent = parentTransform;
                            parentTransform = animationWrapper.transform;
                        }
                    }

                    if (poi.animations.inFocus != null)
                    {
                        foreach (var poiAnimation in poi.animations.inFocus)
                        {
                            SphereCollider sc = objectToAdd.AddComponent<SphereCollider>() as SphereCollider;
                            sc.radius = .71f; // 2 ** -2 / 2

                            // Put the animation into a wrapper
                            var animationWrapper = Instantiate(Wrapper);
                            if (animationWrapper == null)
                            {
                                return "Instantiate(_wrapper) failed";
                            }
                            arObjectState.InFocusAnimations.Add(new ArAnimation(poiId, animationWrapper, objectToAdd, poiAnimation, false));
                            animationWrapper.transform.parent = parentTransform;
                            parentTransform = animationWrapper.transform;
                        }
                    }

                    if (poi.animations.onClick != null)
                    {
                        foreach (var poiAnimation in poi.animations.onClick)
                        {
                            SphereCollider sc = objectToAdd.AddComponent<SphereCollider>() as SphereCollider;
                            sc.radius = .71f; // 2 ** -2 / 2

                            // Put the animation into a wrapper
                            var animationWrapper = Instantiate(Wrapper);
                            if (animationWrapper == null)
                            {
                                return "Instantiate(_wrapper) failed";
                            }
                            arObjectState.OnClickAnimations.Add(new ArAnimation(poiId, animationWrapper, objectToAdd, poiAnimation, false));
                            animationWrapper.transform.parent = parentTransform;
                            parentTransform = animationWrapper.transform;
                        }
                    }

                    if (poi.animations.onFollow != null)
                    {
                        foreach (var poiAnimation in poi.animations.onFollow)
                        {
                            // Put the animation into a wrapper
                            var animationWrapper = Instantiate(Wrapper);
                            if (animationWrapper == null)
                            {
                                return "Instantiate(_wrapper) failed";
                            }
                            arObjectState.OnFollowAnimations.Add(new ArAnimation(poiId, animationWrapper, objectToAdd, poiAnimation, false));
                            animationWrapper.transform.parent = parentTransform;
                            parentTransform = animationWrapper.transform;
                        }
                    }
                }

                // Put the game object into the scene or link it to the parent
                objectToAdd.transform.parent = parentTransform;

                // Set the name of the instantiated game object
                objectToAdd.name = poi.title;

                // Scale the scaleWrapper
                if (poi.transform != null && poi.transform.scale != 0)
                {
                    scaleWrapper.transform.localScale = new Vector3(poi.transform.scale, poi.transform.scale, poi.transform.scale);
                }
                else
                {
                    return "Could not set scale " + ((poi.transform == null) ? "null" : "" + poi.transform.scale);
                }

                // Rotate the rotationWrapper
                if (rotationWrapper != null)
                {
                    rotationWrapper.transform.eulerAngles = new Vector3(0, poi.transform.angle, 0);
                }

                // Relative to user, parent or with absolute coordinates
                var relativePosition = poi.poiObject.relativeLocation;

                if (parentObject != null || !IsEmpty(relativePosition))
                {
                    // Relative to user or parent
                    if (IsEmpty(relativePosition))
                    {
                        relativePosition = "0,0,0";
                    }
                    var parts = relativePosition.Split(',');

                    double value = 0;
                    var xOffset = (float)(parts.Length > 0 && double.TryParse(parts[0].Trim(), out value) ? value : 0);
                    var yOffset = (float)(parts.Length > 1 && double.TryParse(parts[1].Trim(), out value) ? value : 0);
                    var zOffset = (float)(parts.Length > 2 && double.TryParse(parts[2].Trim(), out value) ? value : 0);

                    var arObject = new ArObject(
                        poiId, poi.title, objectName, assetBundleUrl, wrapper, objectToAdd, poi.Latitude, poi.Longitude, poi.relativeAlt + yOffset, true);

                    var result = LinkArObject(arObjectState, parentObject, parentTransform, arObject, objectToAdd, poi);
                    if (result != null)
                    {
                        return result;
                    }

                    arObject.WrapperObject.transform.position = arObject.TargetPosition = new Vector3(xOffset, arObject.RelativeAltitude, zOffset);

                    if (_bleachingValue >= 0)
                    {
                        arObject.SetBleachingValue(_bleachingValue);
                    }
                }
                else
                {
                    // Absolute lat/lon coordinates
                    float filteredLatitude = UsedLatitude;
                    float filteredLongitude = UsedLongitude;

                    var distance = CalculateDistance(poi.Latitude, poi.Longitude, filteredLatitude, filteredLongitude);
                    if (distance <= ((poi.ArLayer != null) ? poi.ArLayer.visibilityRange : 1500))
                    {
                        var arObject = new ArObject(
                            poiId, poi.title, objectName, assetBundleUrl, wrapper, objectToAdd, poi.Latitude, poi.Longitude, poi.relativeAlt, false);

                        var result = LinkArObject(arObjectState, parentObject, parentTransform, arObject, objectToAdd, poi);
                        if (result != null)
                        {
                            return result;
                        }

                        if (_bleachingValue >= 0)
                        {
                            arObject.SetBleachingValue(_bleachingValue);
                        }
                    }
                }
            }
            return null;
        }

        // Create ar objects from layers
        private ArObjectState CreateArObjectState(List<ArObject> existingArObjects, List<ArLayer> layers)
        {
            var arObjectState = new ArObjectState();
            var pois = new List<Poi>();

            bool showInfo = false;
            string informationMessage = null;
            float refreshInterval = 0;
            int bleachingValue = -1;
            int areaSize = -1;
            int areaWidth = -1;
            bool applyKalmanFilter = true;

            foreach (var layer in layers)
            {
                if (applyKalmanFilter && !layer.applyKalmanFilter)
                {
                    applyKalmanFilter = layer.applyKalmanFilter;
                }

                if (bleachingValue < layer.bleachingValue)
                {
                    bleachingValue = layer.bleachingValue;
                }

                if (areaSize < layer.areaSize)
                {
                    areaSize = layer.areaSize;
                }
                if (areaWidth < layer.areaWidth)
                {
                    areaWidth = layer.areaWidth;
                }

                if (refreshInterval <= 0 && layer.refreshInterval >= 1)
                {
                    refreshInterval = layer.refreshInterval;
                }

                if (layer.actions != null)
                {
                    if (!showInfo)
                    {
                        showInfo = layer.actions.FirstOrDefault(x => x.showActivity) != null;
                    }
                    if (informationMessage == null)
                    {
                        informationMessage = layer.actions.Select(x => x.activityMessage).FirstOrDefault(x => !IsEmpty(x));
                    }
                }

                if (layer.hotspots == null)
                {
                    continue;
                }
                var layerPois = layer.hotspots.Where(x => x.isVisible && !IsEmpty(x.GameObjectName) && (x.ArLayer = layer) == layer);
                pois.AddRange(layerPois.Where(x => CalculateDistance(x.Latitude, x.Longitude, UsedLatitude, UsedLongitude) <= layer.visibilityRange));
            }

            _applyKalmanFilter = applyKalmanFilter;
            _informationMessage = informationMessage;
            _showInfo = showInfo;
            if (refreshInterval >= 1)
            {
                _refreshInterval = refreshInterval;
            }

            bool setBleachingValues = false;
            if (_bleachingValue != bleachingValue)
            {
                if (bleachingValue >= 0)
                {
                    setBleachingValues = true;
                    _bleachingValue = bleachingValue;
                    if (_bleachingValue > 100)
                    {
                        _bleachingValue = 100;
                    }
                }
                else
                {
                    _bleachingValue = -1;
                }
            }

            if (_areaSize != areaSize)
            {
                _areaSize = areaSize;
            }
            if (_areaWidth != areaWidth)
            {
                _areaWidth = areaWidth;
            }

            if (existingArObjects != null)
            {
                foreach (var arObject in existingArObjects)
                {
                    var poi = pois.FirstOrDefault(x => arObject.Id == x.id
                                               && arObject.GameObjectName.Equals(x.GameObjectName)
                                               && (IsEmpty(x.BaseUrl) || arObject.BaseUrl.Equals(x.BaseUrl))
                              );
                    if (poi == null)
                    {
                        arObjectState.ArObjectsToDelete.Add(arObject);
                    }
                    else
                    {
                        if (setBleachingValues && _bleachingValue >= 0)
                        {
                            arObject.SetBleachingValue(_bleachingValue);
                        }

                        if (poi.Latitude != arObject.Latitude)
                        {
                            arObject.Latitude = poi.Latitude;
                            arObject.IsDirty = true;
                        }
                        if (poi.Longitude != arObject.Longitude)
                        {
                            arObject.Longitude = poi.Longitude;
                            arObject.IsDirty = true;
                        }
                    }
                }
            }

            foreach (var poi in pois)
            {
                if (existingArObjects != null)
                {
                    string objectName = poi.GameObjectName;
                    if (IsEmpty(objectName))
                    {
                        continue;
                    }

                    string baseUrl = poi.BaseUrl;
                    if (!IsEmpty(baseUrl))
                    {
                        while (baseUrl.Contains('\\'))
                        {
                            baseUrl = baseUrl.Replace("\\", string.Empty);
                        }
                    }

                    if (existingArObjects.Any(
                        x => poi.id == x.Id
                        && objectName.Equals(x.GameObjectName)
                        && baseUrl.Equals(x.BaseUrl)))
                    {
                        continue;
                    }
                }
                arObjectState.ArPois.Add(poi);
            }
            return arObjectState;
        }

        // A coroutine retrieving the objects
        private IEnumerator GetData()
        {
            var build = "rel";
            var os = "Android";
            var bundle = "190224";
#if UNITY_IOS
            os = "iOS";
            bundle = "20190224";
#endif
#if DEVEL
            build = "dev"
#endif
            long count = 0;
            string layerName = _arpoiseDirectoryLayer;
            string uri = _arpoiseDirectoryUrl;

            bool setError = true;

            while (IsEmpty(_error))
            {
                count++;

                var assetBundleUrls = new HashSet<string>();

                float filteredLatitude = _filteredLatitude;
                float filteredLongitude = _filteredLongitude;
                float usedLatitude = UsedLatitude;
                float usedLongitude = UsedLongitude;
                int maxWait = 0;
                var layers = new List<ArLayer>();
                var nextPageKey = string.Empty;

                Debug.Log("uri " + uri + " layerName " + layerName);
                Debug.Log("filteredLatitude " + filteredLatitude + " filteredLongitude " + filteredLongitude);
                Debug.Log("usedLatitude " + usedLatitude + " usedLongitude " + usedLongitude);

                #region Download all pages of the layer
                for (; ; )
                {
                    var url = uri + "?lang=en"
                        + "&lat=" + usedLatitude.ToString("F6")
                        + "&lon=" + usedLongitude.ToString("F6")
                        + (filteredLatitude != usedLatitude ? "&latOfDevice=" + filteredLatitude.ToString("F6") : string.Empty)
                        + (filteredLongitude != usedLongitude ? "&lonOfDevice=" + filteredLongitude.ToString("F6") : string.Empty)
                        + "&layerName=" + layerName
                        + (!IsEmpty(nextPageKey) ? "&pageKey=" + nextPageKey : string.Empty)
                        + "&userId=" + SystemInfo.deviceUniqueIdentifier
                        + "&client=Arpoise&version=1&radius=1500&accuracy=100"
                        + "&bundle=" + bundle
                        + "&os=" + os
                        + "&count=" + count
                        + "&build=" + build
                    ;

                    Debug.Log("url " + url);

                    UnityWebRequest request = UnityWebRequest.Get(url);
                    request.timeout = 30;
                    yield return request.SendWebRequest();

                    maxWait = 3000;
                    while (!(request.isNetworkError || request.isHttpError) && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            _error = "Layer contents didn't download in 30 seconds.";
                        }
                        yield break;
                    }

                    if (request.isNetworkError || request.isHttpError)
                    {
                        if (setError)
                        {
                            _error = "Layer contents download error: " + request.error;
                        }
                        yield break;
                    }

                    var text = request.downloadHandler.text;
                    if (IsEmpty(text))
                    {
                        if (setError)
                        {
                            _error = "Layer contents download received empty text.";
                        }
                        yield break;
                    }

                    try
                    {
                        var layer = ArLayer.Create(text);
                        if (!IsEmpty(layer.redirectionUrl))
                        {
                            uri = layer.redirectionUrl.Trim();
                        }
                        if (!IsEmpty(layer.redirectionLayer))
                        {
                            layerName = layer.redirectionLayer.Trim();
                        }
                        if (!IsEmpty(layer.redirectionUrl) || !IsEmpty(layer.redirectionLayer))
                        {
                            layers.Clear();
                            nextPageKey = string.Empty;
                            continue;
                        }

                        layers.Add(layer);

                        if (layer.morePages == false || IsEmpty(layer.nextPageKey))
                        {
                            break;
                        }
                        nextPageKey = layer.nextPageKey;
                    }
                    catch (Exception e)
                    {
                        if (setError)
                        {
                            _error = "Layer parse exception: " + e.Message;
                        }
                        yield break;
                    }
                }
                #endregion

                if (!MenuEnabled.HasValue)
                {
                    var inputPanel = InputPanel.GetComponent<InputPanel>();
                    if (inputPanel.IsActivated())
                    {
                        MenuEnabled = true;
                    }
                    else
                    {
                        MenuEnabled = !layers.Any(x => !x.showMenuButton);
                    }
                }
                MenuButton.SetActive(MenuEnabled.HasValue && MenuEnabled.Value);

                #region Download the asset bundle for icons
                var iconAssetBundleUrl = "www.arpoise.com/AB/arpoiseicons.ace";
                assetBundleUrls.Add(iconAssetBundleUrl);
                foreach (var url in assetBundleUrls)
                {
                    if (_assetBundles.ContainsKey(url))
                    {
                        continue;
                    }
                    var assetBundleUrl = url;
#if UNITY_IOS
                    if (assetBundleUrl.EndsWith(".ace"))
                    {
                        assetBundleUrl = assetBundleUrl.Replace(".ace", "i.ace");
                    }
                    else
                    {
                        assetBundleUrl += "i";
                    }
#endif
                    while (assetBundleUrl.Contains('\\'))
                    {
                        assetBundleUrl = assetBundleUrl.Replace("\\", string.Empty);
                    }

                    UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(assetBundleUrl, 0);
                    request.timeout = 60;
                    yield return request.SendWebRequest();

                    maxWait = 6000;
                    while (!(request.isNetworkError || request.isHttpError) && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' download timeout.";
                        }
                        yield break;
                    }

                    if (request.isNetworkError || request.isHttpError)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' error: " + request.error;
                        }
                        yield break;
                    }

                    AssetBundle assetBundle = null;
                    try
                    {
                        assetBundle = DownloadHandlerAssetBundle.GetContent(request);
                    }
                    catch (Exception e)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' exception: " + e.Message;
                        }
                        yield break;
                    }

                    if (assetBundle == null)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' download is null.";
                        }
                        yield break;
                    }
                    _assetBundles[url] = assetBundle;
                }
                #endregion

                #region Handle lists of possible layers to show
                {
                    List<Item> itemList = new List<Item>();
                    foreach (var layer in layers.Where(x => x.hotspots != null))
                    {
                        if ("Arpoise-Directory".Equals(layer.layer))
                        {
                            foreach (var poi in layer.hotspots)
                            {
                                GameObject spriteObject = null;
                                var spriteName = poi.line4;
                                if (!IsEmpty(spriteName))
                                {
                                    AssetBundle iconAssetBundle = null;
                                    if (_assetBundles.TryGetValue(iconAssetBundleUrl, out iconAssetBundle))
                                    {
                                        spriteObject = iconAssetBundle.LoadAsset<GameObject>(spriteName);
                                    }
                                }

                                Sprite sprite = spriteObject != null ? spriteObject.GetComponent<SpriteRenderer>().sprite : null;

                                itemList.Add(new Item
                                {
                                    layerName = poi.title,
                                    itemName = poi.line1,
                                    line2 = poi.line2,
                                    line3 = poi.line3,
                                    url = poi.BaseUrl,
                                    distance = (int)poi.distance,
                                    icon = sprite
                                });
                            }
                        }
                    }

                    if (itemList.Any())
                    {
                        // There are different layers to show

                        _layerItemList = itemList;
                        HandleMenuButtonClick();

                        // Wait for the user to select a layer
                        try
                        {
                            _waitingForLayerSelection = true;
                            for (; ; )
                            {
                                var refreshRequest = _refreshRequest;
                                _refreshRequest = null;
                                if (refreshRequest != null)
                                {
                                    count = 0;
                                    layerName = refreshRequest.layerName;
                                    uri = refreshRequest.url;
                                    _fixedDeviceLatitude = refreshRequest.latitude;
                                    _fixedDeviceLongitude = refreshRequest.longitude;
                                    break;
                                }
                                yield return new WaitForSeconds(.1f);
                            }
                        }
                        finally
                        {
                            _waitingForLayerSelection = false;
                        }
                        continue;
                    }
                }
                #endregion

                var innerLayers = new HashSet<string>();

                foreach (var layer in layers.Where(x => x.hotspots != null))
                {
                    innerLayers.UnionWith(layer.hotspots.Where(x => !IsEmpty(x.InnerLayerName)).Select(x => x.InnerLayerName));
                }

                #region Download all inner layers
                foreach (var innerLayer in innerLayers)
                {
                    if (_innerLayers.ContainsKey(innerLayer))
                    {
                        continue;
                    }

                    if (layerName.Equals(innerLayer))
                    {
                        _innerLayers[layerName] = layers;
                        continue;
                    }

                    nextPageKey = string.Empty;
                    for (; ; )
                    {
                        var url = uri + "?lang=en"
                        + "&lat=" + usedLatitude.ToString("F6")
                        + "&lon=" + usedLongitude.ToString("F6")
                        + ((filteredLatitude != usedLatitude) ? "&latOfDevice=" + filteredLatitude.ToString("F6") : string.Empty)
                        + ((filteredLongitude != usedLongitude) ? "&lonOfDevice=" + filteredLongitude.ToString("F6") : string.Empty)
                        + "&layerName=" + innerLayer
                        + (!IsEmpty(nextPageKey) ? "&pageKey=" + nextPageKey : string.Empty)
                        + "&userId=" + SystemInfo.deviceUniqueIdentifier
                        + "&client=Arpoise&version=1&radius=1500&accuracy=100"
                        + "&bundle=" + bundle
                        + "&os=" + os
                        + "&innerLayer=true"
                        + "&build=" + build
                        ;

                        UnityWebRequest request = UnityWebRequest.Get(url);
                        request.timeout = 30;
                        yield return request.SendWebRequest();

                        maxWait = 3000;
                        while (!(request.isNetworkError || request.isHttpError) && !request.isDone && maxWait > 0)
                        {
                            yield return new WaitForSeconds(.01f);
                            maxWait--;
                        }

                        if (maxWait < 1)
                        {
                            if (setError)
                            {
                                _error = "Layer " + innerLayer + " contents didn't download in 30 seconds.";
                            }
                            yield break;
                        }

                        if (request.isNetworkError || request.isHttpError)
                        {
                            if (setError)
                            {
                                _error = "Layer " + innerLayer + " contents download error: " + request.error;
                            }
                            yield break;
                        }

                        var text = request.downloadHandler.text;
                        if (IsEmpty(text))
                        {
                            if (setError)
                            {
                                _error = "Layer " + innerLayer + " contents download received empty text.";
                            }
                            yield break;
                        }

                        try
                        {
                            var layer = ArLayer.Create(text);

                            List<ArLayer> layersList = null;
                            if (_innerLayers.TryGetValue(innerLayer, out layersList))
                            {
                                layersList.Add(layer);
                            }
                            else
                            {
                                _innerLayers[innerLayer] = new List<ArLayer> { layer };
                            }

                            if (layer.morePages == false || IsEmpty(layer.nextPageKey))
                            {
                                break;
                            }
                            nextPageKey = layer.nextPageKey;
                        }
                        catch (Exception e)
                        {
                            if (setError)
                            {
                                _error = "Layer " + innerLayer + " parse exception: " + e.Message;
                            }
                            yield break;
                        }
                    }
                }
                #endregion

                foreach (var layer in layers.Where(x => x.hotspots != null))
                {
                    assetBundleUrls.UnionWith(layer.hotspots.Where(x => !IsEmpty(x.BaseUrl)).Select(x => x.BaseUrl));
                }

                foreach (var layerList in _innerLayers.Values)
                {
                    foreach (var layer in layerList.Where(x => x.hotspots != null))
                    {
                        assetBundleUrls.UnionWith(layer.hotspots.Where(x => !IsEmpty(x.BaseUrl)).Select(x => x.BaseUrl));
                    }
                }

                #region Download the asset bundles
                foreach (var url in assetBundleUrls)
                {
                    if (_assetBundles.ContainsKey(url))
                    {
                        continue;
                    }
                    var assetBundleUrl = url;
#if UNITY_IOS
                    if (assetBundleUrl.EndsWith(".ace"))
                    {
                        assetBundleUrl = assetBundleUrl.Replace(".ace", "i.ace");
                    }
                    else
                    {
                        assetBundleUrl += "i";
                    }
#endif
                    while (assetBundleUrl.Contains('\\'))
                    {
                        assetBundleUrl = assetBundleUrl.Replace("\\", string.Empty);
                    }

                    UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(assetBundleUrl, 0);
                    request.timeout = 60;
                    yield return request.SendWebRequest();

                    maxWait = 6000;
                    while (!(request.isNetworkError || request.isHttpError) && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' download timeout.";
                        }
                        yield break;
                    }

                    if (request.isNetworkError || request.isHttpError)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' error: " + request.error;
                        }
                        yield break;
                    }

                    AssetBundle assetBundle = null;
                    try
                    {
                        assetBundle = DownloadHandlerAssetBundle.GetContent(request);
                    }
                    catch (Exception e)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' exception: " + e.Message;
                        }
                        yield break;
                    }

                    if (assetBundle == null)
                    {
                        if (setError)
                        {
                            _error = "Bundle '" + assetBundleUrl + "' download is null.";
                        }
                        yield break;
                    }
                    _assetBundles[url] = assetBundle;
                }
                #endregion

                var layerTitle = layers.Select(x => x.layerTitle).FirstOrDefault(x => !IsEmpty(x));
                if (!IsEmpty(layerTitle))
                {
                    HeaderText.GetComponent<Text>().text = layerTitle;
                    _headerButtonActivated = true;
                    HeaderButton.SetActive(_headerButtonActivated);
                }
                else
                {
                    HeaderText.GetComponent<Text>().text = string.Empty;
                    _headerButtonActivated = false;
                    HeaderButton.SetActive(_headerButtonActivated);
                }

                List<ArObject> existingArObjects = null;
                var arObjectState = _arObjectState;
                if (arObjectState != null)
                {
                    lock (arObjectState)
                    {
                        existingArObjects = arObjectState.ArObjects.ToList();
                    }
                }
                arObjectState = CreateArObjectState(existingArObjects, layers);
                setError = false;

                if (_arObjectState == null)
                {
                    _error = CreateArObjects(arObjectState, null, SceneAnchor.transform, arObjectState.ArPois);
                    arObjectState.ArPois.Clear();

                    if (!IsEmpty(_error))
                    {
                        yield break;
                    }
                    if (arObjectState.ArObjects.Count == 0)
                    {
                        var message = layers.Select(x => x.noPoisMessage).FirstOrDefault(x => !IsEmpty(x));
                        if (IsEmpty(message))
                        {
                            message = "Sorry, there are no augments at your location!";
                        }
                        _error = message;
                        yield break;
                    }
                    arObjectState.ArObjectsToPlace = arObjectState.ArObjects.Where(x => !x.IsRelative).ToList();

                    InitialHeading = Input.compass.trueHeading;
                    _headingShown = Input.compass.trueHeading;
                    _startTicks = DateTime.Now.Ticks;
                    _arObjectState = arObjectState;
                }
                else
                {
                    lock (_arObjectState)
                    {
                        if (arObjectState.ArPois.Any())
                        {
                            _arObjectState.ArPois.AddRange(arObjectState.ArPois);
                        }
                        if (arObjectState.ArObjectsToDelete.Any())
                        {
                            _arObjectState.ArObjectsToDelete.AddRange(arObjectState.ArObjectsToDelete);
                        }
                        _arObjectState.IsDirty = true;
                    }
                }

                var refreshInterval = _refreshInterval;
                var doNotRefresh = refreshInterval < 1;

                long now = DateTime.Now.Ticks;
                long waitUntil = now + (long)refreshInterval * 10000000L;

                while (doNotRefresh || now < waitUntil)
                {
                    now = DateTime.Now.Ticks;

                    var refreshRequest = _refreshRequest;
                    _refreshRequest = null;
                    if (refreshRequest != null)
                    {
                        count = 0;
                        layerName = refreshRequest.layerName;
                        uri = refreshRequest.url;
                        _fixedDeviceLatitude = refreshRequest.latitude;
                        _fixedDeviceLongitude = refreshRequest.longitude;
                        break;
                    }
                    yield return new WaitForSeconds(.1f);
                }
            }
            yield break;
        }

        #endregion

        #region GetPosition

        private float _latitudeHandled = 0;
        private float _longitudeHandled = 0;

        // Calculate positions for all ar objects
        private void PlaceArObjects(ArObjectState arObjectState)
        {
            var arObjectsToPlace = arObjectState.ArObjectsToPlace;
            if (arObjectsToPlace != null)
            {
                var filteredLatitude = UsedLatitude;
                var filteredLongitude = UsedLongitude;

                if (!arObjectsToPlace.Any(x => x.IsDirty) && _latitudeHandled == filteredLatitude && _longitudeHandled == filteredLongitude)
                {
                    return;
                }
                _latitudeHandled = filteredLatitude;
                _longitudeHandled = filteredLongitude;

                foreach (var arObject in arObjectsToPlace)
                {
                    arObject.IsDirty = false;
                    var latDistance = CalculateDistance(arObject.Latitude, arObject.Longitude, filteredLatitude, arObject.Longitude);
                    var lonDistance = CalculateDistance(arObject.Latitude, arObject.Longitude, arObject.Latitude, filteredLongitude);

                    if (arObject.Latitude < UsedLatitude)
                    {
                        if (latDistance > 0)
                        {
                            latDistance *= -1;
                        }
                    }
                    else
                    {
                        if (latDistance < 0)
                        {
                            latDistance *= -1;
                        }
                    }
                    if (arObject.Longitude < UsedLongitude)
                    {
                        if (lonDistance > 0)
                        {
                            lonDistance *= -1;
                        }
                    }
                    else
                    {
                        if (lonDistance < 0)
                        {
                            lonDistance *= -1;
                        }
                    }

                    if (_areaSize <= 0 && _areaWidth > 0)
                    {
                        _areaSize = _areaWidth;
                    }
                    if (_areaSize > 0)
                    {
                        if (_areaWidth <= 0)
                        {
                            _areaWidth = _areaSize;
                        }

                        var halfWidth = _areaWidth / 2f;
                        while (lonDistance > 0 && lonDistance > halfWidth)
                        {
                            lonDistance -= _areaWidth;
                        }
                        while (lonDistance < 0 && lonDistance < -halfWidth)
                        {
                            lonDistance += _areaWidth;
                        }

                        var halfSize = _areaSize / 2f;
                        while (latDistance > 0 && latDistance > halfSize)
                        {
                            latDistance -= _areaSize;
                        }
                        while (latDistance < 0 && latDistance < -halfSize)
                        {
                            latDistance += _areaSize;
                        }
                        var distanceToAreaBorder = Mathf.Min(Mathf.Abs(Mathf.Abs(latDistance) - halfSize), Mathf.Abs(Mathf.Abs(lonDistance) - halfWidth));
                        if (distanceToAreaBorder < 1)
                        {
                            // The object is less than 1 meter from the border, scale it down with the distance it has
                            arObject.Scale = distanceToAreaBorder;
                        }
                        else
                        {
                            arObject.Scale = 1;
                        }
                    }
                    else
                    {
                        arObject.Scale = 1;
                    }
                    arObject.TargetPosition = new Vector3(lonDistance, arObject.RelativeAltitude, latDistance);
                }
            }
        }

        // Calculates the distance between two sets of coordinates, taking into account the curvature of the earth
        private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
        {
            var R = 6371.0; // Mean radius of earth in KM
            var dLat = lat2 * Mathf.PI / 180 - lat1 * Mathf.PI / 180;
            var dLon = lon2 * Mathf.PI / 180 - lon1 * Mathf.PI / 180;
            float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
              Mathf.Cos(lat1 * Mathf.PI / 180) * Mathf.Cos(lat2 * Mathf.PI / 180) *
              Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
            var c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
            var distance = R * c;
            return (float)(distance * 1000f); // meters
        }

        private long _timeStampInMilliseconds;
        private readonly double _qMetersPerSecond = 3;
        private double _variance = -1;

        private float? _fixedDeviceLatitude = null;
        private float? _fixedDeviceLongitude = null;

        protected float UsedLatitude
        {
            get
            {
                var latitude = _fixedDeviceLatitude;
                if (latitude.HasValue)
                {
                    return latitude.Value;
                }
                return _filteredLatitude;
            }
        }

        protected float UsedLongitude
        {
            get
            {
                var longitude = _fixedDeviceLongitude;
                if (longitude.HasValue)
                {
                    return longitude.Value;
                }
                return _filteredLongitude;
            }
        }

        // Kalman filter processing for latitude and longitude
        private void KalmanFilter(double currentLatitude, double currentLongitude, double accuracy, long timeStampInMilliseconds)
        {
            if (!_applyKalmanFilter)
            {
                _filteredLatitude = (float)currentLatitude;
                _filteredLongitude = (float)currentLongitude;
                return;
            }

            if (accuracy < 1)
            {
                accuracy = 1;
            }
            if (_variance < 0)
            {
                // if variance < 0, the object is unitialised, so initialise it with current values
                _timeStampInMilliseconds = timeStampInMilliseconds;
                _filteredLatitude = (float)currentLatitude;
                _filteredLongitude = (float)currentLongitude;
                _variance = accuracy * accuracy;
            }
            else
            {
                // apply Kalman filter
                long timeIncreaseInMilliseconds = timeStampInMilliseconds - _timeStampInMilliseconds;
                if (timeIncreaseInMilliseconds > 0)
                {
                    // time has moved on, so the uncertainty in the current position increases
                    _variance += timeIncreaseInMilliseconds * _qMetersPerSecond * _qMetersPerSecond / 1000;
                    _timeStampInMilliseconds = timeStampInMilliseconds;
                    // TO DO: USE VELOCITY INFORMATION HERE TO GET A BETTER ESTIMATE OF CURRENT POSITION
                }

                // Kalman gain matrix K = Covariance * Inverse(Covariance + MeasurementVariance)
                // NB: because K is dimensionless, it doesn't matter that variance has different units to lat and lon
                double k = _variance / (_variance + accuracy * accuracy);
                // apply K
                _filteredLatitude += (float)(k * (currentLatitude - _filteredLatitude));
                _filteredLongitude += (float)(k * (currentLongitude - _filteredLongitude));
                // new Covarariance  matrix is (IdentityMatrix - K) * Covariance 
                _variance = (1 - k) * _variance;
            }
        }

        private LocationService _locationService = null;

        // A Coroutine for retrieving the current location
        //
        private IEnumerator GetPosition()
        {
#if UNITY_EDITOR
            // If in editor mode, set a fixed initial location and forget about the location service
            //
            _filteredLatitude = OriginalLatitude = 48.158464f;
            _filteredLongitude = OriginalLongitude = 11.578708f;

            Debug.Log("UNITY_EDITOR fixed location, lat " + _filteredLatitude + ", lon " + _filteredLongitude);

            // Start GetData() function 
            StartCoroutine("GetData");

            while (_filteredLatitude <= 90)
            {
                yield return new WaitForSeconds(3600f);
            }
            // End of editor mode
#endif

            bool doInitialize = true;
            while (IsEmpty(_error))
            {
                if (doInitialize)
                {
                    doInitialize = false;
                    Input.compass.enabled = true;

                    if (_locationService == null)
                    {
                        _locationService = new LocationService();
                        if (!_locationService.isEnabledByUser)
                        {
                            _error = "Please enable the location service for the ARpoise application.";
                            yield break;
                        }
                    }

                    Input.location.Start(.1f, .1f);

                    int maxWait = 3000;
                    while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        _error = "Location service didn't initialize in 30 seconds.";
                        yield break;
                    }

                    if (Input.location.status == LocationServiceStatus.Failed)
                    {
                        _error = "Unable to determine device location.";
                        yield break;
                    }

                    _filteredLatitude = OriginalLatitude = Input.location.lastData.latitude;
                    _filteredLongitude = OriginalLongitude = Input.location.lastData.longitude;

                    InitialDeviceOrientation = Input.deviceOrientation;
                    _cameraTransform.gameObject.GetComponent<VuforiaBehaviour>().enabled = true;
                    VuforiaRuntime.Instance.InitVuforia();

                    // Start GetData() function 
                    StartCoroutine("GetData");
                }

                // For the first N seconds we remember the initial camera heading
                if (_cameraIsInitializing && _startTicks > 0 && DateTime.Now.Ticks > _startTicks + 20000000)
                {
                    _cameraIsInitializing = false;
                }
                _currentHeading = Input.compass.trueHeading;

                if (_locationLatitude != Input.location.lastData.latitude
                    || _locationLongitude != Input.location.lastData.longitude
                    || _locationTimestamp != Input.location.lastData.timestamp
                    || _locationHorizontalAccuracy != Input.location.lastData.horizontalAccuracy
                )
                {
                    _locationLatitude = Input.location.lastData.latitude;
                    _locationLongitude = Input.location.lastData.longitude;
                    _locationTimestamp = Input.location.lastData.timestamp;
                    _locationHorizontalAccuracy = Input.location.lastData.horizontalAccuracy;

                    KalmanFilter(_locationLatitude, _locationLongitude, _locationHorizontalAccuracy, (long)(1000L * _locationTimestamp));
                }

                var arObjectState = _arObjectState;
                if (arObjectState != null)
                {
                    PlaceArObjects(arObjectState);
                }
                yield return new WaitForSeconds(.01f);
            }
            yield break;
        }

        #endregion

        #region Start

        private void Start()
        {
#if UNITY_EDITOR
            Debug.Log("UNITY_EDITOR Start");
#endif
            var inputPanel = InputPanel.GetComponent<InputPanel>();
            inputPanel.Activate(null);
            _fixedDeviceLatitude = inputPanel.GetLatitude();
            _fixedDeviceLongitude = inputPanel.GetLongitude();

            // Placing the wrapper in the hierarchy under the ARCamera,
            // is a work-around because FindGameObjectWithTag
            // does not find the ARCamera game object
            _cameraTransform = Wrapper.transform.parent;

            // Start GetPosition() coroutine 
            StartCoroutine("GetPosition");
        }

        private GameObject FindGameObjectWithTag(string gameObjectTag)
        {
            try
            {
                return GameObject.FindGameObjectWithTag(gameObjectTag);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Update

#if UNITY_IOS
        public readonly int DeviceAngle = 360;
#endif
#if UNITY_ANDROID
        public int DeviceAngle
        {
            get
            {
                switch (InitialDeviceOrientation)
                {
                    case DeviceOrientation.LandscapeRight:
                        return 180;

                    case DeviceOrientation.PortraitUpsideDown:
                        return 270;

                    case DeviceOrientation.Portrait:
                        return 90;

                    default:
                        return 360;
                }
            }
        }
#endif
        private void Update()
        {
            // Set any error text onto the canvas
            if (!IsEmpty(_error) && InfoText != null)
            {
                InfoText.GetComponent<Text>().text = _error;
                return;
            }

            long now = DateTime.Now.Ticks;
            var second = now / 10000000L;

            if (_waitingForLayerSelection)
            {
                InfoText.GetComponent<Text>().text = _selectingText;
                return;
            }

            if (_startTicks == 0 || _arObjectState == null)
            {
                string progress = string.Empty;
                for (long s = _initialSecond; s < second; s++)
                {
                    progress += ".";
                }
                InfoText.GetComponent<Text>().text = _loadingText + progress;
                return;
            }

            var arObjectState = _arObjectState;
            if (arObjectState.IsDirty)
            {
                try
                {
                    lock (arObjectState)
                    {
                        SceneAnchor.transform.eulerAngles = Vector3.zero;

                        if (arObjectState.ArObjectsToDelete.Any())
                        {
                            arObjectState.DestroyArObjects();
                        }
                        if (arObjectState.ArPois.Any())
                        {
                            CreateArObjects(arObjectState, null, SceneAnchor.transform, arObjectState.ArPois);
                            arObjectState.ArPois.Clear();
                        }
                        arObjectState.ArObjectsToPlace = arObjectState.ArObjects.Where(x => !x.IsRelative).ToList();
                        arObjectState.IsDirty = false;
                    }
                }
                finally
                {
                    SceneAnchor.transform.eulerAngles = new Vector3(0, DeviceAngle - InitialHeading, 0);
                }
            }

            foreach (var arAnimation in arObjectState.BillboardAnimations)
            {
                arAnimation.Wrapper.transform.LookAt(Camera.main.transform);
            }

            bool hit = false;
            if (arObjectState.OnFocusAnimations.Count > 0 || arObjectState.InFocusAnimations.Count > 0)
            {
                var ray = Camera.main.ScreenPointToRay(new Vector3(Camera.main.pixelWidth / 2, Camera.main.pixelHeight / 2, 0f));

                var inFocusAnimationsToStop = arObjectState.InFocusAnimations.Where(x => x.IsActive).ToList();

                RaycastHit hitInfo;
                if (Physics.Raycast(ray, out hitInfo, 1500f))
                {
                    var objectHit = hitInfo.transform.gameObject;
                    if (objectHit != null)
                    {
                        foreach (var arAnimation in arObjectState.OnFocusAnimations.Where(x => objectHit.Equals(x.GameObject)))
                        {
                            hit = true;
                            if (!arAnimation.IsActive)
                            {
                                arAnimation.Activate(_startTicks, now);
                            }
                        }

                        foreach (var arAnimation in arObjectState.InFocusAnimations.Where(x => objectHit.Equals(x.GameObject)))
                        {
                            hit = true;
                            if (!arAnimation.IsActive)
                            {
                                arAnimation.Activate(_startTicks, now);
                            }
                            inFocusAnimationsToStop.Remove(arAnimation);
                        }
                    }
                }

                foreach (var arAnimation in inFocusAnimationsToStop)
                {
                    if (arAnimation.IsActive)
                    {
                        arAnimation.Stop(_startTicks, now);
                    }
                }
            }

            bool click = false;
            if (arObjectState.OnClickAnimations.Count > 0 && Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

                RaycastHit hitInfo;
                if (Physics.Raycast(ray, out hitInfo, 1500f))
                {
                    GameObject objectHit = hitInfo.transform.gameObject;
                    if (objectHit != null)
                    {
                        foreach (var arAnimation in arObjectState.OnClickAnimations.Where( x => objectHit.Equals(x.GameObject)))
                        {
                            click = true;
                            if (!arAnimation.IsActive)
                            {
                                arAnimation.Activate(_startTicks, now);
                            }
                        }
                    }
                }
            }

            var animations = arObjectState.OnCreateAnimations
                .Concat(arObjectState.OnFollowAnimations)
                .Concat(arObjectState.OnFocusAnimations)
                .Concat(arObjectState.InFocusAnimations)
                .Concat(arObjectState.OnClickAnimations);

            foreach (var arAnimation in animations)
            {
                if (arAnimation.JustActivated && arAnimation.GameObject != null)
                {
                    var audioSource = arAnimation.GameObject.GetComponent<AudioSource>();
                    if (audioSource != null)
                    {
                        audioSource.Play();
                    }
                }
                arAnimation.Animate(_startTicks, now);

                if (arAnimation.JustStopped && !IsEmpty(arAnimation.FollowedBy))
                {
                    var annimationsToFollow = arAnimation.FollowedBy.Split(',');
                    if (annimationsToFollow != null && annimationsToFollow.Length > 0)
                    {
                        foreach (var animationToFollow in annimationsToFollow)
                        {
                            if (!IsEmpty(animationToFollow))
                            {
                                var animationName = animationToFollow.Trim();
                                foreach (var arAnimationToFollow in animations.Where(x => animationName.Equals(x.Name)))
                                {
                                    if (!arAnimationToFollow.IsActive)
                                    {
                                        arAnimationToFollow.Activate(_startTicks, now);
                                        if (arAnimationToFollow.JustActivated && arAnimationToFollow.GameObject != null)
                                        {
                                            var audioSource = arAnimationToFollow.GameObject.GetComponent<AudioSource>();
                                            if (audioSource != null)
                                            {
                                                audioSource.Play();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (_currentSecond == second)
            {
                _framesPerCurrentSecond++;
            }
            else
            {
                if (_currentSecond == second - 1)
                {
                    _framesPerSecond = _framesPerCurrentSecond;
                }
                else
                {
                    _framesPerSecond = 1;
                }
                _framesPerCurrentSecond = 1;
                _currentSecond = second;
            }

            // Set any error text onto the canvas
            if (!IsEmpty(_error) && InfoText != null)
            {
                InfoText.GetComponent<Text>().text = _error;
                return;
            }

            // Calculate heading
            var currentHeading = _currentHeading;
            if (Math.Abs(currentHeading - _headingShown) > 180)
            {
                if (currentHeading < _headingShown)
                {
                    currentHeading += 360;
                }
                else
                {
                    _headingShown += 360;
                }
            }
            _headingShown += (currentHeading - _headingShown) / 10;
            while (_headingShown > 360)
            {
                _headingShown -= 360;
            }

            // Place the ar objects
            try
            {
                SceneAnchor.transform.eulerAngles = Vector3.zero;

                var arObjectsToPlace = arObjectState.ArObjectsToPlace;
                if (arObjectsToPlace != null)
                {
                    foreach (var arObject in arObjectsToPlace)
                    {
                        var jump = false;
                        LastObject = arObject;

                        // Linearly interpolate from current position to target position
                        Vector3 position;
                        if (_areaSize > 0 && _areaWidth > 0
                            && (Math.Abs(arObject.WrapperObject.transform.position.x - arObject.TargetPosition.x) > _areaWidth * .75
                            || Math.Abs(arObject.WrapperObject.transform.position.z - arObject.TargetPosition.z) > _areaSize * .75))
                        {
                            // Jump if area handling is active and distance is too big
                            position = new Vector3(arObject.TargetPosition.x, arObject.TargetPosition.y, arObject.TargetPosition.z);
                            jump = true;
                        }
                        else
                        {
                            position = Vector3.Lerp(arObject.WrapperObject.transform.position, arObject.TargetPosition, .5f / _framesPerSecond);
                        }
                        arObject.WrapperObject.transform.position = position;

                        if (_areaSize > 0)
                        {
                            // Scale the objects at the edge of the area
                            var scale = arObject.Scale;
                            if (scale < 0)
                            {
                                scale = 1;
                            }
                            Vector3 localScale;
                            if (jump)
                            {
                                if (scale < 1)
                                {
                                    scale = 0.01f;
                                }
                                localScale = new Vector3(scale, scale, scale);
                            }
                            else
                            {
                                localScale = new Vector3(scale, scale, scale);
                                localScale = Vector3.Lerp(arObject.WrapperObject.transform.localScale, localScale, 1f / _framesPerSecond);
                            }
                            arObject.WrapperObject.transform.localScale = localScale;
                        }
                    }
                }
            }
            finally
            {
                SceneAnchor.transform.eulerAngles = new Vector3(0, DeviceAngle - InitialHeading, 0);
            }

            // Turn the ar objects
            if (_cameraIsInitializing)
            {
                InitialHeading = _headingShown;
                InitialCameraAngle = _cameraTransform.eulerAngles.y;
                foreach (var arObject in arObjectState.ArObjects)
                {
                    arObject.WrapperObject.transform.eulerAngles = new Vector3(0, DeviceAngle - InitialHeading, 0);
                }
            }

            if (InfoText != null)
            {
                // Set info text
                if (!_showInfo)
                {
                    InfoText.GetComponent<Text>().text = string.Empty;
                    return;
                }
                if (!IsEmpty(_informationMessage))
                {
                    InfoText.GetComponent<Text>().text = _informationMessage;
                    return;
                }

                InfoText.GetComponent<Text>().text =
                    ""
                    //+ "B " + _bleachingValue
                    //+ "CLT " + (_locationTimestamp).ToString("F3")
                    //+ " CA " + (_locationLatitude).ToString("F6")
                    //+ " A " + (_locationHorizontalAccuracy).ToString("F6")
                    + " LA " + (UsedLatitude).ToString("F6")
                    //+ " CO " + (_locationLongitude).ToString("F6")
                    + " LO " + (UsedLongitude).ToString("F6")
                    //+ " AS " + _areaSize
                    //+ " AV " + AnimationValue.ToString("F3")
                    //+ " Z " + (LastObject != null ? LastObject.TargetPosition : Vector3.zero).z.ToString("F1")
                    //+ " X " + (LastObject != null ? LastObject.TargetPosition : Vector3.zero).x.ToString("F1")
                    //+ " Y " + (LastObject != null ? LastObject.TargetPosition : Vector3.zero).y.ToString("F1")
                    //+ " LA " + (LastObject != null ? LastObject.Latitude : 0).ToString("F6")
                    //+ " LO " + (LastObject != null ? LastObject.Longitude : 0).ToString("F6")
                    + " F " + _framesPerSecond
                    //+ " C " + _cameraTransform.eulerAngles.y.ToString("F")
                    //+ " IC " + _initialCameraAngle.ToString("F")
                    //+ " SA " + _sceneAnchor.transform.eulerAngles.y.ToString("F")
                    + " H " + (int)_headingShown
                    //+ " IH " + _initialHeading.ToString("F")
                    + " N " + arObjectState.ArObjects.Sum(x => x.GameObjects.Count)
                    //+ " O " + _onFocusAnimations.Count
                    //+ " R " + ray.ToString()
                    //+ " R " + ray.origin.x.ToString("F1") + " " + ray.origin.y.ToString("F1") + " " + ray.origin.z.ToString("F1")
                    //+ " " + ray.direction.x.ToString("F1") + " " + ray.direction.y.ToString("F1") + " " + ray.direction.z.ToString("F1")
                    + (hit ? " h " : string.Empty)
                    + (click ? " c " : string.Empty)
                    ;
            }
        }

        #endregion
    }
}
