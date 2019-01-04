using System.Collections;
using System.Collections.Generic;
using Modding;
using Modding.Blocks;
using Modding.Levels;
using UnityEngine;

namespace Dingodile {
    class Poultryizer : BlockScript, IFireEffect {

        public class ChickenReferences {
            public GameObject prefab;
            public EntityAI AI;
            public Transform visPivot;
            public Transform vis;
            public Mesh mesh;
            public Material mat;
            public Texture tex;
            public GameObject bloodCorpse;
            public GameObject dustCorpse;
            public FireController fireControl;
            public RandomSoundController sounds;

            public static ChickenReferences Locate() {
                ChickenReferences chicken = new ChickenReferences();

                chicken.prefab = PrefabMaster.Instance.transform.FindChild("OBJECTS/Prefabs/Animals/WhiteChickenV2").gameObject;
                chicken.AI = chicken.prefab.GetComponent<EntityAI>();

                chicken.visPivot = chicken.prefab.transform.FindChild("Chicken");
                chicken.vis = chicken.visPivot.GetChild(0);
                chicken.mesh = chicken.vis.GetComponent<MeshFilter>().mesh;
                MeshRenderer ren = chicken.vis.GetComponent<MeshRenderer>();
                chicken.mat = ren.material;
                chicken.tex = ren.material.mainTexture;

                KillingHandler killer = chicken.prefab.GetComponent<KillingHandler>();
                chicken.bloodCorpse = killer.my.GibPrefab;
                chicken.dustCorpse = killer.my.corpseDust;
                chicken.fireControl = killer.my.fireControl;
                chicken.sounds = killer.my.SoundController;
                
                return chicken;
            }
        }
        protected static ChickenReferences Chicken;
        public static MessageType targetingMessageType;
        public Polymorpher polymorpher;
        public FireTag fireTag;

        protected AudioSource audioSource;
        protected Material lineMat;
        public MonoBehaviour target;
        public Transform targetTransform;
        protected float focusTime = 0f;
        protected const float focusDuration = 0.2f;
        protected bool hasBeamHit = false;
        protected bool toggled = false;

        private GameObject effect, targeter;
        private List<LineRenderer> lineRenderers;
        private Vector3[,] lineSegmentOrgPos = new Vector3[4, segments];
        private float xModifier, zModifier;
        private const int segments = 10;
        private const float eVariance = 0.1f;
        private const float effectUpdatesPerSecond = 30f;
        private float effectTimer = 0f;
        private bool rangeChanging = false;
        private bool radiusChanging = false;
        private bool animating = false;
        private bool targeting = false;
        private bool simulationStarted = false;
        public bool Heated { get { return heated > 0.4f; } }

        private MKey activateKey;
        private MSlider rangeSlider;
        private MSlider radiusSlider;
        private MToggle holdToShootToggle;

        public override void OnPrefabCreation() {
            Chicken = ChickenReferences.Locate();
            polymorpher = gameObject.AddComponent<Polymorpher>();
            polymorpher.Setup(this, Chicken);
            AddFire();
            Poultryizer.SetupNetworking();
            Polymorpher.SetupNetworking();
        }

        protected virtual void AddFire() {
            fireTag = gameObject.AddComponent<FireTag>();
            fireTag.basicInfo = GetComponent<BasicInfo>();
            fireTag.bvc = GetComponent<BlockVisualController>();
            fireTag.HasBasicInfo = true;
            fireTag.hasBvc = true;
            fireTag.block = fireTag.basicInfo as BlockBehaviour;
            fireTag.block.fireTag = fireTag;

            VisualController.canBeHeated = true;
            VisualController.heating.colToSet = "_EmissCol";
            VisualController.heating.lerpSpeed = 2f;
            VisualController.heating.glowCol = new Color(1f, 0.263f, 0f);
            
            MeshRenderer canonVis = PrefabMaster.Instance.transform.FindChild("BLOCKS/Prefabs/Cannon/Vis").GetComponent<MeshRenderer>();
            VisualController.renderers[0].material.SetTexture("_EmissMap", canonVis.material.GetTexture("_EmissMap"));
        }

        public override void SafeAwake() {
            activateKey = AddKey("Activate", "shoot", KeyCode.Y);
            rangeSlider = AddSlider("Range", "strength", 15f, 0.5f, 30f);
            radiusSlider = AddSlider("Radius", "radius", GetRadiusFromRange(rangeSlider.Value), 0.5f, 2f);
            holdToShootToggle = AddToggle("Hold to shoot", "hold-to-fire", false);

            rangeSlider.ValueChanged += RangeChanged;
            radiusSlider.ValueChanged += RadiusChanged;
        }

