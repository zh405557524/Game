using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectRealm.Presentation.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ProjectRealm.EditorTools
{
    public static class TerrainDebugSceneBuilder
    {
        private const string Textures = "Assets/ProjectRealm/Presentation/Map/Materials/Textures";
        private static readonly string[] Painted = { "terrain-plain-v1", "terrain-hills-v1", "terrain-mountain-v1", "terrain-plateau-v1", "terrain-basin-v1" };
        private static readonly string[] Micro = { "terrain-plain-v2", "terrain-hills-v4", "terrain-mountain-v4", "terrain-plateau-v1", "terrain-basin-v2" };

        // Layer 0 may also contain look-development candidates. Only these original IDs belong to the five-terrain builder.
        public static bool IsBaseTerrainCase(MapDebugCase item) => item != null && item.layer == 0 &&
            item.id != null && item.id.StartsWith("terrain/", StringComparison.Ordinal) &&
            Array.IndexOf(ProjectRealmWorkspaceLayout.TerrainFolders, item.id.Substring("terrain/".Length)) >= 0;

        [MenuItem("Project Realm/Debug/Create Missing Single Terrain Scenes")]
        public static void CreateMissing()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            ProjectRealmWorkspaceLayout.EnsureLayout();
            var catalog=MapDebugWorkbench.LoadCatalog();
            foreach(var item in catalog.cases.Where(IsBaseTerrainCase))
            {
                if(File.Exists(item.scenePath))continue;
                int kind=Array.IndexOf(ProjectRealmWorkspaceLayout.TerrainFolders,item.id.Substring("terrain/".Length));
                Create(item,kind);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(catalog.cases.First(x=>x.id=="terrain/03_Mountain").scenePath);
            var gameType=Type.GetType("UnityEditor.GameView,UnityEditor");
            if(gameType!=null)EditorWindow.GetWindow(gameType).Show();
            Debug.Log("Five single-terrain scenes ready. Inputs and generated outputs are separate. Visual reviews remain pending.");
        }

        private static void Create(MapDebugCase item,int kind)
        {
            var shader=Shader.Find("ProjectRealm/Map/TerrainDebugSurface");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Debug shader failed.");
            ProjectRealmWorkspaceLayout.EnsureFolder(item.testDataPath);
            ProjectRealmWorkspaceLayout.EnsureFolder(item.generatedPath);
            ProjectRealmWorkspaceLayout.EnsureFolder(Path.GetDirectoryName(item.scenePath).Replace('\\','/'));
            var geometry=AssetDatabase.LoadAssetAtPath<FiveTerrainDefinition>(item.testDataPath+"/Geometry.asset");
            if(geometry==null)
            {
                geometry=ScriptableObject.CreateInstance<FiveTerrainDefinition>();
                AssetDatabase.CreateAsset(geometry,item.testDataPath+"/Geometry.asset");
            }
            var settings=AssetDatabase.LoadAssetAtPath<TerrainDebugSettings>(item.testDataPath+"/DebugSettings.asset");
            if(settings==null)
            {
                settings=ScriptableObject.CreateInstance<TerrainDebugSettings>();settings.kind=(LandformKind)kind;settings.geometry=geometry;
                settings.paintedTexture=AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{Painted[kind]}.png");
                settings.microTexture=AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{Micro[kind]}.png");
                settings.textureWorldSize=40;settings.cameraSize=68;settings.pitch=48;
                AssetDatabase.CreateAsset(settings,item.testDataPath+"/DebugSettings.asset");
            }
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.generatedPath+"/Terrain.asset");
            if(mesh==null){mesh=geometry.BuildIsolated((LandformKind)kind);AssetDatabase.CreateAsset(mesh,item.generatedPath+"/Terrain.asset");}
            var material=AssetDatabase.LoadAssetAtPath<Material>(item.generatedPath+"/Surface.mat");
            if(material==null)
            {
                material=new Material(shader){name=Painted[kind]+" RGB debug"};
                material.SetTexture("_BaseMap",settings.paintedTexture);material.SetFloat("_WorldSize",settings.textureWorldSize);
                AssetDatabase.CreateAsset(material,item.generatedPath+"/Surface.mat");
            }
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.skybox=null;RenderSettings.fog=false;
                var ground=new GameObject(item.displayName+" / isolated",typeof(MeshFilter),typeof(MeshRenderer));
                ground.GetComponent<MeshFilter>().sharedMesh=mesh;
                ground.GetComponent<MeshRenderer>().sharedMaterial=material;
                var cameraObject=new GameObject("Debug Camera",typeof(Camera),typeof(AudioListener),typeof(TerrainDebugView));
                cameraObject.tag="MainCamera";
                var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(0.89f,0.88f,0.81f);camera.orthographic=true;
                camera.orthographicSize=settings.cameraSize;camera.nearClipPlane=0.3f;camera.farClipPlane=500;camera.allowHDR=false;
                var rotation=Quaternion.Euler(settings.pitch,0,0);
                camera.transform.SetPositionAndRotation(new Vector3(0,9,0)-rotation*Vector3.forward*230,rotation);
                var controller=cameraObject.GetComponent<TerrainDebugView>();controller.settings=settings;controller.terrain=ground.GetComponent<MeshRenderer>();
                EditorSceneManager.SaveScene(scene,item.scenePath);
                item.findings="独立场景已建立。用白模、原色贴图、光照三种模式定位问题；尚未人工确认。";
                EditorUtility.SetDirty(MapDebugWorkbench.LoadCatalog());
            }
            finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
        }

        [MenuItem("Project Realm/Debug/Export Current Terrain Diagnostics")]
        public static void ExportCurrent()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before exporting.");
            var source=UnityEngine.Object.FindFirstObjectByType<TerrainDebugView>();
            if(source==null)throw new InvalidOperationException("Open one single-terrain scene first.");
            var settings=source.settings;
            string folder=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试",settings.kind.ToString(),DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(folder);
            var oldMaterial=source.terrain.sharedMaterial;
            var material=new Material(oldMaterial){hideFlags=HideFlags.HideAndDontSave};
            var go=new GameObject("Temporary diagnostic camera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.CopyFrom(source.GetComponent<Camera>());camera.enabled=false;
            camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            var record=new List<string>{"name,source,world_units_per_tile,mode,projection,camera_size"};
            source.terrain.sharedMaterial=material;
            try
            {
                // Do not inherit an edited preview material's texture, tiling or projection.
                material.SetTexture("_BaseMap",settings.paintedTexture);
                material.SetFloat("_WorldSize",40);material.SetFloat("_Triplanar",1);
                material.SetFloat("_Mode",0);Capture("01-clay",0,"clay");
                material.SetFloat("_Mode",1);material.SetFloat("_WorldSize",40);Capture("02-painted-unlit",1,"painted");
                material.SetTexture("_BaseMap",settings.microTexture);Capture("03-micro-unlit",1,"micro");
                material.SetTexture("_BaseMap",settings.paintedTexture);material.SetFloat("_Mode",2);
                foreach(float size in new[]{20f,40f,80f}){material.SetFloat("_WorldSize",size);Capture($"04-painted-{size:0}",2,"painted");}
                material.SetFloat("_WorldSize",40);camera.orthographicSize=35;Capture("05-painted-near",2,"painted");
                File.WriteAllLines(Path.Combine(folder,"conditions.csv"),record);
                var item=MapDebugWorkbench.LoadCatalog().cases.First(x=>IsBaseTerrainCase(x)&&x.id.EndsWith(ProjectRealmWorkspaceLayout.TerrainFolders[(int)settings.kind],StringComparison.Ordinal));
                item.state=DebugReviewState.InProgress;item.evidencePath=folder;
                item.findings="已导出白模、原色水墨图、细底纹、20/40/80 单位铺设比例和近景。几何与光照条件固定；等待逐张检查，不代表视觉通过。";
                EditorUtility.SetDirty(MapDebugWorkbench.LoadCatalog());AssetDatabase.SaveAssets();
                Debug.Log("Single terrain diagnostics: "+folder);
            }
            finally{source.terrain.sharedMaterial=oldMaterial;UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(go);}

            void Capture(string name,int mode,string texture)
            {
                Render(camera,1440,1000,Path.Combine(folder,name+".png"));
                string sourceName=texture=="clay"?"none":(texture=="micro"?settings.microTexture.name:settings.paintedTexture.name);
                record.Add($"{name},{sourceName},{material.GetFloat("_WorldSize")},{mode},triplanar,{camera.orthographicSize}");
            }
        }

        private static void Render(Camera camera,int width,int height,string path)
        {
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;Texture2D image=null;
            try
            {
                camera.aspect=(float)width/height;
                var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("URP render request not supported.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;
                image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;if(image!=null)UnityEngine.Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
        }
    }
}
