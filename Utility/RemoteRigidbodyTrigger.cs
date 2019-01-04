using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Dingodile {
    public delegate void OnRemoteRigidbodyTrigger(Rigidbody r);
    public class RemoteRigidbodyTrigger : RemoteTrigger {

        public event OnRemoteRigidbodyTrigger OnRigidbodyTriggerEnter;
        public event OnRemoteRigidbodyTrigger OnRigidbodyTriggerStay;
        public event OnRemoteRigidbodyTrigger OnRigidbodyTriggerExit;

        public HashSet<Rigidbody> Overlapping = new HashSet<Rigidbody>();
        private HashSet<Rigidbody> temp = new HashSet<Rigidbody>();

        protected HashSet<Collider> entering = new HashSet<Collider>();
        protected HashSet<Collider> staying = new HashSet<Collider>();
        protected HashSet<Collider> exiting = new HashSet<Collider>();

        protected override void OnTriggerEnter(Collider col) {
            if (!isSimulating || col.transform.root == transform.root) {
                return;
            }
            if(col.isTrigger && col.gameObject.layer == 2) {
                return;
            }

            if (exiting.Contains(col)) {
                exiting.Remove(col);
            } else if (!entering.Contains(col)) {
                entering.Add(col);
                if (!staying.Contains(col)) {
                    staying.Add(col);
                }
            }
        }
        protected override void OnTriggerStay(Collider col) {
            if (!isSimulating || col.transform.root == transform.root) {
                return;
            }
            if (col.isTrigger && col.gameObject.layer == 2) {
                return;
            }

            if (!staying.Contains(col)) {
                staying.Add(col);
            }
        }
        protected override void OnTriggerExit(Collider col) {
            if (!isSimulating || col.transform.root == transform.root) {
                return;
            }
            if (col.isTrigger && col.gameObject.layer == 2) {
                return;
            }

            if (entering.Contains(col)) {
                entering.Remove(col);
            } else if (!exiting.Contains(col)) {
                exiting.Add(col);
            }
            if (staying.Contains(col)) {
                staying.Remove(col);
            }
        }

        protected virtual void FixedUpdate() {
            if (!isSimulating || !SimPhysics) {
                entering.Clear();
                staying.Clear();
                exiting.Clear();
                Overlapping.Clear();
                return;
            }

            HashSet<Rigidbody> enteringBodies = new HashSet<Rigidbody>();
            foreach (Collider entry in entering) {
                if (!entry) {
                    continue;
                }
                Rigidbody r = entry.attachedRigidbody;
                if (!r || enteringBodies.Contains(r)) {
                    continue;
                }
                enteringBodies.Add(r);
                if (!Overlapping.Contains(r)) {
                    Overlapping.Add(r);
                }
                OnRigidbodyTriggerEnter?.Invoke(r);
            }
            HashSet<Rigidbody> stayingBodies = new HashSet<Rigidbody>();
            foreach (Collider entry in staying) {
                if (!entry) {
                    continue;
                }
                Rigidbody r = entry.attachedRigidbody;
                if (!r || stayingBodies.Contains(r)) {
                    continue;
                }
                stayingBodies.Add(r);
                if (!Overlapping.Contains(r)) {
                    Overlapping.Add(r);
                }
                OnRigidbodyTriggerStay?.Invoke(r);
            }
            HashSet<Rigidbody> exitingBodies = new HashSet<Rigidbody>();
            foreach (Collider entry in exiting) {
                if (!entry) {
                    continue;
                }
                Rigidbody r = entry.attachedRigidbody;
                if (!r || exitingBodies.Contains(r) || enteringBodies.Contains(r) || stayingBodies.Contains(r)) {
                    continue;
                }
                exitingBodies.Add(r);
                if (Overlapping.Contains(r)) {
                    Overlapping.Remove(r);
                }
                OnRigidbodyTriggerExit?.Invoke(r);
            }
            entering.Clear();
            staying.Clear();
            exiting.Clear();
        }

        public virtual void OnDisable() {
            foreach (Collider entry in exiting) {
                if (!entry) {
                    continue;
                }
                Rigidbody r = entry.attachedRigidbody;
                if (!r) {
                    continue;
                }
                OnRigidbodyTriggerExit?.Invoke(r);
            }
            Overlapping.Clear();
        }
    }
}
