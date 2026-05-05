using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace BAP.Localisation
{
    [CreateAssetMenu(
        fileName = "LocalisationConfig",
        menuName = "BAP/Localisation/Localisation Config")]
    public class LocalisationConfig : ScriptableObject
    {
        [Serializable]
        public class Localisation
        {
            public SystemLanguage Language;
            public string CultureCode;
            public string LanguageName;
            public Sprite LanguageIcon;
            public string FileName;
        }

        [FormerlySerializedAs("ImportPath")]
        public string ResourcesRootPath;
        public string KeysFileName;
        public Localisation DefaultLocalisation;
        public Localisation[] Localisations;
    }
}