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
using NUnit.Framework.Interfaces;

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

// Für jeden Test dieser Sammlung, ohne dass ein Fixture etwas dafür tun muss.
// Genau darin liegt der Zweck: Was von einer Zeile in jedem Fixture abhängt,
// hängt an dem, der sie schreibt.
[assembly: org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP.GlobalErrorWatch]

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Wache über alle Server der Sammlung: Sie hängt sich an jeden
    /// <see cref="XMPPServer"/>, der irgendwo entsteht, und lässt den Test
    /// scheitern, wenn das Verarbeiten eines Frames mit einer Ausnahme geendet
    /// hat.
    /// </summary>
    /// <remarks>
    /// <see cref="InternalErrorGuard"/> tut dasselbe je Fixture und bleibt
    /// bestehen — Tests, die die Meldungen <i>ansehen</i> wollen, brauchen ihn.
    /// Was ihm fehlte, war die Gewissheit: Er hing an zwei Zeilen, die jedes
    /// Fixture selbst schreiben musste (<c>Watched(…)</c> und
    /// <c>AssertClean()</c>). Beide lassen sich vergessen, und ihr Fehlen
    /// meldet sich nicht — <b>ein Server ohne Wache verschluckt Ausnahmen
    /// genauso lautlos wie vor der Wache</b>. Gesichert war das bis hierher
    /// durch eine Quelltextprüfung von Hand („kein <c>new XMPPServer(</c> ohne
    /// <c>Watched(…)</c>", siehe D19).
    ///
    /// Diese Wache kennt jeden Server über
    /// <see cref="XMPPServer.OnInstanceCreated"/> und braucht dafür niemandes
    /// Mitwirkung. Damit ist die Verdrahtung keine Eigenschaft mehr, die
    /// jemand herstellen muss, sondern eine, die von selbst gilt.
    ///
    /// <b>Zur Reihenfolge:</b> Ein Aktionsattribut mit
    /// <see cref="ActionTargets.Test"/> auf Assembly-Ebene umschliesst jeden
    /// Test <i>samt</i> seinem SetUp und TearDown. Der Server entsteht also
    /// nach <see cref="BeforeTest"/> und wird vor <see cref="AfterTest"/>
    /// abgeräumt — beides ist nötig, damit hier nichts durchrutscht und nichts
    /// aus dem vorigen Test hängenbleibt.
    /// </remarks>
    internal sealed class GlobalErrorWatchAttribute : Attribute, ITestAction
    {

        #region Data

        private static readonly List<String> _errors = [];
        private static readonly Lock _lock = new();
        private static Boolean _expected;
        private static Boolean _armed;

        #endregion

        #region Properties

        /// <summary>Was in diesem Test gemeldet wurde.</summary>
        internal static IReadOnlyList<String> Errors
        {
            get { lock (_lock) return _errors.ToList(); }
        }

        /// <summary>
        /// Läuft für jeden Test einzeln, nicht einmal für die ganze Sammlung.
        /// </summary>
        public ActionTargets Targets => ActionTargets.Test;

        #endregion


        #region BeforeTest(test)

        public void BeforeTest(ITest test)
        {

            lock (_lock)
            {

                _errors.Clear();
                _expected = false;

                // Einmal für den ganzen Lauf: Das Ereignis ist statisch, ein
                // zweites Abonnement zählte jede Meldung doppelt.
                if (!_armed)
                {
                    XMPPServer.OnInstanceCreated += Anhaengen;
                    _armed = true;
                }

            }

        }

        #endregion

        #region AfterTest(test)

        public void AfterTest(ITest test)
        {

            List<String> gemeldet;

            lock (_lock)
            {

                if (_expected)
                    return;

                gemeldet = [.. _errors];

            }

            Assert.That(gemeldet, Is.Empty,
                        "Ein Server hat beim Verarbeiten eines Frames eine Ausnahme gemeldet. " +
                        "Das ist ein Programmierfehler im Zustellweg und kein Ergebnis dieses " +
                        "Tests:" + Environment.NewLine +
                        String.Join(Environment.NewLine, gemeldet));

        }

        #endregion

        #region Expect()

        /// <summary>
        /// Sagt der Wache, dass dieser Test einen internen Fehler absichtlich
        /// auslöst.
        /// </summary>
        /// <remarks>
        /// Aufgerufen von <see cref="InternalErrorGuard.Expect"/>, damit ein
        /// Fixture es nur an einer Stelle sagen muss.
        /// </remarks>
        internal static void Expect()
        {
            lock (_lock)
                _expected = true;
        }

        #endregion

        #region Record(error)

        /// <summary>
        /// Nimmt eine Meldung auf.
        /// </summary>
        /// <remarks>
        /// Getrennt vom Anhängen an den Server, damit die Wache selbst prüfbar
        /// ist - derselbe Grund wie bei
        /// <see cref="InternalErrorGuard.Record"/>. Ohne diese Trennung liesse
        /// sich nur zeigen, dass sie schweigt, wenn nichts gemeldet wurde;
        /// dass sie den Test <b>scheitern lässt</b>, wenn doch, bliebe
        /// unbelegt. Eine Wache, die immer freigibt, fällt sonst niemandem
        /// auf, und ausgerechnet sie wäre die schlimmste Fassung: Sie sieht
        /// aus wie eine Sicherung und ist keine.
        /// </remarks>
        internal static void Record(String error)
        {
            lock (_lock)
                _errors.Add(error);
        }

        #endregion

        #region (private, static) Anhaengen(server)

        private static void Anhaengen(XMPPServer server)

            => server.OnInternalError += (session, frame, e)
                   => Record($"{e.GetType().Name}: {e.Message}" +
                             Environment.NewLine +
                             $"    beim Frame: {frame}");

        #endregion

    }

}
