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
/// Wer an die Einträge eines Knotens kommt (XEP-0060, Abschnitt 4.5).
/// </summary>
/// <remarks>
/// <b>Drei von fünf.</b> XEP-0060 kennt ausserdem <c>authorize</c> (der
/// Eigentümer genehmigt jedes Abonnement einzeln) und <c>roster</c> (nur
/// bestimmte Rostergruppen). Beide brauchen etwas, das dieser Server nicht
/// hat - einen Genehmigungsvorgang, Rostergruppen als Zugriffsregel.
///
/// Sie werden deshalb nicht angeboten, statt angeboten und ignoriert zu
/// werden. Bei einem Zugriffsmodell wäre das der teuerste Ort für eine Zusage
/// ohne Deckung: Wer <c>whitelist</c> einstellt und <c>open</c> bekommt,
/// glaubt seine Einträge geschützt und hat sie veröffentlicht.
///
/// <see cref="Whitelist"/> kam mit den Rollen dazu (K13): Es ist das Modell,
/// das <see cref="PubSubAffiliation.Member"/> überhaupt einen Sinn gibt.
/// </remarks>
public enum PubSubAccessModel
{

    /// <summary>
    /// Wer fragt, bekommt.
    /// </summary>
    /// <remarks>
    /// Die Vorgabe, und für OMEMO die einzig brauchbare: Wer einem Menschen
    /// verschlüsselt schreiben will, muss dessen Bundle lesen können - im
    /// Zweifel jemand, der in keinem Roster steht (XEP-0384, Abschnitt 5.2).
    /// </remarks>
    Open,

    /// <summary>
    /// Nur, wer die Presence des Eigentümers sehen darf.
    /// </summary>
    Presence,

    /// <summary>
    /// Nur, wer auf der Liste steht: der Eigentümer, ein
    /// <see cref="PubSubAffiliation.Publisher"/> und ein
    /// <see cref="PubSubAffiliation.Member"/>.
    /// </summary>
    /// <remarks>
    /// <b>Das strengste der drei Modelle und das einzige, bei dem der Roster
    /// nichts entscheidet.</b> Presence-Berechtigung entsteht nebenbei -
    /// jemand nimmt einen Kontakt auf, und schon sieht er mehr. Eine Liste
    /// entsteht nicht nebenbei: Auf ihr steht nur, wen der Eigentümer
    /// ausdrücklich daraufgesetzt hat.
    /// </remarks>
    Whitelist

}
