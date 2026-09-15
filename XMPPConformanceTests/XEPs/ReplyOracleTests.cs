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

using System.Text.Json;
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Message Replies (XEP-0461) against a client library nobody here wrote.
    /// </summary>
    /// <remarks>
    /// This is the fixture the whole extension was waiting for. A reply is
    /// client-to-client: Prosody and ejabberd carry the <c>&lt;reply/&gt;</c>
    /// and the <c>&lt;fallback/&gt;</c> across without looking at either, so
    /// the federation lane can say nothing at all about them. Everything
    /// Ratatoskr checks about replies it checks against itself — and two sides
    /// that are the same code agree even where both are wrong. That was the
    /// finding of D62 to D65, five times over, and it is why XEP-0461 sat in
    /// <i>Optional</i> with "the machinery cannot judge it" written next to it.
    ///
    /// <para>
    /// <b>What is contested is not the reference. It is the number beside
    /// it.</b> XEP-0428 points into the body with offsets counted in Unicode
    /// code points (XEP-0426); .NET counts in UTF-16 units. The two are the
    /// same number for every text anybody writes a test with, and different the
    /// moment somebody quotes a message with an emoji in it. An implementation
    /// that confuses them cuts one character too many off every quotation
    /// containing one — and nothing fails, nothing logs, and the answer simply
    /// arrives with its first letter missing.
    /// </para>
    ///
    /// <para>
    /// Python is the useful opposite: a <c>str</c> is code points by nature, so
    /// <c>len()</c> and slicing give the XEP-0426 answer without anybody having
    /// to decide to make them. slixmpp's <c>xep_0461</c> is built directly on
    /// that — <c>len(quoted)</c> into the attribute, <c>body[start:end]</c> back
    /// out — which makes it a second opinion rather than a second copy of ours.
    /// It comes from the same <c>fetch_oracle.py</c> as python-omemo and costs
    /// one more line in its package list.
    /// </para>
    ///
    /// <para>
    /// <b>What this does not check:</b> the servers, for the reason above, and
    /// the announcement. Whether <c>urn:xmpp:reply:0</c> reaches
    /// <c>disco#info</c> and the caps hash is a question for a real server and
    /// is asked in Ratatoskr, over a connection, in
    /// <c>MessageReplyTests</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Wsl)]
    [Category(TestCategories.Replies)]
    public class ReplyOracleTests
    {

        #region Calling the oracle

        private const           String  LibPath     = "/tmp/omemo-oracle/lib";
        private static readonly String  ScriptPath  = Path.Combine("XEPs", "Oracle", "reply_oracle.py");

        private static String? _reasonForSkipping;

        [OneTimeSetUp]
        public void CheckTheOracle()
        {

            var (code, _, errors) = Call("probe", null, check: false);

            if (code != 0)
                _reasonForSkipping =
                    $"The XEP-0461 oracle is not reachable (slixmpp under {LibPath}" +
                    (ForeignSide.IsHere ? "" : ", in WSL") + "): " +
                    $"{errors.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim()}";

        }

        [SetUp]
        public void SkipIfNeeded()
        {
            if (_reasonForSkipping is not null)
                Assert.Ignore(_reasonForSkipping);
        }

        /// <summary>
        /// Runs the oracle and returns what it said.
        /// </summary>
        /// <remarks>
        /// The script is looked for next to the assembly, where the csproj
        /// copies <c>XEPs/Oracle/**</c>. A missing one is RED and not skipped:
        /// it lies in this project, so its absence is a broken checkout and not
        /// a property of the environment. Only a missing slixmpp skips. That is
        /// D97's rule.
        /// </remarks>
        private static (Int32 Code, String Output, String Errors) Call(String   mode,
                                                                       Object?  job,
                                                                       Boolean  check = true)
        {

            var script = Path.Combine(AppContext.BaseDirectory, ScriptPath);

            Assert.That(File.Exists(script), Is.True,
                        $"The oracle is not to be found: '{script}' does not exist. " +
                        $"The csproj copies XEPs/Oracle/** next to the assembly; if it is " +
                        $"missing, the checkout is broken.");

            String? jobFile = null;

            if (job is not null)
            {
                jobFile = Path.Combine(Path.GetTempPath(), $"reply-{Guid.NewGuid():N}.json");
                File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
            }

            var command = $"PYTHONPATH={LibPath} python3 '{ForeignSide.PathOver(script)}' {mode}" +
                          (jobFile is not null ? $" '{ForeignSide.PathOver(jobFile)}'" : "");

            var (code, output, errors) = ForeignSide.Run(command);

            if (jobFile is not null)
                try { File.Delete(jobFile); } catch { /* does not matter */ }

            if (check && code != 0)
                Assert.Fail($"The oracle failed in mode '{mode}':\n{errors}");

            return (code, output, errors);

        }

        private static JsonElement Ask(String mode, Object job)
        {
            var (_, output, _) = Call(mode, job);
            return JsonDocument.Parse(output.Trim()).RootElement;
        }

        private static String? Text(JsonElement element, String name)
            => element.GetProperty(name).ValueKind == JsonValueKind.Null
                   ? null
                   : element.GetProperty(name).GetString();

        #endregion

        #region Building an answer the way this library builds one

        /// <summary>
        /// A stanza as Ratatoskr sends it — put together by
        /// <see cref="MessageReply.Compose"/> and not by this test.
        /// </summary>
        /// <remarks>
        /// The distinction matters. What a foreign implementation has to be
        /// able to read is what this library really sends; a stanza assembled
        /// here the same way and called equivalent would check the assembling,
        /// which is the half that is not in doubt.
        /// </remarks>
        private static String OurStanza(String   answer,
                                        String?  quoted,
                                        String?  author     = null,
                                        String   replyToId  = "question-1")
        {

            var composed = MessageReply.Compose(answer,
                                                replyToId,
                                                JID.Parse("alice@example.org/home"),
                                                quoted,
                                                author);

            return "<message xmlns='jabber:client' from='bob@example.org/phone' " +
                          "to='alice@example.org/home' id='answer-1' type='chat'>" +
                       $"<body>{XmlEscaping.Escape(composed.Body)}</body>" +
                       composed.Extras +
                   "</message>";

        }

        /// <summary>
        /// What this library makes of a stanza: the answer without the
        /// quotation, and the quotation on its own.
        /// </summary>
        private static (MessageReplyTo? Reply, String Text, String? Quote) OurReading(String stanza)
        {

            var element = XElement.Parse(stanza);
            var body    = element.ChildValue("body") ?? "";
            var range   = MessageReply.QuoteRangeIn(element, body);

            return (MessageReply.RepliesTo(element),
                    range is BodyRange found ? body.Remove(found.Start, found.Length) : body,
                    range is BodyRange taken ? body.Substring(taken.Start, taken.Length) : null);

        }

        /// <summary>
        /// A globe: one code point, two chars in .NET.
        /// </summary>
        private const String Globe = "\U0001F30D";

        #endregion


        #region 1. They read what we write

        /// <summary>
        /// slixmpp takes our answer apart and gets the answer back.
        /// </summary>
        /// <remarks>
        /// <b>The one that would have caught the obvious mistake.</b> Written
        /// with <c>quote.Length</c> straight into the attribute, everything on
        /// this side stays green — and the far side, cutting <c>body[0:13]</c>
        /// out of a string that is twelve characters of quotation, hands back
        /// <c>"es"</c> where <c>"Yes"</c> was sent. That is what a foreign
        /// implementation is for: it is the only place the error becomes
        /// visible instead of merely present.
        /// </remarks>
        [Test]
        public void TheyReadWhatWeWrite()
        {

            var stanza = OurStanza("Yes", $"Sea {Globe} end");
            var read   = Ask("read", new Dictionary<String, Object> { { "stanza", stanza } });

            Assert.Multiple(() =>
            {

                Assert.That(read.GetProperty("reply").GetBoolean(), Is.True,
                            "slixmpp did not find a reply in our stanza at all.");

                Assert.That(Text(read, "reply_id"), Is.EqualTo("question-1"),
                            "The reference did not arrive.");

                Assert.That(Text(read, "reply_to"), Is.EqualTo("alice@example.org/home"),
                            "The author of the answered message did not arrive.");

                Assert.That(read.GetProperty("end").GetInt32(), Is.EqualTo(12),
                            "A foreign implementation reads a different end than the quotation has. " +
                            "13 is what UTF-16 counting gives for this quotation.");

                Assert.That(Text(read, "stripped"), Is.EqualTo("Yes"),
                            "A foreign implementation cut the quotation in the wrong place, which " +
                            "means the offsets we sent named the wrong place.");

                Assert.That(Text(read, "quoted"), Is.EqualTo($"> Sea {Globe} end\n"),
                            "What the far side hides is not what we meant to have hidden.");

            });

        }

        #endregion

        #region 2. We read what they write

        /// <summary>
        /// The other direction: slixmpp writes the answer, we take it apart.
        /// </summary>
        /// <remarks>
        /// Not the mirror image of the first, and worth having beside it. The
        /// first says our numbers mean what we think; this one says we read
        /// somebody else's the same way. An implementation can be right in one
        /// direction and wrong in the other, and a chat client that can send
        /// but not receive is the more annoying half.
        /// </remarks>
        [Test]
        public void WeReadWhatTheyWrite()
        {

            var built = Ask("build", new Dictionary<String, Object> {
                                         { "answer",        "Yes"                     },
                                         { "quoted",        $"Sea {Globe} end"        },
                                         { "reply_to",      "question-1"              },
                                         { "reply_author",  "alice@example.org/home"  }
                                     });

            var (reply, text, quote) = OurReading(Text(built, "stanza")!);

            Assert.Multiple(() =>
            {

                Assert.That(reply?.Id, Is.EqualTo("question-1"),
                            "We did not find the reference in a foreign stanza.");

                Assert.That(reply?.To?.ToString(), Is.EqualTo("alice@example.org/home"));

                Assert.That(text, Is.EqualTo(Text(built, "stripped")),
                            "We and the writer disagree about what is left once the quotation goes.");

                Assert.That(text, Is.EqualTo("Yes"),
                            "The answer did not survive our reading.");

                Assert.That(quote, Is.EqualTo(Text(built, "quoted")),
                            "We and the writer disagree about which part is the quotation.");

            });

        }

        #endregion

        #region 3. The two count the same way

        /// <summary>
        /// Character for character, over the data that tells the countings
        /// apart.
        /// </summary>
        /// <remarks>
        /// The direct comparison, without a stanza in between: how long is this
        /// text. Python answers out of the language; <see cref="CharacterCounting"/>
        /// answers by converting on purpose. For the first two rows every way of
        /// counting agrees, which is precisely why a suite made of rows like
        /// them proves nothing.
        ///
        /// The family is the interesting one. Three people joined by two
        /// zero-width joiners is one thing to look at, five code points, eight
        /// UTF-16 units — so a grapheme-based count and a UTF-16 count are both
        /// wrong here, and wrong by different amounts.
        /// </remarks>
        [Test]
        public void TheTwoCountTheSameWay()
        {

            String[] texts = [
                "",
                "plain ascii",
                "Grüße, äöüß",              // beyond ASCII, still one unit each
                $"Sea {Globe} end",          // one astral character
                $"{Globe}{Globe}{Globe}",    // nothing but astral characters
                "\U0001F468‍\U0001F469‍\U0001F467",  // a family: 5 code points, 8 chars
                "égalité",       // combining accents
                "> quoted\n> lines\n"
            ];

            Assert.Multiple(() =>
            {
                foreach (var text in texts)
                {

                    var theirs = Ask("count", new Dictionary<String, Object> { { "text", text } }).
                                     GetProperty("characters").GetInt32();

                    Assert.That(CharacterCounting.Count(text), Is.EqualTo(theirs),
                                $"The two implementations count '{text.Replace("\n", "\\n")}' " +
                                $"differently: {CharacterCounting.Count(text)} here, {theirs} there. " +
                                $"(.NET's own Length says {text.Length}.)");

                }
            });

        }

        #endregion

        #region 4. A quotation from Windows survives the crossing

        /// <summary>
        /// The carriage return the parser eats, seen from the far side.
        /// </summary>
        /// <remarks>
        /// XML 1.0, section 2.11: <c>CR LF</c> in text content becomes a single
        /// <c>LF</c> before any application sees it. Our side counts after
        /// settling the line endings; if it did not, the offsets would be too
        /// large by one per line — and this is where that shows, because the far
        /// side counts what actually arrived.
        ///
        /// A trap with a bias: it can only be stepped into on a machine that
        /// writes both characters, and the far side of this test is always a
        /// Linux one.
        /// </remarks>
        [Test]
        public void AQuotationFromWindowsSurvivesTheCrossing()
        {

            var stanza = OurStanza("Yes", "first line\r\nsecond line\r\nthird line");
            var read   = Ask("read", new Dictionary<String, Object> { { "stanza", stanza } });

            Assert.Multiple(() =>
            {

                Assert.That(Text(read, "body"), Does.Not.Contain("\r"),
                            "The carriage returns arrived at the far side, so this test is measuring " +
                            "nothing that it claims to measure.");

                Assert.That(Text(read, "stripped"), Is.EqualTo("Yes"),
                            "Three lines, three carriage returns: the answer arrives at the far side " +
                            "with its first three characters eaten.");

            });

        }

        #endregion

        #region 5. The conventions differ and the offsets still agree

        /// <summary>
        /// Two implementations quote differently, and it costs nothing.
        /// </summary>
        /// <remarks>
        /// slixmpp strips each quoted line; this library leaves indentation
        /// alone, because quoted text is somebody else's and tidying it is not
        /// this side's to do. Both are defensible and they produce different
        /// bodies for the same input.
        ///
        /// <b>That is the point.</b> XEP-0461 makes the quotation a convention
        /// and the offsets the protocol. Interoperability is owed by the second
        /// only — and a test where both sides happened to write the same
        /// quotation would never have shown whether that is true.
        /// </remarks>
        [Test]
        public void TheConventionsDifferAndTheOffsetsStillAgree()
        {

            const String quoted = "    indented line\n    and another    ";

            var built = Ask("build", new Dictionary<String, Object> {
                                         { "answer",    "Yes"       },
                                         { "quoted",    quoted      },
                                         { "reply_to",  "question-1" }
                                     });

            var (_, ourReadingOfTheirs, theirQuotation) = OurReading(Text(built, "stanza")!);

            var stanza = OurStanza("Yes", quoted);
            var read   = Ask("read", new Dictionary<String, Object> { { "stanza", stanza } });

            Assert.Multiple(() =>
            {

                Assert.That(theirQuotation, Is.Not.EqualTo(MessageReply.Quote(quoted)),
                            "The two implementations happen to write the same quotation for this " +
                            "input, so the test no longer shows that they need not.");

                Assert.That(ourReadingOfTheirs, Is.EqualTo("Yes"),
                            "We could not take their quotation back out.");

                Assert.That(Text(read, "stripped"), Is.EqualTo("Yes"),
                            "They could not take our quotation back out.");

            });

        }

        #endregion

    }

}
