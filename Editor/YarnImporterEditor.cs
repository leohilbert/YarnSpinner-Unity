﻿using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using System.Linq;
using System.IO;
using System.Globalization;

[CustomEditor(typeof(YarnImporter))]
public class YarnImporterEditor : ScriptedImporterEditor {

    int selectedLanguageIndex;

    int selectedNewTranslationLanguageIndex;

    /// <summary>
    /// Index of the currently selected voice over language.
    /// Only show voiceovers for one language.
    /// </summary>
    int selectedVoiceoverLanguageIndex;

    /// <summary>
    /// Foldout bool for voiceover list.
    /// </summary>
    bool showVoiceovers = false;

    SerializedProperty baseLanguageProp;

    private ProjectSettings _projectSettings;

    private Culture[] _culturesAvailable;

    public override void OnEnable() {
        base.OnEnable();
        baseLanguageProp = serializedObject.FindProperty("baseLanguageID");
        _culturesAvailable = Cultures.AvailableCultures;

        var projectSettingsSearch = AssetDatabase.FindAssets("t:ProjectSettings");
        if (projectSettingsSearch.Length > 0) {
            _projectSettings = AssetDatabase.LoadAssetAtPath<ProjectSettings>(AssetDatabase.GUIDToAssetPath(projectSettingsSearch[0]));
            // Check for situations where we don't want to apply the project settings (for instance, if no settings have been made at all ...)
            if (_projectSettings._textProjectLanguages.Count > 0 && (string.IsNullOrEmpty(baseLanguageProp.stringValue) || _projectSettings._textProjectLanguages.Contains(baseLanguageProp.stringValue))) {
                // Reduce the available languages to the list defined on the project settings
                _culturesAvailable = Cultures.LanguageNamesToCultures(_projectSettings._textProjectLanguages.ToArray());
            }
        }
        if (string.IsNullOrEmpty(baseLanguageProp.stringValue)) {
            if (_projectSettings != null && _projectSettings._textProjectLanguages.Count > 0) {
                // Use first language from project settings as base language
                selectedLanguageIndex = 0;
            } else {
                // Use system's language as base language if no project settings are defined
                selectedLanguageIndex = _culturesAvailable.
                    Select((culture, index) => new { culture, index })
                    .FirstOrDefault(element => element.culture.Name == CultureInfo.CurrentCulture.Name)
                    .index;
            }
        } else {
            // Get index from previously stored base language setting
            selectedLanguageIndex = _culturesAvailable.Select((culture, index) => new { culture, index })
                .FirstOrDefault(pair => pair.culture.Name == baseLanguageProp.stringValue)
                .index;
        }
        //selectedNewTranslationLanguageIndex = selectedLanguageIndex;
    }

    public override void OnDisable() {
        base.OnDisable();
    }


