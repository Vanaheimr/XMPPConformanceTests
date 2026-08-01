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

using System.Xml;
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Was mit einer Stanza geschieht, solange der Client sich für inaktiv
/// erklärt hat (XEP-0352, Abschnitt 3).
/// </summary>
public enum ClientStateHandling
{

    /// <summary>Geht sofort hinaus - der Zustand des Clients ändert daran nichts.</summary>
    Immediately,

    /// <summary>Wird zurückgehalten und beim <c>&lt;active/&gt;</c> nachgeliefert.</summary>
    Queued,

    /// <summary>Wird fallengelassen und kommt nie an.</summary>
    Discarded

}

/// <summary>
/// XEP-0352: Client State Indication - der Client sagt, ob ein Mensch
/// hinsieht.
/// </summary>
/// <remarks>
/// Zwei Nonzas, keine Antwort (Abschnitt 4.2: „There is no reply from the
/// server to either of these elements"), und der Server darf daraufhin
/// Verkehr zurückhalten. Der Sinn ist nicht Sparsamkeit auf der Leitung: Ein
/// Funkmodem, das für jede Presence-Änderung aufwacht, leert den Akku eines
/// Telefons, das in der Tasche liegt.
///
/// <b>Was zurückgehalten werden darf, entscheidet der Server</b> - die
/// Spezifikation nennt in Abschnitt 3 nur Beispiele. Diese Klasse hält die
/// Entscheidung an einer Stelle fest und beantwortet sie als reine Funktion,
/// damit sie einzeln prüfbar ist und nicht in der Sendeschleife der Sitzung
/// verschwindet.
///
/// Die Leitlinie dahinter: <b>Zurückgehalten wird nur, was später noch wahr
/// ist.</b> Eine Presence von vorhin ist überholbar, aber nicht falsch - die
/// letzte gilt. Ein „schreibt gerade" von vorhin ist nach der Zustellung
/// schlicht gelogen; deshalb wird es fallengelassen und nicht aufgehoben
/// (Abschnitt 3: „Discard messages containing only Chat State Notifications
/// … payloads"). Und alles, worauf ein Absender wartet, geht sofort hinaus.
/// </remarks>
public static class ClientStateIndication
{

    /// <summary>Der Namespace von XEP-0352.</summary>
    public const String Namespace    = "urn:xmpp:csi:0";

    /// <summary>Die Ankündigung unter den Stream-Features (Abschnitt 4.1).</summary>
    public const String FeatureXml   = $"<csi xmlns='{Namespace}'/>";

    /// <summary>„Es sieht wieder jemand hin."</summary>
    public const String ActiveXml    = $"<active xmlns='{Namespace}'/>";

    /// <summary>„Das Gerät liegt in der Tasche."</summary>
    public const String InactiveXml  = $"<inactive xmlns='{Namespace}'/>";

    #region HandlingOf(stanza)

