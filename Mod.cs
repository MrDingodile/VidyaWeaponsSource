using Modding;
using Modding.Blocks;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace Dingodile
{
	public class VidyaMod : ModEntryPoint
	{
        public static AnimationCurve curve;
        public static bool hasLoaded = false;

        public override void OnLoad() {
            hasLoaded = true;
            OnModLoad();
            SceneManager.sceneLoaded += OnSceneLoad;
        } 
        
        public static void OnModLoad() {
            curve = new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f));
            PortalingMaster.OnModLoad();
            PortalDevice.OnModLoad();
            Portal.LoadConfig();
        }

        public static void OnSceneLoad(Scene scene, LoadSceneMode mode) {
            if (!hasLoaded) {
                return;
            }
            if(mode == LoadSceneMode.Single) {
                if (!AddPiece.IsMenuScene(scene.name)) {
                    PortalingMaster.OnModLoad();
                    PortalDevice.RebuildForNewLevel();
                }
            }
        }

        public static void MatchTransform(Transform target, Transform source) {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }
        
        public static LayerMask AddToLayerMask(LayerMask mask, int layer) {
            return mask | (1 << layer);//add
        }

        public static LayerMask RemoveFromLayerMask(LayerMask mask, int layer) {
            return mask & ~(1 << layer);//remove layer
        }

        public static bool LayerMaskContains(LayerMask mask, int layer) {
            return mask == (mask | (1 << layer));
        }

        public static void PlayAudio(AudioSource audioSource, AudioClip clip, float volume, float min = 1f, float max = 1f) {
            if(audioSource == null || clip == null) {
                return;
            }
            audioSource.Stop();
            float ts = Time.timeScale;
            if (ts > 1f) {
                ts = 1f;
            }
            audioSource.volume = 0.9f * volume;
            audioSource.pitch = Random.Range(min, max) * curve.Evaluate(ts);
            audioSource.clip = clip;
            audioSource.Play();
        }

        public static bool IsVirtualTrigger(Transform t) {
            return t.name == "InsigniaObj" && t.transform.parent.name == "Trigger";
        }
    }
}
