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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Welcher SASL-Mechanismus benutzt werden darf - und was geschieht, wenn ein
/// Server plötzlich weniger anbietet als beim letzten Mal.
/// </summary>
/// <remarks>
/// Die Auswahl selbst ist unstrittig: RFC 6120, Abschnitt 6.3.3 überlässt sie
/// dem Client, und der nimmt den stärksten Mechanismus, den er kennt.
///
/// Nur ist die Ankündigung des Servers nicht authentifiziert. Sie kommt zwar
/// über TLS, aber TLS beweist nur, dass die Gegenstelle das Zertifikat einer
/// vertrauten CA hat - und der klassische Zwischenmann hat eines. Wer allein
/// der Ankündigung folgt, folgt damit auch dem, der sie gefälscht hat: Aus den
/// Features verschwinden die SCRAM-Angebote, übrig bleibt PLAIN, und der
/// Client schickt bereitwillig das Passwort selbst statt eines Beweises, dass
/// er es kennt. Dieselbe Bewegung wie beim STARTTLS-Downgrade, eine Schicht
/// höher.
///
/// Dagegen zwei Untergrenzen, die dieselbe Prüfung durchlaufen:
///
/// <list type="bullet">
///   <item>
///     <b><see cref="Minimum"/></b> - was der Aufrufer verlangt. Wirkt vom
///     ersten Rahmen an, muss aber gesetzt werden.
///   </item>
///   <item>
///     <b><see cref="Pinned"/></b> - womit die letzte Anmeldung gelang. Wirkt
///     von selbst, aber erst ab der zweiten Verbindung.
///   </item>
/// </list>
///
/// Die Anheftung ist damit ein Trust-On-First-Use: Steht der Zwischenmann
/// schon beim allerersten Verbindungsaufbau dazwischen, heftet sie sein
/// Downgrade an statt es abzuwehren. Das ist der Grund, warum
/// <see cref="Minimum"/> daneben existiert - wer weiss, was sein Server kann,
/// sagt es hier und braucht kein erstes Mal.
///
/// Was sie dagegen schon in dieser Form abwehrt, ist der Angriff, der sich
/// lohnt: Der Client baut nach jedem Abriss von selbst neu auf, und ein
/// Abriss lässt sich erzwingen. Ohne Untergrenze genügt es also, die
/// Verbindung zu stören und die zweite Anmeldung abzufangen.
/// </remarks>
internal sealed class SaslMechanismPolicy
{

    #region Data

    /// <summary>SASL PLAIN (RFC 4616) - das Passwort selbst.</summary>
    public const String Plain         = "PLAIN";

    /// <summary>SCRAM-SHA-1 (RFC 5802).</summary>
    public const String ScramSha1     = "SCRAM-SHA-1";

    /// <summary>SCRAM-SHA-256 (RFC 7677).</summary>
    public const String ScramSha256   = "SCRAM-SHA-256";

    /// <summary>
    /// Die unterstützten Mechanismen, vom schwächsten zum stärksten. Die
    /// Reihenfolge ist die Rangfolge; der Index ist die Stärke.
    /// </summary>
    private static readonly String[] byStrength = [Plain, ScramSha1, ScramSha256];

    private String? minimum;

    #endregion

    #region Properties

    /// <summary>
    /// Der schwächste Mechanismus, der noch benutzt werden darf; null verlangt
    /// nichts.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Bei einem Namen, den diese Rangfolge nicht kennt. Ein Tippfehler ergäbe
    /// sonst die Stärke 0 und damit lautlos gar keine Untergrenze - genau das,
    /// was der Setzende ausschliessen wollte.
    /// </exception>
    public String? Minimum
    {

        get => minimum;

        set
        {

            if (value is not null && !IsKnown(value))
                throw new ArgumentException(
                          $"Unbekannter SASL-Mechanismus '{value}'. Bekannt sind: {String.Join(", ", byStrength)}.",
                          nameof(value));

            minimum = value;

        }

    }

    /// <summary>
    /// Der Mechanismus, über den die letzte Anmeldung gelang, oder null vor der
    /// ersten.
    /// </summary>
    public String? Pinned { get; private set; }

    #endregion


    #region (static) Strength  (mechanism)

    /// <summary>
    /// Die Stärke eines Mechanismus; 0 für jeden, den diese Rangfolge nicht
    /// kennt.
    /// </summary>
    /// <remarks>
    /// Verglichen wird ordinal. SASL-Mechanismusnamen sind nach RFC 4422,
    /// Abschnitt 3.1 Grossbuchstaben; ein "Plain" ist keine Schreibvariante,
    /// sondern ein anderer Name.
    /// </remarks>
    public static Int32 Strength(String? mechanism)

        => mechanism is null
               ? 0
               : Array.IndexOf(byStrength, mechanism) + 1;

    #endregion

    #region (static) IsKnown   (mechanism)

    /// <summary>Kennt diese Rangfolge den Mechanismus?</summary>
    public static Boolean IsKnown(String mechanism)

        => Strength(mechanism) > 0;

    #endregion

    #region (static) Strongest (Offered)

    /// <summary>
    /// Der stärkste angebotene Mechanismus, oder null, wenn kein bekannter
    /// dabei ist.
    /// </summary>
    /// <remarks>
    /// Ausgewählt wird nach der Rangfolge, nicht nach der Reihenfolge der
    /// Ankündigung - die bestimmt der Server, und der Server ist hier gerade
    /// die Instanz, der nicht zu trauen ist.
    /// </remarks>
    public static String? Strongest(IEnumerable<String> Offered)

        => Offered.Where (IsKnown).
                   OrderByDescending(Strength).
                   FirstOrDefault();

    #endregion

    #region EnsureAcceptable(Mechanism)

    /// <summary>
    /// Wirft, wenn der Mechanismus unter einer der beiden Untergrenzen liegt.
    /// </summary>
    /// <exception cref="AuthenticationException">Bei einem Downgrade.</exception>
    public void EnsureAcceptable(String Mechanism)
    {

        var strength = Strength(Mechanism);

        if (strength < Strength(minimum))
            throw new AuthenticationException(
                      $"SASL-Downgrade abgewehrt: Der Server bietet höchstens {Mechanism} an, " +
                      $"verlangt ist mindestens {minimum}.");

        if (strength < Strength(Pinned))
            throw new AuthenticationException(
                      $"SASL-Downgrade abgewehrt: Der Server bietet höchstens {Mechanism} an, " +
                      $"die letzte Anmeldung lief aber über {Pinned}.");

    }

    #endregion

    #region Remember       (Mechanism)

    /// <summary>
    /// Merkt sich den Mechanismus als Untergrenze für die nächste Verbindung.
    /// </summary>
    /// <remarks>
    /// Gehört hinter die gelungene Anmeldung, nicht davor: Ein Fehlschlag sagt
    /// nichts darüber, was dieser Server kann, und dürfte deshalb auch nichts
    /// anheften.
    /// </remarks>
    public void Remember(String Mechanism)
    {
        Pinned = Mechanism;
    }

    #endregion

}
