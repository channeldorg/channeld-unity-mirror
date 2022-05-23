#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class RunUnityInstanceWindow : EditorWindow
{
    [MenuItem("Debug/Run Instances...")]
    static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(RunUnityInstanceWindow));
    }
    private void OnEnable()
    {
        string prefKey = GetType().Name;
        if (!EditorPrefs.HasKey(prefKey))
            return;

        string prefValue = EditorPrefs.GetString(prefKey);
        try
        {
            EditorJsonUtility.FromJsonOverwrite(prefValue, this);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load {prefKey} from JSON:\n{prefValue}\nException:{ex}");
        }
    }

    private void OnDisable()
    {
        string prefValue = EditorJsonUtility.ToJson(this, false);
        EditorPrefs.SetString(GetType().Name, prefValue);
    }

    [Serializable]
    public struct InstaceGroup
    {
        public bool visible;
        public int num;
        public bool serverMode;
        public bool alwaysBuild;
        public bool logFile;
        public bool useTimePostfixForLogFile;
        public string otherArgs;
    }

    [SerializeField]
    List<InstaceGroup> groups = new List<InstaceGroup>();
    Vector2 scrollPos;

    private void OnGUI()
    {
        if (GUILayout.Button("Add Group"))
        {
            var newGroup = new InstaceGroup();
            newGroup.visible = true;
            newGroup.num = 1;
            newGroup.serverMode = true;
            newGroup.alwaysBuild = false;
            newGroup.logFile = false;
            newGroup.useTimePostfixForLogFile = true;
            newGroup.otherArgs = "";
            groups.Add(newGroup);
        }
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            group.visible = EditorGUILayout.BeginFoldoutHeaderGroup(group.visible, "Group " + groupIndex);
            if (group.visible)
            {
                EditorGUILayout.LabelField("Number of instances:");
                group.num = EditorGUILayout.IntSlider(group.num, 0, 10);
                group.serverMode = EditorGUILayout.Toggle("Is server?", group.serverMode);
                group.alwaysBuild = EditorGUILayout.Toggle("Always build?", group.alwaysBuild);
                group.logFile = EditorGUILayout.Toggle("Use log file?", group.logFile);
                if (!group.logFile) GUI.enabled = false;
                group.useTimePostfixForLogFile = EditorGUILayout.Toggle("Use time postfix for log file?", group.useTimePostfixForLogFile, GUILayout.ExpandWidth(true));
                GUI.enabled = true;
                EditorGUILayout.LabelField("Other args: (available variables: {index}, {num})");
                group.otherArgs = EditorGUILayout.TextArea(group.otherArgs);
                EditorGUILayout.Space();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Run Group"))
                {
                    RunGroup(group, groupIndex);
                }
                if (GUILayout.Button("Remove"))
                {
                    groups.RemoveAt(groupIndex);
                }
                GUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            groups[groupIndex] = group;
        }

        GUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("Run All"))
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                RunGroup(groups[groupIndex], groupIndex);
        }

        if (hasUnsavedChanges)
            SaveChanges();
    }

    private void RunGroup(InstaceGroup group, int groupIndex)
    {
        bool hasBuiltServer = false;
        bool hasBuiltClient = false;
        var args = group.otherArgs.Replace("{num}", group.num.ToString());
        for (int i = 0; i < group.num; i++)
        {
            string WorkingFolder = "../Build/Win";
            if (group.serverMode)
                WorkingFolder += "Server";

            if (group.alwaysBuild || !Directory.Exists(Path.Combine(Application.dataPath, WorkingFolder)))
            {
                if (group.serverMode)
                { 
                    if (!hasBuiltServer)
                    {
                        BuildScript.BuildWindowsServer();
                        hasBuiltServer = true;
                    }
                }
                else
                { 
                    if (!hasBuiltClient)
                    { 
                        BuildScript.BuildWindowsClient();
                        hasBuiltClient = true;
                    }
                }
            }

            var instanceArgs = args.Replace("{index}", i.ToString());// + $" > {logFile} | type {logFile}";
            
            if (group.logFile)
            {
                string timePostfix = group.useTimePostfixForLogFile ? DateTime.Now.ToString("-yyyyMMddHHmmssff") : "";
                string logFile = $"./Logs/server{i}-group{groupIndex}{timePostfix}.log";
                instanceArgs += $" -logFile {logFile}";
            }

            BuildScript.RunProcess(BuildScript.WINDOWS_BINARY, instanceArgs, WorkingFolder);
        }
    }

    private void _RunInstances()
    {
        string projName = Directory.GetParent(Application.dataPath).Name;
        string rootPath = Path.Combine(Application.dataPath, "../..");
        string dirName = $"{projName}-instance1";
        string dirPath = Path.Combine(rootPath, dirName);
        string unityProcessPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

        BuildScript.ExecuteShellCommands(new string[]
        {
            // The WorkingDirectory in ProcessStartInfo may not work
            $"cd \"{rootPath}\"",
            Directory.Exists(dirPath) ? $"rmdir /Q /S {dirName}" : "",
            $"mkdir {dirName}",
            $"cd {dirName}",
            $"mklink /D Assets ..\\{projName}\\Assets",
            $"mklink /D ProjectSettings ..\\{projName}\\ProjectSettings",
            // The Packages has lock file which prevents the instance from running
            //$"mklink /D Packages ..\\{projName}\\Packages",
            //"cd ..",
            $"\"{unityProcessPath}\" -batchmode -nographics",
            "pause",
        }, "../..", true);
    }
}
#endif