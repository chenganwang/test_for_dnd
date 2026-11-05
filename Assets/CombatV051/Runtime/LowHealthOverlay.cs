using UnityEngine;
using UnityEngine.UI;
namespace CombatV051.Runtime
{
    // Four translucent edge strips. Center pixels are untouched and the layer never consumes input.
    public sealed class HealthEdgeGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;float x=Mathf.Min(r.width*.075f,100),y=Mathf.Min(r.height*.10f,80);Color clear=color;clear.a=0;
            Quad(vh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMin+x,r.yMin),new Vector2(r.xMin+x,r.yMax),new Vector2(r.xMin,r.yMax),color,clear,clear,color);
            Quad(vh,new Vector2(r.xMax-x,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax-x,r.yMax),clear,color,color,clear);
            Quad(vh,new Vector2(r.xMin+x,r.yMin),new Vector2(r.xMax-x,r.yMin),new Vector2(r.xMax-x,r.yMin+y),new Vector2(r.xMin+x,r.yMin+y),color,color,clear,clear);
            Quad(vh,new Vector2(r.xMin+x,r.yMax-y),new Vector2(r.xMax-x,r.yMax-y),new Vector2(r.xMax-x,r.yMax),new Vector2(r.xMin+x,r.yMax),clear,clear,color,color);
        }
        static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color ca,Color cb,Color cc,Color cd)
        {int n=vh.currentVertCount;vh.AddVert(a,ca,Vector2.zero);vh.AddVert(b,cb,Vector2.zero);vh.AddVert(c,cc,Vector2.zero);vh.AddVert(d,cd,Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);}
    }
    public sealed class LowHealthOverlay : MonoBehaviour
    {
        BattleWorld world;HealthEdgeGraphic graphic;
        public int Severity {get;private set;}
        public float Alpha=>graphic?graphic.color.a:0;
        public static float Pulse=>.5f+.5f*Mathf.Sin(Time.unscaledTime*5.5f);
        public void Initialize(BattleWorld owner)
        {
            world=owner;var root=new GameObject("Low HP edge canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.transform.SetParent(transform,false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2;
            var obj=new GameObject("Low HP edges (center clear)",typeof(RectTransform),typeof(CanvasRenderer),typeof(HealthEdgeGraphic));obj.transform.SetParent(root.transform,false);
            var rect=obj.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            graphic=obj.GetComponent<HealthEdgeGraphic>();graphic.raycastTarget=false;Refresh();
        }
        public void Refresh()
        {
            if(world?.Session==null)return;var active=world.ActiveActor;float hp=active==null?1:(float)active.hp/active.maxHp;
            Severity=world.Menu||world.DungeonModal?0:hp<.15f?2:hp<.30f?1:0;
            graphic.color=new Color(.78f,.025f,.025f,Severity==0?0:Severity==1?.17f:.27f+.14f*Pulse);
        }
        void LateUpdate()=>Refresh();
    }
}
