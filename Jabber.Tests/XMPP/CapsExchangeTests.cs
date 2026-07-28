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

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0115 zwischen zwei echten Clients: Ankündigung in der Presence,
    /// disco#info-Abfrage, Prüfung, Cache.
    /// </summary>
    /// <remarks>
    /// Die Prüfung selbst ist in <see cref="CapsVerificationTests"/> für sich
    /// belegt — dort mit Antworten, die ein ehrlicher Client gar nicht geben
    /// könnte. Hier geht es um die andere Hälfte: dass der ganze Weg unter
    /// echten Bedingungen bis in den Cache führt.
    ///
    /// Das ist keine Formsache. Der <c>hash</c>-Wert wird an genau einer Stelle
    /// aus der Presence herausgelesen und weitergereicht; fiele er dort weg,
    /// wäre jede Antwort unprüfbar und der Cache damit dauerhaft leer. Kein
    /// Test der Prüfung selbst würde das bemerken — die Aushandlung liefe
    /// weiter, nur eben ohne den Nutzen, für den es XEP-0115 gibt.
    ///
    /// Nebenbei belegt der Test, dass unser eigenes <c>ver</c> zu unserer
    /// eigenen disco#info-Antwort passt: Angekündigt wird aus
    /// <c>LocalIdentities</c>/<c>LocalFeatures</c>, geantwortet ebenso, und
    /// hier rechnet die Gegenseite beides gegeneinander nach.
    /// </remarks>
    [TestFixture]
    public class CapsExchangeTests : AXMPPTests
    {

        #region CapsOfARealContact_AreVerifiedAndCached()

        /// <summary>
        /// Bob sieht Alices Presence, fragt nach, rechnet nach und legt ab.
        /// </summary>
        [Test]
        public async Task CapsOfARealContact_AreVerifiedAndCached()
        {

            MakeContacts("alice", "bob");

            var alice  = await ConnectClientAsync("alice");
            var bob    = await ConnectClientAsync("bob");

            var abgelehnt = new List<String>();
            bob.Connection.EntityCaps!.OnCapsRejected += (from, grund) => abgelehnt.Add(grund);

            var aliceNode  = alice.Connection.EntityCaps!.Node;
            var aliceVer   = alice.Connection.EntityCaps!.CalculateVerificationString();
            var schluessel = $"{aliceNode}#{aliceVer}";

            await WaitFor(() => bob.Connection.EntityCaps!.GetCachedInfo(schluessel) is not null,
                          "Alices geprüfte Capabilities in Bobs Cache");

            var abgelegt = bob.Connection.EntityCaps!.GetCachedInfo(schluessel)!;

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt, Is.Empty,
                            $"Die eigene Ankündigung wurde abgelehnt: {String.Join(" | ", abgelehnt)}");

                // Die Gegenprobe zur Prüfung: Was da liegt, ergibt den Hash,
                // unter dem es liegt.
                Assert.That(EntityCapsManager.VerificationString(abgelegt.Identities,
                                                                 abgelegt.Features),
                            Is.EqualTo(aliceVer));

                Assert.That(abgelegt.Features, Does.Contain("urn:xmpp:receipts"));

            });

        }

        #endregion

    }

}