    /// <summary>
    /// Wie mit dieser Stanza zu verfahren ist, solange der Client inaktiv ist.
    /// </summary>
    /// <remarks>
    /// <b>Nonzas und <c>iq</c> gehen sofort hinaus.</b> Ein <c>&lt;a/&gt;</c>
    /// oder ein Stream-Fehler gehört nicht zum Verkehr, den ein Telefon
    /// aufschieben möchte, sondern zum Stream selbst. Und ein <c>iq</c> ist
    /// eine Frage mit Frist: Wer es zurückhält, lässt beim Absender die Zeit
    /// ablaufen und beantwortet sie danach - die Antwort kommt dann zu einer
    /// Frage, die niemand mehr stellt.
    ///
    /// <b>Fehler gehen sofort hinaus</b>, in beiden Richtungen und für beide
    /// Stanza-Arten: Ein Fehler ist die Antwort auf etwas, das der Client
    /// selbst geschickt hat.
    ///
    /// <b>Eine Nachricht mit Text ist der Grund, warum das Gerät klingelt.</b>
    /// Sie zurückzuhalten hiesse, aus einer Verkehrsersparnis eine
    /// Zustellverzögerung zu machen - und genau dafür ist XEP-0352 nicht da.
    ///
    /// Ein <c>&lt;body/&gt;</c> aus lauter Leerzeichen zählt nicht als Text.
    /// Andersherum gälte jede Nachricht als wichtig, die ein leeres
    /// <c>&lt;body/&gt;</c> neben ihren Chat States mitführt - und das tun
    /// Clients tatsächlich.
    /// </remarks>
    public static ClientStateHandling HandlingOf(String stanza)
    {

        var name = StanzaElement.NameOf(stanza);

        // Alles andere - iq und jede Nonza - ist unaufschiebbar.
        if (name is not ("message" or "presence"))
            return ClientStateHandling.Immediately;

        XElement element;

        try
        {
            element = XElement.Parse(stanza, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // Was sich nicht lesen lässt, wird nicht zurückgehalten. Ein
            // Puffer ist der schlechteste Ort für etwas Unverstandenes: Es
            // käme später heraus, und niemand wüsste dann noch, warum.
            return ClientStateHandling.Immediately;
        }

        var type = element.Attr("type");

        if (type == "error")
            return ClientStateHandling.Immediately;

        // Presence: die Anwesenheit selbst ist aufschiebbar, die Frage nach
        // ihr nicht. Ein <presence type='subscribe'/> wartet auf eine
        // Entscheidung des Menschen und ist damit dasselbe wie eine
        // Nachricht - RFC 6121, Abschnitt 3.1.3.
        if (name == "presence")
            return type is "subscribe" or "subscribed" or "unsubscribe" or "unsubscribed"
                       ? ClientStateHandling.Immediately
                       : ClientStateHandling.Queued;

        if (!String.IsNullOrWhiteSpace(element.Elements()
                                              .FirstOrDefault(e => e.Name.LocalName == "body")
                                              ?.Value))
            return ClientStateHandling.Immediately;

        // Gezählt werden nur die Erweiterungen, also die Kinder in einem
        // anderen Namensraum als die Stanza selbst. <thread/> steht im
        // Namensraum der Stanza und gehört keiner Erweiterung; wer es
        // mitzählte, hielte jede Chat-State-Nachricht mit Thread für eine
        // Nachricht mit Inhalt - und XEP-0085 empfiehlt genau diese Kombination.
        var erweiterungen = element.Elements()
                                   .Where(e => e.Name.NamespaceName != element.Name.NamespaceName)
                                   .ToList();

        if (erweiterungen.Count > 0 &&
            erweiterungen.All(e => e.Name.NamespaceName == ChatStateExtensions.Namespace))
            return ClientStateHandling.Discarded;

        return ClientStateHandling.Queued;

    }

    #endregion

    #region SupersedeKey(stanza)

    /// <summary>
    /// Wodurch diese zurückgehaltene Stanza von einer späteren abgelöst wird -
    /// oder null, wenn sie durch nichts abgelöst wird.
    /// </summary>
    /// <remarks>
    /// Abschnitt 3 nennt es als erste Massnahme: „Suppress presence updates
    /// until the client becomes active again. On becoming active, push the
    /// <b>latest</b> presence from each contact." Ein Kontakt, der in zehn
    /// Minuten fünfmal zwischen „da" und „weg" wechselt, hinterlässt damit
    /// eine Presence und nicht fünf.
    ///
    /// Der Schlüssel ist die Full-JID des Absenders und nicht sein Bare-JID:
    /// Zwei Geräte desselben Menschen sind zwei Anwesenheiten, und die eine
    /// darf die andere nicht verdrängen - sonst verschwände sein Telefon aus
    /// der Liste, weil sein Rechner sich abgemeldet hat.
    ///
    /// Verdrängt wird nur unter Gleichen: Eine Abmeldung löst eine Anmeldung
    /// ab und umgekehrt, denn beide beantworten dieselbe Frage. Was
    /// <see cref="HandlingOf"/> ohnehin sofort hinausgibt, kommt hier gar
    /// nicht erst an.
    /// </remarks>
    public static String? SupersedeKey(String stanza)
    {

        if (!StanzaElement.Is(stanza, "presence") ||
            HandlingOf(stanza) != ClientStateHandling.Queued)
            return null;

        String? from;

        try
        {
            from = XElement.Parse(stanza, LoadOptions.PreserveWhitespace).Attr("from");
        }
        catch (XmlException)
        {
            return null;
        }

        return from is null
                   ? null
                   : $"presence {from}";

    }

    #endregion

}
