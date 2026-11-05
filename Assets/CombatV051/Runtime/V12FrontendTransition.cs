using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    // One reusable, non-interactive outgoing drawing. Snapshots happen only on
    // navigation, never on HUD refresh or each frame. No RT, cloned controls or
    // delayed profile transactions are used. Each slot owns exactly one Mesh.
    public sealed class V12FrontendTransition:MonoBehaviour
    {
        sealed class Drawing
        {public RectTransform rect;public CanvasRenderer renderer;public Mesh mesh;public Font font;}
        readonly List<Drawing> drawings=new List<Drawing>();
        CanvasGroup live,old;RectTransform oldRoot;Coroutine fading;string page;bool pending;
        public bool Animating=>fading!=null;
        public int SnapshotCount {get;private set;}
        public int PooledDrawingCount=>drawings.Count;
        public float LiveAlpha=>live?live.alpha:1;
        public GameObject SnapshotRoot=>oldRoot?oldRoot.gameObject:null;
        void Awake(){live=gameObject.AddComponent<CanvasGroup>();Font.textureRebuilt+=FontRebuilt;}
        public void BeforeRebind(string key,bool reduced)
        {
            if(reduced){Cancel();page=key;return;}
            if(page==key)return;
            bool hadPage=page!=null;Cancel();page=key;
            if(!hadPage||!isActiveAndEnabled)return;
            Capture();pending=true;
        }
        public void AfterRebind()
        {if(!pending)return;pending=false;live.alpha=0;old.alpha=1;fading=StartCoroutine(Fade());}
        void Capture()
        {
            Canvas.ForceUpdateCanvases();
            if(!oldRoot)
            {
                var obj=new GameObject("V12 outgoing page drawing",typeof(RectTransform),typeof(CanvasGroup));
                oldRoot=obj.GetComponent<RectTransform>();oldRoot.SetParent(transform.parent,false);
                oldRoot.anchorMin=Vector2.zero;oldRoot.anchorMax=Vector2.one;oldRoot.offsetMin=oldRoot.offsetMax=Vector2.zero;
                old=obj.GetComponent<CanvasGroup>();old.blocksRaycasts=false;old.interactable=false;
            }
            oldRoot.gameObject.SetActive(true);oldRoot.SetSiblingIndex(transform.GetSiblingIndex());
            int used=0;
            foreach(var graphic in GetComponentsInChildren<Graphic>())
            {
                if(!graphic.isActiveAndEnabled||graphic.canvasRenderer.cull)continue;
                var source=graphic.canvasRenderer.GetMesh();if(!source||source.vertexCount==0)continue;
                Drawing slot;
                if(used==drawings.Count)
                {
                    var obj=new GameObject("Outgoing drawing "+used,typeof(RectTransform),typeof(CanvasRenderer));
                    obj.transform.SetParent(oldRoot,false);
                    slot=new Drawing{rect=obj.GetComponent<RectTransform>(),renderer=obj.GetComponent<CanvasRenderer>(),mesh=new Mesh{name="V12 page snapshot drawing"}};drawings.Add(slot);
                }
                else slot=drawings[used];
                used++;slot.rect.gameObject.SetActive(true);
                var r=graphic.rectTransform;slot.rect.anchorMin=slot.rect.anchorMax=new Vector2(.5f,.5f);slot.rect.pivot=r.pivot;slot.rect.sizeDelta=r.rect.size;
                slot.rect.position=r.position;slot.rect.rotation=r.rotation;
                Vector3 scale=oldRoot.lossyScale;slot.rect.localScale=new Vector3(r.lossyScale.x/scale.x,r.lossyScale.y/scale.y,1);
                slot.mesh.Clear();slot.mesh.vertices=source.vertices;slot.mesh.colors32=source.colors32;slot.mesh.uv=source.uv;slot.mesh.uv2=source.uv2;slot.mesh.triangles=source.triangles;
                slot.renderer.SetMesh(slot.mesh);slot.renderer.materialCount=1;slot.renderer.SetMaterial(graphic.materialForRendering,0);slot.renderer.SetTexture(graphic.mainTexture);slot.renderer.SetColor(graphic.canvasRenderer.GetColor());
                var mask=graphic.GetComponentInParent<RectMask2D>();if(mask&&mask.isActiveAndEnabled){slot.renderer.EnableRectClipping(mask.canvasRect);slot.renderer.clippingSoftness=mask.softness;}else slot.renderer.DisableRectClipping();
                slot.font=graphic is Text text?text.font:null;
            }
            for(int i=used;i<drawings.Count;i++)drawings[i].rect.gameObject.SetActive(false);
            SnapshotCount++;
        }
        IEnumerator Fade()
        {
            for(float elapsed=0;elapsed<.12f;elapsed+=Time.unscaledDeltaTime)
            {float t=Mathf.SmoothStep(0,1,elapsed/.12f);live.alpha=t;old.alpha=1-t;yield return null;}
            live.alpha=1;old.alpha=0;oldRoot.gameObject.SetActive(false);fading=null;
        }
        void Cancel()
        {if(fading!=null)StopCoroutine(fading);fading=null;pending=false;if(live&&live.alpha!=1)live.alpha=1;if(oldRoot&&oldRoot.gameObject.activeSelf)oldRoot.gameObject.SetActive(false);}
        // Dynamic-font atlas repacking invalidates old text UVs; discard only
        // those outgoing glyphs instead of showing unrelated characters.
        void FontRebuilt(Font font){foreach(var slot in drawings)if(slot.font==font&&slot.renderer)slot.renderer.Clear();}
        void OnDisable(){Cancel();page=null;}
        void OnDestroy(){Font.textureRebuilt-=FontRebuilt;foreach(var slot in drawings)if(slot.mesh)Destroy(slot.mesh);if(oldRoot)Destroy(oldRoot.gameObject);}
    }
}
