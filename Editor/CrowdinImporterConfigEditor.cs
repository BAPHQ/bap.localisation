using UnityEditor;
using UnityEngine;

namespace BAP.Localisation.Editor
{
    [CustomEditor(typeof(CrowdinImportConfig))]
    public class CrowdinImportConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (CrowdinImportConfig)target;

            if (GUILayout.Button("Import"))
            {
                CrowdinLocalisationImporter.Import(config);
            }
        }
    }
}