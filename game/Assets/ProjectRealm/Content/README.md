# Project Realm runtime content

Place versioned, releasable Unity content and configuration in this directory.

Research databases and rebuildable historical-data outputs remain outside the Unity project. Runtime content must be exported through a validated build step and must pass the commercial-license gate before release.

Manual learning and debug scene inputs belong in `Assets/ProjectRealm/Development/TestData/`, not here. The CountyMapPrototype and FiveTerrainStudy samples were moved there on 2026-08-31 with their Unity GUIDs preserved. Player save databases belong in persistent user data, not in `Assets`.
