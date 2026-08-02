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

using System.Threading;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI
{

    /// <summary>
    /// Die eine Stelle, über die alles auf die Konsole geht: eingehende
    /// Nachrichten, Systemmeldungen und die Protokollausgabe.
    /// </summary>
    /// <remarks>
    /// Eine Konsole, die zugleich eine Eingabezeile führt, hat ein Problem, das
    /// eine reine Ausgabe nicht hat: <b>Der Anwender tippt gerade.</b> Wer
    /// dazwischen schreibt, zerlegt seine halb fertige Zeile und lässt ihn ohne
    /// Eingabeaufforderung zurück.
    ///
    /// Die Ereignisbehandlung hat das von Anfang an beachtet und jede Ausgabe
    /// von Hand in „Zeile löschen … Eingabeaufforderung neu schreiben"
    /// geklammert - elfmal dieselben zwei Zeilen. <b>Der Logger hat es nicht
    /// beachtet</b>, denn der wusste von alldem nichts: Ein
    /// <c>AddSimpleConsole</c> schreibt, wann es ihm passt.
    ///
    /// Hier laufen beide zusammen, und zwar unter <b>einer Sperre</b>. Die ist
    /// der zweite, weniger sichtbare Teil: Die Ereignisse kommen aus dem
    /// Empfangsfaden, das Protokoll aus jedem beliebigen, und zwei
    /// gleichzeitige Ausgaben verschränken sich sonst mitten im Wort - samt der
    /// Farbe, die die eine gesetzt und die andere zurückgestellt hat.
    ///
    /// <b>Warum kein <c>TextWriter</c> als Abstraktion?</b> Weil das Löschen
    /// der Zeile und das Setzen der Farbe keine Schreibvorgänge sind, sondern
    /// Steuerung. Ein Schreiber, der beides nicht kennt, könnte die Aufgabe
    /// nicht erfüllen; einer, der es kennt, wäre wieder diese Klasse.
    /// </remarks>
    public sealed class ConsoleOutput
    {

        #region Data

        private readonly Lock _lock = new();
        private readonly TextWriter _writer;
        private readonly Func<String> _prompt;
        private readonly Func<Int32> _width;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Erzeugt die Ausgabe.
        /// </summary>
        /// <param name="prompt">
        /// Liefert die Eingabeaufforderung, wie sie nach einer Ausgabe wieder
        /// dastehen soll. Eine Funktion und keine Zeichenkette: Sie ändert sich
        /// mit dem Gesprächspartner.
        /// </param>
        /// <param name="writer">Wohin geschrieben wird; ohne Angabe die Konsole.</param>
        /// <param name="width">
        /// Wie breit die Zeile zu löschen ist. Ohne Angabe die Konsolenbreite -
        /// und wenn es keine gibt (umgeleitete Ausgabe), null: Dann ist auch
        /// nichts zu löschen, weil dort niemand tippt.
        /// </param>
        public ConsoleOutput(Func<String>   prompt,
                             TextWriter?    writer  = null,
                             Func<Int32>?   width   = null)
        {

            _prompt  = prompt;
            _writer  = writer ?? Console.Out;
            _width   = width  ?? Breite;

        }

        #endregion


        #region Write(ausgabe)

        /// <summary>
        /// Schreibt etwas, das <b>ungefragt</b> kommt: Die angefangene
        /// Eingabezeile weicht, die Ausgabe erscheint, die Eingabeaufforderung
        /// steht wieder da.
        /// </summary>
        /// <param name="ausgabe">
        /// Was zu schreiben ist. Als Rückruf und nicht als Zeichenkette, damit
        /// mehrzeilige und eingefärbte Ausgaben als <b>ein</b> Vorgang unter
        /// der Sperre laufen. Eine Ausgabe, die zwischendurch die Farbe
        /// wechselt, wäre sonst genau dort zu unterbrechen.
        /// </param>
        public void Write(Action<TextWriter> ausgabe)
        {

            using var bereich = Begin();

            ausgabe(_writer);

        }

        /// <summary>Kurzform für eine einzelne Zeile ohne Farbe.</summary>
        public void WriteLine(String zeile)
            => Write(w => w.WriteLine(zeile));

        #endregion

        #region Begin()

        /// <summary>
        /// Eröffnet einen Ausgabebereich: Die Eingabezeile weicht, und beim
        /// Verlassen steht die Eingabeaufforderung wieder da.
        /// </summary>
        /// <remarks>
        /// Für Ausgaben, die sich nicht in einen Rückruf fassen lassen, ohne
        /// unleserlich zu werden - etwa eine, die mitten in einer
        /// <c>switch</c>-Weiche die Farbe wechselt. Der Bereich hält die Sperre
        /// bis zum Verlassen; solange gehört die Konsole dem Aufrufer allein.
        ///
        /// <b>Nicht verschachteln:</b> Die Sperre ist wiedereintrittsfähig, der
        /// innere Bereich schriebe beim Verlassen aber schon die
        /// Eingabeaufforderung, und der äussere danach seine Ausgabe dahinter.
        /// </remarks>
        public IDisposable Begin()
        {

            _lock.Enter();

            try
            {
                ZeileLoeschen();
            }
            catch
            {
                _lock.Exit();
                throw;
            }

            return new Bereich(this);

        }

        /// <summary>Das Ende eines Ausgabebereichs.</summary>
        private sealed class Bereich : IDisposable
        {

            private readonly ConsoleOutput _output;
            private Boolean _beendet;

            internal Bereich(ConsoleOutput output)
            {
                _output = output;
            }

            public void Dispose()
            {

                if (_beendet)
                    return;

                _beendet = true;

                try
                {
                    _output._writer.Write(_output._prompt());
                    _output._writer.Flush();
                }
                finally
                {
                    _output._lock.Exit();
                }

            }

        }

        #endregion

        #region WritePrompt()

        /// <summary>
        /// Schreibt die Eingabeaufforderung - für den Aufrufer, der gerade eine
        /// Eingabe erwartet.
        /// </summary>
        public void WritePrompt()
        {
            lock (_lock)
            {
                _writer.Write(_prompt());
                _writer.Flush();
            }
        }

        #endregion

        #region (private) Hilfsfunktionen

        private void ZeileLoeschen()
        {

            var breite = _width();

            if (breite <= 0)
                return;

            _writer.Write('\r');
            _writer.Write(new String(' ', breite - 1));
            _writer.Write('\r');

        }

        /// <summary>
        /// Die Konsolenbreite, oder 0, wenn es keine gibt.
        /// </summary>
        /// <remarks>
        /// <c>Console.WindowWidth</c> wirft, sobald die Ausgabe umgeleitet ist
        /// - und genau dann gibt es auch keine Eingabezeile, die zu retten
        /// wäre. Die alte Fassung fing den Wurf und schrieb ersatzweise eine
        /// Leerzeile; das half niemandem und trennte jede Ausgabe von der
        /// nächsten.
        /// </remarks>
        private static Int32 Breite()
        {
            try
            {
                return Console.WindowWidth;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

    }

}
