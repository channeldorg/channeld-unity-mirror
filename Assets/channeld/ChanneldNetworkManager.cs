using Mirror;
using System;
using UnityEngine;

namespace Channeld
{
    public enum LogLevel { Debug, Info, Warning, Error }

    public static class Log
    {
        public static Action<string> Debug = Console.WriteLine;
        public static Action<string> Info = Console.WriteLine;
        public static Action<string> Warning = Console.WriteLine;
        public static Action<string> Error = Console.Error.WriteLine;
    }

    public class ChanneldNetworkManager : NetworkManager
    {
        [Header("Logging")]
        public LogLevel logLevel = LogLevel.Info;

        public override void Awake()
        {
            base.Awake();

            Log.Debug = (t) => { if (logLevel <= LogLevel.Debug) Debug.Log(t); };
            Log.Info = (t) => { if (logLevel <= LogLevel.Info) Debug.Log(t); };
            Log.Warning = (t) => { if (logLevel <= LogLevel.Warning) Debug.LogWarning(t); };
            Log.Error = (t) => { if (logLevel <= LogLevel.Error) Debug.LogError(t); };

            var parser = CmdLineArgParser.Default;
            parser.DefaultConversionSuccessHandler = (optionName, alias, value, result) => Log.Info($"Read '{optionName}' from command line: {value}");
            parser.DefaultConversionErrorHandler = (optionName, alias, value, type, ex) => Log.Error($"Invalid value of command line arg '{optionName}': {value}, exception: {ex}");
            parser.GetOptionValue("--client-ip", "-ca", ref networkAddress);
            parser.GetOptionValue("--max-conn", "-maxconn", ref maxConnections);
            parser.GetEnumOptionFromInt("--log-level", "-loglevel", ref logLevel);
            parser.GetOptionValue("--auto-create-player", "-acp", ref autoCreatePlayer);
        }

        public override void OnStopClient()
        {
            if (NetworkClient.aoi && NetworkClient.aoi is ChanneldInterestManagement cim)
            {
                cim.CurrentView.OnDisconnect();
                cim.CurrentView.SendAllChannelUpdates();
            }
        }
    }
}
