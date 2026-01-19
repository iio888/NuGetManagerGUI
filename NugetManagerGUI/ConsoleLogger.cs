using NuGet.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NugetManagerGUI
{
    internal class ConsoleLogger : ILogger
    {
        private readonly Action<string> _action;

        public ConsoleLogger(Action<string> action)
        {
            this._action = action;
        }

        public void Log(LogLevel level, string data)
        {
            _action(data);
        }

        public void Log(ILogMessage message)
        {
            _action(message.Message);
        }

        public Task LogAsync(LogLevel level, string data)
        {
            _action(data);
            return Task.CompletedTask;
        }

        public Task LogAsync(ILogMessage message)
        {
            _action(message.Message);
            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
        }

        public void LogError(string data)
        {
            _action(data);
        }

        public void LogInformation(string data)
        {
            _action(data);
        }

        public void LogInformationSummary(string data)
        {
            _action(data);
        }

        public void LogMinimal(string data)
        {
        }

        public void LogVerbose(string data)
        {
        }

        public void LogWarning(string data)
        {
            _action(data);
        }
    }
}
