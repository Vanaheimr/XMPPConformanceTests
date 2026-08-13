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

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// NUnit category names used to gate tests on external prerequisites.
    /// Default CI filter: <c>TestCategory!=WSL</c>.
    /// </summary>
    /// <remarks>
    /// The same arrangement the DNS and NTS conformance suites use, and it
    /// arrives here for the reason theirs exists: a workflow has to be able to
    /// name a *class of prerequisite*, not a fixture. Until now the gate
    /// excluded <c>FullyQualifiedName!~OmemoOracleTests</c> - a filter that
    /// says which type to leave out and never why, and that goes stale the
    /// moment a second fixture needs the same treatment.
    ///
    /// <para>
    /// What makes the split worth having is that it lets <b>both</b> lanes
    /// expect zero skips. Unfiltered, this suite reports "2 passed, 27 skipped"
    /// on a bare runner and "29 passed" in the container, and both are green -
    /// so the one number that says whether anything was measured has to be read
    /// by hand every time. Filtered, the gate owes 2 passed / 0 skipped and the
    /// interop lane owes 27 passed / 0 skipped, and any skip at all is a
    /// finding. This project has twice been caught by a green run that measured
    /// nothing (D54, D97).
    /// </para>
    /// </remarks>
    public static class TestCategories
    {

        /// <summary>
        /// Needs a POSIX far side that a bare hosted runner does not have:
        /// Prosody, ejabberd or python-omemo, set up by <c>tools/</c> and
        /// <c>XEPs/Oracle/fetch_oracle.py</c>. On the Windows developer machine
        /// that is WSL, in CI it is the debian:13 container, where the peers are
        /// native and on the same loopback.
        /// </summary>
        /// <remarks>
        /// The name is <c>WSL</c> rather than something truer like
        /// <c>ForeignPeer</c> so that the filter reads the same here as in the
        /// DNS and NTS suites. Everything under this category is expected to
        /// <c>Assert.Ignore</c> with a reason when its far side is absent -
        /// never to fail. That is not free: it took the oracle until d39656e to
        /// stop throwing a <c>Win32Exception</c> out of its <c>[OneTimeSetUp]</c>
        /// on any host without <c>wsl.exe</c>, which NUnit turns into failures
        /// rather than skips.
        /// </remarks>
        public const String Wsl       = "WSL";

        /// <summary>Needs the Prosody set-up from <c>tools/prosody/setup.sh</c>.</summary>
        public const String Prosody   = "Prosody";

        /// <summary>Needs the ejabberd set-up from <c>tools/ejabberd/setup.sh</c>.</summary>
        /// <remarks>
        /// Lower case, because that is how the project spells itself and how
        /// this string already stood in the two fixtures. A filter somebody has
        /// saved keeps working.
        /// </remarks>
        public const String Ejabberd  = "ejabberd";

        /// <summary>Needs python-omemo, the reference implementation for <c>urn:xmpp:omemo:2</c>.</summary>
        public const String Omemo     = "OMEMO";

    }

}
