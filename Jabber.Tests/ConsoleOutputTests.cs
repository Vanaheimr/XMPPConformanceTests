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

using System.Text;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// The shared console output: it keeps the input line whole - for events
    /// <b>and</b> for the log.
    /// </summary>
    /// <remarks>
    /// Until D58 there was nothing to check here, because the place did not
    /// exist: the event handling bracketed every output by hand, and the
    /// logger wrote past it. Both could only be judged by eye, and were judged
    /// accordingly rarely.
    ///
    /// The checking goes against a <see cref="StringWriter"/> - the same
    /// class, only without a console behind it. The width is given in doing
    /// so: on a test runner without a window there is none, and the test is
    /// meant to erase the line, not to measure the environment.
    /// </remarks>
    [TestFixture]
    public class ConsoleOutputTests
    {

        #region Data

        private StringWriter  _written  = null!;
        private ConsoleOutput _output   = null!;

        private const String Prompt = "> ";

        #endregion

        #region SetUp

        [SetUp]
        public void Prepare()
        {
            _written  = new StringWriter();
            _output   = new ConsoleOutput(() => Prompt, _written, () => 20);
        }

        #endregion


        #region AnUnpromptedLine_RestoresThePrompt()

        /// <summary>
        /// An unasked output erases the line begun and restores the prompt.
        /// </summary>
        /// <remarks>
        /// That is the whole purpose: the user is typing, a message comes in,
        /// and afterwards their prompt stands there again. Without the erasing
        /// the message would stand behind the half word; without the drawing
        /// back the prompt would be gone, and they would type into nothing.
        /// </remarks>
        [Test]
        public void AnUnprompted_LineRestoresThePrompt()
        {

            _output.WriteLine("Message from Bob");

            var text = _written.ToString();

            Assert.Multiple(() =>
            {

                Assert.That(text, Does.StartWith("\r"),
                            "The line begun is not cleared away.");

                Assert.That(text, Does.Contain("Message from Bob"));

                Assert.That(text, Does.EndWith(Prompt),
                            "The prompt is missing after the output.");

            });

        }

        #endregion

        #region ThePromptFollowsTheConversation()

        /// <summary>
        /// The prompt is asked for anew on every output.
        /// </summary>
        /// <remarks>
        /// It changes with the conversation partner. Were it a string instead
        /// of a function, the old one would go on standing there after a
        /// <c>/to</c> - and would do so until the next restart.
        /// </remarks>
        [Test]
        public void ThePromptFollowsTheConversation()
        {

            var partner  = "alice";
            var output   = new ConsoleOutput(() => $"[{partner}] > ", _written, () => 20);

            output.WriteLine("first");
            partner = "bob";
            output.WriteLine("second");

            var text = _written.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("[alice] > "));
                Assert.That(text, Does.EndWith("[bob] > "));
            });

        }

        #endregion

        #region WithoutAWidth_NothingIsErased()

        /// <summary>
        /// Without a console width nothing is erased - but it is still
        /// written.
        /// </summary>
        /// <remarks>
        /// The case of the redirected output: there is no input line there
        /// that would need saving. The old version caught the exception from
        /// <c>Console.WindowWidth</c> and wrote a blank line instead - that
        /// helped nobody and tore every output from the next.
        /// </remarks>
        [Test]
        public void WithoutAWidth_NothingIsErased()
        {

            var output = new ConsoleOutput(() => Prompt, _written, () => 0);

            output.WriteLine("Line");

            // At the start and not anywhere in it: a carriage return also
            // stands at the end of every line under Windows. What has to be
            // missing here is the erase sequence before it - return, blank,
            // return.
            Assert.That(_written.ToString(), Does.StartWith("Line"),
                        "Without a width there is nothing to erase.");

        }

        #endregion

        #region AScope_HoldsTheConsoleUntilItIsLeft()

        /// <summary>
        /// An output scope writes the prompt only on leaving - not after every
        /// piece.
        /// </summary>
        /// <remarks>
        /// For the outputs that come about in several goes: timestamp, sender,
        /// text, each with its own colour. If the prompt came in between, it
        /// would stand in the middle of the line.
        /// </remarks>
        [Test]
        public void AScope_HoldsTheConsoleUntilItIsLeft()
        {

            using (var scope = _output.Begin())
            {
                _written.Write("[12:00:00] ");
                _written.Write("bob: ");
                _written.WriteLine("Hello");

                Assert.That(_written.ToString(), Does.Not.Contain(Prompt),
                            "The prompt came into the middle of the output.");
            }

            Assert.That(_written.ToString(), Does.EndWith(Prompt));

        }

        #endregion

        #region TheLogger_GoesThroughTheSameDoor()

        /// <summary>
        /// A log line clears the input line and restores it - just like a
        /// message.
        /// </summary>
        /// <remarks>
        /// The actual point of D58. An <c>AddSimpleConsole</c> would simply
        /// have written here; that it no longer makes a difference where the
        /// line comes from is the whole change.
        /// </remarks>
        [Test]
        public void TheLogger_GoesThroughTheSameDoor()
        {

            using var provider = new ConsoleOutputLoggerProvider(_output, LogLevel.Information);

            provider.CreateLogger("org.GraphDefined.Vanaheimr.Hermod.XMPP.XMPPConnection")
                    .LogInformation("Connection is up");

            var text = _written.ToString();

            Assert.Multiple(() =>
            {

                Assert.That(text, Does.StartWith("\r"),
                            "The log writes past the input line.");

                Assert.That(text, Does.Contain("Connection is up"));

                Assert.That(text, Does.Contain("info"));

                Assert.That(text, Does.Contain("XMPPConnection"),
                            "The category name belongs in it - but only its last part.");

                Assert.That(text, Does.Not.Contain("org.GraphDefined"),
                            "The full type name eats half the line width.");

                Assert.That(text, Does.EndWith(Prompt));

            });

        }

        #endregion

        #region TheLogger_KeepsQuietBelowItsLevel()

        /// <summary>
        /// What lies below the minimum level does not reach the console.
        /// </summary>
        /// <remarks>
        /// Without this assurance "write everything" would be a passing
        /// solution - and in normal operation the user would get every trace
        /// line of the log into their input line.
        /// </remarks>
        [Test]
        public void TheLogger_KeepsQuietBelowItsLevel()
        {

            using var provider = new ConsoleOutputLoggerProvider(_output, LogLevel.Warning);

            var logger = provider.CreateLogger("Test");

            logger.LogInformation("please not");
            logger.LogWarning("but this");

            Assert.Multiple(() =>
            {
                Assert.That(logger.IsEnabled(LogLevel.Information), Is.False);
                Assert.That(_written.ToString(), Does.Not.Contain("please not"));
                Assert.That(_written.ToString(), Does.Contain("but this"));
            });

        }

        #endregion

        #region TheLogger_NamesTheException()

        /// <summary>
        /// An exception given along stands in the line.
        /// </summary>
        /// <remarks>
        /// <c>ILogger</c> passes the exception through separately from the
        /// text, and the formatter leaves it out. Whoever does not append it
        /// themselves logs "connection lost" and keeps quiet about what it was
        /// down to.
        /// </remarks>
        [Test]
        public void TheLogger_NamesTheException()
        {

            using var provider = new ConsoleOutputLoggerProvider(_output, LogLevel.Information);

            provider.CreateLogger("Test")
                    .LogError(new InvalidOperationException("Socket gone"), "Connection lost");

            var text = _written.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Connection lost"));
                Assert.That(text, Does.Contain("InvalidOperationException"));
                Assert.That(text, Does.Contain("Socket gone"));
            });

        }

        #endregion

        #region ParallelWriters_DoNotInterleave()

        /// <summary>
        /// Two threads writing at the same time do not interleave.
        /// </summary>
        /// <remarks>
        /// The second, less visible part of the change: events come from the
        /// receiving thread, the log from any thread at all. Without the lock
        /// the one line stands in the middle of the other - and the colour the
        /// one has set the other puts back.
        ///
        /// The checking goes by the shape: every output consists of clearing,
        /// text and prompt. If two interleave, somewhere a piece of text
        /// stands without its beginning.
        /// </remarks>
        [Test]
        public void ParallelWriters_DoNotInterleave()
        {

            var slow   = new SlowWriter();
            var output = new ConsoleOutput(() => "|", slow, () => 0);

            Parallel.For(0, 8, i =>
                output.Write(w =>
                {
                    w.Write("<");
                    w.Write(i);
                    w.Write(">");
                }));

            // Every output is "<n>|" - nothing may stand in between.
            Assert.That(slow.ToString(),
                        Does.Match("^(<[0-7]>\\|){8}$"),
                        $"Two outputs have interleaved: {slow}");

        }

        /// <summary>
        /// A writer that takes its time between two calls - so that an
        /// interleaving gets an opportunity at all.
        /// </summary>
        private sealed class SlowWriter : StringWriter
        {

            public override void Write(String? value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

            public override void Write(Char value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

            public override void Write(Int32 value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

        }

        #endregion

    }

}