        private float heating = 0f;
        private float heated = 0f;
        public bool OnIgnite(FireTag t, Collider c, bool pyroMode) {
            heating += 10f * Time.deltaTime;
            return true;
        }

        protected virtual void RangeChanged(float value) {
            if(StatMaster.KeyMapper.disableSliderLimits) {
                return;
            }
            if (!radiusChanging) {
                rangeChanging = true;
                radiusSlider.SetValue(GetRadiusFromRange(value));
            }
        }

        protected virtual void RadiusChanged(float value) {
            if (StatMaster.KeyMapper.disableSliderLimits) {
                return;
            }
            if (!rangeChanging) {
                radiusChanging = true;
                rangeSlider.SetValue(GetRangeFromRadius(value));
            }
        }

        protected virtual float GetRadiusFromRange(float range) {
            return Mathf.Lerp(0.5f, 2f, 1f - Mathf.InverseLerp(0.5f, 30f, range));
        }

        protected virtual float GetRangeFromRadius(float radius) {
            return Mathf.Lerp(0.5f, 30f, 1f - Mathf.InverseLerp(0.5f, 2f, radius));
        }

        private void DoneMapperUpdate() {
            if(InputManager.LeftMouseButton() || InputManager.LeftMouseButtonHeld()) {
                return;
            }
            if (!rangeChanging && !radiusChanging) {
                return;
            }

            if (BlockMapper.CurrentInstance != null) {
                BlockMapper.Open(BlockMapper.CurrentInstance.Block);
            }
            rangeChanging = false;
            radiusChanging = false;
        }

        public override void BuildingLateUpdate() {
            DoneMapperUpdate();
        }

        public override void OnSimulateStart() {
            CreateEffectChild();

            effectTimer = Random.Range(0f, 1f / effectUpdatesPerSecond);

            ModAudioClip c = ModResource.GetAudioClip("polymorph");
            if (c.Available) {
                audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                audioSource.clip = c;
            }

            simulationStarted = true;
        }

        public override void SimulateUpdateAlways() {
            if (heating > 0f || heated > 0f) {

                heating = Mathf.Clamp(heating - Time.deltaTime, 0f, 1f);
                heated = Mathf.Lerp(heated, (heating > 0f ? 1f : 0f), Time.deltaTime);
                if (heated <= 0.01f) {
                    heated = 0f;
                }
            }
            AnimateBeam();
        }

        public override void SimulateUpdateHost() {
            PhysicsBeam();
            SendTargetPos();
        }

        public override void SimulateLateUpdateClient() {
            if (!simulationStarted) {
                return;
            }
                
            if (effectTimer == 0) {
                hasBeamHit = false;
            }
        }

        protected static void SetupNetworking() {
            targetingMessageType = ModNetworking.CreateMessageType(DataType.Block, DataType.Vector3);
            ModNetworking.Callbacks[targetingMessageType] += ProcessRemoteTargeting;
        }

        protected static void ProcessRemoteTargeting(Message m) {
            Block block = (Block)m.GetData(0);
            Vector3 pos = (Vector3)m.GetData(1);
            Poultryizer script = block.GameObject.GetComponent<Poultryizer>();
            script.ProcessRemoteTargeting(pos);
        }

        public override void OnSimulateStop() {
            base.OnSimulateStop();
        }

        protected void SendTargetPos() {
            if (!simulationStarted) {
                return;
            }
            if (!StatMaster.isLocalSim) {
                Message targetMessage = targetingMessageType.CreateMessage(Block.From(this), targeter.transform.position);
                ModNetworking.SendToAll(targetMessage);
            }
        }
        
        public virtual void ProcessRemoteTargeting(Vector3 pos) {
            if (!simulationStarted) {
                Debug.LogWarning("Trying to process a target on an unstarted Polutryizer: " + name + " - i: " + transform.GetSiblingIndex() );
                return;
            }
            targeter.transform.position = pos;
            hasBeamHit = true;
        }
        
        protected virtual void GetTarget() {
            Vector3 fwd = -transform.up;
            Vector3 pos = effect.transform.position + fwd * 0.25f;
            RaycastHit hit;
            if (Physics.SphereCast(pos, radiusSlider.Value, fwd, out hit, rangeSlider.Value)) {
                targeter.transform.position = hit.point;
                hasBeamHit = true;

                Rigidbody r = hit.collider.attachedRigidbody;
                if (r == null) {
                    ResetTarget();
                    return;
                }

                Transform t = r.transform;

                if (!Heated && t.name.Contains("Chicken")) {
                    ResetTarget();
                    return;
                }
                
                if (RemoveProjectile(t)) {
                    ResetTarget();
                    return;
                }

                MonoBehaviour targ;
                if (targetTransform == t) {
                    targ = target;
                    
                    focusTime += Time.deltaTime;
                    if (focusTime > focusDuration) {
                        polymorpher.ReplaceWithChickenHost(targ, Heated);
                        ResetTarget();
                    }
                } else {
                    targ = GetAIScript(t.gameObject);
                    if (targ == null) {
                        ResetTarget();
                        return;
                    }
                    
                    focusTime = 0f;
                }
                SetTarget(targ, t);
                return;
            }
            hasBeamHit = false;
            ResetTarget();
        }

