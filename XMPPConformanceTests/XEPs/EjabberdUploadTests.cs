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
    /// XEP-0363 against ejabberd's upload service.
    /// </summary>
    /// <remarks>
    /// The second far side, for the reason there is one at all: where Prosody
    /// and this client happen to agree, a single counterpart cannot tell
    /// agreement from correctness.
    ///
    /// Here the two implementations are about as far apart as they get. Prosody
    /// puts a signed token in an <c>Authorization</c> header and verifies it;
    /// ejabberd's <c>mod_http_upload</c> keeps the slot itself and the URL is
    /// the whole of the credential, so the same eight rounds are answered by two
    /// genuinely different mechanisms. Rounds six and seven are where that
    /// shows.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Ejabberd)]
    [Category(TestCategories.Wsl)]
    public class EjabberdUploadTests : AForeignPeerUploadTests
    {

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override URL     Endpoint      => URL.Parse("wss://127.0.0.1:5443/websocket");
        protected override Int32   EndpointPort  => 5443;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

    }

}
