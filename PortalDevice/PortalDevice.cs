using System.Collections;
using System.Collections.Generic;
using Modding;
using Modding.Blocks;
using Modding.Levels;
using UnityEngine;

namespace Dingodile {
    public class PortalDevice : BlockScript {

        public static ModAssetBundle bundle;
        public static AssetBundle AssetBundle;
        public static Portal PortalA;
        public static Portal PortalB;
        public static PortalDevice prefab, stripped;
        public static GameObject impact;

        private static bool setupPortals = false;
        private static bool setupPrefab = false;
        private static bool setupStripped = false;

        public static ModMesh normal;
        public static ModMesh shooting;

        public static HashSet<PortalDevice> PortalDevices = new HashSet<PortalDevice>();
        public static bool updatingDevices = false;
        private static Color def = Color.gray;

        private static float Correction = 0f;
        private static float Size = 1f;

        public override void OnPrefabCreation() {
            if (IsStripped) {
                stripped = this;
                SetupStripped();
            } else {
                prefab = this;
                SetupPrefab();
            }
            SetupNetworking();
            AssignAudio();
            AssignEmission();
        }

        public static void OnModLoad() {
            def = new Color(0.15f, 0.2f, 0.25f);
            FixVacuum();
            bundle = ModResource.GetAssetBundle("portal");
            normal = ModResource.GetMesh("portalDeviceMesh");
            shooting = ModResource.GetMesh("portalDeviceShotMesh");
            bundle.OnLoad += Load;
            Events.OnSimulationToggle += ResetPortal;
        }

        public static void FixVacuum() {
            BlockBehaviour vacuum = PrefabMaster.BlockPrefabs[(int)BlockType.Vacuum].blockBehaviour;
            Collider frontCollider = vacuum.myBounds.childColliders[0];
            Collider[] all = frontCollider.GetComponents<Collider>();
            for (int i = 0; i < all.Length; i++) {
                if (vacuum.myBounds.childColliders.Contains(all[i])) {
                    continue;
                }
                vacuum.myBounds.childColliders.Add(all[i]);
            }
        }

        public static void Load() {
            AssetBundle = bundle.AssetBundle;
            var allAssets = AssetBundle.LoadAllAssets();

            if (allAssets.Length <= 0) {
                Debug.LogWarning("asset bundle is empty attempting manual reloading");
                byte[] bytes = ModIO.ReadAllBytes("Resources/portal.unity3d");
                AssetBundle.Unload(false);
                AssetBundle = new AssetBundle();
                StatMaster.Instance.StartCoroutine(IELoad(AssetBundle.LoadFromMemoryAsync(bytes)));
                return;
            }
            RebuildForNewLevel();
        }
        public static IEnumerator IELoad(AssetBundleCreateRequest request) {
            while (!request.isDone) {
                yield return null;
            }
            AssetBundle = request.assetBundle;
            SetupPortals();
        }

        public static void RebuildForNewLevel() {
            setupPortals = false;
            SetupPrefab();
            SetupStripped();
            SetupParticle();
            SetupPortals();
        }

        public static void SetupPrefab() {
            if (setupPrefab) {
                return;
            }
            if (!prefab || !bundle.Available) {
                return;
            }
            setupPrefab = true;
            AddMuzzleTo(prefab);
        }

        public static void SetupStripped() {
            if (setupStripped) {
                return;
            }
            if (!prefab || !bundle.Available) {
                return;
            }
            setupStripped = true;
            AddMuzzleTo(stripped);
        }

        public static void AddMuzzleTo(PortalDevice device) {
            GameObject muzzle = AssetBundle.LoadAsset<GameObject>("_PD_Muzzle");
            device.muzzle = new ParticleSystemRenderer[2];
            GameObject go = Instantiate(muzzle);
            device.muzzle[0] = go.GetComponent<ParticleSystemRenderer>();
            device.muzzle[0].transform.SetParent(device.transform);
            device.muzzle[0].transform.localPosition = new Vector3(0f, -1.477f, 0.533f);
            device.muzzle[0].transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            device.muzzle[1] = device.muzzle[0].transform.GetChild(0).GetComponent<ParticleSystemRenderer>();
        }

