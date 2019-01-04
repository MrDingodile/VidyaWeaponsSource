using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Modding;
using Modding.Blocks;
using System.Linq;
using Dingodile.EzySlice;

namespace Dingodile {

    public enum PortalAnim { Place, Unlock, Lock, Change, Disturb, Destroy, None }

    public class TriggerInfo {
        public BlockBehaviour block;
        public HashSet<MeshRenderer> rens;
        public HashSet<MeshRenderer> shadows;
        public HashSet<ParticleSystem> particles;
        public HashSet<Collider> cols;
        public Rigidbody body;
        public int layer;
        public enum Type { Block, LevelObject, Projectile }
        public Type type;
        public bool addedDuringAnimation = false;

        public HashSet<TriggerInfo> childBlocks = new HashSet<TriggerInfo>();

        public TriggerInfo(Rigidbody body, HashSet<Collider> cols, HashSet<MeshRenderer> rens, HashSet<MeshRenderer> shadows = null, bool animating = false) {
            this.body = body;
            this.rens = rens;
            this.shadows = shadows;
            this.cols = cols;
            this.type = Type.LevelObject;
            this.addedDuringAnimation = animating;
        }

        public TriggerInfo(Rigidbody body, BlockBehaviour block, HashSet<Collider> cols, HashSet<TriggerInfo> childBlocks, HashSet<ParticleSystem> particles = null) {
            this.type = Type.Block;
            this.body = body;
            this.block = block;
            this.cols = cols;
            this.particles = particles;
            this.childBlocks = childBlocks;
        }
    }

    public class Portal : SimBehaviour {
        public const int INVISIBLE_COLLIDER_LAYER = 5;
        public const int GLOBAL_PORTAL_IGNORE_LAYER = 11;
        public const float TELEPORT_THRESHOLD = 0.9f;
        public const float MIN_EXIT_VERTICAL_VELOCITY = 15f;
        public const string SHADOW_CASTER = "__VW_SHADOW_CASTER";
        private const float wait = 0f;
        private const float duration = 0.5f;
        public static int resolutionDiv = 1;
        public static int maxNesting = 3;
        public static bool allowCamTp = false;
        public static bool accurateFog = true;
        public static bool displayMirrors = true;
        public static bool mirroredBodies = false;
        public static float portalTriggerSize = 5f;
        private static bool first = true;

        public static GameObject cubeMesh, sphereMesh;

        public int index = 0;
        public bool placed = false;
        public bool open = false;
        public float centerForce = 10f;
        public Vector3 scale = Vector3.one;
        public PortalDevice device;
        public Portal otherPortal;
        public Transform visParent;
        private MeshRenderer lockedVisual, nestEnd;
        public Collider[] edgeColliders;
        public RemoteRigidbodyTrigger hideTrigger, portalTrigger;
        public RemoteRigidbodyTrigger coneTrigger;
        public Transform groundPlane;
        public AnimationCurve curve;
        public PortalCameraControl camControl, secCamControl, terCamControl;
        public List<MeshRenderer> renderers = new List<MeshRenderer>();
        public int layer, otherLayer, insideLayer, insideOtherLayer;

        public MeshRenderer portalRenderer { get {return camControl.renderer; } }

        public Dictionary<Rigidbody, TriggerInfo> hiddenObjects = new Dictionary<Rigidbody, TriggerInfo>();
        public Dictionary<Rigidbody, TriggerInfo> teleportingObjects = new Dictionary<Rigidbody, TriggerInfo>();
        public Dictionary<Rigidbody, TriggerInfo> teleportedObjects = new Dictionary<Rigidbody, TriggerInfo>();
        public HashSet<Collider> environment = new HashSet<Collider>();

        public static bool IsTeleporting(BlockBehaviour block) {
            return block.isSimulating && block.wasNotAllowed;
        }
        
        public static bool IsDraggedBlock(BlockBehaviour block) {
            return block is GenericDraggedBlock;
        }

        public static void MarkAsTeleporting(BlockBehaviour block, bool value) {
            if (block.isSimulating) {
                block.wasNotAllowed = value;
            }
        }

        public void AddTeleportingEntry(TriggerInfo entry) {
            Rigidbody r = entry.body;
            if (teleportedObjects.ContainsKey(r)) {
                teleportedObjects.Remove(r);
            }
            teleportingObjects.Add(r, entry);
        }

        public void AddTeleportedEntry(TriggerInfo entry) {
            Rigidbody r = entry.body;
            teleportingObjects.Remove(r);
            if (!teleportedObjects.ContainsKey(r)) {
                teleportedObjects.Add(r, entry);
            }
        }

        public TriggerInfo RemoveTeleportEntry(Rigidbody r) {
            TriggerInfo entry;
            if (teleportingObjects.ContainsKey(r)) {
                entry = teleportingObjects[r];
                teleportingObjects.Remove(r);
            } else if (teleportedObjects.ContainsKey(r)) {
                entry = teleportedObjects[r];
                teleportedObjects.Remove(r);
            } else {
                return null;
            }
            return entry;
        }

        public bool Teleporting = false;
        public bool TeleportingLate = false;
        public HashSet<Collider> enteringHidden = new HashSet<Collider>();
        public HashSet<Collider> exitingHidden = new HashSet<Collider>();

        public Coroutine currentAnimation;
        public PortalAnim currentAnimType;
        public bool animatingScale = false;

        public Color color = Color.gray;
        public Color particleColor = Color.white;
        protected float floorHeight = -0.05f;
        private bool setup = false;
        private bool hasUpdatedTrigger = false;

        public static MessageType visabilityMessageType;

        #region SET-UP

        public static void SetupNetworking() {
            visabilityMessageType = ModNetworking.CreateMessageType(DataType.Block, DataType.Boolean, DataType.Boolean);
            ModNetworking.Callbacks[visabilityMessageType] += ProcessBlockVisability;
        }
        protected void SendBlockVisability(BlockBehaviour b, bool visible, bool teleporting) {
            Message targetMessage = visabilityMessageType.CreateMessage(Block.From(b), visible, teleporting);
            ModNetworking.SendToAll(targetMessage);
        }

        public static void ProcessBlockVisability(Message m) {
            if (StatMaster.isLocalSim) {
                return;
            }
            Block b = (Block)m.GetData(0);
            bool visible = (bool)m.GetData(1);
            bool teleporting = (bool)m.GetData(2);

            if (visible) {
                b.InternalObject.VisualController.SetVisible();
            } else {
                b.InternalObject.VisualController.SetInvisible();
            }
            MarkAsTeleporting(b.InternalObject, teleporting);
        }

