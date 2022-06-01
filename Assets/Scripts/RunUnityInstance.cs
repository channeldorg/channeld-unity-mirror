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

    private void SaveProfile()
    {
        var filePath = EditorUtility.SaveFilePanel("Save profile to...", Application.persistentDataPath, "run", "json");
        if (!string.IsNullOrEmpty(filePath))
            File.WriteAllText(filePath, EditorJsonUtility.ToJson(this, true));
    }

    private void LoadProfile()
    {
        var filePath = EditorUtility.OpenFilePanel("Save profile to...", Application.persistentDataPath, "json");
        var json = File.ReadAllText(filePath);
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load profile from '{filePath}' \nException:{ex}");
            }
        }
    }

    [Serializable]
    public struct InstaceGroup
    {
        public bool visible;
        public int num;
        public float delayTime;
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
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Group"))
        {
            var newGroup = new InstaceGroup();
            newGroup.visible = true;
            newGroup.num = 1;
            newGroup.delayTime = 0;
            newGroup.serverMode = true;
            newGroup.alwaysBuild = false;
            newGroup.logFile = false;
            newGroup.useTimePostfixForLogFile = true;
            newGroup.otherArgs = "";
            groups.Add(newGroup);
        }
        if (GUILayout.Button("Save..."))
        {
            SaveProfile();
        }
        if (GUILayout.Button("Load..."))
        {
            LoadProfile();
        }
        GUILayout.EndHorizontal();

        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            bool removed = false;
            group.visible = EditorGUILayout.BeginFoldoutHeaderGroup(group.visible, "Group " + groupIndex);
            if (group.visible)
            {
                GUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Number of instances:");
                    group.num = EditorGUILayout.IntSlider(group.num, 0, 10);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Delay run in sec:");
                    group.delayTime = EditorGUILayout.FloatField(group.delayTime);
                }
                GUILayout.EndHorizontal();

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
                    removed = true;
                }
                GUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (!removed)
                groups[groupIndex] = group;
        }

        GUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("Run All"))
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                // The Unity instance can only run in the main thread, so we can't use Timer or Task to start a new thread.
                if (group.delayTime > 0)
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(group.delayTime));
                RunGroup(group, groupIndex);
            }
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
                string prefix = group.serverMode ? "server" : "client";
                string logFile = $"./Logs/{prefix}{i}-group{groupIndex}{timePostfix}.log";
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