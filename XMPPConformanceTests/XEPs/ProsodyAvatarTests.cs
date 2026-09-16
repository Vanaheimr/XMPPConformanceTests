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
    /// XEP-0084 against Prosody's personal eventing service.
    /// </summary>
    /// <remarks>
    /// <b>mod_pep, and this comment used to say the opposite.</b> It claimed
    /// Prosody carried personal eventing on every VirtualHost as part of
    /// pubsub and that nothing had to be switched on - which sounded like
    /// knowledge and was a guess. Every publish came back
    /// &lt;service-unavailable/&gt; until <c>"pep"</c> went into
    /// modules_enabled.
    ///
    /// It is the fourth time in this suite: MUC, the archive, the upload
    /// service and now this. The module was always there and always had to be
    /// asked for.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Prosody)]
    [Category(TestCategories.Wsl)]
    public class ProsodyAvatarTests : AForeignPeerAvatarTests
    {

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5281/xmpp-websocket");
        protected override Int32   EndpointPort  => 5281;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

    }

}
