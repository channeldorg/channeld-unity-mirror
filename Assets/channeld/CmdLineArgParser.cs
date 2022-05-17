
using System;
using System.Collections.Generic;

namespace Channeld
{
    public delegate void ConversionSuccessHandler(string optionName, string alias, string optionValue, object result);
    public delegate void ConversionErrorHandler(string optionName, string alias, string optionValue, Type targetType, Exception ex);

    /// <summary>
    /// See the command line syntax definitions in https://docs.microsoft.com/en-us/dotnet/standard/commandline/syntax
    /// </summary>
    public class CmdLineArgParser
    {

        private static Dictionary<string[], CmdLineArgParser> cachedParsers = new Dictionary<string[], CmdLineArgParser>();

        public static CmdLineArgParser Default { get; private set; } = Create(Environment.GetCommandLineArgs());

        public static CmdLineArgParser Create(string[] args)
        {
            CmdLineArgParser parser = null;
            if (!cachedParsers.TryGetValue(args, out parser))
            {
                parser = new CmdLineArgParser(args);
                parser.Parse();
                cachedParsers[args] = parser;
            }
            return parser;
        }

        private string[] args;
        private List<string> subcommands = new List<string>();
        private Dictionary<string, string> options = new Dictionary<string, string>();
        public ConversionSuccessHandler DefaultConversionSuccessHandler {get; set; }
        public ConversionErrorHandler DefaultConversionErrorHandler { get; set; }

        private CmdLineArgParser(string[] args)
        {
            this.args = args;
        }

        private void Parse()
        {
            int optionIndex = -1;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg[0] != '-')
                {
                    if (optionIndex < 0)
                        subcommands.Add(arg);
                }
                else
                {
                    string[] parts = arg.Split('=');
                    if (parts.Length == 1)
                    {
                        string nextArg = i < args.Length - 1 ? args[i + 1] : "";
                        // Case: -[-]option value
                        if (nextArg[0] != '-')
                        {
                            options[arg] = nextArg;
                            i++;
                        }
                        else
                        {
                            // Case: -[-]option
                            options[arg] = "";
                        }
                    }
                    else if (parts.Length == 2)
                    {
                        // Case: -[-]option=value
                        options[parts[0]] = parts[1];
                    }
                    else
                    {
                        continue;
                    }
                    optionIndex++;
                }
            }
        }

        public IEnumerable<string> GetSubcommands()
        {
            return subcommands;
        }
        
        public bool HasOption(string optionName)
        {
            return options.ContainsKey(optionName);
        }

        public string GetOptionValue(string optionName, string alias = null)
        {
            string value = null;
            if (!options.TryGetValue(optionName, out value))
            {
                if (!string.IsNullOrEmpty(alias))
                    options.TryGetValue(alias, out value);
            }
            return value;
        }

        private bool GetOptionValueInternal<T>(string optionName, string alias, Func<string, T> converter, ref T result, ConversionSuccessHandler successHandler = null, ConversionErrorHandler errorHandler = null)
        {
            string value = GetOptionValue(optionName, alias);
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    result = converter(value);
                    if (successHandler == null) successHandler = DefaultConversionSuccessHandler;
                    successHandler?.Invoke(optionName, alias, value, result);
                }
                catch (Exception ex)
                {
                    if (errorHandler == null) errorHandler = DefaultConversionErrorHandler;
                    errorHandler?.Invoke(optionName, alias, value, typeof(T), ex);
                    return false;
                }
                return true;
            }
            return false;
        }

        private static T DefaultConverter<T>(string value)
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }

        private static T EnumConverter<T>(string value)
        {
            return (T)Enum.Parse(typeof(T), value);
        }

        private static T EnumIntConverter<T>(string value)
        {
            return (T)(object)int.Parse(value);
        }

        public bool GetOptionValue<T>(string optionName, string alias, ref T result, ConversionSuccessHandler successHandler = null, ConversionErrorHandler errorHandler = null) where T : IConvertible
        {
            return GetOptionValueInternal<T>(optionName, alias, DefaultConverter<T>, ref result, successHandler, errorHandler);
        }

        public bool GetEnumOptionFromString<T>(string optionName, string alias, ref T result, ConversionSuccessHandler successHandler = null, ConversionErrorHandler errorHandler = null) where T : struct
        {
            return GetOptionValueInternal<T>(optionName, alias, EnumConverter<T>, ref result, successHandler, errorHandler);
        }

        public bool GetEnumOptionFromInt<T>(string optionName, string alias, ref T result, ConversionSuccessHandler successHandler = null, ConversionErrorHandler errorHandler = null) where T : struct
        {
            return GetOptionValueInternal<T>(optionName, alias, EnumIntConverter<T>, ref result, successHandler, errorHandler);
        }
    }
}
