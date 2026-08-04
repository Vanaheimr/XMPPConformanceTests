/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using Microsoft.Extensions.Logging;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI
{

    /// <summary>
    /// An <see cref="ILoggerProvider"/> that sends its lines over
    /// <see cref="ConsoleOutput"/> instead of past the input line.
    /// </summary>
    /// <remarks>
    /// That is the whole difference from <c>AddSimpleConsole</c>: the same
    /// line, but through the same door as everything else. With that it no
    /// longer stands in the middle of a half-typed input, and the prompt is
    /// back afterwards.
    ///
    /// The minimum level stands here and not in the filter chain of the
    /// factory, so that a caller can use this provider on its own as well;
    /// <c>SetMinimumLevel</c> takes effect on top.
    /// </remarks>
    public sealed class ConsoleOutputLoggerProvider : ILoggerProvider
    {

        #region Data

        private readonly ConsoleOutput _output;
        private readonly LogLevel _minimum;

        #endregion

        #region Constructor(s)

        public ConsoleOutputLoggerProvider(ConsoleOutput  output,
                                           LogLevel       minimum  = LogLevel.Information)
        {
            _output   = output;
            _minimum  = minimum;
        }

        #endregion


        public ILogger CreateLogger(String categoryName)
            => new ConsoleOutputLogger(_output, _minimum, categoryName);

        public void Dispose()
        { }

    }

    /// <summary>
    /// The log line as it appears in the console.
    /// </summary>
    internal sealed class ConsoleOutputLogger : ILogger
    {

        #region Data

        private readonly ConsoleOutput _output;
        private readonly LogLevel _minimum;
        private readonly String _category;

        #endregion

        internal ConsoleOutputLogger(ConsoleOutput  output,
                                     LogLevel       minimum,
                                     String         category)
        {
            _output    = output;
            _minimum   = minimum;
            _category  = category;
        }


        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public Boolean IsEnabled(LogLevel logLevel)
            => logLevel >= _minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel                          logLevel,
                                EventId                           eventId,
                                TState                            state,
                                Exception?                        exception,
                                Func<TState, Exception?, String>  formatter)
        {

            if (!IsEnabled(logLevel))
                return;

            var text = formatter(state, exception);

            if (exception is not null)
                text += $" ({exception.GetType().Name}: {exception.Message})";

            _output.Write(w => w.WriteLine($"{DateTime.Now:HH:mm:ss} {Tag(logLevel)} " +
                                           $"{ShortName(_category)}: {text}"));

        }


        /// <summary>The level in four characters, as a console can bear it.</summary>
        internal static String Tag(LogLevel level)
            => level switch {
                   LogLevel.Trace        => "trce",
                   LogLevel.Debug        => "dbug",
                   LogLevel.Information  => "info",
                   LogLevel.Warning      => "warn",
                   LogLevel.Error        => "fail",
                   LogLevel.Critical     => "crit",
                   _                     => "none"
               };

        /// <summary>
        /// The last part of the category name.
        /// </summary>
        /// <remarks>
        /// The full name is the type name together with its namespace, and
        /// thereby around fifty characters in this collection - on a console
        /// that keeps an input line at the same time, that is half the width
        /// for a piece of information that is the same in every line.
        /// </remarks>
        internal static String ShortName(String category)
        {

            var dot = category.LastIndexOf('.');

            return dot >= 0 && dot < category.Length - 1
                       ? category[(dot + 1)..]
                       : category;

        }

    }

}
