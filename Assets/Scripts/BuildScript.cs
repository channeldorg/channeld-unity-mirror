#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BuildScript
{
    static void BuildLinuxServerWithOptions(BuildOptions buildOptions = BuildOptions.None)
    {
        // As a fallback use <project root>/Build/Linux as output path
        var buildPath = Path.Combine(Application.dataPath, "../Build/Linux");

        // read in command line arguments e.g. add "-buildPath some/Path" if you want a different output path 
        var args = Environment.GetCommandLineArgs();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildPath")
            {
                buildPath = args[i + 1];
            }
        }

        // if the output folder doesn't exist create it now
        if (!Directory.Exists(buildPath))
        {
            Directory.CreateDirectory(buildPath);
        }

        BuildPipeline.BuildPlayer(

            // Simply use the scenes from the build settings
            // see https://docs.unity3d.com/ScriptReference/EditorBuildSettings-scenes.html
            EditorBuildSettings.scenes,

            // pass on the output binary file
            buildPath + "/server.x86_64",

            // Build for Linux 64 bit
            BuildTarget.StandaloneLinux64,

            // Use Headless mode
            // see https://docs.unity3d.com/ScriptReference/BuildOptions.EnableHeadlessMode.html
            // and make the build fail for any error
            // see https://docs.unity3d.com/ScriptReference/BuildOptions.StrictMode.html
            BuildOptions.EnableHeadlessMode | BuildOptions.StrictMode | buildOptions
        );
    }

    [MenuItem("Build/Linux Server")]
    static void BuildLinuxServer() => BuildLinuxServerWithOptions(BuildOptions.None);

    [MenuItem("Build/Linux Server (Development)")]
    static void BuildLinuxServerDev() => BuildLinuxServerWithOptions(BuildOptions.Development);
}
#endif