        public static void SetupParticle() {
            if (impact != null) {
                return;
            }
            GameObject asset = AssetBundle.LoadAsset<GameObject>("_PD_Impact");
            impact = Instantiate(asset);
            impact.transform.parent = null;
            //TimedObjectDestructor destructor = impact.AddComponent<TimedObjectDestructor>();
            //destructor.time = 2f;
        }

        public static void SetupPortals() {
            if (setupPortals) {
                return;
            }
            setupPortals = true;

            Transform fogSphere = Camera.main.transform.FindChild("FOG SPHERE");
            if (fogSphere) {
                fogSphere.name = "FOG SPHERE 1";
                fogSphere.localScale = Vector3.one * 1090f;
            }
            fogSphere = GameObject.Find("FOG SPHERE")?.transform;
            if (fogSphere) {
                fogSphere.name = "FOG SPHERE 2";
                fogSphere.localScale = Vector3.one * 1090f;
            }

            Camera.main.cullingMask = VidyaMod.RemoveFromLayerMask(Camera.main.cullingMask, 1);
            Camera.main.cullingMask = VidyaMod.RemoveFromLayerMask(Camera.main.cullingMask, 3);
            Camera.main.cullingMask = VidyaMod.RemoveFromLayerMask(Camera.main.cullingMask, 30);
            Camera.main.cullingMask = VidyaMod.RemoveFromLayerMask(Camera.main.cullingMask, 31);

            Camera.main.gameObject.AddComponent<MainCamTracker>();

            CreatePortal("Portal A", ref PortalA);
            CreatePortal("Portal B", ref PortalB);

            int portalAlayer = 6;
            int portalBlayer = 7;
            int portalAinsidePortalLayer = 1;
            int portalBinsidePortalLayer = 3;
            
            PortalA.Setup(0, portalAlayer, portalBlayer, portalAinsidePortalLayer, portalBinsidePortalLayer, 30, PortalB, 0f, 1f, 1f);
            PortalB.Setup(1, portalBlayer, portalAlayer, portalBinsidePortalLayer, portalAinsidePortalLayer, 31, PortalA, 0.5f, 1f, 0.7f);
            ResetPortal();
        }

        public static void CreatePortal(string name, ref Portal portal) {
            GameObject prefab = AssetBundle.LoadAsset("Portal Prefab") as GameObject;
            GameObject portalGo = Instantiate(prefab);
            portal = portalGo.AddComponent<Portal>();
            portal.name = name + " (Portal)";
            GameObject camera = new GameObject(name + " (Camera 1)");
            portal.camControl = camera.gameObject.AddComponent<PortalCameraControl>();
            camera = new GameObject(name + " (Camera 2)");
            portal.secCamControl = camera.gameObject.AddComponent<PortalCameraControl>();
            camera = new GameObject(name + " (Camera 3)");
            portal.terCamControl = camera.gameObject.AddComponent<PortalCameraControl>();

        }
        
        public static void ResetPortal(bool simulating = false) {

            if (!setupPortals) {
                return;
            }

            if (simulating == false) {
                PortalA.InvokeReset();
                PortalB.InvokeReset();
            }
        }

        public BlockBehaviour Behaviour { get { return VisualController.Block; } }

        public ParticleSystemRenderer[] muzzle;
        public AudioSource audioSource;
        public static ModAudioClip[] clips;

        private MKey shootA;
        private MKey shootB;
        private MSlider correction;
        private MSlider size;
        private MSlider clipPct;

        private float updateInterval = 0.1f;
        private float colorTimerTarget = 0f;
        private float colorTimer = 0f;

        public override void SafeAwake() {
            
            size = AddSlider("Portal Size", "size", Size, 0.5f, 3f);
            clipPct = AddSlider("Portal Overlap", "clip", 0.12f, 0f, 0.2f);
            correction = AddSlider("Center Correction", "correction", Correction, 0f, 1f);
            shootA = AddKey("Orange Portal", "shoot-a", KeyCode.Y);
            shootB = AddKey("Blue Portal", "shoot-b", KeyCode.U);

            size.ValueChanged += UpdateAllPortalDevicesSize;
            correction.ValueChanged += UpdateAllPortalDevicesCentering;
        }

