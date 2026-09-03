using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.Presentation.Map
{
    [RequireComponent(typeof(Camera))]
    public sealed class TerrainDebugView : MonoBehaviour
    {
        public TerrainDebugSettings settings;
        public MeshRenderer terrain;
        private Material instance;
        private Material original;
        private Camera view;
        private int mode;
        private bool micro, triplanar = true;
        private float worldSize, pitch, zoom;

        private void Start()
        {
            view=GetComponent<Camera>();
            original=terrain.sharedMaterial;instance=new Material(original);terrain.sharedMaterial=instance;
            worldSize=settings.textureWorldSize;pitch=settings.pitch;zoom=settings.cameraSize;
            Apply();
        }
        private void OnDestroy()
        {
            if(terrain!=null && original!=null)terrain.sharedMaterial=original;
            if(instance!=null)Destroy(instance);
        }
        private void Update()
        {
            if(instance==null)return;
            var keyboard=Keyboard.current;
            if(keyboard!=null)
            {
                if(keyboard.digit1Key.wasPressedThisFrame)mode=0;
                if(keyboard.digit2Key.wasPressedThisFrame)mode=1;
                if(keyboard.digit3Key.wasPressedThisFrame)mode=2;
                if(keyboard.tKey.wasPressedThisFrame)micro=!micro;
                if(keyboard.fKey.wasPressedThisFrame){pitch=settings.pitch;zoom=settings.cameraSize;}
            }
            var mouse=Mouse.current;
            if(mouse!=null && mouse.position.ReadValue().y>Screen.height*0.16f && mouse.position.ReadValue().y<Screen.height*0.86f)
            {
                float scroll=mouse.scroll.ReadValue().y;
                if(keyboard!=null && keyboard.shiftKey.isPressed)zoom=Mathf.Clamp(zoom*Mathf.Exp(-scroll*0.0015f),15,100);
                else pitch=Mathf.Clamp(pitch+scroll*0.025f,30,75);
            }
            Apply();
        }
        private void Apply()
        {
            instance.SetTexture("_BaseMap",micro?settings.microTexture:settings.paintedTexture);
            instance.SetFloat("_WorldSize",worldSize);instance.SetFloat("_Mode",mode);instance.SetFloat("_Triplanar",triplanar?1:0);
            view.orthographicSize=zoom;
            var rotation=Quaternion.Euler(pitch,0,0);
            transform.SetPositionAndRotation(new Vector3(0,9,0)-rotation*Vector3.forward*230,rotation);
        }
        private void OnGUI()
        {
            if(settings==null || instance==null)return;
            float scale=Mathf.Min(Screen.width/1200f,Screen.height/760f);
            var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width=Screen.width/scale,height=Screen.height/scale;
            // Keep the editor skin's font: dynamic macOS PingFang fails in this editor's IMGUI text backend.
            var title=new GUIStyle(GUI.skin.label){fontSize=25};title.normal.textColor=new Color(0.2f,0.23f,0.2f);
            var text=new GUIStyle(title){fontSize=16};
            var button=new GUIStyle(GUI.skin.button){fontSize=16};
            GUI.Box(new Rect(15,12,width-30,91),GUIContent.none);
            GUI.Label(new Rect(31,21,520,34),FiveTerrainDefinition.Names[(int)settings.kind]+" · 单项调试（未验收）",title);
            string[] labels={"1 白模造型","2 原色贴图","3 加入光照"};
            for(int i=0;i<3;i++)
            {
                GUI.backgroundColor=mode==i?new Color(0.65f,0.85f,0.65f):Color.white;
                if(GUI.Button(new Rect(32+i*150,62,140,30),labels[i],button))mode=i;
            }
            GUI.backgroundColor=Color.white;
            GUI.Label(new Rect(width-400,27,360,54),"只测试当前一种地形\n不叠加其他地形、植被、水系",text);
            GUI.Box(new Rect(15,height-142,width-30,128),GUIContent.none);
            var texture=micro?settings.microTexture:settings.paintedTexture;
            GUI.Label(new Rect(32,height-135,width-60,25),"当前原图："+(texture!=null?texture.name:"missing"),text);
            if(GUI.Button(new Rect(32,height-101,170,34),micro?"T 切换水墨图":"T 切换细底纹",button))micro=!micro;
            if(GUI.Button(new Rect(212,height-101,170,34),triplanar?"投影：三向":"投影：俯视",button))triplanar=!triplanar;
            float[] sizes={20,40,80};
            for(int i=0;i<3;i++)if(GUI.Button(new Rect(396+i*125,height-101,115,34),sizes[i]+" 单位/张",button))worldSize=sizes[i];
            if(GUI.Button(new Rect(width-200,height-101,72,34),"近景",button))zoom=35;
            if(GUI.Button(new Rect(width-118,height-101,72,34),"全景",button))zoom=settings.cameraSize;
            GUI.Label(new Rect(32,height-56,width-50,35),$"当前覆盖 {worldSize:0} 单位/张 · 滚轮调角度 · Shift+滚轮缩放 · F 复位 · Play 中调整不写回配置",text);
            GUI.matrix=old;
        }
    }
}
