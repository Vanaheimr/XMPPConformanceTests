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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Die Reihenfolge, in der SRV-Ziele versucht werden (RFC 2782).
    /// </summary>
    /// <remarks>
    /// Der Teil, den man leicht falsch macht und nie bemerkt. Prioritäten der
    /// Reihe nach abzuarbeiten ist offensichtlich; die Gewichtung innerhalb
    /// einer Priorität ist es nicht. Sie ist <b>keine</b> Sortierung nach
    /// Gewicht, sondern eine gewichtete Ziehung ohne Zurücklegen: aus den
    /// verbleibenden Zielen wird eines mit einer Wahrscheinlichkeit
    /// proportional zu seinem Gewicht gezogen, dann das nächste aus dem Rest.
    /// Wer stattdessen absteigend sortiert, schickt allen Verkehr an den
    /// stärksten Rechner - und die Lastverteilung, um derentwillen es die
    /// Gewichte gibt, findet nie statt. Auffallen würde das erst im Betrieb,
    /// und auch dort nur jemandem, der die Auslastung anschaut.
    ///
    /// Die Zufallsquelle ist einsetzbar, damit der Ablauf prüfbar bleibt.
    /// </remarks>
    public static class SrvSelection
    {

        #region Data

        /// <summary>
        /// Ein Ziel "." heisst nach RFC 2782 ausdrücklich: dieser Dienst wird
        /// für diese Domain <b>nicht</b> angeboten.
        /// </summary>
        public const String NoService = ".";

        #endregion

        #region Order(targets, pick = null)

        /// <summary>
        /// Bringt die Ziele in die Reihenfolge, in der sie versucht werden
        /// sollen.
        /// </summary>
        /// <param name="targets">Die unsortierten SRV-Ziele.</param>
        /// <param name="pick">
        /// Liefert eine Zufallszahl in <c>[0, max]</c> <b>einschliesslich</b>.
        /// Null nimmt eine echte Zufallsquelle.
        /// </param>
        /// <returns>
        /// Die Ziele in der Reihenfolge des Versuchs. Leer, wenn die Domain
        /// den Dienst ausdrücklich nicht anbietet.
        /// </returns>
        public static IReadOnlyList<SrvTarget> Order(IEnumerable<SrvTarget>  targets,
                                                     Func<Int32, Int32>?     pick   = null)
        {

            var alle = targets.ToList();

            // RFC 2782: ein einzelnes "." beendet die Suche. Andere Einträge
            // daneben zu beachten wäre falsch - die Domain hat gesagt, dass es
            // den Dienst nicht gibt.
            if (alle.Any(t => t.Host == NoService))
                return [];

            pick ??= max => Random.Shared.Next(max + 1);

            var ergebnis = new List<SrvTarget>(alle.Count);

            foreach (var gruppe in alle.GroupBy(t => t.Priority).OrderBy(g => g.Key))
            {

                // "all those with weight 0 are placed at the beginning of the
                // list" - so steht es im RFC, und es ist der Grund, warum ein
                // gewichtsloses Ziel überhaupt je gezogen wird.
                var rest = gruppe.OrderBy(t => t.Weight == 0 ? 0 : 1).ToList();

                while (rest.Count > 0)
                {

                    var summe = rest.Sum(t => (Int32) t.Weight);
                    var wurf  = pick(summe);

                    var laufend  = 0;
                    var gewaehlt = rest.Count - 1;

                    for (var i = 0; i < rest.Count; i++)
                    {

                        laufend += rest[i].Weight;

                        // "select the RR whose running sum value is the first
                        // value greater than or equal to the random number"
                        if (laufend >= wurf)
                        {
                            gewaehlt = i;
                            break;
                        }

                    }

                    ergebnis.Add(rest[gewaehlt]);
                    rest.RemoveAt(gewaehlt);

                }

            }

            return ergebnis;

        }

        #endregion

    }

}
