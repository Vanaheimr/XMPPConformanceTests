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
    /// Die Wache gegen verschluckte Programmierfehler: Sie hängt sich an
    /// <see cref="XMPPServer.OnInternalError"/> und lässt den Test scheitern,
    /// wenn das Verarbeiten eines Frames mit einer Ausnahme geendet hat.
    /// </summary>
    /// <remarks>
    /// Bis vor kurzem stand um das Verarbeiten eines Frames ein <c>catch</c> ohne
    /// Filter mit dem Vermerk „Verbindung abgerissen - im Test der Normalfall".
    /// Eine Messung über die gesamte Sammlung fing dort <b>keine einzige</b>
    /// Ausnahme: Was der Fang noch leistete, war das lautlose Verschlucken von
    /// Programmierfehlern. In D15 überlebte eine Mutation nur deshalb, weil ihre
    /// <c>NullReferenceException</c> dort verschwand.
    ///
    /// Eine eigene Klasse und keine Methode in
    /// <see cref="AXMPPTests"/>, weil die Wache nicht an der Vererbung hängen
    /// darf: Mehrere Fixtures betreiben eigene Server ohne diese Basis, und
    /// gerade dort - zwischen zwei Servern - lag der Fall, der den Fehler
    /// aufgedeckt hat.
    ///
    /// Nicht gefiltert wird bewusst. Eine Liste von Ausnahmen, die ein Abriss
    /// „wirklich" erzeugt, wäre geraten; die Messung sagt, dass keine davon
    /// vorkommt. Jede Meldung gilt daher als Mangel, bis das Gegenteil gezeigt
    /// ist - und wenn doch einmal ein Abriss darunter ist, nennt die Meldung
    /// ihren Typ und der Fall ist in einem Zug geklärt statt für immer
    /// unsichtbar.
    /// </remarks>
    internal sealed class InternalErrorGuard
    {

        #region Data

        private readonly List<String> _errors = [];
        private readonly Lock _lock = new();
        private Boolean _expected;

        #endregion

        #region Properties

        /// <summary>Die bisher gemeldeten internen Fehler.</summary>
        public IReadOnlyList<String> Errors
        {
            get { lock (_lock) return _errors.ToList(); }
        }

        #endregion


        /// <summary>
        /// Beginnt einen neuen Test: alles Gemeldete verwerfen und wieder
        /// scharf stellen.
        /// </summary>
        public void Reset()
        {

            lock (_lock)
                _errors.Clear();

            _expected = false;

        }

        /// <summary>
        /// Hängt die Wache an einen Server. Beliebig oft aufrufbar - ein Test
        /// mit zwei Servern bewacht beide.
        /// </summary>
        public void Watch(XMPPServer server)

            => server.OnInternalError += (session, frame, e)
                   => Record(e.GetType().Name + ": " + e.Message, frame);

        /// <summary>
        /// Wie <see cref="Watch"/>, gibt den Server aber zurück - damit sich ein
        /// <c>new XMPPServer(…)</c> an der Stelle umschliessen lässt, an der er
        /// steht.
        /// </summary>
        /// <remarks>
        /// Mehrere Fixtures erzeugen ihre Server nicht im SetUp, sondern
        /// mitten im Test. Für die wäre eine getrennte
        /// <see cref="Watch"/>-Zeile ein zweiter Ort, den man beim nächsten
        /// Server vergessen kann; so steht die Wache dort, wo der Server
        /// entsteht.
        /// </remarks>
        public XMPPServer Watched(XMPPServer server)
        {

            Watch(server);

            return server;

        }

        /// <summary>
        /// Nimmt eine Meldung auf.
        /// </summary>
        /// <remarks>
        /// Getrennt von <see cref="Watch"/>, damit die Wache selbst prüfbar ist:
        /// Ohne diese Trennung liesse sich nur zeigen, dass sie schweigt, wenn
        /// nichts gemeldet wurde - nicht aber, dass sie den Test tatsächlich
        /// scheitern lässt, wenn doch. Eine Wache, die immer freigibt, fällt
        /// sonst niemandem auf; genau diese Mutation überlebte, bevor es diesen
        /// Weg gab.
        /// </remarks>
        public void Record(String error, String frame)
        {
            lock (_lock)
                _errors.Add($"{error}{Environment.NewLine}    beim Frame: {frame}");
        }

        /// <summary>
        /// Sagt der Wache, dass dieser Test einen internen Fehler absichtlich
        /// auslöst.
        /// </summary>
        /// <remarks>
        /// Weitergereicht an <see cref="GlobalErrorWatchAttribute"/>: Seit es
        /// die Wache über alle Server gibt, sieht die den Fehler ebenfalls, und
        /// ein Fixture soll seine Absicht trotzdem nur an einer Stelle sagen
        /// müssen.
        /// </remarks>
        public void Expect()
        {
            _expected = true;
            GlobalErrorWatchAttribute.Expect();
        }

        /// <summary>
        /// Lässt den Test scheitern, wenn etwas gemeldet wurde - aufzurufen im
        /// TearDown.
        /// </summary>
        public void AssertClean()
        {

            if (_expected)
                return;

            var gemeldet = Errors;

            Assert.That(gemeldet, Is.Empty,
                        "Der Server hat beim Verarbeiten eines Frames eine Ausnahme " +
                        "gemeldet. Das ist ein Programmierfehler im Zustellweg und " +
                        "kein Ergebnis dieses Tests:" + Environment.NewLine +
                        String.Join(Environment.NewLine, gemeldet));

        }

    }

}