        public virtual void AssignAudio() {
            clips = new ModAudioClip[3];
            clips[0] = ModResource.GetAudioClip("fireOrange");
            clips[1] = ModResource.GetAudioClip("fireBlue");
            clips[2] = ModResource.GetAudioClip("fireMiss");

            if (clips[0].Available && clips[1].Available) {
                audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                audioSource.volume = 0.9f;
                audioSource.pitch = 1f;
                audioSource.spatialBlend = 0.99f;
                audioSource.reverbZoneMix = 1;
                audioSource.clip = clips[0];
            }
        }
        
        public virtual void AssignEmission() {
            ModTexture t = ModResource.GetTexture("portalEmissionTex");
            
            if (t.Available) {
                MeshRenderer rend = VisualController.renderers[0];
                rend.sharedMaterial.SetTexture("_EmissMap", t);
                rend.sharedMaterial.SetColor("_EmissCol", def);
                /*
                VisualController.Prefab.DefaultSkin.material.SetTexture("_EmissMap", t);
                VisualController.Prefab.DefaultSkin.material.SetColor("_EmissCol", Color.gray);*/

                MaterialPropertyBlock props = new MaterialPropertyBlock();
                rend.GetPropertyBlock(props);
                props.SetColor("_EmissCol", def);
                rend.SetPropertyBlock(props);

                VisualController.canBeHeated = true;
                VisualController.heating.lerpSpeed = 4f;
                VisualController.heating.glowCol = def;
                VisualController.heating.colToSet = "_EmissCol";
            }
        }

        public virtual void SetEmission(Color color) {
            VisualController.heating.glowCol = color;
            colorTimerTarget = 0f;
            colorTimer = 0f;
        }

        public virtual void UpdateEmission() {
            if (colorTimer >= colorTimerTarget) {
                colorTimerTarget = updateInterval;
                colorTimer = 0f;
                UpdateEmissionCycle();
            } else {
                colorTimer += Time.deltaTime;
            }
            return;
        }

        public virtual void UpdateEmissionCycle() {
            VisualController.OnIgnite(null, null, false);
        }

        public virtual void UpdateAllPortalDevicesSize(float value) {
            if (updatingDevices) {
                return;
            }
            updatingDevices = true;
            Size = value;
            foreach (var device in PortalDevices) {
                if (device == this) {
                    continue;
                }
                device.SetSize(value);
            }
            if (fakeOpen) {
                BlockMapper.Open(Behaviour);
                fakeOpen = false;
            }
            updatingDevices = false;
        }
        public virtual void UpdateAllPortalDevicesCentering(float value) {
            if (updatingDevices) {
                return;
            }
            updatingDevices = true;
            Correction = value;
            foreach (var device in PortalDevices) {
                if(device == this) {
                    continue;
                }
                device.SetCentering(value);
            }
            if (fakeOpen) {
                BlockMapper.Open(Behaviour);
                fakeOpen = false;
            }
            updatingDevices = false;
        }

        protected static bool fakeOpen = false;
        protected bool missingSizeSave = false;
        protected bool missingCorrSave = false;
        public virtual void SetSize(float value) {
            size.SetValue(value);

            if(Input.GetMouseButton(0) && !IsSimulating && !InputManager.ToggleSimulationKey()) {
                missingSizeSave = true;
                return;
            }
            missingSizeSave = false;
            BlockMapper.OnEditField(Behaviour, size);
            fakeOpen = true;
        }
        public virtual void SetCentering(float value) {
            correction.SetValue(value);

            if (Input.GetMouseButton(0) && !IsSimulating && !InputManager.ToggleSimulationKey()) {
                missingCorrSave = true;
                return;
            }
            missingCorrSave = false;
            BlockMapper.OnEditField(Behaviour, correction);
            fakeOpen = true;
        }
        public virtual float GetCentering() {
            return correction.Value;
        }

