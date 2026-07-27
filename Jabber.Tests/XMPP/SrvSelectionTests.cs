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

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Reihenfolge der SRV-Ziele nach RFC 2782.
    /// </summary>
    /// <remarks>
    /// Die Zufallsquelle wird hier vorgegeben, damit aus einem
    /// wahrscheinlichkeitsbehafteten Verfahren ein prüfbarer Ablauf wird.
    /// Das ist mehr als Bequemlichkeit: eine falsche Gewichtung fällt bei
    /// echtem Zufall gar nicht auf, weil jedes Ergebnis irgendwie plausibel
    /// aussieht.
    /// </remarks>
    [TestFixture]
    public class SrvSelectionTests
    {

        #region Hilfsfunktionen

        private static SrvTarget Ziel(UInt16 prioritaet, UInt16 gewicht, String host)
            => new (prioritaet, gewicht, host, 5269);

        /// <summary>Eine Zufallsquelle, die der Reihe nach feste Werte liefert.</summary>
        private static Func<Int32, Int32> Wuerfe(params Int32[] werte)
        {

            var i = 0;

            return max =>
            {
                var wert = i < werte.Length ? werte[i] : 0;
                i++;
                return Math.Min(wert, max);
            };

        }

        private static String[] Namen(IEnumerable<SrvTarget> ziele)
            => [.. ziele.Select(z => z.Host)];

        #endregion


        #region LowerPriorityGoesFirst()

        /// <summary>
        /// Die Priorität schlägt alles - auch ein hohes Gewicht.
        /// </summary>
        [Test]
        public void LowerPriorityGoesFirst()
        {

            var geordnet = SrvSelection.Order(
                               [Ziel(20, 100, "spaeter.example"),
                                Ziel(10,   1, "zuerst.example")],
                               Wuerfe(0, 0));

            Assert.That(Namen(geordnet), Is.EqualTo(new[] { "zuerst.example", "spaeter.example" }));

        }

        #endregion

        #region AllOfOnePriorityBeforeTheNext()

        /// <summary>
        /// Erst wenn eine Prioritätsstufe erschöpft ist, kommt die nächste.
        /// </summary>
        [Test]
        public void AllOfOnePriorityBeforeTheNext()
        {

            var geordnet = SrvSelection.Order(
                               [Ziel(20, 10, "c.example"),
                                Ziel(10, 10, "a.example"),
                                Ziel(20, 10, "d.example"),
                                Ziel(10, 10, "b.example")],
                               Wuerfe(0, 0, 0, 0));

            var namen = Namen(geordnet);

            Assert.Multiple(() =>
            {
                Assert.That(namen[..2], Is.EquivalentTo(new[] { "a.example", "b.example" }));
                Assert.That(namen[2..], Is.EquivalentTo(new[] { "c.example", "d.example" }));
            });

        }

        #endregion

        #region TheWeightDecidesWithinAPriority()

        /// <summary>
        /// Innerhalb einer Priorität entscheidet das Gewicht - über eine
        /// gewichtete Ziehung, nicht über eine Sortierung.
        /// </summary>
        /// <remarks>
        /// Die Würfe sind so gewählt, dass sie das schwächere Ziel zuerst
        /// treffen. Wäre die Auswahl eine absteigende Sortierung nach Gewicht,
        /// käme unabhängig vom Wurf immer das stärkere zuerst - und genau das
        /// ist der Fehler, den dieser Test fängt.
        /// </remarks>
        [Test]
        public void TheWeightDecidesWithinAPriority()
        {

            // Reihenfolge im Rest: schwer.example (90), leicht.example (10);
            // laufende Summen 90 und 100. Ein Wurf von 100 trifft das zweite.
            var geordnet = SrvSelection.Order(
                               [Ziel(10, 90, "schwer.example"),
                                Ziel(10, 10, "leicht.example")],
                               Wuerfe(100, 0));

            Assert.That(Namen(geordnet), Is.EqualTo(new[] { "leicht.example", "schwer.example" }));

        }

        #endregion

        #region ALowRollPicksTheFirstRunningSum()

        /// <summary>
        /// Die Gegenprobe: ein kleiner Wurf trifft das erste Ziel der
        /// laufenden Summe.
        /// </summary>
        [Test]
        public void ALowRollPicksTheFirstRunningSum()
        {

            var geordnet = SrvSelection.Order(
                               [Ziel(10, 90, "schwer.example"),
                                Ziel(10, 10, "leicht.example")],
                               Wuerfe(1, 0));

            Assert.That(Namen(geordnet)[0], Is.EqualTo("schwer.example"));

        }

        #endregion

        #region WeightZeroIsNotExcluded()

        /// <summary>
        /// Gewicht null heisst nicht "nie".
        /// </summary>
        /// <remarks>
        /// RFC 2782 stellt gewichtslose Ziele an den Anfang der Liste, womit
        /// ein Wurf von 0 genau sie trifft. Wer sie stattdessen ans Ende
        /// sortiert oder ganz auslässt, macht aus einem Reserveziel ein totes.
        /// </remarks>
        [Test]
        public void WeightZeroIsNotExcluded()
        {

            var geordnet = SrvSelection.Order(
                               [Ziel(10, 100, "haupt.example"),
                                Ziel(10,   0, "reserve.example")],
                               Wuerfe(0, 0));

            Assert.Multiple(() =>
            {
                Assert.That(Namen(geordnet)[0], Is.EqualTo("reserve.example"));
                Assert.That(geordnet,           Has.Count.EqualTo(2));
            });

        }

        #endregion

        #region EveryTargetAppearsExactlyOnce()

        /// <summary>
        /// Gezogen wird ohne Zurücklegen - kein Ziel doppelt, keines verloren.
        /// </summary>
        [Test]
        public void EveryTargetAppearsExactlyOnce()
        {

            var ziele = new[] {
                            Ziel(10, 5, "a.example"),
                            Ziel(10, 5, "b.example"),
                            Ziel(10, 5, "c.example"),
                            Ziel(20, 1, "d.example")
                        };

            var geordnet = SrvSelection.Order(ziele);

            Assert.That(Namen(geordnet), Is.EquivalentTo(Namen(ziele)));

        }

        #endregion

        #region ADotMeansTheServiceIsNotOffered()

        /// <summary>
        /// Ein Ziel "." heisst: diese Domain bietet den Dienst nicht an
        /// (RFC 2782).
        /// </summary>
        /// <remarks>
        /// Das ist eine Aussage und kein fehlender Eintrag. Sie zu übergehen
        /// und auf den Regelport zurückzufallen hiesse, eine ausdrückliche
        /// Absage als Schweigen zu lesen.
        /// </remarks>
        [Test]
        public void ADotMeansTheServiceIsNotOffered()
        {

            var geordnet = SrvSelection.Order([new SrvTarget(0, 0, ".", 0)]);

            Assert.That(geordnet, Is.Empty);

        }

        #endregion

        #region ADotAmongOthers_StillMeansNo()

        /// <summary>
        /// Auch neben anderen Einträgen bleibt "." eine Absage.
        /// </summary>
        [Test]
        public void ADotAmongOthers_StillMeansNo()
        {

            var geordnet = SrvSelection.Order(
                               [Ziel(10, 10, "irgendwo.example"),
                                new SrvTarget(0, 0, ".", 0)]);

            Assert.That(geordnet, Is.Empty);

        }

        #endregion

        #region NothingAtAll_YieldsNothing()

        [Test]
        public void NothingAtAll_YieldsNothing()
        {
            Assert.That(SrvSelection.Order([]), Is.Empty);
        }

        #endregion

        #region OverManyRuns_TheDistributionFollowsTheWeights()

        /// <summary>
        /// Über viele Ziehungen mit echtem Zufall folgt die Verteilung den
        /// Gewichten.
        /// </summary>
        /// <remarks>
        /// Die vorigen Tests halten den Ablauf fest, dieser die Wirkung. Die
        /// Schranken sind grosszügig - der Test soll eine vertauschte oder
        /// ignorierte Gewichtung fangen, nicht die Güte der Zufallsquelle
        /// beurteilen.
        /// </remarks>
        [Test]
        public void OverManyRuns_TheDistributionFollowsTheWeights()
        {

            var ziele = new[] {
                            Ziel(10, 90, "schwer.example"),
                            Ziel(10, 10, "leicht.example")
                        };

            var schwerZuerst = 0;

            for (var i = 0; i < 2000; i++)
                if (SrvSelection.Order(ziele)[0].Host == "schwer.example")
                    schwerZuerst++;

            Assert.That(schwerZuerst, Is.InRange(1500, 1950),
                        "Rund 90 Prozent sollten auf das schwere Ziel entfallen.");

        }

        #endregion

    }

}
