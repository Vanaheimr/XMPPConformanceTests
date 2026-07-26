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

using NUnit.Framework;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XMPPConnection serialisiert ausgehende Stanzas über ein SemaphoreSlim,
    /// weil der WebSocket-Vertrag nur einen ausstehenden Sendevorgang erlaubt
    /// und aus mehreren Richtungen gleichzeitig gesendet wird (Keepalive,
    /// Auto-Receipts aus der Empfangsschleife, Benutzeraktionen).
    ///
    /// Hinweis zur Einordnung: ClientWebSocket serialisiert unter .NET 10
    /// bereits intern, ein ungeschützter Aufruf brach in Messungen nicht. Das
    /// Lock sichert die Zusicherung explizit ab, statt sich auf ein
    /// undokumentiertes Implementierungsdetail zu verlassen.
    /// </summary>
    [TestFixture]
    public class SendLockTests : AXMPPTests
    {

        #region Data

        private const Int32 PayloadSize = 40_000;
        private const Int32 Burst       = 200;

        #endregion

        #region ConcurrentSends_ArriveIntactAndComplete()

        /// <summary>
        /// 200 gleichzeitige Sends mit je 40 kB Nutzlast müssen fehlerfrei
        /// durchlaufen und unverfälscht ankommen. Vermischen sich die Frames,
        /// stimmt entweder die Länge nicht oder der Rumpf ist nicht mehr
        /// einheitlich.
        /// </summary>
        [Test]
        public async Task ConcurrentSends_ArriveIntactAndComplete()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            var errors = await Task.WhenAll(
                             Enumerable.Range(0, Burst).Select(i => Task.Run(async () =>
                             {
                                 try
                                 {
                                     await client.SendRawAsync(Payload(i));
                                     return (Exception?) null;
                                 }
                                 catch (Exception ex)
                                 {
                                     return ex;
                                 }
                             })));

            var failed = errors.Where(e => e is not null).ToList();

            Assert.That(failed, Is.Empty,
                        $"{failed.Count} von {Burst} parallelen Sends sind fehlgeschlagen, " +
                        $"erster Fehler: {failed.FirstOrDefault()?.Message}");

            await WaitFor(() => Inspect(session.Received).intact == Burst,
                          $"Eintreffen aller {Burst} Stanzas",
                          TimeSpan.FromSeconds(20));

            var (intact, corrupt) = Inspect(session.Received);

            Assert.Multiple(() =>
            {
                Assert.That(intact,  Is.EqualTo(Burst), "Es fehlen Stanzas.");
                Assert.That(corrupt, Is.Zero,           "Es sind beschädigte Stanzas angekommen.");
            });

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>Eine Stanza, deren Rumpf aus genau einem wiederholten Zeichen besteht.</summary>
        private static String Payload(Int32 i)
            => $"<p id='burst-{i}'>" + new String((Char) ('A' + i % 26), PayloadSize) + "</p>";

        /// <summary>Zählt vollständige und beschädigte Payload-Frames.</summary>
        private static (Int32 intact, Int32 corrupt) Inspect(IEnumerable<String> frames)
        {

            Int32 intact = 0, corrupt = 0;

            foreach (var f in frames.Where(x => x.StartsWith("<p id='burst-", StringComparison.Ordinal)))
            {

                var m = Regex.Match(f, @"^<p id='burst-(\d+)'>(.*)</p>$", RegexOptions.Singleline);

                if (!m.Success)
                {
                    corrupt++;
                    continue;
                }

                var expected = (Char) ('A' + Int32.Parse(m.Groups[1].Value) % 26);
                var body     = m.Groups[2].Value;

                if (body.Length == PayloadSize && body.All(c => c == expected))
                    intact++;
                else
                    corrupt++;

            }

            return (intact, corrupt);

        }

        #endregion

    }

}