        public override void BuildingUpdate() {
            if (Input.GetMouseButton(0) && !InputManager.ToggleSimulationKey()) {
                return;
            }
            if (BlockMapper.CurrentInstance) {
                return;
            }
            bool close = false;
            if (missingSizeSave) {
                missingSizeSave = false;
                BlockMapper.OnEditField(Behaviour, size);
                close = true;
            }
            if (missingCorrSave) {
                missingCorrSave = false;
                BlockMapper.OnEditField(Behaviour, correction);
                close = true;
            }
            if (close) {
                BlockMapper.Close();
            }
        }

        public virtual void OnEnable() {
            if (IsSimulating 
             || PortalDevices.Contains(this)) {
                return;
            }
            PortalDevices.Add(this);
        }

        public virtual void OnDisable() {
            if (IsSimulating
             || !PortalDevices.Contains(this)) {
                return;
            }
            PortalDevices.Remove(this);
        }

        public static MessageType portalShotMessageType;
        public static void SetupNetworking() {
            portalShotMessageType = ModNetworking.CreateMessageType(DataType.Block, DataType.Integer, DataType.Integer, DataType.Vector3, DataType.Vector3, DataType.Vector3);
            ModNetworking.Callbacks[portalShotMessageType] += ProcessRemotePortalPlacement;
        }
        protected void SendPortalPlacement(Portal portal, ShootState state, Vector3 impact, Vector3 pos, Vector3 normal) {
            MuzzlePlarticle(portal);
            VidyaMod.PlayAudio(audioSource, state == ShootState.Place ? clips[portal.index] : clips[2], 0.9f);
            if (!StatMaster.isLocalSim) {
                Message targetMessage = portalShotMessageType.CreateMessage(Block.From(this), portal.index, (int)state, impact, pos, normal);
                ModNetworking.SendToAll(targetMessage);
            }
        }

        public static void ProcessRemotePortalPlacement(Message m) {
            Block b = (Block)m.GetData(0);
            int p = (int)m.GetData(1);
            int s = (int)m.GetData(2);
            Vector3 impact = (Vector3)m.GetData(3);
            Vector3 pos = (Vector3)m.GetData(3);
            Vector3 normal = (Vector3)m.GetData(4);

            PortalDevice script = b.GameObject.GetComponent<PortalDevice>();
            Portal portal = PortalA, other = PortalB;

            switch (p) {
                case 0:
                    portal = PortalA;
                    other = PortalB;
                    break;
                case 1:
                    portal = PortalB;
                    other = PortalA;
                    break;
            }
            script.ReceivePortal(portal, other, p, (ShootState)s, impact, pos, normal);
        }

        public override void OnSimulateStart() {
            MeshRenderer rend = VisualController.renderers[0];
            /*rend.sharedMaterial.SetColor("_EmissCol", Color.gray);*/
            VisualController.Prefab.DefaultSkin.material.SetColor("_EmissCol", def);
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            rend.GetPropertyBlock(props);
            props.SetColor("_EmissCol", def);
            rend.SetPropertyBlock(props);

        }

        public override void SimulateLateUpdateAlways() {
            UpdateEmission();
        }

        public override void SimulateUpdateHost() {
            if (PortalA.teleportedObjects.Count > 0
             || PortalB.teleportedObjects.Count > 0) {
                if (shootA.IsPressed) {
                    SendPortalPlacement(PortalA, ShootState.Unable, Vector3.zero, Vector3.zero, Vector3.zero);
                } else if (shootB.IsPressed) {
                    SendPortalPlacement(PortalB, ShootState.Unable, Vector3.zero, Vector3.zero, Vector3.zero);
                }
                return;
            }

            if (shootA.IsPressed) {
                ShootPortal(PortalA, PortalB, clips[0]);
            } else if (shootB.IsPressed) {
                ShootPortal(PortalB, PortalA, clips[1]);
            }
        }