    public override void OnInspectorGUI() {
        serializedObject.Update();
        EditorGUILayout.Space();
        YarnImporter yarnImporter = (target as YarnImporter);

        // Array of translations that have been added to this asset + base language
        var languageIdsAvailableOnAsset = yarnImporter.localizations.
            Select(element => element.languageName).
            Append(_culturesAvailable[selectedLanguageIndex].Name).
            OrderBy(element => element).
            ToArray();

        selectedLanguageIndex = EditorGUILayout.Popup("Base Language", selectedLanguageIndex, Cultures.CulturesToDisplayNames(_culturesAvailable));
        baseLanguageProp.stringValue = _culturesAvailable[selectedLanguageIndex].Name;

        if (yarnImporter.isSuccesfullyCompiled == false) {
            EditorGUILayout.HelpBox(yarnImporter.compilationErrorMessage, MessageType.Error);
            return;
        }

        EditorGUILayout.Space();

        var canCreateLocalisation = yarnImporter.StringsAvailable == true && yarnImporter.AnyImplicitStringIDs == false;

        using (new EditorGUI.DisabledScope(!canCreateLocalisation))
        using (new EditorGUILayout.HorizontalScope()) {

            var culturesAvailableNotOnAsset = _culturesAvailable.Except(Cultures.LanguageNamesToCultures(languageIdsAvailableOnAsset)).ToArray();

            if (culturesAvailableNotOnAsset.Length > 0) {
                selectedNewTranslationLanguageIndex = EditorGUILayout.Popup(selectedNewTranslationLanguageIndex, Cultures.CulturesToDisplayNames(culturesAvailableNotOnAsset));

                if (GUILayout.Button("Create New Localisation", EditorStyles.miniButton)) {
                    var stringsTableText = AssetDatabase
                        .LoadAllAssetsAtPath(yarnImporter.assetPath)
                        .OfType<TextAsset>()
                        .FirstOrDefault()?
                        .text ?? "";

                    var selectedCulture = culturesAvailableNotOnAsset[selectedNewTranslationLanguageIndex];

                    var assetDirectory = Path.GetDirectoryName(yarnImporter.assetPath);

                    var newStringsTablePath = $"{assetDirectory}/{Path.GetFileNameWithoutExtension(yarnImporter.assetPath)} ({selectedCulture.Name}).csv";
                    newStringsTablePath = AssetDatabase.GenerateUniqueAssetPath(newStringsTablePath);

                    var writer = File.CreateText(newStringsTablePath);
                    writer.Write(stringsTableText);
                    writer.Close();

                    AssetDatabase.ImportAsset(newStringsTablePath);

                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(newStringsTablePath);

                    EditorGUIUtility.PingObject(asset);

                    // Automatically add newly created translation csv file to yarn program
                    var localizationsIndex = System.Array.FindIndex(yarnImporter.localizations, element => element.languageName == selectedCulture.Name);
                    var localizationSerializedProperty = serializedObject.FindProperty("localizations");
                    if (localizationsIndex != -1) {
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationsIndex).FindPropertyRelative("text").objectReferenceValue = asset;
                    } else {
                        localizationSerializedProperty.InsertArrayElementAtIndex(localizationSerializedProperty.arraySize);
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationSerializedProperty.arraySize - 1).FindPropertyRelative("text").objectReferenceValue = asset;
                        localizationSerializedProperty.GetArrayElementAtIndex(localizationSerializedProperty.arraySize - 1).FindPropertyRelative("languageName").stringValue = selectedCulture.Name;
                    }
                }
            } else {
                EditorGUILayout.HelpBox("Go to Project Settings if you want to add more translations.", MessageType.Info);
            }

        }

        if (yarnImporter.StringsAvailable == false) {
            EditorGUILayout.HelpBox("This file doesn't contain any localisable lines or options.", MessageType.Info);
        }

        if (yarnImporter.AnyImplicitStringIDs) {
            EditorGUILayout.HelpBox("Add #line: tags to all lines and options to enable creating new localisations. Either add them manually, or click Add Line Tags to automatically add tags. Note that this will modify your files on disk, and cannot be undone.", MessageType.Info);
            if (GUILayout.Button("Add Line Tags")) {
                AddLineTagsToFile(yarnImporter.assetPath);
            }
        }

        // Localization list
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localizations"), true);

        EditorGUILayout.Space();

        // Automatically find voice over assets based on the linetag and the language id
        // Note: this could be expanded to multiple search patterns via actions or 
        // delegates returning their results, comparing these results and selecting the 
        // result that returns exactly one matching asset.
        // Possible alternative search patterns: 
        // * search for $linetag but return asset with parent directory matching $language
        // * search for "$linetag-$language"
        // * search for "$language-$linetag"
        if (GUILayout.Button("Import Voice Over Audio Files")) {
            // For every linetag of this yarn asset
            for (int i = 0; i < yarnImporter.voiceOvers.Length; i++) {
                LinetagToLanguage linetagToLanguage = yarnImporter.voiceOvers[i];
                var linetag = linetagToLanguage.linetag.Remove(0, 5);
                // For every language of this yarn asset
                for (int j = 0; j < linetagToLanguage.languageToAudioclip.Length; j++) {
                    LanguageToAudioclip languageToAudioclip = linetagToLanguage.languageToAudioclip[j];

                    // Do not overwrite existing content
                    if (languageToAudioclip.audioClip != null) {
                        continue;
                    }

                    var language = languageToAudioclip.language;
                    var results = AssetDatabase.FindAssets("t:AudioClip " + linetag + " " + language);

                    // Write found AudioClip into voice overs array
                    if (results.Length != 0) {
                        var voiceOversProp = serializedObject.FindProperty("voiceOvers");
                        var linetagProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("linetag");
                        var languagetoAudioClipProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageToAudioclip");
                        var audioclipProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("audioClip");
                        audioclipProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(results[0]));
                    }

                    // Return info if the search results were ambiguous or there was not result
                    if (results.Length > 1) {
                        Debug.LogWarning("More than one asset found matching the linetag " + linetag + "  and the language " + language + ".");
                    } else if (results.Length == 0) {
                        Debug.LogWarning("No asset found matching the linetag '" + linetag + "' and the language '" + language + "'.");
                    }
                }
            }

        }
        // Voiceover list. Reduced to one language.
        showVoiceovers = EditorGUILayout.Foldout(showVoiceovers, "Voice Overs"); // FIXME: Clicking on the foldout triangle doesn't open/close the foldout
        if (showVoiceovers) {
            EditorGUI.indentLevel++;
            // Language selected here will reduce the visual representation of the voiceover data structure
            selectedVoiceoverLanguageIndex = EditorGUILayout.Popup(selectedVoiceoverLanguageIndex, Cultures.LanguageNamesToDisplayNames(languageIdsAvailableOnAsset), GUILayout.MaxWidth(96));
            // Only draw AudioClips from selected language
            for (int i = 0; i < yarnImporter.voiceOvers.Length; i++) {
                LinetagToLanguage linetagToLanguage = yarnImporter.voiceOvers[i];
                for (int j = 0; j < linetagToLanguage.languageToAudioclip.Length; j++) {
                    LanguageToAudioclip languageToAudioclip = linetagToLanguage.languageToAudioclip[j];
                    if (languageToAudioclip.language == languageIdsAvailableOnAsset[selectedVoiceoverLanguageIndex]) {
                        var voiceOversProp = serializedObject.FindProperty("voiceOvers");
                        var linetagProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("linetag");
                        var languagetoAudioClipProp = voiceOversProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageToAudioclip");
                        var languageProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("language");
                        var audioclipProp = languagetoAudioClipProp.GetArrayElementAtIndex(j).FindPropertyRelative("audioClip");
                        EditorGUILayout.PropertyField(audioclipProp, new GUIContent(linetagProp.stringValue));
                    }
                }
            }
            EditorGUI.indentLevel--;
        }

        var success = serializedObject.ApplyModifiedProperties();
