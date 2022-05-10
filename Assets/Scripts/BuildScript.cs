#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class BuildScript
{
    public const string LINUX_SERVER_BINARY = "server.x86_64";
    public const string WINDOWS_BINARY = "MirrorTest.exe";

    static void BuildPlayer(string folderName, string binaryName, BuildTarget target, BuildOptions buildOptions = BuildOptions.None)
    {
        // As a fallback use <project root>/Build/Linux as output path
        var buildPath = Path.Combine(Application.dataPath, "../Build", folderName);

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
            buildPath + "/" + binaryName,

            // Build for Linux 64 bit
            target,

            // Use Headless mode
            // see https://docs.unity3d.com/ScriptReference/BuildOptions.EnableHeadlessMode.html
            // and make the build fail for any error
            // see https://docs.unity3d.com/ScriptReference/BuildOptions.StrictMode.html
            BuildOptions.StrictMode | buildOptions
        );
    }

    [MenuItem("Build/Linux Server")]
    public static void BuildLinuxServer() => BuildPlayer("Linux", LINUX_SERVER_BINARY,
        BuildTarget.StandaloneLinux64, BuildOptions.EnableHeadlessMode);

    [MenuItem("Build/Linux Server (Development)")]
    public static void BuildLinuxServerDev() => BuildPlayer("Linux", LINUX_SERVER_BINARY,
        BuildTarget.StandaloneLinux64, BuildOptions.EnableHeadlessMode | BuildOptions.Development);

    [MenuItem("Build/Docker Image")]
    public static void BuildDockerImage()
    {
        RunCommandLines(new string[]
        {
            "docker build -t channeld/tanks .",
        });
    }

    [MenuItem("Build/Build Server and Image")]
    public static void BuildServerAndImage()
    {
        BuildLinuxServer();
        ExecuteShellCommands("build_docker.bat");
    }

    [MenuItem("Build/Windows Client", priority = 1)]
    public static void BuildWindowsClient() => BuildPlayer("Win", WINDOWS_BINARY,
        BuildTarget.StandaloneWindows64, BuildOptions.Development);

    [MenuItem("Build/Windows Server", priority = 2)]
    public static void BuildWindowsServer() => BuildPlayer("WinServer", WINDOWS_BINARY,
        BuildTarget.StandaloneWindows64, BuildOptions.EnableHeadlessMode | BuildOptions.Development);

    public static System.Diagnostics.Process RunProcess(string filename, string args, string relativePathToAssets = "..")
    {
        string path = Path.Combine(Application.dataPath, relativePathToAssets);
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
        {
            FileName = Path.Combine(path, filename),
            Arguments = args,
            WorkingDirectory = path,
            CreateNoWindow = false,
            UseShellExecute = true
        });

        return p;
    }

    public static void ExecuteShellCommands(string[] cmds, string relativePathToAssets = "..", bool asAdmin = false)
    {
        string path = Path.Combine(Application.dataPath, relativePathToAssets);
        File.WriteAllLines(Path.Combine(path, "temp.bat"), cmds);
        ExecuteShellCommands("temp.bat", relativePathToAssets, asAdmin);
    }

    public static void ExecuteShellCommands(string filename, string relativePathToAssets = "..", bool asAdmin = false)
    {
        var psi = new System.Diagnostics.ProcessStartInfo()
        {
            FileName = filename,
            WorkingDirectory = Path.Combine(Application.dataPath, relativePathToAssets),
            UseShellExecute = true,
        };
        if (asAdmin)
        {
            psi.Verb = "runas";
        }

        var p = System.Diagnostics.Process.Start(psi);
        p.WaitForExit();
        p.Close();
    }

    public static void RunCommandLines(string[] cmds, string relativePathToAssets = "..")
    {
        string path = Path.Combine(Application.dataPath, relativePathToAssets);

        var psi = new System.Diagnostics.ProcessStartInfo()
        {
            FileName = "cmd.exe",
            WorkingDirectory = path,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.Default,
            StandardOutputEncoding = Encoding.Default,
        };

        var p = System.Diagnostics.Process.Start(psi);
        /*
        p.ErrorDataReceived += (sender, e) => Debug.LogError(e.Data);
        p.OutputDataReceived += (sender, e) => Debug.Log(e.Data);
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        */
        using (StreamWriter sw = p.StandardInput)
        {
            foreach (var cmd in cmds)
            {
                sw.WriteLine(cmd);
                var output = p.StandardOutput.ReadToEnd();
                if (string.IsNullOrEmpty(output))
                    Debug.Log(output);
                var error = p.StandardError.ReadToEnd();
                if (string.IsNullOrEmpty(error))
                    Debug.LogError(error);
            }
        }
        p.WaitForExit();
        p.Close();
    }
}
#endif