        public void ShootPortal(Portal portal, Portal other, AudioClip clip) {
            
            RaycastHit target = RaycastPortal(portal.transform, other.transform);
            Vector3 org = target.point;
            target = CheckPortal(target, portal, other);

            if (target.distance >= 0f) {
                if (target.distance > 0f) {
                    ImpactParticle(org, target.normal, portal, portal.layer);
                    portal.Place(this, target.point, Quaternion.LookRotation(target.normal, GetPseudoPortalUp(portal.transform, target.normal)), Vector3.one * size.Value);

                    SetEmission(portal.color);
                    SendPortalPlacement(portal, ShootState.Place, org, target.point, target.normal);
                    return;
                } else {
                    ImpactParticle(org, target.normal, portal, other.layer);
                    other.StartAnimation(PortalAnim.Disturb);
                    SendPortalPlacement(portal, ShootState.Impact, org, target.point, target.normal);
                    return;
                }
            }
            SendPortalPlacement(portal, ShootState.Miss, org, target.point, target.normal);
            VidyaMod.PlayAudio(audioSource, clips[2], 0.9f);
        }

        public enum ShootState { Place, Impact, Miss, Unable }
        public void ReceivePortal(Portal portal, Portal other, int clipIndex, ShootState state, Vector3 impact, Vector3 pos, Vector3 normal) {
            MuzzlePlarticle(portal);
            switch (state) {
                case ShootState.Place:
                    ImpactParticle(impact, normal, portal, portal.layer);
                    portal.Place(this, pos, Quaternion.LookRotation(normal, GetPseudoPortalUp(portal.transform, normal)), Vector3.one * size.Value);

                    SetEmission(portal.color);
                    VidyaMod.PlayAudio(audioSource, clips[clipIndex], 1f, 0.9f, 1.05f);
                    break;
                case ShootState.Impact:
                    ImpactParticle(impact, normal, portal, other.layer);
                    other.StartAnimation(PortalAnim.Disturb);
                    VidyaMod.PlayAudio(audioSource, clips[2], 0.9f);
                    break;
                case ShootState.Miss:
                    VidyaMod.PlayAudio(audioSource, clips[2], 0.9f);
                    break;
                case ShootState.Unable:
                    VidyaMod.PlayAudio(audioSource, clips[2], 0.3f);
                    portal.StartAnimation(PortalAnim.Disturb);
                    other.StartAnimation(PortalAnim.Disturb);
                    break;
            }
        }

        public void MuzzlePlarticle(Portal p) {
            muzzle[0].material.SetColor("_TintColor", p.particleColor);
            muzzle[1].material.SetColor("_TintColor", p.particleColor);
            muzzle[0].GetComponent<ParticleSystem>().Play();
            if (VisualController.selectedSkin == VisualController.Prefab.DefaultSkin) {
                StartCoroutine(AnimateModel());
            }
        }

        public IEnumerator AnimateModel() {
            VisualController.meshFiltery.mesh = shooting;
            yield return new WaitForSeconds(0.1f);
            VisualController.meshFiltery.mesh = normal;
            yield break;
        }

        public void ImpactParticle(Vector3 point, Vector3 normal, Portal p, int layer) {
            GameObject hit = Instantiate(impact);
            hit.transform.position = point;
            hit.transform.rotation = Quaternion.LookRotation(normal);
            hit.SetActive(true);
            hit.layer = p.otherLayer;
            ParticleSystemRenderer[] rens = hit.GetComponentsInChildren<ParticleSystemRenderer>();
            foreach (var ren in rens) {
                ren.gameObject.layer = layer;
                ren.material.SetColor("_TintColor", p.particleColor * 0.6f);
            }
        }

