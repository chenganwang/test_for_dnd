using UnityEngine;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>A projected, flat-shaded icosahedron. Geometry and labels are allocated once.
    /// Numbered faces stay fixed; the saved raw face rotates toward the viewer at rest.</summary>
    public sealed class D20Graphic : MaskableGraphic
    {
        static readonly int[] Faces = {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        readonly Vector3[] vertices = new Vector3[12], projected = new Vector3[12];
        readonly Vector3[] normals = new Vector3[20], centers = new Vector3[20];
        readonly int[] painterOrder = new int[20];
        readonly Text[] labels = new Text[20];
        Quaternion settledRotation;
        Quaternion pose;
        float emphasis = 1;
        bool settled;
        bool initialized;
        int rawFace;
        bool poseDirty=true;
        Vector2 motionOffset;
        Color resultColor=new Color(.82f,.65f,.31f);
        float previousRadius=-1;
        public int DisplayedRaw => rawFace;
        public int FaceCount => 20;
        public Quaternion Pose => pose;
        public float Emphasis => emphasis;
        public Vector3 FrontFaceNormal => pose * normals[Mathf.Max(0,rawFace-1)];
        public Vector2 MotionOffset => motionOffset;
        public int MeshBuildCount { get; private set; }
        public int AllocatedFaceLabels => initialized ? labels.Length : 0;

        public void Initialize(Font font)
        {
            if (initialized) return;
            raycastTarget = false;
            float phi = (1 + Mathf.Sqrt(5)) / 2;
            var points = new[] {new Vector3(-1,phi,0),new Vector3(1,phi,0),new Vector3(-1,-phi,0),new Vector3(1,-phi,0),
                new Vector3(0,-1,phi),new Vector3(0,1,phi),new Vector3(0,-1,-phi),new Vector3(0,1,-phi),
                new Vector3(phi,0,-1),new Vector3(phi,0,1),new Vector3(-phi,0,-1),new Vector3(-phi,0,1)};
            for (int i=0;i<12;i++) vertices[i] = points[i].normalized;
            for (int i=0;i<20;i++)
            {
                var a=vertices[Faces[i*3]]; var b=vertices[Faces[i*3+1]]; var c=vertices[Faces[i*3+2]];
                centers[i]=(a+b+c)/3;
                normals[i]=Vector3.Cross(b-a,c-a).normalized;
                if(Vector3.Dot(normals[i],centers[i])<0) normals[i]=-normals[i];
                painterOrder[i]=i;
                var obj=new GameObject("D20 face "+(i+1),typeof(RectTransform),typeof(Text));obj.transform.SetParent(transform,false);
                var text=labels[i]=obj.GetComponent<Text>();text.font=font;text.fontSize=28;text.fontStyle=FontStyle.Bold;
                text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
                text.rectTransform.sizeDelta=new Vector2(80,42);
                text.text=(i+1).ToString();
            }
            settledRotation=Quaternion.FromToRotation(normals[0],Vector3.back);
            initialized=true;SetRawFace(1);SetPose(0,0,1);
        }
        public void SetRawFace(int raw)
        {
            raw=Mathf.Clamp(raw,1,20);if(rawFace==raw)return;rawFace=raw;
            settledRotation=Quaternion.FromToRotation(normals[raw-1],Vector3.back);poseDirty=true;
        }
        public void SetResultAppearance(bool success)
        {
            Color next=rawFace==20?new Color(1,.77f,.27f):rawFace==1?new Color(.38f,.51f,.65f):success?new Color(.83f,.67f,.36f):new Color(.54f,.25f,.26f);
            if(next==resultColor)return;resultColor=next;poseDirty=true;
        }
        protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();poseDirty=true;}
        public static Vector2 FlightOffset(float progress,int lane)
        {
            float p=Mathf.Clamp01(progress/DicePresentationLayer.SettleFraction);
            if(p>=1)return Vector2.zero;
            float height;
            if(p<.22f)height=Mathf.Sin(p/.22f*Mathf.PI)*58;
            else if(p<.52f)height=Mathf.Sin((p-.22f)/.30f*Mathf.PI)*25;
            else if(p<.8f)height=Mathf.Sin((p-.52f)/.28f*Mathf.PI)*10;
            else height=Mathf.Sin((p-.8f)/.2f*Mathf.PI)*3;
            return new Vector2((lane==0?-1:1)*92*Mathf.Pow(1-p,2.4f),height);
        }
        public void SetPose(float progress,int lane,float brightness)
        {
            if(!initialized)return;
            float p=Mathf.Clamp01(progress/DicePresentationLayer.SettleFraction);
            float spin=1-Mathf.Pow(1-p,2.4f);
            var rolling=Quaternion.Euler((1-spin)*1120+lane*23*(1-p),(1-spin)*850,(1-spin)*430)*settledRotation;
            Quaternion next=p>=1?settledRotation:rolling;
            float radius=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.43f;
            Vector2 offset=FlightOffset(progress,lane)*(radius/84);
            bool changed=poseDirty||Quaternion.Angle(pose,next)>.001f||Mathf.Abs(emphasis-brightness)>.001f||settled!=(p>=1)||radius!=previousRadius||offset!=motionOffset;
            if(!changed)return;
            poseDirty=false;previousRadius=radius;motionOffset=offset;
            pose=next;emphasis=brightness;settled=p>=1;
            Vector3 origin=rectTransform.rect.center+motionOffset;
            for(int i=0;i<12;i++) projected[i]=pose*vertices[i]*radius+origin;
            for(int i=0;i<20;i++)
            {
                Vector3 n=pose*normals[i],c=pose*centers[i]*radius;
                bool visible=n.z<-.12f;
                if(labels[i].gameObject.activeSelf!=visible) labels[i].gameObject.SetActive(visible);
                if(!visible)continue;
                labels[i].rectTransform.anchoredPosition=new Vector2(c.x,c.y)+motionOffset;
                labels[i].rectTransform.localScale=new Vector3(.72f,Mathf.Clamp(-n.z,.25f,1)*.72f,1);
                float edge=Mathf.Clamp01((-n.z-.12f)/.42f);
                bool adoptedFace=settled&&i==rawFace-1;
                labels[i].color=adoptedFace?new Color(1,.94f,.76f,edge*brightness):new Color(.70f,.66f,.55f,edge*brightness*(settled?.6f:1));
            }
            if(changed)SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!initialized)return;MeshBuildCount++;
            Vector3 shadow=rectTransform.rect.center;shadow.y-=previousRadius*.88f;
            float shadowWidth=previousRadius*.80f*(1-motionOffset.y/200);
            for(int i=0;i<20;i++){float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;int v=vh.currentVertCount;Color c=new Color(.015f,.012f,.009f,.24f*emphasis);vh.AddVert(shadow,c,Vector2.zero);vh.AddVert(shadow+new Vector3(Mathf.Cos(a)*shadowWidth,Mathf.Sin(a)*previousRadius*.16f,0),c,Vector2.zero);vh.AddVert(shadow+new Vector3(Mathf.Cos(b)*shadowWidth,Mathf.Sin(b)*previousRadius*.16f,0),c,Vector2.zero);vh.AddTriangle(v,v+1,v+2);}
            // Only twenty faces; insertion sorting avoids per-frame enumerable/array allocations.
            for(int i=1;i<20;i++) {int key=painterOrder[i],j=i-1;float z=(pose*centers[key]).z;while(j>=0&&(pose*centers[painterOrder[j]]).z<z){painterOrder[j+1]=painterOrder[j];j--;}painterOrder[j+1]=key;}
            for(int order=0;order<20;order++)
            {
                int i=painterOrder[order];Vector3 n=pose*normals[i];if(n.z>=0)continue;
                var a=projected[Faces[i*3]];var b=projected[Faces[i*3+1]];var c=projected[Faces[i*3+2]];
                float light=.37f+.50f*Mathf.Clamp01(Vector3.Dot(n,new Vector3(-.35f,.55f,-1).normalized));
                bool front=settled&&i==rawFace-1;
                Color face=new Color(.23f*light,.20f*light,.15f*light,1)*new Color(emphasis,emphasis,emphasis,1);
                if(front)face=Color.Lerp(face,new Color(resultColor.r*emphasis,resultColor.g*emphasis,resultColor.b*emphasis,1),.56f);
                int start=vh.currentVertCount;vh.AddVert(a,face,Vector2.zero);vh.AddVert(b,face,Vector2.zero);vh.AddVert(c,face,Vector2.zero);vh.AddTriangle(start,start+1,start+2);
                Color edge=front?new Color(resultColor.r*emphasis,resultColor.g*emphasis,resultColor.b*emphasis,1):new Color(.48f*emphasis,.39f*emphasis,.25f*emphasis,1);Edge(vh,a,b,edge);Edge(vh,b,c,edge);Edge(vh,c,a,edge);
            }
        }
        static void Edge(VertexHelper vh,Vector3 a,Vector3 b,Color color)
        {
            Vector3 offset=Vector3.Cross((b-a).normalized,Vector3.forward)*.7f;int start=vh.currentVertCount;
            vh.AddVert(a-offset,color,Vector2.zero);vh.AddVert(a+offset,color,Vector2.zero);vh.AddVert(b+offset,color,Vector2.zero);vh.AddVert(b-offset,color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
    }
}
