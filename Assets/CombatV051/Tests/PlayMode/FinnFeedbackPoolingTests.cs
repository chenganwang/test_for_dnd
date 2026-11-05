using System.Collections;
using System.Linq;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Presentation-only saturation fixture. MicroHeal receives no combat state and
    // this test never simulates healing, damage, a hit, or a natural Player result.
    public sealed class FinnFeedbackPoolingTests
    {
        static int[] MaterialIds(GameObject owner)=>owner.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.GetInstanceID()).Distinct().OrderBy(id=>id).ToArray();
        static int ActiveContacts(GameObject owner)=>owner.transform.Cast<Transform>().Count(t=>t.gameObject.activeSelf);

        [UnityTest] public IEnumerator OneHundredMicroHealsReuseWarmedObjectsMaterialsAndAudioAndFreezeTheirLifetime()
        {
            float originalScale=Time.timeScale;var owner=new GameObject("Finn micro-heal presentation pool fixture");
            try
            {
                var feedback=owner.AddComponent<BattleFeedback>();
                yield return null; // Complete deferred primitive Collider disposal before measuring.
                int objects=owner.GetComponentsInChildren<Transform>(true).Length;
                int[] materials=MaterialIds(owner);
                int globalMaterials=Resources.FindObjectsOfTypeAll<Material>().Length;
                int globalAudio=Resources.FindObjectsOfTypeAll<AudioSource>().Length;
                Assert.That(feedback.CreatedEffectCount,Is.EqualTo(24));
                Time.timeScale=0;
                for(int i=0;i<100;i++)feedback.MicroHeal(new Vector3(i%3,0,i%5));
                Assert.That(feedback.CreatedEffectCount,Is.EqualTo(24));
                Assert.That(owner.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
                Assert.That(MaterialIds(owner),Is.EqualTo(materials),"The original owned shared materials must be reused, not cloned.");
                Assert.That(Resources.FindObjectsOfTypeAll<Material>().Length,Is.EqualTo(globalMaterials),"No new Material during the synchronous 100-call burst.");
                Assert.That(Resources.FindObjectsOfTypeAll<AudioSource>().Length,Is.EqualTo(globalAudio),"Micro-heal must not create an AudioSource.");
                Assert.That(owner.GetComponentsInChildren<AudioSource>(true),Is.Empty);
                Assert.That(ActiveContacts(owner),Is.EqualTo(24),"Saturation reuses the oldest contacts rather than growing the pool.");
                var line=owner.GetComponentsInChildren<LineRenderer>(false)[0];var position=line.GetPosition(1);
                // More real time than MicroHeal's .25s lifetime must not age world effects in freeze.
                yield return new WaitForSecondsRealtime(.35f);
                Assert.That(Time.timeScale,Is.Zero);Assert.That(ActiveContacts(owner),Is.EqualTo(24));
                Assert.That(line.GetPosition(1),Is.EqualTo(position));Assert.That(feedback.CreatedEffectCount,Is.EqualTo(24));
                Assert.That(MaterialIds(owner),Is.EqualTo(materials));
                Time.timeScale=1;var timer=System.Diagnostics.Stopwatch.StartNew();
                while(ActiveContacts(owner)>0&&timer.Elapsed.TotalSeconds<2)yield return null;
                Assert.That(ActiveContacts(owner),Is.Zero,"The same effects should return to the pool after simulation resumes.");
                Assert.That(feedback.CreatedEffectCount,Is.EqualTo(24));
                Assert.That(owner.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
                Assert.That(MaterialIds(owner),Is.EqualTo(materials));Assert.That(owner.GetComponentsInChildren<AudioSource>(true),Is.Empty);
            }
            finally{Time.timeScale=originalScale;Object.Destroy(owner);}
            yield return null;
        }
    }
}
