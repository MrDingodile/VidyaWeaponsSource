using System.Collections.Generic;
using UnityEngine;
using Modding;
using Modding.Blocks;
using Modding.Levels;

namespace Dingodile {
    class Polymorpher : MonoBehaviour {

        public static Poultryizer.ChickenReferences Chicken;
        public static MessageType polymorphMessageType;

        public Poultryizer poultryizer;
        public AudioSource audioSource;
        public AudioClip clip;

        public void Setup(Poultryizer p, Poultryizer.ChickenReferences c) {
            poultryizer = p;
            Chicken = c;
            ModAudioClip a = ModResource.GetAudioClip("polymorph");
            if (a.Available) {
                audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                audioSource.volume = 0.9f;
                audioSource.spatialBlend = 0.99f;
                audioSource.reverbZoneMix = 1;
                audioSource.clip = clip = a;
            }
        }

        public static void SetupNetworking() {
            polymorphMessageType = ModNetworking.CreateMessageType(DataType.Block, DataType.Entity, DataType.Boolean);
            ModNetworking.Callbacks[polymorphMessageType] += ProcessRemotePolymorphing;
        }
        
        protected void SendPolymorph(MonoBehaviour m, bool heated) {
            if (!StatMaster.isLocalSim) {
                LevelEntity le = m.GetComponent<LevelEntity>();
                if (le == null) {
                    return;
                }
                Entity e = Entity.From(le);
                Message targetMessage = polymorphMessageType.CreateMessage(Block.From(poultryizer), e, heated);
                ModNetworking.SendToAll(targetMessage);
            }
        }

        public static void ProcessRemotePolymorphing(Message m) {
            Block b = (Block)m.GetData(0);
            Entity e = (Entity)m.GetData(1);
            bool heated = (bool)m.GetData(2);

            Poultryizer script = b.GameObject.GetComponent<Poultryizer>();
            script.polymorpher.ProcessRemotePolymorphing(e, heated);
        }

        public void ProcessRemotePolymorphing(Entity e, bool heated) {
            poultryizer.targetTransform = e.GameObject.transform;
            poultryizer.target = Poultryizer.GetAIScript(e.GameObject);
            if (poultryizer.target) {
                if (heated) {
                    ReplaceWithGrilledChicken(poultryizer.target);
                } else {
                    ReplaceWithChicken(poultryizer.target);
                }
                poultryizer.ResetTarget();
            }
        }

        public void ReplaceWithChickenHost(MonoBehaviour m, bool heated) {
            SendPolymorph(m, heated);
            if (heated) {
                ReplaceWithGrilledChicken(m);
            } else {
                ReplaceWithChicken(m);
            }
        }

        public void ReplaceWithGrilledChicken(MonoBehaviour m) {
            /*if (m.name.Contains("(Burning)")) {
                return;
            }*/
            
            //GameObject g = ReplaceWithChicken(m);
            //g.name += "(Burning)";

            if (m is EntityAI) {
                EntityAI ai = m as EntityAI;

                ChangeDeathController(ai);
                KillingHandler killer = ai.my.killingHandler;
                killer.my.GibPrefab = killer.my.corpseDust;
                ai.my.killingHandler.KillMe(false);

            } else {
                GameObject featherBurst = Instantiate(Chicken.dustCorpse, m.transform.position, Quaternion.identity, m.transform.parent) as GameObject;
                Destroy(m.gameObject);
            }
            //g.GetComponent<FireTag>().Ignite();
        }

        public GameObject ReplaceWithChicken(MonoBehaviour m) {
            if (m.name.Contains("Chicken")) {
                return m.gameObject;
            }
            GameObject g = m.gameObject;
            m.gameObject.name = "Polymorphed Chicken";
            //Debug.Log(Chicken + ", " + m);
            GameObject featherBurst = Instantiate(Chicken.dustCorpse, m.transform.position, Quaternion.identity, m.transform.parent) as GameObject;
            
            VidyaMod.PlayAudio(audioSource, clip, 1f, 0.95f, 1.1f);

            if (m is EntityAI) {
                EntityAI ai = m as EntityAI;

                SetVisual(ai);
                ChangePosing(ai);
                ChangeDeathController(ai);
                ChangeBobbing(ai);
                ChangeFaction(ai);
                ChangeBehaviour(ai);
                ChangeSounds(ai);

                if (StatMaster.isHosting || StatMaster.isLocalSim) {
                    ChangeFireControl(ai);
                    
                    ToChickenColliderAnimator collideranim = ai.gameObject.AddComponent<ToChickenColliderAnimator>();
                    collideranim.Setup(ai, Chicken.AI.my.Collider as SphereCollider);
                }
            } else if (m is SimpleBirdAI) {
                g = Spawn(Chicken.prefab, m.gameObject);
                Destroy(m.gameObject);
            } else {
                g = Spawn(Chicken.prefab, m as EnemyAISimple);
                Destroy(m.gameObject);
            }
            return g;
        }

