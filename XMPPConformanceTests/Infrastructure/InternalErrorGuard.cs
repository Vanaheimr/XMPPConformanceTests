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

using org.GraphDefined.Vanaheimr.Ratatoskr.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// The guard against swallowed programming errors: it hangs itself on
    /// <see cref="XMPPServer.OnInternalError"/> and lets the test fail when the
    /// processing of a frame has ended with an exception.
    /// </summary>
    /// <remarks>
    /// Until recently a <c>catch</c> without a filter stood around the
    /// processing of a frame, with the note "connection cut off - the normal
    /// case in a test". A measurement over the whole collection caught <b>not a
    /// single</b> exception there: what the catch still achieved was the
    /// noiseless swallowing of programming errors. In D15 a mutation survived
    /// only because its <c>NullReferenceException</c> vanished there.
    ///
    /// A class of its own and not a method in <see cref="AXMPPTests"/>, because
    /// the guard must not hang on the inheritance: several fixtures run servers
    /// of their own without this base, and precisely there - between two servers
    /// - lay the case that uncovered the error.
    ///
    /// There is deliberately no filtering. A list of exceptions that a cut-off
    /// "really" produces would be guesswork; the measurement says that none of
    /// them occurs. Every report therefore counts as a defect until the opposite
    /// is shown - and if a cut-off is among them after all, the report names its
    /// type and the case is settled in one go instead of staying invisible for
    /// ever.
    /// </remarks>
    internal sealed class InternalErrorGuard
    {

        #region Data

        private readonly List<String> _errors = [];
        private readonly Lock _lock = new();
        private Boolean _expected;

        #endregion

        #region Properties

        /// <summary>The internal errors reported so far.</summary>
        public IReadOnlyList<String> Errors
        {
            get { lock (_lock) return _errors.ToList(); }
        }

        #endregion


        /// <summary>
        /// Begins a new test: discard everything reported and arm again.
        /// </summary>
        public void Reset()
        {

            lock (_lock)
                _errors.Clear();

            _expected = false;

        }

        /// <summary>
        /// Hangs the guard on a server. Callable any number of times - a test
        /// with two servers guards both.
        /// </summary>
        public void Watch(XMPPServer server)

            => server.OnInternalError += (session, frame, e)
                   => Record(e.GetType().Name + ": " + e.Message, frame);

        /// <summary>
        /// Like <see cref="Watch"/>, but gives the server back - so that a
        /// <c>new XMPPServer(…)</c> can be wrapped at the place where it stands.
        /// </summary>
        /// <remarks>
        /// Several fixtures create their servers not in the SetUp but in the
        /// middle of the test. For those a separate <see cref="Watch"/> line
        /// would be a second place one can forget at the next server; this way
        /// the guard stands where the server comes into being.
        /// </remarks>
        public XMPPServer Watched(XMPPServer server)
        {

            Watch(server);

            return server;

        }

        /// <summary>
        /// Takes a report in.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Watch"/> so that the guard itself is
        /// checkable: without this separation it could only be shown that it
        /// stays silent when nothing was reported - but not that it actually
        /// lets the test fail when something was. A guard that always lets
        /// things through is otherwise noticed by nobody; precisely this
        /// mutation survived before this route existed.
        /// </remarks>
        public void Record(String error, String frame)
        {
            lock (_lock)
                _errors.Add($"{error}{Environment.NewLine}    at the frame: {frame}");
        }

        /// <summary>
        /// Tells the guard that this test triggers an internal error on
        /// purpose.
        /// </summary>
        /// <remarks>
        /// Passed on to <see cref="GlobalErrorWatchAttribute"/>: since the watch
        /// over all servers exists, it sees the error as well, and a fixture
        /// shall still have to say its intention in only one place.
        /// </remarks>
        public void Expect()
        {
            _expected = true;
            GlobalErrorWatchAttribute.Expect();
        }

        /// <summary>
        /// Lets the test fail when something was reported - to be called in the
        /// teardown.
        /// </summary>
        public void AssertClean()
        {

            if (_expected)
                return;

            var reported = Errors;

            Assert.That(reported, Is.Empty,
                        "The server has reported an exception while processing a frame. " +
                        "That is a programming error in the delivery route and " +
                        "no result of this test:" + Environment.NewLine +
                        String.Join(Environment.NewLine, reported));

        }

    }

}