        protected virtual RaycastHit RaycastPortal(Transform portal, Transform otherPortal) {
            Vector3 fwd = -transform.up;
            Vector3 up = transform.forward;
            Vector3 pos = transform.position + up * 0.25f + fwd * 0.5f;
            LayerMask mask = AddPiece.CreateLayerMask(new int[] {0, 2, 6, 7, 11, 24, 28, 29});
            RaycastHit[] hits = Physics.SphereCastAll(pos, 0.2f, fwd, 300f, mask);
            RaycastHit hit = new RaycastHit();
            hit.distance = -1f;
            hit.point = pos;

            float min = float.MaxValue;
            for (int i = 0; i < hits.Length; i++) {
                if (hits[i].distance < min) {
                    RaycastHit h = hits[i];

                    if (h.collider.transform.root == portal.transform
                     || h.collider.isTrigger
                     || VidyaMod.IsVirtualTrigger(h.collider.transform)) {
                        continue;
                    }
                    if (!h.collider.transform.root.GetComponent<WinCondition>()) {
                        if (h.collider.gameObject.layer == 2) {
                            if (h.collider.transform.root == otherPortal.transform) {
                                min = h.distance;
                                h.distance = 0f;
                                hit = h;
                            }
                            continue;
                        }
                        if (h.collider.attachedRigidbody) {
                            if(h.collider.attachedRigidbody == Rigidbody) {
                                continue;
                            }
                        }
                        min = h.distance;
                        h.distance = -1f;
                        hit = h;
                        continue;
                    }
                    min = h.distance;
                    h.point = h.point + h.normal * (h.collider.name == "FloorBig" ? 0.02f : 0.2f);
                    hit = h;
                }
            }
            return hit;
        }
        
        protected virtual RaycastHit CheckPortal(RaycastHit hit, Portal portal, Portal other) {
            if(hit.distance <= 0f) {
                return hit;
            }
            Vector3 startPoint = hit.point;
            //get a perpendicular vector to the normal

            float angle = 45f;
            Vector3 perp = (Quaternion.LookRotation(hit.normal, Vector3.up) * Vector3.up).normalized;

            for (int n = 0; n * angle < 90f - float.Epsilon; n++) {
                hit = CheckPortal(hit, perp, portal, other);
                if (hit.distance <= 0f) {
                    //Debug.Break();
                    return hit;
                }
                perp = Quaternion.AngleAxis(angle, hit.normal) * perp;
            }
            return hit;
        }

        public virtual RaycastHit CheckPortal(RaycastHit hit, Vector3 perp, Portal portal, Portal other) {
            Vector3 startPoint = hit.point;
            float angle = 90f;
            for (int n = 0; n * angle < 180f; n++) {
                //raycast bidirectional based on the perpendicular vector
                float portalRadius = 5f * size.Value;

                Vector3 possitive = RaycastProximity(hit, perp, portalRadius, portal, other);
                Vector3 negative = RaycastProximity(hit, -perp, portalRadius + possitive.magnitude, portal, other);
                float sqrPos = possitive.sqrMagnitude;
                float sqrNeg = negative.sqrMagnitude;
                
                if (sqrNeg > float.Epsilon) {
                    //if both vectors have any sort of magnitude it means the portal can't fit and we finish
                    if (sqrPos > float.Epsilon) {
                        hit.distance = 0f;
                        return hit;
                    } else {
                        //if the negative did find anything we need to check further for possitive to see if it would be moved into something
                        possitive = RaycastProximity(hit, perp, portalRadius + negative.magnitude, portal, other);
                        sqrPos = possitive.sqrMagnitude;
                        if (sqrPos > float.Epsilon) {
                            hit.distance = 0f;
                            return hit;
                        }
                    }
                } else {
                    //if neither vector has any magnitude we don't need to worry about more and continue to the next
                    if (sqrPos < float.Epsilon) {
                        perp = Quaternion.AngleAxis(angle, hit.normal) * perp;
                        continue;
                    }
                }
                //we calculate a composite vector because we know the result will work for our purposes with having done the two check before, and we simply get the one direction we need (with magnitude)
                Vector3 composite = possitive + negative;
                //let's see how far the hit point has moved, if it's even necessary for any movement to be done by this, if the offset doesn't have any magnitude then we don't need to bother with altering the composite.
                Vector3 offset = (hit.point - startPoint);
                if (offset.sqrMagnitude > float.Epsilon) {
                    //we project the offset onto the composite, so we can see how much the offset already has done for us towards the movement the composite wants, and we subtract that, so we only move as much as necessary.
                    composite -= Vector3.Project(offset, composite);
                }
                //we offset the hit point by the composite, now out point has moved away from anything too close to it.
                hit.point += composite;
                //we rotate the perpendicular vector by 45 degrees around the normal, so next time we loop we check 45 degrees differently to cover more area.
                perp = Quaternion.AngleAxis(angle, hit.normal) * perp;
            }
            return hit;
        }
        
