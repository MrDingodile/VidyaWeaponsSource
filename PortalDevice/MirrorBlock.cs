using UnityEngine;

namespace Dingodile {
    public class MirrorBlock : MonoBehaviour {
        public BlockBehaviour source;
        public MeshRenderer[] rens;
        public MeshRenderer[] brokens;
        public Collider[] cols;
        public ParticleSystem[] particles;
        public ParticleSystem[] sourceParticles;
        public bool visible = true;
        public static float boundsMagnitude = 400f;
        private bool setup = false;
        private bool hasFragment = false;

        public void Setup(BlockBehaviour source, MeshRenderer[] rens, Collider[] cols, ParticleSystem[] particles, ParticleSystem[] sourceParticles, MeshRenderer[] brokens, bool hasFragment) {
            this.source = source;
            this.rens = rens;
            this.brokens = brokens;
            this.cols = cols;
            this.particles = particles;
            this.sourceParticles = sourceParticles;
            setup = true;
            this.hasFragment = hasFragment;
        }

        public void LateUpdate() {
            if (!setup || !source || !StatMaster.levelSimulating) {
                return;
            }
            Bounds b;

            if(!PortalDevice.PortalA.placed || !PortalDevice.PortalB.placed) {
                if (transform.position != Vector3.up * -300f) {
                    transform.position = Vector3.up * -3000f;
                }
                return;
            }

            SetVisibility();
            if (!visible) {
                for (int i = 0; i < particles.Length; i++) {
                    if (particles[i].isPlaying) {
                        particles[i].Stop();
                    }
                }
                return;
            }
            
            float magnitudeA = (PortalDevice.PortalA.transform.position - source.transform.position).sqrMagnitude;
            float magnitudeB = (PortalDevice.PortalB.transform.position - source.transform.position).sqrMagnitude;
            bool favorA = magnitudeA < magnitudeB;
            float magnitude = favorA ? magnitudeA : magnitudeB;
            if(magnitude > boundsMagnitude) {
                FixSourceBlock();
                SetVisibility(false);
                return;
            }
            Portal portal = favorA ? PortalDevice.PortalA : PortalDevice.PortalB;
            portal.TeleportTransform(transform, source.transform);
            for (int i = 0; i < particles.Length; i++) {
                if (sourceParticles[i].isPlaying) {
                    if (!particles[i].isPlaying) {
                        particles[i].Play();
                    }
                    portal.TeleportTransform(particles[i].transform, sourceParticles[i].transform);
                } else {
                    if (particles[i].isPlaying) {
                        particles[i].Stop();
                    }
                }
            }

        }

        public void SetVisibility() {
            SetVisibility(!source.VisualController.isVisible || Portal.IsTeleporting(source));
        }

        public void SetVisibility(bool visible) {
            if (this.visible != visible) {
                for (int i = 0; i < rens.Length; i++) {
                    rens[i].enabled = visible;
                    MatchRenderer(rens[i], source.VisualController.renderers[i]);
                }
                for (int i = 0; i < brokens.Length; i++) {
                    brokens[i].enabled = visible;
                    MatchRenderer(brokens[i], source.VisualController.Fragment.brokenVis[i].renderer);
                }
                for (int i = 0; i < cols.Length; i++) {
                    cols[i].enabled = visible;
                }
                this.visible = visible;
            }
            else if (visible) {
                for (int i = 0; i < rens.Length; i++) {
                    MatchRenderer(rens[i], source.VisualController.renderers[i]);
                }
            }
        }

        public void MatchRenderer(MeshRenderer target, MeshRenderer source) {
            if(source == null) {
                Debug.LogWarning("PortalDevice: Missing source renderer");
                return;
            }
            MatchBroken(target);
            if (target.gameObject.activeSelf != source.gameObject.activeSelf) {
                target.gameObject.SetActive(source.gameObject.activeSelf);
            }
            if (target.material != source.material) {
                target.material = source.material;
            }
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            source.GetPropertyBlock(props);
            target.SetPropertyBlock(props);
        }

        public void FixSourceBlock() {
            if (BesiegeLogFilter.logDebug) {
                Debug.LogWarning("PortalDevice: Had to fix mirrored block error for " + source.name + " with index: " + source.transform.GetSiblingIndex() + " in '" + source.ParentMachine.name + "'");
            }
            PortalDevice.PortalA.FixTeleportingObject(source.Rigidbody);
            PortalDevice.PortalB.FixTeleportingObject(source.Rigidbody);
        }

        public bool GetBrokenOffRen(out MeshRenderer ren) {
            if (!hasFragment) {
                ren = null;
                return false;
            }
            ren = brokens[0];
            if (ren.gameObject.activeSelf) {
                return false;
            }
            return true;
        }

        public void MatchBroken(MeshRenderer target) {
            MeshRenderer broken;
            this.source.CreateSimLists();

            if (this.source.iJointTo.Count > 0 && GetBrokenOffRen(out broken)) {
                if (target == broken && broken.transform.parent == transform) {
                    if (this.source.iJointTo[0].connectedBody != null) {
                        target.transform.parent = FromBlock(this.source.iJointTo[0].connectedBody.transform);
                    }
                }
            }
        }

        public static Transform FromBlock(Transform source) {
            int i = source.GetSiblingIndex();
            Transform parent = PortalingMaster.MirrorMachineRoot.FindChild(source.parent.parent.name);
            return parent.GetChild(i);
        }
    }
}
