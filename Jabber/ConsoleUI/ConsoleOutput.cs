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

using System.Threading;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI
{

    /// <summary>
    /// The one place everything goes to the console through: incoming
    /// messages, system notices and the log output.
    /// </summary>
    /// <remarks>
    /// A console that keeps an input line at the same time has a problem a
    /// pure output does not: <b>the user is typing right now.</b> Whoever
    /// writes in between takes their half-finished line apart and leaves them
    /// without a prompt.
    ///
    /// The event handling has taken that into account from the start and
    /// bracketed every output by hand in "erase the line ... write the prompt
    /// again" - eleven times the same two lines. <b>The logger did not take it
    /// into account</b>, for it knew nothing of all this: an
    /// <c>AddSimpleConsole</c> writes whenever it suits it.
    ///
    /// Here both run together, and under <b>one lock</b>. That is the second,
    /// less visible part: the events come from the receiving thread, the log
    /// from any thread at all, and two simultaneous outputs otherwise
    /// interleave in the middle of a word - together with the colour the one
    /// set and the other put back.
    ///
    /// <b>Why no <c>TextWriter</c> as the abstraction?</b> Because erasing the
    /// line and setting the colour are no writing operations but control. A
    /// writer that knows neither could not do the job; one that knows them
    /// would be this class again.
    /// </remarks>
    public sealed class ConsoleOutput
    {

        #region Data

        private readonly Lock _lock = new();
        private readonly TextWriter _writer;
        private readonly Func<String> _prompt;
        private readonly Func<Int32> _width;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Creates the output.
        /// </summary>
        /// <param name="prompt">
        /// Delivers the prompt as it is to stand again after an output. A
        /// function and not a string: it changes with the conversation
        /// partner.
        /// </param>
        /// <param name="writer">Where it is written to; without a value the console.</param>
        /// <param name="width">
        /// How wide the line is to be erased. Without a value the console
        /// width - and if there is none (redirected output), zero: then there
        /// is nothing to erase either, because nobody is typing there.
        /// </param>
        public ConsoleOutput(Func<String>   prompt,
                             TextWriter?    writer  = null,
                             Func<Int32>?   width   = null)
        {

            _prompt  = prompt;
            _writer  = writer ?? Console.Out;
            _width   = width  ?? ConsoleWidth;

        }

        #endregion


        #region Write(output)

        /// <summary>
        /// Writes something that comes <b>unasked</b>: the line begun gives
        /// way, the output appears, the prompt stands there again.
        /// </summary>
        /// <param name="output">
        /// What is to be written. As a callback and not as a string, so that
        /// multi-line and coloured outputs run as <b>one</b> operation under
        /// the lock. An output that changes the colour halfway would otherwise
        /// be interruptible at exactly that point.
        /// </param>
        public void Write(Action<TextWriter> output)
        {

            using var scope = Begin();

            output(_writer);

        }

        /// <summary>Short form for a single line without colour.</summary>
        public void WriteLine(String line)
            => Write(w => w.WriteLine(line));

        #endregion

        #region Begin()

        /// <summary>
        /// Opens an output scope: the input line gives way, and on leaving,
        /// the prompt stands there again.
        /// </summary>
        /// <remarks>
        /// For outputs that cannot be put into a callback without becoming
        /// unreadable - one that changes the colour in the middle of a
        /// <c>switch</c>, say. The scope holds the lock until it is left; for
        /// that long the console belongs to the caller alone.
        ///
        /// <b>Do not nest:</b> the lock is reentrant, but the inner scope
        /// would already write the prompt on leaving, and the outer one would
        /// put its output behind it.
        /// </remarks>
        public IDisposable Begin()
        {

            _lock.Enter();

            try
            {
                EraseLine();
            }
            catch
            {
                _lock.Exit();
                throw;
            }

            return new Scope(this);

        }

        /// <summary>The end of an output scope.</summary>
        private sealed class Scope : IDisposable
        {

            private readonly ConsoleOutput _output;
            private Boolean _ended;

            internal Scope(ConsoleOutput output)
            {
                _output = output;
            }

            public void Dispose()
            {

                if (_ended)
                    return;

                _ended = true;

                try
                {
                    _output._writer.Write(_output._prompt());
                    _output._writer.Flush();
                }
                finally
                {
                    _output._lock.Exit();
                }

            }

        }

        #endregion

        #region WritePrompt()

        /// <summary>
        /// Writes the prompt - for the caller who is expecting an input right
        /// now.
        /// </summary>
        public void WritePrompt()
        {
            lock (_lock)
            {
                _writer.Write(_prompt());
                _writer.Flush();
            }
        }

        #endregion

        #region (private) Helpers

        private void EraseLine()
        {

            var width = _width();

            if (width <= 0)
                return;

            _writer.Write('\r');
            _writer.Write(new String(' ', width - 1));
            _writer.Write('\r');

        }

        /// <summary>
        /// The console width, or 0 if there is none.
        /// </summary>
        /// <remarks>
        /// <c>Console.WindowWidth</c> throws as soon as the output is
        /// redirected - and precisely then there is no input line to be saved
        /// either. The old version caught the throw and wrote a blank line
        /// instead; that helped nobody and tore every output from the next.
        /// </remarks>
        private static Int32 ConsoleWidth()
        {
            try
            {
                return Console.WindowWidth;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

    }

}
