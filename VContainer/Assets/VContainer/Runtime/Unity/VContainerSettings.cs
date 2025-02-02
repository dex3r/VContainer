using System.Linq;
using UnityEngine;

namespace VContainer.Unity
{
    public sealed class VContainerSettings : ScriptableObject
    {
        public static VContainerSettings Instance { get; private set; }
        public static bool DiagnosticsEnabled => Instance != null && Instance.EnableDiagnostics;

        [SerializeField]
        [Tooltip("Enables the collection of information that can be viewed in the VContainerDiagnosticsWindow. Note: Performance degradation")]
        public bool EnableDiagnostics;

        [SerializeField]
        [Tooltip("Disables script modification for LifetimeScope scripts.")]
        public bool DisableScriptModifier;

        [SerializeField]
        [Tooltip("Removes (Clone) postfix in IObjectResolver.Instantiate() and IContainerBuilder.RegisterComponentInNewPrefab().")]
        public bool RemoveClonePostfix;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/VContainer/VContainer Settings")]
        public static void CreateAsset()
        {
            var path = UnityEditor.EditorUtility.SaveFilePanelInProject(
                "Save VContainerSettings",
                "VContainerSettings",
                "asset",
                string.Empty);

            if (string.IsNullOrEmpty(path))
                return;

            var newSettings = CreateInstance<VContainerSettings>();
            UnityEditor.AssetDatabase.CreateAsset(newSettings, path);

            var preloadedAssets = UnityEditor.PlayerSettings.GetPreloadedAssets().ToList();
            preloadedAssets.RemoveAll(x => x is VContainerSettings);
            preloadedAssets.Add(newSettings);
            UnityEditor.PlayerSettings.SetPreloadedAssets(preloadedAssets.ToArray());
        }

        public static void LoadInstanceFromPreloadAssets()
        {
            var preloadAsset = UnityEditor.PlayerSettings.GetPreloadedAssets().FirstOrDefault(x => x is VContainerSettings);
            if (preloadAsset is VContainerSettings instance)
            {
                instance.OnDisable();
                instance.OnEnable();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeInitialize()
        {
            // For editor, we need to load the Preload asset manually.
            LoadInstanceFromPreloadAssets();
        }
#endif

        void OnEnable()
        {
            if (Application.isPlaying)
            {
                Instance = this;
            }
        }

        void OnDisable()
        {
            Instance = null;
        }

        static void SetName(Object instance, Object prefab)
        {
            if (Instance != null && Instance.RemoveClonePostfix)
                instance.name = prefab.name;
        }
    }
}