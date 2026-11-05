using System;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Read-only role silhouette and animation. The existing view root and every gameplay collider remain untouched.</summary>
    public sealed class ArtActorView : MonoBehaviour
    {
        ArtResources art;
        Transform visual, leftLeg, rightLeg, weapon, torso, activeRing, targetRing, guard, stagger;
        Vector3 previousFeet, torsoOrigin;
        bool observed;
        float cueUntil, cueStarted,impactFlinch=1;
        double postureHoldUntil;
        string currentCue;
        HeroVisual hero;
        public string Archetype { get; private set; }
        public float LastVisualTime { get; private set; }
        public Transform VisualRoot => visual;
        public HeroVisual Hero => hero;
        public bool ActiveRingVisible => activeRing && activeRing.gameObject.activeSelf;
        public bool TargetRingVisible => targetRing && targetRing.gameObject.activeSelf;

        public static ArtActorView Attach(Transform existingView, ArtResources resources, ActorRole role, string enemyTemplate, string characterId = null)
        {
            var result = existingView.GetComponent<ArtActorView>();
            if (result) return result;
            result = existingView.gameObject.AddComponent<ArtActorView>();
            result.Build(resources, role, enemyTemplate, characterId); return result;
        }
        void Build(ArtResources resources, ActorRole role, string enemyTemplate, string characterId)
        {
            art = resources; Archetype = role == ActorRole.Enemy ? (enemyTemplate ?? "guard") : role.ToString().ToLowerInvariant();
            var old = GetComponent<Renderer>(); if (old) old.enabled = false;
            var oldCrest = transform.Find("Facing crest"); if (oldCrest) oldCrest.gameObject.SetActive(false);
            visual = new GameObject("P3 silhouette " + Archetype).transform; visual.SetParent(transform, false);
            // Existing world views are centered 0.8 above feet and scaled .75/.8/.75.
            // Compensate inside the display only; persistence continues to see the original root.
            Vector3 scale = transform.localScale;
            visual.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            visual.localPosition = new Vector3(0, -.8f / scale.y, 0);
            bool enemy = role == ActorRole.Enemy;
            Color cloth = enemy ? ArtResources.Red : role == ActorRole.Mage ? ArtResources.Blue : role == ActorRole.Rogue ? new Color(.14f,.46f,.63f) : ArtResources.Teal;
            string heroKey = enemy ? null : HeroVisual.ResourceKey(characterId);
            if (heroKey != null)
            {
                var prefab = Resources.Load<GameObject>(heroKey);
                if (prefab) hero = Instantiate(prefab, visual, false).GetComponent<HeroVisual>();
                else Debug.LogError("Missing baked hero Prefab: Resources/" + heroKey + ". Run HeroModelBuilder.Bake before build.");
            }
            if (hero) { Archetype = characterId; visual.name = "P6 hero " + characterId; }
            else if (Archetype == "mechanism") BuildMechanism();
            else BuildHumanoid(role, cloth, enemy);
            activeRing = Ring("Active character ring", .51f, .61f, ArtResources.Teal, .036f);
            targetRing = Ring("Target outline base", .64f, .70f, enemy ? ArtResources.Gold : Color.white, .045f);
            // Four upright corner strokes complement the ground outline without a stencil shader.
            var brackets = new ArtMeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                brackets.Box(new Vector3(side * .68f, .30f, 0), new Vector3(.045f,.40f,.045f), Quaternion.identity);
                brackets.Box(new Vector3(side * .57f,.12f,0), new Vector3(.22f,.045f,.045f), Quaternion.identity);
            }
            var bracketView = art.MeshObject("Target silhouette brackets", targetRing, brackets.Build("target brackets"), ArtResources.Gold, ArtTone.Glow);
            bracketView.transform.localPosition = Vector3.zero;
            guard = Ring("Guard arc", .56f, .64f, new Color(.40f,.82f,1f,.72f), .82f);
            guard.localRotation = Quaternion.Euler(72,0,0); guard.localPosition += Vector3.forward * .3f;
            stagger = Ring("Break halo", .26f, .34f, ArtResources.Gold, Archetype == "boss" ? 2.50f : 2.04f);
            activeRing.gameObject.SetActive(false); targetRing.gameObject.SetActive(false); guard.gameObject.SetActive(false); stagger.gameObject.SetActive(false);
        }
        Transform Part(string name, Vector3 pos, Vector3 size, Color color, Transform parent = null)
        {
            var obj = art.MeshObject(name, parent ? parent : visual, ArtMeshBuilder.BoxMesh(size), color, ArtTone.Actor);
            obj.transform.localPosition = pos; return obj.transform;
        }
        Transform Prism(string name, Vector3 pos, float bottom, float top, float height, Color color, int sides = 6, Transform parent = null)
        {
            var obj = art.MeshObject(name, parent ? parent : visual, ArtMeshBuilder.FrustumMesh(bottom,top,height,sides), color, ArtTone.Actor);
            obj.transform.localPosition = pos; return obj.transform;
        }
        Transform Ring(string name, float inner, float outer, Color color, float y)
        {
            var obj = art.MeshObject(name, visual, ArtMeshBuilder.Ring(inner,outer), color, ArtTone.Glow);
            obj.transform.localPosition = Vector3.up*y; return obj.transform;
        }
        void BuildHumanoid(ActorRole role, Color cloth, bool enemy)
        {
            bool mage = role == ActorRole.Mage, light = role == ActorRole.Rogue || Archetype == "scout";
            bool elite = Archetype == "elite" || Archetype == "boss";
            float width = light ? .42f : elite ? .82f : .65f;
            leftLeg = Part("Left boot", new Vector3(-.17f,.30f,0), new Vector3(.21f,.55f,.32f), ArtResources.Dark);
            rightLeg = Part("Right boot", new Vector3(.17f,.30f,0), new Vector3(.21f,.55f,.32f), ArtResources.Dark);
            torso = Prism(mage ? "Long robe" : light ? "Light tunic" : "Armored torso", new Vector3(0,.92f,0), mage ? .44f : width*.58f, mage ? .26f : width*.53f, mage ? 1.05f : .68f, cloth, 6);
            torsoOrigin = torso.localPosition;
            Part("Belt", new Vector3(0,.72f,.03f), new Vector3(width+.05f,.10f,.42f), ArtResources.Dark);
            Part("Belt clasp", new Vector3(0,.72f,.25f), new Vector3(.10f,.10f,.06f), ArtResources.Metal);
            Prism("Faceted face", new Vector3(0,1.48f,.035f), .22f,.22f,.34f, new Color(.64f,.70f,.67f));
            Part("Dark visor", new Vector3(0,1.52f,.23f), new Vector3(.30f,.06f,.035f), ArtResources.Dark);
            if (mage)
            {
                Prism("Wide hat brim", new Vector3(0,1.67f,0), .39f,.39f,.06f, cloth);
                Prism("Tall pointed hat", new Vector3(0,1.91f,0), .29f,.035f,.48f, cloth);
                weapon = Part("Staff shaft", new Vector3(.43f,1.03f,.14f), new Vector3(.09f,1.94f,.09f), ArtResources.Dark);
                Prism("Staff crystal", new Vector3(.43f,2.06f,.14f), .16f,.025f,.34f, ArtResources.Teal, 5);
                Prism("Staff collar", new Vector3(.43f,1.86f,.14f), .13f,.13f,.10f, ArtResources.Metal);
                Part("Spell book", new Vector3(-.42f,.95f,.23f), new Vector3(.26f,.34f,.12f), ArtResources.Metal).localRotation=Quaternion.Euler(0,0,-18);
            }
            else if (light)
            {
                Prism("Angular hood", new Vector3(0,1.61f,-.03f), .29f,.16f,.37f, cloth,5);
                Part("Visible hood opening", new Vector3(0,1.53f,.25f), new Vector3(.24f,.15f,.025f), ArtResources.Dark);
                Part("Short cloak", new Vector3(0,.99f,-.26f), new Vector3(.45f,.60f,.07f), cloth).localRotation=Quaternion.Euler(-15,0,0);
                weapon = Part("Right dagger", new Vector3(.36f,.86f,.32f), new Vector3(.08f,.62f,.10f), ArtResources.Metal); weapon.localRotation=Quaternion.Euler(36,0,-12);
                Part("Left dagger", new Vector3(-.36f,.83f,.25f), new Vector3(.08f,.54f,.10f), ArtResources.Metal).localRotation=Quaternion.Euler(32,0,12);
                Part("Scarf", new Vector3(0,1.31f,.02f), new Vector3(.39f,.12f,.36f), enemy ? ArtResources.Red : ArtResources.Teal);
            }
            else
            {
                Part("Left broad shoulder", new Vector3(-width*.61f,1.25f,0), new Vector3(.31f,.29f,.44f), ArtResources.Metal);
                Part("Right broad shoulder", new Vector3(width*.61f,1.25f,0), new Vector3(.31f,.29f,.44f), ArtResources.Metal);
                Prism("Helmet", new Vector3(0,1.67f,0), .27f,.23f,.22f, ArtResources.Metal);
                bool greatsword = role.ToString() == "Paladin";
                weapon = Part(greatsword ? "Oathlight greatsword" : elite ? "Heavy axe handle" : "Sword blade", new Vector3(.54f,greatsword?1.04f:.87f,.35f), new Vector3(greatsword?.22f:.11f,greatsword?1.62f:1.03f,.10f), ArtResources.Metal);
                Part("Weapon grip", new Vector3(.54f,.43f,.35f), new Vector3(.12f,.25f,.12f), ArtResources.Dark);
                Part("Weapon crossguard", new Vector3(.54f,.60f,.35f), new Vector3(.38f,.08f,.15f), ArtResources.Metal);
                if(!greatsword)
                {
                    Part("Front shield", new Vector3(-.53f,.98f,.30f), new Vector3(.42f,.70f,.13f), cloth);
                    Part("Shield boss", new Vector3(-.53f,.98f,.39f), new Vector3(.15f,.23f,.05f), ArtResources.Metal);
                }
                if (elite)
                {
                    Part("Broad axe head", new Vector3(.54f,1.31f,.35f), new Vector3(.53f,.29f,.16f), ArtResources.Metal);
                    Prism("Left crown horn",new Vector3(-.27f,1.89f,0),.11f,0,.46f,ArtResources.Red,4);
                    Prism("Right crown horn",new Vector3(.27f,1.89f,0),.11f,0,.46f,ArtResources.Red,4);
                }
            }
            if (Archetype == "boss")
            {
                Part("Boss back standard pole",new Vector3(0,1.47f,-.29f),new Vector3(.09f,1.90f,.09f),ArtResources.Dark);
                Part("Boss torn standard",new Vector3(.26f,2.16f,-.31f),new Vector3(.54f,.60f,.045f),ArtResources.Red);
            }
        }
        void BuildMechanism()
        {
            Prism("Turret hexagonal base",new Vector3(0,.20f,0),.50f,.42f,.40f,ArtResources.Dark);
            for (int i=0;i<3;i++)
            {
                float angle=i*120*Mathf.Deg2Rad;
                Part("Turret stabilizer",new Vector3(Mathf.Cos(angle)*.38f,.16f,Mathf.Sin(angle)*.38f),new Vector3(.22f,.30f,.65f),ArtResources.Metal).localRotation=Quaternion.Euler(0,-i*120+90,0);
            }
            torso=Prism("Remote rune projector",new Vector3(0,.90f,0),.34f,.25f,1.10f,ArtResources.Red);
            torsoOrigin=torso.localPosition;
            weapon=Part("Projector muzzle",new Vector3(0,1.06f,.43f),new Vector3(.31f,.25f,.62f),ArtResources.Dark);
            Prism("Projector focus crystal",new Vector3(0,1.66f,0),.23f,0,.43f,ArtResources.Danger,5);
        }
        public void Apply(ActorState actor, bool active, bool target, bool focus, bool frozen, float simulationTime)
        {
            if (actor == null) return;
            activeRing.gameObject.SetActive(active && actor.Alive);
            targetRing.gameObject.SetActive(target && actor.Alive);
            if (frozen && observed) return;
            if (PostureHeld && observed) return;
            LastVisualTime=simulationTime;
            Vector3 feet=BattleWorld.Position(actor); bool moving=observed&&(feet-previousFeet).sqrMagnitude>.0000001f;
            previousFeet=feet; observed=true;
            if (hero)
            {
                hero.Apply(actor.Alive, moving, actor.guardRemaining>0, currentCue, simulationTime-cueStarted, simulationTime, impactFlinch);
                guard.gameObject.SetActive(actor.Alive && (actor.guardRemaining>0 || actor.statuses.Exists(s=>s.id=="shield"&&s.remaining>0)));
                stagger.gameObject.SetActive(actor.Alive && actor.staggerRemaining>0);
                if(stagger)stagger.localRotation=Quaternion.Euler(0,simulationTime*100,0);
                return;
            }
            float phase=simulationTime*10, stride=moving?Mathf.Sin(phase)*22:0;
            if(leftLeg)leftLeg.localRotation=Quaternion.Euler(stride,0,0);
            if(rightLeg)rightLeg.localRotation=Quaternion.Euler(-stride,0,0);
            if(torso)
            {
                float flinch = currentCue=="hit" && simulationTime<cueUntil ? Mathf.Cos(Mathf.Clamp01((simulationTime-cueStarted)/.22f)*Mathf.PI*.5f) : 0;
                torso.localPosition=torsoOrigin+Vector3.up*(moving?Mathf.Abs(Mathf.Sin(phase))*.035f:0)-Vector3.forward*(flinch*.09f*impactFlinch);
                torso.localRotation=Quaternion.Euler(-flinch*8*impactFlinch,0,0); // Cosmetic only; never extends a gameplay interrupt.
            }
            guard.gameObject.SetActive(actor.guardRemaining>0 || actor.statuses.Exists(s=>s.id=="shield"&&s.remaining>0));
            stagger.gameObject.SetActive(actor.staggerRemaining>0);
            if(stagger)stagger.localRotation=Quaternion.Euler(0,simulationTime*100,0);
            if(weapon)
            {
                float attack=simulationTime<cueUntil&&currentCue=="attack"?Mathf.Sin(Mathf.Clamp01((simulationTime-cueStarted)/.3f)*Mathf.PI)*-46:0;
                weapon.localRotation=Quaternion.Euler(actor.guardRemaining>0?-20:attack,0,0);
            }
        }
        public void Cue(string cue,float simulationTime,float flinch=1){impactFlinch=flinch;currentCue=cue;cueStarted=simulationTime;cueUntil=simulationTime+.3f;}
        public bool PostureHeld => Time.realtimeSinceStartupAsDouble < postureHoldUntil;
        public void HoldPosture(float seconds){postureHoldUntil=Math.Max(postureHoldUntil,Time.realtimeSinceStartupAsDouble+Mathf.Clamp(seconds,0,.1f));}
        public void ResetObservation(){observed=false;cueUntil=0;currentCue=null;postureHoldUntil=0;}
    }
}
