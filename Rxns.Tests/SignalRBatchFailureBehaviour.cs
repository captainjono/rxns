using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace Rxns.Tests
{
    /// <summary>
    /// What the publish bridge does with a batch the server refused.
    ///
    /// <para>Every failure used to be requeued unchanged. That is right for a dropped connection
    /// and unrecoverable for an oversized payload: the server closes the connection to report the
    /// limit, the client reconnects, drains the identical bytes, and is closed again. Nothing in
    /// the log says "stuck" - each step reads as an ordinary reconnect - so a fleet can sit in
    /// that loop indefinitely reporting no error at all, which is exactly what a hundred-instance
    /// run did while never registering a single worker.</para>
    ///
    /// <para>These pin the decision, not the transport: no hub, no socket, no cluster.</para>
    /// </summary>
    [TestClass]
    [TestCategory("Internal - SignalR transport")]
    public class SignalRBatchFailureBehaviour
    {
        /// <summary>The shape SignalR actually delivers - the limit is only in the message.</summary>
        private static Exception TooLarge() =>
            new HubException(
                "Connection closed with an error. InvalidDataException: The maximum message size of 32768B was exceeded. " +
                "The message size can be configured in AddHubOptions.");

        private static object[] Batch(int n)
        {
            var b = new object[n];
            for (var i = 0; i < n; i++) b[i] = "event-" + i;
            return b;
        }

        [TestMethod]
        public void A_dropped_connection_is_still_retried_unchanged()
        {
            // The common case, and the one the old behaviour got right. It must keep working.
            var plan = SignalRRxnManagerBridge.PlanBatchFailure(
                Batch(10), new IOException("The remote party closed the WebSocket"));

            plan.Should().Be(SignalRRxnManagerBridge.BatchFailure.Requeue);
        }

        [TestMethod]
        public void A_batch_refused_for_size_is_split_rather_than_replayed()
        {
            var plan = SignalRRxnManagerBridge.PlanBatchFailure(Batch(10), TooLarge());
            plan.Should().Be(SignalRRxnManagerBridge.BatchFailure.Split,
                "replaying the same bytes is what made the loop unbreakable");
        }

        [TestMethod]
        public void The_size_limit_is_recognised_when_it_arrives_wrapped()
        {
            // SignalR nests it: an AggregateException over a HubException whose text names the
            // limit. Only checking the outermost message finds nothing and the loop returns.
            var wrapped = new AggregateException("One or more errors occurred", TooLarge());

            SignalRRxnManagerBridge.LooksTooLarge(wrapped).Should().BeTrue();
            SignalRRxnManagerBridge.PlanBatchFailure(Batch(4), wrapped)
                .Should().Be(SignalRRxnManagerBridge.BatchFailure.Split);
        }

        [TestMethod]
        public void An_unrelated_failure_is_never_mistaken_for_a_size_problem()
        {
            // Splitting a batch that failed for some other reason would halve throughput on every
            // ordinary blip, and quietly drop single events that were perfectly deliverable.
            foreach (var ex in new Exception[]
                     {
                         new TimeoutException("The operation has timed out"),
                         new InvalidOperationException("The 'InvokeCoreAsync' method cannot be called if the connection is not active"),
                         new ObjectDisposedException("HubConnection"),
                         new HubException("An unexpected error occurred invoking 'PublishBatch' on the server")
                     })
            {
                SignalRRxnManagerBridge.LooksTooLarge(ex).Should().BeFalse(ex.GetType().Name + " is not a size failure");
                SignalRRxnManagerBridge.PlanBatchFailure(Batch(8), ex)
                    .Should().Be(SignalRRxnManagerBridge.BatchFailure.Requeue);
            }
        }

        [TestMethod]
        public void One_event_that_will_not_fit_is_dropped_rather_than_retried_forever()
        {
            // It cannot be delivered at any batch size. Keeping it means the queue never empties.
            SignalRRxnManagerBridge.PlanBatchFailure(Batch(1), TooLarge())
                .Should().Be(SignalRRxnManagerBridge.BatchFailure.Drop);

            SignalRRxnManagerBridge.PlanBatchFailure(new object[0], TooLarge())
                .Should().Be(SignalRRxnManagerBridge.BatchFailure.Drop);

            SignalRRxnManagerBridge.PlanBatchFailure(null, TooLarge())
                .Should().Be(SignalRRxnManagerBridge.BatchFailure.Drop);
        }

        [TestMethod]
        public void Splitting_keeps_every_event_and_their_order()
        {
            // A split that loses an item would turn a delivery problem into missing telemetry,
            // which is worse: the run completes and the numbers are quietly short.
            foreach (var n in new[] { 2, 3, 9, 64, 501 })
            {
                SignalRRxnManagerBridge.SplitBatch(Batch(n), out var a, out var b);

                a.Length.Should().BeGreaterThan(0, "an empty half would not shrink anything");
                b.Length.Should().BeGreaterThan(0);
                (a.Length + b.Length).Should().Be(n);

                var rejoined = new List<object>(a);
                rejoined.AddRange(b);
                // Joined rather than compared as collections: Equal's params object[] overload
                // swallows an object[] argument as a single expected element, even when it is
                // explicitly typed as IEnumerable<object>. Order matters here because events
                // carry causality between them.
                string.Join(",", rejoined).Should().Be(string.Join(",", Batch(n)),
                    "splitting must preserve every event and the order they happened in");
            }
        }

        [TestMethod]
        public void Repeated_refusal_terminates_instead_of_looping()
        {
            // The property that matters. Against a server refusing everything, the policy must
            // reach a stop - every batch either delivered or dropped - rather than reproducing
            // work forever. Simulated against the real decision function.
            var queue = new Queue<object[]>();
            queue.Enqueue(Batch(500));

            var dropped = 0;
            var rounds = 0;

            while (queue.Count > 0)
            {
                if (++rounds > 10_000) break;   // the old behaviour never leaves this loop

                var batch = queue.Dequeue();
                switch (SignalRRxnManagerBridge.PlanBatchFailure(batch, TooLarge()))
                {
                    case SignalRRxnManagerBridge.BatchFailure.Split:
                        SignalRRxnManagerBridge.SplitBatch(batch, out var a, out var b);
                        queue.Enqueue(a);
                        queue.Enqueue(b);
                        break;
                    case SignalRRxnManagerBridge.BatchFailure.Drop:
                        dropped += batch.Length;
                        break;
                    default:
                        queue.Enqueue(batch);   // would spin; only reachable on a non-size failure
                        break;
                }
            }

            queue.Should().BeEmpty("the policy has to reach a stop even when nothing can be sent");
            dropped.Should().Be(500, "every event is accounted for - delivered or reported dropped");
            rounds.Should().BeLessThan(2000, "halving bounds the work; this used to be unbounded");
        }

        [TestMethod]
        public void A_server_with_a_larger_limit_ends_the_splitting_early()
        {
            // The realistic case: the batch is too big, its halves are not. Two rounds, no drops.
            const int fits = 128;
            var queue = new Queue<object[]>();
            queue.Enqueue(Batch(500));

            var delivered = 0;
            var rounds = 0;

            while (queue.Count > 0 && ++rounds <= 10_000)
            {
                var batch = queue.Dequeue();
                if (batch.Length <= fits) { delivered += batch.Length; continue; }

                SignalRRxnManagerBridge.PlanBatchFailure(batch, TooLarge())
                    .Should().Be(SignalRRxnManagerBridge.BatchFailure.Split);
                SignalRRxnManagerBridge.SplitBatch(batch, out var a, out var b);
                queue.Enqueue(a);
                queue.Enqueue(b);
            }

            delivered.Should().Be(500, "nothing is lost when the halves fit");
            rounds.Should().BeLessThan(50);
        }
    }
}