#if UNITY_2018
        if (success) {
            EditorUtility.SetDirty(target);
            AssetDatabase.WriteImportSettingsIfDirty(AssetDatabase.GetAssetPath(target));
        }
#endif
#if UNITY_2019_1_OR_NEWER
        ApplyRevertGUI();
#endif
    }

    private void AddLineTagsToFile(string assetPath) {
        // First, gather all existing line tags, so that we don't
        // accidentally overwrite an existing one. Do this by finding _all_
        // YarnPrograms, and by extension their importers, and get the
        // string tags that they found.

        var allLineTags = Resources.FindObjectsOfTypeAll<YarnProgram>() // get all yarn programs that have been imported
            .Select(asset => AssetDatabase.GetAssetOrScenePath(asset)) // get the path on disk
            .Select(path => AssetImporter.GetAtPath(path)) // get the asset importer for that path
            .OfType<YarnImporter>() // ensure that they're all YarnImporters
            .SelectMany(importer => importer.stringIDs)
            .ToList(); // get all string IDs, flattened into one list            

        var contents = File.ReadAllText(assetPath);
        var taggedVersion = Yarn.Compiler.Utility.AddTagsToLines(contents, allLineTags);

        File.WriteAllText(assetPath, taggedVersion);

        AssetDatabase.ImportAsset(assetPath);
    }
}