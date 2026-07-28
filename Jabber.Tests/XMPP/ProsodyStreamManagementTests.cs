/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
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

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0198 samt Wiederaufnahme gegen Prosody 13.
    /// </summary>
    /// <remarks>
    /// Der Aufbau steht in <c>tools/prosody/setup.sh</c>: Prosody bekommt
    /// <c>mod_smacks</c> und <c>mod_websocket</c>, einen HTTPS-Endpunkt auf
    /// 5281 und zwei Konten. Unser Client spricht XMPP über WebSocket
    /// (RFC 7395), nicht über den rohen 5222er-Strom - der Weg dorthin ist
    /// also <c>wss://</c>, und ohne <c>mod_websocket</c> gäbe es gar keinen.
    ///
    /// Was geprüft wird, steht in
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