        private static MonoBehaviour tempBehaviour;
        public static MonoBehaviour GetAIScript(GameObject c) {
            tempBehaviour = null;
            tempBehaviour = c.gameObject.GetComponent<EntityAI>();
            if (!tempBehaviour) tempBehaviour = c.gameObject.GetComponent<EnemyAISimple>();
            if (!tempBehaviour) tempBehaviour = c.gameObject.GetComponent<SimpleBirdAI>();
            return tempBehaviour;
        }

        protected void SetTarget(MonoBehaviour b, Transform t) {
            if (targetTransform != t) {
                target = b;
                targetTransform = t;
            }
        }

        public void ResetTarget() {
            target = null;
            targetTransform = null;
            focusTime = 0f;
        }

        public virtual void PhysicsBeam() {
            if (!simulationStarted) {
                return;
            }
            if (activateKey.IsPressed) {
                if (!holdToShootToggle.IsActive) {
                    targeting = !targeting;
                }
            } else {
                if (holdToShootToggle.IsActive) {
                    targeting = activateKey.IsDown;
                }
            }
            if (targeting) {
                GetTarget();
            } else {
                ResetTarget();
            }
        }

        public virtual void AnimateBeam() {
            if (!simulationStarted) {
                return;
            }
            if (activateKey.IsPressed) {
                if (!holdToShootToggle.IsActive) {
                    animating = !animating;
                } 
            } else {
                if (holdToShootToggle.IsActive) {
                    animating = activateKey.IsDown;
                }
            }
            if (animating) {
                if(effect.activeSelf == false) {
                    AnimateLinesNoTarget();
                    effect.SetActive(true);
                }
                if (effectTimer >= (1f/effectUpdatesPerSecond)) {
                    effectTimer = 0f;
                    if (hasBeamHit) {
                        AnimateLines();
                    } else {
                        AnimateLinesNoTarget();
                    }
                } else {
                    effectTimer += Time.deltaTime;
                }
            } else if (effect.activeSelf == true) {
                effectTimer = 0f;
                effect.SetActive(false);
            }
        }

        protected virtual void AnimateLines() {
            for (int i = 0; i < 4; i++) {
                float pct = heated * 0.4f;
                float heat = Mathf.Lerp(0f, 0.5f, pct);
                float rev = Mathf.Lerp(1f, 0.2f, heated);
                lineRenderers[i].SetColors(new Color(heat + 0.075f, heat/2f + 0.01f, rev), new Color(heat/2f, heat/2f + 0.05f, rev));

                for (int j = 2; j < segments - 1; j++) {
                    lineRenderers[i].SetPosition(j, new Vector3(lineSegmentOrgPos[i, j].x + (j / (float)segments) * (targeter.transform.localPosition.x - lineSegmentOrgPos[i, 1].x) + Random.Range(-eVariance, eVariance),
                                                                lineSegmentOrgPos[i, 1].y + (j / (float)segments) * (targeter.transform.localPosition.y - lineSegmentOrgPos[i, 1].y),
                                                                lineSegmentOrgPos[i, j].z + (j / (float)segments) * (targeter.transform.localPosition.z - lineSegmentOrgPos[i, 1].z) + Random.Range(-eVariance, eVariance)));
                }
                lineRenderers[i].SetPosition(0, lineSegmentOrgPos[i, 0]);
                lineRenderers[i].SetPosition(1, new Vector3(lineSegmentOrgPos[i, 1].x + Random.Range(-eVariance, eVariance),
                                                            lineSegmentOrgPos[i, 1].y,
                                                            lineSegmentOrgPos[i, 1].z + Random.Range(-eVariance, eVariance)));
                lineRenderers[i].SetPosition(segments - 1, new Vector3(lineSegmentOrgPos[i, segments - 1].x + (targeter.transform.localPosition.x - lineSegmentOrgPos[i, 1].x),
                                                                     targeter.transform.localPosition.y,
                                                                     lineSegmentOrgPos[i, segments - 1].z + (targeter.transform.localPosition.z - lineSegmentOrgPos[i, 1].z)));
            }
        }
        protected virtual void AnimateLinesNoTarget() {
            for (int i = 0; i < 4; i++) {
                float xM = ((i - 1) % 2) * 0.05f;
                float zM = ((i - 2) % 2) * 0.05f;
                lineRenderers[i].SetPosition(0, lineSegmentOrgPos[i, 0]);
                for (int j = 1; j < segments; j++)
                    lineRenderers[i].SetPosition(j, new Vector3(lineSegmentOrgPos[i, 0].x + Random.Range(0f, xM * 4f) + xM * 0.2f + Random.Range(-0.2f, 0.2f),
                                                                lineSegmentOrgPos[i, 1].y / 1.5f,
                                                                lineSegmentOrgPos[i, 0].z + Random.Range(0f, zM * 4f) + zM * 0.2f + Random.Range(-0.2f, 0.2f)));
            }
        }
        
