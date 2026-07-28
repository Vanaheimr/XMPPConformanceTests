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

using System.Text.RegularExpressions;
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0115, Abschnitt 5.4: Der Cache nimmt eine disco#info-Antwort erst
    /// auf, wenn ihr Hash den angekündigten <c>ver</c>-Wert ergibt.
    /// </summary>
    /// <remarks>
    /// Ohne diese Prüfung war der Cache von jedem vergiftbar, dessen Presence
    /// hier ankommt. Der Angreifer kündigt das <c>node#ver</c>-Paar eines
    /// verbreiteten Clients an und antwortet auf die folgende Abfrage mit einer
    /// Liste seiner Wahl; unter diesem Paar liegt fortan seine Liste, und
    /// ausgeliefert wird sie an jeden weiteren Kontakt, der dasselbe Paar
    /// ankündigt — ohne dass der je gefragt würde.
    ///
    /// Geprüft wird ohne Server: <see cref="DiscoManager"/> bekommt eine
    /// Sende-Funktion, die die Abfrage nur mitschreibt, und die Antwort wird
    /// von Hand eingespeist. Nur so lässt sich eine Antwort bauen, die zum
    /// angekündigten Hash nicht passt — ein ehrlicher Client käme gar nicht
    /// dazu.
    /// </remarks>
    [TestFixture]
    public class CapsVerificationTests
    {

        #region Data

        private const String Knoten   = "https://example.org/client";
        private const String Mallory  = "mallory@example.org/r";
        private const String Alice    = "alice@example.org/r";

        /// <summary>Die Features, die der verbreitete Client wirklich hat.</summary>
        private static readonly String[] Echt = [
            "http://jabber.org/protocol/caps",
            "http://jabber.org/protocol/disco#info"
        ];

        /// <summary>Die Liste, die der Angreifer stattdessen unterschieben will.</summary>
        private static readonly String[] Untergeschoben = [
            "urn:xmpp:receipts"
        ];

        private static readonly DiscoIdentity Identitaet = new("client", "pc", "Exodus 0.9.1");

        private DiscoManager        disco       = null!;
        private EntityCapsManager   caps        = null!;
        private List<String>        gesendet    = null!;
        private List<String>        abgelehnt   = null!;
        private List<DiscoInfo>     gemeldet    = null!;

        #endregion

        #region SetUp

        [SetUp]
        public void Setup()
        {

            gesendet   = [];
            abgelehnt  = [];
            gemeldet   = [];

            disco = new DiscoManager(xml =>
            {
                lock (gesendet) gesendet.Add(xml);
                return Task.CompletedTask;
            });

            caps = new EntityCapsManager(disco) { Node = Knoten };

            caps.OnCapsRejected   += (from, grund) => { lock (abgelehnt) abgelehnt.Add(grund); };
            caps.OnCapsDiscovered += (from, info)  => { lock (gemeldet)  gemeldet.Add(info);   };

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>Der Verification String über diese Features.</summary>
        private static String VerOf(params String[] features)
            => EntityCapsManager.VerificationString([Identitaet], features);

        /// <summary>Eine disco#info-Antwort mit genau diesen Features.</summary>
        private static String Antwort(params String[] features)
            => "<query xmlns='http://jabber.org/protocol/disco#info'>" +
               $"<identity category='{Identitaet.Category}' type='{Identitaet.Type}' " +
               $"name='{Identitaet.Name}'/>" +
               String.Concat(features.Select(f => $"<feature var='{f}'/>")) +
               "</query>";

        private Int32 Abfragen
        {
            get { lock (gesendet) return gesendet.Count; }
        }

        /// <summary>Wartet, bis so viele disco#info-Abfragen abgeschickt sind.</summary>
        private async Task WaitForQueries(Int32 count)
        {

            var ok = await XMPPServer.WaitUntilAsync(() => Abfragen >= count);

            Assert.That(ok, Is.True,
                        $"Erwartet waren {count} disco#info-Abfragen, abgeschickt wurden {Abfragen}.");

        }

        /// <summary>Beantwortet die zuletzt abgeschickte Abfrage.</summary>
        private void Answer(String from, String query)
        {

            String letzte;
            lock (gesendet) letzte = gesendet[^1];

            var id = Regex.Match(letzte, @"id='([^']+)'").Groups[1].Value;

            disco.ProcessInfoResult(id,
                                    XElement.Parse($"<iq type='result' id='{id}'>{query}</iq>"),
                                    from);

        }

        #endregion


        #region AnAnswerThatDoesNotHashToTheAnnouncedVer_IsNotCached()

        /// <summary>
        /// Der Kern: Wer ein <c>ver</c> ankündigt und etwas anderes antwortet,
        /// kommt nicht in den Cache.
        /// </summary>
        [Test]
        public async Task AnAnswerThatDoesNotHashToTheAnnouncedVer_IsNotCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Untergeschoben));
            await laeuft;

            Assert.Multiple(() =>
            {

                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null,
                            "Die untergeschobene Antwort darf nicht im Cache stehen.");

                Assert.That(abgelehnt, Is.Not.Empty, "Die Ablehnung muss gemeldet werden.");

                // Gemeldet wird sie trotzdem: Sie ist das, was diese Entity über
                // sich selbst sagt, und genau das ergäbe auch eine gewöhnliche
                // disco#info-Abfrage. Verweigert wird nur das Bündeln.
                Assert.That(gemeldet, Has.Count.EqualTo(1));

            });

        }

        #endregion

        #region AnAnswerThatHashesToTheAnnouncedVer_IsCached()

        /// <summary>
        /// Die Gegenprobe: Die ehrliche Antwort wird abgelegt.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn schlicht nichts mehr
        /// in den Cache käme — und der ganze Zweck von XEP-0115, die zweite
        /// Abfrage zu sparen, wäre still verschwunden.
        /// </remarks>
        [Test]
        public async Task AnAnswerThatHashesToTheAnnouncedVer_IsCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Alice, Antwort(Echt));
            await laeuft;

            var abgelegt = caps.GetCachedInfo($"{Knoten}#{ver}");

            Assert.Multiple(() =>
            {

                Assert.That(abgelegt, Is.Not.Null, "Die geprüfte Antwort gehört in den Cache.");
                Assert.That(abgelegt!.Features, Is.EquivalentTo(Echt));

                Assert.That(abgelehnt, Is.Empty,
                            $"Ohne Grund abgelehnt: {String.Join(" | ", abgelehnt)}");

            });

        }

        #endregion

        #region ThePoisonedEntryIsNotServedToTheNextContact()

        /// <summary>
        /// Der eigentliche Schaden, ausbuchstabiert: Was der Angreifer
        /// hinterlässt, darf dem nächsten Kontakt nicht als dessen Auskunft
        /// ausgeliefert werden.
        /// </summary>
        /// <remarks>
        /// Das ist der Test, der die Vergiftung als solche zeigt. Die anderen
        /// zeigen nur, dass ein Eintrag fehlt — hier fehlt er an der Stelle,
        /// an der er Wirkung entfaltet hätte: Alice kündigt dasselbe Paar an
        /// und wird deshalb ein zweites Mal gefragt, statt Mallorys Liste
        /// untergeschoben zu bekommen.
        /// </remarks>
        [Test]
        public async Task ThePoisonedEntryIsNotServedToTheNextContact()
        {

            var ver = VerOf(Echt);

            // Mallory kündigt das Paar eines verbreiteten Clients an und
            // antwortet mit einer Liste seiner Wahl.
            var angriff = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(1);
            Answer(Mallory, Antwort(Untergeschoben));
            await angriff;

            // Alice kündigt dasselbe Paar an - diesmal zu Recht.
            var ehrlich = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(2);
            Answer(Alice, Antwort(Echt));
            await ehrlich;

            Assert.Multiple(() =>
            {

                Assert.That(Abfragen, Is.EqualTo(2),
                            "Alice muss selbst gefragt werden; aus dem Cache bedient zu werden " +
                            "hiesse, Mallorys Liste für ihre zu halten.");

                Assert.That(gemeldet[^1].Features, Is.EquivalentTo(Echt));
                Assert.That(gemeldet[^1].Features, Does.Not.Contain("urn:xmpp:receipts"));

            });

        }

        #endregion

        #region WithoutAHashAttribute_NothingIsCached()

        /// <summary>
        /// Die Altform aus XEP-0115 vor 1.4: <c>ver</c> ist dort eine
        /// Versionsnummer und kein Hash. Nachrechnen lässt sich nichts, also
        /// wird auch nichts abgelegt.
        /// </summary>
        /// <remarks>
        /// Ohne diese Regel bliebe der bequemste Weg offen: Wer den Cache
        /// vergiften will, lässt das <c>hash</c>-Attribut einfach weg.
        ///
        /// Geprüft wird auch die Begründung, und das nicht aus Ordnungsliebe:
        /// Ein fehlendes Attribut fiele sonst unter „unbekannter Algorithmus"
        /// (<c>null</c> ist nun einmal nicht <c>sha-1</c>), und der eigene
        /// Zweig dafür wäre nicht mehr als Zierde. Der Unterschied gehört ins
        /// Protokoll: Die Gegenstelle ist nicht kaputt, sie ist alt.
        /// </remarks>
        [Test]
        public async Task WithoutAHashAttribute_NothingIsCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, hash: null);

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Echt));
            await laeuft;

            Assert.Multiple(() =>
            {

                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);
                Assert.That(gemeldet, Has.Count.EqualTo(1));

                Assert.That(abgelehnt.Any(g => g.Contains("kein hash-Attribut", StringComparison.Ordinal)),
                            Is.True,
                            $"Die Altform muss als solche benannt werden. Gemeldet wurde: " +
                            $"{String.Join(" | ", abgelehnt)}");

            });

        }

        #endregion

        #region AnUnknownHashAlgorithm_IsNotCached()

        /// <summary>
        /// Und ein Algorithmus, den dieser Client nicht rechnen kann, ebenso —
        /// auch wenn er stärker ist als SHA-1.
        /// </summary>
        [Test]
        public async Task AnUnknownHashAlgorithm_IsNotCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, "sha-256");

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Echt));
            await laeuft;

            Assert.Multiple(() =>
            {
                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);
                Assert.That(abgelehnt, Is.Not.Empty);
            });

        }

        #endregion

        #region AnAnswerWithADataForm_IsNotCached()

        /// <summary>
        /// Eine ehrliche Antwort, die ein Datenformular trägt, wird ebenfalls
        /// nicht abgelegt — nicht weil sie falsch wäre, sondern weil diese
        /// Rechnung sie nicht nachvollziehen kann.
        /// </summary>
        /// <remarks>
        /// XEP-0115, Abschnitt 5.1 lässt XEP-0128-Datenformulare in den
        /// Verification String eingehen; hier gehen sie es noch nicht. Der
        /// errechnete Wert wäre also zwangsläufig ein anderer als der
        /// angekündigte. Ihn dennoch abzulegen hiesse, eine Prüfung zu
        /// behaupten, die nicht stattgefunden hat — und ihn stumm als Fälschung
        /// zu behandeln wäre ebenso falsch, deshalb nennt der Grund den
        /// Unterschied.
        /// </remarks>
        [Test]
        public async Task AnAnswerWithADataForm_IsNotCached()
        {

            var ver = VerOf(Echt);

            var mitFormular =
                "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                $"<identity category='{Identitaet.Category}' type='{Identitaet.Type}' " +
                $"name='{Identitaet.Name}'/>" +
                String.Concat(Echt.Select(f => $"<feature var='{f}'/>")) +
                "<x xmlns='jabber:x:data' type='result'>" +
                "<field var='FORM_TYPE' type='hidden'>" +
                "<value>urn:xmpp:dataforms:softwareinfo</value>" +
                "</field></x>" +
                "</query>";

            var laeuft = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Alice, mitFormular);
            await laeuft;

            Assert.Multiple(() =>
            {

                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);

                Assert.That(abgelehnt.Any(g => g.Contains("XEP-0128", StringComparison.Ordinal)),
                            Is.True,
                            $"Der Grund muss das Datenformular benennen. Gemeldet wurde: " +
                            $"{String.Join(" | ", abgelehnt)}");

            });

        }

        #endregion

        #region ACachedEntryIsServedWithoutAsking()

        /// <summary>
        /// Und wozu das Ganze da ist: Ein geprüfter Eintrag erspart dem
        /// nächsten Kontakt die Abfrage.
        /// </summary>
        [Test]
        public async Task ACachedEntryIsServedWithoutAsking()
        {

            var ver = VerOf(Echt);

            var erste = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(1);
            Answer(Alice, Antwort(Echt));
            await erste;

            await caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            Assert.Multiple(() =>
            {
                Assert.That(Abfragen, Is.EqualTo(1), "Der Cache muss die zweite Abfrage sparen.");
                Assert.That(gemeldet, Has.Count.EqualTo(2));
            });

        }

        #endregion

    }

}
