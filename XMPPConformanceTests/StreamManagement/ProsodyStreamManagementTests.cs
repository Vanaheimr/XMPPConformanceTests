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
    /// XEP-0198 including resumption against Prosody 13.
    /// </summary>
    /// <remarks>
    /// The setup stands in <c>tools/prosody/setup.sh</c>: Prosody gets
    /// <c>mod_smacks</c> and <c>mod_websocket</c>, an HTTPS endpoint on 5281
    /// and two accounts. Our client speaks XMPP over WebSocket (RFC 7395), not
    /// over the raw 5222 stream - the way there is therefore <c>wss://</c>, and
    /// without <c>mod_websocket</c> there would be none at all.
    ///
    /// What is checked stands in
    /// <see cref="AForeignPeerStreamManagementTests"/>.
    /// </remarks>
    [TestFixture]
    [Category("Prosody")]
    public class ProsodyStreamManagementTests : AForeignPeerStreamManagementTests
    {

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override String  Endpoint      => "wss://127.0.0.1:5281/xmpp-websocket";
        protected override Int32   EndpointPort  => 5281;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

    }

}
