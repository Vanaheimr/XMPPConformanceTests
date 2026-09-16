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

using System.Collections.Concurrent;
using System.Security.Cryptography;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0084 over XEP-0163 against a real personal eventing service.
    /// </summary>
    /// <remarks>
    /// <b>The half that matters here is not ours.</b> Publishing an item and
    /// reading it back is arithmetic; what cannot be arranged from one side is
    /// that the server <em>pushes</em> the change to somebody else - which it
    /// does only for a contact whose presence carries the right caps, and only
    /// once the two have a subscription. Both of those are the server's
    /// bookkeeping, and both are set up here for real rather than assumed.
    ///
    /// It is also the first lane in this suite that needs the two accounts to
    /// know each other. Everything before it - rooms, archives, uploads - works
    /// between strangers.
    /// </remarks>
    public abstract class AForeignPeerAvatarTests : AForeignPeerTests
    {

        #region Helper functions

        /// <summary>
        /// Two accounts that are subscribed to each other's presence.
        /// </summary>
        /// <remarks>
        /// <b>Not decoration for this lane, but its precondition.</b> A server
        /// pushes a PEP node to a contact, and before the handshake there is no
        /// contact. Done in full - ask, accept, both directions - because a
        /// one-sided subscription pushes one way only, and a round that happened
        /// to test the working direction would say nothing about the other.
        /// </remarks>
        protected async Task<(XMPPClient Alice, XMPPClient Bob)> TwoContactsAsync()
        {

            var alice = await ConnectAsync();
            var bob   = await ConnectAsync(User2);

            var aliceSaw = new ConcurrentQueue<JID>();
            var bobSaw   = new ConcurrentQueue<JID>();

            alice.OnSubscriptionRequest += (t, s, jid, status, ct) => { aliceSaw.Enqueue(jid); return Task.CompletedTask; };
            bob.  OnSubscriptionRequest += (t, s, jid, status, ct) => { bobSaw.  Enqueue(jid); return Task.CompletedTask; };

            // Only if they are not already subscribed, and that condition is not
            // tidiness: these are real accounts on a real server and their rosters
            // outlive the test, the suite and the machine. Asking again produces no
            // second request - the server has nothing new to tell - so a helper that
            // waited for one passed exactly once, on the first run after the accounts
            // were created, and timed out ever after. Measured: the first round to
            // run was green and the two behind it hung.
            await SubscribeAsync(alice, bob, bobSaw);
            await SubscribeAsync(bob, alice, aliceSaw);

            // Presence, because that is what carries the caps a server decides
            // the pushing by. Without it the subscription exists and nothing is
            // ever delivered - which looks exactly like a server that does not
            // do PEP.
            await alice.SetPresenceAsync();
            await bob.  SetPresenceAsync();

            return (alice, bob);

        }

        /// <summary>
        /// Makes sure one account may see the other's presence.
        /// </summary>
        /// <remarks>
        /// Idempotent, because the roster on the far side is not ours to reset.
        /// A subscription that is already there needs nothing; one that is not
        /// goes through the whole handshake, request and acceptance.
        /// </remarks>
        private async Task SubscribeAsync(XMPPClient                Asking,
                                          XMPPClient                Asked,
                                          ConcurrentQueue<JID>      SeenByTheAsked)
        {

            var already = Asking.GetContacts().FirstOrDefault(c => c.Jid.Bare == Asked.BareJid.Bare);

            if (already is not null &&
                already.Subscription is SubscriptionState.To or SubscriptionState.Both)
            {
                return;
            }

            await Asking.AddContactAsync(Asked.BareJid);

            await WaitFor(() => !SeenByTheAsked.IsEmpty,
                          $"the subscription request reaching {Asked.BareJid}");

            await Asked.AcceptSubscriptionAsync(Asking.BareJid);

            await WaitFor(() => Asking.GetContacts().Any(c => c.Jid.Bare == Asked.BareJid.Bare &&
                                                              c.Subscription is SubscriptionState.To or
                                                                                SubscriptionState.Both),
                          $"the subscription of {Asking.BareJid} to {Asked.BareJid}");

        }

        private static Byte[] APicture(Int32 size = 512)
            => RandomNumberGenerator.GetBytes(size);

        #endregion


        #region 1. A picture published can be fetched by somebody else

        /// <summary>
        /// The data node, read by a second account.
        /// </summary>
        /// <remarks>
        /// <b>Not arrangeable from one side</b>, and the reason is the access
        /// model: a PEP node is created on first publish with whatever default
        /// the server has, and if that default is not open the picture is
        /// published to nobody. That is the server's decision and this is where
        /// it becomes visible.
        ///
        /// The bytes are compared exactly, and the id is checked against them -
        /// a service that stored the right number of the wrong bytes would pass
        /// anything that only counted.
        /// </remarks>
        [Test]
        public async Task APicturePublishedCanBeFetchedBySomebodyElse()
        {

            var (alice, bob) = await TwoContactsAsync();

            var picture = APicture();

            var published = await alice.PublishAvatarAsync(picture, "image/png", 32, 32);

            Assert.That(published, Is.Not.Null,
                        $"{PeerName} did not take the picture, so nothing here can be published at all.");

            var infos = await bob.FetchAvatarInfoAsync(alice.BareJid);

            Assert.That(infos, Is.Not.Null,
                        $"{PeerName} refused the metadata node, or answered nothing at all.");

            Assert.That(infos!, Is.Not.Empty,
                        "The other side is told there is no picture, right after one was published.");

            Assert.Multiple(() =>
            {
                Assert.That(infos![0].Id,     Is.EqualTo(published!.Id));
                Assert.That(infos![0].Bytes,  Is.EqualTo(picture.Length));
                Assert.That(infos![0].Type,   Is.EqualTo("image/png"));
                Assert.That(infos![0].Width,  Is.EqualTo(32));
            });

            var avatar = await bob.FetchAvatarAsync(alice.BareJid, infos![0]);

            Assert.That(avatar, Is.Not.Null,
                        "The picture itself could not be fetched - or the bytes did not hash to the " +
                        "id they were fetched under.");

            Assert.That(avatar!.Data, Is.EqualTo(picture),
                        "What came back is not the picture that was published.");

        }

        #endregion

        #region 2. Everybody subscribed is told

        /// <summary>
        /// The push, which is what personal eventing is for.
        /// </summary>
        /// <remarks>
        /// <b>The round this lane exists for, and the one no fixture can
        /// fake.</b> Nothing here asks for the change: Alice publishes, and the
        /// server works out from Bob's caps and the subscription between them
        /// that he wants to know. Three of its parts are the server's
        /// bookkeeping and not ours - the roster subscription, the caps, and the
        /// decision to deliver - and a client that got the caps wrong would see
        /// silence, not an error.
        ///
        /// What arrives is the <em>metadata</em>, a few bytes. That is the whole
        /// design: a person with three hundred contacts and a new photograph
        /// sends three hundred short notices, and only those who do not already
        /// have that id ask for the picture.
        /// </remarks>
        [Test]
        public async Task EverybodySubscribedIsTold()
        {

            var (alice, bob) = await TwoContactsAsync();

            var heard = new ConcurrentQueue<(JID Who, IReadOnlyList<AvatarInfo> What)>();

            bob.Connection.OnAvatarChanged += (t, s, jid, infos, ct) =>
            {
                heard.Enqueue((jid, infos));
                return Task.CompletedTask;
            };

            var picture   = APicture();
            var published = await alice.PublishAvatarAsync(picture, "image/png");

            Assert.That(published, Is.Not.Null, "Nothing was published.");

            // Waited for by *id*, and that is not fussiness. A server sends the
            // last item of a PEP node when presence is exchanged, so the first
            // thing that arrives here is whatever Alice published in an earlier
            // round - these are real accounts and their nodes outlive the test.
            // A round that took the first announcement with anything in it was
            // green on the first run and wrong ever after, which is worse than
            // red: measured, it reported the previous picture as this one.
            await WaitFor(() => heard.Any(h => h.What.Any(i => i.Id == published!.Id)),
                          "the announcement reaching a contact without being asked for");

            var (who, what) = heard.First(h => h.What.Any(i => i.Id == published!.Id));

            Assert.Multiple(() =>
            {
                Assert.That(who,        Is.EqualTo(alice.BareJid));
                Assert.That(what[0].Id, Is.EqualTo(published!.Id));
            });

            // And the picture is still only a fetch away - the push carried the
            // notice, not the bytes.
            var avatar = await bob.FetchAvatarAsync(alice.BareJid, what[0]);

            Assert.That(avatar?.Data, Is.EqualTo(picture));

        }

        #endregion

        #region 3. Taking the face down arrives as such

        /// <summary>
        /// An empty <c>&lt;metadata/&gt;</c> reaches the other side as an empty
        /// list.
        /// </summary>
        /// <remarks>
        /// <b>Publishing nothing is not publishing an absence.</b> A node left
        /// alone goes on announcing the old picture to everybody who subscribes
        /// later, so a removal has to travel as something - and it has to arrive
        /// distinguishable from a stanza that could not be read. One means take
        /// the face down; the other means leave what is there alone.
        /// </remarks>
        [Test]
        public async Task TakingTheFaceDownArrivesAsSuch()
        {

            var (alice, bob) = await TwoContactsAsync();

            Assert.That(await alice.PublishAvatarAsync(APicture(), "image/png"), Is.Not.Null);

            var heard = new ConcurrentQueue<IReadOnlyList<AvatarInfo>>();

            bob.Connection.OnAvatarChanged += (t, s, jid, infos, ct) =>
            {
                heard.Enqueue(infos);
                return Task.CompletedTask;
            };

            Assert.That(await alice.RemoveAvatarAsync(), Is.True,
                        $"{PeerName} refused the empty metadata item, so a picture cannot be taken " +
                        "down here at all.");

            await WaitFor(() => heard.Any(infos => infos.Count == 0),
                          "the removal reaching a contact");

            // And asking outright says the same thing - an empty list, which is
            // an answer, and not null, which would mean the question failed.
            var infos = await bob.FetchAvatarInfoAsync(alice.BareJid);

            Assert.That(infos, Is.Not.Null,
                        "After a removal the metadata node cannot be read at all.");

            Assert.That(infos!, Is.Empty,
                        "The picture was taken down and the other side is still told there is one.");

        }

        #endregion

        #region 4. A stranger is not told, and the same publish tells a contact

        /// <summary>
        /// XEP-0163: who a PEP node is pushed to, and who it is not.
        /// </summary>
        /// <remarks>
        /// <b>The other half of round 2, and until D131 there was no way to ask
        /// for it.</b> That round shows that a contact is told without asking.
        /// This one shows that being told is not the default state of the world
        /// - which is the half that matters for a picture of somebody's face.
        ///
        /// <b>One publish, two watchers, one difference.</b> Carol connects the
        /// same way Bob does, sends the same presence carrying the same caps,
        /// and differs from him in exactly one thing: nobody has ever subscribed
        /// her to Alice. So a silence here cannot be the run being too quick or
        /// the caps being wrong - the same stanza reached Bob while it was
        /// being waited for.
        ///
        /// That is why the two are watched together and not in two rounds. A
        /// round that only waited for Carol to hear nothing would pass just as
        /// happily against a server that had published nothing at all, which is
        /// the failure D101 named: a run that measured nothing must not look
        /// like one that measured everything.
        ///
        /// And then Carol asks outright, because not being pushed a thing is not
        /// the same as not being able to fetch it. XEP-0163 section 4.3 has the
        /// default access model at <c>presence</c>, so somebody with no
        /// subscription should get nothing - and what a service actually does
        /// with that request is its own decision about somebody's face.
        /// </remarks>
        [Test]
        public async Task AStrangerIsNotToldAndTheSamePublishTellsAContact()
        {

            var (alice, bob) = await TwoContactsAsync();

            var carol = await ConnectAsync(User3);
            await carol.SetPresenceAsync();

            var toldBob    = new ConcurrentQueue<AvatarInfo>();
            var toldCarol  = new ConcurrentQueue<AvatarInfo>();

            bob.  Connection.OnAvatarChanged += (t, s, jid, infos, ct) =>
            {
                if (jid.Bare == alice.BareJid.Bare)
                    foreach (var info in infos)
                        toldBob.Enqueue(info);
                return Task.CompletedTask;
            };

            carol.Connection.OnAvatarChanged += (t, s, jid, infos, ct) =>
            {
                if (jid.Bare == alice.BareJid.Bare)
                    foreach (var info in infos)
                        toldCarol.Enqueue(info);
                return Task.CompletedTask;
            };

            var picture   = APicture();
            var published = await alice.PublishAvatarAsync(picture, "image/png");

            Assert.That(published, Is.Not.Null, "Nothing was published.");

            // By id, for the reason round 2 gives: these nodes outlive the test,
            // and a server hands out the last item on presence.
            await WaitFor(() => toldBob.Any(info => info.Id == published!.Id),
                          "the announcement reaching the contact");

            Assert.That(toldCarol.Any(info => info.Id == published!.Id), Is.False,
                        $"{PeerName} announced the picture to somebody who is not on the " +
                        "publisher's roster. The same stanza had already arrived at the " +
                        "contact, so this is the service deciding to tell a stranger and not " +
                        "the round being too quick.");

            // Asking outright is a different question from being told, and the
            // answer is not the one the specification leads one to expect.
            //
            // XEP-0163 section 5 says a PEP service MUST support the presence
            // access model and set it as the default, and under that model
            // (XEP-0060, section 4.5) only somebody with a subscription of
            // from or both may retrieve items. Carol has neither. Both services
            // hand her the metadata anyway.
            //
            // So it is pinned as what it is: a measured fact about both far
            // sides and not a rule of ours - and pinned rather than merely
            // printed, because a finding that only prints is one nobody reads.
            // Whoever finds this round red has found a service that became
            // stricter, which is the behaviour the section asks for; the right
            // answer then is to turn the round round.
            //
            // What could not be told apart from here: whether the node was
            // created with an open model long ago and kept it, or whether the
            // model is not consulted on retrieval at all. Neither service would
            // answer a configuration query for its own PEP node.
            var asked = await carol.FetchAvatarInfoAsync(alice.BareJid);

            Assert.That(asked?.Any(info => info.Id == published!.Id), Is.True,
                        $"{PeerName} refused a stranger the metadata node. That is what " +
                        "XEP-0163 section 5 asks for and is not what it did when this round " +
                        "was written - so check it is a refusal and not a fault here, and " +
                        "then turn this round round.");

        }

        #endregion

    }

}
