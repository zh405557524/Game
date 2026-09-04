namespace ProjectRealm.Framework
{
    /// <summary>Project Realm 进程级状态；场景只是该状态的表现，不拥有世界。</summary>
    public enum RealmApplicationState
    {
        Cold,
        Booting,
        MainMenu,
        LoadingWorld,
        Running,
        Paused,
        Saving,
        UnloadingWorld,
        ShuttingDown,
        Faulted
    }
}
