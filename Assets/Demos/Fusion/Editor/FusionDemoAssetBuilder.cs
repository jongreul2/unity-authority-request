using Fusion;
using Jongreul.AuthorityRequest.Networking;
using UnityEditor;
using UnityEngine;

namespace Jongreul.AuthorityRequest.Demos.Fusion.Editor
{
    /// <summary>
    /// Fusion 샘플 자산(가격표, 네트워크 프리팹)을 코드로 만든다. Fusion SDK를 넣은 뒤 한 번 실행한다.
    /// 배치 실행: -executeMethod Jongreul.AuthorityRequest.Demos.Fusion.Editor.FusionDemoAssetBuilder.BuildAll
    /// </summary>
    public static class FusionDemoAssetBuilder
    {
        public const string Folder = "Assets/Demos/Fusion";
        public const string PriceTablePath = Folder + "/PriceTable.asset";
        public const string NetworkPrefabPath = Folder + "/AuthorityRequestNetwork.prefab";
        public const int StartingBalance = 1000;

        [MenuItem("Tools/Authority Request/Rebuild Fusion Demo Assets")]
        public static void BuildAll()
        {
            var table = AssetDatabase.LoadAssetAtPath<PriceTableAsset>(PriceTablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<PriceTableAsset>();
                AssetDatabase.CreateAsset(table, PriceTablePath);
            }

            table.SetEntries(new[]
            {
                new PriceTableAsset.Entry { itemId = "hat", price = 300 },
                new PriceTableAsset.Entry { itemId = "cape", price = 500 },
                new PriceTableAsset.Entry { itemId = "boots", price = 200 },
            });
            EditorUtility.SetDirty(table);

            var root = new GameObject("AuthorityRequestNetwork", typeof(NetworkObject));
            root.AddComponent<PurchaseAuthority>().Configure(table, StartingBalance);
            root.AddComponent<GrabAuthority>();
            PrefabUtility.SaveAsPrefabAsset(root, NetworkPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Debug.Log($"[FusionDemoAssetBuilder] {PriceTablePath}, {NetworkPrefabPath}");
        }
    }
}
