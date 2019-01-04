using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Modding;


namespace Dingodile {
    public class MainCamTracker : MonoBehaviour {

        public static MainCamTracker Instance;
        public static Vector3 lastPos = Vector3.zero;

        public void Awake() {
            Instance = this;
            lastPos = transform.position;
        }

        public void OnPreRender() {
            UpdatePos();
        }

        public static void UpdatePos() {
            lastPos = Instance.transform.position;
        }
    }
}
