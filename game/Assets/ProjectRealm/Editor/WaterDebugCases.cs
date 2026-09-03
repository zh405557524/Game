using System;
using System.IO;
using UnityEditor;

namespace ProjectRealm.EditorTools
{
    public static class WaterDebugCases
    {
        public static readonly string[] Folders = { "01_River", "02_Stream", "03_Lake", "04_Pond", "05_Wetland", "06_Coast" };
        public static readonly string[] Names = { "河流", "溪流", "湖泊", "池塘", "湿地", "海岸" };
        public const string RiverScene = ProjectRealmWorkspaceLayout.DebugScenes + "/02_Water/01_River/RiverStudy.unity";
        public const string RiverData = ProjectRealmWorkspaceLayout.TestData + "/Map/02_Water/01_River";
        public const string RiverOutput = ProjectRealmWorkspaceLayout.Generated + "/Map/02_Water/01_River";

        public static bool Ensure(MapDebugCatalog catalog)
        {
            bool changed = false;
            for (int i = 0; i < Folders.Length; i++)
            {
                string id = "water/" + Folders[i];
                var existing = catalog.cases.Find(x => x.id == id);
                string builtScene = i == 0 ? RiverScene : ProjectRealmWorkspaceLayout.DebugScenes + "/02_Water/" + Folders[i] + "/" + Folders[i].Substring(3) + "Study.unity";
                if (existing != null)
                {
                    if (string.IsNullOrEmpty(existing.scenePath) && File.Exists(builtScene)) { existing.scenePath = builtScene; changed = true; }
                    continue;
                }
                var item = i == 0 ? catalog.cases.Find(x => x.id == "02_Water" && string.IsNullOrEmpty(x.scenePath)) : null;
                if (item == null) { item = new MapDebugCase(); catalog.cases.Add(item); }
                item.id = id; item.displayName = Names[i]; item.layer = 1;
                item.scenePath = i == 0 || File.Exists(builtScene) ? builtScene : "";
                item.testDataPath = ProjectRealmWorkspaceLayout.TestData + "/Map/02_Water/" + Folders[i];
                item.generatedPath = ProjectRealmWorkspaceLayout.Generated + "/Map/02_Water/" + Folders[i];
                if (string.IsNullOrEmpty(item.findings) || item.findings.StartsWith("分类已预留", StringComparison.Ordinal))
                    item.findings = i == 0 ? "按水系设计图制作独立河流样板；尚未视觉确认。" : "分类入口已建立；尚未制作独立场景，不代表已实现。";
                ProjectRealmWorkspaceLayout.EnsureFolder(ProjectRealmWorkspaceLayout.DebugScenes + "/02_Water/" + Folders[i]);
                ProjectRealmWorkspaceLayout.EnsureFolder(item.testDataPath);
                ProjectRealmWorkspaceLayout.EnsureFolder(item.generatedPath);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(catalog);
            return changed;
        }
    }
}
