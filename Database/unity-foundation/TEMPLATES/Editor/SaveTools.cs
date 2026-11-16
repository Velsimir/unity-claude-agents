using System.IO;
using UnityEditor;
using UnityEngine;
using {{ROOT}}.Core.Save;

namespace {{ROOT}}.Editor
{
    public static class SaveTools
    {
        private const string MENU_PATH = "Tools/Clear Save";
        private const string DIALOG_TITLE = "Clear Save";
        private const string DIALOG_MESSAGE = "Удалить локальный сейв и PlayerPrefs? В Play Mode игра будет остановлена.";

        [MenuItem(MENU_PATH)]
        private static void ClearSave()
        {
            if (EditorUtility.DisplayDialog(DIALOG_TITLE, DIALOG_MESSAGE, "Удалить", "Отмена") == false)
                return;

            if (EditorApplication.isPlaying)
            {
                EditorApplication.playModeStateChanged += ClearAfterExitingPlayMode;
                EditorApplication.isPlaying = false;

                return;
            }

            DeleteSaveData();
        }

        private static void ClearAfterExitingPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= ClearAfterExitingPlayMode;

            DeleteSaveData();
        }

        public static void DeleteSaveData()
        {
            string directory = LocalFileSaveStorage.DirectoryPath;

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            Debug.Log($"[Save] Удалены локальный сейв ({directory}) и PlayerPrefs.");
        }
    }
}
