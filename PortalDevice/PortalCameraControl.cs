using System;
using System.Collections.Generic;
using UnityEngine;
using Modding;
using UnityEngine.SceneManagement;

namespace Dingodile {
    public class PortalCameraControl : SimBehaviour {

        public enum Environment { Ipsilon = 0, Tolbrynd = 1, Valfross = 2, Desert = 3, Sandbox, Other }

        public Environment environment = Environment.Sandbox;
        protected Camera source;
        public new Camera camera;
        public RenderTexture texture;
        public new MeshRenderer renderer;
        public Portal portal, otherPortal;
        public Transform portalDummy, otherDummy;

        public BloomAndLensFlares bloom;
        public SSAOPro ssao;
        public AntialiasingAsPostEffect aa;
        public ColorfulFog fog;
        public FogVolume fog2;
        public BloomAndLensFlares sourceBloom;
        public SSAOPro sourceSsao;
        public AntialiasingAsPostEffect sourceAa;
        public ColorfulFog sourceFog;
        public FogVolume sourceFog2;

        public bool display = true;

        public bool hasParent = false;
        public PortalCameraControl parentCamera;

        private bool setUp = false;
        private float fovCos = 0.7f;
        private float halfFovCos = 0.7f;
        public float nearClipOffset = 0f;

        protected int layer = 0;
        protected bool ignored = false;
        protected Transform ipsilonFog, otherFog;

        public virtual bool CanBeSeenBySource() {
            bool seenBySub = hasParent ? parentCamera.CanBeSeenBySource() : true;
            float posAngle = Vector3.Dot((portal.transform.position - source.transform.position).normalized, source.transform.forward);
            float dirAngle = Vector3.Dot(source.transform.forward, portal.transform.forward);
            return dirAngle < halfFovCos && posAngle > fovCos && seenBySub;
        }

        public void Setup(Camera source, Portal portal, Portal otherPortal, int[] remove, int[] add, PortalCameraControl par = null, float nearclipoffset = 0f, bool ignoreFog = false) {
            if (par) {
                hasParent = true;
                parentCamera = par;
            }
            setUp = true;
            this.source = source;
            this.portal = portal;
            this.otherPortal = otherPortal;
            this.nearClipOffset = nearclipoffset;

            if (portalDummy == null) {
                portalDummy = new GameObject(portal.name + " Dummy").transform;
                portalDummy.parent = portal.transform;
            }
            if (otherDummy == null) {
                otherDummy = new GameObject(otherPortal.name + " Dummy").transform;
                otherDummy.parent = otherPortal.transform;
            }

            camera = gameObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            int i;
            for (i = 0; i < remove.Length; i++) {
                camera.cullingMask = VidyaMod.RemoveFromLayerMask(camera.cullingMask, remove[i]);
                //camera.cullingMask = camera.cullingMask & ~(1 << remove[i]);//remove layer
            }
            for (i = 0; i < add.Length; i++) {
                camera.cullingMask = VidyaMod.AddToLayerMask(camera.cullingMask, add[i]);
                //camera.cullingMask = camera.cullingMask | (1 << add[i]);//add
            }
            camera.depth = -2;

            sourceBloom = Camera.main.GetComponent<BloomAndLensFlares>();
            sourceSsao = Camera.main.GetComponent<SSAOPro>();
            sourceAa = Camera.main.GetComponent<AntialiasingAsPostEffect>();
            sourceFog = Camera.main.GetComponent<ColorfulFog>();

            bloom = ModUtility.CopyComponent<BloomAndLensFlares>(sourceBloom, gameObject);
            ssao = ModUtility.CopyComponent<SSAOPro>(sourceSsao, gameObject);
            aa = ModUtility.CopyComponent<AntialiasingAsPostEffect>(sourceAa, gameObject);
            fog = ModUtility.CopyComponent<ColorfulFog>(sourceFog, gameObject);

            bloom.enabled = false;
            ssao.enabled = sourceSsao.enabled;
            aa.enabled = sourceAa.enabled;
            fog.enabled = sourceFog.enabled;

            layer = add[0];
            ignored = ignoreFog;
            ProcessFog();

            UpdateFOV();
            ReferenceMaster.onFOVChanged += UpdateFOV;
        }

        public void ProcessFog() {
            bool isCampaign = false;
            int n;
            if (int.TryParse(SceneManager.GetActiveScene().name, out n)) {
                isCampaign = true;
            }

            if (isCampaign) {
                environment = (Environment)StatMaster.currentIslandID;
            } else if (StatMaster.isMP) {
                if (LevelEditor.Instance != null) {
                    if (LevelEditor.Instance.Settings != null) {
                        environment = MPEnvironmentToSimple((int)LevelEditor.Instance.Settings.Environment);
                    } else {
                        environment = Environment.Other;
                    }
                    LevelEditor.Instance.LevelSettingsChanged += SettingsChanged;
                }
            }

            ipsilonFog = Camera.main.transform.FindChild("Fog Volume");
            if (!ipsilonFog) {
                ipsilonFog = GameObject.Find("MULTIPLAYER LEVEL")?.transform.FindChild("Environments/Ipsilon/Fog Volume");
            }

            Transform darkFog = Camera.main.transform.FindChild("Fog Volume Dark");
            otherFog = StatMaster.isMP ? GameObject.Find("MULTIPLAYER LEVEL")?.transform.FindChild("Environments/Tolbrynd/Fog Volume") : darkFog;
            if (otherFog) {
                otherFog.gameObject.layer = 0;
            }
            if (darkFog) {
                darkFog.gameObject.layer = 0;
            }

            sourceFog2 = (environment == Environment.Valfross || environment == Environment.Tolbrynd ? otherFog : ipsilonFog)?.GetComponent<FogVolume>();
            CreateFog();
        }