        public virtual void SetVisual(EntityAI ai) {
            MeshFilter filter = ai.my.killingHandler.my.Poser.meshFilter;
            MeshRenderer render = filter.GetComponent<MeshRenderer>();
            Transform vis = filter.transform;
            Transform visPivot = vis.parent;

            ai.health = Chicken.AI.health;
            
            filter.mesh = Chicken.mesh;
            render.sharedMaterial = Chicken.mat;
            if (visPivot.parent != ai.transform) {
                visPivot.parent.localScale = Vector3.one;
            }
            VidyaMod.MatchTransform(visPivot, Chicken.visPivot);
            VidyaMod.MatchTransform(vis, Chicken.vis);

            Transform remove = visPivot.FindChild("ArmCollider");
            if (remove) {
                remove.gameObject.SetActive(false);
            }
            remove = visPivot.FindChild("AttackCollider");
            if (remove) {
                remove.gameObject.SetActive(false);
            }
        }

        public virtual void ChangePosing(EntityAI ai) {
            SetPoseForAI poser = ai.my.killingHandler.my.Poser;

            poser.StandingPoses =
            poser.FleeingPoses =
            poser.CowardPoses =
            poser.ChargingPoses =
            poser.AttackingPoses =
            poser.DeathPoses = new Mesh[] { Chicken.mesh };
        }

        public virtual void ChangeDeathController(EntityAI ai) {
            KillingHandler killer = ai.my.killingHandler;

            killer.my.Poser = null;
            killer.UseGibPrefab = true;
            killer.my.GibPrefab = Chicken.bloodCorpse;
            killer.my.corpseDust = Chicken.dustCorpse;
            killer.my.BloodyTexture = new Texture2D[0];
        }

        public virtual void ChangeBobbing(EntityAI ai) {
            EntityAI.Bob target = ai.bob;
            EntityAI.Bob source = Chicken.AI.bob;
            Transform sourceVis = Chicken.AI.my.VisObject;

            target.Able = source.Able;
            target.Amount = source.Amount;
            target.lerpSpeed = source.lerpSpeed;
            target.Rate = source.Rate;
            target.startValue = source.startValue;
            target.startY = sourceVis.localPosition.y;
            target.visPosX = sourceVis.localPosition.x;
            target.visPosZ = sourceVis.localPosition.z;
        }

        public virtual void ChangeFaction(EntityAI ai) {
            EntityAI.FactionSystem target = ai.factionSystem;
            EntityAI.FactionSystem source = Chicken.AI.factionSystem;

            if (ai.faction.Infantry.Contains(ai)) {
                ai.faction.Infantry.Remove(ai);
            }
            foreach (var enemy in ai.TargetedBy) {
                if (enemy.TargetBlock.isAI) {
                    enemy.TargetBlock.Null();
                }
            }
            ai.faction = new Faction(null);
            Faction faction = ai.faction;

            target.faction = source.faction;
            target.primaryTargetFaction = source.primaryTargetFaction;
            target.AttackOnlyTypeOf = FactionsController.AttackOnlyEnum.Machine;
            target.Discrimination = FactionsController.DiscriminantEnum.Machine;
            target.Setup(ai);

            if (!FactionsController.Factions.ContainsKey(faction.Name)) {
                FactionsController.Factions.Add(faction.Name, faction);
            } else {
                ai.faction = FactionsController.Factions[faction.Name];
            }
            //FactionsController.ChangeFaction(ai, FactionsController.Factions[faction.Name]);
        }

        public virtual void ChangeBehaviour(EntityAI ai) {
            EntityAI.Disposition target = ai.disposition;
            EntityAI.Disposition source = Chicken.AI.disposition;

            target.canAttack = false;
            //Destroy(ai.gameObject.GetComponent<AttackScript>());
            target.AvoidFire = source.AvoidFire;
            target.behaviours = source.behaviours;
            target.behavioursArray = target.behaviours.ToArray();
            target.currentBehaviour = target.behaviours[0];
        }

