using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BAP.Localisation
{
    public class LocalisationManager : MonoBehaviour
    {
        [Serializable]
        public class Localisation
        {
            public SystemLanguage Language;
            public string CultureCode;
            public string LanguageName;
            public Sprite LanguageIcon;
            public TextAsset PhraseData;
        }

        [Serializable]
        public class DebugConfig
        {
            public bool OverrideSystemLanguage;
            public SystemLanguage LanguageOverride;
            public bool OutputLocalisation;
        }

        [SerializeField] private TextAsset _keyData;
        [SerializeField] private Localisation _defaultLocalisation;
        [SerializeField] private Localisation[] _localisations;
        [SerializeField] private DebugConfig _debug;
        [SerializeField] private Translator.Configuration _translation;

        private static LocalisationManager _instance;
        private readonly Dictionary<int, ILocalised> _listenerLookup = new();
        private readonly List<Translator> _loadedLocalisations = new();
        private Translator _defaultTranslator;
        private Translator _currentTranslator;
        public event Action<Translator> OnLanguageChanged;

        public static LocalisationManager Instance
        {
            get => _instance;
        }

        public Translator CurrentTranslator
        {
            get => _currentTranslator;
        }

        public int CurrentLanguageIndex
        {
            get
            {
                if (_currentTranslator == _defaultTranslator) return 0;
                var index = _loadedLocalisations.IndexOf(_currentTranslator);
                if (index >= 0) return index + 1;
                return -1;
            }
        }

        public List<Localisation> Localisations
        {
            get
            {
                var localisations = new List<Localisation> { _defaultLocalisation };
                localisations.AddRange(_localisations);
                return localisations;
            }
        }

        public void Initialise()
        {
            Debug.Log("[LocalisationManager] Initialise");

            // Load the default configuration
            try
            {
                _defaultTranslator = LoadFromConfig(_defaultLocalisation);
                _currentTranslator = _defaultTranslator;
            }
            catch (Exception e)
            {
                Debug.LogError("Localisation failed to parse default language json with exception");
                Debug.LogException(e);
                return;
            }

            var output = _debug.OutputLocalisation ? "<b>Localisations</b>\n\n" : "";

            // Load localisations
            for (var i = 0; i < _localisations.Length; i++)
            {
                var configuration = _localisations[i];
                var language = configuration.Language;

                try
                {
                    var localisation = LoadFromConfig(configuration);

                    if (_debug.OutputLocalisation) output += $"{localisation}\n";

                    _loadedLocalisations.Add(localisation);
                }
                catch (Exception e)
                {
                    Debug.LogWarningFormat("[Localisation] Failed to load {0} with exception", language);
                    Debug.LogException(e);
                }
            }

            if (_debug.OutputLocalisation && !string.IsNullOrEmpty(output))
            {
                Debug.Log(output);
            }

            // If the language has been manually set then try to load that localisation
            var languageIndexPref = PlayerPrefs.GetInt("Settings_Language_Index", -1);
            if (languageIndexPref >= 0)
            {
                var allLocalisations = Localisations;
                if (languageIndexPref < allLocalisations.Count)
                {
                    if (languageIndexPref == 0)
                    {
                        _currentTranslator = _defaultTranslator;
                        return;
                    }

                    var loadedIndex = languageIndexPref - 1;
                    if (loadedIndex < _loadedLocalisations.Count)
                    {
                        _currentTranslator = _loadedLocalisations[loadedIndex];
                        return;
                    }
                }
            }

            var languagePref = PlayerPrefs.GetString("Settings_Language");
            if (languagePref != string.Empty &&
                Enum.TryParse<SystemLanguage>(languagePref, out var l))
            {
                var t = _loadedLocalisations.FirstOrDefault(x => x.Language == l);
                if (t != null)
                {
                    _currentTranslator = t;
                    return;
                }
            }

            // If the system language is not the default language and is present in lookup then use that language
            var systemLanguage = Application.systemLanguage;
            var systemCulture = System.Globalization.CultureInfo.CurrentCulture.Name;

            // Hack to map Chinese => ChineseSimplified
            if (systemLanguage == SystemLanguage.Chinese)
            {
                systemLanguage = SystemLanguage.ChineseSimplified;
            }

            if (systemLanguage != _currentTranslator.Language || !string.IsNullOrEmpty(_currentTranslator.CultureCode))
            {
                // Try to find an exact match for both language and culture
                var translator = _loadedLocalisations.FirstOrDefault(x =>
                    x.Language == systemLanguage && x.CultureCode == systemCulture);

                // If no exact match, try matching just the language (preferring one without a specific culture code or the first one found)
                if (translator == null)
                {
                    translator = _loadedLocalisations.FirstOrDefault(x => x.Language == systemLanguage);
                }

                if (translator != null)
                {
                    _currentTranslator = translator;
                }
            }

            // Save the index for next time if it changed or wasn't set
            if (languageIndexPref == -1)
            {
                PlayerPrefs.SetInt("Settings_Language_Index", CurrentLanguageIndex);
            }

            _instance = this;
        }

        /// <summary>
        /// Register a listener to be notified of localisation changes, will be called immediately, registered
        /// listeners are checked for null and removed if found, deregistration is optional
        /// </summary>
        public void Register(ILocalised listener)
        {
            if (listener == null) return;

            var hash = listener.GetHashCode();

            if (!_listenerLookup.TryAdd(hash, listener)) return;

            if (_currentTranslator != null)
            {
                listener.Localise(_currentTranslator);
            }
        }

        /// <summary>
        /// Deregister a listener from localisation changes
        /// </summary>
        public void Deregister(ILocalised listener)
        {
            if (listener == null) return;

            var hash = listener.GetHashCode();

            _listenerLookup.Remove(hash);
        }

        /// <summary>
        /// Set the current language by system language, cache and notify any listeners
        /// </summary>
        public void SetLanguage(SystemLanguage language, int index = -1)
        {
            if (index >= 0)
            {
                var allLocalisations = Localisations;
                if (index < allLocalisations.Count)
                {
                    if (index == 0)
                    {
                        _currentTranslator = _defaultTranslator;
                    }
                    else
                    {
                        var loadedIndex = index - 1;
                        if (loadedIndex < _loadedLocalisations.Count)
                        {
                            _currentTranslator = _loadedLocalisations[loadedIndex];
                        }
                    }
                }
            }
            else
            {
                // If it's Portuguese, try to find the best match based on culture
                if (language == SystemLanguage.Portuguese)
                {
                    var systemCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                    _currentTranslator =
                        _loadedLocalisations.FirstOrDefault(x =>
                            x.Language == language && x.CultureCode == systemCulture)
                        ?? _loadedLocalisations.FirstOrDefault(x => x.Language == language)
                        ?? _defaultTranslator;
                }
                else
                {
                    _currentTranslator = _loadedLocalisations.FirstOrDefault(x => x.Language == language) ??
                                         _defaultTranslator;
                }
            }

            index = CurrentLanguageIndex;
            PlayerPrefs.SetInt("Settings_Language_Index", index);
            PlayerPrefs.SetString("Settings_Language", _currentTranslator.Language.ToString());
            NotifyLanguageChanged();
        }

        /// <summary>
        /// The default localisation context, this will fall back to empty
        /// if there's an exception when loading from configuration
        /// </summary>
        public Translator DefaultTranslator
        {
            get => _defaultTranslator ?? new Translator(
                _translation, SystemLanguage.English, new Dictionary<string, string>());
        }

        /// <summary>
        /// Obtain a localisation context for the provided language
        /// if the data isn't found will fall back to default
        /// </summary>
        public Translator GetTranslator(SystemLanguage language)
        {
            return _loadedLocalisations.FirstOrDefault(x => x.Language == language) ?? DefaultTranslator;
        }

        private Translator LoadFromConfig(Localisation configuration)
        {
            // Obtain the keys/phrases from the data files
            var language = configuration.Language;
            var phrases = configuration.PhraseData.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var keys = _keyData.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Create a lookup for phrases
            var dictionary = new Dictionary<string, string>();

            // Check the key length, throw an exception as this means
            // there's an issue with the import and nothing is safe
            if (keys.Length != phrases.Length)
            {
                throw new Exception("[Localisation] key phrase length mismatch");
            }

            // Iterate over the keys/phrases and populate the dictionary
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var phrase = phrases[i];

                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (dictionary.ContainsKey(key))
                {
                    Debug.LogWarning("[Localisation] duplicate key found " + key);
                }

                // If we reach an untranslated phrase, fall back to the default translation
                if (string.IsNullOrEmpty(phrase))
                {
                    // Check for null since the default translator is configured by this method also
                    if (_defaultTranslator != null)
                    {
                        phrase = _defaultTranslator.Translate(key);
                    }
                }

                dictionary.Add(key, phrase);
            }

            return new Translator(_translation, language, dictionary, configuration.CultureCode);
        }

        // Safe notification of language change, emit event and notify listeners
        private void NotifyLanguageChanged()
        {
            OnLanguageChanged?.Invoke(_currentTranslator);

            List<int> remove = null;

            foreach (var kvp in _listenerLookup)
            {
                if (kvp.Value == null)
                {
                    remove ??= new List<int>();
                    remove.Add(kvp.Key);
                    continue;
                }

                kvp.Value.Localise(_currentTranslator);
            }

            if (remove != null)
            {
                foreach (var key in remove)
                {
                    _listenerLookup.Remove(key);
                }
            }
        }
    }
}