        private void CreateEffectChild() {
            effect = new GameObject("Polymorph Effect");
            effect.transform.parent = transform;
            effect.transform.localPosition = new Vector3(0f, -1.75f, 0.65f);
            targeter = new GameObject("Targeter");
            targeter.transform.parent = effect.transform;
            /*  DEBUG SPHERE
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(sphere.GetComponent<Rigidbody>());
                Destroy(sphere.GetComponent<SphereCollider>());
                sphere.transform.parent = targeter.transform;
                sphere.transform.localPosition = Vector3.zero;
                sphere.transform.localScale = Vector3.one * (0.001931f * (range * range) - 0.075f * range + 1.075f);
            */
            lineMat = new Material(Shader.Find("Particles/Additive"));
            lineRenderers = new List<LineRenderer>();
            for (int i = 0; i < 4; i++) {
                float length = 5f;
                xModifier = ((i - 1) % 2) * 0.175f;
                zModifier = ((i - 2) % 2) * 0.175f;
                lineRenderers.Add(new GameObject("Line " + i, typeof(LineRenderer)).GetComponent<LineRenderer>());
                lineRenderers[i].transform.parent = effect.transform;
                lineRenderers[i].transform.localPosition = new Vector3(xModifier, 0f, zModifier);
                lineRenderers[i].material = lineMat;
                lineRenderers[i].SetVertexCount(segments);
                lineRenderers[i].useWorldSpace = false;
                lineRenderers[i].SetWidth(0.1f, 0.1f);
                lineRenderers[i].SetColors(new Color(0.075f, 0.01f, 1f), new Color(0f, 0.05f, 1f));

                lineSegmentOrgPos[i, 0] = -lineRenderers[i].transform.localPosition;
                for (int j = 1; j < segments - 1; j++) {
                    float displace = (((float)j - 1f) / ((float)segments - 2f)) * 0.65f;
                    lineSegmentOrgPos[i, j] = new Vector3(-xModifier * displace, -j * length / (float)segments, -zModifier * displace);
                    lineRenderers[i].SetPosition(j, lineSegmentOrgPos[i, j]);
                }
                lineSegmentOrgPos[i, segments - 1] = new Vector3(-xModifier, -((float)segments - 1f) * length / ((float)segments - 1f), -zModifier);
                lineRenderers[i].SetPosition(0, lineSegmentOrgPos[i, 0]);
                lineRenderers[i].SetPosition(segments - 1, lineSegmentOrgPos[i, segments - 1]);
            }
            effect.transform.localRotation = Quaternion.identity;
            effect.SetActive(false);
        }

        protected bool RemoveProjectile(Transform t) {
            if (t.gameObject.layer == LayerMask.NameToLayer("EnemyProjectile") && t.name.Contains("Arrow") || t.name.Contains("Axe")) {
                DisableProjectileArrow(t.GetComponent<ProjectileScript>());
                return true;
            }
            if (t.tag == "ArrowRigStatic" || t.tag == "ArrowRigMove") {
                Destroy(t.gameObject);
                return true;
            }
            return false;
        }

        protected void DisableProjectileArrow(ProjectileScript pro) {
            if (StatMaster.isHosting && !StatMaster.isLocalSim) {
                NetworkProjectile netProjectile = pro.GetComponent<NetworkProjectile>();
                ProjectileManager.Instance.Despawn(netProjectile);
            } else {
                gameObject.SetActive(false);
                pro.projectileInfo.Rigidbody.isKinematic = true;
            }
            if (!Object.ReferenceEquals(pro.firecontrol, null) && pro.firecontrol.onFire && pro.firecontrol.gameObject.activeInHierarchy) {
                pro.firecontrol.DouseFire();
            }
            pro.hasAttached = false;
        }

    }
}
