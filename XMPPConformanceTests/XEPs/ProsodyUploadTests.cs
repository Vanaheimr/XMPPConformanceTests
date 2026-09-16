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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0363 against Prosody's upload service.
    /// </summary>
    /// <remarks>
    /// <c>mod_http_file_share</c>, which is core in Prosody 13 and which
    /// <c>tools/prosody/setup.sh</c> switches on as a component of its own. The
    /// files travel over the same HTTPS port as the WebSocket - 5281 carries
    /// both, and the set-up says where by giving the component an
    /// <c>http_host</c> of 127.0.0.1.
    ///
    /// Its slot is a JWT in an <c>Authorization</c> header, which is a
    /// particularly clear form of the thing rounds six and seven are about: the
    /// token names the slot, the size and the uploader, and the service verifies
    /// it against all three rather than merely noticing that a token is present.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Prosody)]
    [Category(TestCategories.Wsl)]
    public class ProsodyUploadTests : AForeignPeerUploadTests
    {

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5281/xmpp-websocket");
        protected override Int32   EndpointPort  => 5281;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

    }

}
