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

using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die Einstellungen eines Knotens (XEP-0060, Abschnitt 8.2).
/// </summary>
/// <param name="AccessModel">Wer an die Einträge kommt.</param>
/// <param name="MaxItems">
/// Wie viele Einträge der Knoten behält; ist die Grenze erreicht, weicht der
/// älteste.
/// </param>
/// <param name="PersistItems">
/// Werden Einträge überhaupt behalten? Ein Knoten ohne Ablage meldet nur -
/// wer nicht zuhörte, hat es verpasst.
/// </param>
/// <remarks>
/// <b>Drei Felder, und jedes tut etwas.</b> XEP-0060 kennt zwei Dutzend
/// weitere - Titel, Sprache, Benachrichtigungen über Konfigurationsänderungen,
/// Sammelabfragen, Publikationsmodelle. Angeboten wird hier nur, was auch
/// wirkt; alles andere wäre eine Zusage ohne Deckung, und zwar an der Stelle,
/// an der ein Eigentümer glaubt, etwas geregelt zu haben.
/// </remarks>
public sealed record PubSubNodeConfiguration(PubSubAccessModel  AccessModel   = PubSubAccessModel.Open,
                                             Int32              MaxItems      = 256,
                                             Boolean            PersistItems  = true)
{

    /// <summary>Der Formulartyp dieser Einstellungen.</summary>
    public const String FormType = "http://jabber.org/protocol/pubsub#node_config";

    /// <summary>Das Feld für das Zugriffsmodell.</summary>
    public const String AccessModelVariable = "pubsub#access_model";

    /// <summary>Das Feld für die Zahl der behaltenen Einträge.</summary>
    public const String MaxItemsVariable = "pubsub#max_items";

    /// <summary>Das Feld für die Ablage.</summary>
    public const String PersistItemsVariable = "pubsub#persist_items";

    /// <summary>Die Vorgabe: offen, 256 Einträge, mit Ablage.</summary>
    public static readonly PubSubNodeConfiguration Default = new();

    /// <summary>Das Zugriffsmodell, wie es im Formular steht.</summary>
    public static String NameOf(PubSubAccessModel model)
        => model switch {
               PubSubAccessModel.Presence   => "presence",
               PubSubAccessModel.Whitelist  => "whitelist",
               _                            => "open"
           };

    /// <summary>
    /// Liest ein Zugriffsmodell.
    /// </summary>
    /// <returns>
    /// false bei allem, was dieser Server nicht durchsetzen kann - auch bei
    /// <c>authorize</c> und <c>roster</c>.
    /// </returns>
    /// <remarks>
    /// <b>Eine Stelle für alle, die danach fragen</b>: das Knotenformular in
    /// beide Richtungen und die Bedingungen einer Veröffentlichung. Vier
    /// Stellen, die dieselbe Liste führen, führen sie irgendwann verschieden -
    /// und die eine, die ein Modell nicht kennt, lässt es still als
    /// <c>open</c> durchgehen.
    /// </remarks>
    public static Boolean TryReadAccessModel(String? name, out PubSubAccessModel model)
    {

        switch (name)
        {

            case "open":       model = PubSubAccessModel.Open;       return true;
            case "presence":   model = PubSubAccessModel.Presence;   return true;
            case "whitelist":  model = PubSubAccessModel.Whitelist;  return true;

            default:           model = PubSubAccessModel.Open;       return false;

        }

    }

    /// <summary>
    /// Das Angebot des Dienstes (<c>type='form'</c>) - was sich einstellen
    /// lässt und was gerade gilt.
    /// </summary>
    public XElement ToForm()
        => DataForm.Form("form", FormType,
               DataForm.Field(AccessModelVariable,  "list-single", "Wer an die Einträge kommt", NameOf(AccessModel)),
               DataForm.Field(MaxItemsVariable,     "text-single", "Behaltene Einträge",        MaxItems.ToString()),
               DataForm.Field(PersistItemsVariable, "boolean",     "Einträge behalten",         DataForm.Boolean(PersistItems)));

    /// <summary>Die Antwort des Eigentümers (<c>type='submit'</c>).</summary>
    public XElement ToSubmit()
        => DataForm.Form("submit", FormType,
               DataForm.Field(AccessModelVariable,  null, null, NameOf(AccessModel)),
               DataForm.Field(MaxItemsVariable,     null, null, MaxItems.ToString()),
               DataForm.Field(PersistItemsVariable, null, null, DataForm.Boolean(PersistItems)));

    /// <summary>
    /// Liest ein abgeschicktes Formular - streng, wie jede Anweisung.
    /// </summary>
    /// <param name="basis">
    /// Der Stand, auf den sich fehlende Felder beziehen. XEP-0060,
    /// Abschnitt 8.2.4 lässt Teilformulare zu; was nicht dasteht, bleibt wie
    /// es war.
    /// </param>
    /// <returns>
    /// false, wenn es kein abgeschicktes Formular ist, den falschen Zweck hat,
    /// ein unbekanntes Feld enthält oder einen Wert, der keiner ist.
    /// </returns>
    public static Boolean TryRead(XElement                  x,
                                  PubSubNodeConfiguration   basis,
                                  out PubSubNodeConfiguration?  configuration)
    {

        configuration = null;

        if (!DataForm.Is(x, "submit"))
            return false;

        var zugriff  = basis.AccessModel;
        var anzahl   = basis.MaxItems;
        var ablage   = basis.PersistItems;

        foreach (var field in DataForm.Fields(x))
        {

            var wert = DataForm.ValueOf(field);

            switch (field.Attr("var"))
            {

                case "FORM_TYPE":
                    if (wert != FormType)
                        return false;
                    break;

                case AccessModelVariable:
                    // authorize und roster stehen nicht im Angebot. Sie
                    // anzunehmen und offen zu bleiben wäre die gefährlichste
                    // Höflichkeit dieses Servers.
                    if (!TryReadAccessModel(wert, out zugriff))
                        return false;
                    break;

                case MaxItemsVariable:
                    if (!Int32.TryParse(wert, out anzahl) || anzahl < 1)
                        return false;
                    break;

                case PersistItemsVariable:
                    if (!DataForm.TryBoolean(wert, out ablage))
                        return false;
                    break;

                default:
                    return false;

            }

        }

        configuration = new PubSubNodeConfiguration(zugriff, anzahl, ablage);

        return true;

    }

    /// <summary>
    /// Liest das Angebot eines Dienstes (<c>type='form'</c>) - nachsichtig,
    /// wie jede Auskunft.
    /// </summary>
    /// <remarks>
    /// Unbekannte Felder werden übergangen: Ein fremder Dienst bietet zwei
    /// Dutzend an, von denen dieser Client drei versteht. Ein Angebot, das
    /// keines davon nennt, ist trotzdem keines - dann gibt es nichts zu lesen.
    /// </remarks>
    public static Boolean TryReadForm(XElement x, out PubSubNodeConfiguration? configuration)
    {

        configuration = null;

        if (!DataForm.Is(x, "form"))
            return false;

        var gefunden  = false;
        var zugriff   = PubSubAccessModel.Open;
        var anzahl    = Default.MaxItems;
        var ablage    = Default.PersistItems;

        foreach (var field in DataForm.Fields(x))
        {

            var wert = DataForm.ValueOf(field);

            switch (field.Attr("var"))
            {

                case "FORM_TYPE":
                    if (wert != FormType)
                        return false;
                    break;

                case AccessModelVariable:
                    // Ein fremdes Modell wird gelesen, wie es ist: Ein Client,
                    // der 'authorize' zu 'open' verkürzte, zeigte dem Menschen
                    // das Gegenteil dessen, was gilt. Hier gibt es dafür keinen
                    // Wert - also ist das Angebot nicht zu lesen.
                    if (!TryReadAccessModel(wert, out zugriff))
                        return false;
                    gefunden = true;
                    break;

                case MaxItemsVariable:
                    if (!Int32.TryParse(wert, out anzahl))
                        return false;
                    gefunden = true;
                    break;

                case PersistItemsVariable:
                    if (!DataForm.TryBoolean(wert, out ablage))
                        return false;
                    gefunden = true;
                    break;

            }

        }

        if (!gefunden)
            return false;

        configuration = new PubSubNodeConfiguration(zugriff, anzahl, ablage);

        return true;

    }

}
