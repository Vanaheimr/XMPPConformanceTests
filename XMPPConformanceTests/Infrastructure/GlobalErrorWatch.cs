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

using NUnit.Framework;
using NUnit.Framework.Interfaces;

using org.GraphDefined.Vanaheimr.Ratatoskr.Server;

#endregion

// For every test of this collection, without a fixture having to do anything
// for it. Precisely therein lies the point: what depends on a line in every
// fixture depends on whoever writes it.
[assembly: org.GraphDefined.Vanaheimr.Ratatoskr.Tests.GlobalErrorWatch]

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// The watch over all servers of the collection: it hangs itself on every
    /// <see cref="XMPPServer"/> that comes into being anywhere, and lets the
    /// test fail when the processing of a frame has ended with an exception.
    /// </summary>
    /// <remarks>
    /// <see cref="InternalErrorGuard"/> does the same per fixture and stays —
    /// tests that want to <i>look at</i> the reports need it. What it lacked was
    /// the certainty: it hung on two lines every fixture had to write itself
    /// (<c>Watched(…)</c> and <c>AssertClean()</c>). Both can be forgotten, and
    /// their absence does not report itself — <b>a server without a guard
    /// swallows exceptions just as noiselessly as before the guard</b>. Up to
    /// here that was secured by a source inspection by hand ("no <c>new
    /// XMPPServer(</c> without <c>Watched(…)</c>", see D19).
    ///
    /// This watch knows every server through
    /// <see cref="XMPPServer.OnInstanceCreated"/> and needs nobody's cooperation
    /// for it. With that the wiring is no longer a property somebody has to
    /// establish but one that holds of itself.
    ///
    /// <b>On the order:</b> an action attribute with
    /// <see cref="ActionTargets.Test"/> at assembly level encloses every test
    /// <i>together with</i> its SetUp and TearDown. The server therefore comes
    /// into being after <see cref="BeforeTest"/> and is cleared away before
    /// <see cref="AfterTest"/> — both are necessary so that nothing slips
    /// through here and nothing hangs over from the previous test.
    /// </remarks>
    internal sealed class GlobalErrorWatchAttribute : Attribute, ITestAction
    {

        #region Data

        private static readonly List<String> _errors = [];
        private static readonly Lock _lock = new();
        private static Boolean _expected;
        private static Boolean _armed;

        #endregion

        #region Properties

        /// <summary>
        /// What was reported in this test.
        /// </summary>
        internal static IReadOnlyList<String> Errors
        {
            get { lock (_lock) return _errors.ToList(); }
        }

        /// <summary>
        /// Runs for every test separately, not once for the whole collection.
        /// </summary>
        public ActionTargets Targets => ActionTargets.Test;

        #endregion


        #region BeforeTest(test)

        public void BeforeTest(ITest test)
        {

            lock (_lock)
            {

                _errors.Clear();
                _expected = false;

                // Once for the whole run: the event is static, a second
                // subscription would count every report twice.
                if (!_armed)
                {
                    XMPPServer.OnInstanceCreated += Attach;
                    _armed = true;
                }

            }

        }

        #endregion

        #region AfterTest(test)

        public void AfterTest(ITest test)
        {

            List<String> reported;

            lock (_lock)
            {

                if (_expected)
                    return;

                reported = [.. _errors];

            }

            Assert.That(reported, Is.Empty,
                        "A server has reported an exception while processing a frame. " +
                        "That is a programming error in the delivery route and no result of this " +
                        "test:" + Environment.NewLine +
                        String.Join(Environment.NewLine, reported));

        }

        #endregion

        #region Expect()

        /// <summary>
        /// Tells the watch that this test triggers an internal error on
        /// purpose.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="InternalErrorGuard.Expect"/>, so that a fixture
        /// has to say it in only one place.
        /// </remarks>
        internal static void Expect()
        {
            lock (_lock)
                _expected = true;
        }

        #endregion

        #region Record(error)

        /// <summary>
        /// Takes a report in.
        /// </summary>
        /// <remarks>
        /// Separate from the hanging-on to the server, so that the watch itself
        /// is checkable - the same reason as with
        /// <see cref="InternalErrorGuard.Record"/>. Without this separation it
        /// could only be shown that it stays silent when nothing was reported;
        /// that it <b>lets the test fail</b> when something was would stay
        /// unproven. A watch that always lets things through is otherwise
        /// noticed by nobody, and precisely that would be the worst version: it
        /// looks like a safeguard and is none.
        /// </remarks>
        internal static void Record(String error)
        {
            lock (_lock)
                _errors.Add(error);
        }

        #endregion

        #region (private, static) Attach(server)

        private static void Attach(XMPPServer server)

            => server.OnInternalError += (timestamp, sender, session, frame, e, ct) =>
               {
                   Record($"{e.GetType().Name}: {e.Message}" +
                          Environment.NewLine +
                          $"    at the frame: {frame}");
                   return Task.CompletedTask;
               };

        #endregion

    }

}
