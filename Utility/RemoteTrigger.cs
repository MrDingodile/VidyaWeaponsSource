using UnityEngine;
using System.Collections;

namespace Dingodile {
    public delegate void OnRemoteTrigger(Collider col);
    public class RemoteTrigger : SimBehaviour {

        public event OnRemoteTrigger OnRemoteTriggerEnter;
        public event OnRemoteTrigger OnRemoteTriggerStay;
        public event OnRemoteTrigger OnRemoteTriggerExit;

        protected virtual void OnTriggerEnter(Collider col) {
            if (isSimulating)
                if (OnRemoteTriggerEnter != null)
                    OnRemoteTriggerEnter(col);
        }
        protected virtual void OnTriggerStay(Collider col) {
            if (isSimulating)
                if (OnRemoteTriggerStay != null)
                    OnRemoteTriggerStay(col);
        }
        protected virtual void OnTriggerExit(Collider col) {
            if (isSimulating)
                if (OnRemoteTriggerExit != null)
                    OnRemoteTriggerExit(col);
        }
    }
}