        public void CreateFog() {
            if (ignored) {
                if (sourceFog2) {
                    sourceFog2.gameObject.layer = layer;
                }
            } else if (sourceFog2) {
                GameObject child = GameObject.Instantiate(sourceFog2.gameObject);
                DestroyImmediate(child.GetComponent<FollowCam>());
                    
                child.transform.parent = transform;
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = sourceFog2.transform.localScale;

                fog2 = child.GetComponent<FogVolume>();
                fog2.enabled = sourceFog2.enabled && sourceFog2.gameObject.activeSelf;
                fog2.gameObject.SetActive(Portal.accurateFog);

                fog2.gameObject.layer = layer;
            }
        }

        public void SettingsChanged(LevelSettings settings) {
            environment = MPEnvironmentToSimple((int)settings.Environment);
            UpdateFog();
        }

        public Environment MPEnvironmentToSimple(int mp) {
            //LevelEnvironment { None = 0, Barren = 1, Tolbrynd = 3, MountainTop = 4, LoadingMultiverse = 5, Desert = 6, }
            Environment s = Environment.Sandbox;
            switch (mp) {
                case 0:
                    s = Environment.Ipsilon;
                    break;
                case 1:
                    s = Environment.Sandbox;
                    break;
                case 3:
                    s = Environment.Tolbrynd;
                    break;
                case 4:
                case 5:
                    s = Environment.Other;
                    break;
                case 6:
                    s = Environment.Desert;
                    break;
            }
            return s;
        }

        public void UpdateFog() {
            sourceFog2 = (environment == Environment.Valfross || environment == Environment.Tolbrynd ? otherFog : ipsilonFog)?.GetComponent<FogVolume>();
            if (fog2 == null) {
                CreateFog();
            } else {
                if (sourceFog2) {
                    if (ignored) {
                        sourceFog2.gameObject.layer = layer;
                    }
                    fog2.enabled = sourceFog2.enabled && sourceFog2.gameObject.activeSelf;
                    ModUtility.CopyComponentValues<FogVolume>(sourceFog2, fog2);
                }
                fog2.gameObject.SetActive(false);
                if (Portal.accurateFog) {
                    fog2.gameObject.SetActive(true);
                }
                
            }
        }

        public void UpdateFOV() {
            SetIgnoreAngle(OptionsMaster.BesiegeConfig.FieldOfView);
        }

        public void SetIgnoreAngle(float val) {
            float inv = Mathf.Abs(90f - val * 0.5f);
            fovCos = Mathf.Cos(Mathf.Deg2Rad * inv * 2f);
            halfFovCos = Mathf.Cos(Mathf.Deg2Rad * inv);
        }

        public void LateUpdate() {
            if (!setUp) {
                return;
            }
            if (!isSimulating || !portal.placed || !display || !CanBeSeenBySource()) {
                if (camera.enabled) {
                    camera.enabled = false;
                    renderer.enabled = false;
                }
                return;
            }
            if (!camera.enabled) {
                camera.enabled = true;
                renderer.enabled = true;
            }

            portalDummy.position = portal.transform.position;
            portalDummy.rotation = portal.transform.rotation;
            otherDummy.position = otherPortal.transform.position;
            otherDummy.rotation = otherPortal.transform.rotation;

            //Debug.Log("rendering " + name);

            Vector3 up = Vector3.up; // AddPiece.GetLocalDirClosestTo(portal, Vector3.up);

            Vector3 CameraUp = portalDummy.InverseTransformDirection(source.transform.up);
            CameraUp = Quaternion.AngleAxis(180f, up) * CameraUp;
            CameraUp = otherDummy.TransformDirection(CameraUp);

            transform.localScale = source.transform.localScale;

            Vector3 CameraOffsetFromPortal = portalDummy.InverseTransformPoint(source.transform.position);
            CameraOffsetFromPortal = Quaternion.AngleAxis(180f, up) * CameraOffsetFromPortal;
            CameraOffsetFromPortal = otherDummy.TransformPoint(CameraOffsetFromPortal);
            transform.position = CameraOffsetFromPortal;

            Vector3 CameraDirectionToPortal = portalDummy.InverseTransformDirection(source.transform.forward);
            CameraDirectionToPortal = Quaternion.AngleAxis(180f, up) * CameraDirectionToPortal;
            CameraDirectionToPortal = otherDummy.TransformDirection(CameraDirectionToPortal);
            transform.rotation = Quaternion.LookRotation(CameraDirectionToPortal, CameraUp);

            Ray ray = new Ray(transform.position, transform.forward);
            Plane p = new Plane(transform.forward, otherPortal.transform.position);
            float a = Vector3.Dot(transform.forward, otherPortal.transform.forward) - 1f;
            float angleCompensation = Mathf.Abs(a) * 10f;
            float distance;
            if (p.Raycast(ray, out distance)) {
                float near = distance - 1f - angleCompensation + nearClipOffset;
                if (near < 0.01f) near = 0.01f;
                camera.nearClipPlane = near;
            }

            //bloom.enabled = sourceBloom.enabled;
            ssao.enabled = sourceSsao.enabled;
            aa.enabled = sourceAa.enabled;
            fog.enabled = sourceFog.enabled;
            if (Portal.accurateFog && fog2 && sourceFog2) {
                fog2.enabled = sourceFog2.enabled && sourceFog2.gameObject.activeSelf;

                if (fog2.enabled) {
                    fog2.transform.localPosition = sourceFog2.transform.localPosition;
                    fog2.transform.localRotation = sourceFog2.transform.localRotation;
                    fog2.transform.localScale = sourceFog2.transform.localScale;
                }
            }
        }
        
    }
}
