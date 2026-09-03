using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    /// <summary>Development assets are not production definitions or save-game data.</summary>
    public static class ProjectRealmWorkspaceLayout
    {
        public const string DebugScenes = "Assets/Scenes/Debug/Map";
        public const string LearningScene = "Assets/Scenes/Learning/MapLearning/MapLearning.unity";
        public const string IntegrationScene = DebugScenes + "/90_Integration/FiveTerrainMap/FiveTerrainMap.unity";
        public const string PrototypeScene = DebugScenes + "/90_Integration/CountyMapPrototype/CountyMapPrototype.unity";
        public const string MaterialsScene = DebugScenes + "/91_Materials/CountyMapMaterialReview.unity";
        public const string Development = "Assets/ProjectRealm/Development";
        public const string TestData = Development + "/TestData";
        public const string Generated = Development + "/Generated";
        public const string ArchiveData = Development + "/Archive";
        public const string ArchiveScenes = "Assets/Scenes/Archive/Map/FiveTerrainMap";
        public const string StudyDefinition = TestData + "/Map/90_Integration/FiveTerrainMap/FiveTerrainStudy.asset";
        public const string PrototypeDefinition = TestData + "/Map/90_Integration/CountyMapPrototype/CountyMapPrototype.asset";
        public const string TerrainData = TestData + "/Learning/MapLearning/Terrain/MapLearningTerrain.asset";
        public const string TerrainLayers = TestData + "/Learning/MapLearning/TerrainLayers";
        public const string CombinedGenerated = Generated + "/Map/90_Integration/FiveTerrainMap";

        public static readonly string[] LayerFolders = { "01_Terrain", "02_Water", "03_SoilGeology", "04_Vegetation", "05_UndergroundResources", "06_LandUse", "07_Crops", "08_Transport", "09_Settlements", "10_AdministrativeBorders", "11_DynamicState", "12_StrategicInformation" };
        public static readonly string[] LayerNames = { "地形", "水系", "土壤与地质", "植被", "地下资源", "土地利用", "农作物", "交通", "聚落建筑", "行政边界", "动态状态", "战略信息" };
        public static readonly string[] TerrainFolders = { "01_Plain", "02_Hills", "03_Mountain", "04_Plateau", "05_Basin" };

        [Serializable] public sealed class MoveRecord { public string from, to, guid; }
        [Serializable] public sealed class FileRecord { public string path, sha256; }
        [Serializable] private sealed class Manifest { public string createdAt; public List<MoveRecord> moves; public List<FileRecord> files; }

        private static readonly string[,] Moves =
        {
            { "Assets/Scenes/MapLearning.unity", LearningScene },
            { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/Learning/Template/SampleScene.unity" },
            { "Assets/Scenes/CountyMapPrototype.unity", PrototypeScene },
            { "Assets/Scenes/CountyMapMaterialReview.unity", MaterialsScene },
            { "Assets/Scenes/FiveTerrainMap.unity", IntegrationScene },
            { "Assets/Scenes/FiveTerrainBackups", ArchiveScenes },
            { "Assets/New Terrain.asset", TerrainData },
            { "Assets/Scenes/NewLayer.terrainlayer", TerrainLayers + "/BasinPractice.terrainlayer" },
            { "Assets/ProjectRealm/Presentation/Map/Materials/Textures/NewLayer.terrainlayer", TerrainLayers + "/PlainPractice.terrainlayer" },
            { "Assets/ProjectRealm/Presentation/Map/Materials/Textures/NewLayer 1.terrainlayer", TerrainLayers + "/HillsPractice.terrainlayer" },
            { "Assets/ProjectRealm/Content/Maps/FiveTerrainStudy.asset", StudyDefinition },
            { "Assets/ProjectRealm/Content/Maps/CountyMapPrototype.asset", PrototypeDefinition },
            { "Assets/ProjectRealm/Presentation/Map/FiveTerrainGenerated", CombinedGenerated }
        };

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (string.IsNullOrEmpty(guid)) throw new IOException($"Could not create {path}");
        }

        public static void EnsureLayout()
        {
            EnsureFolder("Assets/Scenes/Runtime");
            EnsureFolder("Assets/ProjectRealm/Tests/Fixtures");
            EnsureFolder(Development + "/Catalog");
            EnsureFolder(ArchiveData);
            foreach (string layer in LayerFolders) EnsureFolder($"{DebugScenes}/{layer}");
            foreach (string terrain in TerrainFolders)
            {
                EnsureFolder($"{DebugScenes}/01_Terrain/{terrain}");
                EnsureFolder($"{TestData}/Map/01_Terrain/{terrain}");
                EnsureFolder($"{Generated}/Map/01_Terrain/{terrain}");
            }
            EnsureFolder(DebugScenes + "/90_Integration");
            EnsureFolder(DebugScenes + "/91_Materials");
        }

        [MenuItem("Project Realm/Workspace/Organize Scenes and Test Data")]
        public static void Organize()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var records = new List<MoveRecord>();
            for (int i = 0; i < Moves.GetLength(0); i++)
            {
                string from = Moves[i, 0], to = Moves[i, 1];
                string guid = AssetDatabase.AssetPathToGUID(from);
                if (string.IsNullOrEmpty(guid)) continue;
                if (File.Exists(to) || Directory.Exists(to)) throw new IOException($"Destination exists; nothing moved: {to}");
                records.Add(new MoveRecord { from = from, to = to, guid = guid });
            }
            if (records.Count == 0)
            {
                EnsureLayout();
                MapDebugWorkbench.Open();
                Debug.Log("Workspace layout already organized; no assets moved and no duplicate snapshot created.");
                return;
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            var setupGuids = setup.Select(s => AssetDatabase.AssetPathToGUID(s.path)).ToArray();
            var buildScenes = EditorBuildSettings.scenes;
            var originalBuildScenes = EditorBuildSettings.scenes;
            var buildGuids = buildScenes.Select(s => AssetDatabase.AssetPathToGUID(s.path)).ToArray();
            string backup = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../builds/workspace-layout", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            var manifest = new Manifest { createdAt = DateTime.Now.ToString("O"), moves = records, files = new List<FileRecord>() };
            Directory.CreateDirectory(backup);
            foreach (var record in records)
            {
                var files = Directory.Exists(record.from) ? Directory.GetFiles(record.from, "*", SearchOption.AllDirectories).ToList() : new List<string> { record.from };
                if (File.Exists(record.from + ".meta")) files.Add(record.from + ".meta");
                foreach (string path in files)
                {
                    string destination = Path.Combine(backup, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(path, destination, false);
                    using var hash = SHA256.Create(); using var stream = File.OpenRead(path);
                    manifest.files.Add(new FileRecord { path = path, sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() });
                }
            }
            string buildSettings = "ProjectSettings/EditorBuildSettings.asset";
            Directory.CreateDirectory(Path.Combine(backup, "ProjectSettings"));
            File.Copy(buildSettings, Path.Combine(backup, buildSettings));
            File.WriteAllText(Path.Combine(backup, "manifest.json"), JsonUtility.ToJson(manifest, true));
            var completed = new List<MoveRecord>();
            try
            {
                EnsureLayout();
                foreach (var record in records)
                {
                    EnsureFolder(Path.GetDirectoryName(record.to).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(record.from, record.to);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    completed.Add(record);
                    if (AssetDatabase.AssetPathToGUID(record.to) != record.guid) throw new IOException($"GUID changed: {record.to}");
                }
                for (int i = 0; i < buildScenes.Length; i++)
                    if (!string.IsNullOrEmpty(buildGuids[i])) buildScenes[i].path = AssetDatabase.GUIDToAssetPath(buildGuids[i]);
                EditorBuildSettings.scenes = buildScenes;
                for (int i = 0; i < setup.Length; i++)
                    if (!string.IsNullOrEmpty(setupGuids[i])) setup[i].path = AssetDatabase.GUIDToAssetPath(setupGuids[i]);
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Path.Combine(backup, "result.txt"), $"Moved {completed.Count} targets; all GUIDs preserved. No assets deleted.");
                Debug.Log($"Workspace layout: {completed.Count} targets moved with GUIDs preserved. Recovery snapshot: {backup}");
                MapDebugWorkbench.Open();
            }
            catch
            {
                for (int i = completed.Count - 1; i >= 0; i--)
                {
                    string error = AssetDatabase.MoveAsset(completed[i].to, completed[i].from);
                    if (!string.IsNullOrEmpty(error)) Debug.LogError($"Rollback needs attention: {error}. Snapshot: {backup}");
                }
                EditorBuildSettings.scenes = originalBuildScenes;
                // Resolve by GUID even if one reverse move needs manual recovery.
                for (int i = 0; i < originalSetup.Length; i++)
                    if (!string.IsNullOrEmpty(setupGuids[i])) originalSetup[i].path = AssetDatabase.GUIDToAssetPath(setupGuids[i]);
                if (originalSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                throw;
            }
        }
    }
}
