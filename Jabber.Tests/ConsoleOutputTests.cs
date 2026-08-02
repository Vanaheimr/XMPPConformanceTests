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

using System.Text;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Die gemeinsame Konsolenausgabe: Sie hält die Eingabezeile heil - für
    /// Ereignisse <b>und</b> für das Protokoll.
    /// </summary>
    /// <remarks>
    /// Bis D58 gab es hier nichts zu prüfen, weil es die Stelle nicht gab: Die
    /// Ereignisbehandlung klammerte jede Ausgabe von Hand, und der Logger
    /// schrieb daran vorbei. Beides liess sich nur mit den Augen beurteilen,
    /// und beurteilt wurde es entsprechend selten.
    ///
    /// Geprüft wird gegen einen <see cref="StringWriter"/> - dieselbe Klasse,
    /// nur ohne Konsole dahinter. Die Breite wird dabei vorgegeben: Auf einem
    /// Testläufer ohne Fenster gibt es keine, und der Test soll die Zeile
    /// löschen, nicht die Umgebung ausmessen.
    /// </remarks>
    [TestFixture]
    public class ConsoleOutputTests
    {

        #region Data

        private StringWriter  _geschrieben  = null!;
        private ConsoleOutput _ausgabe      = null!;

        private const String Prompt = "> ";

        #endregion

        #region SetUp

        [SetUp]
        public void Aufbau()
        {
            _geschrieben  = new StringWriter();
            _ausgabe      = new ConsoleOutput(() => Prompt, _geschrieben, () => 20);
        }

        #endregion


        #region AnUnpromptedLine_RestoresThePrompt()

        /// <summary>
        /// Eine ungefragte Ausgabe löscht die angefangene Zeile und stellt die
        /// Eingabeaufforderung wieder her.
        /// </summary>
        /// <remarks>
        /// Das ist der ganze Zweck: Der Anwender tippt, es kommt eine
        /// Nachricht, und danach steht seine Eingabeaufforderung wieder da.
        /// Ohne das Löschen stünde die Nachricht hinter dem halben Wort; ohne
        /// das Nachziehen wäre die Eingabeaufforderung fort, und er tippte ins
        /// Nichts.
        /// </remarks>
        [Test]
        public void AnUnprompted_LineRestoresThePrompt()
        {

            _ausgabe.WriteLine("Nachricht von Bob");

            var text = _geschrieben.ToString();

            Assert.Multiple(() =>
            {

                Assert.That(text, Does.StartWith("\r"),
                            "Die angefangene Zeile wird nicht geräumt.");

                Assert.That(text, Does.Contain("Nachricht von Bob"));

                Assert.That(text, Does.EndWith(Prompt),
                            "Nach der Ausgabe fehlt die Eingabeaufforderung.");

            });

        }

        #endregion

        #region ThePromptFollowsTheConversation()

        /// <summary>
        /// Die Eingabeaufforderung wird bei jeder Ausgabe neu erfragt.
        /// </summary>
        /// <remarks>
        /// Sie ändert sich mit dem Gesprächspartner. Wäre sie eine Zeichenkette
        /// statt einer Funktion, stünde nach einem <c>/to</c> weiter die alte
        /// da - und zwar bis zum nächsten Neustart.
        /// </remarks>
        [Test]
        public void ThePromptFollowsTheConversation()
        {

            var partner  = "alice";
            var ausgabe  = new ConsoleOutput(() => $"[{partner}] > ", _geschrieben, () => 20);

            ausgabe.WriteLine("erste");
            partner = "bob";
            ausgabe.WriteLine("zweite");

            var text = _geschrieben.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("[alice] > "));
                Assert.That(text, Does.EndWith("[bob] > "));
            });

        }

        #endregion

        #region WithoutAWidth_NothingIsErased()

        /// <summary>
        /// Ohne Konsolenbreite wird nicht gelöscht - aber weiterhin
        /// geschrieben.
        /// </summary>
        /// <remarks>
        /// Der Fall der umgeleiteten Ausgabe: Dort gibt es keine Eingabezeile,
        /// die zu retten wäre. Die alte Fassung fing die Ausnahme von
        /// <c>Console.WindowWidth</c> und schrieb ersatzweise eine Leerzeile -
        /// das half niemandem und riss jede Ausgabe von der nächsten.
        /// </remarks>
        [Test]
        public void WithoutAWidth_NothingIsErased()
        {

            var ausgabe = new ConsoleOutput(() => Prompt, _geschrieben, () => 0);

            ausgabe.WriteLine("Zeile");

            // Am Anfang und nicht am Vorkommen: Ein Wagenrücklauf steht unter
            // Windows auch am Ende jeder Zeile. Was hier fehlen muss, ist die
            // Löschfolge davor - Rücklauf, Leerzeichen, Rücklauf.
            Assert.That(_geschrieben.ToString(), Does.StartWith("Zeile"),
                        "Ohne Breite gibt es nichts zu löschen.");

        }

        #endregion

        #region AScope_HoldsTheConsoleUntilItIsLeft()

        /// <summary>
        /// Ein Ausgabebereich schreibt die Eingabeaufforderung erst beim
        /// Verlassen - nicht nach jedem Teilstück.
        /// </summary>
        /// <remarks>
        /// Für die Ausgaben, die in mehreren Zügen entstehen: Zeitstempel,
        /// Absender, Text, jeweils mit eigener Farbe. Käme dazwischen die
        /// Eingabeaufforderung, stünde sie mitten in der Zeile.
        /// </remarks>
        [Test]
        public void AScope_HoldsTheConsoleUntilItIsLeft()
        {

            using (var bereich = _ausgabe.Begin())
            {
                _geschrieben.Write("[12:00:00] ");
                _geschrieben.Write("bob: ");
                _geschrieben.WriteLine("Hallo");

                Assert.That(_geschrieben.ToString(), Does.Not.Contain(Prompt),
                            "Die Eingabeaufforderung kam mitten in die Ausgabe.");
            }

            Assert.That(_geschrieben.ToString(), Does.EndWith(Prompt));

        }

        #endregion

        #region TheLogger_GoesThroughTheSameDoor()

        /// <summary>
        /// Eine Protokollzeile räumt die Eingabezeile und stellt sie wieder
        /// her - genau wie eine Nachricht.
        /// </summary>
        /// <remarks>
        /// Der eigentliche Punkt von D58. Ein <c>AddSimpleConsole</c> hätte
        /// hier einfach geschrieben; dass es keinen Unterschied mehr macht,
        /// woher die Zeile kommt, ist die ganze Änderung.
        /// </remarks>
        [Test]
        public void TheLogger_GoesThroughTheSameDoor()
        {

            using var provider = new ConsoleOutputLoggerProvider(_ausgabe, LogLevel.Information);

            provider.CreateLogger("org.GraphDefined.Vanaheimr.Hermod.XMPP.XMPPConnection")
                    .LogInformation("Verbindung steht");

            var text = _geschrieben.ToString();

            Assert.Multiple(() =>
            {

                Assert.That(text, Does.StartWith("\r"),
                            "Das Protokoll schreibt an der Eingabezeile vorbei.");

                Assert.That(text, Does.Contain("Verbindung steht"));

                Assert.That(text, Does.Contain("info"));

                Assert.That(text, Does.Contain("XMPPConnection"),
                            "Der Kategoriename gehört dazu - aber nur sein letzter Teil.");

                Assert.That(text, Does.Not.Contain("org.GraphDefined"),
                            "Der volle Typname frisst die halbe Zeilenbreite.");

                Assert.That(text, Does.EndWith(Prompt));

            });

        }

        #endregion

        #region TheLogger_KeepsQuietBelowItsLevel()

        /// <summary>
        /// Was unter der Mindeststufe liegt, erreicht die Konsole nicht.
        /// </summary>
        /// <remarks>
        /// Ohne diese Zusicherung wäre „schreibe alles" eine bestandene Lösung
        /// - und der Anwender bekäme im Normalbetrieb jede Trace-Zeile des
        /// Protokolls in seine Eingabezeile.
        /// </remarks>
        [Test]
        public void TheLogger_KeepsQuietBelowItsLevel()
        {

            using var provider = new ConsoleOutputLoggerProvider(_ausgabe, LogLevel.Warning);

            var logger = provider.CreateLogger("Test");

            logger.LogInformation("bitte nicht");
            logger.LogWarning("aber das");

            Assert.Multiple(() =>
            {
                Assert.That(logger.IsEnabled(LogLevel.Information), Is.False);
                Assert.That(_geschrieben.ToString(), Does.Not.Contain("bitte nicht"));
                Assert.That(_geschrieben.ToString(), Does.Contain("aber das"));
            });

        }

        #endregion

        #region TheLogger_NamesTheException()

        /// <summary>
        /// Eine mitgegebene Ausnahme steht in der Zeile.
        /// </summary>
        /// <remarks>
        /// <c>ILogger</c> reicht die Ausnahme getrennt vom Text durch, und der
        /// Formatierer lässt sie weg. Wer sie nicht selbst anhängt, protokolliert
        /// „Verbindung verloren" und verschweigt, woran.
        /// </remarks>
        [Test]
        public void TheLogger_NamesTheException()
        {

            using var provider = new ConsoleOutputLoggerProvider(_ausgabe, LogLevel.Information);

            provider.CreateLogger("Test")
                    .LogError(new InvalidOperationException("Socket weg"), "Verbindung verloren");

            var text = _geschrieben.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Verbindung verloren"));
                Assert.That(text, Does.Contain("InvalidOperationException"));
                Assert.That(text, Does.Contain("Socket weg"));
            });

        }

        #endregion

        #region ParallelWriters_DoNotInterleave()

        /// <summary>
        /// Zwei Fäden, die gleichzeitig schreiben, verschränken sich nicht.
        /// </summary>
        /// <remarks>
        /// Der zweite, weniger sichtbare Teil der Änderung: Ereignisse kommen
        /// aus dem Empfangsfaden, das Protokoll aus jedem beliebigen. Ohne die
        /// Sperre steht die eine Zeile mitten in der anderen - und die Farbe,
        /// die die eine gesetzt hat, stellt die andere zurück.
        ///
        /// Geprüft wird an der Form: Jede Ausgabe besteht aus Räumen, Text und
        /// Eingabeaufforderung. Verschränken sich zwei, steht irgendwo ein
        /// Textstück ohne seinen Anfang.
        /// </remarks>
        [Test]
        public void ParallelWriters_DoNotInterleave()
        {

            var langsam = new LangsamerWriter();
            var ausgabe = new ConsoleOutput(() => "|", langsam, () => 0);

            Parallel.For(0, 8, i =>
                ausgabe.Write(w =>
                {
                    w.Write("<");
                    w.Write(i);
                    w.Write(">");
                }));

            // Jede Ausgabe ist "<n>|" - dazwischen darf nichts stehen.
            Assert.That(langsam.ToString(),
                        Does.Match("^(<[0-7]>\\|){8}$"),
                        $"Zwei Ausgaben haben sich verschränkt: {langsam}");

        }

        /// <summary>
        /// Ein Schreiber, der sich zwischen zwei Aufrufen Zeit lässt - damit
        /// eine Verschränkung überhaupt eine Gelegenheit bekommt.
        /// </summary>
        private sealed class LangsamerWriter : StringWriter
        {

            public override void Write(String? value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

            public override void Write(Char value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

            public override void Write(Int32 value)
            {
                Thread.Sleep(1);
                base.Write(value);
            }

        }

        #endregion

    }

}
