using System;

namespace Rxns.Hosting.Updates
{
    /// <summary>
    /// A storage backend that can say WHERE an update is, instead of handing over its bytes.
    ///
    /// <para><see cref="IUpdateStorageClient.GetUpdate"/> returns a Stream, so whoever implements
    /// it carries the payload. That is fine when one caller wants one copy and ruinous when a
    /// hundred want the same one: a host serving a fleet moves the bundle once per machine, and on
    /// a 100-device run that was 7.1GB of egress from two cores - while the same box was taking
    /// heartbeats and serving status. Every other cost measured there totalled under 50ms.</para>
    ///
    /// <para>Kept as a separate capability rather than added to IUpdateStorageClient, which has
    /// four implementations. A filesystem repo has no location a remote machine could use and an
    /// HTTP client's location is the host being relieved, so widening the contract would force
    /// three backends to answer a question only one of them can. A caller tests for this and falls
    /// back to the stream when it is absent, which is also what makes adopting it optional.</para>
    /// </summary>
    public interface ICanIssueUpdateLocations
    {
        /// <summary>
        /// A URL the caller can fetch the update from directly, valid for <paramref name="validFor"/>.
        ///
        /// <para>Time-limited on purpose: the URL usually carries its own authority, so it is a
        /// credential with a shape. The window has to be derived from how long the work it belongs
        /// to may take - a machine that boots late into a long run still needs a live URL when it
        /// finally asks, and a window sized to "should be plenty" is how that becomes a failure
        /// nobody can reproduce.</para>
        /// </summary>
        IObservable<Uri> GetUpdateLocation(string system, string version, TimeSpan validFor);
    }
}