        protected virtual Vector3 RaycastProximity(RaycastHit center, Vector3 dir, float dist, Portal portal, Portal other) {
            RaycastHit hit = Raycast(center.point, dir, dist, portal, other, dist * clipPct.Value);
            float distance = hit.distance;
            //chceck for ground
            Vector3 point = hit.point - dir * 0.02f;
            float floorDepth = 0.35f;
            bool groundFound = Raycast(point + center.normal * 0.02f, -center.normal, floorDepth, portal, other).distance != floorDepth;
            if (!groundFound) {
                //find closest ground
                hit = Raycast(point - center.normal * (floorDepth - 0.02f), -dir, (hit.point - center.point).magnitude, portal, other);
                if(hit.distance > 0.02f) {
                    distance -= hit.distance;
                }
            }
            return -dir * (dist - distance);
        }
        
        protected virtual RaycastHit Raycast(Vector3 pos, Vector3 dir, float dist, Portal portal, Portal other, float overlap = 0f) {
            LayerMask mask = AddPiece.CreateLayerMask(new int[] { 0, 2, 6, 7, 11, 24, 28, 29 });
            RaycastHit[] hits = Physics.RaycastAll(pos, dir, dist, mask);
            RaycastHit hit = new RaycastHit();
            hit.distance = dist;
            hit.point = pos + dir * dist;
            float min = float.MaxValue;

            for (int i = 0; i < hits.Length; i++) {
                if (hits[i].distance < min) {
                    RaycastHit h = hits[i];

                    if (h.collider.transform.root == portal.transform) {
                        continue;
                    }
                    if (h.collider.transform.root == BlockBehaviour.ParentMachine.transform.root) {
                        continue;
                    }
                    float d = h.distance;
                    if (h.collider.gameObject.layer == 2) {
                        if (h.collider.transform.root != other.transform) {
                            continue;
                        }
                    } else if (overlap > 0f) {
                        if (!VidyaMod.IsVirtualTrigger(h.collider.transform)) {
                            float diff = dist - h.distance;
                            float o = diff > overlap ? overlap : diff;

                            h.distance = h.distance + o;
                        }
                    }
                    min = d;
                    hit = h;
                }
            }
            /*
            float r = dir.x * 0.5f + 0.75f;
            float g = dir.y * 0.5f + 0.75f;
            float b = dir.z * 0.5f + 0.75f;
            Debug.DrawRay(pos, dir * dist, new Color(r, g, b, 0.2f));
            Debug.DrawRay(pos, dir * hit.distance, new Color(r, g, b, 1f));*/
            return hit;
        }

        public static Vector3 CheckAngle(ref float min, Vector3 perp, Vector3 up) {
            float cur = Vector3.Dot(perp, Vector3.up);
            //if closer to up
            if (cur > min) {
                min = cur;
                return perp;
            }
            return up;
        }

        public virtual Vector3 GetPseudoPortalUp(Transform t, Vector3 normal) {
            Vector3 up = Vector3.up;

            ///FIX ROTATION FOR MATCHING UP AND NORMAL
            float dot = Mathf.Abs(Vector3.Dot(normal, up));
            if (dot > 0.92f) {
                up = -transform.up;

                dot = Mathf.Abs(Vector3.Dot(normal, up));
                if (dot > 0.92f) {
                    up = transform.forward;
                    /*
                    dot = Mathf.Abs(Vector3.Dot(normal, up));
                    if (dot > 0.707f) {
                        up = transform.right;
                    }*/
                }
            }
            /*Vector3[] snaps = new Vector3[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            for(int i = 0; i < snaps.Length; i++) {

            }*/
            
            return up;
        }
    }
}
