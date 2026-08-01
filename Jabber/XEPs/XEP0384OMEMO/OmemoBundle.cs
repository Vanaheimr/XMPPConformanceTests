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
/// Ein PreKey mit seiner Kennung.
/// </summary>
/// <param name="Id">
/// Eine positive ganze Zahl (XEP-0384, Abschnitt 5.3.2: 1 bis 2³¹-1).
/// </param>
/// <param name="PublicKey">Der öffentliche Teil, 32 Byte Montgomery-u.</param>
public sealed record OmemoPreKey(UInt32 Id, Byte[] PublicKey);

/// <summary>
/// Das öffentliche Bundle eines Geräts (XEP-0384, Abschnitt 5.3.2) - alles,
/// was ein Fremder braucht, um ohne Rückfrage eine Sitzung zu beginnen.
/// </summary>
/// <param name="IdentityKey">
/// Der IdentityKey <b>in Ed25519-Form</b>. Der Abschnitt lässt beide internen
/// Formen zu, legt die Übertragung aber fest: „The public key is ALWAYS
/// transferred in its Ed25519 form."
/// </param>
/// <param name="SignedPreKeyId">Die Kennung des Signed PreKey.</param>
/// <param name="SignedPreKey">Der Signed PreKey, 32 Byte Montgomery-u.</param>
/// <param name="SignedPreKeySignature">
/// Die Signatur des IdentityKey über den Signed PreKey.
/// </param>
/// <param name="PreKeys">Die einmal verwendbaren PreKeys.</param>
/// <remarks>
/// <b>Das Bundle ist die einzige Stelle, an der eine Sitzung ohne den anderen
/// beginnt.</b> Bob ist offline, Alice schreibt ihm trotzdem verschlüsselt -
/// das geht nur, weil sein Server seine Schlüssel vorrätig hält. Damit ist der
/// Server auch der naheliegende Angreifer: Er könnte ein eigenes Bundle
/// unterschieben. Dagegen hilft genau zweierlei - die Signatur über den Signed
/// PreKey (der Server kann sie nicht fälschen, ohne Bobs IdentityKey zu haben)
/// und der Fingerabdruck, den ein Mensch vergleicht (gegen einen ausgetauschten
/// IdentityKey hilft nur er).
/// </remarks>
public sealed record OmemoBundle(Byte[]                       IdentityKey,
                                 UInt32                       SignedPreKeyId,
                                 Byte[]                       SignedPreKey,
                                 Byte[]                       SignedPreKeySignature,
                                 IReadOnlyList<OmemoPreKey>   PreKeys)
{

    /// <summary>
    /// Der IdentityKey in Montgomery-Form, wie ihn Diffie-Hellman braucht.
    /// </summary>
    public Byte[] IdentityKeyForAgreement()
        => Curve25519.EdwardsToMontgomery(IdentityKey);

    /// <summary>
    /// Ist die Signatur über den Signed PreKey gültig?
    /// </summary>
    /// <remarks>
    /// <b>Vor jeder Benutzung zu fragen, und ohne Ausnahme.</b> Ein Bundle
    /// kommt vom Server der Gegenstelle - also von genau der Partei, gegen die
    /// eine Ende-zu-Ende-Verschlüsselung schützen soll. Ohne diese Prüfung
    /// könnte er den Signed PreKey durch seinen eigenen ersetzen und jede
    /// erste Nachricht mitlesen; der Fingerabdruck des IdentityKey bliebe
    /// dabei unverändert, und der Mensch, der ihn vergleicht, sähe nichts.
    ///
    /// Unterschrieben wird der Signed PreKey <b>in Montgomery-Form</b>, so wie
    /// er im Bundle steht. Die Spezifikation sagt an dieser Stelle nur „the
    /// signed PreKey signature"; welche Kodierung gemeint ist, steht dort
    /// nicht, und es gibt hier keine fremde Gegenstelle, an der sich das
    /// nachprüfen liesse. <b>Das ist die wahrscheinlichste Lesart und eine
    /// ungeprüfte Annahme</b> - stimmt sie nicht, scheitert die Prüfung gegen
    /// fremde Clients an dieser einen Zeile.
    /// </remarks>
    public Boolean SignatureIsValid()
        => Curve25519.VerifyEdwards(IdentityKey, SignedPreKey, SignedPreKeySignature);

}
