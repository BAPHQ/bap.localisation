using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BAP.Localisation
{
    public class LocalisationManager : MonoBehaviour
    {
        private const string DEFAULT_LANGUAGE_INDEX_PREF_KEY = "Settings_Language_Index";
        private const string DEFAULT_LANGUAGE_PREF_KEY = "Settings_Language";

        [SerializeField] private LocalisationConfig _localisationConfig;
        
        [Header("Debug")]
        [SerializeField] private bool _overrideSystemLanguage;
        [SerializeField] private SystemLanguage _languageOverride;

        [Header("Translation")]
        [SerializeField] private bool _keyForceUppercase;
        [SerializeField] private string _keyFailureString;
        [SerializeField] private string _ignoreTranslationString;

        [Header("Persistence")]
        [SerializeField] private string _languageIndexPlayerPrefsKey = DEFAULT_LANGUAGE_INDEX_PREF_KEY;
        [SerializeField] private string _languagePlayerPrefsKey = DEFAULT_LANGUAGE_PREF_KEY;

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

        public List<LocalisationConfig.Localisation> Localisations
        {
            get
            {
                if (_localisationConfig == null)
                {
                    return new List<LocalisationConfig.Localisation>();
                }

                var localisations = new List<LocalisationConfig.Localisation>();
                if (_localisationConfig.DefaultLocalisation != null)
                {
                    localisations.Add(_localisationConfig.DefaultLocalisation);
                }

                if (_localisationConfig.Localisations != null)
                {
                    localisations.AddRange(_localisationConfig.Localisations);
                }

                return localisations;
            }
        }

        public void Initialise()
        {
            // Expose instance immediately so dependent components can safely access it during init.
            _instance = this;

            Debug.Log("[LocalisationManager] Initialise");

            if (_localisationConfig == null || _localisationConfig.DefaultLocalisation == null)
            {
                Debug.LogError("[Localisation] LocalisationConfig is missing or DefaultLocalisation is not configured.");
                return;
            }

            _loadedLocalisations.Clear();

            string[] keys;
            try
            {
                keys = LoadKeys();
            }
            catch (Exception e)
            {
                Debug.LogError("[Localisation] Failed to load keys file.");
                Debug.LogException(e);
                return;
            }

            var defaultLocalisation = _localisationConfig.DefaultLocalisation;

            // Load the default configuration
            try
            {
                _defaultTranslator = LoadFromConfig(defaultLocalisation, keys);
                _currentTranslator = _defaultTranslator;
            }
            catch (Exception e)
            {
                Debug.LogError("Localisation failed to parse default language json with exception");
                Debug.LogException(e);
                return;
            }

            // Load localisations
            var localisations = _localisationConfig.Localisations ?? Array.Empty<LocalisationConfig.Localisation>();
            for (var i = 0; i < localisations.Length; i++)
            {
                var configuration = localisations[i];
                var language = configuration.Language;

                try
                {
                    var localisation = LoadFromConfig(configuration, keys);

                    _loadedLocalisations.Add(localisation);
                }
                catch (Exception e)
                {
                    Debug.LogWarningFormat("[Localisation] Failed to load {0} with exception", language);
                    Debug.LogException(e);
                }
            }

            // Emit a compact summary of the language set that was loaded.
            LogInitialisedLanguages();

            // If the language has been manually set then try to load that localisation
            var languageIndexPref = PlayerPrefs.GetInt(LanguageIndexPlayerPrefsKey, -1);
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

            var languagePref = PlayerPrefs.GetString(LanguagePlayerPrefsKey);
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
                PlayerPrefs.SetInt(LanguageIndexPlayerPrefsKey, CurrentLanguageIndex);
            }

        }

        private void LogInitialisedLanguages()
        {
            var languages = new List<string>();

            if (_defaultTranslator != null)
            {
                languages.Add($"{_defaultTranslator.Language}(default)");
            }

            foreach (var localisation in _loadedLocalisations)
            {
                languages.Add(localisation.Language.ToString());
            }

            Debug.Log($"[LocalisationManager] Initialised languages ({languages.Count}): {string.Join(", ", languages)}");
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
            PlayerPrefs.SetInt(LanguageIndexPlayerPrefsKey, index);
            PlayerPrefs.SetString(LanguagePlayerPrefsKey, _currentTranslator.Language.ToString());
            NotifyLanguageChanged();
        }

        private string LanguageIndexPlayerPrefsKey =>
            string.IsNullOrWhiteSpace(_languageIndexPlayerPrefsKey)
                ? DEFAULT_LANGUAGE_INDEX_PREF_KEY
                : _languageIndexPlayerPrefsKey;

        private string LanguagePlayerPrefsKey =>
            string.IsNullOrWhiteSpace(_languagePlayerPrefsKey)
                ? DEFAULT_LANGUAGE_PREF_KEY
                : _languagePlayerPrefsKey;

        /// <summary>
        /// The default localisation context, this will fall back to empty
        /// if there's an exception when loading from configuration
        /// </summary>
        public Translator DefaultTranslator
        {
            get => _defaultTranslator ?? new Translator(
                _keyForceUppercase, _keyFailureString, _ignoreTranslationString,
                SystemLanguage.English, new Dictionary<string, string>());
        }

        /// <summary>
        /// Obtain a localisation context for the provided language
        /// if the data isn't found will fall back to default
        /// </summary>
        public Translator GetTranslator(SystemLanguage language)
        {
            return _loadedLocalisations.FirstOrDefault(x => x.Language == language) ?? DefaultTranslator;
        }

        private Translator LoadFromConfig(LocalisationConfig.Localisation configuration, string[] keys)
        {
            // Obtain the keys/phrases from the data files
            var language = configuration.Language;
            var phraseText = LoadResourceText(configuration.FileName);
            var phrases = SplitResourceLines(phraseText);

            // Create a lookup for phrases
            var dictionary = new Dictionary<string, string>();

            // Check the key length, throw an exception as this means
            // there's an issue with the import and nothing is safe
            if (keys.Length != phrases.Length)
            {
                throw new Exception($"[Localisation] key phrase length mismatch for '{configuration.FileName}'. Keys: {keys.Length}, Phrases: {phrases.Length}");
            }

            // Iterate over the keys/phrases and populate the dictionary
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var phrase = RestoreEscapedNewlines(phrases[i]);

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

            return new Translator(
                _keyForceUppercase,
                _keyFailureString,
                _ignoreTranslationString,
                language,
                dictionary,
                configuration.CultureCode);
        }

        private string[] LoadKeys()
        {
            var keysText = LoadResourceText(_localisationConfig.KeysFileName);
            return SplitResourceLines(keysText);
        }

        private static string[] SplitResourceLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            if (lines.Length > 0 && string.IsNullOrEmpty(lines[^1]))
            {
                Array.Resize(ref lines, lines.Length - 1);
            }

            return lines;
        }

        private static string RestoreEscapedNewlines(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
            {
                return phrase;
            }

            return phrase.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\r", "\r");
        }

        private string LoadResourceText(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new Exception("[Localisation] Resource file name is missing.");
            }

            // Build a Resources.Load-compatible path from root + file name.
            var resourcesRootPath = NormalizeResourcePathSegment(_localisationConfig.ResourcesRootPath);
            var normalizedFileName = NormalizeResourcePathSegment(NormalizeResourceFileName(fileName));
            var resourcePath = string.IsNullOrEmpty(resourcesRootPath)
                ? normalizedFileName
                : $"{resourcesRootPath}/{normalizedFileName}";

            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                throw new Exception($"[Localisation] Resource file '{resourcePath}' was not found.");
            }

            return textAsset.text;
        }

        private static string NormalizeResourceFileName(string fileName)
        {
            var normalized = fileName.Trim().Replace('\\', '/');
            var extension = System.IO.Path.GetExtension(normalized);

            return string.IsNullOrWhiteSpace(extension)
                ? normalized
                : normalized.Substring(0, normalized.Length - extension.Length);
        }

        private static string NormalizeResourcePathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Replace('\\', '/').Trim('/');
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