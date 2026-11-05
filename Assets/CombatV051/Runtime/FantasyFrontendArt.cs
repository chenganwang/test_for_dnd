using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

namespace CombatV051.Runtime
{
    public enum FantasySurface { Forest, Menu, Card, SmallCard, Node, Page, Button, Divider, Seal, Emblem }
    public enum FantasyNodeState { None, Unlocked, Available, Prerequisite, Insufficient }

    // Original, code-authored paper/metal/forest artwork. No textures, external
    // assets, random stream, materials or gameplay state are involved. Finite UI
    // response values come from the existing button skin, never the world clock.
    public sealed class FantasyFrontendArt : MaskableGraphic
    {
        public static readonly Color Paper = new Color(.90f,.85f,.72f);
        public static readonly Color Ink = new Color(.16f,.20f,.18f);
        public static readonly Color Gold = new Color(.68f,.57f,.35f);
        public static readonly Color Light = new Color(.90f,.88f,.76f);
        FantasySurface surface; FantasyNodeState nodeState; bool selected,hover,pressed; int symbol;
        float hoverWeight,selectionWeight,dimWeight,pulseWeight;
        public FantasySurface Surface=>surface;
        public FantasyNodeState NodeState=>nodeState;
        public bool Selected=>selected;
        public int MeshBuildCount {get;private set;}
        public void Configure(FantasySurface value,FantasyNodeState state=FantasyNodeState.None,bool chosen=false,int icon=0)
        {if(surface==value&&nodeState==state&&selected==chosen&&symbol==icon)return;surface=value;nodeState=state;selected=chosen;symbol=icon;SetVerticesDirty();}
        public void Interact(bool over,bool down)
        {if(hover==over&&pressed==down)return;hover=over;pressed=down;SetVerticesDirty();}
        public void Motion(float over,float chosen,float dim,float pulse)
        {
            if(Mathf.Approximately(hoverWeight,over)&&Mathf.Approximately(selectionWeight,chosen)&&Mathf.Approximately(dimWeight,dim)&&Mathf.Approximately(pulseWeight,pulse))return;
            hoverWeight=over;selectionWeight=chosen;dimWeight=dim;pulseWeight=pulse;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            MeshBuildCount++;v.Clear();Rect r=rectTransform.rect;
            if(surface==FantasySurface.Forest){Forest(v,r);return;}
            if(surface==FantasySurface.Divider){Line(v,r.width*.11f,0,r.width*.89f,0,1,Gold);Diamond(v,r.width*.06f,0,4,Gold);Diamond(v,r.width*.94f,0,4,Gold);return;}
            if(surface==FantasySurface.Emblem){Emblem(v,r);return;}
            if(surface==FantasySurface.Seal){Seal(v,r.width*.5f,r.height*.5f,Mathf.Min(r.width,r.height)*.49f,new Color(.23f,.38f,.32f));return;}
            float w=r.width,h=r.height;
            bool card=surface==FantasySurface.Card||surface==FantasySurface.SmallCard;
            bool dark=surface==FantasySurface.Button;
            Color fill=dark?new Color(.105f,.18f,.19f):Paper;
            if(nodeState==FantasyNodeState.Prerequisite)fill=new Color(.60f,.65f,.61f);
            fill=Color.Lerp(fill,dark?new Color(.24f,.34f,.30f):new Color(1,.95f,.81f),.43f*hoverWeight);
            fill=Color.Lerp(fill,new Color(fill.r*.88f,fill.g*.88f,fill.b*.88f,fill.a),dimWeight);
            fill=Color.Lerp(fill,new Color(1,.97f,.83f,fill.a),pulseWeight*.3f);
            if(pressed)fill=Color.Lerp(fill,new Color(.38f,.44f,.36f),.28f);
            Cut(v,1,3,w-2,h-3,8,new Color(0,0,0,.32f));
            Cut(v,0,0,w,h-3,8,dark?new Color(.22f,.29f,.27f):new Color(.40f,.38f,.29f));
            Cut(v,2,2,w-4,h-7,7,fill);
            // Quiet paper wash, deckled seam and sparse corner stitching.
            Quad(v,9,8,w-18,Mathf.Min(26,h*.24f),dark?new Color(.30f,.41f,.36f,.09f):new Color(1,.98f,.83f,.23f));
            Color edge=Color.Lerp(Gold,new Color(.88f,.75f,.44f),hoverWeight);
            edge=Color.Lerp(edge,new Color(.35f,.66f,.52f),selectionWeight);
            edge=Color.Lerp(edge,Light,pulseWeight*.65f);
            Border(v,6,6,w-12,h-15,.8f,edge);
            if(selectionWeight>0)
            {
                float x=w*.5f,half=(w*.5f-6)*selectionWeight,y=(h-3)*.5f,vertical=(h-15)*.5f*selectionWeight;
                Line(v,x-half,6,x+half,6,2.3f,edge);Line(v,x-half,h-9,x+half,h-9,2.3f,edge);
                Line(v,6,y-vertical,6,y+vertical,2.3f,edge);Line(v,w-6,y-vertical,w-6,y+vertical,2.3f,edge);
            }
            if(card||surface==FantasySurface.Menu||surface==FantasySurface.Page)
            {
                Corner(v,11,11,1,1,18,edge);Corner(v,w-11,11,-1,1,18,edge);
                Corner(v,11,h-17,1,-1,18,edge);Corner(v,w-11,h-17,-1,-1,18,edge);
            }
            if(surface==FantasySurface.Menu)
            {
                Quad(v,15,11,48,h-28,new Color(.24f,.34f,.29f));
                Poly(v,new Color(.24f,.34f,.29f),15,h-17,39,h-9,63,h-17);
                Icon(v,39,h*.45f,13,symbol,Light);
                Diamond(v,w-30,h*.48f,4,Gold);
            }
            if(card)
            {
                float y=surface==FantasySurface.Card?h-49:h-15;
                Line(v,20,y,w-20,y,.75f,new Color(.49f,.48f,.35f,.48f));
                if(surface==FantasySurface.Card)
                {
                    // Faint circular portrait halo remains behind the pure black cutout.
                    Arc(v,w*.5f,h*.40f,w*.26f,Mathf.PI*.15f,Mathf.PI*.85f,1,new Color(.59f,.54f,.38f,.24f));
                    Diamond(v,w*.5f,20,3,Gold);
                }
            }
            if(surface==FantasySurface.Node)
            {
                Color seal=nodeState==FantasyNodeState.Unlocked?new Color(.22f,.43f,.33f):nodeState==FantasyNodeState.Insufficient?new Color(.54f,.29f,.26f):nodeState==FantasyNodeState.Prerequisite?new Color(.32f,.38f,.38f):new Color(.64f,.48f,.22f);
                Seal(v,31,h*.48f,17,seal);
                if(nodeState==FantasyNodeState.Unlocked){Line(v,23,h*.48f,29,h*.48f+5,2,Light);Line(v,29,h*.48f+5,39,h*.48f-7,2,Light);}
                else if(nodeState==FantasyNodeState.Available){Line(v,23,h*.48f,39,h*.48f,1.8f,Light);Line(v,31,h*.48f-8,31,h*.48f+8,1.8f,Light);}
                else if(nodeState==FantasyNodeState.Insufficient){Line(v,31,h*.48f-8,31,h*.48f+3,2,Light);Diamond(v,31,h*.48f+8,1.6f,Light);}
                else{Arc(v,31,h*.48f-2,5,Mathf.PI,Mathf.PI*2,1.6f,Light);Quad(v,25,h*.48f-2,12,10,Light);}
            }
            if(surface==FantasySurface.Page)
            {
                Line(v,14,21,14,h-27,1,new Color(.49f,.43f,.30f,.4f));
                Line(v,18,21,18,h-27,.7f,new Color(.49f,.43f,.30f,.3f));
                Poly(v,new Color(.67f,.58f,.39f),w-30,3,w-3,3,w-3,30);Line(v,w-30,3,w-3,30,.8f,Gold);
            }
        }
        void Forest(VertexHelper v,Rect rect)
        {
            float w=rect.width,h=rect.height;Gradient(v,w,h,new Color(.033f,.070f,.099f),new Color(.067f,.143f,.143f));
            // A still moon and distant hills lend depth without animation or noise.
            Disk(v,w*.51f,h*.255f,100,new Color(.46f,.56f,.50f,.055f),44);
            Disk(v,w*.51f,h*.255f,78,new Color(.68f,.70f,.53f,.10f),40);
            Disk(v,w*.51f,h*.255f,53,new Color(.80f,.80f,.64f,.14f),36);
            for(int i=0;i<27;i++)
            {float x=90+(i*173%1090),y=38+(i*67%242);Diamond(v,x,y,i%4==0?1.8f:.8f,new Color(.72f,.78f,.68f,i%4==0?.45f:.21f));}
            Poly(v,new Color(.08f,.17f,.17f,.9f),0,h*.69f,w*.13f,h*.49f,w*.29f,h*.68f,w*.45f,h*.59f,w*.66f,h*.77f,w*.86f,h*.47f,w,h*.62f,w,h,0,h);
            Poly(v,new Color(.065f,.13f,.14f),0,h*.8f,w*.19f,h*.68f,w*.43f,h*.81f,w*.60f,h*.72f,w*.8f,h*.84f,w,h*.66f,w,h,0,h);
            for(int side=0;side<2;side++)
            {
                float direction=side==0?1:-1,origin=side==0?-9:w+9;
                Color bark=new Color(.027f,.067f,.079f);
                Poly(v,bark,origin,0,origin+direction*59,0,origin+direction*38,h*.26f,origin+direction*73,h*.55f,origin+direction*43,h,origin-direction*24,h);
                Branch(v,origin+direction*35,h*.27f,origin+direction*139,h*.105f,origin+direction*268,h*.08f,14,bark);
                Branch(v,origin+direction*42,h*.40f,origin+direction*131,h*.32f,origin+direction*208,h*.18f,9,bark);
                Branch(v,origin+direction*50,h*.72f,origin+direction*143,h*.63f,origin+direction*186,h*.5f,11,bark);
                for(int i=0;i<8;i++)
                {float x=origin+direction*(104+i*17),y=h*(.08f+.017f*(i%3));Leaf(v,x,y,direction*(24-i),i%2==0?-18:18,new Color(.105f,.19f,.18f,.70f));}
                for(int i=0;i<7;i++)Leaf(v,origin+direction*(25+i*17),h-44-i%3*15,direction*23,-33,new Color(.09f,.22f,.19f,.9f));
            }
            // Thin metal book-cover perimeter leaves a broad, quiet content area.
            Border(v,24,21,w-48,h-42,.8f,new Color(.46f,.49f,.35f,.36f));
            Corner(v,32,29,1,1,24,Gold);Corner(v,w-32,29,-1,1,24,Gold);
            Corner(v,32,h-29,1,-1,24,Gold);Corner(v,w-32,h-29,-1,-1,24,Gold);
        }
        void Emblem(VertexHelper v,Rect r)
        {
            float x=r.width*.5f,y=r.height*.5f;
            Arc(v,x,y,29,0,Mathf.PI*2,1,Gold);Arc(v,x,y,24,.2f,Mathf.PI*1.92f,.65f,new Color(.67f,.73f,.61f));
            Diamond(v,x,y,10,Gold);Line(v,x,y-23,x,y+23,1,Gold);Line(v,x-21,y,x+21,y,1,Gold);
            Leaf(v,x-35,y+8,-12,-20,Gold);Leaf(v,x+35,y+8,12,-20,Gold);
            Line(v,x-130,y,x-49,y,.8f,Gold);Line(v,x+49,y,x+130,y,.8f,Gold);
        }
        void Icon(VertexHelper v,float x,float y,float z,int id,Color c)
        {
            if(id==0){Border(v,x-z,y-z,z*2,z*1.6f,1.3f,c);Line(v,x,y-z,x,y+z*.6f,1,c);Line(v,x-z-3,y+z*.8f,x+z+3,y+z*.8f,1,c);}
            else if(id==1){Arc(v,x,y,z,0,Mathf.PI*2,1,c);Poly(v,c,x,y-z-3,x-4,y+3,x+3,y);Poly(v,c,x,y+z+3,x+4,y-3,x-3,y);}
            else{Disk(v,x,y-7,4,c,12);Arc(v,x,y+10,9,Mathf.PI,Mathf.PI*2,1.4f,c);Disk(v,x-11,y-3,3,c,10);Disk(v,x+11,y-3,3,c,10);}
        }
        void Seal(VertexHelper v,float x,float y,float radius,Color c)
        {Disk(v,x+1,y+2,radius,new Color(0,0,0,.15f),16);Disk(v,x,y,radius,c,16);Arc(v,x,y,radius-3,0,Mathf.PI*2,.7f,new Color(.83f,.78f,.59f,.65f));}
        void Gradient(VertexHelper v,float w,float h,Color top,Color bottom)
        {int i=v.currentVertCount;v.AddVert(P(0,0),top,Vector2.zero);v.AddVert(P(w,0),top,Vector2.zero);v.AddVert(P(w,h),bottom,Vector2.zero);v.AddVert(P(0,h),bottom,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
        void Branch(VertexHelper v,float ax,float ay,float bx,float by,float cx,float cy,float thickness,Color c)
        {Line(v,ax,ay,bx,by,thickness,c);Line(v,bx,by,cx,cy,thickness*.48f,c);Line(v,bx,by,bx+(cx-bx)*.65f,by-44,thickness*.3f,c);}
        void Leaf(VertexHelper v,float x,float y,float dx,float dy,Color c)
        {Poly(v,c,x,y,x+dx*.25f-dy*.16f,y+dy*.25f+dx*.16f,x+dx,y+dy,x+dx*.65f+dy*.13f,y+dy*.65f-dx*.13f);}
        void Corner(VertexHelper v,float x,float y,float sx,float sy,float length,Color c)
        {Line(v,x,y,x+sx*length,y,1.5f,c);Line(v,x,y,x,y+sy*length,1.5f,c);Diamond(v,x+sx*4,y+sy*4,2,c);}
        void Cut(VertexHelper v,float x,float y,float w,float h,float c,Color tint)
        {Poly(v,tint,x+c,y,x+w-c,y,x+w,y+c,x+w,y+h-c,x+w-c,y+h,x+c,y+h,x,y+h-c,x,y+c);}
        void Border(VertexHelper v,float x,float y,float w,float h,float t,Color c)
        {Line(v,x,y,x+w,y,t,c);Line(v,x+w,y,x+w,y+h,t,c);Line(v,x+w,y+h,x,y+h,t,c);Line(v,x,y+h,x,y,t,c);}
        void Arc(VertexHelper v,float x,float y,float radius,float from,float to,float t,Color c)
        {const int n=28;float ax=x+Mathf.Cos(from)*radius,ay=y+Mathf.Sin(from)*radius;for(int j=1;j<=n;j++){float a=Mathf.Lerp(from,to,j/(float)n),bx=x+Mathf.Cos(a)*radius,by=y+Mathf.Sin(a)*radius;Line(v,ax,ay,bx,by,t,c);ax=bx;ay=by;}}
        void Diamond(VertexHelper v,float x,float y,float size,Color c)=>Poly(v,c,x,y-size,x+size,y,x,y+size,x-size,y);
        void Disk(VertexHelper v,float x,float y,float radius,Color c,int count)
        {int first=v.currentVertCount;v.AddVert(P(x,y),c,Vector2.zero);for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count;v.AddVert(P(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius),c,Vector2.zero);}for(int i=0;i<count;i++)v.AddTriangle(first,first+1+i,first+1+(i+1)%count);}
        void Line(VertexHelper v,float ax,float ay,float bx,float by,float thickness,Color c)
        {Vector2 a=new Vector2(ax,ay),b=new Vector2(bx,by),n=new Vector2(-(b-a).y,(b-a).x).normalized*thickness*.5f;Poly(v,c,a.x-n.x,a.y-n.y,b.x-n.x,b.y-n.y,b.x+n.x,b.y+n.y,a.x+n.x,a.y+n.y);}
        void Quad(VertexHelper v,float x,float y,float w,float h,Color c)=>Poly(v,c,x,y,x+w,y,x+w,y+h,x,y+h);
        Vector2 P(float x,float y)=>new Vector2(rectTransform.rect.x+x,rectTransform.rect.yMax-y);
        void Poly(VertexHelper v,Color c,params float[] xy)
        {int first=v.currentVertCount,n=xy.Length/2;for(int i=0;i<n;i++)v.AddVert(P(xy[i*2],xy[i*2+1]),c,Vector2.zero);for(int i=1;i<n-1;i++)v.AddTriangle(first,first+i,first+i+1);}
    }

