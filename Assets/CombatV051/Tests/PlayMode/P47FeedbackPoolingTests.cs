using System.Collections;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    public sealed class P47FeedbackPoolingTests
    {
        [UnityTest]
        public IEnumerator FirstContactsAndSaturatedBossBurstReuseTheWarmPool()
        {
            var owner=new GameObject("P47 contact pooling fixture");
            try
            {
                var feedback=owner.AddComponent<BattleFeedback>();
                int objects=owner.GetComponentsInChildren<Transform>(true).Length;
                for(int i=0;i<40;i++)
                    feedback.Impact(Vector3.zero,new Vector3(i,0,1),new CombatResult{success=true,damage=3,critical=i%4==0},true,2,i==39?"boss-kill":"hit");
                Assert.That(feedback.CreatedEffectCount,Is.EqualTo(24));
                Assert.That(owner.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
                var burst=owner.transform.Find("Boss death burst");
                Assert.That(burst,Is.Not.Null);
                Assert.That(burst.GetComponent<LineRenderer>().positionCount,Is.EqualTo(25));
                Assert.That(burst.gameObject.activeSelf,Is.True);
            }
            finally { Object.Destroy(owner); }
            yield return null;
        }
        [UnityTest]
        public IEnumerator FirstMajorCueTypesAlreadyOwnTheirMeshesAndFreezeKeepsTheirLifetime()
        {
            var owner=new GameObject("P47 art pooling fixture");
            try
            {
                var palette=owner.AddComponent<ArtResources>();var cues=owner.AddComponent<ArtCueEffects>();cues.Initialize(palette);
                int objects=owner.GetComponentsInChildren<Transform>(true).Length;
                foreach(var kind in new[]{"hit","parry","critical","break","perfect-dodge","spell"})cues.Play(kind,Vector3.zero,10);
                Assert.That(cues.ActiveCount,Is.EqualTo(6));
                Assert.That(owner.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
                cues.Refresh(100,true);Assert.That(cues.ActiveCount,Is.EqualTo(6));
                cues.Refresh(11,false);Assert.That(cues.ActiveCount,Is.Zero);
            }
            finally { Object.Destroy(owner); }
            yield return null;
        }
    }
}
