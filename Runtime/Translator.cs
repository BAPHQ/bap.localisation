using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BAP.Localisation
{
    /// <summary>
    /// Localisation context for a single language gets translations for provided phrase keys.
    /// </summary>
    public class Translator
    {
        private readonly bool _keyForceUppercase;
        private readonly string _keyFailureString;
        private readonly string _ignoreTranslationString;
        private readonly SystemLanguage _language;
        private readonly string _cultureCode;
        private readonly Dictionary<string, string> _phraseLookup;
        private readonly CultureInfo _cultureInfo;

        public Translator(
            bool keyForceUppercase,
            string keyFailureString,
            string ignoreTranslationString,
            SystemLanguage language,
            Dictionary<string, string> phraseLookup,
            string cultureCode = null)
        {
            _keyForceUppercase = keyForceUppercase;
            _keyFailureString = keyFailureString;
            _ignoreTranslationString = ignoreTranslationString;
            _language = language;
            _phraseLookup = phraseLookup;
            _cultureCode = cultureCode;
            _cultureInfo = GetCultureInfo(language, cultureCode);
        }

        /// <summary>
        /// The system language this localisation represents
        /// </summary>
        public SystemLanguage Language
        {
            get => _language;
        }

        /// <summary>
        /// The culture info for this localisation language
        /// </summary>
        public CultureInfo CultureInfo
        {
            get => _cultureInfo;
        }

        public string CultureCode
        {
            get => _cultureCode;
        }

        /// <summary>
        /// Retrieves a translation for a provided phrase key
        /// </summary>
        public string Translate(string key, Object context = null)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            // If this key starts with the ignoring translation string, trim it and return it
            if (!string.IsNullOrEmpty(_ignoreTranslationString) && key.StartsWith(_ignoreTranslationString))
                return key[_ignoreTranslationString.Length..];

            // Force key to uppercase if configured
            key = _keyForceUppercase ? key.ToUpperInvariant() : key;

            // Get a translation from the lookup
            if (_phraseLookup.TryGetValue(key, out var phrase)) return phrase;

            var warning = $"[Translator] Failed to localise phrase with key: {key} for language: {_language}";
            Debug.LogWarning(warning, context);

            // Suppress exception returns failure string or key if empty
            var failure = _keyFailureString;

            return !string.IsNullOrEmpty(failure) ? failure : key;
        }

        /// <summary>
        /// Retrieves a translation for a provided phrase key and formats it with the provided arguments
        /// </summary>
        public string Translate(string key, params object[] args)
        {
            return string.Format(Translate(key), args);
        }

        public override string ToString()
        {
            var @string = $"<b><color=yellow>{_language}</color></b>\n\n";

            foreach (var kvp in _phraseLookup)
            {
                @string += $"<color=green>{kvp.Key}</color>   :   <b><i>{kvp.Value}</i></b>\n";
            }

            return @string;
        }

        private static readonly Dictionary<SystemLanguage, string> _languageToCultureMap = new()
        {
            { SystemLanguage.Afrikaans, "af-ZA" },
            { SystemLanguage.Arabic, "ar-SA" },
            { SystemLanguage.Basque, "eu-ES" },
            { SystemLanguage.Belarusian, "be-BY" },
            { SystemLanguage.Bulgarian, "bg-BG" },
            { SystemLanguage.Catalan, "ca-ES" },
            { SystemLanguage.Chinese, "zh-CN" }, // Default to Simplified Chinese
            { SystemLanguage.ChineseSimplified, "zh-CN" },
            { SystemLanguage.ChineseTraditional, "zh-TW" },
            { SystemLanguage.Czech, "cs-CZ" },
            { SystemLanguage.Danish, "da-DK" },
            { SystemLanguage.Dutch, "nl-NL" },
            { SystemLanguage.English, "en-US" }, // Default to US English
            { SystemLanguage.Estonian, "et-EE" },
            { SystemLanguage.Faroese, "fo-FO" },
            { SystemLanguage.Finnish, "fi-FI" },
            { SystemLanguage.French, "fr-FR" },
            { SystemLanguage.German, "de-DE" },
            { SystemLanguage.Greek, "el-GR" },
            { SystemLanguage.Hebrew, "he-IL" },
            { SystemLanguage.Hungarian, "hu-HU" },
            { SystemLanguage.Icelandic, "is-IS" },
            { SystemLanguage.Indonesian, "id-ID" },
            { SystemLanguage.Italian, "it-IT" },
            { SystemLanguage.Japanese, "ja-JP" },
            { SystemLanguage.Korean, "ko-KR" },
            { SystemLanguage.Latvian, "lv-LV" },
            { SystemLanguage.Lithuanian, "lt-LT" },
            { SystemLanguage.Norwegian, "no-NO" },
            { SystemLanguage.Polish, "pl-PL" },
            { SystemLanguage.Portuguese, "pt-PT" },
            { SystemLanguage.Romanian, "ro-RO" },
            { SystemLanguage.Russian, "ru-RU" },
            { SystemLanguage.SerboCroatian, "sr-RS" },
            { SystemLanguage.Slovak, "sk-SK" },
            { SystemLanguage.Slovenian, "sl-SI" },
            { SystemLanguage.Spanish, "es-ES" },
            { SystemLanguage.Swedish, "sv-SE" },
            { SystemLanguage.Thai, "th-TH" },
            { SystemLanguage.Turkish, "tr-TR" },
            { SystemLanguage.Ukrainian, "uk-UA" },
            { SystemLanguage.Vietnamese, "vi-VN" }
        };

        private static CultureInfo GetCultureInfo(SystemLanguage language, string cultureCode = null)
        {
            if (!string.IsNullOrEmpty(cultureCode))
            {
                try
                {
                    return new CultureInfo(cultureCode);
                }
                catch (Exception)
                {
                    Debug.LogWarning($"[Translator] Failed to create CultureInfo for code: {cultureCode}");
                }
            }

            if (_languageToCultureMap.TryGetValue(language, out var mappedCultureCode))
            {
                return new CultureInfo(mappedCultureCode);
            }

            // Default to English if the language is not in the dictionary
            return CultureInfo.InvariantCulture;
        }
    }
}