using Modding;
using Modding.Blocks;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace Dingodile {
    public class PortalingMaster {
        public static Transform MirrorMachineRoot;
        public static Dictionary<PlayerMachine, HashSet<MirrorBlock>> MirrorMachines = new Dictionary<PlayerMachine, HashSet<MirrorBlock>>();
        public static Dictionary<PlayerMachine, Transform> MirrorParents = new Dictionary<PlayerMachine, Transform>();
        private static bool didDelegates = false;

        public static void OnModLoad() {
            MirrorMachineRoot = new GameObject("Mirror Machines").transform;
            GameObject floorGrid = GameObject.Find("FloorGrid");
            floorGrid.layer = Portal.GLOBAL_PORTAL_IGNORE_LAYER;
            Rigidbody r = floorGrid.transform.parent.gameObject.AddComponent<Rigidbody>();
            r.useGravity = false;
            r.isKinematic = true;
            if (!didDelegates) {
                Events.OnMachineSimulationToggle += Simulate;
                didDelegates = true;
            }
        }

        public static void Simulate(PlayerMachine machine, bool toggle) {
            if (toggle) {
                StatMaster.Instance.StartCoroutine(IESimulate(machine));
            } else {
                if (MirrorMachines.ContainsKey(machine)) {
                    if (machine.InternalObject) {
                        foreach (var block in MirrorMachines[machine]) {
                            if (!block) {
                                continue;
                            }
                            foreach (var col in block.cols) {
                                IgnoreAllBlocksInMachine(col, machine.InternalObject, false);
                            }
                        }
                    }
                    MirrorMachines.Remove(machine);
                }
                if (MirrorParents.ContainsKey(machine)) {
                    GameObject.Destroy(MirrorParents[machine].gameObject);
                    MirrorParents.Remove(machine);
                }
            }
        }

        public static IEnumerator IESimulate(PlayerMachine machine) {
            yield return new WaitForEndOfFrame();
            if (Portal.displayMirrors) {
                CreateMirrorMachine(machine);
            }
        }

        public static void CreateMirrorMachine(PlayerMachine machine) {
            Transform parent = new GameObject(machine.InternalObject.name).transform;
            parent.parent = MirrorMachineRoot;

            HashSet<MirrorBlock> hash = new HashSet<MirrorBlock>();
            SetupTempVis();
            for (int i = 0; i < machine.SimulationBlocks.Count; i++) {
                Block b = machine.SimulationBlocks[i];
                hash.Add(MakeFakeBlock(b.InternalObject.VisualController, parent, true));
            }
            MirrorBlock.boundsMagnitude = machine.Bounds.size.sqrMagnitude;
            ClearTempVis();

            MirrorMachines.Add(machine, hash);
            MirrorParents.Add(machine, parent);
        }

        private static GameObject tempVis;
        private static Transform tempVisTransform;
        private static MeshFilter tempFilter;
        private static MeshRenderer tempRenderer;

        public static void SetupTempVis() {
            tempVis = new GameObject("Temporary Vis Block");
            tempVisTransform = tempVis.transform;
            tempFilter = tempVis.AddComponent<MeshFilter>();
            tempRenderer = tempVis.AddComponent<MeshRenderer>();
        }

        public static MirrorBlock MakeFakeBlock(BlockVisualController bvc, Transform parent, bool colliders = false) {
            if (bvc.gameObject.layer == 27) {
                return null;
            }
            bool b = false;
            Renderer r;
            GameObject go = new GameObject(bvc.Block.name + " holder");
            go.transform.position = bvc.transform.position;
            go.transform.rotation = bvc.transform.rotation;
            go.transform.localScale = bvc.transform.lossyScale;
            go.transform.parent = parent;

            bool setToInvisible = false;
            if (!bvc.isVisible) {
                setToInvisible = true;
                bvc.SetVisible();
            }
            Collider[] cols = new Collider[0];
            List<MeshRenderer> rens = new List<MeshRenderer>();
            List<MeshRenderer> brokens = new List<MeshRenderer>();
            foreach (Renderer rend in bvc.renderers) {
                if (rend == null || !rend.enabled) {
                    continue;
                }

                b = true;
                rens.Add(DisplayFake(rend, go.transform));
            }
            
            r = bvc.shortVisRen;

            bool hasBroken = false;
            if (!b && r != null && r.enabled) {
                rens.Add(DisplayFake(r, go.transform));
            }
            else if (bvc.hasFragment) {
                foreach (FilterRendererPair pair in bvc.Fragment.brokenVis) {
                    MeshRenderer rend = pair.renderer;
                    if (rend == null || !rend.enabled) {
                        continue;
                    }
                    hasBroken = true;
                    brokens.Add(DisplayFake(rend, go.transform));
                }
            }

            ParticleSystem[] sourceParticles = bvc.GetComponentsInChildren<ParticleSystem>();
            ParticleSystem[] particles = new ParticleSystem[sourceParticles.Length];
            for (int i = 0; i < sourceParticles.Length; i++) {
                particles[i] = GameObject.Instantiate(sourceParticles[i]) as ParticleSystem;
                particles[i].transform.parent = go.transform;
            }

            if (Portal.SimulatePhysics() && colliders) {
                cols = MakeColliders(bvc.Block, go.transform);
            }

            if (Portal.mirroredBodies) {
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
            }

            MirrorBlock m = go.AddComponent<MirrorBlock>();
            m.Setup(bvc.Block, rens.ToArray(), cols, particles, sourceParticles, brokens.ToArray(), hasBroken);

            if (setToInvisible) {
                bvc.SetInvisible();
            }
            return m;
            //what about visAddedToMe
        }

        private static MeshRenderer DisplayFake(Renderer r, Transform parent) {
            Transform vis = r.transform;
            Transform t;
            tempFilter.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            tempRenderer.sharedMaterials = r.sharedMaterials;
            t = GameObject.Instantiate(tempVisTransform, vis.position, vis.rotation) as Transform;
            t.localScale = vis.lossyScale;
            t.parent = parent;
            t.gameObject.layer = vis.gameObject.layer;
            MeshRenderer ren = t.GetComponent<MeshRenderer>();
            ren.gameObject.SetActive(r.gameObject.activeSelf);
            return ren;
        }

        private static Collider[] MakeColliders(BlockBehaviour block, Transform parent) {
            List<Collider> cols = new List<Collider>();
            if (block.hasMyBounds) {
                foreach (var col in block.myBounds.childColliders) {
                    if (!col || col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy) {
                        continue;
                    }
                    GameObject cg = new GameObject(col.name);
                    cg.layer = 2;
                    cg.transform.position = Vector3.up * -4000f;

                    if (col is BoxCollider) {
                        BoxCollider sourceBox = col as BoxCollider;
                        BoxCollider box = cg.AddComponent<BoxCollider>();
                        box.center = sourceBox.center;
                        box.size = sourceBox.size;
                        box.material = sourceBox.material;

                        IgnoreAllBlocksInMachine(box, block.ParentMachine);
                        cols.Add(box);
                    } else if (col is SphereCollider) {
                        SphereCollider sourceSphere = col as SphereCollider;
                        SphereCollider sphere = cg.AddComponent<SphereCollider>();
                        sphere.center = sourceSphere.center;
                        sphere.radius = sourceSphere.radius;
                        sphere.material = sourceSphere.material;

                        IgnoreAllBlocksInMachine(sphere, block.ParentMachine);
                        cols.Add(sphere);
                    } else if (col is CapsuleCollider) {
                        CapsuleCollider sourceCapsule = col as CapsuleCollider;
                        CapsuleCollider capsule = cg.AddComponent<CapsuleCollider>();
                        capsule.center = sourceCapsule.center;
                        capsule.radius = sourceCapsule.radius;
                        capsule.height = sourceCapsule.height;
                        capsule.material = sourceCapsule.material;

                        IgnoreAllBlocksInMachine(capsule, block.ParentMachine);
                        cols.Add(capsule);
                    }

                    cg.transform.position = col.transform.position;
                    cg.transform.rotation = col.transform.rotation;
                    cg.transform.localScale = col.transform.lossyScale;
                    cg.transform.parent = parent;
                }
            }
            return cols.ToArray();
        }

        public static void IgnoreAllBlocksInMachine(Collider col, Machine machine, bool ignore = true) {
            if (!machine) {
                Debug.LogError("Missing machine for block");
                return;
            }
            foreach (var a in machine.SimulationBlocks) {
                if (!a || !a.myBounds) {
                    continue;
                }
                foreach (var b in a.myBounds.childColliders) {
                    if (!b || !b.gameObject.activeInHierarchy || !b.enabled) {
                        continue;
                    }
                    Physics.IgnoreCollision(col, b, ignore);
                }
            }
        }
        
        public static void ClearTempVis() {
            GameObject.Destroy(tempVis);
            tempVis = null;
            tempVisTransform = null;
            tempFilter = null;
            tempRenderer = null;
        }
    }
}
