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

        #region AnIdentityWithXmlLang_SurvivesTheRoundTrip()

        /// <summary>
        /// Führt eine Entity ihren Namen in einer Sprache, muss sie das in
        /// ihrer eigenen Antwort auch sagen.
        /// </summary>
        /// <remarks>
        /// Angekündigt wird ein Hash über <c>category/type/lang/name</c>.
        /// Bliebe das <c>xml:lang</c> in der disco#info-Antwort weg, errechnete
        /// die Gegenstelle einen anderen Wert als den angekündigten und lehnte
        /// uns ab — wir wären für jeden, der nach XEP-0115 §5.4 prüft, ein
        /// Lügner.
        ///
        /// Der Weg dorthin führt über zwei Stellen in verschiedenen Dateien
        /// (Ankündigung und Antwort); nur zusammen ergeben sie einen Sinn, und
        /// nur hier laufen sie gegeneinander.
        /// </remarks>
        [Test]
        public async Task AnIdentityWithXmlLang_SurvivesTheRoundTrip()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");

            // Die Identität wechselt nach dem Verbinden - die Manager
            // entstehen erst dort. Die Presence muss deshalb noch einmal
            // hinaus, sonst steht bei Bob der alte ver-Wert.
            alice.Connection.Disco!.LocalIdentities.Clear();
            alice.Connection.Disco!.LocalIdentities.Add(
                new DiscoIdentity("client", "pc", "Psi 0.11", "en"));

            await alice.Connection.SendPresenceAsync();

            var neuerVer = alice.Connection.EntityCaps!.CalculateVerificationString();

            // Siehe OwnDataForm_SurvivesTheRoundTrip: erst wenn die neue
            // Presence beim Server steht, ist die Ankündigung wieder mit der
            // Antwort in Übereinstimmung.
            await WaitFor(() => Server.SessionOf(alice.FullJid)?.LastPresence?
                                      .Contains(neuerVer, StringComparison.Ordinal) == true,
                          "Alices neue Presence beim Server");

            var bob = await ConnectClientAsync("bob");

            var abgelehnt = new List<String>();
            bob.Connection.EntityCaps!.OnCapsRejected += (from, grund) => abgelehnt.Add(grund);

            var schluessel = $"{alice.Connection.EntityCaps!.Node}#{neuerVer}";

            await WaitFor(() => bob.Connection.EntityCaps!.GetCachedInfo(schluessel) is not null,
                          "Alices geprüfte Capabilities in Bobs Cache");

            var abgelegt = bob.Connection.EntityCaps!.GetCachedInfo(schluessel)!;

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt, Is.Empty,
                            $"Die eigene Ankündigung wurde abgelehnt: {String.Join(" | ", abgelehnt)}");

                Assert.That(abgelegt.Identities[0].Language, Is.EqualTo("en"),
                            "Die Sprache muss in der eigenen Antwort stehen.");

            });

        }

        #endregion

        #region OwnDataForm_SurvivesTheRoundTrip()

        /// <summary>
        /// XEP-0128: Was in <c>LocalForms</c> steht, geht in die eigene
        /// disco#info-Antwort <b>und</b> in den angekündigten Hash.
        /// </summary>
        /// <remarks>
        /// Die beiden gehören zusammen, liegen aber in verschiedenen Dateien -
        /// die Ankündigung im <c>EntityCapsManager</c>, die Antwort im
        /// <c>DiscoManager</c>. Ginge das Formular nur in eines von beiden ein,
        /// wären wir für jede Gegenstelle, die nach XEP-0115 §5.4 nachrechnet,
        /// ein Fälscher: angekündigter und errechneter Hash gingen auseinander,
        /// und zwar bei völlig ehrlicher Auskunft.
        ///
        /// Der Wert mit <c>&amp;</c> und <c>&lt;</c> ist kein Übermut: Er geht
        /// durch XML und muss unbeschädigt wieder herauskommen. Käme er
        /// verändert an, stimmte auch der Hash nicht mehr - der Fehler sähe
        /// dann aus wie eine Fälschung.
        /// </remarks>
        [Test]
        public async Task OwnDataForm_SurvivesTheRoundTrip()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");

            alice.Connection.Disco!.LocalForms.Add(
                DiscoForm.SoftwareInfo(Software:         "Jabber & Co <Testfassung>",
                                       SoftwareVersion:  "0.1",
                                       OperatingSystem:  "Windows"));

            await alice.Connection.SendPresenceAsync();

            var neuerVer = alice.Connection.EntityCaps!.CalculateVerificationString();

            // Bob darf erst danach kommen. Zwischen der geänderten Auskunft und
            // der neuen Presence liegt ein Fenster, in dem Alice noch den alten
            // ver-Wert angekündigt hat und schon die neue Antwort gäbe - wer
            // darin fragt, bekommt zu Recht eine Abweichung gemeldet. Das ist
            // kein Fehler, sondern der Preis dafür, dass sich Capabilities
            // ändern können; nur hat er in diesem Test nichts zu suchen.
            await WaitFor(() => Server.SessionOf(alice.FullJid)?.LastPresence?
                                      .Contains(neuerVer, StringComparison.Ordinal) == true,
                          "Alices neue Presence beim Server");

            var bob = await ConnectClientAsync("bob");

            var abgelehnt = new List<String>();
            bob.Connection.EntityCaps!.OnCapsRejected += (from, grund) => abgelehnt.Add(grund);

            var schluessel = $"{alice.Connection.EntityCaps!.Node}#{neuerVer}";

            await WaitFor(() => bob.Connection.EntityCaps!.GetCachedInfo(schluessel) is not null,
                          "Alices geprüfte Capabilities in Bobs Cache");

            var abgelegt  = bob.Connection.EntityCaps!.GetCachedInfo(schluessel)!;
            var formular  = abgelegt.Forms.SingleOrDefault();

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt, Is.Empty,
                            $"Die eigene Ankündigung wurde abgelehnt: {String.Join(" | ", abgelehnt)}");

                Assert.That(formular, Is.Not.Null, "Das Formular fehlt in der Antwort.");

                Assert.That(formular!.FormType,
                            Is.EqualTo("urn:xmpp:dataforms:softwareinfo"));

                Assert.That(formular.Fields.Single(f => f.Var == "software").Values.Single(),
                            Is.EqualTo("Jabber & Co <Testfassung>"),
                            "Der Wert muss die XML-Runde unbeschädigt überstehen.");

                Assert.That(formular.Fields.Any(f => f.Var == "os_version"), Is.False,
                            "Eine nicht angegebene Auskunft darf kein leeres Feld werden.");

            });

        }

        #endregion

        #region WithoutOwnForms_NothingIsAnnounced()

        /// <summary>
        /// Die Gegenprobe zur Vorgabe: Ohne eigenes Zutun veröffentlicht
        /// dieser Client keine erweiterten Angaben.
        /// </summary>
        /// <remarks>
        /// Software, Fassung und Betriebssystem sind genau die Angaben, aus
        /// denen sich ein Gerät wiedererkennen lässt, und jeder Kontakt bekommt
        /// sie ungefragt. Dass die Liste leer anfängt, ist deshalb eine
        /// Entscheidung und kein Zufall - und gehört abgesichert wie jede
        /// andere.
        /// </remarks>
        [Test]
        public async Task WithoutOwnForms_NothingIsAnnounced()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var schluessel = $"{alice.Connection.EntityCaps!.Node}#" +
                             $"{alice.Connection.EntityCaps!.CalculateVerificationString()}";

            await WaitFor(() => bob.Connection.EntityCaps!.GetCachedInfo(schluessel) is not null,
                          "Alices geprüfte Capabilities in Bobs Cache");

            Assert.Multiple(() =>
            {
                Assert.That(alice.Connection.Disco!.LocalForms, Is.Empty);
                Assert.That(bob.Connection.EntityCaps!.GetCachedInfo(schluessel)!.Forms, Is.Empty);
            });

        }

        #endregion

    }

}