        protected static void CreatePrimitives() {
            if (!SlicingExists()) {
                //Debug.LogWarning("PortalDevice missing DingodileExtensions Slicing");
                return;
            }
            cubeMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cubeMesh.GetComponent<Collider>());
            cubeMesh.transform.position = Vector3.up * -5000f;
            sphereMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphereMesh.GetComponent<Collider>());
            sphereMesh.transform.position = Vector3.up * -5000f;
        }

        protected override void Awake() {
            int i = 0;
            base.Awake();
            if (first) {
                SetupNetworking();
                CreatePrimitives();
                first = false;
            }

            visParent = transform.GetChild(0);
            lockedVisual = visParent.GetChild(3).GetComponent<MeshRenderer>();
            nestEnd = visParent.GetChild(4).GetComponent<MeshRenderer>();

            hideTrigger = transform.GetChild(1).gameObject.AddComponent<RemoteRigidbodyTrigger>();
            hideTrigger.transform.localScale = new Vector3(1.15f, 1.15f, 2f);

            portalTrigger = transform.GetChild(2).gameObject.AddComponent<RemoteRigidbodyTrigger>();
            float size = 0.2f;
            BoxCollider[] cols = portalTrigger.GetComponentsInChildren<BoxCollider>();
            for (i = 0; i < cols.Length; i++) {
                Transform c = cols[i].transform;
                c.localPosition = new Vector3(c.localPosition.x, c.localPosition.y, 0f);
                c.localScale = new Vector3(5f, 9.5f, size);
            }
            portalTrigger.transform.localScale = new Vector3(portalTrigger.transform.localScale.x, portalTrigger.transform.localScale.y, portalTriggerSize);

            groundPlane = transform.GetChild(3);
            Transform t = transform.GetChild(4);
            edgeColliders = new Collider[t.childCount];
            i = 0;
            foreach (Transform child in t) {
                edgeColliders[i] = t.GetComponent<Collider>();
                i++;
            }

            coneTrigger = CreateCone().AddComponent<RemoteRigidbodyTrigger>();

            coneTrigger.OnRigidbodyTriggerEnter += ConeEnter;
            coneTrigger.OnRigidbodyTriggerExit += ConeExit;
            hideTrigger.OnRigidbodyTriggerEnter += HideEnter;
            hideTrigger.OnRigidbodyTriggerExit += HideExit;
            portalTrigger.OnRigidbodyTriggerEnter += TeleportEnter;
            portalTrigger.OnRigidbodyTriggerExit += TeleportExit;
            Events.OnMachineSimulationToggle += SimulationToggle;
            Events.OnMachineSimulationToggle += OnSimulationToggle;
            Events.OnDisconnect += ResetPlacement;

            curve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f));
        }

        protected GameObject CreateCone() {
            GameObject parent = new GameObject("coneTrigger");
            Rigidbody body = parent.AddComponent<Rigidbody>();
            GameObject child = new GameObject("capsule");
            CapsuleCollider capsule = child.AddComponent<CapsuleCollider>();

            parent.transform.parent = transform;
            child.transform.parent = parent.transform;

            parent.layer = 2;
            child.layer = 2;

            body.isKinematic = true;
            body.useGravity = false;

            child.transform.localPosition = new Vector3(0f, 0f, 6f);
            parent.transform.localRotation = Quaternion.identity;
            parent.transform.localPosition = Vector3.zero;
            capsule.isTrigger = true;
            capsule.radius = 4.3f;
            capsule.height = 15f;
            capsule.direction = 2;

            return parent;
        }

        public static void LoadConfig() {

            bool save = false;
            if (Configuration.GetData().HasKey("portal-resolution")) {
                float invScale = Configuration.GetData().ReadFloat("portal-resolution");
                resolutionDiv = (int)(1 / (invScale <= 0f ? 1 : invScale));
            } else {
                Configuration.GetData().Write("portal-resolution", 1f / (float)resolutionDiv);
                save = true;
            }

            if (Configuration.GetData().HasKey("portal-max-nesting")) {
                maxNesting = Configuration.GetData().ReadInt("portal-max-nesting");
                maxNesting = Mathf.Clamp(maxNesting, 1, 3);
            } else {
                Configuration.GetData().Write("portal-max-nesting", maxNesting);
                save = true;
            }

            if (Configuration.GetData().HasKey("allow-camera-teleport")) {
                allowCamTp = Configuration.GetData().ReadBool("allow-camera-teleport");
            } else {
                Configuration.GetData().Write("allow-camera-teleport", allowCamTp);
                save = true;
            }

            if (Configuration.GetData().HasKey("accurate-fog")) {
                accurateFog = Configuration.GetData().ReadBool("accurate-fog");
            } else {
                Configuration.GetData().Write("accurate-fog", accurateFog);
                save = true;
            }
            
            if (Configuration.GetData().HasKey("display-mirrored-meshes")) {
                displayMirrors = Configuration.GetData().ReadBool("display-mirrored-meshes");
            } else {
                Configuration.GetData().Write("display-mirrored-meshes", displayMirrors);
                save = true;
            }

            if (Configuration.GetData().HasKey("use-mirrored-bodies")) {
                mirroredBodies = Configuration.GetData().ReadBool("use-mirrored-bodies");
            } else {
                Configuration.GetData().Write("use-mirrored-bodies", mirroredBodies);
                save = true;
            }

            if (Configuration.GetData().HasKey("portaling-registrator-size")) {
                portalTriggerSize = Configuration.GetData().ReadFloat("portaling-registrator-size");
            } else {
                Configuration.GetData().Write("portaling-registrator-size", portalTriggerSize);
                save = true;
            }

            if (save) {
                Configuration.Save();
            }
        }

        public void Setup(int index, int portalLayer, int otherPortalLayer, int insidePortalLayer, int insideOtherPortalLayer, int nestedPortalLayer, Portal otherPortal, float hue, float saturation, float brightness) {
            transform.position = Vector3.up * -1000f;
            transform.rotation = Quaternion.identity;

            this.index = index;
            setup = true;
            this.otherPortal = otherPortal;

            groundPlane.GetChild(0).gameObject.layer = insidePortalLayer;
            if (maxNesting > 0) {
                nestEnd.gameObject.SetActive(true);
                camControl.Setup(Camera.main, this, otherPortal, new int[] { otherPortalLayer, insidePortalLayer, nestedPortalLayer, GLOBAL_PORTAL_IGNORE_LAYER }, new int[] { insideOtherPortalLayer });
                CreateRenderTex(" (Texture 1)", camControl, visParent.GetChild(0).GetComponent<MeshRenderer>(), 1, otherPortalLayer);
                nestEnd.gameObject.layer = insideOtherPortalLayer;
            }
            if (maxNesting > 1) {
                secCamControl.Setup(camControl.camera, this, otherPortal, new int[] { otherPortalLayer, insidePortalLayer, GLOBAL_PORTAL_IGNORE_LAYER }, new int[] { nestedPortalLayer }, camControl);//maxNesting > 2 ? new int[] { alayer, insideOtherPortalLayer } : 
                CreateRenderTex(" (Texture 2)", secCamControl, visParent.GetChild(1).GetComponent<MeshRenderer>(), 2, insideOtherPortalLayer);
                nestEnd.gameObject.layer = nestedPortalLayer;
            }
            if (maxNesting > 2) {
                nestEnd.gameObject.SetActive(false);
                terCamControl.Setup(secCamControl.camera, this, otherPortal, new int[] { insideOtherPortalLayer, insidePortalLayer, nestedPortalLayer, GLOBAL_PORTAL_IGNORE_LAYER }, new int[] { otherPortalLayer }, secCamControl, 3.5f, true);
                CreateRenderTex(" (Texture 3)", terCamControl, visParent.GetChild(2).GetComponent<MeshRenderer>(), 4, nestedPortalLayer);
            }
            
            layer = portalLayer;
            otherLayer = otherPortalLayer;
            insideLayer = insidePortalLayer;
            insideOtherLayer = insideOtherPortalLayer;

            foreach (Transform child in portalRenderer.transform) {
                MeshRenderer ren = child.GetComponent<MeshRenderer>();
                renderers.Add(ren);
                ren.gameObject.layer = portalLayer;
                SetHSV(ren, hue, saturation, brightness);
            }
            SetHSV(lockedVisual, hue * 0.85f, saturation, brightness + hue * 0.5f);
            nestEnd.material.SetColor("_TintColor", lockedVisual.material.GetColor("_TintColor"));
            particleColor = lockedVisual.material.GetColor("_TintColor") * 1.1f;
            color = SetHSV(new Color(1f, 0.5f, 0f), hue * 0.98f, 1f, 0.95f + hue * 0.03f);
        }

        public void SetHSV(MeshRenderer ren, float hue, float saturation, float value) {
            if (!ren.material.HasProperty("_TintColor")) {
                return;
            }
            Color col = SetHSV(ren.material.GetColor("_TintColor"), hue, saturation, value);
            ren.material.SetColor("_TintColor", col);
        }
        
        public Color SetHSV(Color color, float hue, float saturation, float value) {
            float a, h, s, v;
            a = color.a;
            Color.RGBToHSV(color, out h, out s, out v);
            h = (h + hue) % 1f;
            s = Mathf.Clamp(s * saturation, 0f, 1f);
            v = Mathf.Clamp(v * value, 0f, 1f);
            color = Color.HSVToRGB(h, s, v);
            color.a = a;
            return color;
        }

        public void CreateRenderTex(string name, PortalCameraControl control, MeshRenderer renderer, int fidelity, int layer) {
            if (!control) {
                Debug.LogError("Missing PortalCameraControl");
                return;
            }
            control.renderer = renderer;

            control.texture = new RenderTexture(Screen.width / (resolutionDiv * fidelity), Screen.height / (resolutionDiv * fidelity), 24);
            control.texture.name = this.name + name;
            control.texture.antiAliasing = 1;
            control.texture.filterMode = FilterMode.Bilinear;
            control.texture.wrapMode = TextureWrapMode.Clamp;
            control.camera.targetTexture = control.texture;

            control.renderer.gameObject.SetActive(true);
            control.renderer.material.mainTexture = control.texture;
            control.renderer.gameObject.layer = layer;
        }

        public void ReadRenderTex(ref Texture2D target, Camera cam, int fidelity) {
            RenderTexture tex = cam.targetTexture;
            RenderTexture def = RenderTexture.active;
            RenderTexture.active = tex;
            cam.Render();

            target.ReadPixels(new Rect(0, 0, tex.width / fidelity, tex.height / fidelity), 0, 0);
            target.Apply();

            RenderTexture.active = def;
        }

        #endregion

        public static bool teleportedCamera = false;
        public void LateUpdate() {
            if (!isSimulating) {
                return;
            }
            TeleportCamera();
        }

        #region PLACEMENT

        public void Place(PortalDevice device, Vector3 pos, Quaternion rot, Vector3 scale) {
            this.device = device;
            placed = true;
            this.scale = scale;

            open = otherPortal.placed;

            lockedVisual.gameObject.SetActive(!open);
            camControl.display = open;
            portalTrigger.gameObject.SetActive(false);
            coneTrigger.gameObject.SetActive(SimulatePhysics());

            Color color = lockedVisual.material.GetColor("_TintColor");
            lockedVisual.material.SetFloat("_DistortionPower", 0.055f);
            if (!open) {
                lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, 1f));
            } else {
                if (SlicingExists()) {
                    Physics.IgnoreLayerCollision(INVISIBLE_COLLIDER_LAYER, INVISIBLE_COLLIDER_LAYER, false);
                }
                lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, 0f));
            }

            gameObject.SetActive(true);
            transform.position = pos;
            transform.rotation = rot;
            transform.localScale = scale;
            visParent.localScale = Vector3.one * 0.01f;
            hasUpdatedTrigger = false;
            hideTrigger.transform.position = new Vector3(otherPortal.hideTrigger.transform.position.y, -1000f, 0f);

            if (SlicingExists()) {
                ProjectNearCollidersToOtherPortal();
            }
            PlaceGroundPlane();
            StartAnimation(PortalAnim.Place);
        }

        public void PlaceGroundPlane() {
            if (Mathf.Abs(Vector3.Dot(transform.forward, Vector3.up)) > 0.99f) {
                groundPlane.position = new Vector3(0f, -1000f, 0.1f);
                return;
            }
            groundPlane.localPosition = new Vector3(0f, -5f, 0.1f);
            groundPlane.position = new Vector3(groundPlane.position.x, floorHeight, groundPlane.position.z);
            groundPlane.rotation = Quaternion.LookRotation(-Vector3.up, transform.forward);
        }

        public void ResetPlacement() {
            if (isDestroyed) {
                return;
            }
            placed = false;
            open = false;
            transform.position = Vector3.up * -1000f;
            transform.rotation = Quaternion.identity;

            groundPlane.localPosition = new Vector3(0f, -5f, 0.1f);
            groundPlane.rotation = Quaternion.LookRotation(-Vector3.up, transform.forward);
            portalTrigger.gameObject.SetActive(false);
            foreach (var hidden in new Dictionary<Rigidbody, TriggerInfo>(hiddenObjects)) {
                RemoveHiddenObject(hidden.Value);
            }

            hasUpdatedTrigger = true;
            hideTrigger.transform.localPosition = Vector3.zero;
            hiddenObjects.Clear();
            teleportedObjects.Clear();
            teleportingObjects.Clear();
        }

        #endregion

        #region ANIMATION

        public void StartAnimation(PortalAnim anim) {
            if(!gameObject || !gameObject.activeInHierarchy) {
                return;
            }
            bool wasAnimating = animatingScale;
            PortalAnim state = currentAnimType;

            if (currentAnimation == null) {
                currentAnimType = PortalAnim.None;
            }
            switch (anim) {
                case PortalAnim.Place:
                    StopAnimation();
                    otherPortal.StopAnimation();

                    currentAnimation = StartCoroutine(AnimatePlace());

                    if (open) {
                        if (!otherPortal.open) {
                            otherPortal.currentAnimation = otherPortal.StartCoroutine(otherPortal.AnimateUnlock());
                        } else if (otherPortal.currentAnimType != PortalAnim.Place) {
                            otherPortal.currentAnimation = otherPortal.StartCoroutine(otherPortal.AnimateChange());
                        }
                    }
                    break;
                case PortalAnim.Disturb:
                    if (currentAnimation == null) {
                        currentAnimation = StartCoroutine(AnimateDisturbance());
                    }
                    break;
                case PortalAnim.Destroy:
                    if (wasAnimating || state == PortalAnim.Destroy) {
                        return;
                    }
                    StopAnimation();
                    otherPortal.StopAnimation();
                    open = false;
                    placed = false;

                    currentAnimation = StartCoroutine(AnimateClose());
                    otherPortal.currentAnimation = otherPortal.StartCoroutine(otherPortal.AnimateLock());
                    break;
            }
        }

        public void StopAnimation() {
            if (currentAnimation != null) {
                StopCoroutine(currentAnimation);
            }
            currentAnimation = null;
            currentAnimType = PortalAnim.None;
            animatingScale = false;
        }

        protected IEnumerator AnimateUnlock() {
            currentAnimType = PortalAnim.Unlock;
            portalTrigger.gameObject.SetActive(false);
            camControl.display = true;

            Vector3 startScale = visParent.localScale;
            float scaleDiff = Mathf.Abs(1f - startScale.x);
            if (scaleDiff > float.Epsilon) {
                animatingScale = true;
            }
            yield return new WaitForSeconds(duration * 0.2f * scaleDiff);
            float dur = duration * 1.1f;
            Color color = lockedVisual.material.GetColor("_TintColor");
            for (float t = 0f; t < dur; t += Time.deltaTime) {
                float pct = t / dur;
                if (scaleDiff > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(startScale, Vector3.one, curve.Evaluate(pct));
                    animatingScale = true;
                }
                
                lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 0f, pct)));
                portalRenderer.material.SetFloat("_DistortionPower", (1f - pct) * 0.05f);
                PlaceGroundPlane();
                yield return null;
                UpdateTrigger();
            }

            if (scaleDiff > float.Epsilon) {
                visParent.localScale = Vector3.one;
            }
            lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, 0f));
            portalRenderer.material.SetFloat("_DistortionPower", 0f);

            lockedVisual.gameObject.SetActive(false);
            portalTrigger.gameObject.SetActive(SimulatePhysics());
            otherPortal.portalTrigger.gameObject.SetActive(SimulatePhysics());

            open = true;
            otherPortal.open = true;
            StopAnimation();
        }

        protected IEnumerator AnimatePlace() {
            //if(wait > 0) yield return new WaitForSeconds(wait);
            
            currentAnimType = PortalAnim.Place;
            portalTrigger.gameObject.SetActive(false);
            Vector3 startScale = visParent.localScale;
            float scaleDiff = Mathf.Abs(1f - startScale.x);
            MeshRenderer ren = otherPortal.placed ? portalRenderer : lockedVisual;
            camControl.display = otherPortal.placed;

            float endValue = otherPortal.placed ? 0f : 0.055f;
            
            for (float t = 0f; t < duration; t += Time.deltaTime) {
                float pct = t / duration;
                if (scaleDiff > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(startScale, Vector3.one, curve.Evaluate(pct));
                    animatingScale = true;
                }

                ren.material.SetFloat("_DistortionPower", (1f - pct - endValue) * 0.4f + endValue);
                PlaceGroundPlane();
                yield return null;
                UpdateTrigger();
            }

            if (scaleDiff > float.Epsilon) {
                visParent.localScale = Vector3.one;
            }
            ren.material.SetFloat("_DistortionPower", endValue);
            portalTrigger.gameObject.SetActive(open && SimulatePhysics());

            PlaceGroundPlane();
            StopAnimation();
        }

        public void UpdateTrigger() {
            if (!hasUpdatedTrigger) {
                hasUpdatedTrigger = true;
                hideTrigger.transform.localPosition = Vector3.zero;
            }
        }

        protected IEnumerator AnimateChange() {
            currentAnimType = PortalAnim.Change;
            portalTrigger.gameObject.SetActive(false);
            MeshRenderer ren = portalRenderer;
            camControl.display = true;

            Vector3 startScale = visParent.localScale;
            float scaleDiff = Mathf.Abs(1f - startScale.x);
            for (float t = 0f; t < duration * 0.15f; t += Time.deltaTime) {
                float pct = t / duration;
                if (scaleDiff > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(startScale, Vector3.one, curve.Evaluate(pct));
                    animatingScale = true;
                }
                pct /= 0.15f;
                ren.material.SetFloat("_DistortionPower", pct * 0.4f);
                yield return null;
                UpdateTrigger();
            }
            for (float t = 0f; t < duration * 0.85f; t += Time.deltaTime) {
                float pct = t / duration;
                if (scaleDiff > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(startScale, Vector3.one, curve.Evaluate(pct + 0.15f));
                    animatingScale = true;
                }
                pct /= 0.85f;
                ren.material.SetFloat("_DistortionPower", (1.0f - pct) * 0.4f);
                yield return null;
            }

            if (scaleDiff > float.Epsilon) {
                visParent.localScale = Vector3.one;
            }
            ren.material.SetFloat("_DistortionPower", 0f);
            portalTrigger.gameObject.SetActive(open && SimulatePhysics());
            StopAnimation();
        }

        protected IEnumerator AnimateDisturbance() {
            currentAnimType = PortalAnim.Disturb;
            MeshRenderer ren = portalRenderer;

            for (float t = 0f; t < duration * 0.05f; t += Time.deltaTime) {
                float pct = t / (duration * 0.05f);
                ren.material.SetFloat("_DistortionPower", pct * 0.03f);
                yield return null;
                UpdateTrigger();
            }
            for (float t = 0f; t < duration * 0.35f; t += Time.deltaTime) {
                float pct = t / (duration * 0.35f);
                ren.material.SetFloat("_DistortionPower", (1.0f - pct) * 0.03f);
                yield return null;
            }

            ren.material.SetFloat("_DistortionPower", 0f);
            StopAnimation();
        }

        protected IEnumerator AnimateClose() {
            currentAnimType = PortalAnim.Destroy;
            portalTrigger.gameObject.SetActive(false);
            Vector3 startScale = visParent.localScale;
            camControl.display = true;

            float endValue = otherPortal.placed ? 0f : 0.055f;

            float dur = duration * 0.6f;
            Color color = lockedVisual.material.GetColor("_TintColor");
            for (float t = 0f; t < dur; t += Time.deltaTime) {
                float pct = t / dur;
                if (startScale.x > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(Vector3.zero, startScale, curve.Evaluate(1f - pct));
                    animatingScale = true;
                }

                lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 1f, curve.Evaluate(Mathf.Clamp(pct * 3f, 0f, 1f)))));
                portalRenderer.material.SetFloat("_DistortionPower", pct * 0.05f);
                PlaceGroundPlane();
                yield return null;
                UpdateTrigger();
            }

            visParent.localScale = Vector3.zero;
            lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, 1f));
            portalRenderer.material.SetFloat("_DistortionPower", 0.05f);

            yield return new WaitForSeconds(duration - dur);

            ResetPlacement();
            StopAnimation();
        }

        protected IEnumerator AnimateLock() {
            currentAnimType = PortalAnim.Lock;
            lockedVisual.gameObject.SetActive(true);
            portalTrigger.gameObject.SetActive(false);
            open = false;
            otherPortal.open = false;

            Vector3 startScale = visParent.localScale;
            float scaleDiff = Mathf.Abs(1f - startScale.x);
            float dur = duration;
            Color color = lockedVisual.material.GetColor("_TintColor");
            for (float t = 0f; t < dur; t += Time.deltaTime) {
                float pct = t / dur;

                if (scaleDiff > float.Epsilon) {
                    visParent.localScale = Vector3.Lerp(startScale, Vector3.one, curve.Evaluate(pct));
                    animatingScale = true;
                }
                lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 1f, pct)));
                portalRenderer.material.SetFloat("_DistortionPower", pct * 0.4f);
                PlaceGroundPlane();
                yield return null;
                UpdateTrigger();
            }

            if (scaleDiff > float.Epsilon) {
                visParent.localScale = Vector3.one;
            }

            lockedVisual.material.SetColor("_TintColor", new Color(color.r, color.g, color.b, 1f));
            portalRenderer.material.SetFloat("_DistortionPower", 0.4f);
            camControl.display = false;

            StopAnimation();
        }

        #endregion

        public HashSet<Rigidbody> inCone = new HashSet<Rigidbody>();

        public void OnSimulationToggle(PlayerMachine machine, bool toggle) {
            foreach(var r in new HashSet<Rigidbody>(inCone)) {
                if(r == null) {
                    inCone.Remove(r);
                }
            }
        }

        public void ConeEnter(Rigidbody r) {
            if (r.isKinematic
            || inCone.Contains(r)
            || r.transform.root != Machine.Active().transform.root) {
                return;
            }
            inCone.Add(r);
        }

        public void ConeExit(Rigidbody r) {
            if (!inCone.Contains(r)) {
                return;
            }
            inCone.Remove(r);
        }

        public void FixedUpdate() {
            if (!isSimulating || !open || !SimulatePhysics()) {
                return;
            }
            
            foreach(var r in inCone) {
                if (r == null) {
                    continue;
                }
                Vector3 pos = r.transform.position;

                //r.AddForce(GetDirectionToCenter(pos) * centerForce, ForceMode.Force);
                float dot = Vector3.Dot(r.velocity, -transform.forward);
                if (dot > 0) {
                    Vector3 projection = Vector3.Project(r.velocity, -transform.forward);
                    r.AddForce(GetDirectionToCenter(pos) * projection.magnitude * centerForce * 0.1f * device.GetCentering(), ForceMode.Force);
                }
            }
        }

        public Vector3 GetDirectionToCenter(Vector3 pos) {
            Vector3 vector = pos - transform.position;
            Vector3 projection = Vector3.Project(vector, transform.forward);
            Vector3 point = transform.position + projection;
            //Debug.DrawRay(pos, (point - pos), Color.magenta, Time.deltaTime * 2f);
            Vector3 result = point - pos;
            result = new Vector3(result.x, 0f, result.z);
            return result;
        }

        #region HIDING

        public void HideEnter(Rigidbody r) {
            if (ObjectNotEnvironmental(r.transform)) {
                return;
            }
            //Debug.Log(r.transform.root.name);
            if (r.GetComponent<ProjectileInfo>()) {
                return;
            }

            Vector3 max = Vector3.one * float.MinValue;
            Vector3 min = Vector3.one * float.MaxValue;
            GetMinMax(r, ref min, ref max);
            //Debug.Break();

            Vector3 relative = transform.InverseTransformPoint(max);
            if (relative.z > 0f) {/*
                Debug.DrawLine(min, r.worldCenterOfMass, Color.magenta * 0.5f, Time.deltaTime * 2f);
                Debug.DrawLine(max, r.worldCenterOfMass, Color.red, Time.deltaTime * 2f);*/
                return;
            }
            relative = transform.InverseTransformPoint(min);
            if (relative.z > 0f) {/*
                Debug.DrawLine(min, r.worldCenterOfMass, Color.magenta, Time.deltaTime * 2f);
                Debug.DrawLine(max, r.worldCenterOfMass, Color.red * 0.5f, Time.deltaTime * 2f);*/
                return;
            }/*
            Debug.DrawLine(min, r.worldCenterOfMass, Color.cyan, Time.deltaTime * 2f);
            Debug.DrawLine(max, r.worldCenterOfMass, Color.green, Time.deltaTime * 2f);*/

            AddHiddenObject(r);
        }

        public static void GetMinMax(Rigidbody r, ref Vector3 min, ref Vector3 max) {
            Collider[] cols = r.GetComponentsInChildren<Collider>();

            for (int i=0; i<cols.Length; i++) {
                Collider col = cols[i];
                if(!col || col.isTrigger || !col.gameObject.activeInHierarchy) {
                    continue;
                }
                if (col is BoxCollider) {
                    BoxCollider box = col as BoxCollider;
                    Vector3 center = box.transform.position;
                    Vector3 scale = box.size;// Vector3.Scale(box.size, box.transform.localScale);
                    Vector3 worldA = box.transform.TransformPoint(-scale * 0.5f);
                    Vector3 worldB = box.transform.TransformPoint( scale * 0.5f);
                    
                    Vector3 a = r.transform.InverseTransformPoint(worldA);
                    Vector3 b = r.transform.InverseTransformPoint(worldB);
                    GetMin(a, ref min);
                    GetMin(b, ref min);
                    GetMax(a, ref max);
                    GetMax(b, ref max);
                } else {
                    Vector3 temp = r.transform.InverseTransformPoint(col.bounds.min);
                    GetMin(temp, ref min);
                    temp = r.transform.InverseTransformPoint(col.bounds.max);
                    GetMax(temp, ref max);
                }
            }
            min = r.transform.TransformPoint(min);
            max = r.transform.TransformPoint(max);
        }

        public static void GetMin(Vector3 temp, ref Vector3 min) {
            if (temp.x < min.x) {
                min = new Vector3(temp.x, min.y, min.z);
            }
            if (temp.y < min.y) {
                min = new Vector3(min.x, temp.y, min.z);
            }
            if (temp.z < min.z) {
                min = new Vector3(min.x, min.y, temp.z);
            }
        }
        public static void GetMax(Vector3 temp, ref Vector3 max) {
            if (temp.x > max.x) {
                max = new Vector3(temp.x, max.y, max.z);
            }
            if (temp.y > max.y) {
                max = new Vector3(max.x, temp.y, max.z);
            }
            if (temp.z > max.z) {
                max = new Vector3(max.x, max.y, temp.z);
            }
        }

        public void HideExit(Rigidbody r) {
            if (RemoveHiddenObject(r)) {
                hiddenObjects.Remove(r);
            }
        }

        public void AddHiddenObject(Rigidbody r) {
            if (hiddenObjects.ContainsKey(r)
             || teleportedObjects.ContainsKey(r) || teleportingObjects.ContainsKey(r)) {
                return;
            }

            GameObject go = r.gameObject;

            MeshRenderer[] rens = go.GetComponentsInChildren<MeshRenderer>();
            Collider[] cols = go.GetComponentsInChildren<Collider>();

            HashSet<Collider> ignored = new HashSet<Collider>();
            for (int i = 0; i < cols.Length; i++) {
                Collider col = cols[i];
                if (!col || col.isTrigger || !col.enabled || !col.gameObject.activeSelf) {
                    continue;
                }
                ignored.Add(col);
            }

            HashSet<MeshRenderer> changed = new HashSet<MeshRenderer>();
            HashSet<MeshRenderer> shadows = new HashSet<MeshRenderer>();

            for (int i = 0; i < rens.Length; i++) {
                MeshRenderer ren = rens[i];
                if (VidyaMod.LayerMaskContains(AddPiece.Instance.layerMaskyHud, ren.gameObject.layer)
                || ren.name == SHADOW_CASTER || ren.name == "FloorGrid") {
                    continue;
                }
                if (layer != -1) {
                    if (ren.gameObject.layer == otherLayer) {
                        ren.gameObject.layer = GLOBAL_PORTAL_IGNORE_LAYER;
                        MeshRenderer shadow = GetShadow(ren.transform);
                        if (shadow) {
                            shadows.Add(shadow);
                        }
                    } else {
                        ren.gameObject.layer = layer;
                        ren.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        MeshRenderer shadow = Portal.CreateShadow(ren, r.gameObject.isStatic);
                        shadows.Add(shadow);
                    }
                }

                changed.Add(ren);
            }

            hiddenObjects.Add(r, new TriggerInfo(r, ignored, changed, shadows, currentAnimType != PortalAnim.None));
            
            for (int i = 0; i < cols.Length; i++) {
                Collider col = cols[i];
                Portal.IgnoreCollision(col, edgeColliders, true);
                IgnoreCollisionWithTeleport(col, true);
            }
        }
        
        public bool RemoveHiddenObject(TriggerInfo entry) {
            HashSet<Collider> cols = entry.cols;

            HashSet<MeshRenderer> rens = entry.rens;
            HashSet<MeshRenderer> shadows = entry.shadows;
            bool inOther = otherPortal.hiddenObjects.ContainsKey(entry.body);

            foreach (var ren in rens) {
                if (!ren) {
                    continue;
                }
                if (ren.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.On) {
                    if (ren.gameObject.layer == GLOBAL_PORTAL_IGNORE_LAYER) {
                        ren.gameObject.layer = otherLayer;
                    } else {
                        ren.gameObject.layer = 0;
                        ren.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                }
            }

            foreach (var shadow in shadows) {
                if (!shadow) {
                    continue;
                }
                if (inOther) {
                    continue;
                }
                GameObject.Destroy(shadow.gameObject);
            }

            foreach (var col in cols) {
                if (!col) {
                    continue;
                }
                Portal.IgnoreCollision(col, edgeColliders, false);
                if (inOther) {
                    continue;
                }
                IgnoreCollisionWithTeleport(col, false);
            }
            
            hiddenObjects.Remove(entry.body);
            if(entry.addedDuringAnimation && !ObjectNotEnvironmental(entry.body.transform)) {
                StartAnimation(PortalAnim.Destroy);
            }
            return true;
        }

        public static bool ObjectNotEnvironmental(Transform t) {
            return t.root.name == "_PERSISTENT"
                || t.root.name == "HUD"
                || t.root == PortalDevice.PortalA.transform
                || t.root == PortalDevice.PortalA.transform
                || t.root == PortalingMaster.MirrorMachineRoot
                || t.root == Machine.Active().transform.root
                || VidyaMod.LayerMaskContains(AddPiece.Instance.layerMaskyHud, t.gameObject.layer);
        }

        public bool RemoveHiddenObject(Rigidbody r) {
            if (!r || !hiddenObjects.ContainsKey(r)) {
                return false;
            }
            TriggerInfo entry = hiddenObjects[r];
            RemoveHiddenObject(entry);

            return true;
        }

        #endregion

        private static bool sliceChecked = false, sliceFound = false;
        public static bool SlicingExists() {
            if (!sliceChecked) {
                Type type = Type.GetType("DingodileExtensions.EzySlice.Slicer");

                sliceChecked = true;
                sliceFound = type != null;
            }
            return sliceFound;
        }

        public bool IsStill(Rigidbody r) {
            return r.isKinematic || (r.velocity.sqrMagnitude <= float.Epsilon && r.angularVelocity.sqrMagnitude <= float.Epsilon);
        }

        public void ProjectNearCollidersToOtherPortal() {
            Transform otherPortalEnvironmentals = otherPortal.transform.FindChild("environmentColliders");
            if(!otherPortalEnvironmentals) {
                otherPortalEnvironmentals = new GameObject("environmentColliders").transform;
                otherPortalEnvironmentals.parent = otherPortal.transform;
                otherPortalEnvironmentals.localPosition = Vector3.zero;
                otherPortalEnvironmentals.localRotation = Quaternion.identity;
            }
            environment.Clear();
            foreach (Transform child in otherPortalEnvironmentals) {
                Destroy(child);
            }
            Vector3 scale = new Vector3(30f, 30f, 20f);
            Vector3 pos = transform.TransformPoint(new Vector3(0f, 0f, scale.z / 2f));
            Quaternion rot = transform.rotation;
            LayerMask mask = AddPiece.CreateLayerMask(new int[] { 0, layer, otherLayer, GLOBAL_PORTAL_IGNORE_LAYER, 24, 28, 29 });
            Collider[] hitColliders = Physics.OverlapBox(pos, scale / 2f, rot, mask);

            for (int i = 0; i < hitColliders.Length; i++) {
                GameObject go;
                Collider org = hitColliders[i];
                if (org.attachedRigidbody && !IsStill(org.attachedRigidbody)) {
                    continue;
                }
                if (org is BoxCollider) {
                    go = GameObject.Instantiate(cubeMesh, org.transform.position, org.transform.rotation) as GameObject;
                    go.transform.localScale = org.transform.lossyScale;
                } else if (org is SphereCollider) {
                    go = GameObject.Instantiate(sphereMesh, org.transform.position, org.transform.rotation) as GameObject;
                    go.transform.localScale = org.transform.lossyScale;
                } else {
                    continue;
                }
                go.layer = INVISIBLE_COLLIDER_LAYER;
                GameObject[] gos = go.SliceInstantiate(transform.position, transform.forward);
                Destroy(go);
                go = gos[0];
                Destroy(gos[1]);
                if (go == null) {
                    continue;
                }
                go.layer = INVISIBLE_COLLIDER_LAYER;
                MeshFilter filter = go.GetComponent<MeshFilter>();
                Mesh colMesh = filter.mesh;
                MeshCollider meshCol = go.AddComponent<MeshCollider>();
                Destroy(go.GetComponent<MeshRenderer>());
                Destroy(filter);
                meshCol.sharedMesh = colMesh;
                meshCol.convex = true;
                meshCol.material = org.material;
                TeleportTransform(go.transform);
                go.transform.parent = otherPortalEnvironmentals;
                environment.Add(meshCol);
            }
        }

        #region TELEPORTING METHODS

        public void TeleportCamera() {

            MouseOrbit camScript = MouseOrbit.Instance;

            float focusSmooth;
            if (TeleportingLate) {
                FixedCameraBlock blockCam = FixedCameraController.hasInstance() ? FixedCameraController.Instance.activeCamera : null;
                if (blockCam != null) {
                    /*float lerp = blockCam.sliderLerp;
                    blockCam.sliderLerp = 100000f;
                    blockCam.LateUpdateBlock();
                    blockCam.FixedUpdateBlock();
                    blockCam.sliderLerp = lerp;*/
                    for (int i = 0; i < 100f; i++) {
                        blockCam.LateUpdateBlock();
                        blockCam.FixedUpdateBlock();
                    }
                } else if (camScript.targetType == MouseOrbit.TargetType.Block || camScript.targetType == MouseOrbit.TargetType.Machine) {
                    if (camScript.target.name == "CamFollow" || (teleportedObjects.Count > 0 && camScript.target.root == teleportedObjects.First().Value.block.transform.root)) {
                        focusSmooth = camScript.focusLerpSmooth;
                        float dist = (otherPortal.transform.position - transform.position).magnitude;
                        dist = dist / 10f;
                        if (dist < 1f) dist = 1f;
                        camScript.focusLerpSmooth = focusSmooth * dist;
                        camScript.UpdateCam();
                        camScript.focusLerpSmooth = focusSmooth;
                        //camScript.SetCameraPositionAndRotation(t.position, t.rotation);
                    }
                }
                TeleportingLate = false;
            }

            if (allowCamTp) {
                if (InputManager.Camera.LeftKeyHeld() || InputManager.Camera.RightKeyHeld() || InputManager.Camera.ForwardKeyHeld() || InputManager.Camera.BackwardKeyHeld() || InputManager.RightMouseButtonHeld()) {
                    Transform t = camScript.transform;
                    if (teleportedCamera) {
                        teleportedCamera = false;
                        MainCamTracker.UpdatePos();
                    }

                    Vector3 diff = (t.position - MainCamTracker.lastPos);

                    if (diff.sqrMagnitude < 0.0001f) {
                        return;
                    }
                    if (Vector3.Dot(diff.normalized, transform.forward) < 0f) {
                        RaycastHit hit;
                        Vector3 normal = diff.normalized;
                        RaycastHit[] hits = Physics.RaycastAll(t.position - normal * 0.1f, normal, diff.magnitude + 0.1f, AddPiece.CreateLayerMask(new int[] { 2 }));
                        //Debug.Log(hits.Length + ": " + name);
                        for (int i = 0; i < hits.Length; i++) {
                            hit = hits[i];
                            if (hit.collider.transform.parent == portalTrigger.transform) {
                                //Debug.Log(hit.collider.name);
                                teleportedCamera = true;

                                TeleportTransform(t);
                                camScript.SetCameraPositionAndRotation(t.position, t.rotation);
                                break;
                            }
                        }
                    }
                }
            }
        }

        public void TeleportTransform(Transform t) {
            TeleportTransform(t, t);
        }

        public void TeleportTransform(Transform target, Transform source) {
            Vector3 up = Vector3.up; // AddPiece.GetLocalDirClosestTo(portal, Vector3.up);

            Vector3 sourceUp = transform.InverseTransformDirection(source.up);
            sourceUp = Quaternion.AngleAxis(180f, up) * sourceUp;
            sourceUp = otherPortal.transform.TransformDirection(sourceUp);

            TeleportTransform(target, source, sourceUp);
        }

        public void TeleportTransform(Transform target, Transform source, Vector3 sourceUp) {
            Vector3 up = Vector3.up; // AddPiece.GetLocalDirClosestTo(portal, Vector3.up);

            Vector3 Offset = transform.InverseTransformPoint(source.position);
            Offset = Quaternion.AngleAxis(180f, Vector3.up) * Offset;
            Offset = otherPortal.transform.TransformPoint(Offset);
            target.position = Offset;

            Vector3 Direction = transform.InverseTransformDirection(source.forward);
            Direction = Quaternion.AngleAxis(180f, Vector3.up) * Direction;
            Direction = otherPortal.transform.TransformDirection(Direction);
            target.rotation = Quaternion.LookRotation(Direction, sourceUp);
        }

        public Vector3 TeleportForce(Vector3 velocity) {
            Vector3 up = Vector3.up; // AddPiece.GetLocalDirClosestTo(portal, Vector3.up);

            if(Vector3.Dot(transform.forward, up) > 0.65f) {
                if (velocity.sqrMagnitude == 0f) {
                    velocity = -transform.forward;
                }
                if(velocity.sqrMagnitude < Mathf.Pow(MIN_EXIT_VERTICAL_VELOCITY, 2f)) {
                    velocity = velocity.normalized * MIN_EXIT_VERTICAL_VELOCITY;
                }
            }

            Vector3 Offset = transform.InverseTransformVector(velocity);
            Offset = Quaternion.AngleAxis(180f, up) * Offset;
            Offset = otherPortal.transform.TransformVector(Offset);
            return Offset;
        }

        #endregion

        #region HANDLE TELEPORTATION

        public void TeleportEnter(Rigidbody r) {
            if (!SimulatePhysics()) {
                return;
            }
            if (r.transform.root == Machine.Active().transform.root) {
                AddTeleportingObject(r);
            } else {
                ProjectileInfo projectile = r.GetComponent<ProjectileInfo>();
                if (projectile) {
                    TeleportProjectile(projectile);
                }
            }
        }

        public void TeleportExit(Rigidbody r) {
            if (!SimulatePhysics() || !r) {
                return;
            }
            if (r.transform.root == Machine.Active().transform.root) {
                Vector3 relative = transform.InverseTransformPoint(r.worldCenterOfMass);
                //if exited on the "outside" side of the portal
                if (relative.z < 0f) {
                    UpgradeToTeleportedObject(r);
                    return;
                }
                RemoveTeleportObject(r);
            } else {
                ProjectileInfo projectile = r.GetComponent<ProjectileInfo>();
                if (projectile) {
                    PostTeleportProjectile(projectile);
                }
            }
        }

        public void AddTeleportingObject(Rigidbody r) {
            if (teleportingObjects.ContainsKey(r)) {
                SetTriggerInfoVisibility(teleportingObjects[r]);
                return;
            }
            TriggerInfo entry = CreateInfo(r, this);
            AddTeleportingObject(entry);
        }
        
        public void AddTeleportingObject(TriggerInfo entry) {
            if (teleportingObjects.ContainsKey(entry.body)) {
                SetTriggerInfoVisibility(entry);
                return;
            }

            AddTeleportingEntry(entry);
            UpdateTeleportObject(entry, true);
        }

        public void UpgradeToTeleportedObject(Rigidbody r) {
            if (!teleportingObjects.ContainsKey(r)) {
                return;
            }
            TriggerInfo entry = teleportingObjects[r];

            AddTeleportedEntry(entry);
            UpdateTeleportObject(entry, true, false);

            if (entry.type == TriggerInfo.Type.Block) {
                BlockTree tree = new BlockTree();
                GetBlockTree(entry.block, tree);
                if (tree.fract / (float)tree.total > TELEPORT_THRESHOLD) {
                    //Debug.Log(tree.fract + " / " + tree.total);
                    TeleportComposite(entry, tree);
                }
            }
        }

        public void AddTeleportedObject(Rigidbody r) {
            bool alreadyExists = teleportingObjects.ContainsKey(r);
            TriggerInfo entry = alreadyExists ? teleportingObjects[r] : CreateInfo(r, this);
            
            if (!alreadyExists) {
                teleportedObjects.Add(r, entry);
            }
            UpdateTeleportObject(entry, true);
        }

        public void CollideOnlyWithInside(TriggerInfo entry, bool insidePortalCollisions) {
            if (!SlicingExists()) {
                return;
            }
            
            foreach (var col in entry.cols) {
                if (!col) {
                    continue;
                }
                Portal.IgnoreCollision(col, environment.ToArray(), !insidePortalCollisions);
            }
        }

        public void TeleportComposite(TriggerInfo entry, BlockTree t) {
            Teleporting = true;
            otherPortal.Teleporting = true;

            BlockBehaviour other;
            for (int i = 0; i < t.blocks.Count; i++) {
                other = t.blocks[i];
                Rigidbody r = other.Rigidbody;
                Vector3 ang = r.angularVelocity;
                Vector3 vel = r.velocity;
                r.interpolation = RigidbodyInterpolation.None;
                
                otherPortal.RemoveTeleportObject(r);
                if (!IsDraggedBlock(other)) {
                    if (teleportedObjects.ContainsKey(r)) {
                        AddOtherNot(r);
                    } else if (teleportingObjects.ContainsKey(r)) {
                        AddOtherTeleporting(r);
                    } else {
                        AddOtherTeleported(r);
                    }
                }
                
                TeleportTransform(r.transform);
                r.interpolation = RigidbodyInterpolation.Interpolate;
                r.velocity = TeleportForce(vel);
                r.angularVelocity = ang;

                RemoveTeleportEntry(r);
            }
            otherPortal.TeleportingLate = true;
        }
        
        public void AddOtherNot(Rigidbody r) {
            RemoveTeleportObject(r);
        }
        public void AddOtherTeleporting(Rigidbody r) {
            TriggerInfo value = teleportingObjects[r];
            otherPortal.AddTeleportingObject(value);
        }
        public void AddOtherTeleported(Rigidbody r) {
            otherPortal.AddTeleportedObject(r);
        }

        public void RemoveTeleportObject(Rigidbody r) {
            TriggerInfo entry = RemoveTeleportEntry(r);
            UpdateTeleportObject(entry, false);
        }

        protected void UpdateTeleportObject(TriggerInfo entry, bool mark, bool collisionUpdate = true, bool? visible = null) {
            if (entry == null) {
                return;
            }
            CollideOnlyWithInside(entry, mark);
            if (visible.HasValue) {
                SetTriggerInfoVisibility(entry, visible.Value);
            } else {
                visible = SetTriggerInfoVisibility(entry);
            }

            MarkAsTeleporting(entry.block, mark);
            if (collisionUpdate) {
                foreach (var col in entry.cols) {
                    if (!col) {
                        continue;
                    }
                    IgnoreCollisionWithHidden(col, mark);
                }
            }
            foreach (var child in entry.childBlocks) {
                UpdateTeleportObject(child, mark, collisionUpdate, visible);
            }
        }

        #region Projectile

        public void TeleportProjectile(ProjectileInfo projectile) {
            Rigidbody r = projectile.Rigidbody;
            float a = Vector3.Dot(r.velocity.normalized, transform.forward);
            if (a < 0f) {
                IgnoreCollisionWithHidden(r.GetComponentInChildren<Collider>(), true);
                Vector3 vel = r.velocity;
                r.interpolation = RigidbodyInterpolation.None;
                TeleportTransform(r.transform);
                r.interpolation = RigidbodyInterpolation.Interpolate;
                r.velocity = TeleportForce(vel);
            }
        }

        public void PostTeleportProjectile(ProjectileInfo projectile) {
            Rigidbody r = projectile.Rigidbody;
            float a = Vector3.Dot(r.velocity.normalized, transform.forward);
            if (a > 0f) {
                IgnoreCollisionWithHidden(r.GetComponentInChildren<Collider>(), false);
            }
        }

        #endregion

        public void FixTeleportingObject(Rigidbody r) {
            TriggerInfo entry = RemoveTeleportEntry(r);
            
            if (entry != null) {
                UpdateTeleportObject(entry, false);
            } else if (r) {
                BlockBehaviour b = r.GetComponent<BlockBehaviour>();
                if (b) {
                    b.VisualController.SetVisible();
                } else {
                    MeshRenderer[] rens = r.GetComponentsInChildren<MeshRenderer>();
                    foreach (var ren in rens) {
                        ren.enabled = true;
                    }
                }
                ParticleSystem[] particles = r.GetComponentsInChildren<ParticleSystem>();
                foreach (var particle in particles) {
                    particle.gameObject.layer = 0;
                }
                Collider[] cols = b.myBounds.childColliders.ToArray();
                for (int i = 0; i < cols.Length; i++) {
                    Collider col = cols[i];
                    IgnoreCollisionWithHidden(col, false);
                }
            }
        }

        #endregion

        public bool SetTriggerInfoVisibility(TriggerInfo info) {
            bool visible = !teleportedObjects.ContainsKey(info.body) && !otherPortal.teleportingObjects.ContainsKey(info.body);
            SetTriggerInfoVisibility(info, visible);
            return visible;
        }

        public void SetTriggerInfoVisibility(TriggerInfo info, bool shouldBeVisible) {

            if (info.type == TriggerInfo.Type.Block) {
                if (shouldBeVisible) {
                    info.block.VisualController.SetVisible();
                } else {
                    info.block.VisualController.SetInvisible();
                }
                SetConntectedBracesVisability(info.block, shouldBeVisible);
                SendBlockVisability(info.block, shouldBeVisible, IsTeleporting(info.block));
            } else {
                foreach (var ren in info.rens) {
                    ren.enabled = shouldBeVisible;
                }
            }
            foreach (var particle in info.particles) {
                particle.gameObject.layer = shouldBeVisible ? 0 : insideLayer;
            }
        }

        public void SetConntectedBracesVisability(BlockBehaviour block, bool visible) {
            foreach(Joint j in block.jointsToMe) {
                if(j == null) {
                    continue;
                }
                BlockBehaviour b = JointToBlock(j);
                if (IsDraggedBlock(b)) {
                    if (visible) {
                        b.VisualController.SetVisible();
                        SendBlockVisability(b, true, false);
                    } else {
                        foreach (Joint jj in b.iJointTo) {
                            if (jj == null) {
                                continue;
                            }
                            if (JointConnectionToBlock(jj).VisualController.isVisible) {
                                b.VisualController.SetVisible();
                                SendBlockVisability(b, true, false);
                                goto Next;
                            }
                        }
                        b.VisualController.SetInvisible();
                        SendBlockVisability(b, false, false);
                    }
                }
                Next:;
            }
        }

        public static MeshRenderer CreateShadow(MeshRenderer ren, bool isStatic) {
            MeshRenderer shadow;
            if (isStatic) {
                shadow = GameObject.Instantiate(ren);
                Component[] components = shadow.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++) {
                    if (components[i] is MeshFilter || components[i] is MeshRenderer || components[i] is Transform) {
                        continue;
                    }
                    DestroyImmediate(components[i]);
                }
                foreach (Transform child in shadow.transform) {
                    DestroyImmediate(child);
                }
            } else {
                shadow = new GameObject(SHADOW_CASTER, typeof(MeshRenderer), typeof(MeshFilter)).GetComponent<MeshRenderer>();
                shadow.gameObject.isStatic = isStatic;
                shadow.transform.position = ren.transform.position;
                shadow.transform.rotation = ren.transform.rotation;
                shadow.transform.localScale = ren.transform.lossyScale;
                shadow.material = ren.material;
                shadow.GetComponent<MeshFilter>().mesh = ren.GetComponent<MeshFilter>().mesh;
            }
            shadow.name = SHADOW_CASTER;
            shadow.transform.SetParent(ren.transform, true);
            shadow.gameObject.layer = 0;
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            return shadow;
        }

        public static MeshRenderer GetShadow(Transform t) {
            Transform sc = t.FindChild(SHADOW_CASTER);
            if (!sc) {
                return null;
            }
            return sc.GetComponent<MeshRenderer>();
        }

        private bool isDestroyed = false;
        public void OnDestroy() {
            isDestroyed = true;
            InvokeReset();
            Physics.IgnoreLayerCollision(INVISIBLE_COLLIDER_LAYER, INVISIBLE_COLLIDER_LAYER, true);
        }

        public void InvokeReset() {
            ResetPlacement();
            first = false;
            Portal.IgnoreCollision(teleportingObjects, hiddenObjects, false);
            Portal.IgnoreCollision(teleportedObjects, hiddenObjects, false);
            Portal.IgnoreCollision(edgeColliders, hiddenObjects, false);
            teleportedObjects.Clear();
            teleportingObjects.Clear();
            enteringHidden.Clear();
            exitingHidden.Clear();
        }

        public void IgnoreCollisionWithHidden(Collider col, bool ignore) {
            Portal.IgnoreCollision(col, this.hiddenObjects, ignore);
            Portal.IgnoreCollision(col, otherPortal.hiddenObjects, ignore);
        }

        public void IgnoreCollisionWithTeleport(Collider col, bool ignore) {
            Portal.IgnoreCollision(col, teleportingObjects, ignore);
            Portal.IgnoreCollision(col, teleportedObjects, ignore);
            Portal.IgnoreCollision(col, otherPortal.teleportingObjects, ignore);
            Portal.IgnoreCollision(col, otherPortal.teleportedObjects, ignore);
        }

        public static void IgnoreCollision(Dictionary<Rigidbody, TriggerInfo> dict1, Dictionary<Rigidbody, TriggerInfo> dict2, bool toggled) {
            foreach (var entry in dict1) {
                foreach (var col in entry.Value.cols) {
                    if (!col) {
                        continue;
                    }
                    Portal.IgnoreCollision(col, dict2, toggled);
                }
            }
        }
        
        public static void IgnoreCollision(Collider[] cols, Dictionary<Rigidbody, TriggerInfo> dict2, bool toggled) {
            foreach (var col in cols) {
                if (!col) {
                    continue;
                }
                Portal.IgnoreCollision(col, dict2, toggled);
            }
        }

        public static void IgnoreCollision(Collider col, Dictionary<Rigidbody, TriggerInfo> dict, bool toggled) {
            if (!col || !col.gameObject.activeInHierarchy) {
                return;
            }
            foreach (var hidden in dict) {
                foreach (var other in hidden.Value.cols) {
                    if (!other || !other.enabled || !other.gameObject.activeInHierarchy) {
                        continue;
                    }
                    Physics.IgnoreCollision(col, other, toggled);
                }
            }
        }

        public static void IgnoreCollision(Collider col, Collider[] cols, bool toggled) {
            if (!col || !col.gameObject.activeInHierarchy) {
                return;
            }
            for (int i = 0; i < cols.Length; i++) {
                if (!cols[i] || !cols[i].enabled || !cols[i].gameObject.activeInHierarchy) {
                    continue;
                }
                Physics.IgnoreCollision(col, cols[i], toggled);
            }
        }

        public class BlockTree {
            public List<BlockBehaviour> blocks = new List<BlockBehaviour>();
            public int total = 0;
            public int fract = 0;
        }

        public static BlockTree GetBlockTree(BlockBehaviour block, BlockTree t) {
            /*if (!block) {
                Debug.LogError("Portal trying to teleport null block");
                return t;
            }*/
            if (t.blocks.Contains(block)) {
                return t;
            }

            if (!IsDraggedBlock(block)) {
                if (IsTeleporting(block)) {// && !block.VisualController.isVisible) {
                    t.fract++;
                }
                t.total++;
            }
            t.blocks.Add(block);

            Joint j;
            BlockBehaviour b;

            block.CreateSimLists();
            for (int i = 0; i < block.jointsToMe.Count; i++) {
                j = block.jointsToMe[i];
                if (!j) {
                    continue;
                }
                b = JointToBlock(j);
                t = GetBlockTree(b, t);
            }
            for (int i = 0; i < block.iJointTo.Count; i++) {
                j = block.iJointTo[i];
                if (!j) {
                    continue;
                }
                b = JointConnectionToBlock(j);
                t = GetBlockTree(b, t);
            }
            JoinOnTriggerBlock g;
            for (int i = 0; i < block.grabbers.Count; i++) {
                g = block.grabbers[i];
                if (!g) {
                    continue;
                }
                b = JointToBlock(g.currentJoint, false);
                t = GetBlockTree(b, t);
            }
            if (block is GrabberBlock) {
                ConfigurableJoint joint = (block as GrabberBlock).joinOnTriggerBlock.currentJoint;
                if(joint && joint.connectedBody) {
                    b = RigidbodyToBlock(joint.connectedBody);
                    if (b) {
                        t = GetBlockTree(b, t);
                    }
                }
            }
            return t;
        }

        public static bool SimulatePhysics() {
            return StatMaster.isMP ? (StatMaster.isClient ? StatMaster.isLocalSim  : true) : true;
        }
        
        public static void SimulationToggle(PlayerMachine machine, bool toggle) {
            if (!SimulatePhysics()) {
                return;
            }
            
            if (!toggle) {
                CleanupJointsToBlocks(machine);
                CleanupRigidbodiesToBlocks(machine);
            }
        }

        public static void CleanupJointsToBlocks(PlayerMachine machine) {
            foreach (var entry in new Dictionary<Joint, BlockBehaviour>(jointToBlock)) {
                if (entry.Key) {
                    BlockBehaviour b = entry.Value;
                    if (b) {
                        if (b.ParentMachine.BuildingMachine == machine.InternalObject.BuildingMachine) {
                            jointToBlock.Remove(entry.Key);
                        }
                    } else {
                        jointToBlock.Remove(entry.Key);
                    }
                } else {
                    jointToBlock.Remove(entry.Key);
                }
            }
        }
        
        public static void CleanupRigidbodiesToBlocks(PlayerMachine machine) {
            foreach (var entry in new Dictionary<Rigidbody, BlockBehaviour>(rigidbodyToBlock)) {
                if (entry.Key) {
                    BlockBehaviour b = entry.Value;
                    if (b) {
                        if (b.ParentMachine.BuildingMachine == machine.InternalObject.BuildingMachine) {
                            rigidbodyToBlock.Remove(entry.Key);
                        }
                    } else {
                        rigidbodyToBlock.Remove(entry.Key);
                    }
                } else {
                    rigidbodyToBlock.Remove(entry.Key);
                }
            }
        }

        protected static Dictionary<Joint, BlockBehaviour> jointToBlock = new Dictionary<Joint, BlockBehaviour>();
        protected static Dictionary<Rigidbody, BlockBehaviour> rigidbodyToBlock = new Dictionary<Rigidbody, BlockBehaviour>();

        public static BlockBehaviour JointToBlock(Joint j, bool useList = true) {
            if(j == null) {
                return null;
            }
            if (useList && jointToBlock.ContainsKey(j)) {
                return jointToBlock[j];
            }
            BlockBehaviour b = j.gameObject.GetComponentInParent<BlockBehaviour>();
            if (useList) {
                jointToBlock.Add(j, b);
            }
            return b;
        }

        public static BlockBehaviour JointConnectionToBlock(Joint j) {
            return RigidbodyToBlock(j.connectedBody);
        }

        public static BlockBehaviour RigidbodyToBlock(Rigidbody r) {
            if (rigidbodyToBlock.ContainsKey(r)) {
                return rigidbodyToBlock[r];
            }
            BlockBehaviour b = r.GetComponent<BlockBehaviour>();
            rigidbodyToBlock.Add(r, b);
            return b;
        }

        public static TriggerInfo CreateInfo(Rigidbody r, Portal p) {
            BlockBehaviour block = r.GetComponent<BlockBehaviour>();
            
            return CreateInfo(block, p);
        }

        public static TriggerInfo CreateInfo(BlockBehaviour block, Portal p) {

            ParticleSystem[] particles = block.GetComponentsInChildren<ParticleSystem>();

            HashSet<Collider> cols = new HashSet<Collider>();
            foreach (var col in block.myBounds.childColliders) {
                if (!col || !col.enabled || !col.gameObject.activeInHierarchy) {
                    continue;
                }
                cols.Add(col);
                p.IgnoreCollisionWithHidden(col, true);
            }
            HashSet<TriggerInfo> childBlocks = new HashSet<TriggerInfo>();
            foreach (var pair in block.parentedColliders) {
                childBlocks.Add(CreateInfo(pair.Key, p));
            }

            return new TriggerInfo(block.Rigidbody, block, cols, childBlocks, new HashSet<ParticleSystem>(particles));
        }
        
        public static Dictionary<BlockBehaviour, TriggerInfo> BlockToTriggerInfo(Dictionary<Rigidbody, TriggerInfo> source) {
            Dictionary<BlockBehaviour, TriggerInfo> target = new Dictionary<BlockBehaviour, TriggerInfo>();
            foreach (var entry in source) {
                target.Add(entry.Value.block, entry.Value);
            }

            return target;
        }
    }
}
