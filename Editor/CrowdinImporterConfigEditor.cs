using UnityEditor;

namespace BAP.Localisation.Editor
{
    [CustomEditor(typeof(CrowdinImporterConfig))]
    public class CrowdinImporterConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (CrowdinImporterConfig)target;

            if (GUILayout.Button("Import From Crowdin"))
            {
                CrowdinLocalisationImporter.Import(config);
            }
        }
    }
}