        public virtual void ChangeSounds(EntityAI ai) {
            RandomSoundController target = ai.my.killingHandler.my.SoundController;
            RandomSoundController source = Chicken.sounds;

            target.audioclips = source.audioclips;
            target.audioclips2 = source.audioclips2;
            target.audioclips3 = source.audioclips3;
        }

        public virtual void ChangeFireControl(EntityAI ai) {
            FireController fireControl = ai.my.killingHandler.my.fireControl;

            VidyaMod.MatchTransform(fireControl.transform, Chicken.fireControl.transform);
            SphereCollider fireCollider = fireControl.myCollider as SphereCollider;
            FireController chickenFireControl = Chicken.fireControl;
            SphereCollider chickenFireCollider = chickenFireControl.myCollider as SphereCollider;
            fireCollider.center = chickenFireCollider.center;
            fireCollider.radius = chickenFireCollider.radius;
            fireControl.destroyTimer = chickenFireControl.destroyTimer;
            fireControl.randomAmount = chickenFireControl.randomAmount;
            fireControl.onFireDuration = chickenFireControl.onFireDuration;
            fireControl.fullFireDuration = fireControl.destroyTimer + fireControl.onFireDuration;
        }

        /*
        public void ChangeCollider(EntityAI ai) {
            float pct = 1f;

            SphereCollider targetCollider = Chicken.AI.my.Collider as SphereCollider;
            ai.selfRighting.enabled = false;
            Collider target = ai.my.Collider;
            bool spherical = false;
            SphereCollider sphere = null;
            CapsuleCollider capsule = null;

            if (target is SphereCollider) {
                spherical = true;
                sphere = target as SphereCollider;
            } else if (target is CapsuleCollider) {
                spherical = false;
                capsule = target as CapsuleCollider;
            } else {
                return;
            }

            Transform t = target.transform;
            Vector3 startPosition = Vector3.zero;
            Quaternion startRotation = Quaternion.identity;
            Vector3 startScale = Vector3.one;
            Vector3 startCenter = Vector3.zero;
            float startRadius = 1f;
            float startHeight = 1f;
            bool isChildCollider = t != ai.transform;
            float maxAxisScale = 1f;

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
                capsule.height = Mathf.Lerp(startHeight, 0f, pct);

                if (isChildCollider) {
                    capsule.transform.localPosition = Vector3.Lerp(startPosition, targetCollider.transform.localPosition, pct);
                    capsule.transform.localRotation = Quaternion.Slerp(startRotation, targetCollider.transform.localRotation, pct);
                    capsule.transform.localScale = Vector3.Lerp(startScale, targetCollider.transform.localScale, pct);
                }
            }
        }*/

        public virtual GameObject Spawn(GameObject prefab, EnemyAISimple old) {
            GameObject entity = Instantiate(prefab, old.transform.position, old.transform.rotation, old.transform.parent) as GameObject;
            entity.name = "Polymorphed Chicken";
            EntityAI ai = entity.GetComponent<EntityAI>();
            ai.my.basicInfo = entity.GetComponent<BasicInfo>();
            ai.my.Rigidbody = ai.my.basicInfo.Rigidbody;

            SphereCollider col = ai.my.Collider as SphereCollider;
            if (old.myCollider is SphereCollider) {
                col.radius = (old.myCollider as SphereCollider).radius;
            } else if (old.myCollider is CapsuleCollider) {
                col.radius = (old.myCollider as CapsuleCollider).radius;
            }

            ToChickenColliderAnimator collideranim = entity.AddComponent<ToChickenColliderAnimator>();
            collideranim.Setup(ai, Chicken.AI.my.Collider as SphereCollider);

            entity.SetActive(true);
            return entity;
        }

        public virtual GameObject Spawn(GameObject prefab, GameObject old) {
            GameObject entity = Instantiate(prefab, old.transform.position, old.transform.rotation, old.transform.parent) as GameObject;
            entity.name = "Polymorphed Chicken";
            EntityAI ai = entity.GetComponent<EntityAI>();
            ai.my.basicInfo = entity.GetComponent<BasicInfo>();
            ai.my.Rigidbody = ai.my.basicInfo.Rigidbody;
            
            entity.SetActive(true);
            return entity;
        }
    }
}
