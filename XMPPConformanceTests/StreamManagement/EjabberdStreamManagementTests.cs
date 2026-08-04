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

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0198 including resumption against ejabberd 24.12 - the same trial as
    /// against Prosody, at a second counterpart.
    /// </summary>
    /// <remarks>
    /// With XEP-0288 the two diverged: ejabberd put the enabling element into
    /// the stream features instead of the announcement, and we therefore
    /// overlooked its offer. Prosody alone would never have shown that.
    /// <c>mod_stream_mgmt</c> is the next opportunity for a divergence that no
    /// single server makes visible.
    ///
    /// The setup stands in <c>tools/ejabberd/setup.sh</c>: ejabberd gets an
    /// <c>ejabberd_http_ws</c> handler on 5443, <c>mod_stream_mgmt</c> and two
    /// accounts. The endpoint is called <c>/websocket</c> and not
    /// <c>/xmpp-websocket</c> as with Prosody - RFC 7395 prescribes no path,
    /// and whoever hard-wires one gets into only one of the two.
    ///
    /// What is checked stands in
    /// <see cref="AForeignPeerStreamManagementTests"/>.
    /// </remarks>
    [TestFixture]
    [Category("ejabberd")]
    public class EjabberdStreamManagementTests : AForeignPeerStreamManagementTests
    {

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override String  Endpoint      => "wss://127.0.0.1:5443/websocket";
        protected override Int32   EndpointPort  => 5443;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

    }

}
