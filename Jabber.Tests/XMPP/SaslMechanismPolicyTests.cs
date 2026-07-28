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
    /// Die Rangfolge der SASL-Mechanismen und die beiden Untergrenzen darauf.
    /// </summary>
    /// <remarks>
    /// Für sich geprüft, weil die Verbindung von diesen Entscheidungen nur
    /// diejenige zeigt, die der Testserver gerade anbietet - und der bietet
    /// vom stärksten zum schwächsten an. Eine Auswahl, die in Wahrheit nur den
    /// ersten Eintrag nimmt, sähe dort genauso aus wie eine, die die Rangfolge
    /// liest.
    /// </remarks>
    [TestFixture]
    public class SaslMechanismPolicyTests
    {

        #region Strongest_ReadsTheRankingAndNotTheOrder()

        /// <summary>
        /// Gewählt wird nach Stärke, nicht nach der Reihenfolge der
        /// Ankündigung.
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge bestimmt der Server, und der Server ist an dieser
        /// Stelle gerade die Instanz, der nicht zu trauen ist: Wer den ersten
        /// Eintrag nimmt, lässt sich das Downgrade schlicht hinschreiben.
        /// </remarks>
        [Test]
        public void Strongest_ReadsTheRankingAndNotTheOrder()
        {

            Assert.Multiple(() =>
            {

                Assert.That(SaslMechanismPolicy.Strongest(["PLAIN", "SCRAM-SHA-1", "SCRAM-SHA-256"]),
                            Is.EqualTo("SCRAM-SHA-256"));

                Assert.That(SaslMechanismPolicy.Strongest(["SCRAM-SHA-256", "SCRAM-SHA-1", "PLAIN"]),
                            Is.EqualTo("SCRAM-SHA-256"));

                Assert.That(SaslMechanismPolicy.Strongest(["PLAIN", "SCRAM-SHA-1"]),
                            Is.EqualTo("SCRAM-SHA-1"));

                Assert.That(SaslMechanismPolicy.Strongest(["PLAIN"]),
                            Is.EqualTo("PLAIN"));

            });

        }

        #endregion

        #region Strongest_IgnoresWhatItCannotSpeak()

        /// <summary>
        /// Unbekannte Mechanismen zählen nicht mit - auch dann nicht, wenn sie
        /// stärker klingen.
        /// </summary>
        /// <remarks>
        /// EXTERNAL, ANONYMOUS und X-OAUTH2 kommen im Bestand vor; der Client
        /// spricht keines davon. Sie zu wählen hiesse, mit einem Mechanismus
        /// anzufangen, für den es kein Verfahren gibt.
        /// </remarks>
        [Test]
        public void Strongest_IgnoresWhatItCannotSpeak()
        {

            Assert.Multiple(() =>
            {

                Assert.That(SaslMechanismPolicy.Strongest(["EXTERNAL", "ANONYMOUS", "SCRAM-SHA-1"]),
                            Is.EqualTo("SCRAM-SHA-1"));

                Assert.That(SaslMechanismPolicy.Strongest(["EXTERNAL", "ANONYMOUS"]),
                            Is.Null);

                Assert.That(SaslMechanismPolicy.Strongest([]),
                            Is.Null);

                // Kleingeschrieben ist es nicht derselbe Name: SASL-Mechanismen
                // sind nach RFC 4422, Abschnitt 3.1 Grossbuchstaben.
                Assert.That(SaslMechanismPolicy.Strongest(["scram-sha-256"]),
                            Is.Null);

            });

        }

        #endregion

        #region Pinned_RefusesTheWeakerAndAllowsTheStronger()

        /// <summary>
        /// Die angeheftete Untergrenze lässt gleich Starkes und Stärkeres durch
        /// und weist nur nach unten ab.
        /// </summary>
        /// <remarks>
        /// Der Punkt ist die zweite Hälfte: Ein Server, der SCRAM-SHA-256
        /// nachrüstet, darf nicht daran scheitern, dass beim letzten Mal
        /// SCRAM-SHA-1 lief. Eine Anheftung, die auf Gleichheit prüft, wäre
        /// bequemer zu schreiben und würde genau das tun.
        /// </remarks>
        [Test]
        public void Pinned_RefusesTheWeakerAndAllowsTheStronger()
        {

            var policy = new SaslMechanismPolicy();

            // Vor der ersten Anmeldung ist nichts angeheftet.
            Assert.That(() => policy.EnsureAcceptable("PLAIN"), Throws.Nothing);

            policy.Remember("SCRAM-SHA-1");

            Assert.Multiple(() =>
            {

                Assert.That(policy.Pinned, Is.EqualTo("SCRAM-SHA-1"));

                Assert.That(() => policy.EnsureAcceptable("SCRAM-SHA-1"),   Throws.Nothing);
                Assert.That(() => policy.EnsureAcceptable("SCRAM-SHA-256"), Throws.Nothing);

                Assert.That(() => policy.EnsureAcceptable("PLAIN"),
                            Throws.TypeOf<AuthenticationException>());

            });

        }

        #endregion

        #region Minimum_HoldsWithoutAnyPreviousLogin()

        /// <summary>
        /// Die gesetzte Untergrenze wirkt sofort - sie ist das, was die
        /// Anheftung beim allerersten Mal noch nicht sein kann.
        /// </summary>
        [Test]
        public void Minimum_HoldsWithoutAnyPreviousLogin()
        {

            var policy = new SaslMechanismPolicy
            {
                Minimum = "SCRAM-SHA-256"
            };

            Assert.Multiple(() =>
            {

                Assert.That(policy.Pinned, Is.Null, "Ohne Anmeldung darf nichts angeheftet sein.");

                Assert.That(() => policy.EnsureAcceptable("SCRAM-SHA-256"), Throws.Nothing);

                Assert.That(() => policy.EnsureAcceptable("SCRAM-SHA-1"),
                            Throws.TypeOf<AuthenticationException>());

                Assert.That(() => policy.EnsureAcceptable("PLAIN"),
                            Throws.TypeOf<AuthenticationException>());

            });

        }

        #endregion

        #region Minimum_RefusesAnUnknownName()

        /// <summary>
        /// Ein Name, den die Rangfolge nicht kennt, wird beim Setzen abgewiesen.
        /// </summary>
        /// <remarks>
        /// Sonst wäre ein Tippfehler die gefährlichste Eingabe überhaupt: Ein
        /// unbekannter Name hat die Stärke 0, und eine Untergrenze von 0
        /// verlangt gar nichts. Der Aufrufer bekäme lautlos das Gegenteil
        /// dessen, was er hinschrieb.
        /// </remarks>
        [Test]
        public void Minimum_RefusesAnUnknownName()
        {

            var policy = new SaslMechanismPolicy();

            Assert.Multiple(() =>
            {

                Assert.That(() => policy.Minimum = "SCRAM-SHA-512",
                            Throws.TypeOf<ArgumentException>());

                Assert.That(() => policy.Minimum = "scram-sha-256",
                            Throws.TypeOf<ArgumentException>());

                // Und null bleibt zulässig: es ist das Abschalten.
                Assert.That(() => policy.Minimum = null, Throws.Nothing);

            });

        }

        #endregion

    }

}
