using System;
using SQLite;
using UnityEngine;

namespace ProjectRealm.Bootstrap
{
    /// <summary>
    /// Definition SQLiteAsset 的唯一 Resources 边界。业务系统只接收 IWorldDefinitionStore，
    /// 不知道 Unity Resources 的存在；未来可在此处替换为 StreamingAssets 或 Addressables。
    /// </summary>
    internal sealed class UnityDefinitionAssetProvider
    {
        private readonly string _resourceName;

        public UnityDefinitionAssetProvider(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("A Definition resource name is required.", nameof(resourceName));
            }

            _resourceName = resourceName;
        }

        public SQLiteAsset LoadRequired()
        {
            var asset = Resources.Load<SQLiteAsset>(_resourceName);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "The development Definition database is missing. Run: python3 tools/framework/build_runtime_definition.py");
            }

            return asset;
        }
    }
}
