using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public enum FrontArtworkKind { Background, Border, Campaign, Map, Company, Fighter, Rogue, Mage }
    /// <summary>Original ink illustrations. Fixed contours rebuild only on rect/style dirtiness; no textures or Update.</summary>
    public sealed class P47FrontendArtwork : MaskableGraphic
    {
        public FrontArtworkKind Kind;
        float glow;
        static readonly Color Ink=new Color(.115f,.105f,.08f,.92f),Gold=new Color(.67f,.52f,.31f,.9f);
        static readonly Vector2[] Paper={new Vector2(.045f,.10f),new Vector2(.026f,.28f),new Vector2(.050f,.49f),new Vector2(.025f,.79f),new Vector2(.068f,.935f),new Vector2(.30f,.951f),new Vector2(.49f,.93f),new Vector2(.77f,.949f),new Vector2(.948f,.915f),new Vector2(.969f,.71f),new Vector2(.946f,.50f),new Vector2(.971f,.255f),new Vector2(.931f,.057f),new Vector2(.72f,.041f),new Vector2(.50f,.062f),new Vector2(.24f,.039f)};
        // Existing eight map marks, for illustration only; never navigation or collision data.
        static readonly Vector2[] Rooms={new Vector2(.16f,.32f),new Vector2(.37f,.35f),new Vector2(.57f,.32f),new Vector2(.78f,.37f),new Vector2(.77f,.68f),new Vector2(.55f,.72f),new Vector2(.32f,.66f),new Vector2(.14f,.73f)};
        static readonly Vector2[] Robe={new Vector2(-.25f,.12f),new Vector2(-.22f,.31f),new Vector2(-.25f,.48f),new Vector2(-.16f,.60f),new Vector2(-.08f,.62f),new Vector2(.09f,.62f),new Vector2(.22f,.53f),new Vector2(.24f,.31f),new Vector2(.28f,.12f),new Vector2(.12f,.08f),new Vector2(-.07f,.10f)};
        static readonly Vector2[] Hood={new Vector2(-.115f,.65f),new Vector2(-.105f,.79f),new Vector2(-.06f,.91f),new Vector2(.012f,.944f),new Vector2(.098f,.843f),new Vector2(.135f,.68f),new Vector2(.055f,.61f),new Vector2(-.042f,.625f)};
        static readonly Vector2[] Face={new Vector2(-.061f,.79f),new Vector2(-.044f,.845f),new Vector2(.043f,.84f),new Vector2(.067f,.775f),new Vector2(.04f,.681f),new Vector2(-.022f,.658f),new Vector2(-.06f,.711f)};
        static readonly Vector2[] Helmet={new Vector2(-.115f,.695f),new Vector2(-.111f,.837f),new Vector2(-.059f,.901f),new Vector2(.048f,.907f),new Vector2(.106f,.835f),new Vector2(.106f,.699f),new Vector2(.062f,.665f),new Vector2(-.066f,.665f)};
        static readonly Vector2[] Shield={new Vector2(-.295f,.526f),new Vector2(-.175f,.565f),new Vector2(-.064f,.512f),new Vector2(-.084f,.30f),new Vector2(-.185f,.17f),new Vector2(-.279f,.297f)};
        public void SetGlow(float value){if(Mathf.Abs(glow-value)<.003f)return;glow=value;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;if(r.width<=0||r.height<=0)return;
            switch(Kind)
            {
                case FrontArtworkKind.Background:Background(vh,r);return;
                case FrontArtworkKind.Border:Border(vh,r);return;
                case FrontArtworkKind.Map:Map(vh,r);return;
                case FrontArtworkKind.Campaign:Castle(vh,Fit(r,1.45f));return;
                case FrontArtworkKind.Company:
                    r=Fit(r,1.50f);Person(vh,r,.24f,.76f,FrontArtworkKind.Rogue);Person(vh,r,.75f,.81f,FrontArtworkKind.Mage);Person(vh,r,.50f,1,FrontArtworkKind.Fighter);return;
                default:Person(vh,Fit(r,.86f),.5f,1,Kind);return;
            }
        }
        // Internal letterboxing preserves portrait anatomy without changing any UI rectangle.
        static Rect Fit(Rect r,float aspect)
        {float w=Mathf.Min(r.width,r.height*aspect),h=w/aspect;return new Rect(r.x+(r.width-w)*.5f,r.y+(r.height-h)*.5f,w,h);}
        static void Background(VertexHelper vh,Rect r)
        {
            Quad(vh,r,0,0,1,1,new Color(.055f,.062f,.069f));
            for(int i=0;i<42;i++)
            {
                float x=.025f+Noise(i*3)*.93f,y=.10f+Noise(i*3+1)*.83f;
                Line(vh,r,new Vector2(x,y),new Vector2(Mathf.Min(.99f,x+.018f+Noise(i+9)*.073f),y+.0025f*(Noise(i+2)-.5f)),.00065f,new Color(.54f,.40f,.24f,.10f));
            }
            for(int i=0;i<9;i++)
            {
                float x=i*.128f,top=.24f+.10f*Noise(i+61);
                Triangle(vh,r,new Vector2(x-.08f,.13f),new Vector2(x+.04f,top),new Vector2(x+.19f,.13f),new Color(.079f,.090f,.098f));
                Stroke(vh,r,new Vector2(x-.047f,.20f),new Vector2(x+.04f,top),.0007f,new Color(.23f,.25f,.25f,.20f),i);
            }
            Quad(vh,r,0,0,1,.115f,new Color(.071f,.059f,.049f));
        }
        void Border(VertexHelper vh,Rect r)
        {
            var ink=Color.Lerp(new Color(.43f,.32f,.19f,.8f),new Color(.91f,.73f,.38f),Mathf.Clamp01(glow));
            if(glow>1)ink=Color.Lerp(ink,new Color(1,.91f,.64f),(glow-1)*3);
            for(int edge=0;edge<2;edge++)
            {
                float n=edge==0?.014f:.026f,w=edge==0?.0024f:.0012f;
                Stroke(vh,r,new Vector2(n,n),new Vector2(1-n,n),w,ink,edge);
                Stroke(vh,r,new Vector2(n,1-n),new Vector2(1-n,1-n),w,ink,edge+4);
                Line(vh,r,new Vector2(n,n),new Vector2(n,1-n),w,ink);Line(vh,r,new Vector2(1-n,n),new Vector2(1-n,1-n),w,ink);
            }
            for(int i=0;i<4;i++)
            {
                float sx=i%2==0?1:-1,sy=i<2?1:-1;var p=new Vector2(sx>0?.047f:.953f,sy>0?.049f:.951f);
                Diamond(vh,r,p,.010f,ink);
                Line(vh,r,p+new Vector2(.006f*sx,.012f*sy),p+new Vector2(.048f*sx,.019f*sy),.0018f,ink);
                Line(vh,r,p+new Vector2(.012f*sx,.006f*sy),p+new Vector2(.019f*sx,.051f*sy),.0018f,ink);
            }
        }
        static void Map(VertexHelper vh,Rect r)
        {
            Polygon(vh,r,Paper,Vector2.zero,Vector2.one,new Color(.43f,.345f,.222f,.93f));
            Outline(vh,r,Paper,Vector2.zero,Vector2.one,.004f,new Color(.72f,.58f,.36f,.62f));
            for(int i=0;i<17;i++)
            {
                float x=.08f+Noise(i+22)*.81f,y=.09f+Noise(i+80)*.79f;
                Stroke(vh,r,new Vector2(x,y),new Vector2(Mathf.Min(.92f,x+.045f+Noise(i)*.075f),y+.014f),.0014f,new Color(.22f,.16f,.095f,.29f),i);
            }
            Line(vh,r,new Vector2(.34f,.065f),new Vector2(.36f,.927f),.012f,new Color(.76f,.61f,.37f,.12f));
            Line(vh,r,new Vector2(.67f,.06f),new Vector2(.66f,.93f),.009f,new Color(.15f,.105f,.055f,.16f));
            for(int i=1;i<Rooms.Length;i++)
            {
                var a=Rooms[i-1];var b=Rooms[i];var normal=new Vector2(-(b-a).y,(b-a).x).normalized*.016f;
                Stroke(vh,r,a+normal,b+normal,.004f,Ink,i);Stroke(vh,r,a-normal,b-normal,.003f,Ink,i+7);
            }
            for(int i=0;i<7;i++)
            {float t=i/7f;Line(vh,r,Vector2.Lerp(Rooms[0],Rooms[6],t),Vector2.Lerp(Rooms[0],Rooms[6],t+.065f),.0045f,new Color(.75f,.61f,.35f,.85f));}
            for(int i=0;i<Rooms.Length;i++)
            {
                var p=Rooms[i];var a=p+new Vector2(-.048f,-.068f);var b=p+new Vector2(.052f,-.061f);var c=p+new Vector2(.047f,.064f);var d=p+new Vector2(-.056f,.071f);
                QuadPoints(vh,r,a,b,c,d,new Color(.54f,.435f,.275f));
                Stroke(vh,r,a,b,.009f,Ink,i);Stroke(vh,r,b,c,.007f,Ink,i+1);Stroke(vh,r,c,d,.009f,Ink,i+2);Stroke(vh,r,d,a,.006f,Ink,i+3);
                Arch(vh,r,p+new Vector2(-.022f,-.041f),.043f,.077f,Ink);
                Arc(vh,r,p+new Vector2(0,.002f),.033f,.044f,0,Mathf.PI,7,.003f,new Color(.82f,.67f,.43f,.66f));
                Line(vh,r,p+new Vector2(-.046f,-.075f),p+new Vector2(.043f,-.072f),.002f,Ink);
            }
            Diamond(vh,r,Rooms[0]+new Vector2(0,-.017f),.017f,new Color(.91f,.74f,.40f));
            for(int i=0;i<8;i++){float x=.17f+i*.037f;Line(vh,r,new Vector2(x,.145f),new Vector2(x+.015f,.20f+.014f*Noise(i+91)),.002f,Ink);}
            var compass=new Vector2(.864f,.825f);Arc(vh,r,compass,.050f,.066f,0,Mathf.PI*2,12,.0025f,Ink);
            Triangle(vh,r,compass+new Vector2(0,.10f),compass+new Vector2(-.018f,-.01f),compass+new Vector2(.005f,.006f),Ink);
            Triangle(vh,r,compass+new Vector2(0,-.083f),compass+new Vector2(.017f,.008f),compass+new Vector2(-.004f,-.005f),Ink);
            Line(vh,r,compass+new Vector2(-.072f,0),compass+new Vector2(.071f,0),.002f,Ink);
        }
        static void Castle(VertexHelper vh,Rect r)
        {
            Color stone=new Color(.32f,.33f,.30f),lit=new Color(.48f,.45f,.35f),shade=new Color(.17f,.20f,.20f),pen=new Color(.65f,.55f,.38f,.74f);
            Arc(vh,r,new Vector2(.52f,.49f),.40f,.44f,.08f,3.03f,18,.002f,new Color(.60f,.47f,.30f,.25f));
            QuadPoints(vh,r,new Vector2(.16f,.18f),new Vector2(.85f,.16f),new Vector2(.81f,.50f),new Vector2(.20f,.47f),stone);
            QuadPoints(vh,r,new Vector2(.39f,.20f),new Vector2(.66f,.18f),new Vector2(.64f,.78f),new Vector2(.42f,.80f),lit);
            QuadPoints(vh,r,new Vector2(.56f,.18f),new Vector2(.66f,.18f),new Vector2(.64f,.78f),new Vector2(.57f,.76f),shade);
            Tower(vh,r,.16f,.19f,.17f,.61f,stone,pen);Tower(vh,r,.73f,.17f,.14f,.52f,stone,pen);
            Triangle(vh,r,new Vector2(.386f,.784f),new Vector2(.522f,.956f),new Vector2(.671f,.773f),new Color(.36f,.26f,.19f));
            Stroke(vh,r,new Vector2(.386f,.784f),new Vector2(.522f,.956f),.004f,pen,6);Stroke(vh,r,new Vector2(.522f,.956f),new Vector2(.671f,.773f),.004f,pen,7);
            for(int i=0;i<5;i++)Line(vh,r,new Vector2(.445f+i*.025f,.80f),new Vector2(.509f+i*.008f,.912f),.0018f,new Color(.13f,.12f,.105f,.72f));
            for(int i=0;i<4;i++){float x=.32f+i*.1f;Quad(vh,r,x,.48f,.049f,.052f,stone);Line(vh,r,new Vector2(x,.53f),new Vector2(x+.049f,.529f),.003f,pen);}
            Arch(vh,r,new Vector2(.454f,.18f),.139f,.262f,new Color(.065f,.077f,.075f));
            Arc(vh,r,new Vector2(.523f,.348f),.089f,.132f,0,Mathf.PI,10,.011f,pen);
            Line(vh,r,new Vector2(.435f,.18f),new Vector2(.435f,.35f),.008f,pen);Line(vh,r,new Vector2(.612f,.18f),new Vector2(.612f,.35f),.008f,pen);
            for(int i=0;i<5;i++){float x=.472f+i*.025f;Line(vh,r,new Vector2(x,.19f),new Vector2(x,.364f),.003f,lit);}
            Arch(vh,r,new Vector2(.473f,.58f),.034f,.10f,shade);Arch(vh,r,new Vector2(.55f,.58f),.034f,.10f,shade);
            for(int row=0;row<4;row++)
            {
                float y=.245f+row*.047f;Stroke(vh,r,new Vector2(.23f,y),new Vector2(.40f,y+.002f),.0018f,pen,row);Stroke(vh,r,new Vector2(.64f,y+.005f),new Vector2(.79f,y),.0018f,pen,row+4);
                for(int i=0;i<2;i++){float x=.25f+i*.073f+(row%2)*.028f;Line(vh,r,new Vector2(x,y),new Vector2(x+.003f,y+.042f),.0015f,pen);}
            }
            QuadPoints(vh,r,new Vector2(.36f,.03f),new Vector2(.51f,.04f),new Vector2(.56f,.19f),new Vector2(.49f,.195f),new Color(.48f,.39f,.25f));
            for(int i=0;i<4;i++){float y=.06f+i*.032f;Line(vh,r,new Vector2(.405f+i*.021f,y),new Vector2(.518f+i*.008f,y+.015f),.003f,shade);}
            for(int i=0;i<12;i++){float x=.10f+i*.07f;Stroke(vh,r,new Vector2(x,.13f+Noise(i)*.04f),new Vector2(x+.048f,.14f+Noise(i+4)*.024f),.003f,pen,i);}
        }
        static void Tower(VertexHelper vh,Rect r,float x,float y,float w,float h,Color stone,Color pen)
        {
            QuadPoints(vh,r,new Vector2(x,y),new Vector2(x+w,y-.015f),new Vector2(x+w*.92f,y+h),new Vector2(x+w*.07f,y+h+.011f),stone);Quad(vh,r,x+w*.63f,y,w*.30f,h,new Color(.20f,.235f,.23f));
            for(int i=0;i<3;i++)Quad(vh,r,x+i*w*.36f,y+h,w*.23f,.048f,stone);
            Stroke(vh,r,new Vector2(x,y),new Vector2(x+w*.07f,y+h+.01f),.003f,pen,3);Arch(vh,r,new Vector2(x+w*.38f,y+h*.56f),w*.20f,h*.20f,new Color(.08f,.10f,.095f));
            for(int i=1;i<5;i++){float row=y+h*i/5;Stroke(vh,r,new Vector2(x+.009f,row),new Vector2(x+w*.9f,row+.005f),.002f,pen,i);}
        }
        static void Person(VertexHelper vh,Rect r,float center,float scale,FrontArtworkKind role)
        {
            var origin=new Vector2(center,.02f);var size=new Vector2(scale*r.height*.86f/r.width,scale);
            Color cloth=role==FrontArtworkKind.Mage?new Color(.23f,.30f,.34f):role==FrontArtworkKind.Rogue?new Color(.22f,.275f,.24f):new Color(.37f,.33f,.265f);
            Color edge=new Color(.63f,.53f,.37f,.89f),dark=new Color(.09f,.115f,.113f),metal=new Color(.53f,.56f,.50f),skin=new Color(.65f,.52f,.36f);
            Polygon(vh,r,Robe,origin,size,cloth);Outline(vh,r,Robe,origin,size,.0035f,edge);
            RoleLine(vh,r,origin,size,-.17f,.52f,-.10f,.15f,.004f,edge);RoleLine(vh,r,origin,size,.13f,.53f,.20f,.16f,.004f,edge);
            RoleLine(vh,r,origin,size,-.03f,.53f,-.045f,.11f,.003f,dark);RoleLine(vh,r,origin,size,.05f,.50f,.10f,.12f,.005f,dark);
            for(int i=0;i<4;i++)RoleLine(vh,r,origin,size,-.19f+i*.021f,.19f,-.155f+i*.02f,.37f,.0018f,new Color(.65f,.55f,.37f,.45f));
            RoleLine(vh,r,origin,size,-.15f,.32f,.17f,.30f,.035f,new Color(.24f,.175f,.115f));RoleLine(vh,r,origin,size,-.15f,.328f,.17f,.308f,.003f,edge);Diamond(vh,r,RolePoint(origin,size,.015f,.313f),.021f*size.x,Gold);
            if(role==FrontArtworkKind.Fighter)
            {
                Polygon(vh,r,Face,origin,size,skin);Polygon(vh,r,Helmet,origin,size,metal);Outline(vh,r,Helmet,origin,size,.004f,edge);
                RoleLine(vh,r,origin,size,-.09f,.781f,.09f,.781f,.020f,dark);RoleLine(vh,r,origin,size,.005f,.796f,.005f,.693f,.015f,dark);RoleLine(vh,r,origin,size,-.072f,.852f,.055f,.866f,.004f,new Color(.75f,.71f,.56f));
                QuadPoints(vh,r,RolePoint(origin,size,-.175f,.585f),RolePoint(origin,size,.17f,.588f),RolePoint(origin,size,.13f,.462f),RolePoint(origin,size,-.135f,.458f),metal);
                RoleLine(vh,r,origin,size,-.13f,.448f,.12f,.442f,.004f,edge);RoleLine(vh,r,origin,size,0,.579f,0,.365f,.005f,edge);
                for(int i=0;i<4;i++)RoleLine(vh,r,origin,size,-.115f,.42f-i*.023f,.113f,.417f-i*.023f,.002f,dark);
                Polygon(vh,r,Shield,origin,size,new Color(.30f,.32f,.285f));Outline(vh,r,Shield,origin,size,.009f,edge);
                RoleLine(vh,r,origin,size,-.179f,.525f,-.185f,.217f,.0045f,Gold);RoleLine(vh,r,origin,size,-.268f,.448f,-.09f,.433f,.0045f,Gold);Diamond(vh,r,RolePoint(origin,size,-.184f,.414f),.037f*size.x,edge);
                RoleLine(vh,r,origin,size,.255f,.25f,.249f,.79f,.021f,metal);RoleLine(vh,r,origin,size,.249f,.30f,.25f,.76f,.003f,new Color(.89f,.83f,.66f));
                RoleLine(vh,r,origin,size,.205f,.31f,.297f,.31f,.016f,Gold);RoleLine(vh,r,origin,size,.256f,.17f,.255f,.27f,.018f,new Color(.28f,.21f,.15f));
            }
            else
            {
                Polygon(vh,r,Hood,origin,size,cloth);Outline(vh,r,Hood,origin,size,.005f,edge);Polygon(vh,r,Face,origin,size,dark);
                RoleLine(vh,r,origin,size,-.038f,.779f,.006f,.783f,.009f,skin);RoleLine(vh,r,origin,size,.020f,.783f,.054f,.777f,.008f,skin);
                RoleLine(vh,r,origin,size,-.085f,.687f,-.145f,.579f,.011f,edge);RoleLine(vh,r,origin,size,.086f,.681f,.139f,.545f,.010f,edge);
                if(role==FrontArtworkKind.Mage)
                {
                    RoleLine(vh,r,origin,size,-.074f,.619f,.04f,.434f,.007f,Gold);RoleLine(vh,r,origin,size,.080f,.620f,.04f,.434f,.007f,Gold);Diamond(vh,r,RolePoint(origin,size,.04f,.428f),.026f*size.x,Gold);
                    for(int i=0;i<4;i++)RoleLine(vh,r,origin,size,.165f,.19f+i*.046f,.19f,.205f+i*.046f,.003f,Gold);
                    RoleLine(vh,r,origin,size,.277f,.11f,.284f,.802f,.023f,new Color(.38f,.27f,.16f));RoleLine(vh,r,origin,size,.282f,.13f,.29f,.785f,.004f,edge);
                    Arc(vh,r,RolePoint(origin,size,.283f,.839f),.053f*size.x,.078f*size.y,-.35f,3.7f,10,.009f,Gold);Diamond(vh,r,RolePoint(origin,size,.283f,.849f),.026f*size.x,new Color(.39f,.62f,.59f));
                }
                else
                {
                    RoleLine(vh,r,origin,size,-.133f,.584f,.135f,.347f,.023f,new Color(.33f,.25f,.17f));RoleLine(vh,r,origin,size,-.131f,.588f,.138f,.354f,.003f,edge);
                    for(int i=0;i<4;i++)Diamond(vh,r,RolePoint(origin,size,-.089f+i*.047f,.536f-i*.042f),.007f*size.x,Gold);
                    RoleLine(vh,r,origin,size,-.17f,.30f,-.288f,.625f,.020f,metal);RoleLine(vh,r,origin,size,.183f,.292f,.295f,.603f,.020f,metal);
                    RoleLine(vh,r,origin,size,-.183f,.33f,-.278f,.601f,.003f,edge);RoleLine(vh,r,origin,size,.192f,.321f,.287f,.588f,.003f,edge);
                    RoleLine(vh,r,origin,size,-.218f,.362f,-.125f,.339f,.014f,Gold);RoleLine(vh,r,origin,size,.134f,.34f,.227f,.363f,.014f,Gold);
                }
            }
        }
        static Vector2 RolePoint(Vector2 o,Vector2 size,float x,float y)=>o+Vector2.Scale(new Vector2(x,y),size);
        static void RoleLine(VertexHelper vh,Rect r,Vector2 o,Vector2 size,float ax,float ay,float bx,float by,float width,Color c)
        {Line(vh,r,o+Vector2.Scale(new Vector2(ax,ay),size),o+Vector2.Scale(new Vector2(bx,by),size),width*size.x,c);}
        static float Noise(int seed){float n=Mathf.Sin(seed*12.9898f+78.233f)*43758.5453f;return n-Mathf.Floor(n);}
        static void Stroke(VertexHelper vh,Rect r,Vector2 a,Vector2 b,float width,Color c,int seed)
        {
            Vector2 d=b-a,n=new Vector2(-d.y,d.x).normalized;var previous=a;
            for(int i=1;i<=4;i++){var next=Vector2.Lerp(a,b,i*.25f)+(i==4?Vector2.zero:n*((Noise(seed*7+i)-.5f)*.005f));Line(vh,r,previous,next,width,c);previous=next;}
        }
        static void Arch(VertexHelper vh,Rect r,Vector2 p,float width,float height,Color c)
        {
            float radius=height*.36f;Quad(vh,r,p.x,p.y,width,height-radius,c);var center=p+new Vector2(width*.5f,height-radius);var previous=center+new Vector2(width*.5f,0);
            for(int i=1;i<=7;i++){float t=Mathf.PI*i/7;var next=center+new Vector2(Mathf.Cos(t)*width*.5f,Mathf.Sin(t)*radius);Triangle(vh,r,center,previous,next,c);previous=next;}
        }
        static void Arc(VertexHelper vh,Rect r,Vector2 center,float rx,float ry,float start,float end,int segments,float width,Color c)
        {
            var previous=center+new Vector2(Mathf.Cos(start)*rx,Mathf.Sin(start)*ry);
            for(int i=1;i<=segments;i++){float angle=Mathf.Lerp(start,end,(float)i/segments);var next=center+new Vector2(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry);Line(vh,r,previous,next,width,c);previous=next;}
        }
        static void Polygon(VertexHelper vh,Rect r,Vector2[] points,Vector2 origin,Vector2 scale,Color c)
        {
            var center=Vector2.zero;for(int i=0;i<points.Length;i++)center+=points[i];center=origin+Vector2.Scale(center/points.Length,scale);
            int first=vh.currentVertCount;vh.AddVert(P(r,center),c,Vector2.zero);
            for(int i=0;i<points.Length;i++)vh.AddVert(P(r,origin+Vector2.Scale(points[i],scale)),c,Vector2.zero);
            for(int i=0;i<points.Length;i++)vh.AddTriangle(first,first+1+i,first+1+(i+1)%points.Length);
        }
        static void Outline(VertexHelper vh,Rect r,Vector2[] points,Vector2 o,Vector2 scale,float width,Color c)
        {for(int i=0;i<points.Length;i++)Line(vh,r,o+Vector2.Scale(points[i],scale),o+Vector2.Scale(points[(i+1)%points.Length],scale),width,c);}
        static Vector2 P(Rect r,Vector2 p)=>new Vector2(r.x+p.x*r.width,r.y+p.y*r.height);
        static void Quad(VertexHelper vh,Rect r,float x,float y,float w,float h,Color c)
        {QuadPoints(vh,r,new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h),c);}
        static void QuadPoints(VertexHelper vh,Rect r,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {int i=vh.currentVertCount;vh.AddVert(P(r,a),color,Vector2.zero);vh.AddVert(P(r,b),color,Vector2.zero);vh.AddVert(P(r,c),color,Vector2.zero);vh.AddVert(P(r,d),color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
        static void Triangle(VertexHelper vh,Rect r,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int i=vh.currentVertCount;vh.AddVert(P(r,a),color,Vector2.zero);vh.AddVert(P(r,b),color,Vector2.zero);vh.AddVert(P(r,c),color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
        static void Diamond(VertexHelper vh,Rect r,Vector2 p,float size,Color c)
        {Triangle(vh,r,p+Vector2.left*size,p+Vector2.up*size,p+Vector2.right*size,c);Triangle(vh,r,p+Vector2.left*size,p+Vector2.right*size,p+Vector2.down*size,c);}
        static void Line(VertexHelper vh,Rect r,Vector2 a,Vector2 b,float width,Color c)
        {var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;QuadPoints(vh,r,a-n,b-n,b+n,a+n,c);}
    }
}
