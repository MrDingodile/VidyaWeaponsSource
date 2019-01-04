using System;
using Modding;
using System.Collections;
using UnityEngine;

namespace Dingodile {
    class ToChickenColliderAnimator : MonoBehaviour {
        private EntityAI ai;
        private EnemyAISimple simpleai;
        private bool spherical = false;
        private bool isChildCollider = false;
        private SphereCollider sphere;
        private CapsuleCollider capsule;
        private Vector3 startCenter;
        private float startRadius;
        private float startHeight;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Vector3 startScale;
        private SphereCollider targetCollider;
        private float maxAxisScale = 1f;

        private bool setup = false;
        private const float duration = 0.25f;
        private float timer = 0f;

        public void Setup(EntityAI e, SphereCollider c) {
            ai = e;
            targetCollider = c;
            Setup(e.my.Collider, e.transform);

            e.selfRighting.enabled = false;
            //RigidbodyConstraints rc = ai.my.Rigidbody.constraints;
            //ai.my.Rigidbody.constraints = RigidbodyConstraints.None;
            //SetCollider(1f);
            //ai.my.Rigidbody.constraints = rc;
            //ResetSelfRightingAI();
        }

        public void Setup(Collider target, Transform p) {
            setup = true;
            if (target is SphereCollider) {
                spherical = true;
                sphere = target as SphereCollider;
            } else if (target is CapsuleCollider) {
                spherical = false;
                capsule = target as CapsuleCollider;
            } else {
                DestroyImmediate(this);
                return;
            }

            Transform t = target.transform;
            isChildCollider = t != p;
            if (isChildCollider) {
                startPosition = t.localPosition;
                startRotation = t.localRotation;
                startScale = t.localScale;
                maxAxisScale = Mathf.Max(startScale.x, startScale.y, startScale.z);
            }

            if (spherical) {
                startCenter = sphere.center;
                startRadius = sphere.radius;
            } else {
                startCenter = capsule.center;
                startRadius = capsule.radius;
                startHeight = capsule.height;
            }
        }

        public void SetCollider(float pct) {
            if (spherical) {
                sphere.center = Vector3.Lerp(startCenter, isChildCollider ? targetCollider.center : targetCollider.center + targetCollider.transform.localPosition, pct);
                sphere.radius = Mathf.Lerp(startRadius, isChildCollider ? targetCollider.radius : targetCollider.radius * maxAxisScale, pct);

                if (isChildCollider) {
                    sphere.transform.localPosition = Vector3.Lerp(startPosition, targetCollider.transform.localPosition, pct);
                    sphere.transform.localRotation = Quaternion.Slerp(startRotation, targetCollider.transform.localRotation, pct);
                    sphere.transform.localScale = Vector3.Lerp(startScale, targetCollider.transform.localScale, pct);
                }
            } else {
                capsule.center = Vector3.Lerp(startCenter, isChildCollider ? targetCollider.center : targetCollider.center + targetCollider.transform.localPosition, pct);
                capsule.radius = Mathf.Lerp(startRadius, isChildCollider ? targetCollider.radius : targetCollider.radius * maxAxisScale, pct);
                capsule.height = Mathf.Lerp(startHeight, isChildCollider ? targetCollider.radius * 2f + 0.01f : targetCollider.radius * maxAxisScale * 2f + 0.01f, pct);

                if (isChildCollider) {
                    capsule.transform.localPosition = Vector3.Lerp(startPosition, targetCollider.transform.localPosition, pct);
                    capsule.transform.localRotation = Quaternion.Slerp(startRotation, targetCollider.transform.localRotation, pct);
                    capsule.transform.localScale = Vector3.Lerp(startScale, targetCollider.transform.localScale, pct);
                }
            }
            ai.CalculateHeight(ai.my.Collider);
        }
        
        public void LateUpdate() {
            if (!setup) {
                return;
            }
            if (timer > duration) {
                timer = duration;
            }
            
            float pct = timer / duration;
            SetCollider(pct);
            
            if (timer == duration) {
                Destroy(this);
                return;
            } else {
                timer += Time.deltaTime;
            }
        }
        
        /*
        public void ResetRigidbodyRotation() {
            ai.selfRighting.RandomWait = 0f;
            ai.selfRighting.Timer = 0f;
            ai.selfRighting.FallenCount = 0;

            ai.my.VisObject.localPosition = new Vector3(ai.bob.visPosX, ai.bob.startY, ai.bob.visPosZ);
            ai.selfRighting.LockedRotation = true;

            if (!ai.looking.rotateRigidbody) {
                ai.my.Rigidbody.rotation = ai.movement.identityQuat;
                ai.my.Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            } else {
                ai.selfRighting.StartRotation.eulerAngles = new Vector3(ai.selfRighting.StartRotation.eulerAngles.x, ai.my.Rigidbody.rotation.eulerAngles.y, ai.selfRighting.StartRotation.eulerAngles.z);

                ai.my.Rigidbody.constraints = RigidbodyConstraints.None;
                ai.my.Rigidbody.rotation = ai.selfRighting.StartRotation;
                ai.my.Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
            ai.my.Rigidbody.angularDrag = ai.selfRighting.ResetDrag;
            ai.my.Rigidbody.drag = 0f;
        }

        public void ResetSelfRightingAI() {
            ai.StopDizzyParticles();
            ai.disposition.myState = EntityAI.EntityState.Idle;

            ai.grounded = true;
            ai.selfRighting.Fallen = false;
            ai.movement.inJump = true;

            ai.selfRighting.ConfusedParticles = new ParticleSystem[0];
            ai.selfRighting.particleVelocityThreshold = 1000000f;
            ai.selfRighting.FallImpactThreshold = 1000000f;
            ai.selfRighting.SleepTime = 0;

            ResetRigidbodyRotation();
        }*/
    }
}
