using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    public enum DebugReviewState { NotStarted, InProgress, WaitingReview, Approved }

    [Serializable]
    public sealed class MapDebugCase
    {
        public string id;
        public string displayName;
        public int layer;
        public string scenePath;
        public string testDataPath;
        public string generatedPath;
        public DebugReviewState state;
        [TextArea] public string findings;
        public string evidencePath;
    }

    // Editor-only catalog, deliberately outside formal Content definitions and saves.
    public sealed class MapDebugCatalog : ScriptableObject
    {
        public List<MapDebugCase> cases = new List<MapDebugCase>();
    }
}
