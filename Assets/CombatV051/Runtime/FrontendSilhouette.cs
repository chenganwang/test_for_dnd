using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CombatV051.Runtime
{
    /// <summary>Cached reference-derived main-character cutouts with original companion/
    /// missing-resource contours. Every vertex remains pure black, including textured art.</summary>
    public sealed class FrontendSilhouette:MaskableGraphic
    {
        static readonly Dictionary<string,Sprite> references=new Dictionary<string,Sprite>();
        string key="fighter";Rect fitted;Sprite reference;
        public string Key {get=>key;set{if(key==value)return;key=value;reference=FindReference(key);SetAllDirty();}}
        public Sprite ReferenceSprite=>reference;
        public bool HasReferenceSprite=>reference!=null;
        public string ReferenceResourcePath=>ResourcePath(key);
        public override Texture mainTexture=>reference!=null?reference.texture:base.mainTexture;
        public static string ResourcePath(string identity)=>identity=="evan"?"UI/V12Silhouettes/Evan":identity=="mira"?"UI/V12Silhouettes/Mira":identity=="finn"?"UI/V12Silhouettes/Finn":null;
        public static void PrewarmReferences(){FindReference("evan");FindReference("mira");FindReference("finn");}
        static Sprite FindReference(string identity)
        {
            string path=ResourcePath(identity);if(path==null)return null;
            if(references.TryGetValue(path,out var loaded))return loaded;
            loaded=Resources.Load<Sprite>(path);
            if(loaded==null)
            {
                var texture=Resources.Load<Texture2D>(path);
                if(texture!=null){loaded=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);loaded.name="Cached reference silhouette "+identity;}
            }
            references[path]=loaded;return loaded;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;
            if(reference!=null)
            {
                float aspect=reference.rect.width/reference.rect.height;
                float imageHeight=Mathf.Min(r.height,r.width/aspect),imageWidth=imageHeight*aspect;
                var imageRect=new Rect(r.center.x-imageWidth*.5f,r.center.y-imageHeight*.5f,imageWidth,imageHeight);
                var uv=UnityEngine.Sprites.DataUtility.GetOuterUV(reference);
                vh.AddVert(new Vector2(imageRect.xMin,imageRect.yMin),Color.black,new Vector2(uv.x,uv.y));
                vh.AddVert(new Vector2(imageRect.xMax,imageRect.yMin),Color.black,new Vector2(uv.z,uv.y));
                vh.AddVert(new Vector2(imageRect.xMax,imageRect.yMax),Color.black,new Vector2(uv.z,uv.w));
                vh.AddVert(new Vector2(imageRect.xMin,imageRect.yMax),Color.black,new Vector2(uv.x,uv.w));
                vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);return;
            }
            float h=Mathf.Min(r.height,r.width/.73f),w=h*.73f;
            fitted=new Rect(r.center.x-w*.5f,r.center.y-h*.5f,w,h);
            switch(key)
            {
                case "evan":Evan(vh);break;case "mira":Mira(vh);break;case "finn":Finn(vh);break;
                case "mage":Mage(vh);break;case "rogue":Rogue(vh);break;case "paladin":Paladin(vh);break;default:Fighter(vh);break;
            }
        }
        void Evan(VertexHelper v)
        {
            Ellipse(v,-.035f,.845f,.085f,.094f);Poly(v,-.12f,.90f,-.09f,.967f,.016f,.961f,.067f,.883f);
            Poly(v,-.17f,.76f,.086f,.766f,.18f,.56f,-.23f,.57f); // short cape
            Poly(v,-.098f,.706f,.083f,.708f,.112f,.36f,-.105f,.36f);
            Limb(v,-.066f,.38f,-.075f,.10f,.064f);Limb(v,.065f,.38f,.11f,.10f,.061f);
            Poly(v,-.113f,.12f,-.045f,.12f,-.031f,.056f,-.154f,.045f);Poly(v,.077f,.12f,.135f,.12f,.169f,.04f,.068f,.042f);
            Limb(v,-.12f,.675f,-.235f,.50f,.065f);Limb(v,.102f,.671f,.162f,.53f,.064f);Limb(v,.162f,.53f,.005f,.516f,.049f);
            Poly(v,-.28f,.61f,-.10f,.631f,-.07f,.43f,-.25f,.409f); // notebook outside body
            Limb(v,.001f,.52f,-.051f,.61f,.020f); // pen held above the book
        }
        void Mira(VertexHelper v)
        {
            Ellipse(v,.07f,.831f,.087f,.095f);Poly(v,.10f,.92f,.265f,.936f,.37f,.866f,.271f,.842f,.17f,.887f);
            Poly(v,-.015f,.756f,.17f,.751f,.19f,.52f,-.087f,.487f);
            Limb(v,-.006f,.701f,-.20f,.635f,.077f);Limb(v,-.20f,.635f,-.361f,.739f,.062f);
            Limb(v,.16f,.69f,.29f,.553f,.07f);Limb(v,.29f,.553f,.38f,.624f,.055f);
            Poly(v,-.087f,.525f,.19f,.54f,.263f,.374f,-.16f,.346f);
            Limb(v,-.053f,.38f,-.166f,.166f,.086f);Limb(v,.146f,.39f,.252f,.095f,.075f);
            Poly(v,-.209f,.20f,-.137f,.166f,-.176f,.052f,-.324f,.06f);Poly(v,.209f,.11f,.291f,.116f,.366f,.042f,.219f,.043f);
            Limb(v,-.355f,.68f,-.405f,.938f,.032f);Poly(v,-.435f,.928f,-.38f,.944f,-.44f,1.0f); // raised short sword
            Limb(v,-.417f,.739f,-.313f,.757f,.025f);
        }
        void Finn(VertexHelper v)
        {
            Ellipse(v,-.021f,.841f,.071f,.090f);Poly(v,-.09f,.867f,-.21f,.922f,-.09f,.815f);Poly(v,.037f,.865f,.166f,.918f,.046f,.82f);
            Poly(v,-.074f,.936f,.033f,.952f,.10f,.892f,-.135f,.881f);
            Poly(v,-.123f,.757f,.10f,.757f,.153f,.416f,-.155f,.422f);
            Poly(v,-.154f,.48f,.154f,.48f,.109f,.315f,.026f,.357f,-.075f,.307f,-.169f,.371f);
            Limb(v,-.102f,.70f,-.25f,.547f,.053f);Limb(v,-.25f,.547f,-.345f,.61f,.043f);
            Limb(v,.099f,.716f,.235f,.627f,.053f);Limb(v,.235f,.627f,.35f,.79f,.042f);
            Poly(v,.041f,.745f,.155f,.764f,.276f,.872f,.453f,.78f,.466f,.74f,.292f,.804f,.199f,.687f); // long ribbon
            Limb(v,-.079f,.353f,-.09f,.102f,.049f);Limb(v,.07f,.356f,.131f,.105f,.047f);
            Ellipse(v,-.108f,.067f,.071f,.027f);Ellipse(v,.147f,.066f,.063f,.026f);
            for(int i=0;i<5;i++){Ellipse(v,-.126f+i*.018f,.139f+(i%2)*.006f,.015f,.014f);Ellipse(v,.091f+i*.016f,.146f+(i%2)*.006f,.014f,.013f);}
        }
        void Fighter(VertexHelper v)
        {
            Poly(v,-.115f,.934f,.085f,.934f,.117f,.815f,.077f,.742f,-.071f,.742f,-.113f,.814f);
            Poly(v,-.227f,.749f,.194f,.749f,.166f,.409f,-.173f,.409f);
            Ellipse(v,-.212f,.718f,.090f,.087f);Ellipse(v,.198f,.72f,.089f,.087f);
            Limb(v,-.10f,.42f,-.123f,.092f,.113f);Limb(v,.093f,.417f,.121f,.092f,.11f);
            Poly(v,-.178f,.118f,-.078f,.115f,-.060f,.035f,-.219f,.036f);Poly(v,.068f,.11f,.17f,.11f,.222f,.033f,.063f,.035f);
            Limb(v,.208f,.694f,.306f,.498f,.077f);Limb(v,.305f,.502f,.331f,.681f,.061f);
            Poly(v,-.414f,.675f,-.254f,.724f,-.104f,.674f,-.122f,.405f,-.254f,.292f,-.393f,.41f); // wide shield
            Limb(v,.336f,.438f,.336f,.846f,.030f);Poly(v,.313f,.849f,.359f,.849f,.336f,.95f);Limb(v,.273f,.566f,.397f,.566f,.028f);
        }
        void Mage(VertexHelper v)
        {
            Poly(v,-.19f,.868f,-.024f,.993f,.119f,.864f);Poly(v,-.234f,.849f,.171f,.849f,.14f,.81f,-.195f,.812f);
            Ellipse(v,-.027f,.765f,.078f,.088f);
            Poly(v,-.137f,.696f,.111f,.70f,.235f,.059f,-.276f,.061f);
            Poly(v,-.111f,.658f,-.28f,.455f,-.383f,.503f,-.253f,.687f);
            Poly(v,.096f,.682f,.25f,.508f,.361f,.584f,.384f,.54f,.248f,.438f,.081f,.54f);
            Limb(v,.35f,.06f,.35f,.854f,.035f);Ellipse(v,.35f,.90f,.091f,.074f);Poly(v,.35f,.959f,.423f,.91f,.35f,.845f,.278f,.91f);
        }
        void Rogue(VertexHelper v)
        {
            Poly(v,-.073f,.954f,.078f,.912f,.133f,.762f,-.127f,.754f,-.118f,.878f);
            Poly(v,-.172f,.77f,.125f,.742f,.223f,.34f,-.067f,.364f,-.258f,.526f);
            Poly(v,-.118f,.704f,.083f,.706f,.15f,.43f,-.123f,.419f);
            Limb(v,-.074f,.453f,-.198f,.219f,.081f);Limb(v,-.198f,.219f,-.127f,.074f,.073f);
            Limb(v,.087f,.443f,.251f,.251f,.081f);Limb(v,.25f,.253f,.218f,.077f,.072f);
            Poly(v,-.167f,.087f,-.105f,.087f,-.03f,.025f,-.185f,.032f);Poly(v,.182f,.09f,.25f,.09f,.317f,.028f,.174f,.032f);
            Limb(v,-.135f,.668f,-.291f,.455f,.067f);Limb(v,.097f,.678f,.286f,.51f,.065f);
            Poly(v,-.323f,.476f,-.37f,.49f,-.421f,.771f);Poly(v,.29f,.493f,.345f,.489f,.434f,.742f);
            Limb(v,-.368f,.49f,-.284f,.463f,.025f);Limb(v,.272f,.496f,.36f,.516f,.025f);
        }
        void Paladin(VertexHelper v)
        {
            Poly(v,-.22f,.717f,.19f,.717f,.328f,.06f,-.332f,.076f); // heavy cloak
            Poly(v,-.117f,.90f,.09f,.908f,.136f,.805f,.072f,.735f,-.084f,.735f,-.13f,.81f);
            Poly(v,-.07f,.903f,-.012f,.982f,.039f,.906f); // crest
            Poly(v,-.235f,.745f,.192f,.745f,.168f,.426f,-.164f,.418f);
            Poly(v,-.304f,.744f,-.137f,.792f,-.084f,.661f,-.295f,.636f);Poly(v,.099f,.76f,.259f,.75f,.295f,.635f,.113f,.653f);
            Limb(v,-.09f,.421f,-.114f,.112f,.121f);Limb(v,.086f,.43f,.139f,.116f,.12f);
            Poly(v,-.18f,.148f,-.051f,.124f,-.064f,.033f,-.242f,.034f);Poly(v,.07f,.127f,.197f,.153f,.257f,.034f,.066f,.033f);
            Limb(v,-.216f,.672f,-.085f,.519f,.085f);Limb(v,.206f,.67f,.05f,.497f,.086f);
            Limb(v,-.048f,.363f,.314f,.866f,.075f);Poly(v,.272f,.856f,.34f,.912f,.407f,1,.347f,.808f); // broad diagonal greatsword
            Limb(v,-.058f,.524f,.109f,.424f,.052f);Limb(v,-.096f,.32f,.031f,.493f,.044f);
        }
        Vector2 P(float x,float y)=>new Vector2(fitted.center.x+x*fitted.width,fitted.y+y*fitted.height);
        void Poly(VertexHelper v,params float[] points)
        {
            int n=points.Length/2;if(n<3)return;int first=v.currentVertCount;
            for(int i=0;i<n;i++)v.AddVert(P(points[i*2],points[i*2+1]),Color.black,Vector2.zero);
            for(int i=1;i<n-1;i++)v.AddTriangle(first,first+i,first+i+1);
        }
        void Limb(VertexHelper v,float ax,float ay,float bx,float by,float width)
        {
            Vector2 a=new Vector2(ax,ay),b=new Vector2(bx,by),n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
            Poly(v,a.x-n.x,a.y-n.y,b.x-n.x,b.y-n.y,b.x+n.x,b.y+n.y,a.x+n.x,a.y+n.y);
            Ellipse(v,ax,ay,width*.48f,width*.40f);Ellipse(v,bx,by,width*.48f,width*.40f);
        }
        void Ellipse(VertexHelper v,float x,float y,float rx,float ry)
        {
            int first=v.currentVertCount;v.AddVert(P(x,y),Color.black,Vector2.zero);
            const int sides=12;for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;v.AddVert(P(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry),Color.black,Vector2.zero);}
            for(int i=0;i<sides;i++)v.AddTriangle(first,first+1+i,first+1+(i+1)%sides);
        }
    }
}
