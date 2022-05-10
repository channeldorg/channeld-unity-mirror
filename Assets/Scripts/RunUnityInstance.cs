#if UNITY_EDITOR
using System;
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

    int num = 1;
    bool serverMode = true;
    bool alwaysBuild = false;
    string otherArgs = "";

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Number of instances:");
        num = EditorGUILayout.IntSlider(num, 1, 10);
        serverMode = EditorGUILayout.Toggle("Is server?", serverMode);
        alwaysBuild = EditorGUILayout.Toggle("Always build?", alwaysBuild);
        EditorGUILayout.LabelField("Other args: (available variables: {index}, {num})");
        otherArgs = EditorGUILayout.TextArea(otherArgs);
        var replacedArgs = otherArgs.Replace("{num}", num.ToString());
        EditorGUILayout.Space();

        if (GUILayout.Button("Run"))
        {
            for (int i = 0; i < num; i++)
            {
                string WorkingFolder = "../Build/Win";
                if (serverMode)
                    WorkingFolder += "Server";
                if (alwaysBuild || !Directory.Exists(Path.Combine(Application.dataPath, WorkingFolder)))
                {
                    if (serverMode)
                        BuildScript.BuildWindowsServer();
                    else
                        BuildScript.BuildWindowsClient();
                }

                replacedArgs = replacedArgs.Replace("{index}", i.ToString());

                BuildScript.RunProcess(BuildScript.WINDOWS_BINARY, otherArgs, WorkingFolder);
            }
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