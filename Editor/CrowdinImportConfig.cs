using UnityEngine;

namespace BAP.Localisation.Editor
{
    [CreateAssetMenu(
        fileName = "CrowdinImportConfig",
        menuName = "BAP/Localisation/Crowdin Import Config",
        order = 100)]
    public class CrowdinImportConfig : ScriptableObject
    {
        [Header("Crowdin")]
        public string ApiKey;
        public string ProjectName;
        public string BundleName;

        [Header("Import")] 
        public string ResourcesPath = "Assets/Config/Localisation/Import";
        public string SourceLanguageFileName = "en.json";
    }
}