    public sealed class FantasyFrontendButton : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        public FantasyFrontendArt Art;public Button Button;public RectTransform Visual;public Text Label;
        bool over,down,reduced,dimmed,initialized,boundSelected;FantasyNodeState boundNode;string identity;Color labelColour;
        float hoverWeight,selectionWeight,dimWeight,pulse;Coroutine motion;
        public bool Animating=>motion!=null;
        public int UnlockPulseCount {get;private set;}
        public Vector2 VisualOffset=>Visual!=null?Visual.anchoredPosition:Vector2.zero;
        public void Bind(string key,bool reduceMotion,bool dim,Color textColour)
        {
            var command=Button as FantasyFrontendCommandButton;if(command!=null)command.CommandKey=key;
            labelColour=textColour;bool availableNow=Art.NodeState==FantasyNodeState.Available&&(!initialized||identity!=key||boundNode!=Art.NodeState);
            bool changed=!initialized||identity!=key||reduced!=reduceMotion||dimmed!=dim||boundSelected!=Art.Selected||boundNode!=Art.NodeState;
            if(identity!=key){over=down=false;hoverWeight=selectionWeight=dimWeight=pulse=0;}
            identity=key;reduced=reduceMotion;dimmed=dim;boundSelected=Art.Selected;boundNode=Art.NodeState;initialized=true;
            float target=Art.Selected?1:0;
            if(changed||motion==null&&!Mathf.Approximately(selectionWeight,target))
            {
                if(availableNow&&!reduced)pulse=.35f;
                Refresh();
            }
            else Apply();
        }
        public void PlayUnlock()
        {if(reduced||!isActiveAndEnabled)return;UnlockPulseCount++;pulse=1;Refresh();}
        void Refresh()
        {
            if(!Art||!Button)return;
            Art.Interact(over&&Button.interactable,down&&Button.interactable);
            if(motion!=null){StopCoroutine(motion);motion=null;}
            if(reduced||!isActiveAndEnabled)
            {hoverWeight=over&&Button.interactable?1:0;selectionWeight=Art.Selected?1:0;dimWeight=dimmed?1:0;pulse=0;Apply();return;}
            motion=StartCoroutine(Animate());
        }
        IEnumerator Animate()
        {
            float fromHover=hoverWeight,fromSelection=selectionWeight,fromDim=dimWeight,fromPulse=pulse;
            float toHover=over&&Button.interactable?1:0,toSelection=Art.Selected?1:0,toDim=dimmed?1:0;
            for(float elapsed=0;elapsed<.2f;elapsed+=Time.unscaledDeltaTime)
            {
                hoverWeight=Mathf.Lerp(fromHover,toHover,Mathf.SmoothStep(0,1,elapsed/.15f));
                selectionWeight=Mathf.Lerp(fromSelection,toSelection,Mathf.SmoothStep(0,1,elapsed/.18f));
                dimWeight=Mathf.Lerp(fromDim,toDim,Mathf.SmoothStep(0,1,elapsed/.18f));
                pulse=Mathf.Lerp(fromPulse,0,Mathf.SmoothStep(0,1,elapsed/.2f));Apply();yield return null;
            }
            hoverWeight=toHover;selectionWeight=toSelection;dimWeight=toDim;pulse=0;Apply();motion=null;
        }
        void Apply()
        {
            if(Visual)Visual.anchoredPosition=reduced?Vector2.zero:Vector2.up*(4*hoverWeight);
            if(Art)Art.Motion(hoverWeight,selectionWeight,dimWeight,pulse);
            if(Label)Label.color=Color.Lerp(labelColour,Color.white,hoverWeight*.08f);
        }
        public void OnPointerEnter(PointerEventData e){over=true;Refresh();}
        public void OnPointerExit(PointerEventData e){over=false;down=false;Refresh();}
        public void OnPointerDown(PointerEventData e){if(e.button!=PointerEventData.InputButton.Left)return;down=true;Refresh();}
        public void OnPointerUp(PointerEventData e){down=false;Refresh();}
        void OnDisable()
        {
            if(motion!=null){StopCoroutine(motion);motion=null;}over=down=false;initialized=false;
            hoverWeight=selectionWeight=dimWeight=pulse=0;if(Art){Art.Interact(false,false);Art.Motion(0,0,0,0);}if(Visual)Visual.anchoredPosition=Vector2.zero;
        }
    }

    // The first click commits synchronously. A second click in the same physical
    // double-click only suppresses the same command; pooled controls rebound to
    // another page/node and rapid Back remain immediately usable.
    public sealed class FantasyFrontendCommandButton : Button
    {
        public string CommandKey;public bool SuppressDoubleClick;
        public int SuppressedDoubleClicks {get;private set;}
        string lastClickedCommand;
        public override void OnPointerClick(PointerEventData eventData)
        {
            if(eventData.button!=PointerEventData.InputButton.Left||!IsActive()||!IsInteractable())return;
            if(SuppressDoubleClick&&eventData.clickCount>1&&lastClickedCommand==CommandKey){SuppressedDoubleClicks++;return;}
            lastClickedCommand=CommandKey;base.OnPointerClick(eventData);
        }
    }
}
