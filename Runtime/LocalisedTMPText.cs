using TMPro;
using UnityEngine;

namespace BAP.Localisation
{
    [RequireComponent(typeof(TMP_Text))]
    public class LocalisedTMPText : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private string _format;

        private TMP_Text _label;
        private object[] _args;

        private bool TryGetLocalisationManager(out LocalisationManager localisation)
        {
            localisation = LocalisationManager.Instance;
            return localisation != null;
        }

        private void Awake()
        {
            _label = GetComponent<TMP_Text>();

            if (TryGetLocalisationManager(out var localisation))
            {
                localisation.OnLanguageChanged += Localise;
                Localise(localisation.CurrentTranslator);
            }
        }

        private void OnDestroy()
        {
            if (TryGetLocalisationManager(out var localisation))
            {
                localisation.OnLanguageChanged -= Localise;
            }
        }

        public void SetKey(string key, params object[] args)
        {
            _key = key;
            _args = args;

            if (TryGetLocalisationManager(out var localisation))
            {
                Localise(localisation.CurrentTranslator);
            }
        }

        public void SetArgs(params object[] args)
        {
            _args = args;

            if (TryGetLocalisationManager(out var localisation))
            {
                Localise(localisation.CurrentTranslator);
            }
        }

        private void Localise(Translator translator)
        {
            if (string.IsNullOrEmpty(_key))
                return;

            var translated = _args != null ? translator.Translate(_key, _args) : translator.Translate(_key);

            if (!string.IsNullOrEmpty(_format))
            {
                try
                {
                    translated = string.Format(translated, _format);
                }
                catch (System.FormatException)
                {
                    Debug.LogError(
                        $"[LocalisedTMP] Failed to format string. Key: {_key}, Language: {translator.Language}");
                    translated = "ERROR";
                }
            }

            if (_label == null)
                return;

            _label.text = translated;
        }
    }
}