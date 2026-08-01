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
/// Was jemand an einem Knoten ist (XEP-0060, Abschnitt 4.1).
/// </summary>
/// <remarks>
/// <b>Vier von sechs, und jede entscheidet etwas.</b> XEP-0060 kennt ausserdem
/// <c>publish-only</c> — ein Publizierender, der nicht lesen darf. Der
/// Unterschied zu <see cref="Publisher"/> wäre eine dritte Zeile in zwei
/// Prüfungen und für einen PEP-Knoten eine exotische Rolle; er wird deshalb
/// abgewiesen statt angeboten.
///
/// <b>Der Eigentümer ist kein Eintrag, sondern das Konto.</b> Ein PEP-Knoten
/// gehört dem Menschen, in dessen Konto er steht, und das lässt sich nicht
/// umtragen: Wer den Eigentümer wechseln könnte, könnte einem anderen sein
/// eigenes Konto wegnehmen.
/// </remarks>
public enum PubSubAffiliation
{

    /// <summary>Keine Rolle - der Normalfall für Fremde.</summary>
    None,

    /// <summary>
    /// Der Eigentümer: das Konto, in dem der Knoten steht. Er darf alles und
    /// ist nicht setzbar.
    /// </summary>
    Owner,

    /// <summary>
    /// Darf in den Knoten veröffentlichen, ihn aber nicht einstellen.
    /// </summary>
    Publisher,

    /// <summary>
    /// Darf lesen und abonnieren, auch wenn der Knoten nur seiner Liste
    /// offensteht.
    /// </summary>
    /// <remarks>
    /// Wirksam wird das erst mit dem Zugriffsmodell <c>whitelist</c>; bei
    /// <c>open</c> und <c>presence</c> darf ein Mitglied nicht mehr als jeder
    /// andere. Eine Rolle, die nirgends etwas entscheidet, gäbe es hier nicht.
    /// </remarks>
    Member,

    /// <summary>
    /// Ausgeschlossen: kommt weder ans Abonnieren noch ans Abrufen, unabhängig
    /// vom Zugriffsmodell.
    /// </summary>
    /// <remarks>
    /// Und er verliert bestehende Abonnements (Abschnitt 8.9.4). Ihn nur an
    /// neuen zu hindern hiesse, den Ausschluss von dem Zufall abhängig zu
    /// machen, ob er vorher schon da war.
    /// </remarks>
    Outcast

}
