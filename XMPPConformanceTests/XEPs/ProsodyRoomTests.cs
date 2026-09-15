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
    /// XEP-0045 against Prosody's room service.
    /// </summary>
    /// <remarks>
    /// The rooms live on a component of their own, <c>conference.prosody.test</c>,
    /// which <c>tools/prosody/setup.sh</c> switches on. A component is a domain
    /// beside the host and not a corner of it, so it needs its own name on the
    /// certificate - the setup puts both into one SAN.
    ///
    /// The clients reach Prosody over its WebSocket endpoint, the same way the
    /// stream-management lane does. No federation is involved: both accounts
    /// live on Prosody, and the room is Prosody's own business.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Prosody)]
    [Category(TestCategories.Wsl)]
    public class ProsodyRoomTests : AForeignPeerRoomTests
    {

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override String  RoomDomain    => "conference.prosody.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5281/xmpp-websocket");
        protected override Int32   EndpointPort  => 5281;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

    }

}
