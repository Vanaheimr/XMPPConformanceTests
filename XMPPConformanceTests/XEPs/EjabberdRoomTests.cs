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
    /// XEP-0045 against ejabberd's room service.
    /// </summary>
    /// <remarks>
    /// The second far side, and the reason for having one: where Prosody and
    /// this client happen to agree, a single counterpart cannot tell agreement
    /// from correctness. The seven rounds are the same ones; what differs is who
    /// answers them.
    ///
    /// Two places where the two services were already known to be free to differ
    /// - and where the specification leaves them free:
    ///
    /// <list type="bullet">
    ///   <item>whether a room that a join created is <b>locked</b> until it is
    ///         configured;</item>
    ///   <item>whether a message gets a <c>&lt;stanza-id/&gt;</c> of the room's
    ///         at all. Prosody attaches one only for a room that archives, which
    ///         is why archiving is switched on in both set-ups.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Ejabberd)]
    [Category(TestCategories.Wsl)]
    public class EjabberdRoomTests : AForeignPeerRoomTests
    {

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override String  RoomDomain    => "conference.ejabberd.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5443/websocket");
        protected override Int32   EndpointPort  => 5443;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

    }

}
