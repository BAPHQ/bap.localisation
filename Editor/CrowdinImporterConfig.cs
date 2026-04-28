using System;
using System.Collections.Generic;
using UnityEngine;

namespace BAP.Localisation.Editor
{
    [CreateAssetMenu(
        fileName = "CrowdinImporterConfig",
        menuName = "BAP/Localisation/Crowdin Importer Config",
        order = 100)]
    public class CrowdinImporterConfig : ScriptableObject
    {
        [Serializable]
        public class ImportTarget
        {
            public string CrowdinLanguageId;
            public SystemLanguage UnityLanguage;
            public string CultureCode;
            public string ZipEntryPath;
            public string OutputAssetPath;
        }

        [Header("Crowdin")]
        public string ProjectId;
        [TextArea] public string PersonalAccessToken;
        public int BuildPollIntervalMilliseconds = 2000;
        public int BuildTimeoutSeconds = 120;

        [Header("Source")]
        public string KeyZipEntryPath;
        public string KeyOutputAssetPath;

        [Header("Targets")]
        public List<ImportTarget> Targets = new();
    }
}