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
    /// XEP-0198 samt Wiederaufnahme gegen ejabberd 24.12 - dieselbe Probe wie
    /// gegen Prosody, an einem zweiten Gegenüber.
    /// </summary>
    /// <remarks>
    /// Bei XEP-0288 wichen die beiden voneinander ab: ejabberd legte in die
    /// Stream-Features das Freischalt-Element statt der Ankündigung, und wir
    /// übersahen sein Angebot deshalb. Prosody allein hätte das nie gezeigt.
    /// <c>mod_stream_mgmt</c> ist die nächste Gelegenheit für eine Abweichung,
    /// die kein einzelner Server sichtbar macht.
    ///
    /// Der Aufbau steht in <c>tools/ejabberd/setup.sh</c>: ejabberd bekommt
    /// einen <c>ejabberd_http_ws</c>-Handler auf 5443, <c>mod_stream_mgmt</c>
    /// und zwei Konten. Der Endpunkt heisst <c>/websocket</c> und nicht
    /// <c>/xmpp-websocket</c> wie bei Prosody - RFC 7395 schreibt keinen Pfad
    /// vor, und wer ihn fest verdrahtet, kommt nur bei einem der beiden
    /// hinein.
    ///
    /// Was geprüft wird, steht in
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
