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
/// <b>Alle fünf.</b> Was dieser Server nicht durchsetzen kann, bietet er nicht
/// an, statt es anzunehmen und zu übergehen - bei einem Zugriffsmodell wäre
/// das der teuerste Ort für eine Zusage ohne Deckung: Wer <c>whitelist</c>
/// einstellt und <c>open</c> bekommt, glaubt seine Einträge geschützt und hat
/// sie veröffentlicht. Dass die Liste jetzt vollständig ist, heisst deshalb
/// auch: Jedes Modell hier tut etwas.
///
/// <see cref="Whitelist"/> kam mit den Rollen dazu (K13): Es ist das Modell,
/// das <see cref="PubSubAffiliation.Member"/> überhaupt einen Sinn gibt.
/// <see cref="Roster"/> folgte in D92 - und brauchte erst einmal einen Server,
/// der Rostergruppen überhaupt kennt (D91). <see cref="Authorize"/> in D93, mit
/// dem Zustand <see cref="PubSubSubscriptionState.Pending"/>, den es bis dahin
/// nur auf dem Papier gab.
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
    Whitelist,

    /// <summary>
    /// Nur, wer im Roster des Eigentümers steht - und, wenn Gruppen genannt
    /// sind, in einer davon.
    /// </summary>
    /// <remarks>
    /// <b>Der Roster ist die Liste des Eigentümers</b>, und deshalb genügt ein
    /// Eintrag: Wer darin steht, steht dort, weil der Eigentümer ihn
    /// eingetragen hat. Ein Presence-Zustand wird nicht verlangt - das wäre
    /// <see cref="Presence"/>, und das ist eine andere Frage: Dort geht es
    /// darum, wer <i>mich sehen darf</i>, hier darum, wen <i>ich führe</i>.
    /// Beides kann auseinandergehen, und dann sind es zwei verschiedene
    /// Antworten und keine ungenaue.
    ///
    /// <b>Ohne genannte Gruppen kommt der ganze Roster herein.</b> Eine leere
    /// Liste als „niemand" zu lesen wäre die andere Möglichkeit und die
    /// schlechtere: Sie machte das Modell in seiner Grundeinstellung
    /// wirkungsgleich mit einer leeren <see cref="Whitelist"/> - zwei Namen
    /// für dieselbe Sache, und einer davon führte in die Irre.
    /// </remarks>
    Roster,

    /// <summary>
    /// Nur, wen der Eigentümer einzeln hereingelassen hat.
    /// </summary>
    /// <remarks>
    /// <b>Das einzige Modell, bei dem Abonnieren und Hereinkommen zwei Dinge
    /// sind.</b> Bei allen anderen entscheidet dieselbe Regel beides: Wer nicht
    /// hereindarf, darf auch nicht abonnieren. Hier darf jeder <i>fragen</i> -
    /// das Fragen ist der Vorgang -, und was er bekommt, ist ein Abonnement im
    /// Zustand <see cref="PubSubSubscriptionState.Pending"/>: angenommen,
    /// aber noch nicht zugesagt.
    ///
    /// Der Unterschied zu <see cref="Whitelist"/> ist der Zeitpunkt: Dort muss
    /// der Eigentümer jemanden eintragen, <i>bevor</i> der fragt, und erfährt
    /// nie, dass jemand vergeblich angeklopft hat. Hier kommt die Frage bei
    /// ihm an.
    /// </remarks>
    Authorize

}
