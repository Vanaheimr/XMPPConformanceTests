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
    /// XEP-0084 against ejabberd's personal eventing service.
    /// </summary>
    /// <remarks>
    /// The second opinion, and for this extension it is more than a formality:
    /// what a server pushes to whom is entirely its own bookkeeping. The access
    /// model a PEP node is created with, whether presence is required before
    /// anything is delivered, and how quickly a caps change is acted on are
    /// three places where two implementations may differ while both being
    /// defensible - and a client written against one of them would notice
    /// nothing until it met the other.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Ejabberd)]
    [Category(TestCategories.Wsl)]
    public class EjabberdAvatarTests : AForeignPeerAvatarTests
    {

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5443/websocket");
        protected override Int32   EndpointPort  => 5443;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

    }

}
