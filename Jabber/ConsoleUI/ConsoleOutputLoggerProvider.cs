/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.ConsoleUI
{

    /// <summary>
    /// Ein <see cref="ILoggerProvider"/>, der seine Zeilen über
    /// <see cref="ConsoleOutput"/> schickt statt an der Eingabezeile vorbei.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Unterschied zu <c>AddSimpleConsole</c>: Dieselbe
    /// Zeile, aber durch dieselbe Tür wie alles andere. Damit steht sie nicht
    /// mehr mitten in einer halb getippten Eingabe, und die
    /// Eingabeaufforderung ist danach wieder da.
    ///
    /// Die Mindeststufe steht hier und nicht in der Filterkette des
    /// Fabrikanten, damit ein Aufrufer diesen Anbieter auch einzeln benutzen
    /// kann; <c>SetMinimumLevel</c> wirkt zusätzlich.
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
    /// Die Protokollzeile, wie sie in der Konsole erscheint.
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

            _output.Write(w => w.WriteLine($"{DateTime.Now:HH:mm:ss} {Kuerzel(logLevel)} " +
                                           $"{Kurzname(_category)}: {text}"));

        }


        /// <summary>Die Stufe in vier Zeichen, wie sie eine Konsole verträgt.</summary>
        internal static String Kuerzel(LogLevel level)
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
        /// Der letzte Teil des Kategorienamens.
        /// </summary>
        /// <remarks>
        /// Der volle Name ist der Typname samt Namensraum und damit in dieser
        /// Sammlung rund fünfzig Zeichen - auf einer Konsole, die zugleich eine
        /// Eingabezeile führt, ist das die halbe Breite für eine Auskunft, die
        /// in jeder Zeile dieselbe ist.
        /// </remarks>
        internal static String Kurzname(String category)
        {

            var punkt = category.LastIndexOf('.');

            return punkt >= 0 && punkt < category.Length - 1
                       ? category[(punkt + 1)..]
                       : category;

        }

    }

}
