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
using System.Text.Json;
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

// System.ComponentModel and System.Diagnostics stood here until D107, for the
// Win32Exception and the ProcessStartInfo of the oracle call - and with them an
// alias, because System.ComponentModel brings a CategoryAttribute of its own
// and NUnit's [Category] is ambiguous against it (CS0104). Starting the far
// side moved to ForeignSide, so both are gone and the alias with them: the
// three lines of workaround were the last thing the duplicated process handling
// was still costing this file.

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// OMEMO against the reference implementation - python-omemo (Syndace),
    /// the same version `urn:xmpp:omemo:2`.
    /// </summary>
    /// <remarks>
    /// <b>This collection cannot find one class of errors on principle.</b> If
    /// both sides are the same code, they agree even when both calculate
    /// wrongly in the same way. In D62 to D65 that was the finding five times -
    /// an info string, an order, an embedding. Every time two clients of this
    /// house would have understood each other perfectly and not a single
    /// foreign one.
    ///
    /// The only thing that helps against that is a far end nobody here wrote.
    ///
    /// <b>These tests skip themselves</b> when the oracle is not reachable -
    /// like the tests against Prosody and ejabberd. A run without python-omemo
    /// is not supposed to be red, only to say less. How many are skipped says
    /// afterwards what was measured.
    ///
    /// Where the oracle runs depends on where the tests run: on Windows in
    /// WSL, because python-omemo is not a Windows library, and on Linux -
    /// developer's WSL, container in CI - directly. Only the detour is
    /// platform-bound; the reference implementation is the same one either
    /// way.
    ///
    /// What is checked is everything from the payload downwards: bundle
    /// format, X3DH, the beginning of the ratchet, the wire format. The SCE
    /// envelope stays out of it - python-omemo leaves it to the application
    /// using it.
    /// </remarks>
    // The whole fixture needs the far side, so the category sits here. Since
    // d39656e a missing one skips instead of throwing out of [OneTimeSetUp],
    // which is what makes the category a selector rather than a shield: the
    // gate excludes these because they cannot measure anything without
    // python-omemo, not because they would go red.
    [TestFixture]
    [Category(TestCategories.Wsl)]
    [Category(TestCategories.Omemo)]
    public class OmemoOracleTests
    {

        #region Calling the oracle

        private const String LibPath     = "/tmp/omemo-oracle/lib";

        /// <summary>
        /// The oracle, relative to the test assembly.
        /// </summary>
        /// <remarks>
        /// The csproj copies `XEPs/Oracle/**` into the output directory, and
        /// that copy is what makes this path hold at all. Before it, the file
        /// was searched for by walking upwards from the output until precisely
        /// it lay there - and that walk finds nothing the moment the output
        /// lies outside the repository. Which is exactly what the run both
        /// setup scripts print does: `--artifacts-path /tmp/conformance-artifacts`
        /// keeps the Linux build out of the Windows tree, so the documented
        /// command turned three tests red with "the oracle is not to be found"
        /// while the checkout was perfectly fine.
        ///
        /// The walk stays behind the copy as a fallback, for an output
        /// directory built before it existed.
        ///
        /// Until D97 the walk went by `WORKPLAN.md` instead, and the path
        /// pointed at `Jabber.Tests/` (this suite under its name of back then):
        /// both belonged to the program, not to the library, and both were
        /// wrong here after the move. Ratatoskr has to be able to run its own
        /// tests even when nobody has checked this repository out next to it.
        /// </remarks>
        private static readonly String ScriptPath = Path.Combine("XEPs", "Oracle", "omemo_oracle.py");

        private static String? _reasonForSkipping;

        [OneTimeSetUp]
        public void CheckTheOracle()
        {

            var (code, _, errors) = Call("bundle", null, check: false);

            if (code != 0)
                _reasonForSkipping =
                    $"The oracle is not reachable (python-omemo under {LibPath}" +
                    (OperatingSystem.IsLinux() ? "" : ", in WSL") + "): " +
                    $"{errors.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim()}";

        }

        [SetUp]
        public void SkipIfNeeded()
        {
            if (_reasonForSkipping is not null)
                Assert.Ignore(_reasonForSkipping);
        }

        /// <summary>
        /// Where the oracle lies: next to the test assembly, where the csproj
        /// copies it - and otherwise upwards from there.
        /// </summary>
        private static String? WhereTheOracleLies()
        {

            var beside = Path.Combine(AppContext.BaseDirectory, ScriptPath);

            if (File.Exists(beside))
                return beside;

            var root = new DirectoryInfo(AppContext.BaseDirectory);

            while (root is not null && !File.Exists(Path.Combine(root.FullName, ScriptPath)))
                root = root.Parent;

            return root is not null
                       ? Path.Combine(root.FullName, ScriptPath)
                       : null;

        }

        /// <summary>
        /// Starts the oracle and returns what it said.
        /// </summary>
        /// <remarks>
        /// The job goes over a file and not over the command line: a bundle
        /// with a hundred PreKeys blows every line length, and base64 in
        /// quotation marks across two operating system borders is a source of
        /// errors nobody needs.
        ///
        /// Where the far side lives, and how a path reaches it, is
        /// <see cref="ForeignSide"/>'s business. This fixture carried its own
        /// copy of that split until D107 - the third of them, which is what
        /// <see cref="TestEnvironment"/> had said would be one too many. What
        /// kept it here longest was not the split itself but
        /// <see cref="PathOver"/> below: the state file of
        /// <see cref="TheReferenceCanReadWhatWeWrote"/> travels *inside* the
        /// job, as a value in JSON, where an argument list cannot help it and
        /// only a translated path will do.
        ///
        /// A missing interpreter comes back as a non-zero code rather than as
        /// an exception, and that matters here beyond tidiness: what leaves
        /// <c>[OneTimeSetUp]</c> as an exception turns the whole fixture red,
        /// and on Linux - where `wsl` is no program at all - that used to mean
        /// three failures where three skips belonged. That guarantee now sits
        /// in <see cref="ForeignSide.Run"/>, where the other callers get it too.
        /// </remarks>
        private static (Int32 Code, String Output, String Errors) Call(String   mode,
                                                                       Object?  job,
                                                                       Boolean  check = true)
        {

            var script = WhereTheOracleLies();

            // No Assert.Ignore: the oracle lies in this project. If it is
            // missing, the checkout is broken - and a broken checkout is
            // supposed to be red and not skipped. What is skipped is only the
            // case where python-omemo is missing.
            Assert.That(script, Is.Not.Null,
                        $"The oracle is not to be found: '{ScriptPath}' lies neither next to " +
                        $"'{AppContext.BaseDirectory}' nor in any directory above it.");

            String? jobFile = null;

            try
            {

                if (job is not null)
                {
                    jobFile = Path.Combine(Path.GetTempPath(), $"orakel-{Guid.NewGuid():N}.json");
                    File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
                }

                var command = $"PYTHONPATH={LibPath} python3 '{PathOver(script!)}'" +
                              $" {mode}" +
                              (jobFile is not null ? $" '{PathOver(jobFile)}'" : "");

                var (code, output, errors) = ForeignSide.Run(command);

                if (check && code != 0)
                    Assert.Fail($"The oracle failed in mode '{mode}':\n{errors}");

                return (code, output, errors);

            }
            finally
            {
                if (jobFile is not null)
                    try { File.Delete(jobFile); } catch { /* does not matter */ }
            }

        }

        /// <summary>
        /// A path as the oracle sees it.
        /// </summary>
        /// <remarks>
        /// <see cref="ForeignSide.PathOver"/> under a name that says what it is
        /// for here. It is kept as a name of its own because the jobs use it in
        /// a place a reader does not expect: not only for the arguments, but
        /// for the state file that travels as a value inside the JSON of
        /// <see cref="TheReferenceCanReadWhatWeWrote"/>, where no argument list
        /// can translate anything.
        /// </remarks>
        private static String PathOver(String path)
            => ForeignSide.PathOver(path);

        /// <summary>
        /// The oracle's bundle out of its answer - with the signature checked
        /// before anything is built on it.
        /// </summary>
        /// <remarks>
        /// The check belongs here and not in the tests: recomputing the
        /// reference's signature is the first thing every one of them depends
        /// on, and a bundle that fails it would make everything after it a
        /// measurement of the wrong thing.
        /// </remarks>
        private static OmemoBundle BundleFrom(JsonElement b)
        {

            var bundle = new OmemoBundle(
                             Convert.FromBase64String(b.GetProperty("identity_key").GetString()!),
                             b.GetProperty("signed_pre_key_id").GetUInt32(),
                             Convert.FromBase64String(b.GetProperty("signed_pre_key").GetString()!),
                             Convert.FromBase64String(b.GetProperty("signed_pre_key_sig").GetString()!),
                             [.. b.GetProperty("pre_keys").EnumerateArray()
                                  .Select(p => new OmemoPreKey(
                                                   p.GetProperty("id").GetUInt32(),
                                                   Convert.FromBase64String(p.GetProperty("key").GetString()!)))]);

            Assert.That(bundle.SignatureIsValid(), Is.True,
                        "We consider the signature of the reference implementation invalid - " +
                        "then we check over something other than what it signs.");

            return bundle;

        }

        /// <summary>
        /// Hands one message to the oracle and returns what it read.
        /// </summary>
        /// <remarks>
        /// The three stateful tests differ in which messages they hand over and
        /// in which order, and in nothing else. Written out each time, that
        /// order - which is the entire subject - would be buried in six
        /// identical dictionaries.
        /// </remarks>
        private static String? Hand(String          mode,
                                    String          state,
                                    OmemoIdentity   own,
                                    String          key,
                                    Byte[]          payload)
        {

            var (code, output, errors) = Call(mode,
                                              new Dictionary<String, Object> {
                                                  ["state"]             = PathOver(state),
                                                  ["key"]               = key,
                                                  ["payload"]           = B64(payload),
                                                  ["sender_jid"]        = TheirViewOfUs,
                                                  ["sender_device_id"]  = (Int32) own.DeviceId
                                              },
                                              check: false);

            Assert.That(code, Is.EqualTo(0),
                        $"The oracle could not read this message in mode '{mode}':\n{errors}");

            return Reply(output).GetProperty("plaintext").GetString();

        }

        private static JsonElement Reply(String output)
            => JsonDocument.Parse(output.Trim()).RootElement;

        private static String B64(Byte[] data)
            => Convert.ToBase64String(data);

        #endregion

        #region Our bundle as a job

        /// <summary>
        /// Our bundle in the shape the oracle expects.
        /// </summary>
        private static Object AsJob(OmemoIdentity own, String jid, String? plaintext = null)
        {

            var bundle = own.Bundle();

            return new Dictionary<String, Object?> {
                ["jid"]                 = jid,
                ["device_id"]           = own.DeviceId,
                ["identity_key"]        = B64(bundle.IdentityKey),
                ["signed_pre_key_id"]   = bundle.SignedPreKeyId,
                ["signed_pre_key"]      = B64(bundle.SignedPreKey),
                ["signed_pre_key_sig"]  = B64(bundle.SignedPreKeySignature),
                ["pre_keys"]            = bundle.PreKeys
                                                .Take(10)
                                                .Select(p => new Dictionary<String, Object> {
                                                            ["id"]   = p.Id,
                                                            ["key"]  = B64(p.PublicKey)
                                                        })
                                                .ToList(),
                ["plaintext"]           = plaintext
            };

        }

        #endregion


        #region TheReferenceAcceptsOurBundle()

        /// <summary>
        /// The reference implementation takes our bundle - <b>and checks the
        /// signature over the signed PreKey itself while doing so</b>.
        /// </summary>
        /// <remarks>
        /// That was an unchecked assumption from D63, expressly noted as such:
        /// the signed PreKey is signed in Montgomery form. Section 5.3.2 says
        /// only "the signed PreKey signature" and leaves the encoding open.
        /// <b>Here it is decided whether the reading holds</b> - a foreign
        /// library checks the signature with its own idea of what it goes over.
        /// </remarks>
        [Test]
        public void TheReferenceAcceptsOurBundle()
        {

            var own = OmemoIdentity.Create();

            var (code, output, errors) = Call("encrypt",
                                              AsJob(own, "us@example.org", "Sample"),
                                               check: false);

            Assert.That(code, Is.EqualTo(0),
                        "The reference implementation refused our bundle. If there is talk here " +
                        "of an invalid signature, we sign the signed PreKey over something other " +
                        "than what it expects - the unchecked assumption from " +
                        $"D63:\n{errors}");

            Assert.That(Reply(output).GetProperty("key").GetString(), Is.Not.Empty);

        }

        #endregion

        #region WeCanReadWhatTheReferenceWrote()

        /// <summary>
        /// <b>The test this stage exists for:</b> the reference implementation
        /// encrypts, we decrypt.
        /// </summary>
        /// <remarks>
        /// What is checked here all at once, and that against foreign code: the
        /// encoding of the bundle, the order of the four Diffie-Hellmans, the
        /// info string of X3DH, the 0xFF prefix, the addition out of both
        /// identity keys, the beginning of the ratchet, the info strings of the
        /// root chain and of the message key, the constants 0x01/0x02, the
        /// protobuf field numbers, the embedding of the ciphertext into the
        /// message, the truncation of the HMAC and the derivation of the
        /// payload.
        ///
        /// <b>Every single one of these points was a surviving mutation or a
        /// find while reading in D62 to D65.</b> This one test would have found
        /// them all.
        /// </remarks>
        [Test]
        public void WeCanReadWhatTheReferenceWrote()
        {

            const String secret = "Written by the reference implementation";

            var own  = OmemoIdentity.Create();
            var jid  = "us@example.org";

            var (_, output, _) = Call("encrypt", AsJob(own, jid, secret));
            var reply         = Reply(output);

            // What is checked is on the layer the oracle covers: from the key
            // exchange to the payload. The SCE envelope stays out of it -
            // python-omemo leaves it to the application, and an envelope I
            // would build in the oracle myself would be no foreign check but
            // the same assumption twice.
            var exchange = OmemoKeyExchange.Decode(
                               Convert.FromBase64String(reply.GetProperty("key").GetString()!));

            var x3dh = X3DH.Accept(own,
                                   exchange.IdentityKey,
                                   exchange.EphemeralKey,
                                   exchange.SignedPreKeyId,
                                   exchange.PreKeyId == 0 ? null : exchange.PreKeyId);

            var ratchet = DoubleRatchet.InitiateAsReceiver(x3dh.SharedSecret, own.SignedPreKey);

            var keyAndHmac = ratchet.Decrypt(
                                 OmemoWireFormat.Decode(exchange.Message),
                                        x3dh.AssociatedData);

            var plaintext = OmemoPayloadCipher.Decrypt(
                                Convert.FromBase64String(reply.GetProperty("payload").GetString()!),
                               keyAndHmac);

            Assert.That(Encoding.UTF8.GetString(plaintext), Is.EqualTo(secret),
                        "What the reference implementation wrote, we could not read.");

        }

        #endregion

        #region TheReferenceCanReadWhatWeWrote()

        /// <summary>
        /// The reverse direction: <b>we</b> encrypt, the reference
        /// implementation reads.
        /// </summary>
        /// <remarks>
        /// <b>That is the direction deciding whether anybody can read us.</b>
        /// The forward direction checks whether we understand foreign messages;
        /// only this one checks whether ours are understood - and that is the
        /// question a client fails at without anybody noticing: whoever never
        /// gets an answer does not know whether nobody wanted to write or
        /// nobody could read.
        ///
        /// What is checked on top of that is our encoding of the key exchange:
        /// the library separates both parts out of our
        /// <c>&lt;key kex='true'/&gt;</c> - the exchange and the packed-in
        /// message. If that succeeds, our field numbers hold.
        /// </remarks>
        [Test]
        public void TheReferenceCanReadWhatWeWrote()
        {

            const String secret = "Written by us, read by the reference";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                // 1. The oracle hands its bundle out - and remembers its keys
                //    in a file, otherwise the second call would be a different
                //    device.
                var (_, bundleOutput, _) = Call("bundle", new Dictionary<String, Object> {
                                                              ["state"] = PathOver(state)
                                                           });

                var b = Reply(bundleOutput);

                var bundle = new OmemoBundle(
                                 Convert.FromBase64String(b.GetProperty("identity_key").GetString()!),
                                 b.GetProperty("signed_pre_key_id").GetUInt32(),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key").GetString()!),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key_sig").GetString()!),
                                 [.. b.GetProperty("pre_keys").EnumerateArray()
                                      .Select(p => new OmemoPreKey(
                                                       p.GetProperty("id").GetUInt32(),
                                                       Convert.FromBase64String(p.GetProperty("key").GetString()!)))]);

                // And a check falls due here already: we recalculate the
                // signature of the reference.
                Assert.That(bundle.SignatureIsValid(), Is.True,
                            "We consider the signature of the reference implementation invalid - " +
                            "then we check over something other than what it signs.");

                // 2. We encrypt against it.
                var own      = OmemoIdentity.Create();
                var x3dh     = X3DH.Initiate(own, bundle);
                var ratchet  = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
                var payload  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(secret));
                var content  = ratchet.Encrypt(payload.KeyAndHmac, x3dh.AssociatedData);

                var exchange = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                    bundle.SignedPreKeyId,
                                                     own.PublicIdentityKey,
                                                     x3dh.EphemeralKey!,
                                                     OmemoWireFormat.Encode(content));

                // 3. The oracle reads.
                var (code, output, errors) = Call("decrypt",
                                                  new Dictionary<String, Object> {
                                                       ["state"]             = PathOver(state),
                                                       ["key"]               = B64(exchange.Encode()),
                                                       ["payload"]           = B64(payload.Ciphertext),
                                                       ["sender_jid"]        = "us@example.org",
                                                       ["sender_device_id"]  = (Int32) own.DeviceId
                                                   },
                                                   check: false);

                Assert.That(code, Is.EqualTo(0),
                            $"The reference implementation could not read our message:\n{errors}");

                Assert.That(Reply(output).GetProperty("plaintext").GetString(), Is.EqualTo(secret));

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region TheReferenceReadsASecondMessageInTheSameSession()

        /// <summary>
        /// <b>The first check here in which the ratchet is a ratchet.</b> Two
        /// messages in a row into one session; the reference has to read both.
        /// </summary>
        /// <remarks>
        /// Everything against this oracle until now was message number one of
        /// a session, and that is the one place where the Double Ratchet has
        /// not yet done anything a plain key agreement would not also have
        /// done: the first message key comes straight out of the chain the
        /// X3DH secret starts. Agreement there says nothing about whether the
        /// two sides step the chain on the same way afterwards.
        ///
        /// From the second message onwards they have to. The chain key is
        /// advanced with its own constant, the message key is derived from the
        /// new one, and the counter in the header says which it is. A client
        /// that gets any of that wrong sends a first message everybody can
        /// read and a second nobody can - which is the shape of interop defect
        /// that is hardest to notice from the inside, because the session
        /// looks established and the handshake looked fine.
        ///
        /// The reverse direction stays out of this on purpose. It would want
        /// the oracle to encrypt twice against a session it keeps, and that is
        /// a second mode; this one is the half that decides whether anybody
        /// can go on reading us.
        /// </remarks>
        [Test]
        public void TheReferenceReadsASecondMessageInTheSameSession()
        {

            const String first   = "The first message, which builds the session";
            const String second  = "The second one, which only a stepped chain can read";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                var (own, bundle, x3dh, ratchet) = SessionAgainstTheOracle(state);

                // 1. The first message, with the key exchange in front of it.
                var payload1  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(first));
                var content1  = ratchet.Encrypt(payload1.KeyAndHmac, x3dh.AssociatedData);

                var exchange  = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                     bundle.SignedPreKeyId,
                                                     own.PublicIdentityKey,
                                                     x3dh.EphemeralKey!,
                                                     OmemoWireFormat.Encode(content1));

                var (code1, output1, errors1) = Call("decrypt",
                                                     new Dictionary<String, Object> {
                                                         ["state"]             = PathOver(state),
                                                         ["key"]               = B64(exchange.Encode()),
                                                         ["payload"]           = B64(payload1.Ciphertext),
                                                         ["sender_jid"]        = TheirViewOfUs,
                                                         ["sender_device_id"]  = (Int32) own.DeviceId
                                                     },
                                                     check: false);

                Assert.That(code1, Is.EqualTo(0),
                            $"The reference could not read the first message:\n{errors1}");

                Assert.That(Reply(output1).GetProperty("plaintext").GetString(),
                            Is.EqualTo(first));

                // 2. The second, in the session that now stands - no key
                //    exchange, only the message. This is the one the chain has
                //    to have been stepped for.
                var payload2  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(second));
                var content2  = ratchet.Encrypt(payload2.KeyAndHmac, x3dh.AssociatedData);

                var (code2, output2, errors2) = Call("continue",
                                                     new Dictionary<String, Object> {
                                                         ["state"]             = PathOver(state),
                                                         ["key"]               = B64(OmemoWireFormat.Encode(content2)),
                                                         ["payload"]           = B64(payload2.Ciphertext),
                                                         ["sender_jid"]        = TheirViewOfUs,
                                                         ["sender_device_id"]  = (Int32) own.DeviceId
                                                     },
                                                     check: false);

                Assert.That(code2, Is.EqualTo(0),
                            "The reference read our first message and not our second. The session " +
                            "stands, so what differs is the step: the chain key, the message key " +
                            "derived from it, or the counter in the header that says which one " +
                            $"this is.\n{errors2}");

                Assert.That(Reply(output2).GetProperty("plaintext").GetString(),
                            Is.EqualTo(second),
                            "The reference decrypted our second message into something else - the " +
                            "chains have gone apart rather than stopped.");

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region AUsedPreKeyIsNotHandedOutASecondTime()

        /// <summary>
        /// The one-time prekeys are used up, and a replay does not use one up
        /// again.
        /// </summary>
        /// <remarks>
        /// X3DH promises forward secrecy for the very first message through
        /// exactly one thing: the one-time prekey is used once and then gone.
        /// A responder that leaves it lying has not consumed it - the same
        /// intercepted message builds a second session just as well, and that
        /// promise is worth nothing.
        ///
        /// <b>Our own tests cannot get at this and it is worth being precise
        /// about why.</b> Both halves would be ours: we would choose a prekey,
        /// we would book it out, and we would agree with ourselves that the
        /// right one went. What is being checked here is that the id we put on
        /// the wire finds <i>the same key</i> in a store nobody here wrote -
        /// and the count is what shows it, because a wrong id would not raise
        /// an error, it would take a different key and the arithmetic would
        /// still come out.
        ///
        /// Three observations, and the third is the one that carries:
        ///
        ///   two sessions on named prekeys   both book out, the visible count
        ///                                   falls by exactly one between them
        ///   the same message a second time  refused outright, naming our id
        ///
        /// The third was written here as a weaker expectation first - that the
        /// replay would still decrypt and merely be reported as already
        /// consumed - because <c>hide_pre_key</c> promises to "keep the pre key
        /// for cryptographic operations". Measuring it said otherwise, and the
        /// reason is worth having: <c>build_session_passive</c> resolves the id
        /// against the <i>visible</i> prekeys, and hiding takes it out of
        /// exactly that set. So the replay does not get as far as decryption:
        ///
        ///     KeyExchangeFailed: No pre key with id 1 known.
        ///
        /// Which makes this a chain of three facts rather than one, and none of
        /// them ours to decide: our id reaches their store and finds a key at
        /// all; their bookkeeping ties that key to the session it opened, or
        /// hiding would have missed it; and once hidden, the same id resolves
        /// to nothing. Send a stale or invented id and the first link fails;
        /// tie the wrong key to the session and the third does.
        ///
        /// Note what the oracle is doing to earn that: it consumes the prekey
        /// because a correct responder must, and this fixture makes it do so
        /// rather than assuming it. What is under test is our half - that the
        /// key exchange we put on the wire takes part in that lifecycle the way
        /// the far side expects.
        /// </remarks>
        [Test]
        public void AUsedPreKeyIsNotHandedOutASecondTime()
        {

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                var (_, bundle, _, _) = SessionAgainstTheOracle(state, buildSession: false);

                Assert.That(bundle.PreKeys.Count, Is.GreaterThanOrEqualTo(2),
                            "The oracle handed out fewer than two one-time prekeys, so nothing " +
                            "about using them up can be measured here.");

                var firstId   = bundle.PreKeys[0].Id;
                var secondId  = bundle.PreKeys[1].Id;

                var (firstJob,  firstReply)  = SessionOn(bundle, state, firstId,  "One");
                var (secondJob, secondReply) = SessionOn(bundle, state, secondId, "Two");

                Assert.That(firstReply.GetProperty("pre_key_was_consumed").GetBoolean(), Is.True,
                            "The reference did not book out the prekey our first session names.");

                Assert.That(secondReply.GetProperty("pre_key_was_consumed").GetBoolean(), Is.True,
                            "The reference did not book out the prekey our second session names.");

                var afterOne  = firstReply.GetProperty("visible_pre_keys").GetInt32();
                var afterTwo  = secondReply.GetProperty("visible_pre_keys").GetInt32();

                Assert.That(afterTwo, Is.EqualTo(afterOne - 1),
                            $"Two sessions on two different prekeys ({firstId} and {secondId}), and " +
                            $"the number of usable ones fell by {afterOne - afterTwo} rather than by " +
                            "one. Either both sessions reached the same key over there - then our " +
                            "prekey id does not mean what we think - or one of them used none at all.");

                // The replay: the first message again, byte for byte.
                var (replayCode, _, replayErrors) = Call("decrypt", firstJob, check: false);

                Assert.That(replayCode, Is.Not.EqualTo(0),
                            "The reference built a second session out of the same message. Then the " +
                            "one-time prekey was not used up by the first, and what X3DH promises " +
                            "for that message - that intercepting it buys nothing later - does not " +
                            "hold.");

                Assert.That(replayErrors, Does.Contain($"No pre key with id {firstId}"),
                            "The replay was refused, but not for the reason this test is about. " +
                            "Expected the prekey our first session named to be gone; what came back " +
                            $"instead was:\n{replayErrors}");

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region TheReferenceReadsMessagesThatArriveOutOfOrder()

        /// <summary>
        /// Three messages, handed over as 1, 3, 2. The reference has to set the
        /// key for the one that is missing aside and still have it when it
        /// arrives.
        /// </summary>
        /// <remarks>
        /// XMPP does not reorder, but everything around it does: a message held
        /// by carbons, a resumed stream that replays what was in flight, two
        /// devices answering at once. The Double Ratchet is built for it - a
        /// message that is ahead makes the chain step forward and the keys
        /// stepped over are kept - and that machinery is precisely where two
        /// implementations can agree on every single message and still not agree
        /// on the pair.
        ///
        /// What this can find and the second-message test cannot: the counter in
        /// the header is not only <i>a</i> number that goes up, it has to be the
        /// number the far side counts keys by. A client whose counter runs on
        /// its own scale reads every message that arrives in order and none that
        /// does not.
        /// </remarks>
        [Test]
        public void TheReferenceReadsMessagesThatArriveOutOfOrder()
        {

            const String one    = "One, which opens the session";
            const String two    = "Two, which is going to arrive last";
            const String three  = "Three, which overtakes it";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                var (own, bundle, x3dh, ratchet) = SessionAgainstTheOracle(state);

                var first   = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(one));
                var c1      = ratchet.Encrypt(first.KeyAndHmac, x3dh.AssociatedData);

                var second  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(two));
                var c2      = ratchet.Encrypt(second.KeyAndHmac, x3dh.AssociatedData);

                var third   = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(three));
                var c3      = ratchet.Encrypt(third.KeyAndHmac, x3dh.AssociatedData);

                var exchange = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                    bundle.SignedPreKeyId,
                                                    own.PublicIdentityKey,
                                                    x3dh.EphemeralKey!,
                                                    OmemoWireFormat.Encode(c1));

                // The first has to go first - it carries the key exchange, and
                // without it there is no session for the others to be early or
                // late in.
                Assert.That(Hand("decrypt", state, own, B64(exchange.Encode()), first.Ciphertext),
                            Is.EqualTo(one));

                // Then the third, over the second's head.
                Assert.That(Hand("continue", state, own, B64(OmemoWireFormat.Encode(c3)), third.Ciphertext),
                            Is.EqualTo(three),
                            "The reference could not read a message that arrived early. Either the " +
                            "counter in our header does not mean what it counts by, or the step it " +
                            "was asked to jump is not the step it makes.");

                // And now the one that was stepped over. Its key was set aside
                // three messages ago; if it was not, this is where that shows.
                Assert.That(Hand("continue", state, own, B64(OmemoWireFormat.Encode(c2)), second.Ciphertext),
                            Is.EqualTo(two),
                            "The reference read the message that overtook and then not the one it " +
                            "overtook. The key stepped over was not kept - or was kept under a " +
                            "number our header does not name.");

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region BothSidesOpeningAtOnceLeavesBothMessagesReadable()

        /// <summary>
        /// Both sides start a session before either has seen the other's - and
        /// both messages stay readable.
        /// </summary>
        /// <remarks>
        /// <b>The oldest interop defect in OMEMO, and the one a user describes
        /// as "he can see me but I cannot see him".</b> Two clients write to
        /// each other at almost the same moment; each fetches the other's
        /// bundle, each opens a session actively, and each then receives a key
        /// exchange for a device it already has a session with. Whoever
        /// discards the incoming one because "there is a session already"
        /// keeps a chain the other side has thrown away.
        ///
        /// Here the oracle opens the first one, into a state it keeps, and our
        /// key exchange arrives afterwards at a device that is already talking
        /// to us. The check is in both directions, because the failure is
        /// one-sided by nature: the message from over there is decrypted
        /// locally out of the session <i>it</i> opened, and ours is handed to
        /// the reference in the session <i>we</i> opened.
        ///
        /// Which session either side keeps afterwards is deliberately not
        /// asserted. The specification lets that be decided per implementation,
        /// and what a user notices is only whether both messages arrived.
        /// </remarks>
        [Test]
        public void BothSidesOpeningAtOnceLeavesBothMessagesReadable()
        {

            const String theirs  = "Written by them, before ours arrived";
            const String ours    = "Written by us, before theirs arrived";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                // The oracle's bundle first, so that our half can be built
                // against the same device the other half comes from.
                var (own, bundle, _, _) = SessionAgainstTheOracle(state, buildSession: false);

                // They open one to us - actively, against our bundle, and into
                // the state they keep.
                var theirJob = new Dictionary<String, Object?>(
                                   (Dictionary<String, Object?>) AsJob(own, TheirViewOfUs, theirs)) {
                                   ["state"] = PathOver(state)
                               };

                var (_, theirOutput, _) = Call("encrypt", theirJob);
                var fromThem            = Reply(theirOutput);

                // We open one to them, knowing nothing of theirs.
                var x3dh     = X3DH.Initiate(own, bundle);
                var ratchet  = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
                var payload  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(ours));
                var content  = ratchet.Encrypt(payload.KeyAndHmac, x3dh.AssociatedData);

                var exchange = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                    bundle.SignedPreKeyId,
                                                    own.PublicIdentityKey,
                                                    x3dh.EphemeralKey!,
                                                    OmemoWireFormat.Encode(content));

                // Their side: a key exchange from a device it is already
                // talking to.
                Assert.That(Hand("decrypt", state, own, B64(exchange.Encode()), payload.Ciphertext),
                            Is.EqualTo(ours),
                            "The reference refused our key exchange because it already had a session " +
                            "with us. Then two clients that write at the same moment end up with one " +
                            "of them talking into a chain the other has dropped.");

                // Our side: theirs, out of the session they opened. Nothing of
                // ours has been thrown away for it.
                var theirExchange = OmemoKeyExchange.Decode(
                                        Convert.FromBase64String(fromThem.GetProperty("key").GetString()!));

                var accepted = X3DH.Accept(own,
                                           theirExchange.IdentityKey,
                                           theirExchange.EphemeralKey,
                                           theirExchange.SignedPreKeyId,
                                           theirExchange.PreKeyId == 0 ? null : theirExchange.PreKeyId);

                var theirRatchet = DoubleRatchet.InitiateAsReceiver(accepted.SharedSecret, own.SignedPreKey);

                var keyAndHmac = theirRatchet.Decrypt(OmemoWireFormat.Decode(theirExchange.Message),
                                                      accepted.AssociatedData);

                var plaintext = OmemoPayloadCipher.Decrypt(
                                    Convert.FromBase64String(fromThem.GetProperty("payload").GetString()!),
                                    keyAndHmac);

                Assert.That(Encoding.UTF8.GetString(plaintext), Is.EqualTo(theirs),
                            "We could not read the session they opened while we were opening ours.");

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region AnEmptyKeyTransportOpensASessionWeCanAnswerIn()

        /// <summary>
        /// A message with a key exchange and <b>no payload at all</b> - and an
        /// answer in the session it opened.
        /// </summary>
        /// <remarks>
        /// This is not an edge case, it is how most sessions in a real
        /// conversation actually begin. A client that has restarted, or has just
        /// seen a new device, sends a key transport element: everything a
        /// session needs and nothing to read. XEP-0384 leaves the
        /// <c>&lt;payload/&gt;</c> away entirely for it, and
        /// <see cref="OmemoEncryptedElement"/> has said since it was written
        /// that a message without one is no error.
        ///
        /// <b>What is checked is that this holds against a foreign sender.</b>
        /// Our own tests build the empty case the way we would build it; the
        /// oracle builds it the way the reference does, with
        /// <c>encrypt_empty</c>, and the ciphertext it reports is empty rather
        /// than a zero-length encryption of nothing.
        ///
        /// The answer afterwards is the half that makes it worth a test rather
        /// than a look. A key transport that is accepted and leaves the ratchet
        /// in the wrong place is worse than one that is refused: the session
        /// looks established, and the first real sentence in either direction is
        /// the one that disappears.
        /// </remarks>
        [Test]
        public void AnEmptyKeyTransportOpensASessionWeCanAnswerIn()
        {

            const String answer = "Answered in the session their empty message opened";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                var own = OmemoIdentity.Create();

                var emptyJob = new Dictionary<String, Object?>(
                                   (Dictionary<String, Object?>) AsJob(own, TheirViewOfUs)) {
                                   ["state"] = PathOver(state)
                               };

                var (_, output, _) = Call("empty", emptyJob);
                var fromThem       = Reply(output);

                Assert.That(fromThem.GetProperty("empty").GetBoolean(), Is.True,
                            "The reference does not consider its own key transport element empty, so " +
                            "what follows would measure something else.");

                Assert.That(fromThem.GetProperty("payload").GetString(), Is.Empty,
                            "The key transport carries a payload. XEP-0384 leaves the <payload/> away " +
                            "for these, and an empty encryption of nothing is not the same thing on " +
                            "the wire.");

                // We accept it: everything a session needs, nothing to read.
                var theirExchange = OmemoKeyExchange.Decode(
                                        Convert.FromBase64String(fromThem.GetProperty("key").GetString()!));

                var accepted = X3DH.Accept(own,
                                           theirExchange.IdentityKey,
                                           theirExchange.EphemeralKey,
                                           theirExchange.SignedPreKeyId,
                                           theirExchange.PreKeyId == 0 ? null : theirExchange.PreKeyId);

                var ratchet = DoubleRatchet.InitiateAsReceiver(accepted.SharedSecret, own.SignedPreKey);

                Assert.That(() => ratchet.Decrypt(OmemoWireFormat.Decode(theirExchange.Message),
                                                  accepted.AssociatedData),
                            Throws.Nothing,
                            "We could not take the key out of a message that carries nothing but the " +
                            "key - which is the message most sessions actually start with.");

                // And the half that decides whether the session is usable: we
                // answer in it, and they read the answer.
                var payload  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(answer));
                var content  = ratchet.Encrypt(payload.KeyAndHmac, accepted.AssociatedData);

                Assert.That(Hand("continue", state, own, B64(OmemoWireFormat.Encode(content)), payload.Ciphertext),
                            Is.EqualTo(answer),
                            "The key transport was accepted and the session it opened does not carry. " +
                            "That is the worse of the two failures: nothing looks wrong until the " +
                            "first real sentence goes missing.");

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region OneMessageReachesEveryDeviceAndNamesTheOneItCannot()

        /// <summary>
        /// One message, four devices of the far side: three that can read it,
        /// each out of its own key entry, and one that is named rather than
        /// passed over in silence.
        /// </summary>
        /// <remarks>
        /// <b>This is the first check of <c>OmemoManager.EncryptAsync</c>
        /// against anything foreign, and the first of any kind for the list of
        /// devices it could not reach.</b> Everything before it went through the
        /// primitives - X3DH, the ratchet, the payload cipher - and built the
        /// fan-out by hand, one session at a time. What the manager adds is the
        /// part that has no counterpart in a test written by hand: one payload,
        /// several key entries, and the bookkeeping of which entry belongs to
        /// which device.
        ///
        /// Three things are asked, and the second is the one only a far side can
        /// answer:
        ///
        /// <b>Every device reads the same text.</b> One `OmemoPayloadCipher`
        /// payload, encrypted once; the key that opens it goes to each device
        /// separately through its own session. If that bookkeeping slips, this
        /// does not fail quietly in a corner - some devices of a contact stop
        /// receiving while others carry on, which is the shape a user reports as
        /// "it works on my phone".
        ///
        /// <b>The entries are not interchangeable.</b> Device 2 is handed the
        /// entry meant for device 3, and has to refuse it. Without that check a
        /// suite that gave every device the same entry would pass all three
        /// decryptions - in the one case where it happens to be wrong.
        ///
        /// <b>The device that could not be reached is named.</b> The fourth
        /// stands in the list and has no bundle, and `Skipped` has to carry it
        /// with a reason. The record's own remark says why this is not cosmetic:
        /// a sender who does not learn that three of four devices cannot read
        /// along takes the conversation for held and wonders about the answer
        /// that does not come. Since `ca8bce3` this is what `SendEncryptedMessageAsync`
        /// returns, and nothing outside this house had ever looked at it.
        ///
        /// The plaintext that comes back is the XEP-0420 envelope, not the bare
        /// sentence - the manager wraps content before encrypting, and the
        /// reference hands out what it decrypted without opinion. That the
        /// envelope arrives intact on a foreign side is worth as much as the
        /// text inside it.
        /// </remarks>
        [Test]
        public async Task OneMessageReachesEveryDeviceAndNamesTheOneItCannot()
        {

            const String secret       = "One message, three devices, one that cannot";
            const UInt32 unreachable  = 4;

            var oracleJid  = JID.Parse("oracle@example.org");
            var ourJid     = JID.Parse(TheirViewOfUs);

            var states     = new Dictionary<UInt32, String>();
            var bundles    = new Dictionary<UInt32, OmemoBundle>();

            try
            {

                // Three devices of the far side. Their own state file each is
                // what makes them different devices rather than one device
                // wearing three numbers.
                foreach (var id in new UInt32[] { 1, 2, 3 })
                {

                    var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");
                    states[id] = state;

                    var (_, output, _) = Call("bundle", new Dictionary<String, Object> {
                                                            ["state"]      = PathOver(state),
                                                            ["jid"]        = oracleJid.ToString(),
                                                            ["device_id"]  = (Int32) id
                                                        });

                    bundles[id] = BundleFrom(Reply(output));

                }

                var manager = new OmemoManager(
                                  new OmemoMemoryStore(),
                                  ourJid,
                                  fetchDeviceList: jid => Task.FromResult<OmemoDeviceList?>(
                                      jid == oracleJid
                                          ? new OmemoDeviceList([new OmemoDevice(1), new OmemoDevice(2),
                                                                 new OmemoDevice(3), new OmemoDevice(unreachable)])
                                          // Our own bare JID is appended by EncryptAsync itself, so that
                                          // one's own other devices see what one has written. We have none,
                                          // and an empty list says so without becoming a skip.
                                          : new OmemoDeviceList([])),
                                  fetchBundle: (jid, id) => Task.FromResult(
                                      bundles.TryGetValue(id, out var bundle) ? bundle : null));

                var result = await manager.EncryptAsync([oracleJid],
                                                        [new XElement("body", secret)]);

                // 1. The one that could not be reached is named, and named with
                //    its number rather than as a count.
                Assert.That(result.Skipped.Select(s => s.DeviceId), Is.EquivalentTo(new[] { unreachable }),
                            "The device without a bundle is either missing from the list of those that " +
                            "cannot read along, or others have wrongly landed in it. A sender is told " +
                            "this and nothing else about who did not get the message.");

                Assert.That(result.Skipped[0].Reason, Is.Not.Empty,
                            "The device is named without a reason, which is a list a sender cannot act on.");

                // 2. Three entries, one payload.
                var keys = result.Element.Keys[oracleJid];

                Assert.That(keys.Select(k => k.DeviceId), Is.EquivalentTo(new UInt32[] { 1, 2, 3 }));
                Assert.That(result.Element.Payload, Is.Not.Null,
                            "A message with content and no payload - then there is nothing for the " +
                            "keys to open.");

                // 3. Each device out of its own entry, and all of them the same
                //    text.
                foreach (var id in new UInt32[] { 1, 2, 3 })
                {

                    var entry = keys.Single(k => k.DeviceId == id);

                    Assert.That(entry.IsKeyExchange, Is.True,
                                $"The entry for device {id} is not a key exchange, although no session " +
                                "with it exists yet.");

                    var read = Hand("decrypt", states[id], manager.Identity,
                                    B64(entry.Data), result.Element.Payload!);

                    Assert.That(read, Does.Contain(secret),
                                $"Device {id} did not get the text out of the message, although its own " +
                                "key entry was handed to it. One payload for all, one key each - and " +
                                "this is where that comes apart.");

                }

                // 4. And the entries are not interchangeable: device 2 gets the
                //    one meant for 3.
                var wrongState  = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");
                var (_, o2, _)  = Call("bundle", new Dictionary<String, Object> {
                                                     ["state"]      = PathOver(wrongState),
                                                     ["jid"]        = oracleJid.ToString(),
                                                     ["device_id"]  = 2
                                                 });
                states[99]      = wrongState;

                // A device with a fresh state is a fresh device, so what is
                // checked here is the entry and not a session already standing.
                _ = BundleFrom(Reply(o2));

                var (code, _, _) = Call("decrypt",
                                        new Dictionary<String, Object> {
                                            ["state"]             = PathOver(states[2]),
                                            ["key"]               = B64(keys.Single(k => k.DeviceId == 3).Data),
                                            ["payload"]           = B64(result.Element.Payload!),
                                            ["sender_jid"]        = TheirViewOfUs,
                                            ["sender_device_id"]  = (Int32) manager.Identity.DeviceId
                                        },
                                        check: false);

                Assert.That(code, Is.Not.EqualTo(0),
                            "Device 2 opened the entry that was meant for device 3. Then the entries " +
                            "are not bound to the device they name, and the fan-out only appears to " +
                            "be one - every device would be reading the same key.");

            }
            finally
            {
                foreach (var state in states.Values)
                    try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

        #region AForeignMessageNamingAnotherSenderIsRefused()

        /// <summary>
        /// A message the reference implementation really encrypted, carrying an
        /// envelope that names somebody else as its sender - and our side has to
        /// throw it away.
        /// </summary>
        /// <remarks>
        /// <b>This one runs the other way round, and the reason is in the
        /// oracle's own preface:</b> python-omemo *"leaves the SCE envelope to
        /// the application using it"*. There is nobody over there to refuse a
        /// forged affix, so asking the reference to do it would measure a check
        /// that does not exist. What the far side can supply is the other half -
        /// a message that is genuine in every respect but the one under test.
        ///
        /// So the envelope is written here, handed to the oracle as plaintext,
        /// and comes back inside a real key exchange with a real ratchet and a
        /// real payload cipher. <see cref="OmemoManager.DecryptAsync"/> then has
        /// to get all the way through that and still refuse it, because
        /// <c>&lt;envelope/&gt;</c> says <c>mallory@</c> where the stanza says
        /// <c>oracle@</c>.
        ///
        /// <b>What this can find that a unit test cannot.</b> The affix check
        /// itself is ours either way - that much is honest to say. What is not
        /// ours is the path to it: a check that sits in a helper and is skipped
        /// in the real decryption, or is applied to the wrong string of the two
        /// the envelope carries, passes a test that hands the checker a
        /// hand-built element and fails here. XEP-0420 exists for precisely one
        /// attack - the outer sender can be changed by anybody, the inner one
        /// cannot - and a check that is only reached in the easy path defends
        /// against nothing.
        ///
        /// The control half is not decoration. Without a message that must be
        /// *accepted*, a decryption path that refused everything would pass this
        /// test and look like rigour.
        /// </remarks>
        [Test]
        public async Task AForeignMessageNamingAnotherSenderIsRefused()
        {

            const String secret     = "Written by the oracle, sealed in an envelope";

            var oracleJid  = JID.Parse("oracle@example.org");
            var ourJid     = JID.Parse(TheirViewOfUs);
            var mallory    = JID.Parse("mallory@example.org");

            // The honest one first, so that what follows is a comparison and not
            // an assertion standing on its own.
            var accepted = await WhatWeMakeOf(FromTheOracle(oracleJid, ourJid, secret), oracleJid, ourJid);

            Assert.That(accepted, Is.Not.Null,
                        "We threw away a message whose envelope names the sender it came from. Then " +
                        "the refusal below says nothing - a path that refuses everything refuses the " +
                        "forgery too.");

            Assert.That(accepted!.Content.First().Value, Is.EqualTo(secret));

            Assert.That(accepted.EnvelopeFrom, Is.EqualTo(oracleJid),
                        "The sender out of the envelope did not arrive at the caller, so nobody above " +
                        "us could tell the two apart even if they wanted to.");

            // And the same message again, with one name changed inside the seal.
            var forged = await WhatWeMakeOf(FromTheOracle(mallory, ourJid, secret), oracleJid, ourJid);

            Assert.That(forged, Is.Null,
                        "We read a message whose envelope names a sender other than the one it came " +
                        "from. That is the one thing XEP-0420's affix is for: the outer sender can be " +
                        "changed by anybody on the way, the inner one cannot, and a message where the " +
                        "two disagree has been passed on under a foreign name.");

        }

        /// <summary>
        /// An envelope with a sender of our choosing, encrypted by the reference
        /// against our bundle.
        /// </summary>
        private static (JsonElement Reply, OmemoIdentity Own, OmemoManager Manager) FromTheOracle(JID  envelopeFrom,
                                                                                                  JID  ourJid,
                                                                                                  String text)
        {

            // A store and a manager of its own per message: a key exchange uses
            // up a prekey and opens a session, and two of them from what the
            // manager takes for one device would be answered as a changed
            // identity key rather than as the second message.
            var manager = new OmemoManager(
                              new OmemoMemoryStore(),
                              ourJid,
                              fetchDeviceList: _ => Task.FromResult<OmemoDeviceList?>(new OmemoDeviceList([])),
                              fetchBundle:     (_, _) => Task.FromResult<OmemoBundle?>(null));

            var envelope = new SceEnvelope([new XElement("body", text)],
                                           From: envelopeFrom.ToString(),
                                           Time: DateTimeOffset.UtcNow).ToXml();

            var (_, output, _) = Call("encrypt",
                                      AsJob(manager.Identity,
                                            ourJid.ToString(),
                                            envelope.ToString(SaveOptions.DisableFormatting)));

            return (Reply(output), manager.Identity, manager);

        }

        /// <summary>
        /// What our own decryption makes of it - the whole way, not a shortcut
        /// into the envelope check.
        /// </summary>
        private static async Task<OmemoDecrypted?> WhatWeMakeOf((JsonElement Reply, OmemoIdentity Own, OmemoManager Manager) from,
                                                                JID  senderJid,
                                                                JID  ourJid)
        {

            var element = new OmemoEncryptedElement(
                              from.Reply.GetProperty("sender_device_id").GetUInt32(),
                              new Dictionary<JID, IReadOnlyList<OmemoKey>> {
                                  [ourJid] = [new OmemoKey(
                                                  from.Own.DeviceId,
                                                  Convert.FromBase64String(from.Reply.GetProperty("key").GetString()!),
                                                  IsKeyExchange: true)]
                              },
                              Convert.FromBase64String(from.Reply.GetProperty("payload").GetString()!));

            return await from.Manager.DecryptAsync(element, senderJid);

        }

        #endregion

        #region (private) Building a session against the oracle

        /// <summary>
        /// How the oracle sees us. The two stateful tests hand this to it
        /// twice each, and the second call only finds the first one's session
        /// if the name matches to the letter.
        /// </summary>
        private const String TheirViewOfUs = "us@example.org";

        /// <summary>
        /// Fetches the oracle's bundle into the given state file and - unless
        /// asked not to - opens a session against it.
        /// </summary>
        private static (OmemoIdentity Own,
                        OmemoBundle   Bundle,
                        X3DHResult    X3dh,
                        DoubleRatchet Ratchet) SessionAgainstTheOracle(String   state,
                                                                       Boolean  buildSession = true)
        {

            var (_, bundleOutput, _) = Call("bundle", new Dictionary<String, Object> {
                                                          ["state"] = PathOver(state)
                                                      });

            var bundle = BundleFrom(Reply(bundleOutput));

            var own = OmemoIdentity.Create();

            if (!buildSession)
                return (own, bundle, null!, null!);

            var x3dh     = X3DH.Initiate(own, bundle);
            var ratchet  = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);

            return (own, bundle, x3dh, ratchet);

        }

        /// <summary>
        /// One whole session on a <b>named</b> prekey, and what the oracle
        /// answered - along with the job, so that the very same message can be
        /// handed over a second time.
        /// </summary>
        /// <remarks>
        /// Naming the prekey is what makes the count readable.
        /// <see cref="X3DH.Initiate"/> picks one by itself otherwise, and two
        /// runs could pick the same - a test that then measured nothing would
        /// pass just as quietly as one that measured everything.
        /// </remarks>
        private static (Dictionary<String, Object> Job, JsonElement Reply) SessionOn(OmemoBundle  bundle,
                                                                                     String       state,
                                                                                     UInt32       preKeyId,
                                                                                     String       text)
        {

            var own      = OmemoIdentity.Create();
            var x3dh     = X3DH.Initiate(own, bundle, preKeyId);

            Assert.That(x3dh.UsedPreKeyId, Is.EqualTo(preKeyId),
                        "We asked for a specific prekey and took a different one, so what follows " +
                        "would measure the oracle against the wrong expectation.");

            var ratchet  = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
            var payload  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(text));
            var content  = ratchet.Encrypt(payload.KeyAndHmac, x3dh.AssociatedData);

            var exchange = new OmemoKeyExchange(preKeyId,
                                                bundle.SignedPreKeyId,
                                                own.PublicIdentityKey,
                                                x3dh.EphemeralKey!,
                                                OmemoWireFormat.Encode(content));

            var job = new Dictionary<String, Object> {
                          ["state"]             = PathOver(state),
                          ["key"]               = B64(exchange.Encode()),
                          ["payload"]           = B64(payload.Ciphertext),
                          ["sender_jid"]        = TheirViewOfUs,
                          ["sender_device_id"]  = (Int32) own.DeviceId
                      };

            var (code, output, errors) = Call("decrypt", job, check: false);

            Assert.That(code, Is.EqualTo(0),
                        $"The reference could not read a session on prekey {preKeyId}:\n{errors}");

            return (job, Reply(output));

        }

        #endregion

    }

}
